using System.Text.Json;

namespace Kbot.MailService.Utility;

/// <summary>
/// Rate-limits the startup notification mail (<c>"DCA - Welcome"</c> / <c>"DCA - Restart of
/// Container"</c>) so a container that keeps restarting cannot keep mailing.
/// </summary>
/// <remarks>
/// The timestamp of the last successful send is kept in its own small file next to the other state
/// files, which is what makes the limit survive the restart it is meant to catch. A missing,
/// unreadable or future-dated marker counts as "no recent send": the notification is worth one mail
/// too many far more than it is worth being silently switched off.
/// </remarks>
public sealed class RestartNoticeThrottle(
  ILogger logger,
  string? markerPath = null,
  TimeSpan? minimumInterval = null
)
{
  /// <summary>Long enough that a Docker restart loop sends at most one mail per hour.</summary>
  public static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromHours(1);

  private const string DefaultMarkerPath = "state/restart-notice.json";
  private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

  private readonly string _markerPath = markerPath ?? DefaultMarkerPath;
  private readonly TimeSpan _minimumInterval = minimumInterval ?? DefaultMinimumInterval;

  /// <summary>
  /// Whether the startup notification may be sent now.
  /// </summary>
  public bool ShouldSend(DateTimeOffset utcNow)
  {
    var lastSent = ReadLastSent();
    if (lastSent is null)
    {
      return true;
    }

    var elapsed = utcNow - lastSent.Value;
    if (elapsed >= _minimumInterval || elapsed < TimeSpan.Zero)
    {
      return true;
    }

    logger.LogWarning(
      "Startup notification suppressed: the last one was sent {Elapsed} ago, which is less than "
        + "{MinimumInterval}. A restarting container must not keep mailing.",
      elapsed,
      _minimumInterval
    );
    return false;
  }

  /// <summary>
  /// Records a successful send. Called only after the mail actually went out, so a failed send does
  /// not start the quiet period.
  /// </summary>
  public void RecordSent(DateTimeOffset utcNow)
  {
    try
    {
      var directory = Path.GetDirectoryName(_markerPath);
      if (!string.IsNullOrEmpty(directory))
      {
        Directory.CreateDirectory(directory);
      }
      File.WriteAllText(_markerPath, JsonSerializer.Serialize(new Marker(utcNow), JsonOptions));
    }
    catch (Exception ex)
    {
      // Losing the marker only costs the rate limit, so it must never take the service down with
      // it. Unifying the state-file IO is P4-01.
      logger.LogWarning(
        ex,
        "Could not record the startup notification in {MarkerPath}.",
        _markerPath
      );
    }
  }

  private DateTimeOffset? ReadLastSent()
  {
    try
    {
      if (!File.Exists(_markerPath))
      {
        return null;
      }
      var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(_markerPath), JsonOptions);
      return marker?.LastSentUtc;
    }
    catch (Exception ex)
    {
      logger.LogWarning(
        ex,
        "Could not read the startup notification marker {MarkerPath}; treating it as absent.",
        _markerPath
      );
      return null;
    }
  }

  private sealed record Marker(DateTimeOffset LastSentUtc);
}
