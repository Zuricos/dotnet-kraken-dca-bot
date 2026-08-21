# P3-02 · Extract the pure `DcaPlanner`

|  |  |
|---|---|
| **Findings** | Review §8 (`DcaWorker` SRP violation), §9 Phase 3 |
| **Phase** | 3 — Make it testable |
| **Branch** | `refactor/p3-dca-planner` |
| **Effort** | M (~1 day) |
| **Depends on** | **P3-01** |
| **Blocks** | **P4-09** (the scheduling fixes become trivial unit tests once this exists), `FUTURE_FEATURES.md` **F-3** |
| **Conflict surface** | `src/Kbot.DcaService/DcaWorker.cs`, `src/Kbot.DcaService/Utility/TimeComputeService.cs` |

## Problem

`DcaWorker` does loop control, static state I/O, options unwrapping (7 accessor properties),
price/fee arithmetic, the buy/wait decision, order-id generation, request construction and delay
clamping — all in one class against concrete collaborators. Every finding about interval maths,
clamping and month boundaries is therefore only testable by running the real worker against the live
exchange, which is exactly what `DcaWorkerTest` does today.

The scheduling core itself is the best code in the repository
(`interval = timeUntilNextTopUp / (balance / costPerMinOrder)`, self-correcting by construction). It
deserves to be a pure function.

## Scope

### In scope
1. Introduce the pure policy type:
   ```csharp
   public sealed record CycleInput(
     decimal FiatBalance, decimal AskPrice, DcaState State, DateTimeOffset Now, PairInfo Pair);

   public abstract record CycleDecision
   {
     public sealed record Buy(decimal Price, decimal Volume, string ClientOrderId) : CycleDecision;
     public sealed record Wait(TimeSpan For, string Reason) : CycleDecision;
   }

   public sealed class DcaPlanner(OrderOptions order, BalanceOptions balance, WaitOptions wait)
   {
     public CycleDecision Plan(CycleInput input);   // no I/O, no clock, no logging side effects
   }
   ```
2. `DcaWorker` becomes: **fetch → `Plan` → execute → persist → delay.** Nothing else. Move the
   arithmetic (`askPrice`, `costForVolume`, the balance guard, the interval computation, the
   `nextOrderTime` comparison, order-id generation, the Min/Max clamp) into the planner.
3. Keep `TimeComputeService` as the calendar/top-up component and have the planner take its result as
   input (`State.TimeUntilNextTopUp`), so the two remain independently testable.
4. Every `Wait` decision carries a `Reason` string; log it. This replaces the ad-hoc log messages and
   makes the daily behaviour explainable from the logs alone.
5. Delete `DcaWorker.FixOrderId` — the planner produces the client order id, and tests construct it
   directly. (If **P1-01** already removed it, skip.)
6. Table-driven unit tests for the planner covering: happy path; insufficient balance; zero/negative
   balance; zero ask price; too-early (wait) path; interval floor; residual-balance burst (M-5, the
   *behaviour* fix is P4-09 — assert current behaviour here and let P4-09 change the assertion);
   top-up boundary.

### Out of scope
- Behaviour changes: M-2 (available vs total balance), M-4 (`TimeUntilNextTopUp` refresh), M-5
  (investment-interval floor), M-6 (send↔persist window) → all **P4-09**. This branch is a pure
  refactor; the tests you write here are the safety net that makes P4-09 safe.
- Strategy pluggability → `FUTURE_FEATURES.md` **F-3**.

## Acceptance criteria

- `DcaPlanner.Plan` has no `DateTime`/`TimeProvider`/`ILogger`/`Task` in its signature — it is a pure
  function of `CycleInput`.
- `DcaWorker` is under ~80 lines and contains no arithmetic.
- Behaviour is unchanged: for a captured set of real inputs, the planner reproduces the decisions the
  old worker made (write these as regression tests first, then refactor).
- Planner unit tests cover all the cases listed above.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
