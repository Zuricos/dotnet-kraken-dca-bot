# P1-03 · Clamp the top-up day and persist state before bookkeeping

|  |  |
|---|---|
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
