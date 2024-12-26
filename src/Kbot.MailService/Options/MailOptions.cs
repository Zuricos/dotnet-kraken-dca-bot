namespace Kbot.MailService.Options;

public record MailOptions
{
    public int HourOfDay { get; init; } = 6;
    public string CryptoPair { get; init; } = string.Empty;
    public string Crypto { get; init; } = string.Empty;
    public string Fiat { get; init; } = string.Empty;
}
