using Kbot.Common.Enums;

namespace Kbot.DcaService.Options;

public record OrderOptions
{
    public OrderType Type { get; init; }
    public double Fee { get; init; }
    public double MinOrderVolume { get; init; }
    public double AskMultiplier { get; init; }
    public required string CryptoPair { get; init; }

    public double InklusiveFeeMultiplier => 1 + Fee / 100;
}
