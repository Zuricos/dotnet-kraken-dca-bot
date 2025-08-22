using Microsoft.Extensions.Options;

namespace Kbot.DcaService.Options;

public record WaitOptions
{
  public TimeSpan MinWaitTime { get; init; }
  public TimeSpan MaxWaitTime { get; init; }
}

public class WaitOptionsValidator : IValidateOptions<WaitOptions>
{
  public ValidateOptionsResult Validate(string? name, WaitOptions options)
  {
    List<string> vor = [];
    if (options.MinWaitTime < TimeSpan.Zero)
    {
      vor.Add("MinWaitTime must be greater than or equal to 0");
    }
    if (options.MaxWaitTime < TimeSpan.Zero)
    {
      vor.Add("MaxWaitTime must be greater than or equal to 0");
    }
    if (options.MinWaitTime > options.MaxWaitTime)
    {
      vor.Add("MinWaitTime must be less than or equal to MaxWaitTime");
    }
    if (vor.Count > 0)
    {
      return ValidateOptionsResult.Fail("WaitOptions incomplete: " + string.Join(", ", vor));
    }
    return ValidateOptionsResult.Success;
  }
}
