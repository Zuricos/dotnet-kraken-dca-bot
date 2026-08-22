using Kbot.Common.Api;
using Kbot.Common.Dtos;
using Kbot.Common.Enums;
using Kbot.Common.Options;
using Kbot.DcaService.Models;
using Kbot.DcaService.Options;
using Kbot.DcaService.Utility;
using Microsoft.Extensions.Options;

namespace Kbot.DcaService;

public class DcaWorker(
  ILogger<DcaWorker> logger,
  TimeComputeService computeService,
  KrakenClient krakenClient,
  IOptions<OrderOptions> orderOptions,
  IOptions<BalanceOptions> balanceOptions,
  IOptions<CultureOptions> cultureOptions,
  IOptions<WaitOptions> waitOptions
) : BackgroundService
{
  /// <summary>
  /// Test seam: <see cref="InvestmentCycle"/> is exercised directly with a stubbed transport, which
  /// needs the state seeded without going through <see cref="ExecuteAsync"/>. Extracting the
  /// scheduling core into a pure component is P3-02.
  /// </summary>
  internal DcaState State { get; set; } = null!;

  // Options accessors for convenience
  private string CryptoPair => orderOptions.Value.CryptoPair;
  private string FiatCode => cultureOptions.Value.Fiat;
  private double ReserveFiat => balanceOptions.Value.ReserveFiat;
  private double AskMultiplier => orderOptions.Value.AskMultiplier;
  private double InclusiveFeeMultiplier => orderOptions.Value.InklusiveFeeMultiplier;
  private double MinOrderVolume => orderOptions.Value.MinOrderVolume;

  public string? FixOrderId { get; set; }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    logger.LogInformation("DCA Worker running at: {time}", DateTime.UtcNow);
    // Stated explicitly because OrderType.Market is the enum default: an omitted OrderOptions__Type
    // selects it, and a market order ignores the price this worker computes.
    logger.LogInformation(
      "Effective order type is {OrderType} for {CryptoPair}, volume {MinOrderVolume} at {AskMultiplier}x ask.",
      orderOptions.Value.Type,
      CryptoPair,
      MinOrderVolume,
      AskMultiplier
    );
    if (orderOptions.Value.Type == OrderType.Market)
    {
      logger.LogWarning(
        "Order type is {OrderType}: orders execute at whatever the book offers, not at the computed price.",
        OrderType.Market
      );
    }

    State = DcaStateHandler.Load();
    var nextTopUpTime = computeService.ComputeNextTopUpTime(
      DateTime.UtcNow,
      balanceOptions.Value.DefaultTopupDayOfMonth
    );
    State = State with { NextTopUpTime = nextTopUpTime };
    // A freshly loaded state carries TimeSpan.Zero, and the interval computation refuses to
    // schedule on a non-positive top-up window, so seed it before the first cycle.
    State = computeService.ComputeTimeUntilNextTopUp(
      State,
      balanceOptions.Value.DefaultTopupDayOfMonth
    );

    while (!stoppingToken.IsCancellationRequested)
    {
      var waitTime = await InvestmentCycle(stoppingToken);

      State.Save();

      waitTime =
        waitTime > waitOptions.Value.MaxWaitTime ? waitOptions.Value.MaxWaitTime : waitTime;
      waitTime =
        waitTime < waitOptions.Value.MinWaitTime ? waitOptions.Value.MinWaitTime : waitTime;

      logger.LogInformation("Waiting for {waitTime}", waitTime);
      await Task.Delay(waitTime, stoppingToken);
    }
  }

  internal async Task<TimeSpan> InvestmentCycle(CancellationToken stoppingToken)
  {
    var balance = await krakenClient.CheckBalance();
    if (!balance.TryGetValue(FiatCode, out var fiatBalance))
    {
      logger.LogError(
        "Fiat asset {Fiat} is not in the Kraken balance (keys: {Keys}); skipping this cycle.",
        FiatCode,
        string.Join(", ", balance.Keys)
      );
      return waitOptions.Value.MaxWaitTime;
    }
    var balanceFiat = fiatBalance - ReserveFiat;

    var currentCryptoPrice = await krakenClient.GetCurrentCryptoPrice(CryptoPair);
    if (currentCryptoPrice <= 0)
    {
      logger.LogError(
        "Ticker for {CryptoPair} is unavailable (price {CurrentCryptoPrice}); skipping this cycle.",
        CryptoPair,
        currentCryptoPrice
      );
      return waitOptions.Value.MaxWaitTime;
    }
    var askPrice = Math.Round(currentCryptoPrice * AskMultiplier, 1);
    var costForVolume =
      Math.Ceiling(askPrice * MinOrderVolume * InclusiveFeeMultiplier * 100) / 100;

    if (balanceFiat < costForVolume)
    {
      logger.LogWarning(
        "Not enough balance to buy {CryptoPair}. Balance: {balanceFiat}, Cost: {costPerMinOrderVolume}",
        CryptoPair,
        balanceFiat,
        costForVolume
      );
      return TimeSpan.MaxValue;
    }

    var investmentInterval = computeService.ComputeNextInvestmentInterval(
      balanceFiat,
      costForVolume,
      State.TimeUntilNextTopUp
    );
    var nextOrderTime = AddSaturating(State.LastInvestmentTime, investmentInterval);

    if (DateTime.UtcNow < nextOrderTime)
    {
      logger.LogInformation($"Next order probably at: {nextOrderTime:dd.MM.yyyy HH:mm:ss} UTC.");
      return (nextOrderTime - DateTime.UtcNow) / 2;
    }
    logger.LogInformation(
      "Starting to execute buy order for {CryptoPair} at {currentCryptoPrice} {FiatCode}",
      CryptoPair,
      currentCryptoPrice,
      FiatCode
    );
    var isSuccess = await SendOrder(askPrice);
    if (isSuccess)
    {
      State = State with { LastInvestmentTime = DateTime.UtcNow };
      // Durable before anything else can throw: the money is already spent, so a crash between here
      // and the loop's Save() must not reload a pre-order LastInvestmentTime and buy again (C-4).
      // The Save() in ExecuteAsync stays and is idempotent. P4-01 makes the write atomic; P4-09
      // closes the remaining send-to-persist crash window (M-6).
      State.Save();
      State = computeService.ComputeTimeUntilNextTopUp(
        State,
        balanceOptions.Value.DefaultTopupDayOfMonth
      );
    }
    return (nextOrderTime - DateTime.UtcNow) / 2;
  }

  /// <summary>
  /// Adds an interval to an instant without ever overflowing: the interval computation returns
  /// <see cref="TimeSpan.MaxValue"/> when there is nothing to schedule, and plain
  /// <see cref="DateTime"/> addition throws on that.
  /// </summary>
  private static DateTime AddSaturating(DateTime instant, TimeSpan interval) =>
    interval >= DateTime.MaxValue - instant ? DateTime.MaxValue : instant + interval;

  private async Task<bool> SendOrder(double btcPrice)
  {
    var volume = orderOptions.Value.MinOrderVolume;
    var orderType = orderOptions.Value.Type.ToString().ToLower();
    var pair = orderOptions.Value.CryptoPair;

    var utcNow = DateTime.UtcNow;
    var cl_ord_id = FixOrderId ?? $"dca-{orderType.First()}{utcNow:yyMMddHHmm}";

    var orderRequest = new OrderRequest
    {
      OrderType = orderOptions.Value.Type,
      Type = BuyOrSell.Buy,
      Pair = pair,
      Volume = volume,
      Price = btcPrice,
      OrderId = cl_ord_id,
    };
    logger.LogInformation("Sending order: {OrderData}", orderRequest);
    return await krakenClient.SendOrder(orderRequest);
  }
}
