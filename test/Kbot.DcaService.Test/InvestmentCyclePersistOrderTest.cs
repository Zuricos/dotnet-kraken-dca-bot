using System.Text;
using System.Text.Json;
using Kbot.Common.Api;
using Kbot.Common.Enums;
using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Kbot.DcaService.Models;
using Kbot.DcaService.Options;
using Kbot.DcaService.Utility;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Kbot.DcaService.Test;

/// <summary>
/// The order in which a sent order is written down (C-4). The bookkeeping that follows a successful
/// order used to run before the state was persisted, so a throw in it unwound out of the host with
/// the pre-order <c>LastInvestmentTime</c> still on disk: Docker restarted the container, the state
/// file said no order had been placed, and the bot bought again.
///
/// <see cref="DcaStateHandler"/> writes to a path relative to the working directory, so these tests
/// run in a temporary one and are therefore not parallelised.
/// </summary>
[TestClass]
[DoNotParallelize]
public class InvestmentCyclePersistOrderTest
{
  private const string StateFile = "state/state.json";

  [TestMethod]
  public async Task InvestmentCycle_PersistsTheOrderEvenWhenTheBookkeepingThrows()
  {
    var transport = new StubKrakenTransport();
    var startedAt = DateTime.UtcNow;

    var persisted = await InTemporaryWorkingDirectory(async () =>
    {
      // Day 0 cannot be clamped into existence, so ComputeTimeUntilNextTopUp throws exactly where
      // the day-31-in-April bug used to. BalanceOptionsValidator rejects this value at startup; the
      // point here is what happens if a throw reaches that call site anyway.
      var worker = NewWorker(transport, topUpDayOfMonth: 0);
      worker.State = new DcaState
      {
        LastInvestmentTime = DateTime.UtcNow.AddDays(-1),
        // In the past, so the cycle recomputes the top-up time instead of reusing it.
        NextTopUpTime = DateTime.UtcNow.AddDays(-1),
        TimeUntilNextTopUp = TimeSpan.FromDays(5),
      };

      await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
        worker.InvestmentCycle(CancellationToken.None)
      );
      return ReadState();
    });

    Assert.IsTrue(transport.SawAddOrder, "The cycle should have placed an order before throwing.");
    Assert.IsNotNull(persisted, $"{StateFile} should have been written before the throw.");
    Assert.IsGreaterThanOrEqualTo(
      startedAt,
      persisted.LastInvestmentTime,
      "The persisted state must be the post-order one, not the state the cycle started from."
    );
  }

  [TestMethod]
  public async Task InvestmentCycle_LeavesTheStateAloneWhenNoOrderWasPlaced()
  {
    // The counter-test: a cycle that does not spend money must not move the watermark.
    var transport = new StubKrakenTransport { Ticker = TickerError };

    var persisted = await InTemporaryWorkingDirectory(async () =>
    {
      var worker = NewWorker(transport, topUpDayOfMonth: 26);
      await worker.InvestmentCycle(CancellationToken.None);
      return ReadState();
    });

    Assert.IsFalse(transport.SawAddOrder);
    Assert.IsNull(persisted, "A skipped cycle should not have written a state file.");
  }

  private static DcaState? ReadState() =>
    File.Exists(StateFile)
      ? JsonSerializer.Deserialize<DcaState>(File.ReadAllText(StateFile))
      : null;

  /// <summary>
  /// Runs <paramref name="body"/> with the process working directory pointed at a fresh temporary
  /// directory that has the <c>state</c> subdirectory the state handler expects, and restores it
  /// afterwards.
  /// </summary>
  private static async Task<T> InTemporaryWorkingDirectory<T>(Func<Task<T>> body)
  {
    var original = Directory.GetCurrentDirectory();
    var temp = Directory.CreateTempSubdirectory("kbot-state-");
    Directory.CreateDirectory(Path.Combine(temp.FullName, "state"));
    try
    {
      Directory.SetCurrentDirectory(temp.FullName);
      return await body();
    }
    finally
    {
      Directory.SetCurrentDirectory(original);
      temp.Delete(recursive: true);
    }
  }

  private const string BalanceOfThousandFrancs =
    """{"error":[],"result":{"CHF":"1000","XXBT":"0"}}""";
  private const string TickerError = """{"error":["EQuery:Unknown asset pair"]}""";
  private const string AddOrderAccepted =
    """{"error":[],"result":{"txid":["OAV6BB-Q2SHQ-XSCJPG"],"descr":{"order":"buy 0.00005 XBTCHF @ limit 50000.0"}}}""";

  // Whole numbers only: parsing the wire values still runs under the ambient culture, and pinning
  // that to invariant culture is P2-02.
  private static readonly string HealthyTicker = JsonSerializer.Serialize(
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

  private static DcaWorker NewWorker(StubKrakenTransport transport, int topUpDayOfMonth)
  {
    var cultureOptions = MsOptions.Create(
      new CultureOptions
      {
        CultureString = "de-CH",
        CountyCode = "CH-ZH",
        Fiat = "CHF",
      }
    );
    var api = new KrakenApi(
      NullLogger<KrakenApi>.Instance,
      MsOptions.Create(new Secrets { ApiKey = "test-api-key", ApiSecret = "dGVzdC1zZWNyZXQ=" }),
      transport
    );
    var holidayService = new HolidayService(NullLogger<HolidayService>.Instance, cultureOptions);
    holidayService.Holidays[DateTime.UtcNow.Year] = [];
    return new DcaWorker(
      NullLogger<DcaWorker>.Instance,
      new TimeComputeService(NullLogger<TimeComputeService>.Instance, holidayService),
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
      MsOptions.Create(
        new BalanceOptions { DefaultTopupDayOfMonth = topUpDayOfMonth, ReserveFiat = 100 }
      ),
      cultureOptions,
      MsOptions.Create(
        new WaitOptions
        {
          MinWaitTime = TimeSpan.FromSeconds(10),
          MaxWaitTime = TimeSpan.FromHours(1),
        }
      )
    )
    {
      State = new DcaState
      {
        LastInvestmentTime = DateTime.UtcNow.AddDays(-1),
        NextTopUpTime = DateTime.UtcNow.AddDays(5),
        TimeUntilNextTopUp = TimeSpan.FromDays(5),
      },
    };
  }

  /// <summary>
  /// Answers Kraken's balance, ticker and AddOrder endpoints from canned content and remembers
  /// whether an order was ever sent. A copy of the stub in
  /// <see cref="InvestmentCycleGuardTest"/>, kept local so the two files stay independent.
  /// </summary>
  private sealed class StubKrakenTransport : HttpMessageHandler
  {
    internal string Balance { get; init; } = BalanceOfThousandFrancs;
    internal string Ticker { get; init; } = HealthyTicker;
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
}
