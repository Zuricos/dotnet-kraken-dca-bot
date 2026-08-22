using System.Text;
using System.Text.Json;
using Kbot.Common.Api;
using Kbot.Common.Enums;
using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Kbot.DcaService.Models;
using Kbot.DcaService.Options;
using Kbot.DcaService.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Kbot.DcaService.Test;

/// <summary>
/// One investment cycle against a stubbed transport, covering the two sentinel call sites the worker
/// used to consume unchecked: an empty balance dictionary (C-3) and a price of 0.0 (C-2). No
/// network, no credentials, no order.
/// </summary>
[TestClass]
public class InvestmentCycleGuardTest
{
  private static readonly TimeSpan MaxWaitTime = TimeSpan.FromHours(1);

  /// <summary>
  /// A successful cycle persists the state before it does any further bookkeeping (C-4), and
  /// <see cref="DcaStateHandler"/> writes to a path relative to the working directory. The
  /// dedicated ordering test in <see cref="InvestmentCyclePersistOrderTest"/> uses a temporary one;
  /// here the test output directory is enough.
  /// </summary>
  [ClassInitialize]
  public static void EnsureStateDirectory(TestContext testContext) =>
    Directory.CreateDirectory("state");

  private const string BalanceOfThousandFrancs =
    """{"error":[],"result":{"CHF":"1000","XXBT":"0"}}""";
  private const string BalanceWithoutFrancs = """{"error":[],"result":{"ZUSD":"1000"}}""";
  private const string EmptyBalance = """{"error":[],"result":{}}""";
  private const string TickerError = """{"error":["EQuery:Unknown asset pair"]}""";
  private const string AddOrderAccepted =
    """{"error":[],"result":{"txid":["OAV6BB-Q2SHQ-XSCJPG"],"descr":{"order":"buy 0.00005 XBTCHF @ limit 50000.0"}}}""";

  // Whole numbers only: parsing the wire values still runs under the ambient culture, and pinning
  // that to invariant culture is P2-02.
  private static readonly string Ticker = JsonSerializer.Serialize(
    new
    {
      error = Array.Empty<string>(),
      result = new Dictionary<string, object>
      {
        ["XBTCHF"] = new
        {
          a = new[] { "50000", "1", "1" },
          b = new[] { "49990", "2", "2" },
          c = new[] { "49995", "1" },
          v = new[] { "10", "20" },
          p = new[] { "50000", "50050" },
          t = new[] { 100, 200 },
          l = new[] { "49000", "48000" },
          h = new[] { "51000", "52000" },
          o = "50000",
        },
      },
    }
  );

  [TestMethod]
  public async Task InvestmentCycle_SkipsTheCycleWhenTheBalanceCallFailed()
  {
    var transport = new StubKrakenTransport { Balance = EmptyBalance, Ticker = Ticker };

    var (worker, log) = NewWorker(transport);
    var waitTime = await worker.InvestmentCycle(CancellationToken.None);

    Assert.AreEqual(MaxWaitTime, waitTime, "A failed balance call must cost one skipped cycle.");
    Assert.IsFalse(transport.SawAddOrder, "No order may be constructed from an unknown balance.");
    Assert.IsTrue(
      log.Errors.Any(m => m.Contains("not in the Kraken balance")),
      $"Expected a logged error about the missing fiat asset, got: {string.Join(" | ", log.Errors)}"
    );
  }

  [TestMethod]
  public async Task InvestmentCycle_SkipsTheCycleWhenTheFiatCodeIsNotKrakensAssetName()
  {
    // CultureOptions.Fiat is CHF while the account is funded in ZUSD: indexing the dictionary threw
    // KeyNotFoundException out of the worker and stopped the host.
    var transport = new StubKrakenTransport { Balance = BalanceWithoutFrancs, Ticker = Ticker };

    var (worker, log) = NewWorker(transport);
    var waitTime = await worker.InvestmentCycle(CancellationToken.None);

    Assert.AreEqual(MaxWaitTime, waitTime);
    Assert.IsFalse(transport.SawAddOrder);
    Assert.IsTrue(log.Errors.Any(m => m.Contains("ZUSD")), "The available keys should be logged.");
  }

  [TestMethod]
  public async Task InvestmentCycle_SkipsTheCycleWhenTheTickerIsUnavailable()
  {
    var transport = new StubKrakenTransport
    {
      Balance = BalanceOfThousandFrancs,
      Ticker = TickerError,
    };

    var (worker, log) = NewWorker(transport);
    var waitTime = await worker.InvestmentCycle(CancellationToken.None);

    Assert.AreEqual(MaxWaitTime, waitTime, "A price of 0 must not start an investment.");
    Assert.IsFalse(transport.SawAddOrder, "No order may be constructed from a price of 0.");
    Assert.IsTrue(
      log.Errors.Any(m => m.Contains("is unavailable")),
      $"Expected a logged error about the ticker, got: {string.Join(" | ", log.Errors)}"
    );
  }

  [TestMethod]
  public async Task InvestmentCycle_StillOrdersOnAHealthyResponse()
  {
    // The counter-test: the guards above skip the cycle because the data is bad, not because a
    // stubbed cycle can never place an order.
    var transport = new StubKrakenTransport
    {
      Balance = BalanceOfThousandFrancs,
      Ticker = Ticker,
      AddOrder = AddOrderAccepted,
    };

    var (worker, _) = NewWorker(transport);
    await worker.InvestmentCycle(CancellationToken.None);

    Assert.IsTrue(transport.SawAddOrder, "A healthy cycle should have sent an order.");
  }

  [TestMethod]
  public async Task InvestmentCycle_DoesNotOrderWithoutATopUpWindow()
  {
    // TimeUntilNextTopUp is TimeSpan.Zero in a freshly written state file. The interval used to come
    // out as TimeSpan.Zero here, which put the next order time in the past on every cycle.
    var transport = new StubKrakenTransport
    {
      Balance = BalanceOfThousandFrancs,
      Ticker = Ticker,
      AddOrder = AddOrderAccepted,
    };

    var (worker, _) = NewWorker(transport);
    worker.State = worker.State with { TimeUntilNextTopUp = TimeSpan.Zero };
    var waitTime = await worker.InvestmentCycle(CancellationToken.None);

    Assert.IsFalse(transport.SawAddOrder, "An unschedulable cycle must not place an order.");
    Assert.IsGreaterThan(
      MaxWaitTime,
      waitTime,
      "The wait must be long enough for the caller's clamp to make it MaxWaitTime."
    );
  }

  private static (DcaWorker Worker, RecordingLogger Log) NewWorker(StubKrakenTransport transport)
  {
    var cultureOptions = MsOptions.Create(
      new CultureOptions
      {
        CultureString = "de-CH",
        CountyCode = "CH-ZH",
        Fiat = "CHF",
      }
    );
    var log = new RecordingLogger();
    var api = new KrakenApi(
      NullLogger<KrakenApi>.Instance,
      MsOptions.Create(new Secrets { ApiKey = "test-api-key", ApiSecret = "dGVzdC1zZWNyZXQ=" }),
      transport
    );
    var worker = new DcaWorker(
      log,
      new TimeComputeService(
        NullLogger<TimeComputeService>.Instance,
        new HolidayService(NullLogger<HolidayService>.Instance, cultureOptions)
      ),
      new KrakenClient(NullLogger<KrakenClient>.Instance, api),
      MsOptions.Create(
        new OrderOptions
        {
          Type = OrderType.Limit,
          Fee = 0.4,
          MinOrderVolume = 0.00005,
          AskMultiplier = 1.0,
          CryptoPair = "XBTCHF",
        }
      ),
      MsOptions.Create(new BalanceOptions { DefaultTopupDayOfMonth = 26, ReserveFiat = 100 }),
      cultureOptions,
      MsOptions.Create(
        new WaitOptions { MinWaitTime = TimeSpan.FromSeconds(10), MaxWaitTime = MaxWaitTime }
      )
    )
    {
      // What ExecuteAsync would have loaded: due for an order, with a top-up window to spread over.
      State = new DcaState
      {
        LastInvestmentTime = DateTime.UtcNow.AddDays(-1),
        NextTopUpTime = DateTime.UtcNow.AddDays(5),
        TimeUntilNextTopUp = TimeSpan.FromDays(5),
      },
    };
    return (worker, log);
  }

  /// <summary>
  /// Answers Kraken's balance, ticker and AddOrder endpoints from canned content and remembers
  /// whether an order was ever sent.
  /// </summary>
  private sealed class StubKrakenTransport : HttpMessageHandler
  {
    internal string Balance { get; init; } = EmptyBalance;
    internal string Ticker { get; init; } = TickerError;
    internal string AddOrder { get; init; } = AddOrderAccepted;
    internal bool SawAddOrder { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      var content = path switch
      {
        "/0/private/Balance" => Balance,
        "/0/public/Ticker" => Ticker,
        "/0/private/AddOrder" => Order(),
        _ => throw new InvalidOperationException($"Unexpected request to {path}."),
      };
      return Task.FromResult(
        new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
          Content = new StringContent(content, Encoding.UTF8, "application/json"),
        }
      );
    }

    private string Order()
    {
      SawAddOrder = true;
      return AddOrder;
    }
  }

  /// <summary>
  /// Keeps the messages the worker logged, so a skipped cycle can be told from a silent one.
  /// </summary>
  private sealed class RecordingLogger : ILogger<DcaWorker>
  {
    internal List<string> Errors { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
      where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel,
      EventId eventId,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter
    )
    {
      if (logLevel >= LogLevel.Error)
      {
        Errors.Add(formatter(state, exception));
      }
    }
  }
}
