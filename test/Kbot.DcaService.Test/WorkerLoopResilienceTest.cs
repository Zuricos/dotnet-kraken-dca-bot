using System.Diagnostics;
using Kbot.Common.Api;
using Kbot.Common.Enums;
using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Kbot.DcaService.Options;
using Kbot.DcaService.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Kbot.DcaService.Test;

/// <summary>
/// The worker loop against a collaborator that throws on every iteration (C-5). A
/// <see cref="BackgroundService"/> that lets an exception escape stops the host, Docker restarts the
/// container and the reloaded pre-order state can buy again — so the loop has to log the fault, pace
/// the retry and stay alive. No network, no credentials, no order.
/// </summary>
[TestClass]
public class WorkerLoopResilienceTest
{
  [TestMethod]
  public async Task ExecuteAsync_LogsBacksOffAndStaysUpWhenEveryIterationThrows()
  {
    var minWait = TimeSpan.FromMilliseconds(20);
    var maxWait = TimeSpan.FromMilliseconds(160);
    var (worker, log) = NewWorker(minWait, maxWait);

    await worker.StartAsync(CancellationToken.None);
    try
    {
      await WaitUntil(() => log.Errors.Count >= 8, TimeSpan.FromSeconds(20));

      Assert.IsNotNull(worker.ExecuteTask);
      Assert.IsFalse(
        worker.ExecuteTask.IsCompleted,
        "A failing cycle must not end the worker: that is what stops the host."
      );

      var errors = log.Errors;
      Assert.IsTrue(
        errors.All(e => e.Exception is not null),
        "Every caught exception has to reach the sink as an exception, not as a message."
      );
      Assert.IsTrue(
        errors.All(e => e.Message.Contains("Investment cycle failed")),
        $"Unexpected errors logged: {string.Join(" | ", errors.Select(e => e.Message))}"
      );

      var delays = errors.Select(e => e.RetryIn).ToList();
      Assert.IsTrue(
        delays.All(d => d is not null),
        "The retry delay belongs in the log line, so an operator can see the backoff."
      );
      var waits = delays.Select(d => d!.Value).ToList();
      Assert.AreEqual(minWait, waits[0], "The first retry waits exactly MinWaitTime.");
      for (var i = 1; i < waits.Count; i++)
      {
        Assert.IsTrue(
          waits[i] >= waits[i - 1],
          $"Retry {i} waited {waits[i]}, less than the previous {waits[i - 1]}."
        );
      }
      Assert.IsTrue(
        waits.All(w => w >= minWait && w <= maxWait),
        $"Every retry delay must stay within [{minWait}, {maxWait}]: {string.Join(", ", waits)}"
      );
      Assert.AreEqual(maxWait, waits[^1], "A run of failures has to settle on MaxWaitTime.");
    }
    finally
    {
      await worker.StopAsync(CancellationToken.None);
    }
  }

  [TestMethod]
  public async Task ExecuteAsync_StoppingExitsPromptlyAndIsNotAnError()
  {
    // Long enough that the worker is certainly sitting in its retry delay when the stop arrives.
    var wait = TimeSpan.FromSeconds(30);
    var (worker, log) = NewWorker(wait, wait);

    await worker.StartAsync(CancellationToken.None);
    await WaitUntil(() => log.Errors.Count >= 1, TimeSpan.FromSeconds(20));
    var errorsBeforeStop = log.Errors.Count;

    var stopwatch = Stopwatch.StartNew();
    await worker.StopAsync(CancellationToken.None);
    stopwatch.Stop();

    Assert.IsLessThan(
      TimeSpan.FromSeconds(5),
      stopwatch.Elapsed,
      "Cancellation must break the loop instead of waiting the retry delay out."
    );
    Assert.AreEqual(
      TaskStatus.RanToCompletion,
      worker.ExecuteTask!.Status,
      "A stop is an orderly exit, not a fault."
    );
    Assert.AreEqual(errorsBeforeStop, log.Errors.Count, "Stopping the service is not an error.");
  }

  /// <summary>
  /// A worker whose first act throws: <see cref="HolidayService.IsHoliday"/> takes the minimum of an
  /// empty cache, so seeding the top-up window raises <see cref="InvalidOperationException"/> on
  /// every iteration (H-7, owned by P4-02 — this test only needs a reliable thrower). The transport
  /// is never reached.
  /// </summary>
  private static (DcaWorker Worker, RecordingLogger Log) NewWorker(
    TimeSpan minWaitTime,
    TimeSpan maxWaitTime
  )
  {
    var cultureOptions = MsOptions.Create(
      new CultureOptions
      {
        CultureString = "de-CH",
        CountyCode = "CH-ZH",
        Fiat = "CHF",
      }
    );
    var log = new RecordingLogger();
    var api = new KrakenApi(
      NullLogger<KrakenApi>.Instance,
      MsOptions.Create(new Secrets { ApiKey = "test-api-key", ApiSecret = "dGVzdC1zZWNyZXQ=" }),
      new UnreachableTransport()
    );
    var worker = new DcaWorker(
      log,
      new TimeComputeService(
        NullLogger<TimeComputeService>.Instance,
        new HolidayService(NullLogger<HolidayService>.Instance, cultureOptions)
      ),
      new KrakenClient(NullLogger<KrakenClient>.Instance, api),
      MsOptions.Create(
        new OrderOptions
        {
          Type = OrderType.Limit,
          Fee = 0.4,
          MinOrderVolume = 0.00005,
          AskMultiplier = 1.0,
          CryptoPair = "XBTCHF",
        }
      ),
      MsOptions.Create(new BalanceOptions { DefaultTopupDayOfMonth = 26, ReserveFiat = 100 }),
      cultureOptions,
      MsOptions.Create(new WaitOptions { MinWaitTime = minWaitTime, MaxWaitTime = maxWaitTime })
    );
    return (worker, log);
  }

  private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (!condition())
    {
      if (DateTime.UtcNow > deadline)
      {
        Assert.Fail($"The expected worker activity did not happen within {timeout}.");
      }
      await Task.Delay(10);
    }
  }

  private sealed class UnreachableTransport : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    ) => throw new InvalidOperationException($"No request expected, got {request.RequestUri}.");
  }

  /// <summary>
  /// Keeps every error the worker logged, together with its exception and the structured
  /// <c>RetryIn</c> value, so the backoff can be asserted without measuring wall-clock time.
  /// </summary>
  private sealed class RecordingLogger : ILogger<DcaWorker>
  {
    private readonly List<LoggedError> _errors = [];
    private readonly Lock _gate = new();

    internal List<LoggedError> Errors
    {
      get
      {
        lock (_gate)
        {
          return [.. _errors];
        }
      }
    }

    public IDisposable? BeginScope<TState>(TState state)
      where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel,
      EventId eventId,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter
    )
    {
      if (logLevel < LogLevel.Error)
      {
        return;
      }
      TimeSpan? retryIn = null;
      if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
      {
        var retry = values.FirstOrDefault(v => v.Key == "RetryIn").Value;
        if (retry is TimeSpan span)
        {
          retryIn = span;
        }
      }
      lock (_gate)
      {
        _errors.Add(new LoggedError(formatter(state, exception), exception, retryIn));
      }
    }
  }

  private sealed record LoggedError(string Message, Exception? Exception, TimeSpan? RetryIn);
}
