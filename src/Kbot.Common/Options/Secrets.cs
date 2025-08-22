using Microsoft.Extensions.Options;

namespace Kbot.Common.Options;

public record Secrets
{
  public string ApiKey { get; init; } = "";
  public string ApiSecret { get; init; } = "";
}

public class SecretsValidator : IValidateOptions<Secrets>
{
  public ValidateOptionsResult Validate(string? name, Secrets options)
  {
    List<string> vor = [];

    if (string.IsNullOrEmpty(options.ApiKey))
    {
      vor.Add("ApiKey must be set");
    }
    if (string.IsNullOrEmpty(options.ApiSecret))
    {
      vor.Add("ApiSecret must be set");
    }
    if (vor.Count > 0)
    {
      return ValidateOptionsResult.Fail("Secrets incomplete: " + string.Join(", ", vor));
    }
    return ValidateOptionsResult.Success;
  }
}
