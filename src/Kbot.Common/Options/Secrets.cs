namespace Kbot.Common.Options;

public record Secrets
{
    public string ApiKey { get; init; } = "";
    public string ApiSecret { get; init; } = "";
}
