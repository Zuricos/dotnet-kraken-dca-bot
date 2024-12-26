using Kbot.Common.Dtos;
using Kbot.Common.Enums;
using Kbot.Common.Models;

namespace Kbot.Common.Conversion;

public static class OrderParser
{
    public static Order ToOrder(this ClosedOrderInfo info)
    {
        return new Order
        {
            OrderId = info.OrderId,
            ClientOrderId = info.ClientOrderId,
            Status = info.Status switch
            {
                "pending" => OrderStatus.Pending,
                "open" => OrderStatus.Open,
                "closed" => OrderStatus.Closed,
                "canceled" => OrderStatus.Canceled,
                "expired" => OrderStatus.Expired,
                _ => throw new ArgumentOutOfRangeException()
            },
            CloseTimeStamp = DateTimeOffset.FromUnixTimeSeconds((long)info.CloseTimeStamp),
            Pair = info.Pair,
            Type = info.Type == "buy" ? BuyOrSell.Buy : BuyOrSell.Sell,
            OrderType = info.OrderType == "limit" ? OrderType.Limit : OrderType.Market,
            Price = info.Price,
            Volume = info.Volume,
            Cost = info.Cost,
            Fee = info.Fee
        };
    }
}
