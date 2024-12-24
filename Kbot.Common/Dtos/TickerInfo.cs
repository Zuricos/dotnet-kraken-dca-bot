using System.Text.Json.Serialization;

namespace Kbot.Common.Dtos;

public record TickerInfoUnparsed
{
    [JsonPropertyName("a")]
    public List<string> Ask { get; set; } = new();
    [JsonPropertyName("b")]
    public List<string> Bid { get; set; } = new();
    [JsonPropertyName("c")]
    public List<string> LastTrade { get; set; } = new();
    [JsonPropertyName("v")]
    public List<string> Volume { get; set; } = new();
    [JsonPropertyName("p")]
    public List<string> VolumeWeighted { get; set; } = new();
    [JsonPropertyName("t")]
    public List<int> NumberOfTrades { get; set; } = new();
    [JsonPropertyName("l")]
    public List<string> Low { get; set; } = new();
    [JsonPropertyName("h")]
    public List<string> High { get; set; } = new();
    [JsonPropertyName("o")]
    public string Opening { get; set; } = null!;

    public TickerInfo Parse()
    {
        return new TickerInfo
        {
            Ask = new AskTickerInfo
            {
                Price = double.Parse(Ask[0]),
                WholeLotVolume = int.Parse(Ask[1]),
                LotVolume = double.Parse(Ask[2])
            },
            Bid = new BidTickerInfo
            {
                Price = double.Parse(Bid[0]),
                WholeLotVolume = int.Parse(Bid[1]),
                LotVolume = double.Parse(Bid[2])
            },
            LastTrade = new LastTradeTickerInfo
            {
                Price = double.Parse(LastTrade[0]),
                LotVolume = double.Parse(LastTrade[1])
            },
            Volume = new VolumeTickerInfo
            {
                Today = double.Parse(Volume[0]),
                Last24Hours = double.Parse(Volume[1])
            },
            VolumeWeightedAveragePrice = new VolumeWeightedAveragePriceTickerInfo
            {
                Today = double.Parse(VolumeWeighted[0]),
                Last24Hours = double.Parse(VolumeWeighted[1])
            },
            NumberOfTrades = new NumberOfTradesTickerInfo
            {
                Today = NumberOfTrades[0],
                Last24Hours = NumberOfTrades[1]
            },
            Low = new LowTickerInfo
            {
                Today = double.Parse(Low[0]),
                Last24Hours = double.Parse(Low[1])
            },
            High = new HighTickerInfo
            {
                Today = double.Parse(High[0]),
                Last24Hours = double.Parse(High[1])
            },
            OpeningPrice = double.Parse(Opening)
        };
    }
}

public record TickerInfo
{
    public required AskTickerInfo Ask { get; set; }
    public required BidTickerInfo Bid { get; set; }
    public required LastTradeTickerInfo LastTrade { get; set; }
    public required VolumeTickerInfo Volume { get; set; }
    public required VolumeWeightedAveragePriceTickerInfo VolumeWeightedAveragePrice { get; set; }
    public required NumberOfTradesTickerInfo NumberOfTrades { get; set; }
    public required LowTickerInfo Low { get; set; }
    public required HighTickerInfo High { get; set; }
    public required double OpeningPrice { get; set; }
}

public record AskTickerInfo
{
    public double Price { get; set; }
    public int WholeLotVolume { get; set; }
    public double LotVolume { get; set; }
}

public record BidTickerInfo
{
    public double Price { get; set; }
    public int WholeLotVolume { get; set; }
    public double LotVolume { get; set; }
}

public record LastTradeTickerInfo
{
    public double Price { get; set; }
    public double LotVolume { get; set; }
}

public record VolumeTickerInfo
{
    public double Today { get; set; }
    public double Last24Hours { get; set; }
}

public record VolumeWeightedAveragePriceTickerInfo
{
    public double Today { get; set; }
    public double Last24Hours { get; set; }
}

public record NumberOfTradesTickerInfo
{
    public int Today { get; set; }
    public int Last24Hours { get; set; }
}

public record LowTickerInfo
{
    public double Today { get; set; }
    public double Last24Hours { get; set; }
}

public record HighTickerInfo
{
    public double Today { get; set; }
    public double Last24Hours { get; set; }
}

