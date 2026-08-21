# P4-09 · DCA scheduling correctness batch

|  |  |
|---|---|
| **Findings** | M-2, M-4, M-5, M-6 |
| **Phase** | 4 — Operational hardening |
| **Branch** | `fix/p4-dca-scheduling-correctness` |
| **Effort** | M (~1 day) |
| **Depends on** | **P3-02** (`DcaPlanner` — these become one-line policy changes plus tests) |
| **Blocks** | `FUTURE_FEATURES.md` **F-3** |
| **Conflict surface** | `src/Kbot.DcaService/DcaWorker.cs`, the new `DcaPlanner`, `src/Kbot.DcaService/Models/DcaState.cs` |

> Four behaviour changes to the trading policy. Each changes **when and how much the bot buys**, so
> each needs its own commit, its own test, and an explicit note in the PR body describing the
> before/after behaviour.

## Findings and required fixes

### M-2 · Balance read as *total*, not *available*
[DcaWorker.cs:63-64](../../src/Kbot.DcaService/DcaWorker.cs#L63-L64) — Kraken's `Balance` endpoint
returns the total; fiat held against an unfilled limit order is counted as spendable, inflating the
computed interval and causing rejected orders.
**Fix:** use `BalanceEx` (or `TradeBalance` / open-order exposure) to obtain the *available* balance,
or subtract the notional of the bot's own open orders. Add the endpoint to `KrakenClient` with the
same typed-result treatment (**P2-01**). State clearly in the PR which endpoint you chose and why.

### M-4 · `TimeUntilNextTopUp` is persisted derived state, never refreshed at startup
[DcaWorker.cs:38-43](../../src/Kbot.DcaService/DcaWorker.cs#L38-L43) — it is refreshed only after a
*successful* order. On a fresh state file it is `TimeSpan.Zero` → interval 0 → **immediate unscheduled
buy**.
**Fix:** stop persisting it. Compute it **every cycle** from `NextTopUpTime` and the current time
(that is what the README describes anyway). Remove the field from `DcaState` — and note the state-file
shape change; `JsonStateStore` (**P4-01**) tolerates a missing property, but verify.

### M-5 · No floor on the *investment* interval
[DcaWorker.cs:87-109](../../src/Kbot.DcaService/DcaWorker.cs#L87-L109) — the floor applies only to the
poll delay, so a residual balance near the top-up date can be dumped in a 10-second burst: the exact
opposite of dollar-cost averaging.
**Fix:** add `WaitOptions.MinInvestmentInterval` (default e.g. 15 min), validated `> MinWaitTime`.
When the computed interval falls below the floor, either buy a **larger** volume at the floor interval
(preferred — it preserves the "spend the balance by the top-up date" property) or clamp the interval
and accept a residual balance. Pick one, implement it in the planner, and document the trade-off.

### M-6 · Crash window between `SendOrder` and state persist
[DcaWorker.cs:100-119](../../src/Kbot.DcaService/DcaWorker.cs#L100-L119) — `cl_ord_id` is
minute-granular and therefore **not** a reliable idempotency key.
**Fix:** two parts.
1. Make the client order id genuinely unique and deterministic per intended purchase — e.g.
   `dca-{yyyyMMddHHmm}-{sequence}` where `sequence` is persisted, or a hash of
   `(LastInvestmentTime, volume, price)`. It must be reproducible after a crash so a retry cannot
   double-buy.
2. Write an *intent* record before sending (`state.PendingOrder = { ClientOrderId, At }`), then on
   startup query Kraken for that id (`QueryOrders` / closed-orders lookup) before deciding whether the
   order landed. Clear the intent once confirmed. **P2-01**'s `Indeterminate` `SendOrder` outcome feeds
   directly into this.

## Out of scope
- The refactor that makes these testable → **P3-02**.
- Pair-aware volume/price rounding → **P2-05**.
- Strategy pluggability → `FUTURE_FEATURES.md` **F-3**.

## Acceptance criteria

- Planner tests cover: available < total balance; fresh state file (no immediate buy); residual
  balance near top-up (no burst); crash between send and persist (recovered without a duplicate).
- A fresh `state/` directory does **not** produce a buy on the first cycle.
- The documented algorithm in the README matches the implementation after this branch (coordinate with
  **P5-01**).

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
