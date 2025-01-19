using System.Globalization;
using System.Text;
using Kbot.Common.Models;
using Kbot.MailService.Options;

namespace Kbot.MailService.Utility;

public class HtmlService
{
    private const long SATS = 100_000_000;

    public static string GenerateDailyWelcome(List<Order> orders, MailOptions mailOptions)
    {
        var investments = orders.Sum(o => o.Volume);
        var totalCosts = orders.Sum(o => o.Cost);

        if (mailOptions.Crypto.Equals("BTC"))
            return $"<div style='text-align: center; font-size: 18px; margin: 20px 0;'>Hi,<br>I've bought in the last 24 hours for you: {investments * SATS:N0} sats for {totalCosts:F2} {mailOptions.Fiat}.</div>";
        else
            return $"<div style='text-align: center; font-size: 18px; margin: 20px 0;'>Hi,<br>I've bought in the last 24 hours for you: {investments:N0} {mailOptions.Crypto} for {totalCosts:F2} {mailOptions.Fiat}.</div>";
    }

    public static string GenerateDailySummary(List<Order> orders, double lastAveragePrice, MailOptions mailOptions)
    {
        var investments = orders.Sum(o => o.Volume);
        var totalCosts = orders.Sum(o => o.Cost);
        var totalFees = orders.Sum(o => o.Fee);
        var averagePrice = totalCosts / investments;
        var priceIncrease = averagePrice / lastAveragePrice * 100 - 100;

        return $"<div style='text-align: center; font-size: 16px; margin-top: 30px;'>Summary:<br>Investments: {investments:F8} {mailOptions.Crypto}<br>Total Costs: {totalCosts:F2} {mailOptions.Fiat}<br>Total Fees: {totalFees:F2} {mailOptions.Fiat}<br>Average Price: {averagePrice:F2} {mailOptions.Fiat}<br>Price Increase: {priceIncrease:F2}%</div>";
    }

    public static string GenerateGreetings()
    {
        return "<div style='text-align: center; font-size: 16px; margin-top: 30px;'>Greetings, your DCA - BOT</div>";
    }

    public static string GenerateDailyTableFromOrders(List<Order> orders, MailOptions mailOptions)
    {
        var sb = new StringBuilder();
        sb.Append("<table style='width: 100%; margin: 20px 0; border-collapse: collapse;' class='table table-striped table-bordered'>");
        sb.Append("<thead><tr style='background-color: #f2f2f2; color: #333;'>");
        sb.Append("<th style='padding: 12px; text-align: center; border: 1px solid #ddd;'>Order ID</th>");
        sb.Append("<th style='padding: 12px; text-align: center; border: 1px solid #ddd;'>Executed Time UTC</th>");
        sb.Append("<th style='padding: 12px; text-align: center; border: 1px solid #ddd;'>Price</th>");
        if (mailOptions.Crypto.Equals("BTC"))
            sb.Append($"<th style='padding: 12px; text-align: center; border: 1px solid #ddd;'>Amount (Sats)</th>");
        else
            sb.Append($"<th style='padding: 12px; text-align: center; border: 1px solid #ddd;'>Amount ({mailOptions.Crypto})</th>");
        sb.Append($"<th style='padding: 12px; text-align: center; border: 1px solid #ddd;'>Cost ({mailOptions.Fiat})</th>");
        sb.Append($"<th style='padding: 12px; text-align: center; border: 1px solid #ddd;'>Fee ({mailOptions.Fiat})</th>");
        sb.Append("</tr></thead><tbody>");

        // Add rows
        for (int i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            sb.Append("<tr style='background-color: " + (i % 2 == 0 ? "#f9f9f9" : "#ffffff") + ";'>");
            sb.Append($"<td style='padding: 12px; text-align: center; border: 1px solid #ddd;'>{order.OrderId}</td>");
            sb.Append($"<td style='padding: 12px; text-align: center; border: 1px solid #ddd;'>{order.CloseTimeStamp?.ToString("u", CultureInfo.InvariantCulture)}</td>");
            sb.Append($"<td style='padding: 12px; text-align: center; border: 1px solid #ddd;'>{order.Price:F2}</td>");
            if (mailOptions.Crypto.Equals("BTC"))
                sb.Append($"<td style='padding: 12px; text-align: center; border: 1px solid #ddd;'>{order.Volume * SATS:N0}</td>");
            else
                sb.Append($"<td style='padding: 12px; text-align: center; border: 1px solid #ddd;'>{order.Volume:F6}</td>");
            sb.Append($"<td style='padding: 12px; text-align: center; border: 1px solid #ddd;'>{order.Cost:F2}</td>");
            sb.Append($"<td style='padding: 12px; text-align: center; border: 1px solid #ddd;'>{order.Fee:F2}</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    public static string GenerateReportWelcome(DateTimeOffset start, DateTimeOffset end)
    {
        return $"""
            <div style='text-align: center; font-size: 16px; margin-top: 30px;'>
                Hi there, <br/><br/>

                In the attachments you will find the report of your DCA bot. <br/>
                The report contains all closed orders from {start.ToString("u", CultureInfo.InvariantCulture)} to {end.ToString("u", CultureInfo.InvariantCulture)}. <br/><br/>
            </div>";
        """;
    }
}
