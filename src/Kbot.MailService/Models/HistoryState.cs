using System.Text.Json;

namespace Kbot.MailService.Models;

public record HistoryState
{
  public DateTimeOffset LastReportedOrderTimeStamp { get; init; }

  public static HistoryState Load()
  {
    if (File.Exists("state/history.json"))
    {
      var json = File.ReadAllText("state/history.json");
      return JsonSerializer.Deserialize<HistoryState>(json)
        ?? throw new Exception("Failed to load state");
    }
    else
    {
      return new HistoryState { LastReportedOrderTimeStamp = DateTimeOffset.MinValue };
    }
  }

  public void Save()
  {
    var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText("state/history.json", json);
  }
}
