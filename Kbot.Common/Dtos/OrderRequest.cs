using Kbot.Common.Enums;

namespace Kbot.Common.Dtos;

public record OrderRequest
{
    public OrderType OrderType { get; init; }
    public BuyOrSell Type { get; init; }
    public required string Pair { get; init; }
    public double Volume { get; init; }
    public double? Price { get; init; }
    public string? OrderId { get; init; }

    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>
        {
            { "pair", Pair },
            { "type", Type.ToString().ToLower() },
            { "ordertype", OrderType.ToString().ToLower() },
            { "volume", Volume }
        };

        if (Price.HasValue)
        {
            dict.Add("price", Math.Round(Price.Value, 1));
        }

        if (!string.IsNullOrEmpty(OrderId))
        {
            if (OrderId.Length > 18)
            {
                dict.Add("cl_ord_id", OrderId[..18]);
            }
            else
            {
                dict.Add("cl_ord_id", OrderId);
            }
        }

        return dict;
    }
}
