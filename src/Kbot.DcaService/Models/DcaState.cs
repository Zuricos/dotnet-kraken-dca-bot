using System.Text.Json;

namespace Kbot.DcaService.Models;

public record DcaState
{
  public DateTime LastInvestmentTime { get; init; }
  public DateTime NextTopUpTime { get; init; }
  public TimeSpan TimeUntilNextTopUp { get; init; }
}
