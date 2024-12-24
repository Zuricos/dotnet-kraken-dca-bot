using Kbot.Common.Api;
using Kbot.Common.Conversion;
using Kbot.Common.Models;
using Kbot.MailService.Database;
using Kbot.MailService.Models;
using Microsoft.EntityFrameworkCore;

namespace Kbot.MailService.Utility;

public class OrderService(IDbContextFactory<KrakenDbContext> dbContextFactory, KrakenClient krakenClient)
{
    public async Task<int> FetchClosedOrdersAndSave()
    {
        await using var dbContext = dbContextFactory.CreateDbContext();
        var lastOrder = dbContext.Orders.OrderByDescending(o => o.CloseTimeStamp).FirstOrDefault();
        var startDate = lastOrder?.CloseTimeStamp ?? DateTimeOffset.MinValue;

        var closedOrders = await krakenClient.GetClosedOrders(fetchAll: true, start: startDate, end: DateTime.UtcNow);
        if (closedOrders == null) return 0;

        var orders = dbContext.Orders.ToList();
        var newOrders = closedOrders.Closed.Where(o => !orders.Any(oo => oo.OrderId == o.OrderId)).ToList();
        if (newOrders.Count == 0) return 0;

        dbContext.Orders.AddRange(newOrders.Select(o => o.ToOrder()));
        return await dbContext.SaveChangesAsync();
    }

    public async Task<List<Order>> QueryClosedOrdersFromDatabase(ClosedOrderQuery query)
    {
        await using var dbContext = dbContextFactory.CreateDbContext();
        var orders = await dbContext.Orders
            .Where(o => !query.StartDate.HasValue || query.StartDate.HasValue && o.CloseTimeStamp >= query.StartDate.Value)
            .Where(o => !query.EndDate.HasValue || query.EndDate.HasValue && o.CloseTimeStamp <= query.EndDate.Value)
            .Where(o => !query.BuyOrSell.HasValue || query.BuyOrSell.HasValue && o.Type == query.BuyOrSell.Value)
            .Where(o => !query.OrderType.HasValue || query.OrderType.HasValue && o.OrderType == query.OrderType.Value)
            .Where(o => !query.OrderStatus.HasValue || query.OrderStatus.HasValue && o.Status == query.OrderStatus.Value)
            .Where(o => query.Pair == null || query.Pair != null && o.Pair == query.Pair).ToListAsync();
        return orders;
    }
}
