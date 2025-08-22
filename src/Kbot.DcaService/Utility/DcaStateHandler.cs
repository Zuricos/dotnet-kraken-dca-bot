using System.Text.Json;
using Kbot.DcaService.Models;

namespace Kbot.DcaService.Utility;

public static class DcaStateHandler
{
  private static readonly JsonSerializerOptions JsonSerializerOptions = new()
  {
    WriteIndented = true,
  };

  public static DcaState Load()
  {
    if (File.Exists("state/state.json"))
    {
      var json = File.ReadAllText("state/state.json");
      return JsonSerializer.Deserialize<DcaState>(json, JsonSerializerOptions)
        ?? throw new Exception("Failed to load state");
    }
    else
    {
      return new DcaState
      {
        LastInvestmentTime = DateTime.MinValue,
        NextTopUpTime = DateTime.MinValue,
        TimeUntilNextTopUp = TimeSpan.Zero,
      };
    }
  }

  public static void Save(this DcaState dcaState)
  {
    var json = JsonSerializer.Serialize(dcaState, JsonSerializerOptions);
    try
    {
      File.WriteAllText("state/state.json", json);
    }
    catch
    {
      // If error try to delete file and write new one
      File.Delete("state/state.json");
      File.WriteAllText("state/state.json", json);
    }
  }
}
