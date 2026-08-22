# P1-04 · Worker loop resilience and backoff

|  |  |
|---|---|
| **Status** | ✅ **Resolved** — merged into `review-and-fix` via PR #46 |
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

---

## Resolution

Merged into `review-and-fix` from `fix/p1-c5-worker-loop-resilience` as PR #46. **C-5 is closed**: no
throw in either service can stop the host any more, so a transient fault costs a logged error and a
paced retry instead of a container restart that reloads pre-order state and buys again.

What landed:

- [ExponentialBackoff.cs](../../src/Kbot.Common/Helpers/ExponentialBackoff.cs) — the shared retry
  pacing: `Next()` doubles the delay per consecutive failure, `Reset()` returns it to the base after
  a success, and every value stays inside `[baseDelay, maxDelay]`. The jitter fills the window
  between the previous ceiling and the current one rather than the full `[0, ceiling]` range the plan
  suggested: full jitter can return a delay shorter than the one before it, and the acceptance
  criterion requires the loops to back off *monotonically*. `Random.Shared` supplies the jitter, so
  there is no shared `Random` to synchronise.
- [DcaWorker.cs](../../src/Kbot.DcaService/DcaWorker.cs) — the loop body is guarded, around P1-03's
  post-order `State.Save()` rather than in place of it. A caught
  exception is logged with the exception object and the retry delay, then paced by a backoff whose
  floor is `WaitOptions.MinWaitTime` and whose cap is `WaitOptions.MaxWaitTime`; a successful cycle
  resets it. `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)` and a
  guarded `Task.Delay` make a stop an orderly exit rather than a logged fault.
- [DcaWorker.cs](../../src/Kbot.DcaService/DcaWorker.cs) — the startup path (`DcaStateHandler.Load()`
  and the first `ComputeNextTopUpTime`) moved into `SeedState()` and now runs *inside* that guard on
  the loop's first iteration, so a throw there is retried after a backoff delay instead of stopping
  the host before the loop exists.
- [DailyReporter.cs](../../src/Kbot.MailService/DailyReporter.cs) — `WelcomeOrRestartMessage()` and
  `SendReportOnStartup()` are guarded individually, so a failed startup mail no longer prevents the
  daily loop from running, and the per-iteration `SendDailyMail()` / `SendReportAsync()` pair is
  guarded with its own backoff (1 min → 1 h) before the next attempt.
- [RestartNoticeThrottle.cs](../../src/Kbot.MailService/Utility/RestartNoticeThrottle.cs) — the
  restart notification is rate-limited. The simplest correct option turned out to be a marker file
  (`state/restart-notice.json`) holding the timestamp of the last *successful* send: it survives the
  restart it is meant to catch, which an in-memory guard cannot. A second notice inside an hour is
  suppressed; a missing, unreadable or future-dated marker counts as "no recent send", and a marker
  that cannot be written is a warning, never a fault.

Two things the scope implied but did not spell out:

- A non-positive backoff delay in `DcaWorker` falls back to one second. `WaitOptions` of `00:00:00`
  passes validation today, and pacing a persistent fault at zero would replace a restart loop with a
  busy loop against Kraken. `ExponentialBackoff` likewise normalises a negative base and an inverted
  range instead of throwing — a guard that throws on bad config is not a guard.
- `Task.Delay` is wrapped as well, not just the cycle: a stop arriving during the wait is the common
  case, and it must exit without an error.

Tests: [ExponentialBackoffTest.cs](../../test/Kbot.Common.Test/ExponentialBackoffTest.cs) pins the
first delay, the monotonic growth, the cap, the jitter window, `Reset()`, the degenerate ranges and
the absence of overflow.
[WorkerLoopResilienceTest.cs](../../test/Kbot.DcaService.Test/WorkerLoopResilienceTest.cs) runs the
real `ExecuteAsync` against a collaborator that throws on every iteration (an empty `HolidayService`
cache — H-7, which P4-02 owns) and asserts an error with its exception per iteration, retry delays
that never shrink and settle on `MaxWaitTime`, and a worker task that is still running; a second test
asserts that stopping the service exits promptly, runs to completion and logs nothing.
[RestartNoticeThrottleTest.cs](../../test/Kbot.MailService.Test/RestartNoticeThrottleTest.cs) covers
the quiet period, the first run, a stale marker, a future-dated marker, a corrupt marker and an
unwritable location.

Verified: `dotnet build Kbot.sln -warnaserror` clean, 64 tests pass under the default filter,
`csharpier check .` clean.

Deliberately not done: the individual throws themselves — C-4 → **P1-03**, H-7 → **P4-02**,
state-file IO → **P4-01**; watermark semantics on a failed fetch → **P2-07**;
`CancellationToken` propagation into the Kraken client → **P4-04**; healthchecks and the dead-man's
switch → **P4-06** and `FUTURE_FEATURES.md` F-1. The mail loop has no test driving `ExecuteAsync`
either: `DailyReporter` depends on the concrete `MailSenderService`, which needs a database, and the
seams for that are **P3-01** / **P3-03**.

Follow-ups unblocked: **P2-01** — the last of its two prerequisites.
