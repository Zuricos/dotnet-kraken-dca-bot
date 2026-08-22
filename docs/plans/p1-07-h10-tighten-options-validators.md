# P1-07 · Tighten the options validators

|  |  |
|---|---|
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
