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

public class OrderOptionsValidator : IValidateOptions<OrderOptions>
{
  public ValidateOptionsResult Validate(string? name, OrderOptions options)
  {
    List<string> vor = [];

    if (options.Fee < 0)
    {
      vor.Add("Fee must be greater than or equal to 0");
    }
    if (options.MinOrderVolume < 0)
    {
      vor.Add("MinOrderVolume must be greater than or equal to 0");
    }
    if (options.AskMultiplier < 0)
    {
      vor.Add("AskMultiplier must be greater than or equal to 0");
    }
    if (string.IsNullOrEmpty(options.CryptoPair))
    {
      vor.Add("CryptoPair must be set");
    }
    if (vor.Count > 0)
    {
      return ValidateOptionsResult.Fail("OrderOptions incomplete: " + string.Join(", ", vor));
    }
    return ValidateOptionsResult.Success;
  }
}
