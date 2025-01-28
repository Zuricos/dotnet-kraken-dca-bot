using Kbot.MailService.Options;
using Kbot.MailService.Utility;
using Microsoft.Extensions.Options;

namespace Kbot.MailService;

public class DailyReporter(ILogger<DailyReporter> logger, MonthlyReporter monthlyReporter, MailSenderService mailSender, IOptions<MailOptions> mailOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker running at: {time}", DateTimeOffset.UtcNow);
        await mailSender.WelcomeOrRestartMessage();
        logger.LogInformation("Send Mail as startup to verify the service is running and works.");
        await monthlyReporter.SendReportOnStartup();
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRunTime = new DateTime(now.Year, now.Month, now.Day, mailOptions.Value.HourOfDay, 0, 0); // 6 AM today
            if (now >= nextRunTime) nextRunTime = nextRunTime.AddDays(1);

            var delay = nextRunTime - now;
            logger.LogInformation("Next run time: {time}", nextRunTime);
            await Task.Delay(delay, stoppingToken);

            await SendDailyMail();
            await monthlyReporter.SendReportAsync();
        }
    }

    private async Task SendDailyMail()
    {
        try
        {
            await mailSender.SendMailWithClosedOrdersLast24Hours();
            logger.LogInformation("Send Daily Mail.");
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred while sending daily mail.");
        }
    }
}

