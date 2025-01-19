using System.Text;
using Kbot.MailService.Models;

namespace Kbot.MailService.Utility;

public static class CsvService
{
    public static MemoryStream ToCsv(this IEnumerable<AggregatedOrder> aggOrders)
    {
        var memoryStream = new MemoryStream();
        var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8);

        streamWriter.WriteLine("Date,Volume,Price,Crypto,Fiat,OrderType,Fee");

        foreach (var order in aggOrders)
        {
            var crypto = order.Pair[..^3];
            crypto = crypto == "XBT" ? "Bitcoin" : crypto;
            var fiat = order.Pair[^3..];
            streamWriter.WriteLine($"{order.Date},{order.Volume:F8},{order.Price:F1},{crypto},{fiat},{order.OrderType},{order.Fee:F4}");
        }

        streamWriter.Flush();
        memoryStream.Position = 0;
        return memoryStream;
    }
};