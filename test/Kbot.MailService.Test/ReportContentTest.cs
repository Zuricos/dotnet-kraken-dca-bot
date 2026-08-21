using Kbot.Common.Enums;
using Kbot.Common.Models;
using Kbot.MailService.Models;
using Kbot.MailService.Options;
using Kbot.MailService.Utility;

namespace Kbot.MailService.Test;

/// <summary>
/// Covers what <see cref="MailGenerateTest"/> only ever proved by sending a real mail: that the
/// report bodies and the csv attachment are built from the orders they are given. Assertions stay
/// on structure and content rather than number formatting, which is culture-dependent until P2-02.
/// </summary>
[TestClass]
public class ReportContentTest
{
  private static readonly MailOptions Options = new()
  {
    CryptoPair = "XXBTZCHF",
    Crypto = "BTC",
    Fiat = "CHF",
    HistoryStartDate = "2024-01-01",
  };

  private static List<Order> TwoOrders() =>
    [
      NewOrder("O-FIRST", new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero)),
      NewOrder("O-SECOND", new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero)),
    ];

  [TestMethod]
  public void GenerateDailyWelcome_NamesTheAmountBoughtAndTheCurrencies()
  {
    var html = HtmlService.GenerateDailyWelcome(TwoOrders(), Options);

    StringAssert.Contains(html, "last 24 hours");
    StringAssert.Contains(html, "sats", "A BTC report reports the volume in sats.");
    StringAssert.Contains(html, "CHF");
  }

  [TestMethod]
  public void GenerateDailyWelcome_ReportsAnAltcoinInItsOwnUnit()
  {
    var html = HtmlService.GenerateDailyWelcome(TwoOrders(), Options with { Crypto = "ETH" });

    StringAssert.Contains(html, "ETH");
    Assert.IsFalse(html.Contains("sats"), "Only BTC is reported in sats.");
  }

  [TestMethod]
  public void GenerateDailyTableFromOrders_EmitsOneRowPerOrder()
  {
    var orders = TwoOrders();

    var html = HtmlService.GenerateDailyTableFromOrders(orders, Options);

    Assert.AreEqual(
      orders.Count + 1,
      html.Split("<tr").Length - 1,
      "Expected a header row plus one row per order."
    );
    foreach (var order in orders)
    {
      StringAssert.Contains(html, order.OrderId);
    }
    StringAssert.Contains(html, "2026-08-20 06:00:00Z", "Timestamps are written in UTC.");
  }

  [TestMethod]
  public void GenerateDailySummary_ListsEverySummaryLine()
  {
    var html = HtmlService.GenerateDailySummary(TwoOrders(), lastAveragePrice: 49000, Options);

    string[] labels =
    [
      "Investments",
      "Total Costs",
      "Total Fees",
      "Average Price",
      "Price Increase",
    ];
    foreach (var label in labels)
    {
      StringAssert.Contains(html, label);
    }
  }

  [TestMethod]
  public void ToCsv_WritesAHeaderAndOneLinePerAggregatedOrder()
  {
    List<AggregatedOrder> aggregated =
    [
      new()
      {
        Date = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
        Price = 50000,
        Volume = 0.001,
        Pair = "XBTCHF",
        OrderType = "limit",
        Fee = 0.25,
      },
      new()
      {
        Date = new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero),
        Price = 51000,
        Volume = 0.002,
        Pair = "ETHCHF",
        OrderType = "limit",
        Fee = 0.5,
      },
    ];

    using var reader = new StreamReader(aggregated.ToCsv());
    var lines = reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    Assert.AreEqual("Date,Volume,Price,Crypto,Fiat,OrderType,Fee", lines[0]);
    Assert.AreEqual(aggregated.Count + 1, lines.Length);
    Assert.AreEqual(7, lines[1].Split(',').Length);
    StringAssert.Contains(lines[1], "Bitcoin", "XBT is spelled out in the report.");
    StringAssert.Contains(lines[1], "CHF");
    StringAssert.Contains(lines[2], "ETH");
  }

  [TestMethod]
  public void ToCsv_RewindsTheStreamSoItCanBeAttached()
  {
    var csv = new List<AggregatedOrder>().ToCsv();

    Assert.AreEqual(0, csv.Position);
    Assert.IsGreaterThan(0, csv.Length, "The header alone should have been written.");
  }

  private static Order NewOrder(string orderId, DateTimeOffset closedAt) =>
    new()
    {
      OrderId = orderId,
      Status = OrderStatus.Closed,
      CloseTimeStamp = closedAt,
      Pair = "XXBTZCHF",
      Type = BuyOrSell.Buy,
      OrderType = OrderType.Limit,
      Price = 50000,
      Volume = 0.001,
      Cost = 50,
      Fee = 0.13,
    };
}
