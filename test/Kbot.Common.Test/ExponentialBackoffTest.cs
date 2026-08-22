using Kbot.Common.Helpers;

namespace Kbot.Common.Test;

/// <summary>
/// The pacing the worker loops rely on: a first delay of exactly the base, delays that never shrink
/// while failures continue, a hard cap, and a reset back to the base after a success.
/// </summary>
[TestClass]
public class ExponentialBackoffTest
{
  private static readonly TimeSpan Base = TimeSpan.FromSeconds(10);
  private static readonly TimeSpan Max = TimeSpan.FromMinutes(4); // Base * 2^4 * 1.5

  [TestMethod]
  public void Next_StartsAtTheBaseDelay()
  {
    var backoff = new ExponentialBackoff(Base, Max);

    Assert.AreEqual(Base, backoff.Next());
  }

  [TestMethod]
  public void Next_NeverShrinksAndNeverExceedsTheCap()
  {
    var backoff = new ExponentialBackoff(Base, Max);
    var previous = TimeSpan.Zero;

    for (var attempt = 0; attempt < 100; attempt++)
    {
      var delay = backoff.Next();

      Assert.IsTrue(
        delay >= previous,
        $"Attempt {attempt} returned {delay}, which is shorter than the previous {previous}."
      );
      Assert.IsTrue(delay >= Base, $"Attempt {attempt} returned {delay}, below the base {Base}.");
      Assert.IsTrue(delay <= Max, $"Attempt {attempt} returned {delay}, above the cap {Max}.");
      previous = delay;
    }

    Assert.AreEqual(Max, previous, "A long run of failures must settle on the cap.");
  }

  [TestMethod]
  public void Next_DoublesTheWindowUntilItReachesTheCap()
  {
    var backoff = new ExponentialBackoff(Base, Max);

    backoff.Next(); // attempt 0: exactly Base.
    var second = backoff.Next();
    var third = backoff.Next();

    Assert.IsTrue(second >= Base && second <= 2 * Base, $"Second delay {second} left its window.");
    Assert.IsTrue(third >= 2 * Base && third <= 4 * Base, $"Third delay {third} left its window.");
  }

  [TestMethod]
  public void Next_JittersInsideTheWindow()
  {
    // Full jitter would let a delay come out shorter than the previous one, which the worker loops
    // must not do; the jitter therefore fills the window between the previous ceiling and the
    // current one. Two samples of the same attempt should still differ.
    var samples = new List<TimeSpan>();
    for (var run = 0; run < 50; run++)
    {
      var backoff = new ExponentialBackoff(Base, Max);
      backoff.Next();
      backoff.Next();
      samples.Add(backoff.Next());
    }

    Assert.IsGreaterThan(1, samples.Distinct().Count(), "The delay is not jittered at all.");
    Assert.IsTrue(samples.All(s => s >= 2 * Base && s <= 4 * Base));
  }

  [TestMethod]
  public void Reset_ReturnsToTheBaseDelay()
  {
    var backoff = new ExponentialBackoff(Base, Max);
    for (var attempt = 0; attempt < 5; attempt++)
    {
      backoff.Next();
    }

    backoff.Reset();

    Assert.AreEqual(Base, backoff.Next(), "A successful cycle must clear the failure history.");
  }

  [TestMethod]
  public void Constructor_NormalisesADegenerateRange()
  {
    // WaitOptions currently accepts 00:00:00 and does not require Min <= Max at every call site, so
    // neither a negative base nor an inverted range may produce a nonsense delay. Rejecting such a
    // configuration outright is P1-07.
    var negative = new ExponentialBackoff(TimeSpan.FromSeconds(-5), Max);
    var inverted = new ExponentialBackoff(Base, TimeSpan.Zero);

    Assert.AreEqual(TimeSpan.Zero, negative.Next());
    Assert.AreEqual(Base, inverted.Next());
    Assert.AreEqual(Base, inverted.Next(), "An inverted range collapses onto the base delay.");
  }

  [TestMethod]
  public void Next_DoesNotOverflowOnAHugeBaseDelay()
  {
    var backoff = new ExponentialBackoff(TimeSpan.FromDays(1000), TimeSpan.MaxValue);

    for (var attempt = 0; attempt < 100; attempt++)
    {
      var delay = backoff.Next();
      Assert.IsTrue(delay > TimeSpan.Zero, $"Attempt {attempt} overflowed into {delay}.");
    }
  }
}
