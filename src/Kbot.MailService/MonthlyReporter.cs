using Kbot.DcaService.Models;
using Kbot.MailService.Options;
using Kbot.MailService.Utility;
using Microsoft.Extensions.Options;

namespace Kbot.MailService;

public class MonthlyReporter(ILogger<MonthlyReporter> logger, MailSenderService mailSender, IOptions<MailOptions> mailOptions) : BackgroundService
{
    private HistoryState _state = null!;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker running at: {time}", DateTimeOffset.UtcNow);
        _state = HistoryState.Load();


        if (_state.LastReportedOrderTimeStamp == DateTimeOffset.MinValue)
        {
            _state = _state with { LastReportedOrderTimeStamp = mailOptions.Value.HistoryStartDateDto };
            await SendReport();
        }
        else
        {
            var last = _state.LastReportedOrderTimeStamp;
            var nextShould = new DateTimeOffset(last.Year, last.Month, mailOptions.Value.DayOfMonth, mailOptions.Value.HourOfDay, 0, 0, TimeSpan.Zero).AddMonths(1);
            if (DateTimeOffset.UtcNow > nextShould)
            {
                await SendReport();
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRunTime = new DateTime(now.Year, now.Month, mailOptions.Value.DayOfMonth, mailOptions.Value.HourOfDay, 0, 0);
            if (now >= nextRunTime) nextRunTime = nextRunTime.AddMonths(1);

            var delay = nextRunTime - now;
            logger.LogInformation("Next run time: {time}", nextRunTime);
            await Task.Delay(delay, stoppingToken);

            await SendReport();
        }
    }

    private async Task SendReport()
    {
        try
        {
            var lastReportedOrderTimeStamp = DateTimeOffset.UtcNow;
            await mailSender.SendReportMail(_state.LastReportedOrderTimeStamp);
            _state = _state with { LastReportedOrderTimeStamp = lastReportedOrderTimeStamp };
            _state.Save();
            logger.LogInformation("Send Report Mail.");
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred while sending daily mail.");
        }
    }
}

