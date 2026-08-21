# P4-08 · Reporting correctness batch

|  |  |
|---|---|
| **Findings** | M-1, M-7, M-8, M-12, M-13, L-12 |
| **Phase** | 4 — Operational hardening |
| **Branch** | `fix/p4-reporting-correctness` |
| **Effort** | M (~1 day) |
| **Depends on** | **P2-03** (decimal), **P2-04** (entity split) |
| **Blocks** | — |
| **Conflict surface** | `src/Kbot.MailService/Utility/OrderService.cs`, `src/Kbot.MailService/Utility/MailSenderService.cs` (also P2-05, P2-07, P4-07), `src/Kbot.Common/Dtos/ClosedOrderInfo.cs` |

> Six independent defects in the reporting path. Each is small; they are batched because they touch
> the same two files and would otherwise conflict with each other. Do them as **six separate
> commits** so they can be reverted individually.

## Findings and required fixes

### M-1 · Daily mail prints "Price Increase: NaN%"
[MailSenderService.cs:185](../../src/Kbot.MailService/Utility/MailSenderService.cs#L185) —
`GetLastAveragePrice` divides by zero whenever the day-2 window is empty (the first two days, after
downtime, whenever funds ran out).
**Fix:** guard the divisor; render "n/a" (and omit the comparison line) when there is no baseline.
Never let a computed display value be `NaN`/`Infinity` — add a formatting helper that maps both to a
placeholder, and use it for every computed figure in the mail.

### M-7 · Market orders store `Price = 0`
[ClosedOrderInfo.cs:74-75](../../src/Kbot.Common/Dtos/ClosedOrderInfo.cs#L74-L75) — Kraken's
`descr.price` is the **requested** price; the realised average lives in the unmapped top-level
`price` field. The mail's per-order column reads `0.00` while its summary average is correct, so the
mail visibly contradicts itself.
**Fix:** map the top-level `price` into the model (e.g. `AveragePrice`), prefer it when non-zero, and
fall back to `descr.price`. Add a fixture test with a real market-order payload.

### M-8 · Bogus timezone offset in the CSV date column
[OrderService.cs:77-79](../../src/Kbot.MailService/Utility/OrderService.cs#L77-L79) — the per-day
grouping key converts an `Unspecified` `DateTime` to `DateTimeOffset`, picking up the **host's** UTC
offset.
**Fix:** group on `DateOnly.FromDateTime(closeTime.UtcDateTime)`; keep everything in UTC through the
report and label the column `DateUtc`. Coordinate with **P2-02** for the format provider.

### M-12 · `NULLS FIRST` collapses the watermark
[OrderService.cs:18](../../src/Kbot.MailService/Utility/OrderService.cs#L18) —
`OrderByDescending(o => o.CloseTimeStamp)` on PostgreSQL defaults to **NULLS FIRST**, so a single NULL
row makes `lastOrder?.CloseTimeStamp` null → `DateTimeOffset.MinValue` → the entire history is
re-fetched every day.
**Fix:** `.Where(o => o.CloseTimeStamp != null).OrderByDescending(...)` or use `MaxAsync` over the
non-null projection. Better: compute the watermark with an explicit
`SELECT MAX("CloseTimeStamp")` semantics (`MaxAsync`) which ignores NULLs by definition. Add a test
with a NULL-close-time row present.

### M-13 · Whole table materialised and change-tracked on every fetch
[OrderService.cs:29-37](../../src/Kbot.MailService/Utility/OrderService.cs#L29-L37) — `ToList()` with
no `AsNoTracking()`, then an O(N·M) in-memory scan, and no `DistinctBy` guard against cross-page
duplicates → `InvalidOperationException` on a tracked-key clash.
**Fix:** query only the ids in the fetched range (`Where(o => ids.Contains(o.OrderId)).Select(o => o.OrderId)`),
`AsNoTracking()` for all read paths, and `DistinctBy(o => o.OrderId)` on the incoming page set before
inserting. Add a test with a duplicate order id across two pages.

### L-12 · `Aggregate` written the hard way
`OrderService.GetReportPerDayCalc` uses
`Aggregate<IGrouping<…>, List<…>>([], (l, g) => { l.Add(…); return l; })` — a `Select().ToList()` with
a discarded `OrderBy` and a lambda parameter shadowing its outer scope.
**Fix:** one flat `GroupBy` on a composite key `(Pair, Type, DateUtc)` → `Select` → `ToList`.
Behaviour must be identical; write the equivalence test **before** rewriting.

## Out of scope
- Watermark advance semantics → **P2-07**.
- Pair name filtering / CSV base-quote split → **P2-05**.
- Mail transport, disposal, BOM → **P4-07**.
- `decimal`/entity changes → **P2-03**, **P2-04**.

## Acceptance criteria

- A dataset containing: a market order, a NULL-close-time row, a duplicate order id across pages, and
  an empty day-2 window produces a correct report with no `NaN`, no re-fetch of history, no exception.
- The rewritten aggregation produces byte-identical CSV to the old one for a captured dataset
  (regression test).
- `AsNoTracking()` on every read query; no full-table `ToList()` remains.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
