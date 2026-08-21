# P2-06 · Pagination integrity in `GetClosedOrders`

|  |  |
|---|---|
| **Findings** | H-9 |
| **Phase** | 2 — Contract & numeric correctness |
| **Branch** | `fix/p2-h9-pagination-integrity` |
| **Effort** | S (~3 h) |
| **Depends on** | **P2-01** (needs a failure channel to propagate). Can be written in parallel and rebased. |
| **Blocks** | **P2-07** (the watermark can only be trusted once truncation is impossible) |
| **Conflict surface** | `src/Kbot.Common/Api/KrakenClient.cs` (also P2-01, P2-02, P2-03), `src/Kbot.MailService/Utility/OrderService.cs` |

## Problem

[KrakenClient.cs:176-185](../../src/Kbot.Common/Api/KrakenClient.cs#L176-L185): when
`fetchAll: true`, the recursive page fetch checks `if (next != null)` and, if the follow-up page
failed, simply returns what it has. The caller sees a normal `ClosedOrders` whose `Count` still
reports Kraken's *total* — no indication that pages are missing.

`OrderService.FetchClosedOrdersAndSave` computes the next run's start date as `MAX(CloseTimeStamp)` of
what is in the DB. Kraken returns **newest-first**, so a failure on page 2 persists the newest 50
orders, advances the watermark past them, and the older orders in the gap are **never fetched again**.
The monthly report is then permanently wrong.

Also here: `Task.Delay(2000).Wait()`
([line 178](../../src/Kbot.Common/Api/KrakenClient.cs#L178)) blocks a thread-pool thread for 2 s **per
page** inside an `async` method and wraps any fault in `AggregateException`, which the surrounding
`catch (Exception e) { ...e.Message... }` renders as the useless "One or more errors occurred." A
first-run full-history import is minutes of blocked thread, uncancellable by SIGTERM.

## Scope

### In scope
1. Replace the recursion with a loop, and make a failed page **fail the whole call** — return
   `KrakenResult<ClosedOrders>.Fail(...)` (P2-01) or `null` if P2-01 is not merged yet. The caller must
   not advance any watermark on a partial result.
2. `await` the rate-limit delay with a token:
   ```csharp
   private const int PageSize = 50;
   private static readonly TimeSpan HistoryRateLimit = TimeSpan.FromSeconds(2);
   ...
   await Task.Delay(HistoryRateLimit, cancellationToken);
   ```
   Add the `CancellationToken` parameter to `GetClosedOrders` now (the full end-to-end threading is
   **P4-04**; this one method needs it to be interruptible).
3. Send `closetime=close` explicitly. Kraken's default is `both`, so `start`/`end` may match open
   *or* close time while the local watermark is strictly a close time — an off-by-a-window bug waiting
   to happen.
4. Assert page-count sanity: track how many orders were collected vs `closedOrders.Count` and log a
   `Warning` (or fail) if they disagree at the end of a successful full fetch. Guard against an
   infinite loop if Kraken's `Count` is inconsistent — cap iterations at
   `ceil(Count / PageSize) + 2`.
5. Remove the duplicate log line at
   [KrakenClient.cs:170-174](../../src/Kbot.Common/Api/KrakenClient.cs#L170-L174) (also listed in L-6)
   — it is inside the code you are rewriting anyway.
6. `OrderService.FetchClosedOrdersAndSave`: on a failed/partial fetch, do not save and do not report
   "0 new orders" — surface the failure to the caller (see **P2-07**).
7. Tests with a stubbed handler: page 1 OK + page 2 fails → the call fails and nothing is persisted;
   3 pages OK → all rows collected exactly once, `ofs` sequence `0, 50, 100`; cancellation mid-fetch
   → `OperationCanceledException`, nothing persisted.

### Out of scope
- The `KrakenResult<T>` type itself → **P2-01**.
- Watermark derivation for the monthly report → **P2-07**.
- Retry/backoff around the whole fetch → **P4-03**.
- `AsNoTracking`/`DistinctBy` in `OrderService` → **P4-08** (M-13).

## Acceptance criteria

- No partial page set is ever returned as success.
- No `.Wait()` or `.Result` remains in `KrakenClient`: `git grep -nE '\.Wait\(\)|\.Result\b' src/Kbot.Common` → only `ApiResponse.Result` property accesses.
- The paging loop is cancellable; a SIGTERM during a full-history import exits within the host's
  shutdown timeout.
- The three tests above pass.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
