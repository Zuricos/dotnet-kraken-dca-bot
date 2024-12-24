using System.Text.Json.Serialization;

namespace Kbot.Common.Dtos;

public record BalanceUnparsed
{
    [JsonPropertyName("XXBT")]
    public string Btc { get; set; } = "";
    [JsonPropertyName("XXDG")]
    public string Doge { get; set; } = "";
    [JsonPropertyName("CHF")]
    public string Chf { get; set; } = "";
    [JsonPropertyName("ZUSD")]
    public string Usd { get; set; } = "";

    public Balance Parse()
    {
        return new Balance
        {
            Btc = double.Parse(Btc),
            Chf = double.Parse(Chf),
            Doge = double.Parse(Doge),
            Usd = double.Parse(Usd)
        };
    }
}


public record Balance
{
    public double Btc { get; set; }
    public double Chf { get; set; }
    public double Doge { get; set; }
    public double Usd { get; set; }

    public override string ToString()
    {
        return $"BTC: {Btc}, CHF: {Chf}, DOGE: {Doge}, USD: {Usd}";
    }
}