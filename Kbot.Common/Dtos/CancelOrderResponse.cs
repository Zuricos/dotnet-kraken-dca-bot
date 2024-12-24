using System.Text.Json.Serialization;

namespace Kbot.Common.Dtos;

public record CancelOrderResponse
{
    [JsonPropertyName("count")]
    public int Count { get; init; }
    [JsonPropertyName("pending")]
    public bool? Pending { get; init; }
}
