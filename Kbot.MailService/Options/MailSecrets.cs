namespace Kbot.MailService.Options;

public record MailSecrets
{
    public string SenderEmail { get; init; } = string.Empty;
    public string GoogleAppPassword { get; init; } = string.Empty;
    public string ReceiverEmail { get; init; } = string.Empty;
}
