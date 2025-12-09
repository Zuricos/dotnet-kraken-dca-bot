using Kbot.MailService.Models;
using Kbot.MailService.Options;
using Kbot.MailService.Utility;
using Microsoft.Extensions.Options;

namespace Kbot.MailService;

public class MonthlyReporter(
  ILogger<MonthlyReporter> logger,
  MailSenderService mailSender,
  IOptions<MailOptions> mailOptions
)
{
  private HistoryState _state = null!;

  public async Task SendReportOnStartup()
  {
    _state = HistoryState.Load();
    if (_state.LastReportedOrderTimeStamp == DateTimeOffset.MinValue)
    {
      _state = _state with { LastReportedOrderTimeStamp = mailOptions.Value.HistoryStartDateDto };
      await SendReport();
    }
    else
    {
      var last = _state.LastReportedOrderTimeStamp;
      var nextShould = new DateTimeOffset(
        last.Year,
        last.Month,
        mailOptions.Value.DayOfMonth,
        mailOptions.Value.HourOfDay,
        0,
        0,
        TimeSpan.Zero
      ).AddMonths(1);
      if (DateTimeOffset.UtcNow > nextShould)
      {
        await SendReport();
      }
    }
  }

  public Task SendReportAsync()
  {
    var utcNow = DateTimeOffset.UtcNow;
    if (utcNow.Day == mailOptions.Value.DayOfMonth)
    {
      return SendReport();
    }
    else
    {
      return Task.CompletedTask;
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
