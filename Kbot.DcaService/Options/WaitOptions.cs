namespace Kbot.DcaService.Options;

public record WaitOptions
{
    public TimeSpan MinWaitTime { get; init; }
    public TimeSpan MaxWaitTime { get; init; }
}
