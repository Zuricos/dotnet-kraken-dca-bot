# P2-08 · Shared `TradingOptions` across both services

|  |  |
|---|---|
| **Findings** | I-5 |
| **Phase** | 2 — Contract & numeric correctness |
| **Branch** | `refactor/p2-i5-shared-trading-options` |
| **Effort** | M (~5 h) |
| **Depends on** | ✅ **P1-03** — merged (#45); **P1-07** (validator tightening) is the one still open |
| **Blocks** | — (but **P2-05** consumes it; coordinate whichever lands second) |
| **Conflict surface** | `src/Kbot.Common/Options/**`, `src/Kbot.DcaService/Options/**`, `src/Kbot.MailService/Options/MailOptions.cs`, both `ServiceCollectionExtension.cs`, `docker/stack.env` (also P1-08, P4-10) |

## Problem

The same values are configured twice under different names, with no cross-check:

| Concept | DcaService | MailService |
|---|---|---|
| Trading pair | `OrderOptions__CryptoPair="XBTCHF"` | `MailOptions__CryptoPair="XBTCHF"` |
| Fiat currency | `CultureOptions__Fiat="CHF"` | `MailOptions__Fiat="CHF"` |
| Day-of-month | `BalanceOptions__DefaultTopupDayOfMonth` — validated **1–28** since P1-03 ✅ | `MailOptions__DayOfMonth` — validated **1–28** ✅ |

Change one and the reports silently disagree with the trades. The day-of-month divergence is the live
bug behind **C-4**: the two validators encode different beliefs about the same concept and the more
permissive one crashes. P1-03 has since put both on 1–28 and made the computation clamp, so the
divergence is no longer a crash — but it is still two options for one concept, which is what this
plan removes.

Also: `CultureOptions` lives in `Kbot.Common` and `HolidayService` (also Common) depends on it — but
only `Kbot.DcaService` registers either, so wiring `HolidayService` into MailService would fail at
resolve time.

## Scope

### In scope
1. Add `Kbot.Common/Options/TradingOptions.cs`:
   ```csharp
   public record TradingOptions
   {
     public required string CryptoPair { get; init; }   // e.g. XBTCHF
     public required string Fiat { get; init; }         // e.g. CHF
     public required string Crypto { get; init; }       // e.g. BTC (display only)
     public int TopUpDayOfMonth { get; init; }          // 1–28
   }
   ```
   plus one `TradingOptionsValidator` in `Kbot.Common` used by **both** services — one rule set, one
   belief, no drift.
2. Bind it from a single `TradingOptions` config section in both services'
   `ServiceCollectionExtension.SetupOptions`, and remove the duplicated members from `OrderOptions`,
   `CultureOptions` and `MailOptions`. Keep service-specific settings where they are
   (`AskMultiplier`, `MinOrderVolume`, `HourOfDay`, …).
3. Update `docker/stack.env` and both `appsettings.json` files to the new section, and keep a short
   **migration note** in the PR body + CHANGELOG (**P5-02** consumes it): this is a breaking config
   change for existing deployments.
4. Decide `CultureOptions`' fate: it currently mixes a display culture, a holiday county code and the
   fiat asset. After moving `Fiat` out, `CultureOptions` should hold only `CultureString` and
   `CountyCode`. Register it in **both** services if `HolidayService` stays in `Kbot.Common`; if
   **P3-01** has already moved `HolidayService` into `Kbot.DcaService`, register it only there and say
   so in the PR.
5. Optional but valuable: a cross-service consistency check at startup — log the resolved
   `TradingOptions` (they are not secrets) so an operator can eyeball that both containers agree.

### Out of scope
- Canonical pair resolution → **P2-05**.
- Moving `HolidayService` out of `Kbot.Common` → **P3-01**.
- `stack.env` quoting → **P4-10** (M-14).
- Culture validation → **P4-10** (M-15).

## Acceptance criteria

- `CryptoPair`, `Fiat` and the top-up day exist in exactly **one** options record.
- One validator governs the day-of-month for both services; day 29–31 is rejected in both.
- Both services start with the new `stack.env`; starting with the *old* env var names fails
  validation with a message naming the new section (do not silently ignore stale config).
- `git grep -n 'MailOptions__CryptoPair\|OrderOptions__CryptoPair'` → only in migration notes.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
docker compose -f docker/example-compose.yaml config >/dev/null && echo "compose ok"
dotnet csharpier check .
```
