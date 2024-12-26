using Kbot.Common.Enums;

namespace Kbot.Common.Models;

public class Order
{
    public required string OrderId { get; set; }
    public string? ClientOrderId { get; set; }
    public required OrderStatus Status { get; set; }
    public DateTimeOffset? CloseTimeStamp { get; set; }
    public required string Pair { get; set; }
    public required BuyOrSell Type { get; set; }
    public required OrderType OrderType { get; set; }
    public required double Price { get; set; }
    public required double Volume { get; set; }
    public required double Cost { get; set; }
    public required double Fee { get; set; }
}
