using System.Text.RegularExpressions;
using Kbot.Common.Enums;
using Microsoft.Extensions.Options;

namespace Kbot.DcaService.Options;

public record OrderOptions
{
  public OrderType Type { get; init; }
  public double Fee { get; init; }
  public double MinOrderVolume { get; init; }
  public double AskMultiplier { get; init; }
  public required string CryptoPair { get; init; }

  public double InklusiveFeeMultiplier => 1 + Fee / 100;
}

/// <summary>
/// Every numeric rule here used to be <c>&gt;= 0</c>, so a zero — the value an omitted environment
/// variable binds to — was a legal configuration (H-10). A zero <see cref="OrderOptions.AskMultiplier"/>
/// or <see cref="OrderOptions.MinOrderVolume"/> makes the cost of an order zero, which is the input
/// that produced the runaway-buy and <c>NaN</c> paths of C-2. The bands are deliberately narrow:
/// these are all values a human types once, and a typo is far more likely than an exotic-but-valid
/// setting.
/// </summary>
public partial class OrderOptionsValidator : IValidateOptions<OrderOptions>
{
  /// <summary>
  /// Sanity band for a <em>price</em> multiplier: the point of it is to sit a hair above or below
  /// the current ask, so anything outside ±50 % is a typo — <c>100</c> instead of <c>1.0001</c>
  /// would bid 100× the ask.
  /// </summary>
  public const double MinAskMultiplier = 0.5;

  /// <inheritdoc cref="MinAskMultiplier"/>
  public const double MaxAskMultiplier = 1.5;

  /// <summary><see cref="OrderOptions.Fee"/> is a percentage, not a fraction.</summary>
  public const double MaxFeePercent = 100;

  public ValidateOptionsResult Validate(string? name, OrderOptions options)
  {
    List<string> vor = [];

    if (!Enum.IsDefined(options.Type))
    {
      vor.Add(
        $"Type must be one of {string.Join(", ", Enum.GetNames<OrderType>())} (was {(int)options.Type})"
      );
    }
    if (!double.IsFinite(options.Fee) || options.Fee < 0 || options.Fee > MaxFeePercent)
    {
      vor.Add($"Fee must be a percentage between 0 and {MaxFeePercent} (was {options.Fee})");
    }
    if (!double.IsFinite(options.MinOrderVolume) || options.MinOrderVolume <= 0)
    {
      vor.Add(
        $"MinOrderVolume must be greater than 0 (was {options.MinOrderVolume}); a zero volume makes "
          + "the cost of an order zero, which schedules orders as fast as the loop runs"
      );
    }
    if (
      !double.IsFinite(options.AskMultiplier)
      || options.AskMultiplier < MinAskMultiplier
      || options.AskMultiplier > MaxAskMultiplier
    )
    {
      vor.Add(
        $"AskMultiplier must be between {MinAskMultiplier} and {MaxAskMultiplier} (was "
          + $"{options.AskMultiplier}); it multiplies the ask *price*, so a value outside that band "
          + "is a typo rather than a strategy"
      );
    }
    if (string.IsNullOrEmpty(options.CryptoPair))
    {
      vor.Add("CryptoPair must be set");
    }
    else if (!CryptoPairPattern().IsMatch(options.CryptoPair))
    {
      // Deliberately conservative: it catches the quoted values `docker run --env-file` and systemd
      // hand through verbatim, lower-case pairs, and a pair accidentally set to a price or a path.
      // It is not a Kraken pair list — resolving pairs against the AssetPairs endpoint is P2-05.
      vor.Add(
        $"CryptoPair must be 5-12 upper-case letters or digits, e.g. XBTCHF (was "
          + $"'{options.CryptoPair}')"
      );
    }
    if (vor.Count > 0)
    {
      return ValidateOptionsResult.Fail("OrderOptions incomplete: " + string.Join(", ", vor));
    }
    return ValidateOptionsResult.Success;
  }

  [GeneratedRegex("^[A-Z0-9]{5,12}$")]
  private static partial Regex CryptoPairPattern();
}
