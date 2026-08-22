using Kbot.MailService.Utility;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kbot.MailService.Test;

/// <summary>
/// The rate limit on the startup notification. Without it, a host that keeps dying and being
/// restarted by Docker mails "DCA - Restart of Container" on every attempt.
/// </summary>
[TestClass]
public class RestartNoticeThrottleTest
{
  private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
  private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

  private string _markerPath = null!;

  [TestInitialize]
  public void Setup() =>
    _markerPath = Path.Combine(
      Path.GetTempPath(),
      $"kbot-restart-notice-{Guid.NewGuid():N}",
      "restart-notice.json"
    );

  [TestCleanup]
  public void Cleanup()
  {
    var directory = Path.GetDirectoryName(_markerPath)!;
    if (Directory.Exists(directory))
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  [TestMethod]
  public void ShouldSend_IsTrueOnAFirstRun()
  {
    Assert.IsTrue(NewThrottle().ShouldSend(Now), "A first start has to introduce itself.");
  }

  [TestMethod]
  public void ShouldSend_IsFalseWithinTheQuietPeriod()
  {
    var throttle = NewThrottle();
    throttle.RecordSent(Now);

    Assert.IsFalse(
      NewThrottle().ShouldSend(Now.AddMinutes(1)),
      "A restart a minute after the last notification must stay silent."
    );
  }

  [TestMethod]
  public void ShouldSend_IsTrueOnceTheQuietPeriodPassed()
  {
    NewThrottle().RecordSent(Now);

    Assert.IsTrue(NewThrottle().ShouldSend(Now + Interval));
  }

  [TestMethod]
  public void ShouldSend_IsTrueWhenTheMarkerLiesInTheFuture()
  {
    // A clock correction must not silence the notification for good.
    NewThrottle().RecordSent(Now.AddDays(30));

    Assert.IsTrue(NewThrottle().ShouldSend(Now));
  }

  [TestMethod]
  public void ShouldSend_IsTrueWhenTheMarkerIsUnreadable()
  {
    Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
    File.WriteAllText(_markerPath, "not json");

    Assert.IsTrue(
      NewThrottle().ShouldSend(Now),
      "A corrupt marker must not switch the notification off."
    );
  }

  [TestMethod]
  public void RecordSent_SurvivesAnUnwritableLocation()
  {
    // The rate limit is a convenience; losing it may never take the service down.
    var throttle = new RestartNoticeThrottle(
      NullLogger.Instance,
      Path.Combine(_markerPath, "nested", "marker.json"),
      Interval
    );
    Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
    File.WriteAllText(_markerPath, "a file where a directory would have to be");

    throttle.RecordSent(Now);

    Assert.IsTrue(throttle.ShouldSend(Now));
  }

  private RestartNoticeThrottle NewThrottle() => new(NullLogger.Instance, _markerPath, Interval);
}
