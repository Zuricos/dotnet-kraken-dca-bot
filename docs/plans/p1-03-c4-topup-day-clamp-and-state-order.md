# P1-03 · Clamp the top-up day and persist state before bookkeeping

|  |  |
|---|---|
| **Status** | ✅ **Resolved** — merged into `review-and-fix` via PR #45 |
| **Findings** | C-4 |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `fix/p1-c4-topup-day-clamp-and-state-order` |
| **Effort** | S (~2 h) |
| **Depends on** | — (start immediately) |
| **Blocks** | P1-10 (tests assert the clamped behaviour), P2-08 (validator alignment) |
| **Conflict surface** | `src/Kbot.DcaService/Utility/TimeComputeService.cs` (also P1-02, P1-10), `src/Kbot.DcaService/DcaWorker.cs` (also P1-02, P1-04), `src/Kbot.DcaService/Options/BalanceOptions.cs` (also P1-07) |

## Problem

All three `new DateTime(...)` calls in
[TimeComputeService.cs:10,25,29](../../src/Kbot.DcaService/Utility/TimeComputeService.cs#L10) pass
`topUpDayOfMonth` unvalidated against the target month's length. `new DateTime(2026, 4, 31)` and
`new DateTime(2026, 2, 30)` throw `ArgumentOutOfRangeException`, and
[BalanceOptions.cs:16](../../src/Kbot.DcaService/Options/BalanceOptions.cs#L16) accepts anything in
**1–31**, so day 29–31 is a legal configuration. (`MailOptionsValidator` caps the same concept at 28
— the two services disagree, see I-5/P2-08.)

The damage comes from the ordering in `InvestmentCycle`:

```
SendOrder succeeds              (DcaWorker.cs:100)   ← money spent
LastInvestmentTime updated      (DcaWorker.cs:103)   ← in memory only
ComputeTimeUntilNextTopUp       (DcaWorker.cs:104)   ← THROWS
      ↓ unwinds past State.Save() (line 49) and out of ExecuteAsync
```

Restart → `DcaStateHandler.Load()` returns the **pre-order** `LastInvestmentTime` → `nextOrderTime`
is still in the past → **another buy** → another throw → another restart. With day 31 the bot also
cannot start at all in Apr/Jun/Sep/Nov (throw at
[DcaWorker.cs:39](../../src/Kbot.DcaService/DcaWorker.cs#L39)).

## Scope

### In scope
1. Add a clamping helper in `TimeComputeService` and route **all three** date constructions through
   it, with an explicit `DateTimeKind.Utc` (the service compares against `DateTime.UtcNow`):
   ```csharp
   private static DateTime AtDayOfMonth(int year, int month, int day) =>
     new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)), 0, 0, 0, DateTimeKind.Utc);
   ```
2. Replace the month-rollover arithmetic (`utcNow.Month + 1` with a hand-written December special
   case) with `AtDayOfMonth(...).AddMonths(1)` semantics computed from the first of the month, so the
   December branch disappears:
   ```csharp
   var firstOfNextMonth = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
   nextTopUpTime = AtDayOfMonth(firstOfNextMonth.Year, firstOfNextMonth.Month, topUpDayOfMonth);
   ```
3. Tighten `BalanceOptionsValidator` to **1–28** *and* keep the clamp (defence in depth). Document in
   the validator message that 29–31 is rejected because month lengths differ; the clamp exists for
   values that arrive from a persisted state file or a future config source.
4. Reorder `InvestmentCycle` so persistence happens immediately after a successful order, before any
   further computation:
   ```csharp
   var isSuccess = await SendOrder(askPrice);
   if (isSuccess)
   {
     State = State with { LastInvestmentTime = DateTime.UtcNow };
     State.Save();                                    // ← durable before anything can throw
     State = computeService.ComputeTimeUntilNextTopUp(State, balanceOptions.Value.DefaultTopupDayOfMonth);
   }
   ```
   Keep the existing `State.Save()` in `ExecuteAsync` (idempotent); P4-01 makes the write atomic and
   P4-09 addresses the remaining send↔persist crash window (M-6).
5. Add unit tests for `ComputeNextTopUpTime` with `topUpDayOfMonth = 31` in February, April and
   December, plus the year rollover.

### Out of scope
- Sentinel guards → **P1-02**. Loop `try`/`catch` → **P1-04**.
- Aligning `MailOptions.DayOfMonth` with `BalanceOptions.DefaultTopupDayOfMonth` into one shared
  option → **P2-08**.
- `TimeProvider` injection and the calendar-dependent test → **P1-10**.
- Atomic state writes / `MinValue` fallback → **P4-01**.

## Acceptance criteria

- `ComputeNextTopUpTime` never throws for any `topUpDayOfMonth` in 1–31 in any month of any year;
  a unit test iterates all 12 months × days {1, 28, 29, 30, 31}.
- `BalanceOptions { DefaultTopupDayOfMonth = 31 }` fails validation at startup with a clear message.
- A successful order followed by a forced throw in `ComputeTimeUntilNextTopUp` leaves
  `state/state.json` containing the **post-order** `LastInvestmentTime` (test with a temp CWD).
- Returned `DateTime` values have `Kind == Utc`.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```

---

## Resolution

Merged into `review-and-fix` from `fix/p1-c4-topup-day-clamp-and-state-order` as PR #45. **C-4 is
closed**: no `topUpDayOfMonth` can throw a date out of existence any more, and a sent order is on
disk before anything that could throw runs, so a crash-restart can no longer replay a buy.

What landed:

- [TimeComputeService.cs](../../src/Kbot.DcaService/Utility/TimeComputeService.cs) — `AtDayOfMonth`
  clamps the day to `DateTime.DaysInMonth(year, month)` and stamps `DateTimeKind.Utc`; all three
  date constructions go through it.
- [TimeComputeService.cs](../../src/Kbot.DcaService/Utility/TimeComputeService.cs) — the rollover is
  computed from the first of the month plus `AddMonths(1)`, so the hand-written December branch is
  gone and the year boundary is no longer a special case.
- [BalanceOptions.cs](../../src/Kbot.DcaService/Options/BalanceOptions.cs) — the validator accepts
  **1–28** and the failure message says why 29–31 is refused. The clamp stays as defence in depth
  for values that reach the service from a persisted state file or a config source that does not run
  the validator. This also puts `BalanceOptions` and `MailOptions` on the same range, which is what
  P2-08 needs.
- [DcaWorker.cs](../../src/Kbot.DcaService/DcaWorker.cs) — `InvestmentCycle` calls `State.Save()`
  immediately after a successful order, before `ComputeTimeUntilNextTopUp`. The `Save()` in
  `ExecuteAsync` stays and is idempotent.

One knock-on the scope did not spell out: the healthy-cycle test from P1-02 now goes through
`DcaStateHandler.Save`, which writes to a path relative to the working directory, so
[InvestmentCycleGuardTest.cs](../../test/Kbot.DcaService.Test/InvestmentCycleGuardTest.cs) gained a
`[ClassInitialize]` that creates the `state` directory in the test output directory.

Tests:

- [TopUpDayClampTest.cs](../../test/Kbot.DcaService.Test/TopUpDayClampTest.cs) — sweeps 12 months ×
  days {1, 28, 29, 30, 31} over a non-leap and a leap year, from the first of the month and from its
  last evening so both date-construction sites are hit, asserting no throw, `Kind == Utc`, never a
  weekend and never a date in the past. It then pins the exact result for February (leap and
  non-leap), April and December, the month rollover into a short month, and the December → January
  rollover. Hermetic: the holiday cache is seeded empty and every case sits in a past year, so there
  is no network call and no dependency on today's date. It also covers the validator — day 31 and
  day 0 are rejected, day 28 still passes.
- [InvestmentCyclePersistOrderTest.cs](../../test/Kbot.DcaService.Test/InvestmentCyclePersistOrderTest.cs)
  — one cycle against a stubbed transport in a temporary working directory with the post-order
  bookkeeping forced to throw: `state/state.json` must already carry the post-order
  `LastInvestmentTime`. The counter-test asserts that a skipped cycle writes nothing.

Verified: `dotnet build Kbot.sln -warnaserror` clean, 46 tests pass under the default filter,
`csharpier check .` clean.

Deliberately not done: `TimeProvider` injection and the still calendar-dependent tests in
`TimeComputeTest` → **P1-10**. One shared day-of-month option across both services → **P2-08**.
Atomic state writes → **P4-01**. The remaining send↔persist crash window (M-6) → **P4-09**.

Noted while here, not fixed: `DcaStateHandler.Save` throws if the `state` directory is missing and
its `catch` retries with `File.Delete` on the same missing path — production creates `/app/state` in
the Dockerfile, but this is now on the path a successful order takes and looks like the cause of
issue #23 (**P4-01** owns that file). `HolidayService.IsHoliday` throws on an empty holiday cache,
i.e. when the startup fetch failed (**P4-02**).

Follow-ups unblocked: **P1-10**; **P2-08** once P1-07 lands — which it has, as PR #47.
