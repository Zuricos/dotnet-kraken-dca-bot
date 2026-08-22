using Microsoft.Extensions.Options;

namespace Kbot.DcaService.Options;

public record BalanceOptions
{
  public int DefaultTopupDayOfMonth { get; init; }
  public double ReserveFiat { get; init; }
}

public class BalanceOptionsValidator : IValidateOptions<BalanceOptions>
{
  public ValidateOptionsResult Validate(string? name, BalanceOptions options)
  {
    List<string> vor = [];
    // 1-28 only: 29-31 does not exist in every month, so a higher value would silently mean a
    // different day depending on the month. TimeComputeService still clamps to the month length as
    // defence in depth, for values that reach it from a persisted state file or a future config
    // source that does not run this validator.
    if (options.DefaultTopupDayOfMonth < 1 || options.DefaultTopupDayOfMonth > 28)
    {
      vor.Add(
        "DefaultTopupDayOfMonth must be between 1 and 28; 29-31 is rejected because month lengths "
          + "differ and the day would not exist in every month"
      );
    }
    if (options.ReserveFiat < 0)
    {
      vor.Add("ReserveFiat must be greater than or equal to 0");
    }
    if (vor.Count > 0)
    {
      return ValidateOptionsResult.Fail("BalanceOptions incomplete: " + string.Join(", ", vor));
    }
    return ValidateOptionsResult.Success;
  }
}
