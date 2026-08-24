# P3-03 · Close the unit-test coverage gaps

|  |  |
|---|---|
| **Findings** | M-25 + the whole REVIEW.md Appendix |
| **Phase** | 3 — Make it testable |
| **Branch** | `test/p3-coverage-gaps` |
| **Effort** | L (~2 days; splittable — see note) |
| **Depends on** | **P1-01** (safe suite). Partial: the pure-function tests need nothing else and can start early; the worker/report tests need **P3-01**/**P3-02**. |
| **Blocks** | — |
| **Conflict surface** | `test/**` — coordinate with P1-01 and P1-10; those own `KrakenApiTest.cs` / `TimeComputeTest.cs` |

> **Splittable:** if two agents are available, split at the line below into
> `test/p3-coverage-gaps-pure` (items 1–5, no dependencies beyond P1-01) and
> `test/p3-coverage-gaps-behaviour` (items 6–10, needs P3-01/P3-02).

## Problem

No test project covers `KrakenApi.CreateSignature`, `DcaStateHandler`, the watermark logic, or any
DTO parser — all pure functions and ideal unit-test targets. Tests run
`Parallelize(Workers = 0)` ([AssemblyInfo.cs](../../test/Kbot.Common.Test/AssemblyInfo.cs)) against a
shared Kraken key, a shared Gmail account, a shared `state.json` and one process-wide InMemory DB
named `"KrakenDb"`. The DB migration throws on the InMemory provider in every test and is swallowed by
the retry loop — 25 s of the suite's runtime spent failing silently.

## Scope

### In scope — pure functions (no dependencies)
1. **HMAC-SHA512 signing** — `KrakenApi.CreateSignature` against Kraken's published test vector. This
   is the one place where a silent error breaks every authenticated call.
2. **DTO parsers** — `TickerInfoUnparsed.Parse`, `ClosedOrderUnparsed.ToModel`, `OrderParser.ToOrder`,
   with captured real Kraken payloads as fixtures (commit them under `test/Fixtures/`). Include the
   awkward cases: market order with `descr.price = 0` (M-7), unknown status, `stop-loss` type (M-9),
   canonical vs alias pair keys (I-3).
3. **Options validators** — every rule of all five validators (this is how H-10 survived). Coordinate
   with **P1-07**, which adds the first ones.
4. **State persistence** — `DcaStateHandler`/`JsonStateStore` load, save, missing directory, corrupt
   JSON, truncated JSON, unreadable file. Note the existing test deletes the wrong file
   (`state.json` vs `state/state.json`) — fix that. Use a per-test temp directory, never the CWD.
5. **CSV / HTML generation** — including the `NaN` path (M-1) and the culture cases (H-1, if
   **P2-02** is merged assert against its fixtures).

### In scope — behaviour (needs P3-01 / P3-02)
6. **Watermark / incremental fetch** — never asserted today; `MailGenereateTest` calls it and checks
   nothing. Cover the **P2-07** semantics.
7. **Pagination** (`fetchAll`) — the `ofs += 50` loop and the truncation bug (**P2-06**).
8. **`DcaWorker` decision logic** — via `DcaPlanner` (**P3-02**), replacing the live-exchange test
   entirely. Delete `DcaWorkerTest`'s live path once the planner tests cover the same ground.
9. **Daily/monthly scheduling** — `HourOfDay` UTC semantics (M-23), the "already past today" branch,
   the report-day branch, the December rollover.
10. **Nonce monotonicity** — assert strictly increasing nonces across concurrent calls (pairs with
    **P4-05**; if that is not merged, write the test as the specification and mark it
    `[Ignore("enabled by P4-05")]` — do not leave it silently absent).

### Test-infrastructure fixes (in scope)
- Replace the process-wide InMemory DB name `"KrakenDb"` with a per-test unique name
  (`Guid.NewGuid().ToString()`), or better, use `Microsoft.EntityFrameworkCore.Sqlite` in-memory /
  Testcontainers-Postgres so migrations are actually exercised.
- Skip `MigrationService` in tests (register a no-op `IHostedService`) so the suite stops burning 25 s.
- Reconsider `Parallelize(Workers = 0)`: keep parallelism, but remove the shared mutable state that
  makes it unsafe (temp dirs, unique DB names, no shared `state/`).

### Out of scope
- Any production behaviour change. If a test proves a bug that no plan covers, open an issue and
  reference it in the PR — do not fix it here.
- Live-exchange test gating → **P1-01**.

## Acceptance criteria

- Line coverage on `Kbot.Common` and `Kbot.DcaService` above a threshold agreed in the PR (suggest
  70 % as the first bar, measured by the coverlet run CI already collects — **P1-05** is merged
  (#49), so `ci.yml` uploads `coverage.cobertura.xml` in the `test-results` artifact today. No
  threshold is enforced yet; adding one is this plan's job.
- Every row of the REVIEW.md Appendix table is either covered or explicitly listed in the PR as
  deferred with a reason.
- The suite runs offline, in parallel, in under ~30 s.
- No test writes to the repo working directory.

## Verification

```bash
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi" --collect:"XPlat Code Coverage"
reportgenerator -reports:'**/coverage.cobertura.xml' -targetdir:/tmp/cov -reporttypes:TextSummary && cat /tmp/cov/Summary.txt
# offline check:
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"   # with the network disabled
```
