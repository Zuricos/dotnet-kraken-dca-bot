# P1-01 · Gate the live-trading tests

|  |  |
|---|---|
| **Findings** | C-1 (partially M-25) |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `test/p1-c1-gate-live-trading-tests` |
| **Effort** | S (~2–3 h) |
| **Depends on** | — (start immediately) |
| **Blocks** | P1-05 (CI must have a safe default filter), P3-03 |
| **Conflict surface** | `test/**` — shared with P1-10 (`TimeComputeTest.cs` only). Do not touch that file here. |

## Problem

`dotnet test Kbot.sln` spends real money. Three test classes hit production endpoints:

- [test/Kbot.Common.Test/KrakenApiTest.cs:56-80](../../test/Kbot.Common.Test/KrakenApiTest.cs#L56-L80) —
  `TestSendAndCancelBuyOrder` sends a real `OrderRequest` (`Volume = 0.00005`) to
  `https://api.kraken.com` and depends on a follow-up `CancelOrder` succeeding. If the process dies
  between the two, or the cancel is rate-limited (`KrakenApi.PostPrivateAsync` returns `null` on
  HTTP 429 → `CancelOrder` returns `false`), the order rests on the book.
- [test/Kbot.DcaService.Test/DcaWorkerTest.cs:50-80](../../test/Kbot.DcaService.Test/DcaWorkerTest.cs#L50-L80) —
  starts the **entire production `DcaWorker`** and lets it run a full investment cycle. With
  `AskMultiplier = 1.00001` (above ask) the order fills at market.
- [test/Kbot.MailService.Test/MailGenereateTest.cs:50-81](../../test/Kbot.MailService.Test/MailGenereateTest.cs#L50-L81) —
  sends real mail through the maintainer's Gmail app password, with zero assertions.

`TestQueryBalance` and `TestQueryClosedOrders` are read-only but still require live credentials, so
they fail on any machine without user-secrets and train maintainers to ignore red suites.

## Scope

### In scope
1. Add `[TestCategory("LiveExchange")]` to every test that performs a **write** against Kraken or
   sends mail, and `[TestCategory("LiveApi")]` to every test that merely needs live credentials
   (balance, closed orders, ticker, holiday fetch).
2. Add a hard runtime guard in addition to the category, so a manual `--filter` cannot arm them by
   accident. Put one shared helper in each test project (or a shared `test/Directory.Build.props`
   linked file):
   ```csharp
   internal static class LiveGuard
   {
     internal static void RequireOptIn()
       => Assert.Inconclusive(Environment.GetEnvironmentVariable("KBOT_ALLOW_LIVE_TRADING") == "1"
            ? null!
            : "Skipped: set KBOT_ALLOW_LIVE_TRADING=1 to run live-exchange tests.");
   }
   ```
   Call it as the first statement of every `LiveExchange` test (and in `[TestInitialize]` where the
   whole class is live).
3. Add a repo-root `.runsettings` that excludes the live categories by default, and reference it from
   `Directory.Build.props` / the test csprojs so plain `dotnet test` is safe:
   ```xml
   <RunConfiguration>
     <TestCaseFilter>TestCategory!=LiveExchange&amp;TestCategory!=LiveApi</TestCaseFilter>
   </RunConfiguration>
   ```
4. Replace `TestSendAndCancelBuyOrder`'s value with a hermetic test: inject a stub
   `HttpMessageHandler` into `KrakenApi` and assert the serialized request body, the `API-Key` /
   `API-Sign` headers, the URL path and the nonce. If `KrakenApi` cannot yet accept an
   `HttpMessageHandler`, add a minimal `internal` constructor overload taking `HttpClient` plus
   `[assembly: InternalsVisibleTo("Kbot.Common.Test")]` — the full lifetime fix is P4-03.
5. Give the mail tests real assertions on the generated HTML/CSV instead of sending, or mark them
   `LiveExchange` and leave the send path for P3-03 to replace.
6. Remove `public string? FixOrderId { get; set; }` from
   [src/Kbot.DcaService/DcaWorker.cs:32](../../src/Kbot.DcaService/DcaWorker.cs#L32) **only if**
   `DcaWorkerTest` no longer needs it after gating; otherwise leave it and note it for P3-02.
7. Add a `## Running the tests` warning block to `README.md` (2–5 lines) stating that
   `LiveExchange` tests trade real money and how to opt in.

### Out of scope
- `TimeComputeTest.cs` determinism → **P1-10**.
- New unit tests for signing, parsers, state, validators → **P3-03**.
- `Parallelize(Workers = 0)` / shared-fixture problems → **P3-03**.
- CI wiring of the filter → **P1-05** (but land the `.runsettings` here so CI can rely on it).

## Acceptance criteria

- `dotnet test Kbot.sln` on a machine **with** valid user-secrets places **no** order and sends
  **no** mail; the live tests report as skipped/inconclusive.
- `KBOT_ALLOW_LIVE_TRADING=1 dotnet test Kbot.sln --filter "TestCategory=LiveExchange"` still runs
  them (verify by inspection, not by executing against a funded account).
- At least one new hermetic test asserts the signed request shape without network access.
- `dotnet build Kbot.sln` clean.

## Verification

```bash
dotnet build Kbot.sln
dotnet test Kbot.sln                       # must be green and must not touch the network for writes
dotnet test Kbot.sln --list-tests | head -40
dotnet csharpier check .
```
