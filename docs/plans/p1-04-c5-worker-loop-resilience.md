# P1-04 · Worker loop resilience and backoff

|  |  |
|---|---|
| **Findings** | C-5 |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `fix/p1-c5-worker-loop-resilience` |
| **Effort** | S (~3 h) |
| **Depends on** | — (start immediately) |
| **Blocks** | P2-07 (report failure handling builds on this), P4-06 |
| **Conflict surface** | `src/Kbot.DcaService/DcaWorker.cs` (also P1-02, P1-03), `src/Kbot.MailService/DailyReporter.cs` (also P2-07) |

## Problem

Neither worker guards its loop.
[DcaWorker.ExecuteAsync:45-58](../../src/Kbot.DcaService/DcaWorker.cs#L45-L58) has no `try`/`catch`,
so every throw in this review terminates the host — `KeyNotFoundException` (C-3),
`ArgumentOutOfRangeException` (C-4), `ArgumentException` from `TimeSpan / NaN`,
`DirectoryNotFoundException` from `DcaStateHandler`, `InvalidOperationException` from
`HolidayService.IsHoliday` (H-7). .NET's default `BackgroundServiceExceptionBehavior.StopHost` stops
the host, `restart: unless-stopped` restarts the container, and each restart with stale state is an
opportunity for an unscheduled buy.

In MailService the first two `await`s in
[DailyReporter.cs:17,19](../../src/Kbot.MailService/DailyReporter.cs#L17) are likewise unguarded and
`SendMail` deliberately rethrows, so a transient Gmail outage becomes an unbounded restart loop that
sends a *"DCA - Restart of Container"* mail on every iteration that gets far enough.

## Scope

### In scope
1. Add a small reusable backoff helper in `Kbot.Common` (e.g. `Kbot.Common/Helpers/ExponentialBackoff.cs`)
   — full jitter, a configurable base and a cap, `Reset()` on success:
   ```csharp
   public sealed class ExponentialBackoff(TimeSpan baseDelay, TimeSpan maxDelay)
   {
     public TimeSpan Next();     // baseDelay * 2^n with jitter, capped at maxDelay
     public void Reset();
   }
   ```
   No `Random` shared-state surprises: use `Random.Shared`.
2. Wrap the DCA loop body:
   ```csharp
   while (!stoppingToken.IsCancellationRequested)
   {
     TimeSpan waitTime;
     try
     {
       waitTime = await InvestmentCycle(stoppingToken);
       State.Save();
       _backoff.Reset();
     }
     catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
     catch (Exception ex)
     {
       logger.LogError(ex, "Investment cycle failed; backing off.");
       waitTime = _backoff.Next();
     }
     waitTime = Clamp(waitTime, waitOptions.Value.MinWaitTime, waitOptions.Value.MaxWaitTime);
     await Task.Delay(waitTime, stoppingToken);
   }
   ```
   The backoff cap must be `WaitOptions.MaxWaitTime`; the floor `MinWaitTime`.
3. Guard the DCA startup path too — `DcaStateHandler.Load()` and the first `ComputeNextTopUpTime` at
   [DcaWorker.cs:38-43](../../src/Kbot.DcaService/DcaWorker.cs#L38-L43) currently throw *before* the
   loop exists. Either move them inside the guarded loop's first iteration or wrap them with the same
   handler and enter the loop with a backoff delay.
4. Same treatment in `DailyReporter.ExecuteAsync`: guard `WelcomeOrRestartMessage()` and
   `SendReportOnStartup()`, and guard the per-iteration `SendDailyMail()` /
   `monthlyReporter.SendReportAsync()` pair. A failed startup mail must not prevent the loop from
   running.
5. Rate-limit the restart notification: only send *"Restart of Container"* if the previous send
   succeeded more than N minutes ago (persisted alongside the history state is fine, or simply skip
   the mail when the process has been up < 1 min after a crash — pick the simplest correct option and
   document it in the PR).
6. Log every caught exception with `logger.LogError(ex, ...)` (exception object, not `ex.Message`) so
   stack traces reach the Serilog sink.

### Out of scope
- The individual throws themselves — C-2/C-3 (**P1-02**), C-4 (**P1-03**), H-7 (**P4-02**),
  state-file IO (**P4-01**). This plan only guarantees the loop survives them.
- Watermark semantics on a failed fetch → **P2-07**.
- `CancellationToken` propagation into the Kraken client → **P4-04**.
- Healthchecks / dead-man's switch → **P4-06** and `FUTURE_FEATURES.md` F-1.

## Acceptance criteria

- A stubbed collaborator that throws on every cycle produces: an error log per cycle, monotonically
  increasing delays capped at `MaxWaitTime`, and a host that **stays up**.
- `stoppingToken` cancellation exits both loops promptly without logging an error.
- `dotnet build` clean; existing tests still pass.
- No `catch (Exception)` swallows without logging.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
