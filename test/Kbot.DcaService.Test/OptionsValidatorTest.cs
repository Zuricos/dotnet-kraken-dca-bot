using Kbot.Common.Enums;
using Kbot.DcaService.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kbot.DcaService.Test;

/// <summary>
/// H-10: every numeric rule in these validators used to be <c>&gt;= 0</c>, and zero is exactly what
/// an omitted environment variable binds to. It survived a code review and a release because there
/// was no validator coverage at all — hence one test per rule here, each asserting that the
/// degenerate value is rejected, that the failure message names the option (the operator only ever
/// sees that string), and that a sane value still starts up.
///
/// <see cref="BalanceOptions.DefaultTopupDayOfMonth"/> is deliberately absent: P1-03 owns that rule
/// and covers it in <see cref="TopUpDayClampTest"/>.
/// </summary>
[TestClass]
public class OptionsValidatorTest
{
  private static readonly WaitOptions SaneWait = new()
  {
    MinWaitTime = TimeSpan.FromSeconds(30),
    MaxWaitTime = TimeSpan.FromHours(1),
  };

  private static readonly OrderOptions SaneOrder = new()
  {
    Type = OrderType.Limit,
    Fee = 0.4,
    MinOrderVolume = 0.00005,
    AskMultiplier = 1.00001,
    CryptoPair = "XBTCHF",
  };

  // ---------------------------------------------------------------- WaitOptions

  /// <summary>
  /// The finding itself: <c>00:00:00</c> passed all three of the old rules, which made
  /// <c>Task.Delay</c> a no-op and the trading loop a busy loop against a rate-limited API.
  /// </summary>
  [TestMethod]
  public void WaitOptions_RejectsTheAllZeroConfigurationThatUsedToPass()
  {
    AssertWaitFails(
      new WaitOptions { MinWaitTime = TimeSpan.Zero, MaxWaitTime = TimeSpan.Zero },
      "MinWaitTime"
    );
  }

  [TestMethod]
  public void WaitOptions_RejectsANegativeOrSubSecondMinWaitTime()
  {
    AssertWaitFails(SaneWait with { MinWaitTime = TimeSpan.FromSeconds(-1) }, "MinWaitTime");
    AssertWaitFails(SaneWait with { MinWaitTime = TimeSpan.FromMilliseconds(500) }, "MinWaitTime");
  }

  [TestMethod]
  public void WaitOptions_RejectsANonPositiveMaxWaitTime()
  {
    AssertWaitFails(SaneWait with { MaxWaitTime = TimeSpan.Zero }, "MaxWaitTime");
    AssertWaitFails(SaneWait with { MaxWaitTime = TimeSpan.FromSeconds(-1) }, "MaxWaitTime");
  }

  /// <summary>
  /// MaxWaitTime caps every Task.Delay the loop performs, and Task.Delay throws above
  /// Timer.MaxSupportedTimeout (~49.7 days) from a call site P1-04's catch-all does not cover: the
  /// host would stop and Docker would crash-loop it. One typo — 100 days for 100 minutes — is
  /// enough, so the ceiling is validated rather than discovered at runtime.
  /// </summary>
  [TestMethod]
  public void WaitOptions_RejectsAMaxWaitTimeThatTaskDelayCannotHonour()
  {
    AssertWaitFails(SaneWait with { MaxWaitTime = TimeSpan.FromDays(100) }, "MaxWaitTime");
    AssertWaitFails(SaneWait with { MaxWaitTime = TimeSpan.MaxValue }, "MaxWaitTime");

    Assert.IsTrue(
      Validate(SaneWait with { MaxWaitTime = TimeSpan.FromDays(7) }).Succeeded,
      "A week is the documented ceiling, so it must be accepted."
    );
  }

  /// <summary>
  /// The bound above exists because of this: everything the validator lets through must be a delay
  /// the framework can actually wait for.
  /// </summary>
  [TestMethod]
  public async Task WaitOptions_TheAcceptedCeilingIsADelayTaskDelayAccepts()
  {
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();

    await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
      Task.Delay(WaitOptionsValidator.MaximumWaitTime, cts.Token)
    );
    Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
      _ = Task.Delay(TimeSpan.FromDays(100), cts.Token)
    );
  }

  [TestMethod]
  public void WaitOptions_RejectsAnInvertedRange()
  {
    var result = Validate(
      SaneWait with
      {
        MinWaitTime = TimeSpan.FromHours(2),
        MaxWaitTime = TimeSpan.FromHours(1),
      }
    );

    Assert.IsTrue(result.Failed);
    StringAssert.Contains(result.FailureMessage!, "MinWaitTime");
    StringAssert.Contains(result.FailureMessage!, "MaxWaitTime");
  }

  [TestMethod]
  public void WaitOptions_AcceptsASaneRange()
  {
    Assert.IsTrue(Validate(SaneWait).Succeeded, "30 s / 1 h is the shipped default.");
    Assert.IsTrue(
      Validate(SaneWait with { MinWaitTime = TimeSpan.FromSeconds(1) }).Succeeded,
      "One second is the documented floor, so it must be accepted."
    );
    Assert.IsTrue(
      Validate(
        new WaitOptions { MinWaitTime = TimeSpan.FromHours(1), MaxWaitTime = TimeSpan.FromHours(1) }
      ).Succeeded,
      "Equal bounds pin the loop to a fixed interval, which is a legitimate configuration."
    );
  }

  /// <summary>
  /// Between the hard floor and the recommended one the service starts, but the operator gets told:
  /// a one-second poll is legal and still a bad idea.
  /// </summary>
  [TestMethod]
  public void WaitOptions_WarnsWithoutFailingBelowTheRecommendedInterval()
  {
    var logger = new CapturingLogger<WaitOptionsValidator>();
    var validator = new WaitOptionsValidator(logger);

    Assert.IsTrue(
      validator.Validate(null, SaneWait with { MinWaitTime = TimeSpan.FromSeconds(1) }).Succeeded
    );
    Assert.AreEqual(1, logger.Warnings.Count, "One second is below the recommended five.");
    StringAssert.Contains(logger.Warnings[0], "MinWaitTime");

    logger.Warnings.Clear();
    Assert.IsTrue(validator.Validate(null, SaneWait).Succeeded);
    Assert.AreEqual(0, logger.Warnings.Count, "30 s is above the recommendation: nothing to say.");
  }

  // --------------------------------------------------------------- OrderOptions

  /// <summary>
  /// A zero volume makes <c>costForVolume</c> zero, which is the input that produced the
  /// runaway-buy and <c>NaN</c> paths of C-2.
  /// </summary>
  [TestMethod]
  public void OrderOptions_RejectsANonPositiveMinOrderVolume()
  {
    AssertOrderFails(SaneOrder with { MinOrderVolume = 0 }, "MinOrderVolume");
    AssertOrderFails(SaneOrder with { MinOrderVolume = -0.1 }, "MinOrderVolume");
    Assert.IsTrue(Validate(SaneOrder with { MinOrderVolume = 0.00005 }).Succeeded);
  }

  [TestMethod]
  public void OrderOptions_RejectsAnAskMultiplierOutsideTheSanityBand()
  {
    AssertOrderFails(SaneOrder with { AskMultiplier = 0 }, "AskMultiplier");
    // The typo the band exists for: 100 instead of 1.0001 bids a hundred times the ask.
    AssertOrderFails(SaneOrder with { AskMultiplier = 100 }, "AskMultiplier");
    AssertOrderFails(SaneOrder with { AskMultiplier = 0.49 }, "AskMultiplier");
    AssertOrderFails(SaneOrder with { AskMultiplier = 1.51 }, "AskMultiplier");

    Assert.IsTrue(Validate(SaneOrder with { AskMultiplier = 1.00001 }).Succeeded);
    Assert.IsTrue(
      Validate(SaneOrder with { AskMultiplier = 0.5 }).Succeeded,
      "The band is inclusive; the test project's own appsettings.json uses 0.5."
    );
    Assert.IsTrue(Validate(SaneOrder with { AskMultiplier = 1.5 }).Succeeded);
  }

  /// <summary>
  /// A multiplier below 1 is a limit buy under the market. <c>SendOrder</c> sets no expiry and never
  /// checks for a fill, so the worker persists <c>LastInvestmentTime</c> on Kraken's acceptance of a
  /// *resting* order: the schedule advances, the fiat stays locked, and DCA stops silently. It stays
  /// legal — the test project uses 0.5 on purpose — so it is warned about, not refused.
  /// </summary>
  [TestMethod]
  public void OrderOptions_WarnsWithoutFailingBelowTheMarketAsk()
  {
    var logger = new CapturingLogger<OrderOptionsValidator>();
    var validator = new OrderOptionsValidator(logger);

    Assert.IsTrue(validator.Validate(null, SaneOrder with { AskMultiplier = 0.9 }).Succeeded);
    Assert.AreEqual(1, logger.Warnings.Count, "0.9 places the limit price below the ask.");
    StringAssert.Contains(logger.Warnings[0], "AskMultiplier");

    logger.Warnings.Clear();
    Assert.IsTrue(validator.Validate(null, SaneOrder).Succeeded);
    Assert.AreEqual(
      0,
      logger.Warnings.Count,
      "1.00001 crosses the spread, which is the point of the multiplier: nothing to say."
    );
  }

  [TestMethod]
  public void OrderOptions_RejectsAFeeOutsideZeroToOneHundredPercent()
  {
    AssertOrderFails(SaneOrder with { Fee = -0.1 }, "Fee");
    AssertOrderFails(SaneOrder with { Fee = 100.1 }, "Fee");

    Assert.IsTrue(Validate(SaneOrder with { Fee = 0.4 }).Succeeded);
    Assert.IsTrue(Validate(SaneOrder with { Fee = 0 }).Succeeded, "A zero fee is legitimate.");
  }

  [TestMethod]
  public void OrderOptions_RejectsNonFiniteNumbers()
  {
    // A double TypeConverter parses "Infinity" and "NaN", so configuration can carry them.
    AssertOrderFails(SaneOrder with { Fee = double.NaN }, "Fee");
    AssertOrderFails(SaneOrder with { MinOrderVolume = double.PositiveInfinity }, "MinOrderVolume");
    AssertOrderFails(SaneOrder with { AskMultiplier = double.NaN }, "AskMultiplier");
  }

  [TestMethod]
  public void OrderOptions_RejectsAnUndefinedOrderType()
  {
    AssertOrderFails(SaneOrder with { Type = (OrderType)99 }, "Type");

    Assert.IsTrue(Validate(SaneOrder with { Type = OrderType.Market }).Succeeded);
    Assert.IsTrue(Validate(SaneOrder with { Type = OrderType.Limit }).Succeeded);
  }

  [TestMethod]
  public void OrderOptions_RejectsAPairThatIsNotAPairShape()
  {
    AssertOrderFails(SaneOrder with { CryptoPair = "" }, "CryptoPair");
    // What `docker run --env-file` and systemd EnvironmentFile= hand through for the quoted value
    // in docker/stack.env. Compose strips the quotes; those two do not (M-14).
    AssertOrderFails(SaneOrder with { CryptoPair = "\"XBTCHF\"" }, "CryptoPair");
    AssertOrderFails(SaneOrder with { CryptoPair = "xbtchf" }, "CryptoPair");
    AssertOrderFails(SaneOrder with { CryptoPair = "XBT" }, "CryptoPair");
    AssertOrderFails(SaneOrder with { CryptoPair = "XBT/CHF" }, "CryptoPair");

    Assert.IsTrue(Validate(SaneOrder with { CryptoPair = "XBTCHF" }).Succeeded);
    Assert.IsTrue(
      Validate(SaneOrder with { CryptoPair = "XXBTZUSD" }).Succeeded,
      "Kraken's own altname form must stay valid."
    );
  }

  /// <summary>
  /// Both length bounds, at the boundary, against Kraken's live <c>AssetPairs</c> list: the shortest
  /// altnames it trades are four characters and the longest thirteen. The first version of this rule
  /// used <c>{5,12}</c> and would have refused to start for 17 real pairs — the cost of an untested
  /// bound, which is why both edges are pinned here.
  /// </summary>
  [TestMethod]
  public void OrderOptions_AcceptsTheShortestAndLongestPairsKrakenActuallyTrades()
  {
    Assert.IsTrue(
      Validate(SaneOrder with { CryptoPair = "SUSD" }).Succeeded,
      "Four characters: Kraken's single-letter tickers quoted in USD/EUR."
    );
    Assert.IsTrue(
      Validate(SaneOrder with { CryptoPair = "CHILLHOUSEEUR" }).Succeeded,
      "Thirteen characters: the longest altname currently listed."
    );
    Assert.IsTrue(Validate(SaneOrder with { CryptoPair = new string('X', 16) }).Succeeded);

    AssertOrderFails(SaneOrder with { CryptoPair = "USD" }, "CryptoPair");
    AssertOrderFails(SaneOrder with { CryptoPair = new string('X', 17) }, "CryptoPair");
  }

  // ------------------------------------------------------------- BalanceOptions

  [TestMethod]
  public void BalanceOptions_RejectsANegativeReserveButAcceptsZero()
  {
    var validator = new BalanceOptionsValidator();
    var sane = new BalanceOptions { DefaultTopupDayOfMonth = 26, ReserveFiat = 0 };

    var rejected = validator.Validate(null, sane with { ReserveFiat = -1 });

    Assert.IsTrue(rejected.Failed);
    StringAssert.Contains(rejected.FailureMessage!, "ReserveFiat");
    Assert.IsTrue(
      validator.Validate(null, sane).Succeeded,
      "Reserving nothing means spending the whole balance, which is the default and legitimate."
    );
    Assert.IsTrue(validator.Validate(null, sane with { ReserveFiat = 250 }).Succeeded);
  }

  // ---------------------------------------------------------------------- Helpers

  private static ValidateOptionsResult Validate(WaitOptions options) =>
    new WaitOptionsValidator(NullLogger<WaitOptionsValidator>.Instance).Validate(null, options);

  private static ValidateOptionsResult Validate(OrderOptions options) =>
    new OrderOptionsValidator(NullLogger<OrderOptionsValidator>.Instance).Validate(null, options);

  private static void AssertWaitFails(WaitOptions options, string expectedOptionName) =>
    AssertFails(Validate(options), expectedOptionName, options.ToString());

  private static void AssertOrderFails(OrderOptions options, string expectedOptionName) =>
    AssertFails(Validate(options), expectedOptionName, options.ToString());

  private static void AssertFails(
    ValidateOptionsResult result,
    string expectedOptionName,
    string configuration
  )
  {
    Assert.IsTrue(result.Failed, $"Should not have started up with {configuration}.");
    StringAssert.Contains(
      result.FailureMessage!,
      expectedOptionName,
      $"The startup error is all the operator gets, so it must name {expectedOptionName}. "
        + $"Got: {result.FailureMessage}"
    );
  }

  /// <summary>
  /// Records the warnings a validator writes. MSTest has no logger substitute and the repo has no
  /// mocking library, so this is the cheapest way to assert on a log line.
  /// </summary>
  private sealed class CapturingLogger<T> : ILogger<T>
  {
    public List<string> Warnings { get; } = [];

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
      if (logLevel == LogLevel.Warning)
      {
        Warnings.Add(formatter(state, exception));
      }
    }
  }
}
