# P2-07 · Monthly watermark derived from reported data

|  |  |
|---|---|
| **Findings** | H-5 (and the related medium: the daily window has no watermark at all) |
| **Phase** | 2 — Contract & numeric correctness |
| **Branch** | `fix/p2-h5-monthly-report-watermark` |
| **Effort** | M (~5 h) |
| **Depends on** | **P2-06** (a fetch failure must be detectable), **P1-04** (loop must survive the new throw path) |
| **Blocks** | — |
| **Conflict surface** | `src/Kbot.MailService/MonthlyReporter.cs`, `src/Kbot.MailService/Utility/MailSenderService.cs`, `src/Kbot.MailService/DailyReporter.cs` (also P1-04) |

## Problem

[MonthlyReporter.cs:60-63](../../src/Kbot.MailService/MonthlyReporter.cs#L60-L63) sets the new
watermark to `DateTimeOffset.UtcNow` — a wall-clock value — while the report content comes from
whatever happens to be in the database. **`SendReportMail` never fetches from Kraken**; the only fetch
lives in `SendMailWithClosedOrdersLast24Hours`, which `SendDailyMail`
([DailyReporter.cs:43-54](../../src/Kbot.MailService/DailyReporter.cs#L43-L54)) wraps in a `try/catch`
that *swallows* the failure and then proceeds to `SendReportAsync()` regardless.

So on report day, if Kraken or the DB is unavailable during the daily fetch: the daily mail is skipped
and logged, the monthly report runs against a database missing the last day(s) of orders, still
sends, and **still advances `LastReportedOrderTimeStamp` to now**. Next month starts from that
timestamp, and those orders appear in **no monthly CSV, ever** — silently.

## Scope

### In scope
1. Make the report fetch its own data and let failure abort it:
   ```csharp
   public async Task<DateTimeOffset?> SendReportMail(DateTimeOffset startDate, CancellationToken ct)
   {
     await orderService.FetchClosedOrdersAndSave(ct);   // let it throw → watermark not advanced
     var orders = await orderService.QueryClosedOrdersFromDatabase(query);
     if (orders.Count == 0) return null;
     // ... build + send ...
     return orders.Max(o => o.CloseTimeStamp) ?? startDate;
   }
   ```
   `QueryClosedOrdersFromDatabase` already treats `EndDate` as exclusive, so a max-close-time
   watermark composes correctly with the next window.
2. `MonthlyReporter.SendReport`: advance and persist the watermark **only** when `SendReportMail`
   returns a non-null timestamp, and only after the mail was actually sent. On `null` (no orders),
   leave the watermark untouched and log at `Information`.
3. Remove the swallow in `DailyReporter.SendDailyMail` — or rather, keep the daily mail failure
   non-fatal but **stop it from silently authorising the monthly run**: if the daily fetch failed,
   skip `SendReportAsync()` this iteration and log at `Warning` that the monthly report was deferred.
   (P1-04's backoff makes the retry safe.)
4. Give the **daily** window a watermark too, mirroring the monthly one: persist
   `LastDailyReportedOrderTimeStamp` in the same state record and query
   `[lastReported, now)` instead of the pure wall-clock `[UtcNow-1d, UtcNow)`. A missed run then
   reports the gap rather than dropping a day. Cap the lookback (e.g. 30 days) so a long outage does
   not produce an enormous "daily" mail — and say so in the mail body.
5. Make report sending idempotent enough to survive a crash between "mail sent" and "watermark
   persisted": persist first with a `pending` marker, or accept at-most-one duplicate report and log
   it. Pick one, document the reasoning in the PR.
6. Tests (in-memory DB + stubbed mail transport + stubbed client):
   - fetch throws → no mail, watermark unchanged
   - orders exist → mail sent, watermark == max close time of the reported rows
   - zero orders → no watermark movement
   - a missed daily run → the next daily mail covers both days

### Out of scope
- Pagination truncation → **P2-06** (prerequisite).
- `HistoryState` atomic/corrupt-file handling → **P4-01** (H-6).
- `NaN` price-increase in the daily mail → **P4-08** (M-1).
- Mail transport / MailKit → **P4-07**.

## Acceptance criteria

- A failing Kraken fetch on report day leaves `state/history.json` unchanged and sends no report.
- The watermark equals the max `CloseTimeStamp` of the rows actually included in the CSV.
- No order can fall between two consecutive monthly windows — prove it with a test that runs two
  reports back to back over a dataset straddling the boundary.
- A skipped day is reported in the next daily mail.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
