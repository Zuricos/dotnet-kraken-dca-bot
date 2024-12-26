namespace Kbot.DcaService.Options;

public record BalanceOptions
{
    public int DefaultTopupDayOfMonth { get; init; }
    public double ReserveFiat { get; init; }
}
