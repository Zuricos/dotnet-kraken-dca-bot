using System.Globalization;

namespace Kbot.Common.Options;

public record CultureOptions
{
    public string CultureString { get; init; } = "";
    public string Fiat { get; init; } = "";
    public string CountyCode { get; init; } = "";

    public CultureInfo CultureInfo => new(CultureString);
}
