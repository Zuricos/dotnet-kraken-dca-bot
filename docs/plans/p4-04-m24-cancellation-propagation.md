# P4-04 · Thread `CancellationToken` end to end

|  |  |
|---|---|
| **Findings** | M-24 |
| **Phase** | 4 — Operational hardening |
| **Branch** | `refactor/p4-m24-cancellation-propagation` |
| **Effort** | S–M (~4 h) |
| **Depends on** | **P4-03** (typed clients), soft **P2-06** (which already adds a token to `GetClosedOrders`) |
| **Blocks** | — |
| **Conflict surface** | `src/Kbot.Common/Api/**`, `src/Kbot.MailService/Utility/**`, both workers |

## Problem

There is **no `CancellationToken` anywhere** in `KrakenApi`/`KrakenClient`, and MailService threads it
only as far as `Task.Delay`. On shutdown, an in-flight pagination chain or a 100 s SMTP hang outlives
the host's 30 s shutdown timeout and is SIGKILLed — potentially mid-`SaveChangesAsync`. On a
Raspberry Pi with `restart: unless-stopped`, that is a torn write on every deploy.

## Scope

### In scope
1. Add `CancellationToken cancellationToken` as the last parameter of every `async` method in
   `KrakenApi` and `KrakenClient`, and pass it to `GetAsync`/`PostAsync`/`ReadAsStringAsync`/
   `JsonSerializer.DeserializeAsync` and every `Task.Delay`. No defaults (`= default`) on internal
   methods — make the caller pass it, so nothing is forgotten silently.
2. Thread it through `OrderService`, `MailSenderService`, `HtmlService`/`CsvService` (where async),
   `MonthlyReporter`, `DailyReporter` and `DcaWorker` down from `ExecuteAsync(stoppingToken)`.
3. `SaveChangesAsync(cancellationToken)` — but consider whether a persist should be cancellable at
   all: a cancelled save during shutdown loses the just-fetched orders. Prefer
   `CancellationToken.None` (or a linked token with a short grace period) for the **write** after a
   successful fetch, and document the choice inline. Getting this backwards is worse than not
   threading the token.
4. Handle `OperationCanceledException` correctly at the loop boundary: on shutdown it is **not** an
   error (see **P1-04**'s `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)`).
5. Set an explicit SMTP timeout (the default is 100 s and blocks the single loop) — coordinate with
   **P4-07**, which replaces the transport entirely. If P4-07 is landing first, skip this item.
6. Raise the host shutdown timeout only if justified: `HostOptions.ShutdownTimeout`. Prefer making the
   work interruptible over extending the timeout.
7. Test: start a cycle against a stub server that never responds, cancel the token, assert the method
   returns within ~1 s and that no partial DB write occurred.

### Out of scope
- Resilience/retry configuration → **P4-03**.
- Mail transport replacement → **P4-07**.
- Healthchecks → **P4-06**.

## Acceptance criteria

- Every `async` method in `src/` accepts and forwards a `CancellationToken`
  (`git grep -nE 'async Task' src/ | grep -v CancellationToken` returns only justified exceptions,
  listed in the PR).
- `docker stop` (SIGTERM) exits both containers within the shutdown timeout, with a clean
  "shutting down" log line and no SIGKILL.
- A cancelled full-history import leaves the database consistent.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
docker compose -f docker/example-compose.yaml up -d && sleep 20 && time docker compose -f docker/example-compose.yaml stop
dotnet csharpier check .
```
