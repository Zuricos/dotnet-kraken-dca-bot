# P4-02 · Holiday cache resilience

|  |  |
|---|---|
| **Findings** | H-7 |
| **Phase** | 4 — Operational hardening |
| **Branch** | `fix/p4-h7-holiday-cache-resilience` |
| **Effort** | S (~4 h) |
| **Depends on** | Soft: **P3-01** (which splits the class into client/cache/hosted-service — this fix is much smaller afterwards) |
| **Blocks** | — |
| **Conflict surface** | `src/Kbot.Common/Helpers/HolidayService.cs` (moved to `Kbot.DcaService` by P3-01), `test/Kbot.Common.Test/HolidayServiceTest.cs` (also P1-10) |

## Problem

[HolidayService.cs:39,56-62,92](../../src/Kbot.Common/Helpers/HolidayService.cs#L39):
`Holidays.Keys.Min()` is called unconditionally before any emptiness check, and
`FetchHolidaysFromRemote` swallows **all** exceptions. So if `date.nager.at` is unreachable during
`StartAsync`, the host starts successfully with an empty cache; the first `IsHoliday` call throws
`InvalidOperationException: Sequence contains no elements`, which escapes `ComputeNextTopUpTime` →
[DcaWorker.cs:39](../../src/Kbot.DcaService/DcaWorker.cs#L39) → host shutdown **on the very first
cycle**. A third-party holiday API being briefly down crash-loops the trading bot.

Two further defects:
- `UpdateCachedHolidays` calls `.Wait()` (sync-over-async) and then **unconditionally** evicts
  `Min()`. If the refresh fetch failed, the cache goes `{Y, Y+1}` → `{Y+1}`; the guard
  `Min() < utcNow.Year` is then false and **the refresh is never retried**. Every date in `Y+2`
  silently reports "not a holiday", quietly corrupting the top-up schedule.
- `Min()`-then-`TryRemove` is a non-atomic read-modify-write; two concurrent `IsHoliday` calls can
  evict two years.

## Scope

### In scope
1. Guard the empty case in `IsHoliday`: if the cache has no data for the requested year, do **not**
   throw. Either (a) return the bundled-fallback answer, or (b) return `false` **and** log at
   `Warning` — but only after (2) below makes an empty cache nearly impossible. Prefer (a).
2. Ship a **bundled fallback holiday list** as an embedded resource (the configured `CountyCode`'s
   public holidays for the current and next year, regenerated periodically). Seed the cache from it at
   startup, then let the remote fetch overwrite it. A third-party outage then cannot affect trading.
3. Make `StartAsync` honest: retry the fetch with backoff, and if it ultimately fails, log at
   `Warning` and continue on the bundled fallback. Never start with a genuinely empty cache.
4. Replace `.Wait()` with `await` (the refresh path should be an `async` timer/`PeriodicTimer` inside
   the hosted service).
5. Only evict a year **after a confirmed successful add** of the newer year, and use `AddOrUpdate` so
   a refresh actually takes effect. Make the whole cache-swap a single atomic replacement of an
   immutable dictionary rather than a read-modify-write on a `ConcurrentDictionary`:
   ```csharp
   private volatile IReadOnlyDictionary<int, HashSet<DateOnly>> _cache = ...;
   // build the new dictionary, then assign in one statement
   ```
6. Refresh timing: refresh when the cache lacks the **next** year and we are within N weeks of the
   year boundary, and retry on failure. Add a test for the Y+2 scenario described above.
7. Use `DateOnly` for the holiday keys — the current `DateTime` keys invite `Kind`/time-component
   mismatches.
8. Tests with a stubbed `HttpMessageHandler`: remote down at startup → bundled fallback used, no
   throw; remote down at refresh → old data retained, retry happens; year rollover → both years
   present; concurrent `IsHoliday` calls → no eviction race (run 1 000 parallel calls).

### Out of scope
- Moving/splitting the class → **P3-01**.
- The `Is28DecHoliday` test that passes for the wrong reason offline → **P1-10** (or fix it here if
  P1-10 is already merged and it still degrades silently; say which in the PR).
- `HttpClient` lifetime (`new HttpClient()` per call) → **P4-03**.

## Acceptance criteria

- `IsHoliday` never throws, for any date, with any cache state.
- With the remote unreachable from process start, the DCA worker completes cycles normally.
- A failed refresh does not shrink the cache and is retried.
- 1 000 concurrent `IsHoliday` calls leave the cache intact.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
