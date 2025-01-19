using Microsoft.Extensions.Options;

namespace Kbot.MailService.Options;

public record MailSecrets
{
    public string SenderEmail { get; init; } = string.Empty;
    public string GoogleAppPassword { get; init; } = string.Empty;
    public string ReceiverEmail { get; init; } = string.Empty;
}


public class MailSecretsValidator : IValidateOptions<MailSecrets>
{
    public ValidateOptionsResult Validate(string? name, MailSecrets options)
    {
        List<string> vor = [];

        if (string.IsNullOrEmpty(options.SenderEmail))
        {
            vor.Add("SenderEmail must be set");
        }
        if (string.IsNullOrEmpty(options.GoogleAppPassword))
        {
            vor.Add("GoogleAppPassword must be set");
        }
        if (string.IsNullOrEmpty(options.ReceiverEmail))
        {
            vor.Add("ReceiverEmail must be set");
        }
        if (!options.SenderEmail.Contains("@"))
        {
            vor.Add("SenderEmail must be a valid email address");
        }
        if (!options.ReceiverEmail.Contains("@"))
        {
            vor.Add("ReceiverEmail must be a valid email address");
        }
        if (options.GoogleAppPassword.Length != 16)
        {
            vor.Add("GoogleAppPassword must be 16 characters long");
        }
        if (vor.Count > 0)
        {
            return ValidateOptionsResult.Fail("MailSecrets incomplete: " + string.Join(", ", vor));
        }
        return ValidateOptionsResult.Success;
    }
}