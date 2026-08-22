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
    new OrderOptionsValidator().Validate(null, options);

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
