using Kbot.Common.Api;
using Kbot.Common.Conversion;
using Kbot.Common.Models;
using Kbot.MailService.Database;
using Kbot.MailService.Models;
using Microsoft.EntityFrameworkCore;

namespace Kbot.MailService.Utility;

public class OrderService(
  IDbContextFactory<KrakenDbContext> dbContextFactory,
  KrakenClient krakenClient
)
{
  public async Task<int> FetchClosedOrdersAndSave(DateTimeOffset? startDateOverride = null)
  {
    await using var dbContext = dbContextFactory.CreateDbContext();
    var lastOrder = dbContext.Orders.OrderByDescending(o => o.CloseTimeStamp).FirstOrDefault();
    var startDate = startDateOverride ?? lastOrder?.CloseTimeStamp ?? DateTimeOffset.MinValue;

    var closedOrders = await krakenClient.GetClosedOrders(
      fetchAll: true,
      start: startDate,
      end: DateTime.UtcNow
    );
    if (closedOrders == null)
      return 0;

    var orders = dbContext.Orders.ToList();
    var newOrders = closedOrders
      .Closed.Where(o => !orders.Any(oo => oo.OrderId == o.OrderId))
      .ToList();
    if (newOrders.Count == 0)
      return 0;

    dbContext.Orders.AddRange(newOrders.Select(o => o.ToOrder()));
    return await dbContext.SaveChangesAsync();
  }

  public async Task<List<Order>> QueryClosedOrdersFromDatabase(ClosedOrderQuery query)
  {
    await using var dbContext = dbContextFactory.CreateDbContext();
    var orders = await dbContext
      .Orders.Where(o =>
        !query.StartDate.HasValue
        || query.StartDate.HasValue && o.CloseTimeStamp >= query.StartDate.Value
      )
      .Where(o =>
        !query.EndDate.HasValue || query.EndDate.HasValue && o.CloseTimeStamp < query.EndDate.Value
      )
      .Where(o =>
        !query.BuyOrSell.HasValue || query.BuyOrSell.HasValue && o.Type == query.BuyOrSell.Value
      )
      .Where(o =>
        !query.OrderType.HasValue
        || query.OrderType.HasValue && o.OrderType == query.OrderType.Value
      )
      .Where(o =>
        !query.OrderStatus.HasValue
        || query.OrderStatus.HasValue && o.Status == query.OrderStatus.Value
      )
      .Where(o => query.Pair == null || query.Pair != null && o.Pair == query.Pair)
      .OrderByDescending(o => o.CloseTimeStamp)
      .ToListAsync();
    return orders;
  }

  public async Task<List<AggregatedOrder>> GetReportPerDayCalc(ClosedOrderQuery query)
  {
    var orders = await QueryClosedOrdersFromDatabase(query);
    var grouped = orders
      .GroupBy(o => o.Pair)
      .SelectMany(g =>
        g.GroupBy(o => o.Type)
          .SelectMany(g =>
            g.OrderBy(o => o.CloseTimeStamp ?? DateTimeOffset.MinValue)
              .GroupBy(o =>
                o.CloseTimeStamp.HasValue ? o.CloseTimeStamp.Value.Date : DateTimeOffset.MinValue
              )
          )
          .Aggregate<IGrouping<DateTimeOffset, Order>, List<AggregatedOrder>>(
            [],
            (list, group) =>
            {
              var aggregatedOrder = new AggregatedOrder()
              {
                Date = group.Key,
                Price = group.Sum(o => o.Cost) / group.Sum(o => o.Volume),
                Volume = group.Sum(o => o.Volume),
                Pair = group.First().Pair,
                Fee = group.Sum(o => o.Fee),
                OrderType = group.First().Type.ToString(),
              };
              list.Add(aggregatedOrder);
              return list;
            }
          )
      )
      .ToList();
    return grouped;
  }
}
