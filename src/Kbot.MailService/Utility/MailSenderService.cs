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
    public async Task WelcomeOrRestartMessage(){
        await orderService.FetchClosedOrdersAndSave();
        var orders = await GetClosedOrdersLast24Hours();

         if (orders.Count == 0)
        {
            var htmlWelcome = """
            <div style='text-align: left; font-size: 18px; margin: 20px 0;'>
                Hi there, <br/><br/>
                I'd like to welcome you to the dca bot. <br/>
                With daily and monthly reports over mail. <br/><br/>
                The bot will send you a mail every day at 6 AM with the closed orders from the last 24 hours. <br/>
                And a monthly report on the first day of the month at 6 AM, with a csv file with per day aggregated orders. <br/><br/>

                Now, lay back and let the bot do the work for you. <br/><br/>
                Greetings, Zuricos.<br/>
                Creator of the dotnet-kraken-dca-bot.
            </div>
            """;
            await SendMail(htmlWelcome, "DCA - Welcome");
        }
        else{
            var htmlRestart = """
                <div style='text-align: left; font-size: 18px; margin: 20px 0;'>
                    Hi there, <br/><br/>
                    The dca bot has been restarted. <br/>
                    Greetings, Zuricos.<br/>
                    Creator of the dotnet-kraken-dca-bot.
                </div>
                """;
                await SendMail(htmlRestart, "DCA - Restart of Container");
        }
    }

    public async Task SendMailWithClosedOrdersLast24Hours()
    {
        await orderService.FetchClosedOrdersAndSave();
        var orders = await GetClosedOrdersLast24Hours();

        if (orders.Count == 0)
        {
            logger.LogInformation("No closed orders in the last 24 hours.");
            var htmlNoOrds = """
             <div style='text-align: left; font-size: 18px; margin: 20px 0;'>
                Hi there, <br/><br/>
                I'd looks like the dca bot didn't bought any crypto in the last 24 hours. <br/><br/>

                If its the first time you see this message, don't worry. If you just started the bot, it's normal. <br/>
                But if you see this message multiple times, something is wrong. <br/><br/>

                Maybe you don't have enough funds, there is an error with the dca bot
                or with the mail bot accessing the database or the kraken api.<br/><br/>

                Please check the logs for more information.
                And if you found an error which you can't solve with proper configuration, reach out to me via a github issue:
                <a href='https://github.com/Zuricos/dotnet-kraken-dca-bot/issues'>https://github.com/Zuricos/dotnet-kraken-dca-bot/issues</a><br/><br/>
                Greetings, Zuricos.<br/> 
                Creator of the dotnet-kraken-dca-bot.
            </div>
            """;
            await SendMail(htmlNoOrds, "DCA - No Orders");
            return;
        }

        var lastAveragePrice = await GetLastAveragePrice();
        var htmlW = HtmlService.GenerateDailyWelcome(orders, mailOptions.Value);
        var htmlS = HtmlService.GenerateDailySummary(orders, lastAveragePrice, mailOptions.Value);
        var htmlT = HtmlService.GenerateDailyTableFromOrders(orders, mailOptions.Value);
        var htmlG = HtmlService.GenerateGreetings();

        var html = htmlW + htmlS + htmlT + htmlG;
        logger.LogInformation("Sending mail with closed orders last 24 hours.");
        await SendMail(html, "DCA - Daily Report");
    }

    public async Task SendReportMail(DateTimeOffset startDate)
    {
        var endDate = DateTimeOffset.UtcNow;
        var query = new ClosedOrderQuery(){
            StartDate = startDate,
            EndDate = endDate,
            OrderStatus = OrderStatus.Closed,
        };
        var aggregatedOrders = await orderService.GetReportPerDayCalc(query);
        var htmlW = HtmlService.GenerateReportWelcome(startDate, endDate); 
        var htmlG = HtmlService.GenerateGreetings();
        var csvStream = aggregatedOrders.ToCsv();
        var html = htmlW  + htmlG;
        logger.LogInformation("Sending report mail.");
        await SendMail(html, "DCA - Monthly Report", csvStream, "csv");
    }

    public async Task SendMail(string html, string subject, MemoryStream? attachment = null, string? attachmentExt = null)
    {
        var secrets = mailSecrets.Value;
      
        var mail = new MailMessage
        {
            From = new MailAddress(secrets.SenderEmail),
            Subject = subject,
            Body = html,
            IsBodyHtml = true,
            To = { secrets.ReceiverEmail }
        };
        if (attachment is not null && !string.IsNullOrEmpty(attachmentExt))
        {
            attachment.Position = 0;
            var attachmentData = new Attachment(attachment, $"attachment.{attachmentExt}", "application/octet-stream");
            mail.Attachments.Add(attachmentData);
        }

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
            throw;
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
