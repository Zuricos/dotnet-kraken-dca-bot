using System.Reflection;
using Kbot.Common.Api;
using Kbot.Common.Dtos;
using Kbot.Common.Enums;
using Kbot.Common.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kbot.Common.Test;

[TestClass]
public class ApiTestPublic
{
  private IServiceProvider _serviceProvider = null!;

  [TestInitialize]
  public void Setup()
  {
    var builder = Host.CreateApplicationBuilder();
    builder.Environment.EnvironmentName = "Development";

    builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly()).AddUserSecrets<Secrets>();

    builder.Logging.AddConsole();
    builder
      .Services.Configure<Secrets>(builder.Configuration.GetSection(nameof(Secrets)))
      .AddTransient<KrakenApi>()
      .AddTransient<KrakenClient>();

    var host = builder.Build();
    _serviceProvider = host.Services;
    host.Start();
  }

  [TestMethod]
  public async Task TestQueryTicker()
  {
    var client = _serviceProvider.GetRequiredService<KrakenClient>();
    var result = await client.GetCurrentCryptoPrice();
    Assert.IsTrue(result > 0, result.ToString());
  }

  [TestMethod]
  public async Task TestQueryBalance()
  {
    var client = _serviceProvider.GetRequiredService<KrakenClient>();
    var result = await client.CheckBalance();
    Assert.IsNotNull(result, "Result is null");
    Assert.IsTrue(result["CHF"] > 0, result.ToString());
    Assert.IsTrue(result["XXBT"] > 0, result.ToString());
  }

  [TestMethod]
  public async Task TestSendAndCancelBuyOrder()
  {
    var client = _serviceProvider.GetRequiredService<KrakenClient>();
    var currentPrice = await client.GetCurrentCryptoPrice("XBTCHF");
    var cl_ord_id = $"test-{DateTime.UtcNow:yyyyMMddHHmm}";
    var orderData = new OrderRequest
    {
      Pair = "XBTCHF",
      Type = BuyOrSell.Buy,
      Price = currentPrice / 2,
      Volume = 0.00005,
      OrderId = cl_ord_id,
      OrderType = OrderType.Limit,
    };

    var result = await client.SendOrder(orderData);

    var cancelResult = await client.CancelOrder(cl_ord_id, null);

    Assert.IsTrue(result, "Order was not successfully sent");
    Assert.IsTrue(
      cancelResult,
      "Order was not successfully cancelled. Manual Cancelation required!."
    );
  }

  [TestMethod]
  public async Task TestQueryClosedOrders()
  {
    var client = _serviceProvider.GetRequiredService<KrakenClient>();
    var result = await client.GetClosedOrders(fetchAll: false);
    Assert.IsNotNull(result, "Result is null");
    Assert.IsTrue(result.Count > 0, "No orders found");
    Assert.AreEqual(50, result.Count, "Expected 50 orders");
  }

  [TestMethod]
  public async Task TestQueryAllClosedOrders()
  {
    var client = _serviceProvider.GetRequiredService<KrakenClient>();
    var result = await client.GetClosedOrders(fetchAll: true);
    Assert.IsNotNull(result, "Result is null");
    Assert.IsTrue(result.Closed.Count > 0, "No orders found");
    Assert.AreEqual(result.Closed.Count, result.Count, "No orders found");
  }
}
