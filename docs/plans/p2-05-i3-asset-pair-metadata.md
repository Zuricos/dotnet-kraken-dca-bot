# P2-05 · Resolve pair metadata from Kraken's `AssetPairs`

|  |  |
|---|---|
| **Findings** | I-3, M-3 |
| **Phase** | 2 — Contract & numeric correctness |
| **Branch** | `feat/p2-i3-asset-pair-metadata` |
| **Effort** | M (~1 day) |
| **Depends on** | **P2-01** (needs a typed client result), **P2-03** (decimal rounding) |
| **Blocks** | `FUTURE_FEATURES.md` F-2 (multi-pair portfolio) — do not attempt F-2 before this lands |
| **Conflict surface** | `src/Kbot.Common/Api/KrakenClient.cs`, `src/Kbot.DcaService/DcaWorker.cs`, `src/Kbot.MailService/Utility/MailSenderService.cs`, `src/Kbot.MailService/Utility/CsvService.cs` |

## Problem

Kraken accepts an *altname* (`XBTCHF`) but echoes its *canonical* name in responses. Three places
assume they are identical:

1. [KrakenClient.cs:54](../../src/Kbot.Common/Api/KrakenClient.cs#L54) — `response.Result![pair]`
   throws `KeyNotFoundException` → swallowed → `0.0` → **C-2**. (P1-02 mitigates this by indexing by
   value; this plan removes the guesswork entirely.)
2. [MailSenderService.cs:167](../../src/Kbot.MailService/Utility/MailSenderService.cs#L167) — the
   daily query filters `o.Pair == mailOptions.Value.CryptoPair` with exact string equality, but
   `Order.Pair` holds whatever Kraken returned in `descr.pair`. If they differ, the DB fills correctly
   while **every daily mail says "the dca bot didn't bought any crypto in the last 24 hours"** and
   points the user at the issue tracker. The monthly report has no pair filter and is unaffected,
   which makes the inconsistency baffling to diagnose.
3. [CsvService.cs:17-19](../../src/Kbot.MailService/Utility/CsvService.cs#L17-L19) —
   `order.Pair[..^3]` / `[^3..]` assumes a 3-character quote asset. Verified: `XBTCHF`→`XBT`/`CHF` ✅,
   `XXBTZUSD`→`XXBTZ`/`USD` ❌, `ETHUSDT`→`ETHU`/`SDT` ❌.

Related, **M-3**: the limit price is hard-rounded to **1 decimal** regardless of pair
([OrderRequest.cs:26](../../src/Kbot.Common/Dtos/OrderRequest.cs#L26),
[DcaWorker.cs:67](../../src/Kbot.DcaService/DcaWorker.cs#L67)). `Math.Round(0.08123, 1) == 0.1` — a
DOGE order at $0.08123 becomes $0.10, a **23 % overpay**, using banker's rounding. This contradicts
the README's "multiple cryptocurrencies" claim.

## Scope

### In scope
1. Add `PublicMethod.AssetPairs` and a `KrakenClient.GetAssetPairs(...)` returning the fields the bot
   needs per pair: `altname`, `wsname`, `base`, `quote`, `pair_decimals`, `lot_decimals`, `ordermin`,
   and the canonical key.
2. Add an `AssetPairMetadataProvider` (singleton, `IHostedService` or lazily initialised with a
   refresh) in `Kbot.Common` that resolves the configured pair **once at startup** into a record:
   ```csharp
   public sealed record PairInfo(string Configured, string Canonical, string AltName,
     string BaseAsset, string QuoteAsset, int PriceDecimals, int VolumeDecimals, decimal OrderMin);
   ```
   Startup must **fail loudly** if the configured pair does not exist — that is a configuration error,
   not a runtime condition.
3. `DcaWorker`: round the limit price to `PairInfo.PriceDecimals` and the volume to
   `VolumeDecimals`, with an explicit `MidpointRounding`. Validate `MinOrderVolume >= OrderMin` at
   startup and fail with a message naming both values.
4. `MailSenderService`: filter the daily query by the **resolved** canonical/alt names (match either),
   not the configured string. Add the mitigation the review suggests: when the filtered 24 h query
   returns zero rows but the unfiltered one does not, log the pairs actually present at `Warning`.
5. `CsvService`: derive base/quote from `PairInfo.BaseAsset`/`QuoteAsset` instead of string slicing.
   Delete the `[..^3]` arithmetic.
6. Cache the metadata (it changes rarely) and expose it via DI to both services. Both must be able to
   resolve without duplicating the fetch — this is the natural place for the shared `TradingOptions`
   from **P2-08** to be consumed; coordinate if that plan is already merged.
7. Tests: a captured `AssetPairs` response fixture; assert `XBTCHF` → canonical `XXBTZCHF` (verify the
   real value from the fixture), correct base/quote split for `XXBTZUSD` and `ETHUSDT`, and that the
   DOGE case rounds to the pair's actual `pair_decimals` rather than 1.

### Out of scope
- Multi-pair portfolio support → `FUTURE_FEATURES.md` **F-2**.
- The `decimal` conversion itself → **P2-03**.
- `KrakenResult<T>` → **P2-01**.
- Report aggregation / grouping fixes → **P4-08**.

## Acceptance criteria

- Configuring `CryptoPair=XBTUSD` (an alias whose canonical name differs) works end to end: ticker
  parses, order places, the daily mail finds the orders.
- A nonexistent pair fails at startup with a clear message.
- No string slicing of pair names remains: `git grep -n '\[\.\.\^3\]\|\[\^3\.\.\]' src/` → nothing.
- A DOGE-like pair (`pair_decimals = 5`) rounds the limit price to 5 decimals.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
