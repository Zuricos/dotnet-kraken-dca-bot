# P1-02 · Guard the Kraken sentinel call sites

|  |  |
|---|---|
| **Findings** | C-2, C-3 |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `fix/p1-c2-c3-guard-worker-sentinels` |
| **Effort** | S (~2 h) |
| **Depends on** | — (start immediately) |
| **Blocks** | P2-01 (this is the tactical fix that P2-01 later replaces with a typed result) |
| **Conflict surface** | `src/Kbot.DcaService/DcaWorker.cs` (also P1-03, P1-04), `src/Kbot.DcaService/Utility/TimeComputeService.cs` (also P1-03, P1-10), `src/Kbot.Common/Api/KrakenClient.cs` (also P2-01) |

## Problem

`KrakenClient` signals failure in-band and `DcaWorker` checks nothing.

**C-2 — a failed ticker call returns `0.0` and the bot trades on it.**
[KrakenClient.cs:52,66](../../src/Kbot.Common/Api/KrakenClient.cs#L52) returns `0.0` on every error
path, including `response.Result![pair]` throwing `KeyNotFoundException` when Kraken echoes its
canonical pair name (`XXBTZUSD`) instead of the requested alias (`XBTUSD`). The cascade in
[DcaWorker.cs:66-69](../../src/Kbot.DcaService/DcaWorker.cs#L66-L69):

```
price 0 → askPrice 0 → costForVolume 0 → `balanceFiat < 0` is false, cycle proceeds
        → balanceFiat / 0 = +Infinity → TimeSpan / +Infinity = TimeSpan.Zero
        → nextOrderTime in the past → order placed immediately, then every MinWaitTime (10 s)
```
With `OrderOptions.Type = Market` (the enum default, unvalidated) the price field is not
authoritative and **the order executes** — the bot drains the fiat balance in 10-second steps.

**C-3 — a failed balance call returns `[]` and the worker indexes it directly.**
[DcaWorker.cs:64](../../src/Kbot.DcaService/DcaWorker.cs#L64) does `balance[FiatCode]` →
`KeyNotFoundException`, which also fires when Kraken omits a zero-balance asset or when
`CultureOptions.Fiat` (`CHF`, `USD`) does not match Kraken's asset code (`ZUSD`, `ZEUR`). Nothing
catches it; the host stops and Docker restarts it.

## Scope

### In scope
1. **`KrakenClient.GetCurrentCryptoPrice`** — index the ticker response by value, not by the request
   string, so an alias/canonical mismatch is no longer an exception:
   ```csharp
   var tickerInfo = response.Result!.Values.Single().Parse();
   ```
   Keep the existing `0.0` return for now (P2-01 replaces the protocol) but log the pair and the
   returned keys on failure.
2. **`DcaWorker.InvestmentCycle`** — guard both results before any arithmetic:
   ```csharp
   var balance = await krakenClient.CheckBalance();
   if (!balance.TryGetValue(FiatCode, out var fiatBalance))
   {
     logger.LogError("Fiat asset {Fiat} not in Kraken balance (keys: {Keys}).",
       FiatCode, string.Join(",", balance.Keys));
     return waitOptions.Value.MaxWaitTime;
   }
   var balanceFiat = fiatBalance - ReserveFiat;

   var currentCryptoPrice = await krakenClient.GetCurrentCryptoPrice(CryptoPair);
   if (currentCryptoPrice <= 0)
   {
     logger.LogError("Ticker for {Pair} unavailable (price {Price}); skipping cycle.",
       CryptoPair, currentCryptoPrice);
     return waitOptions.Value.MaxWaitTime;
   }
   ```
   Both early returns must return `MaxWaitTime`, never `TimeSpan.Zero`.
3. **`TimeComputeService.ComputeNextInvestmentInterval`** — guard the divisor so no caller can ever
   produce `Zero`, `Infinity` or `NaN`:
   ```csharp
   if (costForVolume <= 0 || balanceFiat <= 0) return timeUntilNextTopUp;   // nothing to schedule
   var n = balanceFiat / costForVolume;
   return n < 1 ? timeUntilNextTopUp : timeUntilNextTopUp / n;
   ```
   Also reject a non-positive `timeUntilNextTopUp` by returning `TimeSpan.MaxValue` so the caller's
   clamp turns it into `MaxWaitTime`.
4. Validate `OrderOptions.Type` explicitly (`Enum.IsDefined`) and log the effective order type once
   at startup so `Market` is never silently selected by an omitted env var. *(The rest of the
   validator tightening is P1-07 — keep this to the one enum check to avoid a conflict.)*

### Out of scope
- Introducing `KrakenResult<T>` / nullable signatures → **P2-01**.
- The top-up day clamp and state-save ordering → **P1-03**.
- `try`/`catch` + backoff around the loop → **P1-04**.
- Resolving canonical pair names from `AssetPairs` → **P2-05**.
- Using *available* rather than *total* balance → **P4-09** (M-2).

## Acceptance criteria

- With a stubbed client returning an empty balance dictionary, one cycle logs an error and returns
  `MaxWaitTime`; no order is constructed.
- With a stubbed client returning price `0`, same behaviour.
- `ComputeNextInvestmentInterval` returns a finite, non-zero `TimeSpan` for every combination of
  `{0, negative, positive}` × `{0, negative, positive}` inputs — add unit tests for exactly this.
- Requesting `XBTUSD` and receiving a `XXBTZUSD`-keyed ticker payload parses successfully (unit test
  with a captured Kraken response body).

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
