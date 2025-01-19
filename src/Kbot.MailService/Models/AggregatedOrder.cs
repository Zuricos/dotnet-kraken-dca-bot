namespace Kbot.MailService.Models;

public record AggregatedOrder{
    public DateTimeOffset Date {get; init;}
    public double Price {get; init;}
    public double Volume {get; init;}
    public string Pair {get; init;} = null!;
    public string OrderType {get; init;} = null!;
    public double Fee {get;init;}
}