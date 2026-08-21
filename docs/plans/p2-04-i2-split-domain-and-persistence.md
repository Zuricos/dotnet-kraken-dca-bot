# P2-04 · Split the domain model from the EF entity

|  |  |
|---|---|
| **Findings** | I-2 |
| **Phase** | 2 — Contract & numeric correctness |
| **Branch** | `refactor/p2-i2-split-domain-and-persistence` |
| **Effort** | M (~1 day) |
| **Depends on** | **P2-03** (one money migration at a time) |
| **Blocks** | **P4-08** (reporting queries move to the new entity) |
| **Conflict surface** | `src/Kbot.Common/Models/Order.cs`, `src/Kbot.MailService/Database/**`, `src/Kbot.MailService/Migrations/**`, `src/Kbot.MailService/Utility/OrderService.cs` |

## Problem

[KrakenDbContext.cs:12](../../src/Kbot.MailService/Database/KrakenDbContext.cs#L12) maps
`Kbot.Common.Models.Order` directly, and `KrakenDbContextModelSnapshot.cs` references
`"Kbot.Common.Models.Order"` by name. So the **shared API library** owns MailService's database
schema while having no EF dependency of its own and no visibility of the migrations.

- Renaming or retyping a property in `Kbot.Common` silently desynchronises the snapshot from the live
  database, with **no compile-time signal**.
- `OrderStatus` / `BuyOrSell` / `OrderType` are persisted as `int`. **Inserting a member into any of
  those enums reinterprets every persisted row.** `OrderStatus` is ordered
  `Pending, Open, Closed, Canceled, Expired` — adding a status anywhere but the end silently rewrites
  history.
- Money typed as `double` in Common became `double precision` in Postgres (fixed in **P2-03**).

## Scope

### In scope
1. Introduce `Kbot.MailService/Database/Entities/OrderEntity.cs` owned by the mail service, plus an
   explicit mapper (`OrderEntity.FromDomain(Order)` / `ToDomain()`), and point `KrakenDbContext` at
   the entity. `Kbot.Common.Models.Order` stays a pure domain record.
2. Persist the three enums as **strings**: `.HasConversion<string>()` with an explicit max length.
   Generate the migration and include a data-migration step converting the existing `int` values
   using the *current* enum ordering — get this right, it is the one irreversible step:
   ```sql
   -- example for Status; do the same for Type and OrderType
   ALTER TABLE "Orders" ADD COLUMN "Status_new" text;
   UPDATE "Orders" SET "Status_new" = CASE "Status"
     WHEN 0 THEN 'Pending' WHEN 1 THEN 'Open' WHEN 2 THEN 'Closed'
     WHEN 3 THEN 'Canceled' WHEN 4 THEN 'Expired' END;
   ...
   ```
   Write the `Down` migration too.
3. Add a model-snapshot pin test: assert `dbContext.Model.GetRelationalModel()` (or a hashed
   `GetDebugView()`) matches a committed baseline string, so any accidental shape change fails a
   unit test instead of a production deployment. Keep the baseline in a `.txt` next to the test with a
   comment explaining how to regenerate it deliberately.
4. Verify the pending-model check is active: EF's `MigrateAsync` throws `PendingModelChangesWarning`
   in .NET 9+ if the snapshot is stale — make sure it is not suppressed anywhere.
5. Update `OrderService` and any report query to use the entity, mapping to the domain `Order` at the
   boundary (the reports consume domain objects).

### Out of scope
- Money type change → **P2-03**.
- The report aggregation rewrite (`Aggregate` → `GroupBy`), `AsNoTracking`, `DistinctBy` →
  **P4-08** (M-13, L-12).
- `NULLS FIRST` watermark bug → **P4-08** (M-12).
- Moving `HolidayService` out of `Kbot.Common` → **P3-01**.

## Acceptance criteria

- `Kbot.Common` contains no EF-mapped type; `git grep -n 'EntityFramework' src/Kbot.Common` → nothing.
- The migration converts existing `int` enum rows to the correct strings — verify on a seeded
  throwaway database, row by row for at least one order per enum member.
- The snapshot pin test fails if a property is renamed or retyped.
- Reports produce identical output before and after (compare a generated CSV for the same dataset).

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
docker run -d --rm --name kbot-pg -e POSTGRES_PASSWORD=test -p 55432:5432 postgres:16
# seed with pre-migration rows, run `dotnet ef database update`, then assert the enum strings
docker stop kbot-pg
dotnet csharpier check .
```
