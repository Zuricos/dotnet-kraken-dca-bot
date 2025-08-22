using System.Text.Json.Serialization;

namespace Kbot.Common.Dtos;

public record ApiResponse<T>
{
  [JsonPropertyName("error")]
  public List<string> Error { get; set; } = new();

  [JsonPropertyName("result")]
  public T? Result { get; set; }
}
