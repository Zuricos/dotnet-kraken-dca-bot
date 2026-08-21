# Future Feature Ideas — dotnet-kraken-dca-bot

Extracted from the [code review](REVIEW.md) of commit `935ca73` (2026-08-21), section 10. These are
**feature proposals, not defects.** The defect remediation work is tracked separately in
[docs/ROADMAP.md](docs/ROADMAP.md) with one plan per finding under [docs/plans/](docs/plans/).

> **Sequencing:** none of these should start before milestone **M1** in the roadmap (findings
> C-1 … C-5 fixed). F-1 ships naturally with roadmap Phase 4; F-2 requires **P2-05** (canonical pair
> metadata) and **P2-03**; F-3 requires **P3-02** (the pure `DcaPlanner`). Each section below states
> its own prerequisites.

---

### F-1 · Self-monitoring: health endpoint, metrics, and a dead-man's switch

**Why this first:** the bot's core failure mode is *silence*. Every critical finding in the review ends the same way — the container looks healthy while quietly not trading, or crash-loops with the reason visible only in a log file on a Raspberry Pi. There is no healthcheck anywhere, and `restart: unless-stopped` cannot detect a worker hung in `Task.Delay`. The mail service was clearly *intended* as the monitoring channel, but it dies for exactly the same reasons the DCA service does — and its "No Orders" mail blames the user's funding rather than reporting the outage.

**Shape:**
- Switch both workers to `Microsoft.NET.Sdk.Web` and expose `/health/live` + `/health/ready` via `AddHealthChecks()` — Kraken reachability, DB connectivity, holiday-cache populated, last-successful-cycle age.
- Add `HEALTHCHECK` to both Dockerfiles and `depends_on: { kraken-database: { condition: service_healthy } }` to compose, closing M-17 and M-21 (see [P4-06](docs/plans/p4-06-m17-m21-startup-and-healthchecks.md)).
- Export Prometheus metrics via `OpenTelemetry.Exporter.Prometheus.AspNetCore`: `kbot_orders_placed_total`, `kbot_order_failures_total`, `kbot_balance_fiat`, `kbot_cycle_duration_seconds`, `kbot_last_successful_cycle_timestamp`.
- **Dead-man's switch:** if no successful cycle completes within `N × MaxWaitTime`, send an alert mail *and* mark unhealthy. This is the piece that would have caught C-2's zero-price loop, C-3's crash-loop, and H-5's silent data gap.

Ships naturally with roadmap Phase 4 ([P4-06](docs/plans/p4-06-m17-m21-startup-and-healthchecks.md)), and makes every other fix verifiable in production.

---

### F-2 · Multi-pair weighted DCA portfolio

**Why:** the README already advertises *"Support for multiple cryptocurrencies"* under Features, but `OrderOptions.CryptoPair` is a single string and the whole worker is built around one pair. This is the largest gap between what the project promises and what it does.

**Shape:** replace the scalar with a weighted list:
```jsonc
"OrderOptions": {
  "Positions": [
    { "Pair": "XBTCHF", "Weight": 0.70, "MinOrderVolume": 0.00005 },
    { "Pair": "ETHCHF", "Weight": 0.30, "MinOrderVolume": 0.002   }
  ]
}
```
The existing interval engine generalises cleanly: compute `costForVolume` per position, then `interval_i = timeUntilNextTopUp / ((balanceFiat × weight_i) / costForVolume_i)`, tracking `LastInvestmentTime` per position in `DcaState`. Validate that weights sum to 1.

This depends on I-3 / [P2-05](docs/plans/p2-05-i3-asset-pair-metadata.md) (resolving canonical pair names and per-pair `pair_decimals`/`lot_decimals`/`ordermin` from `AssetPairs`) and on M-3 — which is exactly why the hardcoded 1-decimal rounding must be fixed first. Rebalancing to target weights on each top-up is a natural follow-on.

---

### F-3 · Pluggable DCA strategies (dip-boosting / value averaging)

**Why:** the scheduling engine is already the strongest part of the codebase, and it currently supports exactly one policy — uniform time-weighted DCA. Once Phase 3 ([P3-02](docs/plans/p3-02-dca-planner.md)) has extracted `DcaPlanner` into a pure function, alternative policies are a small, well-isolated addition with a real payoff, and they are the project's most credible differentiator against exchange-native recurring-buy features.

**Shape:**
```csharp
public interface IDcaStrategy
{
    CycleDecision Decide(CycleInput input, PriceHistory history);
}
```
with selectable implementations:
- `UniformDca` — today's behaviour, the default.
- `DipWeightedDca` — scale order size by deviation from an N-day moving average (e.g. 1.5× at −10 %, 0.5× at +10 %), clamped to keep the balance on track for the top-up date. Needs a rolling price series, which F-1's metrics store or a small `PriceSample` table gives you.
- `ValueAveraging` — target a portfolio *value* trajectory rather than a spend trajectory, buying more when below target.

Every strategy stays a pure function of `(CycleInput, PriceHistory)`, so each is unit-testable against recorded price series — and back-testable offline, which the current architecture makes impossible. Report the active strategy and its multiplier in the daily mail so the user can see why a given day's buy was larger.

---

## Prerequisites at a glance

| Feature | Hard prerequisites (roadmap plans) | Why |
|---|---|---|
| **F-1 · Self-monitoring** | P4-06 (healthchecks, compose ordering) · P1-04 (a loop that survives faults is a prerequisite for a meaningful liveness signal) | The `HEALTHCHECK` and `depends_on` groundwork lands in P4-06; F-1 adds the endpoints, the metrics and the dead-man's switch on top |
| **F-2 · Multi-pair weighted DCA** | P2-05 (canonical names + `pair_decimals`/`lot_decimals`/`ordermin`) · P2-03 (`decimal`) · P3-02 (`DcaPlanner`) | Per-pair rounding and minimum-order metadata must exist before more than one pair can be traded correctly; M-3's hardcoded 1-decimal rounding makes low-priced pairs unusable today |
| **F-3 · Pluggable strategies** | P3-02 (`DcaPlanner` as a pure function) · F-1 or a `PriceSample` table for the rolling price series | A strategy interface over an impure worker is untestable and un-backtestable |

## Also considered

- **Two-way mail / Telegram command interface** — the README already anticipates it (*"there will be
  more features in the future, like responding to mail/rcs commands"*). Worth designing only after
  F-1, since a command channel and a monitoring channel share most of their plumbing.
- **FIFO cost-basis tax export** — genuinely useful given the project's Swiss framing, and the full
  order history is already in PostgreSQL. Depends on the money-precision work (**P2-03**) being done
  first; a tax report computed from `double` sums is not defensible.
