# P1-10 · Deterministic `TimeComputeService` tests

|  |  |
|---|---|
| **Findings** | H-12 |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `test/p1-h12-deterministic-timecompute-tests` |
| **Effort** | M (~4 h) |
| **Depends on** | ✅ **P1-03** — merged (#45), so this is **ready**. The clamp is in and `TopUpDayClampTest.cs` already covers it hermetically for 12 months × days {1, 28, 29, 30, 31}; this plan makes the *existing* `TimeComputeTest` deterministic and can fold that file in. Soft dependency on **P1-02** (divisor guard tests live here too). |
| **Blocks** | P3-01 (extends the same `TimeProvider` seam), P3-03 |
| **Conflict surface** | `src/Kbot.DcaService/Utility/TimeComputeService.cs` (also P1-02, P1-03), `test/Kbot.DcaService.Test/TimeComputeTest.cs` |

## Problem

[TimeComputeTest.cs:133-168](../../test/Kbot.DcaService.Test/TimeComputeTest.cs#L133-L168) builds its
expectation as `new DateTime(y, m+1, d)` and asserts `result.NextTopUpTime.Day == topUpDay.Day`, but
unlike its sibling `_Tomorrow` it never applies the weekend/holiday skip loop to the expectation
while the production code does. On Friday 2026-08-21 next month's day-20 is Sunday 2026-09-20 and the
service correctly rolls to Monday 2026-09-21, so the test fails:
`Assert.AreEqual failed. Expected:<20>. Actual:<21>.`

**The production code is right; the test is wrong.** It fails ~2 days in 7 plus every holiday
collision, so the suite is red for reasons unrelated to any change — which trains maintainers to
ignore failures.

## Scope

### In scope
1. Inject `TimeProvider` (built into .NET 8+) into `TimeComputeService` and replace its internal
   `DateTime.UtcNow` uses (`ComputeTimeUntilNextTopUp`, [line 47](../../src/Kbot.DcaService/Utility/TimeComputeService.cs#L47)).
   Register `TimeProvider.System` in `ServiceCollectionExtension.Setup`. Keep the explicit `utcNow`
   parameter on `ComputeNextTopUpTime` — it is already testable; make the *internal* clock the only
   thing that changes.
2. Replace `HolidayService` with an injectable seam for the test. Minimum viable: extract an
   `IHolidayService` interface in `Kbot.Common` with `bool IsHoliday(DateTime date)` and have
   `HolidayService` implement it (`P3-01` does the broader seam work; this one interface is needed
   now to make the test deterministic without network access).
3. Rewrite `TimeComputeTest` as a table-driven test with `FakeTimeProvider`
   (`Microsoft.Extensions.TimeProvider.Testing`; add the package to `Directory.Packages.props`) and a
   stub holiday service, covering hard-coded dates:
   | case | `utcNow` | day | expected |
   |---|---|---|---|
   | weekday, later this month | 2026-03-10 | 20 | 2026-03-20 (Fri) |
   | Saturday roll | 2026-06-19 | 20 | 2026-06-22 (Mon) |
   | Sunday roll | 2026-09-18 | 20 | 2026-09-21 (Mon) |
   | holiday roll | 2026-12-24 | 25 | first working day after |
   | already past → next month | 2026-03-25 | 20 | 2026-04-20 |
   | December → January rollover | 2026-12-28 | 20 | 2027-01-20 |
   | day 31 in a 30-day month (clamp, P1-03) | 2026-04-01 | 31 | 2026-04-30 (rolled if weekend) |
   | day 31 in February | 2026-02-01 | 31 | 2026-02-28 (rolled if weekend) |
4. Add `ComputeNextInvestmentInterval` cases: zero cost, zero balance, negative balance, huge
   balance, zero/negative `timeUntilNextTopUp` — asserting a finite, non-zero `TimeSpan` (matches the
   guard added in **P1-02**; if that is not merged yet, write the tests against the intended
   behaviour and note the dependency in the PR).
5. Make `HolidayServiceTest` honest: today it degrades silently offline (`Is28DecHoliday` passes for
   the wrong reason because an empty cache returns `false`). Either mark it `[TestCategory("LiveApi")]`
   (P1-01's category) or give it a stubbed `HttpMessageHandler` with a captured `date.nager.at`
   response. Prefer the stub.

### Out of scope
- The clamp itself → **P1-03**.
- The `IsHoliday` empty-cache throw and cache-eviction bugs → **P4-02** (H-7).
- Extracting `IKrakenClient`, `IDcaStateStore`, `DcaPlanner` → **P3-01**, **P3-02**.

## Acceptance criteria

- `dotnet test` passes on **every** calendar day — verify by running the suite with at least three
  distinct `FakeTimeProvider` "today" values including a Saturday and a Sunday.
- No test in `Kbot.DcaService.Test` reads the real clock or the network.
- Every case in the table above is a separate, named test case.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test test/Kbot.DcaService.Test/Kbot.DcaService.Test.csproj --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
