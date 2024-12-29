using System.Net;
using System.Net.Mail;
using Kbot.Common.Enums;
using Kbot.Common.Models;
using Kbot.MailService.Models;
using Kbot.MailService.Options;
using Microsoft.Extensions.Options;

namespace Kbot.MailService.Utility;

public class MailSenderService(
    ILogger<MailSenderService> logger,
    OrderService orderService,
    IOptions<MailOptions> mailOptions,
    IOptions<MailSecrets> mailSecrets)
{
    public async Task SendMailWithClosedOrdersLast24Hours()
    {
        await orderService.FetchClosedOrdersAndSave();
        var orders = await GetClosedOrdersLast24Hours();

        if (orders.Count == 0)
        {
            logger.LogInformation("No closed orders in the last 24 hours.");
            var htmlNoOrds = """
            <div style='text-align: center; font-size: 18px; margin: 20px 0;'>
                Hi there, <br/>
                I'd looks like the dca bot didn't bought any crypto in the last 24 hours. <br/>
                Maybe you don't have enough funds, there is an error with the dca bot
                or with the mail bot accessing the database or the kraken api.</div>

                Please check the logs for more information.
                And if you found an error which you can't solve with proper configuration, reach out to me via a github issue:
                <a href='https://github.com/Zuricos/dotnet-kraken-dca-bot/issues'>

                Greetings, Zuricos. Creator of the dotnet-kraken-dca-bot.
            """;
            await SendMail(htmlNoOrds);
            return;
        }

        var lastAveragePrice = await GetLastAveragePrice();
        var htmlW = HtmlService.GenerateWelcome(orders, mailOptions.Value);
        var htmlS = HtmlService.GenerateSummary(orders, lastAveragePrice, mailOptions.Value);
        var htmlT = HtmlService.GenerateTableFromOrders(orders, mailOptions.Value);
        var htmlG = HtmlService.GenerateGreetings();

        var html = htmlW + htmlS + htmlT + htmlG;
        logger.LogInformation("Sending mail with closed orders last 24 hours.");
        await SendMail(html);
    }

    public async Task SendMail(string html)
    {
        var secrets = mailSecrets.Value;
        var mail = new MailMessage
        {
            From = new MailAddress(secrets.SenderEmail),
            Subject = "Daily DCA Report",
            Body = html,
            IsBodyHtml = true,
            To = { secrets.ReceiverEmail }
        };

        using var smtp = new SmtpClient("smtp.gmail.com", 587)
        {
            Credentials = new NetworkCredential(secrets.SenderEmail, secrets.GoogleAppPassword),
            EnableSsl = true
        };
        try
        {
            await smtp.SendMailAsync(mail);
            logger.LogInformation("Mail sent.");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error sending mail.");
        }
    }

    private async Task<List<Order>> GetClosedOrdersLast24Hours()
    {
        var orders = await orderService.QueryClosedOrdersFromDatabase(new ClosedOrderQuery
        {
            StartDate = DateTimeOffset.UtcNow.AddDays(-1),
            EndDate = DateTimeOffset.UtcNow,
            OrderStatus = OrderStatus.Closed,
            BuyOrSell = BuyOrSell.Buy,
            Pair = mailOptions.Value.CryptoPair
        });
        return orders;
    }

    private async Task<double> GetLastAveragePrice()
    {
        var orders = await orderService.QueryClosedOrdersFromDatabase(new ClosedOrderQuery
        {
            StartDate = DateTimeOffset.UtcNow.AddDays(-2),
            EndDate = DateTimeOffset.UtcNow.AddDays(-1),
            OrderStatus = OrderStatus.Closed,
            BuyOrSell = BuyOrSell.Buy,
            Pair = mailOptions.Value.CryptoPair
        });
        return orders.Sum(o => o.Cost) / orders.Sum(o => o.Volume);
    }
}
