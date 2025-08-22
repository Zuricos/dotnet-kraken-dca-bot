using System.Text.Json.Serialization;

namespace Kbot.Common.Dtos;

public record ClosedOrderUnparsed
{
  [JsonPropertyName("closed")]
  public Dictionary<string, ClosedOrderInfoUnparsed> ClosedOrders { get; init; } = [];

  [JsonPropertyName("count")]
  public int Count { get; init; }

  public ClosedOrders ToModel()
  {
    var closedOrders = new List<ClosedOrderInfo>();
    foreach (var closedOrder in ClosedOrders)
    {
      closedOrders.Add(
        new ClosedOrderInfo
        {
          OrderId = closedOrder.Key,
          ClientOrderId = closedOrder.Value.ClientOrderId,
          Status = closedOrder.Value.Status,
          CloseTimeStamp = closedOrder.Value.CloseTimeStamp,
          Pair = closedOrder.Value.Description.Pair,
          Type = closedOrder.Value.Description.Type,
          OrderType = closedOrder.Value.Description.OrderType,
          Price = double.Parse(closedOrder.Value.Description.Price),
          Volume = double.Parse(closedOrder.Value.Volume),
          Cost = double.Parse(closedOrder.Value.Cost),
          Fee = double.Parse(closedOrder.Value.Fee),
        }
      );
    }
    return new ClosedOrders { Count = Count, Closed = closedOrders };
  }
}

public record ClosedOrderInfoUnparsed
{
  [JsonPropertyName("cl_ord_id")]
  public string? ClientOrderId { get; init; }

  [JsonPropertyName("closetm")]
  public required double CloseTimeStamp { get; init; }

  [JsonPropertyName("status")]
  public required string Status { get; init; }

  [JsonPropertyName("vol")]
  public required string Volume { get; init; }

  [JsonPropertyName("cost")]
  public required string Cost { get; init; }

  [JsonPropertyName("fee")]
  public required string Fee { get; init; }

  [JsonPropertyName("descr")]
  public ClosedOrderDescription Description { get; init; } = null!;
}

public record ClosedOrderDescription
{
  [JsonPropertyName("pair")]
  public required string Pair { get; init; }

  [JsonPropertyName("type")]
  public required string Type { get; init; }

  [JsonPropertyName("ordertype")]
  public required string OrderType { get; init; }

  [JsonPropertyName("price")]
  public required string Price { get; init; }
}

public record ClosedOrders
{
  public int Count { get; init; }
  public List<ClosedOrderInfo> Closed { get; init; } = [];
}

public record ClosedOrderInfo
{
  public required string OrderId { get; init; }
  public string? ClientOrderId { get; init; }
  public required string Status { get; init; }
  public required double CloseTimeStamp { get; init; }
  public required string Pair { get; init; }
  public required string Type { get; init; }
  public required string OrderType { get; init; }
  public required double Price { get; init; }
  public required double Volume { get; init; }
  public required double Cost { get; init; }
  public required double Fee { get; init; }
}
