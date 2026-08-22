namespace Kbot.Common.Helpers;

/// <summary>
/// Retry pacing for the worker loops: the delay doubles with every consecutive failure, is jittered,
/// and never leaves the <c>[baseDelay, maxDelay]</c> range. <see cref="Reset"/> puts it back to
/// <c>baseDelay</c> after a successful iteration.
/// </summary>
/// <remarks>
/// <para>
/// The jitter window for attempt <c>n</c> is the span between the previous ceiling and the current
/// one — <c>base·2^(n-1) … base·2^n</c>, capped at <c>maxDelay</c>. That de-correlates retries
/// without ever returning a delay shorter than the one before it, which is what lets a caller state
/// that it backs off monotonically. Once the ceiling reaches <c>maxDelay</c> every further attempt
/// returns exactly <c>maxDelay</c>.
/// </para>
/// <para>
/// Not thread-safe by design: one instance belongs to one loop. <see cref="Random.Shared"/> is used
/// for the jitter, so there is no shared <see cref="Random"/> instance to synchronise.
/// </para>
/// </remarks>
public sealed class ExponentialBackoff
{
  /// <summary>
  /// Doubling stops here. Any sane cap is reached long before, and it keeps <c>2^n</c> finite.
  /// </summary>
  private const int MaxAttempt = 30;

  private readonly TimeSpan _baseDelay;
  private readonly TimeSpan _maxDelay;
  private int _attempt;

  /// <param name="baseDelay">
  /// The first delay and the floor of every later one. A negative value is treated as zero.
  /// </param>
  /// <param name="maxDelay">
  /// The cap. A value below <paramref name="baseDelay"/> is raised to it, so the range is never
  /// inverted.
  /// </param>
  public ExponentialBackoff(TimeSpan baseDelay, TimeSpan maxDelay)
  {
    _baseDelay = baseDelay < TimeSpan.Zero ? TimeSpan.Zero : baseDelay;
    _maxDelay = maxDelay < _baseDelay ? _baseDelay : maxDelay;
  }

  /// <summary>
  /// The delay to wait before the next attempt, and counts the attempt.
  /// </summary>
  public TimeSpan Next()
  {
    var ceiling = CeilingFor(_attempt);
    var floor = _attempt == 0 ? ceiling : CeilingFor(_attempt - 1);
    if (_attempt < MaxAttempt)
    {
      _attempt++;
    }

    if (ceiling <= floor)
    {
      return ceiling;
    }
    var jitter = (long)(Random.Shared.NextDouble() * (ceiling - floor).Ticks);
    return floor + TimeSpan.FromTicks(jitter);
  }

  /// <summary>
  /// Forgets the failure history, so the next <see cref="Next"/> returns the base delay again.
  /// </summary>
  public void Reset() => _attempt = 0;

  /// <summary>
  /// <c>baseDelay·2^attempt</c>, capped. Computed in <see cref="double"/> so a large base delay
  /// cannot overflow the tick arithmetic on the way to the cap.
  /// </summary>
  private TimeSpan CeilingFor(int attempt)
  {
    var scaledTicks = _baseDelay.Ticks * Math.Pow(2, Math.Min(attempt, MaxAttempt));
    return scaledTicks >= _maxDelay.Ticks ? _maxDelay : TimeSpan.FromTicks((long)scaledTicks);
  }
}
