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
        var orders = await GetClosedOrdersLast24Hours();
        var htmlW = HtmlService.GenerateWelcome(orders, mailOptions.Value);
        var htmlT = HtmlService.GenerateTableFromOrders(orders, mailOptions.Value);
        var htmlG = HtmlService.GenerateGreetings();

        var html = htmlW + htmlT + htmlG;
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
        await orderService.FetchClosedOrdersAndSave();
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
}
