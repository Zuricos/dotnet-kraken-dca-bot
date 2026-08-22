using Kbot.Common.Helpers;
using Kbot.MailService.Options;
using Kbot.MailService.Utility;
using Microsoft.Extensions.Options;

namespace Kbot.MailService;

public class DailyReporter(
  ILogger<DailyReporter> logger,
  MonthlyReporter monthlyReporter,
  MailSenderService mailSender,
  IOptions<MailOptions> mailOptions
) : BackgroundService
{
  // A failed reporting round is retried on this schedule instead of escalating: MailSenderService
  // deliberately rethrows, and an unguarded rethrow out of a BackgroundService stops the host, which
  // Docker then restarts — so a transient Gmail outage used to become a restart loop.
  private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMinutes(1);
  private static readonly TimeSpan RetryMaxDelay = TimeSpan.FromHours(1);

  private readonly RestartNoticeThrottle _restartNotice = new(logger);

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    logger.LogInformation("Worker running at: {time}", DateTimeOffset.UtcNow);
    await SendStartupMails();

    var backoff = new ExponentialBackoff(RetryBaseDelay, RetryMaxDelay);
    while (!stoppingToken.IsCancellationRequested)
    {
      var now = DateTime.UtcNow;
      var nextRunTime = new DateTime(
        now.Year,
        now.Month,
        now.Day,
        mailOptions.Value.HourOfDay,
        0,
        0
      ); // 6 AM today
      if (now >= nextRunTime)
        nextRunTime = nextRunTime.AddDays(1);

      logger.LogInformation("Next run time: {time}", nextRunTime);
      if (!await DelayAsync(nextRunTime - now, stoppingToken))
        break;

      try
      {
        await SendDailyMail();
        await monthlyReporter.SendReportAsync();
        backoff.Reset();
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        var retryIn = backoff.Next();
        logger.LogError(
          ex,
          "The daily reporting round failed; the next attempt is delayed by {RetryIn}.",
          retryIn
        );
        if (!await DelayAsync(retryIn, stoppingToken))
          break;
      }
    }
  }

  /// <summary>
  /// The two startup mails, each guarded on its own: a failed startup mail must not keep the daily
  /// loop from running, and it must not take the host down either.
  /// </summary>
  private async Task SendStartupMails()
  {
    try
    {
      var utcNow = DateTimeOffset.UtcNow;
      if (_restartNotice.ShouldSend(utcNow))
      {
        await mailSender.WelcomeOrRestartMessage();
        _restartNotice.RecordSent(utcNow);
        logger.LogInformation("Send Mail as startup to verify the service is running and works.");
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "The startup notification failed; continuing with the daily loop.");
    }

    try
    {
      await monthlyReporter.SendReportOnStartup();
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "The startup report failed; continuing with the daily loop.");
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

  /// <summary>
  /// Waits, and reports whether the wait completed rather than the service being stopped. A stop is
  /// not an error: it must exit the loop without a logged exception.
  /// </summary>
  private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
  {
    if (delay <= TimeSpan.Zero)
    {
      return !stoppingToken.IsCancellationRequested;
    }
    try
    {
      await Task.Delay(delay, stoppingToken);
      return true;
    }
    catch (OperationCanceledException)
    {
      return false;
    }
  }
}
