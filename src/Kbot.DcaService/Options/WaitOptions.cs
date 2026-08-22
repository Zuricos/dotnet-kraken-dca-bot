using Microsoft.Extensions.Options;

namespace Kbot.DcaService.Options;

public record WaitOptions
{
  public TimeSpan MinWaitTime { get; init; }
  public TimeSpan MaxWaitTime { get; init; }
}

/// <summary>
/// <see cref="WaitOptions.MinWaitTime"/> is the polling interval of the whole trading loop: every iteration
/// issues a Kraken <c>Balance</c> and a <c>Ticker</c> call. A zero value used to pass validation —
/// <c>MinWaitTime = MaxWaitTime = TimeSpan.Zero</c> satisfied every rule — which turned
/// <c>Task.Delay</c> into a no-op and the loop into a busy loop against a rate-limited API at 100 %
/// CPU (H-10). Because there was no <c>WaitOptions</c> section in <c>appsettings.json</c> either,
/// that was what an operator who forgot <c>stack.env</c> got: not a startup failure, a busy loop.
/// </summary>
public class WaitOptionsValidator(ILogger<WaitOptionsValidator> logger)
  : IValidateOptions<WaitOptions>
{
  /// <summary>
  /// The hard floor. Kraken's REST tier allows a small call-rate budget that regenerates over
  /// seconds, so anything below one second per cycle is a rate-limit ban in the making.
  /// </summary>
  public static readonly TimeSpan MinimumPollInterval = TimeSpan.FromSeconds(1);

  /// <summary>
  /// Below this the configuration is legal but hard on the rate limiter, so it is worth a line in
  /// the log rather than a refusal to start.
  /// </summary>
  public static readonly TimeSpan RecommendedMinimumPollInterval = TimeSpan.FromSeconds(5);

  public ValidateOptionsResult Validate(string? name, WaitOptions options)
  {
    List<string> vor = [];
    // One check, not two: "greater than zero" is implied by "at least one second", and emitting
    // both messages for MinWaitTime = 0 would only make the startup error harder to read.
    if (options.MinWaitTime < MinimumPollInterval)
    {
      vor.Add(
        $"MinWaitTime must be at least {MinimumPollInterval} (was {options.MinWaitTime}); it is the "
          + "polling interval of the trading loop, and a zero or sub-second value busy-loops against "
          + "Kraken's rate limit"
      );
    }
    if (options.MaxWaitTime <= TimeSpan.Zero)
    {
      vor.Add($"MaxWaitTime must be greater than 0 (was {options.MaxWaitTime})");
    }
    if (options.MinWaitTime > options.MaxWaitTime)
    {
      vor.Add(
        $"MinWaitTime ({options.MinWaitTime}) must be less than or equal to MaxWaitTime "
          + $"({options.MaxWaitTime})"
      );
    }
    if (vor.Count > 0)
    {
      return ValidateOptionsResult.Fail("WaitOptions incomplete: " + string.Join(", ", vor));
    }
    if (options.MinWaitTime < RecommendedMinimumPollInterval)
    {
      logger.LogWarning(
        "MinWaitTime is {MinWaitTime}, below the recommended {Recommended}: every cycle issues a "
          + "Kraken Balance and Ticker call, so polling this fast risks a rate-limit lockout.",
        options.MinWaitTime,
        RecommendedMinimumPollInterval
      );
    }
    return ValidateOptionsResult.Success;
  }
}
