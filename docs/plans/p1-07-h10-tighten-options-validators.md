# P1-07 · Tighten the options validators

|  |  |
|---|---|
| **Status** | ✅ **Resolved** — merged into `review-and-fix` via PR #47 |
| **Findings** | H-10 |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `fix/p1-h10-tighten-options-validators` |
| **Effort** | S (~2 h) |
| **Depends on** | — (start immediately) |
| **Blocks** | P2-08 (shared `TradingOptions` builds on the aligned validators) |
| **Conflict surface** | `src/Kbot.DcaService/Options/*.cs` (`BalanceOptions.cs` also P1-03), `src/Kbot.DcaService/appsettings.json` |

## Problem

Every numeric check uses `>= 0` instead of `> 0`. Verified by running the built service with no
configuration: `Secrets`, `OrderOptions`, `BalanceOptions` and `CultureOptions` all failed
validation, but **`WaitOptions` produced no error at all** — `MinWaitTime = MaxWaitTime =
TimeSpan.Zero` satisfies all three of its rules
([WaitOptions.cs:16-23](../../src/Kbot.DcaService/Options/WaitOptions.cs#L16-L23)).

- `MinWaitTime = 0` makes `Task.Delay(TimeSpan.Zero)` a busy loop issuing Kraken `Balance` + `Ticker`
  calls as fast as the network allows — instant rate-limit, 100 % CPU.
- `MinOrderVolume = 0` or `AskMultiplier = 0` makes `costForVolume = 0`, reproducing C-2. With
  `balanceFiat == 0` the result is `0.0/0.0 = NaN` and `TimeSpan / NaN` throws `ArgumentException`.

There is no `WaitOptions` section in
[appsettings.json](../../src/Kbot.DcaService/appsettings.json), so an operator who forgets
`stack.env` gets the busy loop rather than a startup failure.

## Scope

### In scope
1. `OrderOptionsValidator`:
   - `MinOrderVolume > 0`
   - `AskMultiplier` within a sanity band `[0.5, 1.5]` — it is a **price** multiplier, and a typo of
     `100` instead of `1.0001` would buy at 100× ask
   - `Fee >= 0 && Fee <= 100` (it is a percentage)
   - `Enum.IsDefined(options.Type)` — coordinate with P1-02, which adds the same check; whoever
     merges second keeps one copy
   - `CryptoPair` non-empty **and** matched against a conservative pattern (`^[A-Z0-9]{5,12}$`)
2. `WaitOptionsValidator`:
   - `MinWaitTime > TimeSpan.Zero`, `MaxWaitTime > TimeSpan.Zero`
   - `MinWaitTime <= MaxWaitTime` (keep)
   - a lower bound on `MinWaitTime` that respects Kraken's rate limits — recommend `>= 1s`, and log a
     warning below 5 s
3. `BalanceOptionsValidator`: keep `ReserveFiat >= 0` (0 is legitimate). **P1-03 is merged**, so
   `DefaultTopupDayOfMonth` is already validated as 1–28 and already has test coverage in
   `TopUpDayClampTest.cs` — leave that rule and its test alone.
4. `CultureOptionsValidator`: keep as is here — the `CultureInfo`/`CountyCode` validation belongs to
   **P4-10** (M-15).
5. Add **every** section with safe defaults to `src/Kbot.DcaService/appsettings.json` so an omitted
   env var can never be silently zero:
   ```jsonc
   "WaitOptions":    { "MinWaitTime": "00:00:30", "MaxWaitTime": "01:00:00" },
   "OrderOptions":   { "Type": "Limit", "Fee": 0.4, "MinOrderVolume": 0.00005, "AskMultiplier": 1.00001 },
   "BalanceOptions": { "DefaultTopupDayOfMonth": 26, "ReserveFiat": 0 }
   ```
   Do **not** put a `CryptoPair` default in — it must remain a required, deliberate choice.
6. Unit-test all four validators: one test per rule, asserting the failure message names the option.
   (This is the only validator coverage in the repo — H-10 survived because there was none.)

### Out of scope
- The clamping logic itself → **P1-03**.
- Unifying duplicated `CryptoPair`/`Fiat` config across services → **P2-08**.
- `MailOptions` / `MailSecrets` validators (the 16-char password rule) → **P4-07** (M-22).
- `stack.env` quoting → **P4-10** (M-14).

## Acceptance criteria

- Starting the DCA service with **no** configuration fails at startup listing every missing/invalid
  option, including `WaitOptions`.
- Starting it with only `appsettings.json` (no `stack.env`) fails only on `CryptoPair` and `Secrets`.
- `AskMultiplier = 100` and `MinWaitTime = 0` both fail validation.
- New validator unit tests pass and cover every rule.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
# Negative-path smoke test — must fail fast with a readable message:
(cd src/Kbot.DcaService && env -i DOTNET_ENVIRONMENT=Production dotnet run --no-build || true)
dotnet csharpier check .
```

---

## Resolution

Merged into `review-and-fix` from `fix/p1-h10-tighten-options-validators` as PR #47. **H-10 is
closed**: no degenerate options value starts the DCA service any more, and an omitted `stack.env`
fails at startup instead of busy-looping against Kraken.

What landed:

- [OrderOptions.cs](../../src/Kbot.DcaService/Options/OrderOptions.cs) — `MinOrderVolume > 0`;
  `AskMultiplier` bounded to the sanity band `[0.5, 1.5]`, because it multiplies the ask *price* and
  the typo `100` for `1.0001` would bid a hundred times the ask; `Fee` bounded to `[0, 100]` as the
  percentage it is; `CryptoPair` matched against `^[A-Z0-9]{4,16}$`. Every `double` is additionally
  checked with `double.IsFinite`: a `TypeConverter` parses `"Infinity"` and `"NaN"`, and an infinite
  cost is as degenerate as a zero one. P1-02's `Enum.IsDefined(options.Type)` check was already
  there and was kept as the single copy. An `AskMultiplier` below 1 is legal but warned about: it
  places a resting limit buy under the market, and `SendOrder` neither sets an expiry nor checks for
  a fill, so the schedule advances on acceptance and DCA can stop silently with the fiat locked in
  an open order.
- [WaitOptions.cs](../../src/Kbot.DcaService/Options/WaitOptions.cs) — `MinWaitTime` must be at least
  one second, `MaxWaitTime` between zero and seven days, `MinWaitTime <= MaxWaitTime` unchanged. The
  plan's separate `> TimeSpan.Zero` rule is subsumed by the one-second floor deliberately: two
  messages for `00:00:00` only make the startup error harder to read, and the tests still prove
  zero, negative and sub-second values are all rejected. A `MinWaitTime` under five seconds is
  logged as a warning rather than refused, which is why the validator now takes an `ILogger`. The
  upper bound on `MaxWaitTime` is there because that value is the ceiling of every `Task.Delay` the
  loop performs, and `Task.Delay` throws above ~49.7 days from a call site P1-04's catch-all does
  not cover — the host would stop and Docker would crash-loop it.
- [BalanceOptions.cs](../../src/Kbot.DcaService/Options/BalanceOptions.cs) — untouched.
  `ReserveFiat >= 0` is correct (reserving nothing is the default) and P1-03 owns
  `DefaultTopupDayOfMonth`.
- [CultureOptions.cs](../../src/Kbot.Common/Options/CultureOptions.cs) — untouched, as planned;
  M-15's `CultureInfo` / `CountyCode` existence checks belong to P4-10.
- [appsettings.json](../../src/Kbot.DcaService/appsettings.json) — `OrderOptions`,
  `BalanceOptions`, `WaitOptions` **and** `CultureOptions` now carry defaults. Every value is the one
  in `docker/stack.env` **except `MinWaitTime`, which ships `00:00:30` where `stack.env` has
  `00:00:10`**: the shipped default is deliberately the more conservative of the two, it is the value
  this plan's scope section specifies, and Compose deployments are unaffected because the environment
  wins over `appsettings.json`. Nothing in the build compares the two files, so the difference is
  stated here on purpose rather than left to be discovered. `CryptoPair` deliberately has no default
  at all: which asset the bot buys must stay a deliberate choice.
- [DcaWorker.cs](../../src/Kbot.DcaService/DcaWorker.cs) — comment only. P1-04's non-positive-delay
  floor stays as defence in depth; it no longer describes this plan as the missing fix.

`CultureOptions` got defaults although the plan's snippet listed only three sections: the acceptance
criterion "starting with only `appsettings.json` fails only on `CryptoPair` and `Secrets`" cannot
hold while the culture section is empty. This weakens an existing fail-fast, so the cost is worth
stating precisely: an operator who sets `CryptoPair=XBTEUR` but omits `CultureOptions` gets `CHF`.
`DcaWorker.InvestmentCycle` then looks the balance up by a `Fiat` code that is simply absent from
Kraken's balance dictionary, logs an error and returns `MaxWaitTime` — an hourly no-op forever, not
a wrong buy. The direction is fail-safe, but it is silent apart from the log. Whether `Fiat` should
be a required, deliberate choice like `CryptoPair` belongs to **P2-08**, which unifies both across
the two services.

Tests: [OptionsValidatorTest.cs](../../test/Kbot.DcaService.Test/OptionsValidatorTest.cs),
[ShippedDefaultsTest.cs](../../test/Kbot.DcaService.Test/ShippedDefaultsTest.cs) and
[CultureOptionsValidatorTest.cs](../../test/Kbot.Common.Test/CultureOptionsValidatorTest.cs) — 25
tests, the first validator coverage in the repo. One test per rule, each asserting that the
degenerate value is rejected, that the failure message names the option (the startup error is all an
operator gets) and that a sane value still starts up. `ShippedDefaultsTest` links the service's real
`appsettings.json` into the test output and runs it through the real `SetupOptions` wiring, so the
shipped defaults cannot drift out of validity and an unregistered validator is caught too.

Verified: `dotnet build Kbot.sln -warnaserror` clean, 89 tests pass under the default filter (up
from 64), `csharpier check .` clean. The plan's negative-path smoke test fails at startup with
exactly `Secrets incomplete: ApiKey must be set, ApiSecret must be set` and `OrderOptions
incomplete: CryptoPair must be set`; the same run with `MinWaitTime=00:00:00`, `AskMultiplier=100`,
`MinOrderVolume=0` and a quoted `CryptoPair` names every one of them.

Deliberately not done: the clamping logic → **P1-03** (merged); the duplicated `CryptoPair` / `Fiat`
config across both services → **P2-08**; `MailOptions` / `MailSecrets` validators → **P4-07**;
`CultureInfo` / `CountyCode` validation and `stack.env` quoting → **P4-10**. No tests were added for
`SecretsValidator`, whose file **P1-09** is editing.

Neither `ServiceCollectionExtension.cs` needed a change in the end, so P1-07 is off that file's
conflict list in [ROADMAP.md](../ROADMAP.md) §6.

Review round 1 (independent agent, CHANGES REQUESTED, no must-fixes) changed four things, all of
them in this PR:

1. `^[A-Z0-9]{5,12}$` → `{4,16}`. Checked against Kraken's live `AssetPairs` list, the original
   bounds rejected 17 pairs it actually trades — 15 four-character ones (`SUSD`, `AEUR`, …) and
   `CHILLHOUSEEUR` / `CHILLHOUSEUSD` at thirteen — so a legitimate configuration could not start.
   The character class survived the same check: no current altname has a lower-case letter or
   punctuation. The upper bound was also the one bound with no test, which is why the wrong value
   shipped; both edges are now pinned.
2. An upper bound on `MaxWaitTime` (see above). The most valuable of the four: it closes the same
   class of defect this plan exists for, one the plan itself did not name.
3. The `AskMultiplier < 1` warning (see above). The band stays `[0.5, 1.5]`, because the test
   project's `appsettings.json` uses `0.5` on purpose so the `LiveExchange` order cannot fill.
4. The `MinWaitTime` default is documented as deliberately differing from `stack.env` instead of
   being claimed identical to it, and the `Fiat` failure mode is described correctly (an hourly
   no-op, not "not enough balance").

Both declared judgement calls were reviewed and kept. Left with their owning plans, as flagged in
review: a finiteness check on `BalanceOptions.ReserveFiat` and whether `Fiat` should be required
(**P2-08**), and real pair resolution (**P2-05**).

Follow-ups unblocked: **P2-08**.
