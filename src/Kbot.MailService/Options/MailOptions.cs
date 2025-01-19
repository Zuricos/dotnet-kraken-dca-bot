using Microsoft.Extensions.Options;

namespace Kbot.MailService.Options;

public record MailOptions
{
    public int HourOfDay { get; init; } = 6;
    public int DayOfMonth { get; init; } = 1;
    public string CryptoPair { get; init; } = string.Empty;
    public string Crypto { get; init; } = string.Empty;
    public string Fiat { get; init; } = string.Empty;
    public string HistoryStartDate { get; init; } = string.Empty;

    public DateTimeOffset HistoryStartDateDto => DateTimeOffset.Parse(HistoryStartDate);
}


public class MailOptionsValidator : IValidateOptions<MailOptions>
{
    public ValidateOptionsResult Validate(string? name, MailOptions options)
    {
        List<string> vor = [];

        if (options.HourOfDay < 0 || options.HourOfDay > 23)
        {
            vor.Add("HourOfDay must be between 0 and 23");
        }
        if (options.DayOfMonth < 1 || options.DayOfMonth > 28)
        {
            vor.Add("DayOfMonth must be between 1 and 28");
        }
        if (string.IsNullOrEmpty(options.CryptoPair))
        {
            vor.Add("CryptoPair must be set");
        }
        if (string.IsNullOrEmpty(options.Crypto))
        {
            vor.Add("Crypto must be set");
        }
        if (string.IsNullOrEmpty(options.Fiat))
        {
            vor.Add("Fiat must be set");
        }
        if (string.IsNullOrEmpty(options.HistoryStartDate))
        {
            vor.Add("HistoryStartDate must be set");
        }
        if (!DateTime.TryParseExact(options.HistoryStartDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _))
        {
            vor.Add("HistoryStartDate must be in format yyyy-MM-dd");
        }
        if (vor.Count > 0)
        {
            return ValidateOptionsResult.Fail("MailOptions incomplete: " + string.Join(", ", vor));
        }
        return ValidateOptionsResult.Success;
    }
}