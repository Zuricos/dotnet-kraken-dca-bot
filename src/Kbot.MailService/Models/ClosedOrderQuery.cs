using Kbot.Common.Enums;

namespace Kbot.MailService.Models;

public record ClosedOrderQuery
{
  public BuyOrSell? BuyOrSell { get; set; }
  public OrderType? OrderType { get; set; }
  public OrderStatus? OrderStatus { get; set; }
  public DateTimeOffset? StartDate { get; set; }
  public DateTimeOffset? EndDate { get; set; }
  public string? Pair { get; set; }
}
