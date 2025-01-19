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
        if (options.DefaultTopupDayOfMonth < 1 || options.DefaultTopupDayOfMonth > 31)
        {
            vor.Add("DefaultTopupDayOfMonth must be between 1 and 31");
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