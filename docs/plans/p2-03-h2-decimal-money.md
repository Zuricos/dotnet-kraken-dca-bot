# P2-03 · Move money to `decimal`

|  |  |
|---|---|
| **Findings** | H-2 |
| **Phase** | 2 — Contract & numeric correctness |
| **Branch** | `refactor/p2-h2-decimal-money` |
| **Effort** | M (~1 day, incl. EF migration) |
| **Depends on** | **P2-02** (culture fix first — otherwise the same lines are rewritten twice). Soft: **P2-01** (coordinate the `KrakenClient` signature change). |
| **Blocks** | **P2-04**, **P4-08** |
| **Conflict surface** | `src/Kbot.Common/Models/Order.cs`, `src/Kbot.Common/Dtos/*.cs`, `src/Kbot.MailService/Database/KrakenDbContext.cs`, `src/Kbot.MailService/Migrations/**`, `src/Kbot.DcaService/DcaWorker.cs`, `src/Kbot.DcaService/Options/OrderOptions.cs` |

## Problem

`Price`, `Volume`, `Cost` and `Fee` are `double`
([Order.cs:14-17](../../src/Kbot.Common/Models/Order.cs#L14-L17)), and `Order` is also the EF entity,
so the migration maps all four to Postgres `double precision`
([KrakenDbContext.cs:19-22](../../src/Kbot.MailService/Database/KrakenDbContext.cs#L19-L22)).
`OrderService.GetReportPerDayCalc` then sums `Cost` and `Fee` across hundreds of rows and divides,
accumulating representation error in the reported average price and total fees.

Two verified consequences:
- `Math.Round(30000.05, 1)` returns `30000`, not `30000.1` — `30000.05` is not representable.
- `JsonSerializer.Serialize` of `MinOrderVolume = 0.00005` emits `{"volume":5E-05}` — .NET's
  shortest-round-trip formatter switches to exponent notation below `1e-4`. Legal JSON, but the bot
  is betting on the exchange's number handling for the single field that decides how much it buys.
  `decimal` serializes without exponent notation, fixing this for free.

## Scope

### In scope
1. Change to `decimal`: `Order.{Price,Volume,Cost,Fee}`, `ClosedOrderInfo.{...}`
   ([ClosedOrderInfo.cs:93-96](../../src/Kbot.Common/Dtos/ClosedOrderInfo.cs#L93-L96)),
   `OrderRequest.{Volume,Price}`
   ([OrderRequest.cs:10-11](../../src/Kbot.Common/Dtos/OrderRequest.cs#L10-L11)), the ticker DTOs
   ([TickerInfo.cs](../../src/Kbot.Common/Dtos/TickerInfo.cs)), the balance dictionary value type,
   `AggregatedOrder`, and `OrderOptions.{Fee,MinOrderVolume,AskMultiplier,InklusiveFeeMultiplier}`
   plus `BalanceOptions.ReserveFiat`.
2. Replace the `*Unparsed` → `Parse()` string layer with a shared `JsonSerializerOptions` carrying a
   `JsonConverter<decimal>` that reads Kraken's string-encoded numbers (and writes them back as
   strings where the API expects that). This deletes a whole layer of hand-written parsing — do it
   here, where the types change anyway. Keep the DTO shape otherwise identical so the diff stays
   reviewable.
3. `Math.Ceiling`/`Math.Round` in `DcaWorker.InvestmentCycle` — use the `decimal` overloads and
   `MidpointRounding.ToZero`/`AwayFromZero` **explicitly** (today's default is banker's rounding; the
   pair-aware rounding fix is **P2-05**/M-3 — here just make the mode explicit and keep behaviour).
4. EF: add `.HasColumnType("numeric(18,8)")` (or `HasPrecision(18, 8)`) for the four money columns and
   generate a migration:
   ```bash
   dotnet ef migrations add MoneyToNumeric --project src/Kbot.MailService
   ```
   Inspect the generated SQL — a `double precision` → `numeric` change is an in-place `ALTER COLUMN
   ... TYPE` with a `USING` cast on Postgres. Confirm EF emits it, and that it is safe on a populated
   table. If EF generates a drop/recreate, hand-edit the migration to an `ALTER`.
5. Verify precision fits: BTC volume needs 8 decimals; fiat cost/fee 2–4; price up to 5 integer
   digits + decimals. `numeric(18,8)` covers all of it — sanity-check against the largest plausible
   value and document the choice.
6. Add unit tests: `30000.05` round-trips; `0.00005` serializes as `0.00005` (not `5E-05`); a
   report aggregation over 500 synthetic orders produces an exact expected total.

### Out of scope
- Splitting the shared `Order` into domain + persistence types → **P2-04** (do it after, so there is
  exactly one money migration in flight at a time).
- Pair-aware `pair_decimals` / `lot_decimals` rounding → **P2-05** (M-3).
- Report aggregation rewrite → **P4-08** (L-12, M-13).

## Acceptance criteria

- No `double` remains on any money-bearing property (`git grep -n 'double' src/Kbot.Common/Models src/Kbot.Common/Dtos`).
- `dotnet ef migrations list` shows the new migration; applying it to a Postgres instance with
  existing rows preserves the values (test with a seeded container).
- `JsonSerializer.Serialize(new OrderRequest { Volume = 0.00005m, ... })` contains `0.00005`, not `5E-05`.
- Report totals for a synthetic dataset match an exact hand-computed expectation.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
# Migration round-trip against a throwaway Postgres:
docker run -d --rm --name kbot-pg -e POSTGRES_PASSWORD=test -p 55432:5432 postgres:16
ConnectionStrings__Kraken="Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=test" \
  dotnet ef database update --project src/Kbot.MailService
docker stop kbot-pg
dotnet csharpier check .
```
