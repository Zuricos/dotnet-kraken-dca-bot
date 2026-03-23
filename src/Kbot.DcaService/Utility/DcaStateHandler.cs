using System.Text.Json;
using Kbot.DcaService.Models;

namespace Kbot.DcaService.Utility;

public static class DcaStateHandler
{
  private const string StateFilePath = "state/state.json";
  private static readonly JsonSerializerOptions JsonSerializerOptions = new()
  {
    WriteIndented = true,
  };

  public static DcaState Load()
  {
    if (File.Exists(StateFilePath))
    {
      try
      {
        var json = File.ReadAllText(StateFilePath);
        return JsonSerializer.Deserialize<DcaState>(json, JsonSerializerOptions)
          ?? throw new Exception("Failed to load state");
      }
      catch
      {
        // If error try to delete file and write new one
        File.Delete(StateFilePath);
        return new DcaState
        {
          LastInvestmentTime = DateTime.MinValue,
          NextTopUpTime = DateTime.MinValue,
          TimeUntilNextTopUp = TimeSpan.Zero,
        };
      }
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
      File.WriteAllText(StateFilePath, json);
    }
    catch
    {
      // If error try to delete file and write new one
      File.Delete(StateFilePath);
      File.WriteAllText(StateFilePath, json);
    }
  }
}
