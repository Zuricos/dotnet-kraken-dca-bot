using Kbot.Common.Api;
using Kbot.Common.Dtos;
using Kbot.Common.Enums;
using Kbot.Common.Helpers;
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
    IOptions<WaitOptions> waitOptions) : BackgroundService
{
    private DcaState State { get; set; } = null!;

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

        State = DcaState.Load();
        var nextTopUpTime = computeService.ComputeNextTopUpTime(DateTime.UtcNow, balanceOptions.Value.DefaultTopupDayOfMonth);
        State = State with { NextTopUpTime = nextTopUpTime };

        while (!stoppingToken.IsCancellationRequested)
        {
            var waitTime = await InvestmentCycle(stoppingToken);

            State.Save();

            waitTime = waitTime > waitOptions.Value.MaxWaitTime ? waitOptions.Value.MaxWaitTime : waitTime;
            waitTime = waitTime < waitOptions.Value.MinWaitTime ? waitOptions.Value.MinWaitTime : waitTime;

            logger.LogInformation("Waiting for {waitTime}", waitTime);
            await Task.Delay(waitTime, stoppingToken);
        }
    }

    private async Task<TimeSpan> InvestmentCycle(CancellationToken stoppingToken)
    {
        var balance = await krakenClient.CheckBalance();
        var balanceFiat = balance[FiatCode] - ReserveFiat;

        var currentCryptoPrice = await krakenClient.GetCurrentCryptoPrice(CryptoPair);
        var askPrice = Math.Round(currentCryptoPrice * AskMultiplier, 1);
        var costForVolume = Math.Ceiling(askPrice * MinOrderVolume * InclusiveFeeMultiplier * 100) / 100;

        if (balanceFiat < costForVolume)
        {
            logger.LogWarning("Not enough balance to buy {CryptoPair}. Balance: {balanceFiat}, Cost: {costPerMinOrderVolume}", CryptoPair, balanceFiat, costForVolume);
            return TimeSpan.MaxValue;
        }

        var investmentInterval = computeService.ComputeNextInvestmentInterval(balanceFiat, costForVolume, State.TimeUntilNextTopUp);
        var nextOrderTime = State.LastInvestmentTime + investmentInterval;

        if (DateTime.UtcNow < nextOrderTime)
        {
            logger.LogInformation($"Next order probably at: {nextOrderTime:dd.MM.yyyy HH:mm:ss} UTC.");
            return (nextOrderTime - DateTime.UtcNow) / 2;
        }
        logger.LogInformation("Starting to execute buy order for {CryptoPair} at {currentCryptoPrice} {FiatCode}", CryptoPair, currentCryptoPrice, FiatCode);
        var isSuccess = await SendOrder(askPrice);
        if (isSuccess)
        {
            State = State with { LastInvestmentTime = DateTime.UtcNow };
            State = computeService.ComputeTimeUntilNextTopUp(State, balanceOptions.Value.DefaultTopupDayOfMonth);
        }
        return (nextOrderTime - DateTime.UtcNow) / 2;
    }

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
            OrderId = cl_ord_id
        };
        logger.LogInformation("Sending order: {OrderData}", orderRequest);
        return await krakenClient.SendOrder(orderRequest);
    }

}