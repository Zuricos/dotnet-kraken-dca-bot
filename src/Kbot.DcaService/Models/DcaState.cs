using System.Text.Json;

namespace Kbot.DcaService.Models;

public record DcaState
{
    public DateTime LastInvestmentTime { get; init; }
    public DateTime NextTopUpTime { get; init; }
    public TimeSpan TimeUntilNextTopUp { get; init; }

    public static DcaState Load()
    {
        if (File.Exists("state/state.json"))
        {
            var json = File.ReadAllText("state/state.json");
            return JsonSerializer.Deserialize<DcaState>(json) ?? throw new Exception("Failed to load state");
        }
        else
        {
            return new DcaState
            {
                LastInvestmentTime = DateTime.MinValue,
                NextTopUpTime = DateTime.MinValue,
                TimeUntilNextTopUp = TimeSpan.Zero
            };
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("state/state.json", json);
    }
}
