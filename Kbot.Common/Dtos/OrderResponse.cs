using System.Text.Json.Serialization;

namespace Kbot.Common.Dtos;

public record OrderResponse
{
    [JsonPropertyName("descr")]
    public OrderDescription Description { get; init; } = null!;
    [JsonPropertyName("txid")]
    public List<string> TransactionIds { get; init; } = new();
}

public record OrderDescription
{
    [JsonPropertyName("order")]
    public string Order { get; init; } = null!;
}
