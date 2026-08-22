# Code Review — dotnet-kraken-dca-bot

**Date:** 2026-08-21 · **Commit:** `935ca73` · **Branch:** `main`
**Scope:** full repository (~3 125 LOC C# across 6 projects), CI, Docker, docs
**Method:** four parallel module reviews (Common / DcaService / MailService / Infrastructure) plus a dedicated cross-module interface pass.

---

## 1. Executive Summary

This is a well-structured, readable hobby project with a genuinely good core idea. The DCA scheduling maths in `TimeComputeService` is self-correcting and elegant: `interval = timeUntilNextTopUp / (balance / costPerMinOrder)`, recomputed at every fill, produces a uniform schedule that exhausts the balance exactly at the next top-up. The code is consistently formatted (csharpier), uses modern C# throughout (file-scoped namespaces, records, primary constructors, `required` members), has real options validation with `ValidateOnStart()`, and correctly uses `IDbContextFactory` to avoid the captive-`DbContext` trap that catches most projects of this shape.

The problem is not the happy path — it is that **almost no failure path is guarded**, and the module boundary actively encourages that. `Kbot.Common.KrakenClient` signals every error with an in-band sentinel (`[]`, `0.0`, `false`, `null`) that is indistinguishable from a legitimate result, and neither consumer checks any of them. This single design decision is the root cause of four of the five critical findings below.

**Build and test state (verified):**

| Command | Result |
|---|---|
| `dotnet build Kbot.sln` | ✅ **exit 0**, 0 warnings, 0 errors across all 6 projects |
| `dotnet test Kbot.sln` | ❌ **exit 1** — 9 of 19 tests fail (Common 4/9, Dca 2/7, Mail 3/3) |

The test failures are not all regressions: most are the suite failing to reach the live Kraken API and live Gmail SMTP that it depends on. That dependency is itself the most serious finding in the repository.

> ⚠️ **`dotnet test` places real buy orders on the live Kraken exchange and sends real email.** There is no sandbox, no mock, and no opt-in guard. This review's test run was safe only because no user-secrets exist on this machine (verified: neither `b9200fc6-…` nor `4e88eea9-…` is present under `~/.microsoft/usersecrets/`), so authentication failed. **On a maintainer's machine the same command spends money.**

### Verdict

| Dimension | Assessment |
|---|---|
| Architecture & readability | **Good** — clean separation, modern idioms, consistent style |
| Core algorithm | **Good** — mathematically sound and self-correcting |
| Failure handling | **Poor** — sentinel-value error protocol, no retries, no backoff, unguarded loops |
| Numeric correctness | **Poor** — `double` for money, culture-sensitive parsing throughout |
| Test safety & coverage | **Unacceptable** — trades real money; the critical logic has zero coverage |
| CI/CD | **Poor** — no build, test, or lint step anywhere; images publish unvalidated |
| Docs accuracy | **Stale** — wrong database, wrong license, three disagreeing version numbers |

**Not safe to run unattended with real money until C-1 … C-5 are fixed.**

---

## 2. Repository Map

```
Kbot.Common        (≈700 LOC)  Kraken REST client, DTOs, HolidayService, shared Options
├── Api/           KrakenApi (HTTP + HMAC-SHA512 signing) · KrakenClient (semantic wrapper)
├── Dtos/          "Unparsed → Parse()" DTO pairs for Kraken's string-encoded numerics
├── Conversion/    OrderParser: ClosedOrderInfo → Order
├── Helpers/       ApiUtility · HolidayService (IHostedService, date.nager.at)
├── Models/        Order (ALSO the EF entity) · PublicHoliday
└── Options/       Secrets · CultureOptions (+ validators)

Kbot.DcaService    (≈400 LOC)  the trading loop
├── DcaWorker.cs           BackgroundService: balance → price → interval → order → wait
├── Utility/               TimeComputeService (schedule maths) · DcaStateHandler (state/state.json)
└── Options/               OrderOptions · BalanceOptions · WaitOptions (+ validators)

Kbot.MailService   (≈650 LOC)  reporting
├── DailyReporter.cs       BackgroundService: sleep-until-hour → fetch → persist → mail
├── MonthlyReporter.cs     CSV report, watermarked by state/history.json
├── Database/              KrakenDbContext (PostgreSQL) · MigrationService
└── Utility/               OrderService · HtmlService · CsvService · MailSenderService (Gmail SMTP)

test/              (≈450 LOC)  3 MSTest projects — non-hermetic, hit live Kraken + Gmail
docker/            2 Dockerfiles, example-compose.yaml, stack.env
.github/workflows/ docker-dca.yml, docker-mail.yml  (publish only — no build/test/lint)
```

---

## 3. Priority Action List

Work top to bottom. Items 1–5 are prerequisites for running the bot unattended.

| # | Action | Findings | Effort |
|---|---|---|---|
| 1 | Gate or delete the live-trading tests; add `[TestCategory("LiveExchange")]` + env guard | C-1 | S |
| 2 | Replace `KrakenClient`'s sentinel returns with `Result<T>`/nullable; guard both call sites | C-2, C-3 | M |
| 3 | Clamp `DefaultTopupDayOfMonth` to month length; save state *before* post-order bookkeeping | C-4 | S |
| 4 | Wrap both worker loops in try/catch + exponential backoff; never let a cycle stop the host | C-5 | S |
| 5 | Parse and format every wire value with `InvariantCulture`; move money to `decimal` | H-1, H-2 | M |
| 6 | Add a `ci.yml` with build + test + csharpier; gate publish on it | H-3 | S |
| 7 | Move `.dockerignore` to the repo root; drop `<None Update="secrets.json">` | H-4 | S |
| 8 | Derive the monthly watermark from data actually reported; fail the report if the fetch failed | H-5 | M |
| 9 | Harden `HistoryState` like `DcaStateHandler`; make both writes atomic | H-6 | S |
| 10 | Fix `HolidayService.IsHoliday` empty-cache throw; add a bundled fallback | H-7 | S |
| 11 | Fix `KrakenClient`/`KrakenApi` lifetimes (`AddHttpClient`, drop `KrakenClient : IDisposable`) | H-8 | S |
| 12 | Propagate pagination failures instead of silently truncating | H-9 | S |
| 13 | Tighten options validators (`> 0`, not `>= 0`); remove `ports:` and the default DB password | H-10, H-11 | S |
| 14 | Fix `ComputeTimeUntilNextTopUp_Yesterday`; inject `TimeProvider` | H-12 | M |
| 15 | Update README (PostgreSQL, AGPL-3.0), reconcile VERSION/CHANGELOG/compose tags | M-* | S |

---

## 4. Critical Findings

These are reachable in normal operation and can lose money or crash-loop the service.

### C-1 · Tests place real buy orders on the live exchange and send real email

> ✅ **Resolved** by [P1-01](docs/plans/p1-01-c1-gate-live-trading-tests.md), merged as `58c2262`
> (2026-08-22). The live tests are category-tagged, excluded by `.runsettings` and additionally
> gated on `KBOT_ALLOW_LIVE_TRADING=1`; the signing path and the mail report now have hermetic tests.

**Files:** [KrakenApiTest.cs:56-80](test/Kbot.Common.Test/KrakenApiTest.cs#L56-L80) · [DcaWorkerTest.cs:50-80](test/Kbot.DcaService.Test/DcaWorkerTest.cs#L50-L80) · [MailGenereateTest.cs:50-81](test/Kbot.MailService.Test/MailGenereateTest.cs#L50-L81)

`TestSendAndCancelBuyOrder` builds a real `OrderRequest` (`Volume = 0.00005`, `Price = currentPrice / 2`) and calls `SendOrder` against `https://api.kraken.com`. `DcaWorkerTest` goes further and starts the **entire production `DcaWorker`**, letting it run a full investment cycle. Both then depend on a follow-up `CancelOrder` succeeding — the test's own assertion message concedes the failure mode: `"Cancel order failed! Do it manually!"`. If the process dies between send and cancel, or the cancel rate-limits ([KrakenApi.cs:80](src/Kbot.Common/Api/KrakenApi.cs#L80) returns `null` on HTTP 429 → `CancelOrder` returns `false`), the order is left resting on the book.

The DCA worker uses `AskMultiplier = 1.00001` from `stack.env` — *above* ask — so a worker-placed order **fills at market**. The three mail tests send real messages through the maintainer's Gmail app password and contain **zero assertions**.

**Why it matters:** `dotnet test` is the most reflexive command any contributor, IDE "Run All Tests" button, or future CI job will run. It moves real funds.

**Action:** Gate behind `[TestCategory("LiveExchange")]` plus an env guard (`KBOT_ALLOW_LIVE_TRADING=1`), filtered out by default — or delete them. Replace with unit tests over an injected `HttpMessageHandler` asserting the serialized body, headers, and nonce.

---

### C-2 · A failed ticker call returns `0.0`, which the worker trades on

> ✅ **Resolved** by [P1-02](docs/plans/p1-02-c2-c3-guard-worker-sentinels.md), merged as PR #43. The ticker
> entry is read by value rather than by the requested pair name, the worker skips the cycle on a
> non-positive price, and the interval computation can no longer yield `Zero`, `Infinity` or `NaN`.
> The `0.0` sentinel itself survives until P2-01 replaces the protocol.

**Files:** [KrakenClient.cs:52,66](src/Kbot.Common/Api/KrakenClient.cs#L52) · [DcaWorker.cs:66-69](src/Kbot.DcaService/DcaWorker.cs#L66-L69) · [TimeComputeService.cs:64-65](src/Kbot.DcaService/Utility/TimeComputeService.cs#L64-L65)

`GetCurrentCryptoPrice` returns `0.0` on *every* error path — HTTP failure, Kraken error array, and notably `response.Result![pair]` throwing `KeyNotFoundException` when Kraken returns its canonical pair name instead of the requested alias (request `XBTUSD`, get key `XXBTZUSD`). `DcaWorker` never checks the result. The cascade:

```
price = 0
  → askPrice = 0
  → costForVolume = Math.Ceiling(0) / 100 = 0
  → balance guard `balanceFiat < 0` is FALSE, so the cycle proceeds
  → ComputeNextInvestmentInterval: balanceFiat / 0 = +Infinity
  → TimeSpan / +Infinity = TimeSpan.Zero        (verified — no exception)
  → nextOrderTime = LastInvestmentTime (in the past)
  → order placed IMMEDIATELY at price 0, and again every MinWaitTime (10 s)
```

With `Type=Limit` Kraken rejects price 0 and the bot enters an unbounded 10-second retry loop against a private endpoint — a fast route to a rate-limited or suspended API key. **With `Type=Market` the price field is not authoritative and the order executes**: the bot buys `MinOrderVolume` every 10 seconds until the fiat balance is gone. `OrderOptions.Type` defaults to `Market` (enum value 0) and is not validated, so omitting `OrderOptions__Type` selects the dangerous variant.

This is not exotic: simply configuring `CryptoPair=XBTUSD` triggers it on the first cycle.

**Action:**
```csharp
var currentCryptoPrice = await krakenClient.GetCurrentCryptoPrice(CryptoPair);
if (currentCryptoPrice <= 0)
{
    logger.LogError("Ticker for {Pair} unavailable (price {Price}); skipping cycle.", CryptoPair, currentCryptoPrice);
    return waitOptions.Value.MaxWaitTime;
}
```
Guard the divisor in `ComputeNextInvestmentInterval`, and index the ticker response by value rather than by the request string:
```csharp
var tickerInfo = response.Result!.Values.Single().Parse();
```

---

### C-3 · A failed balance call returns `[]`, and the worker indexes it directly

> ✅ **Resolved** by [P1-02](docs/plans/p1-02-c2-c3-guard-worker-sentinels.md), merged as PR #43. The fiat
> balance is looked up with `TryGetValue`; a missing asset logs the available keys and costs one
> skipped cycle instead of stopping the host.

**Files:** [KrakenClient.cs:23,36](src/Kbot.Common/Api/KrakenClient.cs#L23) · [DcaWorker.cs:64](src/Kbot.DcaService/DcaWorker.cs#L64)

`CheckBalance` returns an empty dictionary on any error. `DcaWorker` does `balance[FiatCode]` with no guard → `KeyNotFoundException`. The same throw occurs if Kraken simply omits a zero-balance asset, or if `CultureOptions.Fiat` does not match Kraken's asset code (`USD` vs `ZUSD`, `EUR` vs `ZEUR`).

Nothing catches it. It propagates out of `ExecuteAsync`; .NET's default `BackgroundServiceExceptionBehavior.StopHost` stops the host; `restart: unless-stopped` restarts the container; the cycle repeats. **A single transient network blip becomes a crash-loop** — and each restart with a reset state file is an opportunity for an unscheduled buy (see C-4, H-6).

This is exactly what the test run reproduced: `KeyNotFoundException: The given key 'CHF' was not present in the dictionary.`

**Action:**
```csharp
var balance = await krakenClient.CheckBalance();
if (!balance.TryGetValue(FiatCode, out var fiatBalance))
{
    logger.LogError("Fiat asset {Fiat} not in Kraken balance (keys: {Keys}).", FiatCode, string.Join(",", balance.Keys));
    return waitOptions.Value.MaxWaitTime;
}
var balanceFiat = fiatBalance - ReserveFiat;
```

---

### C-4 · `DefaultTopupDayOfMonth` 29–31 throws *after* the order is sent but *before* state is saved → duplicate buys

> ✅ **Resolved** by [P1-03](docs/plans/p1-03-c4-topup-day-clamp-and-state-order.md), merged as PR #45.
> All three date constructions go through a helper that clamps the day to the month's length and
> stamps `DateTimeKind.Utc`, the month rollover no longer special-cases December, the validator
> accepts 1–28 with a message that says why, and `InvestmentCycle` persists the state immediately
> after a successful order — before anything that could throw.

**Files:** [TimeComputeService.cs:10,25,29](src/Kbot.DcaService/Utility/TimeComputeService.cs#L10) · [BalanceOptions.cs:16](src/Kbot.DcaService/Options/BalanceOptions.cs#L16) · [DcaWorker.cs:49,100-107](src/Kbot.DcaService/DcaWorker.cs#L100-L107)

All three `new DateTime(...)` calls pass `topUpDayOfMonth` unvalidated against the target month's length; `new DateTime(2026, 4, 31)` and `new DateTime(2026, 2, 30)` both throw `ArgumentOutOfRangeException`. `BalanceOptionsValidator` accepts anything in **1–31**, so this is a legal configuration. (Note `MailOptionsValidator` correctly caps the same concept at 28 — the two services disagree.)

The dangerous part is the ordering inside `InvestmentCycle`:

```
SendOrder succeeds            (line 100)   ← money spent
LastInvestmentTime updated    (line 103)   ← in memory only
ComputeTimeUntilNextTopUp     (line 104)   ← THROWS
                                            ↓
       exception unwinds past State.Save() (line 49) and out of ExecuteAsync
```

Concrete scenario — `DefaultTopupDayOfMonth = 31`, bot running in April. First successful order → `ComputeNextTopUpTime(April, 31)` → throw → host stops → Docker restarts → `DcaStateHandler.Load()` returns the **pre-order** `LastInvestmentTime` → `nextOrderTime` is still in the past → **another buy** → another throw → another restart. This repeats until an operator notices or the balance guard trips.

With day 31 the bot also cannot start at all during Apr/Jun/Sep/Nov (the throw happens at [DcaWorker.cs:39](src/Kbot.DcaService/DcaWorker.cs#L39)).

**Action:** Clamp, tighten, and reorder:
```csharp
private static DateTime AtDayOfMonth(int year, int month, int day) =>
    new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)), 0, 0, 0, DateTimeKind.Utc);
```
Restrict `DefaultTopupDayOfMonth` to 1–28 in the validator unless clamping is implemented, and persist state immediately after a successful `SendOrder`, before any further computation.

---

### C-5 · Neither worker loop has any exception handling

> ✅ **Resolved** by [P1-04](docs/plans/p1-04-c5-worker-loop-resilience.md), merged as PR #46. Both
> `ExecuteAsync` loops now guard their body, log the caught exception with its stack trace and pace
> the retry with a capped exponential backoff instead of letting the throw stop the host; the DCA
> startup path runs inside that same guard, cancellation exits without an error, and the restart
> notification mail is rate-limited to one an hour so a restart loop cannot flood the inbox. The
> individual throws listed below are still owned by their own plans — this only guarantees the loops
> survive them.

**Files:** [DcaWorker.cs:45-58](src/Kbot.DcaService/DcaWorker.cs#L45-L58) · [DailyReporter.cs:17,19](src/Kbot.MailService/DailyReporter.cs#L17)

Neither `ExecuteAsync`'s `while` body nor `InvestmentCycle` has a `try`/`catch`. Every throw identified in this review terminates the host:

- `KeyNotFoundException` (C-3)
- `ArgumentOutOfRangeException` from `TimeComputeService` (C-4)
- `ArgumentException` from `TimeSpan / NaN` when `balanceFiat == 0 && costForVolume == 0`
- `DirectoryNotFoundException` from `DcaStateHandler` when `state/` is missing
- `InvalidOperationException` from `HolidayService.IsHoliday` on an empty cache (H-7)

In MailService the first two `await`s in `ExecuteAsync` are likewise unguarded, and `SendMail` deliberately rethrows. With `restart: unless-stopped`, a transient Gmail outage becomes an unbounded restart loop — and every iteration that gets far enough sends another *"DCA - Restart of Container"* mail, flooding the inbox.

**Action:**
```csharp
while (!stoppingToken.IsCancellationRequested)
{
    TimeSpan waitTime;
    try { waitTime = await InvestmentCycle(stoppingToken); State.Save(); }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
    catch (Exception ex)
    {
        logger.LogError(ex, "Investment cycle failed; backing off.");
        waitTime = _backoff.Next();   // exponential, capped at MaxWaitTime
    }
    ...
}
```
A trading bot must survive transient faults. Today every fault is a process restart, and every restart is an opportunity for an unrecorded duplicate buy.

---
## 5. High-Severity Findings

### H-1 · Every money value is parsed with the ambient culture

**Files:** [KrakenClient.cs:30](src/Kbot.Common/Api/KrakenClient.cs#L30) · [ClosedOrderInfo.cs:28-31](src/Kbot.Common/Dtos/ClosedOrderInfo.cs#L28-L31) · [TickerInfo.cs:40-76](src/Kbot.Common/Dtos/TickerInfo.cs#L40-L76) · [Balance.cs:23-26](src/Kbot.Common/Dtos/Balance.cs#L23-L26) · [CsvService.cs:20-22](src/Kbot.MailService/Utility/CsvService.cs#L20-L22) · [MailOptions.cs:14](src/Kbot.MailService/Options/MailOptions.cs#L14)

Kraken returns all numerics as strings (`"vol":"0.00005"`). Every one is converted with `double.Parse(s)` / `int.Parse(s)` with **no `IFormatProvider`**, binding to `CultureInfo.CurrentCulture`. There is not a single `InvariantCulture` in `Kbot.Common`. Verified on .NET 10 with the repo's actual configured value:

| Culture | `double.Parse("0.00005")` |
|---|---|
| Invariant / `de-CH` | `0.00005` ✅ |
| `de-DE` | **`5`** — `.` treated as a group separator → **100 000× error** |
| `fr-FR` | **`FormatException`** |

The same class of bug corrupts the monthly CSV attachment, which uses `$"{...:F8}"` with the ambient culture:

```
[en-US] 1/15/2025 12:00:00 AM +01:00,0.00123456,98765.4,BTC,CHF,Buy,1.2345
[de-DE] 15.01.2025 00:00:00 +01:00,0,00123456,98765,4,BTC,CHF,Buy,1,2345   ← 10 columns, unparseable
```

**Why it is latent today:** the Docker base images set no `LANG`, so containers run invariant. But `CultureOptions.CultureString` exists and is validated — its presence actively invites someone to add `CultureInfo.DefaultThreadCurrentCulture = cultureOptions.CultureInfo`, which would silently corrupt every balance, price, volume, and fee. It is one env var away.

**Action:** Pin `CultureInfo.InvariantCulture` on every parse and every wire/CSV format. Better: a shared `JsonSerializerOptions` with a `JsonConverter<decimal>` that reads Kraken's string-encoded numbers, deleting the entire `*Unparsed` → `Parse()` layer.

---

### H-2 · Money is `double`, end to end into PostgreSQL

**Files:** [Order.cs:14-17](src/Kbot.Common/Models/Order.cs#L14-L17) · [ClosedOrderInfo.cs:93-96](src/Kbot.Common/Dtos/ClosedOrderInfo.cs#L93-L96) · [OrderRequest.cs:10-11](src/Kbot.Common/Dtos/OrderRequest.cs#L10-L11) · [KrakenDbContext.cs:19-22](src/Kbot.MailService/Database/KrakenDbContext.cs#L19-L22)

`Price`, `Volume`, `Cost`, `Fee` are `double`, and `Order` is also the EF entity — the migration maps all four to Postgres `double precision`. `OrderService.GetReportPerDayCalc` then sums `Cost` and `Fee` across hundreds of rows and divides, accumulating representation error in the reported average price and total fees.

Two verified consequences:
- `Math.Round(30000.05, 1)` returns `30000`, not `30000.1` — 30000.05 is not representable.
- `JsonSerializer.Serialize` of `MinOrderVolume = 0.00005` emits `{"volume":5E-05}` — .NET's shortest-round-trip formatter switches to exponent notation below 1e-4. Legal JSON, but the bot is betting on the exchange's number handling for the single field that determines how much it buys.

**Action:** Move to `decimal` in `Order`, `ClosedOrderInfo`, `OrderRequest`, and the ticker DTOs; add `.HasColumnType("numeric(18,8)")` and a migration. `decimal` also serializes without exponent notation, fixing the volume issue for free.

---

### H-3 · CI has no build, test, or lint step — images publish unvalidated

**Files:** [docker-dca.yml:20-68](.github/workflows/docker-dca.yml#L20-L68) · [docker-mail.yml:20-68](.github/workflows/docker-mail.yml#L20-L68) · [dependabot.yml:9-16](.github/dependabot.yml#L9-L16)

These are the only two workflows. Each has exactly two jobs — `compute-version` and `build-and-publish`. Neither runs `dotnet build`, `dotnet test`, `dotnet format`, or `csharpier --check`; grepping the workflow directory for `dotnet` returns nothing. A change that breaks the interval maths, corrupts state serialization, or breaks HMAC signing ships straight to `ghcr.io/zuricos/kraken-dca-service:latest`.

Compounding this, **Dependabot's PRs trigger nothing at all**. It watches `directory: "."` and under central package management modifies `/Directory.Packages.props` — which matches none of the workflows' `paths:` filters (`src/Kbot.Common/**`, `src/Kbot.<Service>/**`, `docker/**`), despite being `COPY`'d into both images. So the monthly grouped bump (commit `935ca73` touched **14 packages at once**) gets zero validation *and* builds no new image — GHCR keeps shipping the old versions while `main` claims the new ones, until an unrelated `src/` commit silently rebuilds with 14 untested upgrades.

**Related:** the version gate `if: ${{ needs.compute-version.outputs.needsBump }}` uses raw string truthiness — GitHub casts the literal string `"false"` to `true`. Unless the external action emits an *empty* string, this gate never blocks. Use `== 'true'`.

**Action:** Add a `ci.yml` on `push` + `pull_request` for **all** paths running restore, build `-warnaserror`, and `dotnet test --filter "TestCategory!=LiveExchange"`. Make `build-and-publish` depend on it via `needs:`. Add `Directory.Packages.props`, `Directory.Build.props`, `nuget.config` to the publish filters, and add `github-actions` + `docker` ecosystems to Dependabot.

---

### H-4 · `.dockerignore` is in the wrong directory and is completely inert

> ✅ **Resolved** by [P1-06](docs/plans/p1-06-h4-dockerignore-and-secret-copy.md), merged as PR #48.
> The file now lives at the repository root, where BuildKit actually reads it, so the build context
> went from 41.25 MB (the whole repo, `.git/` and all `bin`/`obj` included) to 380 KB containing
> only what the Dockerfiles copy. Four inherited patterns were replaced by ones that match this
> repo's filenames: `docker` (`**/Dockerfile*` and `**/compose*` matched nothing — they need those
> exact prefixes, not `Kbot.*.Dockerfile` / `example-compose.yaml` — and the directory also holds
> `stack.env`), `**/*.env` (`**/.env` matched nothing either) and `**/secrets*template.json`
> (`**/secrets-template.json` did match the two files spelled that way, but missed the
> `secrets.template.json` spelling). The csproj's `secrets.json` / `state.json` copy
> directives are deleted — and because `Microsoft.NET.Sdk.Worker` globs `**/*.json` into `Content`
> with `CopyToPublishDirectory` set, deleting them was not enough on its own: `Directory.Build.props`
> now excludes `**/*secrets.json` and `**/*state.json` from that glob, which also closes the same
> leak in Kbot.MailService, whose csproj never had any `<None>` items. A `GuardLocalOnlyFilesOutOfOutput`
> MSBuild target in the same file fails the build if either service ever marks a `*secrets.json` /
> `*state.json` for copying again.

**Files:** [docker/.dockerignore](docker/.dockerignore) · [docker-dca.yml:52-53](.github/workflows/docker-dca.yml#L52-L53) · [Kbot.DcaService.csproj:19-21](src/Kbot.DcaService/Kbot.DcaService.csproj#L19-L21)

Both workflows pass `context: .` (repo root) with `dockerfile: docker/Kbot.*.Dockerfile`. BuildKit resolves `<dockerfile-path>.dockerignore` first, then `<context>/.dockerignore`. **Neither exists** — verified: there is no `docker/Kbot.DcaService.Dockerfile.dockerignore`, and `ls .dockerignore` at the root returns *No such file*. The only `.dockerignore` in the tree is `docker/.dockerignore`, a path Docker never consults for this context.

So its careful entries — `**/secrets.json`, `**/appsettings.Development.json`, `**/bin`, `**/obj`, `**/.git` — have **no effect**. The entire repo including `.git/` history and all build output is uploaded into the build context.

This matters because [Kbot.DcaService.csproj:19-21](src/Kbot.DcaService/Kbot.DcaService.csproj#L19-L21) explicitly declares `<None Update="secrets.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory>`, so a developer's local `secrets.json` is copied to the publish output *by design* and would land inside the shipped image via `COPY src/ ./`.

**Action:** Move the file to the repo root as `/.dockerignore`. Delete the `<None Update="secrets.json">` and `<None Update="state.json">` items from the csproj — secrets should arrive only via `/run/secrets/dca-secrets`, which the config setup already handles, and the state file actually lives at `state/state.json`.

---

### H-5 · The monthly watermark advances past data that was never fetched

**Files:** [MonthlyReporter.cs:60-63](src/Kbot.MailService/MonthlyReporter.cs#L60-L63) · [MailSenderService.cs:95-111](src/Kbot.MailService/Utility/MailSenderService.cs#L95-L111) · [DailyReporter.cs:38-39,50-53](src/Kbot.MailService/DailyReporter.cs#L38-L39)

`SendReport` sets the new watermark to `DateTimeOffset.UtcNow` — a wall-clock value — while the report content comes from whatever happens to be in the database. **`SendReportMail` never fetches from Kraken**; the only fetch lives in `SendMailWithClosedOrdersLast24Hours`, which `SendDailyMail` wraps in a `try/catch` that *swallows* the failure and then proceeds to `SendReportAsync()` regardless.

So on report day, if Kraken or the DB is unavailable during the daily fetch: the daily mail is skipped and logged, the monthly report runs against a database missing the last day(s) of orders, still sends, and **still advances `LastReportedOrderTimeStamp` to now**. Next month starts from that timestamp. Those orders appear in **no monthly CSV, ever** — silently.

**Action:** Derive the watermark from the data actually reported, and let the fetch failure abort the report:
```csharp
public async Task<DateTimeOffset?> SendReportMail(DateTimeOffset startDate, CancellationToken ct)
{
    await orderService.FetchClosedOrdersAndSave(ct);   // let it throw → watermark not advanced
    var orders = await orderService.QueryClosedOrdersFromDatabase(query);
    if (orders.Count == 0) return null;
    ... send ...
    return orders.Max(o => o.CloseTimeStamp) ?? startDate;
}
```
`QueryClosedOrdersFromDatabase` already treats `EndDate` as exclusive, so a max-close-time watermark composes correctly.

**Related (M):** the *daily* window is pure wall-clock `[UtcNow-1d, UtcNow)` with no watermark at all, so any missed run silently drops a day from every daily mail.

---

### H-6 · State files: non-atomic writes, and the hardening applied to one was never applied to the other

**Files:** [DcaStateHandler.cs:8,14-60](src/Kbot.DcaService/Utility/DcaStateHandler.cs#L14-L60) · [HistoryState.cs:9-27](src/Kbot.MailService/Models/HistoryState.cs#L9-L27)

Commit `9940b7a` ("Handle unreadable state file by deleting and creating a new one") hardened `DcaStateHandler` against corrupt JSON. **`HistoryState` — which shares the same `state:/app/state` Docker volume — was left untouched.** A power loss mid-`WriteAllText` leaves a partial file; `HistoryState.Load()` then throws `JsonException` from the *unguarded* [DailyReporter.cs:19](src/Kbot.MailService/DailyReporter.cs#L19), making the mail service permanently unstartable until the user manually deletes the file inside the volume.

Three further problems in `DcaStateHandler` itself:

1. `File.WriteAllText` is **not atomic** — a crash mid-write leaves truncated JSON.
2. The `catch` swallows *everything* (including `IOException`/`UnauthorizedAccessException`), deletes the file, and returns `LastInvestmentTime = DateTime.MinValue`. Nothing is logged — the class has no logger. That sentinel means `nextOrderTime` is in year 1, i.e. always in the past → **an immediate unscheduled buy** on the next cycle. A truncated write is silently converted into a purchase the schedule did not call for.
3. `StateFilePath = "state/state.json"` is relative to the CWD and the directory is never created. Both `File.WriteAllText` and `File.Delete` throw `DirectoryNotFoundException` when the parent is missing — so `Save`'s catch block rethrows *out of the catch*, unhandled. This works in Docker only because the Dockerfile does `mkdir /app/state`; a local `dotnet run` crashes the host on the first save.

**Action:** Unify both into one `JsonStateStore<T>` helper in `Kbot.Common` so the hardening cannot drift again. Write atomically, and never silently fall back to `MinValue`:
```csharp
Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
var tmp = _path + ".tmp";
File.WriteAllText(tmp, json);
File.Move(tmp, _path, overwrite: true);   // atomic replace on the same volume
```
On a corrupt read, log at `Error`, move the bad file aside as `state.json.corrupt-<ts>` rather than deleting it, and seed `LastInvestmentTime = DateTime.UtcNow` so a state loss cannot cause an immediate purchase.

---

### H-7 · `HolidayService.IsHoliday` throws on an empty cache, taking the host down

**File:** [HolidayService.cs:39,56-62,92](src/Kbot.Common/Helpers/HolidayService.cs#L39)

`Holidays.Keys.Min()` is called unconditionally before any emptiness check, and `FetchHolidaysFromRemote` swallows **all** exceptions. So if `date.nager.at` is unreachable during `StartAsync`, the host starts successfully with an empty cache; the first `IsHoliday` call throws `InvalidOperationException: Sequence contains no elements`, which escapes `ComputeNextTopUpTime` → [DcaWorker.cs:39](src/Kbot.DcaService/DcaWorker.cs#L39) → host shutdown **on the very first cycle**. A third-party holiday API being briefly down crash-loops the trading bot.

Two further defects in the same class:
- `UpdateCachedHolidays` calls `.Wait()` (sync-over-async) and then **unconditionally** evicts `Min()`. If the refresh fetch failed, the cache goes `{Y, Y+1}` → `{Y+1}`; the guard `Min() < utcNow.Year` is then false and **the refresh is never retried**. Every date in `Y+2` silently reports "not a holiday", quietly corrupting the top-up schedule.
- `Min()`-then-`TryRemove` is a non-atomic read-modify-write; two concurrent `IsHoliday` calls can evict two years.

**Action:** Guard the empty case, let `StartAsync` propagate (or retry with backoff) rather than continuing with an empty cache, only evict after a *confirmed successful* add, and use `AddOrUpdate` so refreshes take effect. Ship a bundled fallback holiday list so a third-party outage cannot stop trading.

---

### H-8 · `HttpClient` lifetime and disposal ownership are both wrong

**Files:** [KrakenApi.cs:16-19](src/Kbot.Common/Api/KrakenApi.cs#L16-L19) · [KrakenClient.cs:217-220](src/Kbot.Common/Api/KrakenClient.cs#L217-L220) · [Dca ServiceCollectionExtension.cs:19-20](src/Kbot.DcaService/Utility/ServiceCollectionExtension.cs#L19-L20) · [Mail ServiceCollectionExtension.cs:26-31](src/Kbot.MailService/Utility/ServiceCollectionExtension.cs#L26-L31)

Three interlocking problems:

1. `KrakenApi` constructs its own `HttpClient` in a field initializer and is registered `AddTransient`. Both services resolve it from the **root** provider, which tracks root-resolved transient `IDisposable`s until process shutdown — so every resolution permanently leaks an `HttpClient` + handler + connection pool. `HolidayService.FetchHolidaysFromRemote` has the same `new HttpClient()` per call.
2. `KrakenClient.Dispose()` disposes the injected `KrakenApi` — **a dependency it does not own**, which DI will dispose again. Harmless today, but it actively blocks the correct fix: the moment `KrakenApi` becomes a singleton, the first `KrakenClient` disposal kills the shared `HttpClient` and every subsequent call throws `ObjectDisposedException` with no obvious culprit.
3. In MailService the transients are captured by a **singleton** `MailSenderService` — a textbook captive dependency. It works only by accident, because `OrderService` uses `IDbContextFactory` rather than an injected `DbContext`. The declared lifetimes are a lie, and the test project registers `MailSenderService` as *transient*, so tests exercise different lifetimes than production.

**Action:**
```csharp
services.AddHttpClient<KrakenApi>(c => c.BaseAddress = new Uri("https://api.kraken.com"));
```
Drop `IDisposable` from both `KrakenApi` and `KrakenClient`, register the graph as singletons, and do the same for `HolidayService`. Attach `AddStandardResilienceHandler()` while you are there — see H-9.

---

### H-9 · A failed page in `GetClosedOrders` silently returns truncated data → permanent loss

**File:** [KrakenClient.cs:176-185](src/Kbot.Common/Api/KrakenClient.cs#L176-L185)

When `fetchAll: true`, the recursive page fetch checks `if (next != null)` and, if the follow-up page failed, simply returns what it has. The caller sees a normal `ClosedOrders` whose `Count` still reports Kraken's *total* — no indication that pages are missing.

`OrderService.FetchClosedOrdersAndSave` computes the next run's start date as `MAX(CloseTimeStamp)` of what is in the DB. Kraken returns **newest-first**, so a failure on page 2 persists the newest 50 orders, advances the watermark past them, and the older orders in the gap are **never fetched again**. The monthly report is then permanently wrong.

Also here: `Task.Delay(2000).Wait()` blocks a thread-pool thread for 2 s **per page** inside an `async` method, and wraps any fault in `AggregateException` — which the surrounding `catch (Exception e) { ...e.Message... }` renders as the useless "One or more errors occurred." A first-run full-history import is minutes of blocked thread, uncancellable by SIGTERM.

**Action:** Return `null` (or throw) if any page fails so the caller does not advance its watermark. Replace the recursion with a loop, `await` the delay with a token, and hoist the magic numbers:
```csharp
private const int PageSize = 50;
private static readonly TimeSpan HistoryRateLimit = TimeSpan.FromSeconds(2);
```
Also send Kraken's `closetime=close` explicitly — the API default is `both`, so `start`/`end` may match open *or* close time while the local watermark is strictly a close time.

---

### H-10 · Options validators accept degenerate zero values

**Files:** [OrderOptions.cs:27-34](src/Kbot.DcaService/Options/OrderOptions.cs#L27-L34) · [WaitOptions.cs:16-23](src/Kbot.DcaService/Options/WaitOptions.cs#L16-L23)

All numeric checks use `>= 0` rather than `> 0`. Verified by running the built service with no configuration: `Secrets`, `OrderOptions`, `BalanceOptions`, and `CultureOptions` all failed validation — but **`WaitOptions` produced no error at all**, because `MinWaitTime = MaxWaitTime = TimeSpan.Zero` satisfies all three of its rules.

- `MinWaitTime = 0` makes `Task.Delay(TimeSpan.Zero)` a busy loop issuing Kraken `Balance` + `Ticker` calls as fast as the network allows — instant rate-limit, 100 % CPU.
- `MinOrderVolume = 0` or `AskMultiplier = 0` makes `costForVolume = 0`, reproducing C-2. If `balanceFiat` is also exactly 0, the result is `0.0/0.0 = NaN`, and `TimeSpan / double.NaN` throws `ArgumentException` → host crash.

There is no `WaitOptions` section in `appsettings.json`, so an operator who forgets `stack.env` gets the busy loop rather than a startup failure.

**Action:** `> 0` for `MinOrderVolume`, `AskMultiplier`, `MinWaitTime`, `MaxWaitTime`; a sanity band for `AskMultiplier` (`[0.5, 1.5]` — it is a *price* multiplier); `Fee <= 100`. Add all sections with safe defaults to `appsettings.json` so an omitted env var is never silently a zero.

---

### H-11 · Committed database password plus Postgres published on all host interfaces

> ✅ **Resolved** by [P1-08](docs/plans/p1-08-h11-database-credentials-exposure.md), merged as PR #44.
> The `ports` block is gone (a loopback-only mapping is left commented out), `stack.env` carries a
> `<CHANGE_ME>` placeholder for `POSTGRES_PASSWORD` and no connection string at all, and
> `appsettings.json` no longer ships one — the mail service now fails fast with a message naming
> `ConnectionStrings:Kraken`. Operators who already deployed the default must rotate it; removing it
> from the repository does not change an existing database.

**Files:** [stack.env:19,23](docker/stack.env#L19) · [example-compose.yaml:31-32](docker/example-compose.yaml#L31-L32) · [appsettings.json:3](src/Kbot.MailService/appsettings.json#L3)

`POSTGRES_PASSWORD="NotYourK3yNotYourCoin$"` is committed in `stack.env` **and baked into `appsettings.json`**, therefore into the published image. The compose file publishes `ports: - "5432:5432"`, binding Postgres to all host interfaces.

Unlike the Kraken keys and SMTP password — which are correctly kept out of the repo and injected via docker secrets — this credential is in git and is the default every user inherits. The README's target deployment is a Raspberry Pi on a home LAN; anyone on that network, or the internet if the Pi is port-forwarded, can read the user's complete trading history with a password published on GitHub. The `ports` mapping is not needed at all: `mail-service` reaches the DB by service name over the compose network.

**Action:** Delete the `ports:` block (or bind `127.0.0.1:5432:5432`). Replace the literal password with a `<CHANGE_ME>` placeholder. Remove the connection string from `appsettings.json` entirely and require it from the environment.

---

### H-12 · `ComputeTimeUntilNextTopUp_Yesterday` is calendar-dependent and fails today

**File:** [TimeComputeTest.cs:133-168](test/Kbot.DcaService.Test/TimeComputeTest.cs#L133-L168)

The test builds its expectation as `new DateTime(y, m+1, d)` and asserts `result.NextTopUpTime.Day == topUpDay.Day` — but unlike its sibling `_Tomorrow`, it **never applies the weekend/holiday skip loop** to that expectation, while production code does. Today is Friday 2026-08-21, so next month's day-20 is **Sunday 2026-09-20** and the service correctly rolls to **Monday 2026-09-21**. Hence `Assert.AreEqual failed. Expected:<20>. Actual:<21>.`

**The production code is right; the test is wrong.** It fails on roughly 2 days in 7 plus every holiday collision, so the suite is red for reasons unrelated to any code change — which trains maintainers to ignore failures.

**Action:** Apply the same skip loop to the expected value, and remove the calendar dependence entirely by injecting `TimeProvider` (built into .NET 8+) and asserting against hard-coded dates covering weekday, Saturday, Sunday, holiday, and December-rollover cases.

---
## 6. Cross-Module Interface Review

The modules are individually coherent; most of the damage lives in the seams between them. Six contracts are wrong or fragile.

### I-1 · The error-signalling protocol is the root cause of half this review

`Kbot.Common.KrakenClient` uses **four different in-band failure conventions**, none distinguishable from a legitimate result:

| Method | Returns on failure | Indistinguishable from |
|---|---|---|
| `CheckBalance()` | `[]` | an account with no assets |
| `GetCurrentCryptoPrice()` | `0.0` | *(nothing — 0 is never a valid price)* |
| `SendOrder()` | `false` | a rejected order **and** a successfully-placed-but-unparseable one |
| `GetClosedOrders()` | `null` | *(mapped to "0 new orders" by the caller)* |

Every consumer must remember to re-check, and none of them do. C-2, C-3, H-5 and the duplicate-buy exposure are all downstream of this one decision. The `SendOrder` case is the most insidious: `false` is *also* returned when the response failed to parse after successful submission, so `false` does **not** guarantee no order exists.

Compounding it, `HasError` [KrakenClient.cs:194-215](src/Kbot.Common/Api/KrakenClient.cs#L194-L215) is a predicate that *throws* — `ArgumentNullException` for a null response but `return true` for an API error, two channels for the same condition — and logs `"Could not query the balance"` for **every** endpoint, so a failed closed-orders sync is reported as a balance failure.

**Action — the single highest-value change in this review:**
```csharp
public readonly record struct KrakenResult<T>(T? Value, IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Errors.Count == 0 && Value is not null;
}
```
Minimum viable alternative: make the signatures nullable (`Task<double?>`, `Task<IReadOnlyDictionary<string,decimal>?>`) so the compiler forces both consumers to handle the failure branch. Replace `HasError` with a non-throwing `TryGetResult(response, operation, out result)` — this removes every `!` and every blanket `catch (Exception)` in the class.

---

### I-2 · `Kbot.Common.Models.Order` is simultaneously the domain model and the EF entity

[KrakenDbContext.cs:12](src/Kbot.MailService/Database/KrakenDbContext.cs#L12) maps `Kbot.Common.Models.Order` directly, and the migration snapshot references `"Kbot.Common.Models.Order"` by name. So the *shared API library* owns MailService's database schema, while having no EF dependency of its own and no visibility of the migrations.

Consequences:
- Renaming or retyping a property in `Kbot.Common` silently desynchronises `KrakenDbContextModelSnapshot.cs` from the live database, with **no compile-time signal**.
- `OrderStatus` / `BuyOrSell` / `OrderType` are persisted as `int`. **Inserting a member into any of those enums reinterprets every persisted row.** `OrderStatus` in particular is ordered `Pending, Open, Closed, Canceled, Expired` — adding a status anywhere but the end silently rewrites history.
- Money typed as `double` in Common becomes `double precision` in Postgres (H-2).

**Action:** Give `Kbot.MailService` its own persistence entity plus a mapper, or at minimum add `.HasConversion<string>()` for the three enums and pin the shape with an EF model-snapshot test.

---

### I-3 · Configured pair aliases vs. Kraken's canonical names — the same bug in three places

Kraken accepts an *altname* (`XBTCHF`) but echoes its *canonical* name in responses. Three places assume they are identical:

1. [KrakenClient.cs:54](src/Kbot.Common/Api/KrakenClient.cs#L54) — `response.Result![pair]` → `KeyNotFoundException` → swallowed → `0.0` → **C-2**.
2. [MailSenderService.cs:167](src/Kbot.MailService/Utility/MailSenderService.cs#L167) — the daily query filters `o.Pair == mailOptions.Value.CryptoPair` with exact string equality, but `Order.Pair` holds whatever Kraken returned in `descr.pair`. If they differ, the DB fills correctly while **every daily mail says "the dca bot didn't bought any crypto in the last 24 hours"** and points the user at the issue tracker. The monthly report has no pair filter and is unaffected — making the inconsistency baffling to diagnose.
3. [CsvService.cs:17-19](src/Kbot.MailService/Utility/CsvService.cs#L17-L19) — `order.Pair[..^3]` / `[^3..]` assumes a 3-character quote asset. Verified: `XBTCHF`→`XBT`/`CHF` ✅, `XXBTZUSD`→`XXBTZ`/`USD` ❌, `ETHUSDT`→`ETHU`/`SDT` ❌.

**Action:** Resolve the canonical name once at startup via Kraken's `AssetPairs` endpoint (which also yields `pair_decimals`, `lot_decimals`, and `ordermin` — fixing the hardcoded rounding in M-3 and letting you validate `MinOrderVolume`). Cache it and use it for both the ticker lookup and the report filter. As an immediate mitigation, when the filtered 24 h query returns zero rows but the unfiltered one does not, log the pairs actually present.

---

### I-4 · The same Kraken API key is used by two processes with a millisecond nonce

Both `docker.dca.secrets-template.json` and `docker.mail.secrets-template.json` define a `Secrets` section with `ApiKey`/`ApiSecret`, and nothing suggests they should differ. [KrakenApi.cs:66](src/Kbot.Common/Api/KrakenApi.cs#L66) uses `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` as the nonce, with no monotonicity enforcement.

Kraken requires a **strictly increasing nonce per API key**. Three collision paths:
1. Two private calls in the same millisecond → `EAPI:Invalid nonce`.
2. **Two processes sharing one key** → interleaved requests arrive out of nonce order.
3. NTP stepping the clock backwards → the key is locked out until wall-clock catches up.

The failure is invisible: the error lands in the `error` array, `HasError` logs it as "Could not query the balance", and the caller gets a sentinel (I-1).

**Action:** Use a process-wide monotonic counter at microsecond resolution seeded from the clock. **Document that each service needs its own Kraken API key**, and that the mail service's key should be restricted to *Query Closed Orders & Trades* with **no trade permission** — least privilege, and it also removes collision path 2.

---

### I-5 · Duplicated and inconsistent configuration across the two services

The same values are configured twice under different names, with no cross-check:

| Concept | DcaService | MailService |
|---|---|---|
| Trading pair | `OrderOptions__CryptoPair="XBTCHF"` | `MailOptions__CryptoPair="XBTCHF"` |
| Fiat currency | `CultureOptions__Fiat="CHF"` | `MailOptions__Fiat="CHF"` |
| Day-of-month | `BalanceOptions__DefaultTopupDayOfMonth` — validated **1–31** ❌ | `MailOptions__DayOfMonth` — validated **1–28** ✅ |

Change one and the reports silently disagree with the trades. The day-of-month divergence is the live bug behind **C-4**: the two validators encode different beliefs about the same concept, and the more permissive one crashes.

Also: `CultureOptions` lives in `Kbot.Common` and `HolidayService` (also Common) depends on it — but only `Kbot.DcaService` registers either. Wiring `HolidayService` into MailService would fail at resolve time.

**Action:** Promote `CryptoPair` and `Fiat` into a single shared `TradingOptions` in `Kbot.Common`, bound by both services from one config section. Align the day-of-month validators on 1–28 (or implement clamping in both).

---

### I-6 · Shared `state` volume, divergent state handling

[example-compose.yaml](docker/example-compose.yaml) mounts the **same named volume** `state:/app/state` into both containers. The filenames happen not to collide (`state/state.json` vs `state/history.json`), so this works — but the two state handlers were written independently and have diverged in robustness (H-6), and neither creates the directory. `.gitignore` covers `*state.json` but **not** `history.json`.

**Action:** Either give each service its own volume, or — better — unify both on one `JsonStateStore<T>` in `Kbot.Common` with an injectable path, atomic writes, and consistent corrupt-file recovery.

---

## 7. Medium and Low Findings

Grouped by theme. Full per-module detail is available in the module reviews; these are the items worth tracking.

### Correctness

| ID | Finding | Location |
|---|---|---|
| M-1 | `GetLastAveragePrice` divides by zero → daily mail prints **"Price Increase: NaN%"** whenever the day-2 window is empty (first two days, after downtime, whenever funds ran out) | [MailSenderService.cs:185](src/Kbot.MailService/Utility/MailSenderService.cs#L185) |
| M-2 | Balance is read as **total**, not available — fiat held against an unfilled limit order is counted as spendable, inflating the interval and causing rejected orders | [DcaWorker.cs:63-64](src/Kbot.DcaService/DcaWorker.cs#L63-L64) |
| M-3 | Limit price hard-rounded to **1 decimal** regardless of pair. `Math.Round(0.08123, 1) == 0.1` — a DOGE order at $0.08123 becomes $0.10, a **23 % overpay**. Uses banker's rounding. Contradicts the README's "multiple cryptocurrencies" claim | [OrderRequest.cs:26](src/Kbot.Common/Dtos/OrderRequest.cs#L26), [DcaWorker.cs:67](src/Kbot.DcaService/DcaWorker.cs#L67) |
| M-4 | `TimeUntilNextTopUp` is **persisted derived state**, refreshed only after a successful order — never at startup. On a fresh state file it is `TimeSpan.Zero` → interval 0 → immediate unscheduled buy | [DcaWorker.cs:38-43](src/Kbot.DcaService/DcaWorker.cs#L38-L43) |
| M-5 | No floor on the *investment* interval (only on the poll delay), so a residual balance near the top-up date can be dumped in a 10-second burst — the opposite of DCA | [DcaWorker.cs:87-109](src/Kbot.DcaService/DcaWorker.cs#L87-L109) |
| M-6 | Crash window between `SendOrder` and `State.Save()`; `cl_ord_id` is minute-granular and therefore not a reliable idempotency key | [DcaWorker.cs:100-119](src/Kbot.DcaService/DcaWorker.cs#L100-L119) |
| M-7 | Market orders store `Price = 0` (Kraken's `descr.price` is the *requested* price; the realised average lives in the unmapped top-level `price`). The mail's per-order column reads `0.00` while its summary average is correct — the mail visibly contradicts itself | [ClosedOrderInfo.cs:74-75](src/Kbot.Common/Dtos/ClosedOrderInfo.cs#L74-L75) |
| M-8 | Per-day grouping key converts an `Unspecified` `DateTime` to `DateTimeOffset`, picking up the **host's** UTC offset and writing a bogus offset into the CSV date column | [OrderService.cs:77-79](src/Kbot.MailService/Utility/OrderService.cs#L77-L79) |
| M-9 | `OrderParser` silently coerces any non-`"buy"` to `Sell` and any non-`"limit"` to `Market` — `stop-loss`, `take-profit`, `settle-position` all become `Market` in the DB and skew reports. Unknown status throws a bare `ArgumentOutOfRangeException()` with no message or value | [OrderParser.cs:22-27](src/Kbot.Common/Conversion/OrderParser.cs#L22-L27) |
| M-10 | `PostPrivateAsync` **mutates the caller's dictionary** (`body.Add("nonce", …)`). Works only because every caller passes a fresh one — a trap for any retry wrapper | [KrakenApi.cs:67](src/Kbot.Common/Api/KrakenApi.cs#L67) |
| M-11 | `EnsureSuccessStatusCode()` runs *before* the body is read, discarding Kraken's `{"error":[…]}` payload. 429 handling exists only on the private path; no `Retry-After`, no retry, no `HttpClient.Timeout` override | [KrakenApi.cs:40,80-85](src/Kbot.Common/Api/KrakenApi.cs#L80-L85) |
| M-12 | `OrderByDescending(o => o.CloseTimeStamp)` — PostgreSQL defaults to **NULLS FIRST**, so one NULL row collapses the watermark to `MinValue` and re-fetches the entire history every day | [OrderService.cs:18](src/Kbot.MailService/Utility/OrderService.cs#L18) |
| M-13 | Whole `Orders` table materialised and change-tracked on every fetch (`ToList()`, no `AsNoTracking()`), then an O(N·M) in-memory scan. No `DistinctBy` guard against cross-page duplicates → `InvalidOperationException` on a tracked-key clash | [OrderService.cs:29-37](src/Kbot.MailService/Utility/OrderService.cs#L29-L37) |
| M-14 | Quoted values in `stack.env` (`CultureOptions__CultureString="de-CH"`) — Compose strips quotes, but `docker run --env-file` and systemd `EnvironmentFile=` do not. `new CultureInfo("\"de-CH\"")` throws `CultureNotFoundException` | [stack.env:1,5,10-12](docker/stack.env#L1) |
| M-15 | `CultureString` validated only for emptiness. `new CultureInfo("bogus")` does **not** throw on ICU — it yields a synthetic culture; the failure surfaces later inside the swallowed holiday fetch | [CultureOptions.cs:12,21-24](src/Kbot.Common/Options/CultureOptions.cs#L12) |

### Security & operations

| ID | Finding | Location |
|---|---|---|
| M-16 | `Secrets` is a `record` — the synthesized `ToString()` prints the API secret in clear text (`Secrets { ApiKey = …, ApiSecret = U0VDUkVU }`). The codebase already logs whole records, and logs are retained 31 days on a shared volume. Override `PrintMembers` on `Secrets` and `MailSecrets` | [Secrets.cs:5-9](src/Kbot.Common/Options/Secrets.cs#L5-L9) |
| M-17 | `MigrationService` swallows total migration failure after 5 attempts and lets the host start against an unusable DB; errors go to `Console.WriteLine`, bypassing Serilog. `mail-service` has no `depends_on`/healthcheck for `kraken-database`, and 25 s is easily exceeded on a Pi cold start | [MigrationService.cs:9-25](src/Kbot.MailService/Database/MigrationService.cs#L9-L25) |
| M-18 | Third-party composite actions pinned to mutable `@main` while holding `contents: write`, `packages: write`, `id-token: write` and receiving `GITHUB_TOKEN`. Pin to SHAs | [docker-dca.yml:26,49,58](.github/workflows/docker-dca.yml#L26) |
| M-19 | `pull_request` runs the publish job with full write permissions for same-repo branches; fork PRs always fail at the push step, giving outside contributors confusing red CI. (Correctly uses `pull_request`, not `pull_request_target`) | [docker-dca.yml:12-18,36-56](.github/workflows/docker-dca.yml#L12-L18) |
| M-20 | No `concurrency:` groups — a commit touching `Kbot.Common` starts both workflows simultaneously, each computing a version and pushing a git tag | both workflows |
| M-21 | Floating base-image tags (`runtime:10.0`), no `HEALTHCHECK`, no `TZ`. Without a healthcheck, a worker hung in `Task.Delay` or blocked on SMTP looks perfectly healthy and stops trading indefinitely | both Dockerfiles |
| M-22 | Gmail hardwired (`new SmtpClient("smtp.gmail.com", 587)`); `MailSecretsValidator` rejects any password that is not exactly 16 chars, so no other provider can be used. No `Timeout` (default 100 s blocks the single loop). `System.Net.Mail.SmtpClient` is not recommended for new development — migrate to MailKit | [MailSenderService.cs:141-145](src/Kbot.MailService/Utility/MailSenderService.cs#L141-L145) |
| M-23 | `HourOfDay` is UTC-only — a Swiss user setting `6` gets mail at 07:00 CET / 08:00 CEST, shifting on the DST boundary. The README does not say UTC | [DailyReporter.cs:22-30](src/Kbot.MailService/DailyReporter.cs#L22-L30) |
| M-24 | No `CancellationToken` anywhere in `KrakenApi`/`KrakenClient`, and MailService threads it only as far as `Task.Delay`. On shutdown, an in-flight pagination chain or a 100 s SMTP hang outlives the host's 30 s timeout and is SIGKILLed — potentially mid-`SaveChangesAsync` | module-wide |
| M-25 | No test project covers `KrakenApi.CreateSignature`, `DcaStateHandler`, the watermark logic, or any DTO parser — all pure functions and ideal unit-test targets. Tests run `Parallelize(Workers = 0)` against a shared Kraken key, Gmail account, `state.json`, and one process-wide InMemory DB named `"KrakenDb"` | `test/**` |

### Documentation & hygiene

| ID | Finding |
|---|---|
| L-1 | **README says SQLite (`Kraken.db`); the code is PostgreSQL only.** No SQLite package exists anywhere. A user provisioning storage per the README never creates the Postgres service, and the mail service starts but silently records nothing |
| L-2 | **README calls the license "custom"; `LICENSE` is verbatim AGPL-3.0** (661 lines, matching commit `6ac4228`). AGPL's network-copyleft obligation is materially different from "custom" — this misleads contributors and downstream users |
| L-3 | **Three disagreeing versions**: `VERSION` = `1.2.9`, newest CHANGELOG entry = `v1.1.0`, `example-compose.yaml` pins `:2.0.1`. The CHANGELOG has no entry in ~20 months despite the .NET 10 migration, the restructure, and the **breaking SQLite → PostgreSQL switch** — a silent data-loss path for anyone upgrading. Workflows compute per-service versions independently, so one root `VERSION` cannot represent both |
| L-4 | No `CONTRIBUTING.md`, `SECURITY.md`, issue/PR templates, or `CLAUDE.md`. For a project handling exchange API keys, no private disclosure channel means full disclosure by default. Nothing warns a new contributor that `dotnet test` trades real money |
| L-5 | `.gitignore` is a **Python** template (~190 irrelevant lines) with .NET rules appended. The security-relevant patterns are correct and no secret has ever been committed (verified over full history). Gaps: `.vscode/` untracked-but-unignored, `history.json` not covered |
| L-6 | Dead code: `Balance`/`BalanceUnparsed` entirely unreachable; `ApiUtility.ToQueryString(Dictionary<string,object>)` unused; `PrivateMethod.OpenOrders`, `CancelOrderResponse.Pending` unreferenced; `var s = secrets.Value.ApiKey;` assigned and never used (a CS0219 the build does not surface); duplicate log line in `GetClosedOrders`. Add `<TreatWarningsAsErrors>` |
| L-7 | `AddUserSecrets<Secrets>()` targets `Kbot.Common`, which has **no `UserSecretsId`** — a silent no-op duplicated across 5 call sites. The trailing `.Build()` on the `ConfigurationManager` chain discards its result. `services.AddOptions()` is redundant |
| L-8 | Serilog file sink specifies **both** `outputTemplate` and `formatter` (different overloads — `outputTemplate` is ignored), plus `archiveEvery`/`archivePath` which are not parameters of `Serilog.Sinks.File` 7.0.0 at all and are silently dropped. `archivePath: /logs/archive` is outside the mounted volume anyway. No `Log.CloseAndFlush()` on shutdown |
| L-9 | Literal `";` rendered into every monthly report body — a leftover from a pre-raw-string version | [HtmlService.cs:119](src/Kbot.MailService/Utility/HtmlService.cs#L119) |
| L-10 | `MailMessage`, the CSV `MemoryStream` and its `StreamWriter` are never disposed. `Encoding.UTF8` emits a BOM, which some CSV readers surface as a stray `ï»¿` in the first header cell |
| L-11 | Naming: `InklusiveFeeMultiplier` (German/English hybrid), `currentInvertval` (typo, ×2), `vor` as a validation-list name in all four validators, `costForVolume` (means "cost of one min-size order"), `AggregatedOrder.OrderType` actually holds Buy/Sell not Limit/Market, `MailGenereateTest.cs` filename typo, `ApiTestPublic` class whose tests are mostly *private* endpoints, `cl_ord_id` snake_case in a public signature, `CountyCode` vs derived `CountryCode` |
| L-12 | `OrderService.GetReportPerDayCalc` uses `Aggregate<IGrouping<…>, List<…>>([], (l, g) => { l.Add(…); return l; })` — a `Select().ToList()` written the hard way, with a discarded `OrderBy` and a lambda parameter shadowing its outer scope. Rewrite as a flat `GroupBy` on a composite key |
| L-13 | `state.json` rewritten on **every** loop iteration — ~8 600 SD-card writes/day on the documented Raspberry Pi deployment for a file that changes a few hundred times a month. `DcaState` is a record; guard with `if (State != _lastSaved)` |
| L-14 | `PublicHoliday.cs` has a file-wide `#nullable disable`, hiding that `Counties` genuinely *is* nullable upstream. Several DTOs use `= null!;` on non-`required` members, converting a missing JSON field into a downstream NRE instead of a clear `JsonException` |
| L-15 | Kraken endpoint paths derive from C# enum member names (`$"/0/private/{method}"`) with no test or attribute pinning them — a routine rename silently produces a 404 |
| L-16 | Malformed placeholder `"<kraken api public key"` (missing `>`) in two mail secret templates; inconsistent `secrets.template.json` vs `secrets-template.json` naming across five files |

---
## 8. Module Summaries

### Kbot.Common — *structurally clean, functionally unsafe*

Three concerns in one assembly: a thin HTTP/signing layer (`KrakenApi`), a semantic wrapper (`KrakenClient`), and "Unparsed → Parsed" DTO pairs converting Kraken's string-encoded numerics. It also hosts two unrelated pieces — `HolidayService` (an `IHostedService` caching `date.nager.at`) and the shared option records.

The surface style is genuinely good: file-scoped namespaces throughout, records, primary constructors, `required` members, full `.editorconfig` compliance. Below it, the module has no interfaces at all (`sealed` classes, `internal` methods, no `InternalsVisibleTo`), so there is no seam to substitute a fake — which is precisely why the only tests that exist trade real money, and why H-1 and I-1 went unnoticed.

`HolidayService` does not belong here: it is three responsibilities in one class (HTTP client, mutable cache with an eviction policy, hosted service), it is used **only** by `Kbot.DcaService`, and it drags a full `Microsoft.Extensions.Hosting` dependency into a library that `Kbot.MailService` also consumes. Move it into `Kbot.DcaService` and split the three concerns.

### Kbot.DcaService — *sound design, dangerous robustness gaps*

The scheduling core is the best code in the repository. `ComputeNextInvestmentInterval` is self-correcting by construction, and options validation with `ValidateOnStart()` puts it ahead of most hobby bots.

**Implemented algorithm** (with the shipped `stack.env`, BTC at 90 000 CHF):
1. `balanceFiat = balance["CHF"] − ReserveFiat`
2. `askPrice = round(price × 1.00001, 1)` → `90000.9`
3. `costForVolume = ceil(askPrice × 0.00005 × 1.004 × 100) / 100` → **4.52 CHF**
4. If `balanceFiat < costForVolume` → wait `MaxWaitTime`
5. `interval = TimeUntilNextTopUp / (balanceFiat / costForVolume)` — 1 000 CHF, 30 d → `n = 221.2`, interval **3 h 15 m**
6. If too early → **wait half the remaining time and re-poll** (not "wait the interval")
7. Otherwise send a limit buy at `askPrice` with `cl_ord_id = "dca-l" + yyMMddHHmm`
8. On success only: update `LastInvestmentTime`, recompute the top-up span

**Divergences from the README:** step 6 halves repeatedly (3h15 → 1h37 → 48m → … → 10 s), costing ~9–10 balance+ticker poll pairs per order rather than one wait. The README's step 3 ("checks how long until the next top up") does not happen per cycle — `TimeUntilNextTopUp` is a persisted snapshot refreshed only after a *successful* order (M-4). The top-up shift is computed against 00:00 **UTC**, not the configured `CultureOptions` timezone.

`DcaWorker` also violates SRP badly — loop control, static state I/O, options unwrapping (7 accessor properties), price/fee arithmetic, the buy/wait decision, order-id generation, request construction, and delay clamping in one class, all against concrete collaborators. The `public string? FixOrderId { get; set; }` is production code existing solely for the live-trading test.

### Kbot.MailService — *good persistence design, inverted failure handling*

One thing is done notably right: persistence goes through `IDbContextFactory<KrakenDbContext>` with a short-lived context per operation, so the classic captive-scoped-`DbContext`-in-a-singleton bug is **absent**.

Beyond that, startup failure handling is inverted: migrations swallow every error and let the app run against a broken database (M-17), while the first two `await`s in `ExecuteAsync` are unguarded and take down the whole host (C-5). The monthly watermark advances regardless of whether data was fetched (H-5), and the daily window is wall-clock rather than watermark-based, so a missed run silently drops a day.

One incidental positive worth recording: the *daily* Kraken fetch boundary is safe. Kraken's `start` is second-granular and effectively inclusive, and the truncating `(long)info.CloseTimeStamp` cast means the boundary order is re-returned and de-duplicated — no rows lost or duplicated there.

### Tests, CI and Docker — *close to zero safety net*

Not a test suite in the usual sense: a set of live-integration smoke scripts pointed at production Kraken and a real Gmail account. Only `TimeComputeTest` and `HolidayServiceTest` contain meaningful assertions, and both are calendar- and network-dependent. The three mail tests have **zero assertions**. `HolidayServiceTest` degrades silently offline — `Is28DecHoliday` **passes for the wrong reason** because an empty cache returns `false`.

Docker gets the fundamentals right — `--platform=$BUILDPLATFORM` correctly cross-compiles rather than emulating, and the final stage runs as `USER app` with `state/` and `logs/` chowned — but loses it on the inert `.dockerignore` (H-4), floating tags, and no healthcheck.

---

## 9. Refactoring Roadmap

Sequenced so each phase is independently shippable.

> **Executable version:** the phases below are broken into 37 branch-sized plans — one per
> finding, or one per tightly-coupled group — with dependencies, merge order and parallel waves
> in [docs/ROADMAP.md](docs/ROADMAP.md) and the specs in [docs/plans/](docs/plans/).

**Phase 1 — Stop the bleeding** *(≈1 day)*
Gate the live tests (C-1). Guard both sentinel call sites (C-2, C-3). Clamp the top-up day and reorder the state save (C-4). Wrap both loops in try/catch with backoff (C-5). Add `ci.yml` running build + test (H-3).

**Phase 2 — Numeric and contract correctness** *(≈2–3 days)*
Introduce `KrakenResult<T>` and delete the sentinel protocol (I-1). Pin `InvariantCulture` everywhere (H-1). Move money to `decimal` with an EF migration (H-2). Split the shared `Order` into a domain model and a persistence entity, with `HasConversion<string>()` for the enums (I-2). Resolve pair names from Kraken's `AssetPairs` once at startup (I-3, M-3).

**Phase 3 — Make it testable** *(≈2 days)*
Extract `IKrakenClient`, `IHolidayService`, `IDcaStateStore`, `IMailTransport`, `INextRunCalculator`. Inject `TimeProvider` in place of the eight direct `DateTime.UtcNow` calls. Extract the DCA policy into a pure `DcaPlanner`:
```csharp
public sealed record CycleInput(decimal FiatBalance, decimal AskPrice, DcaState State, DateTimeOffset Now);
public abstract record CycleDecision
{
    public sealed record Buy(decimal Price, decimal Volume) : CycleDecision;
    public sealed record Wait(TimeSpan For, string Reason)  : CycleDecision;
}
```
`DcaWorker` then becomes: fetch → `Plan` → execute → persist → delay, and every finding about interval maths, clamping, and month boundaries becomes a trivial unit test.

**Phase 4 — Operational hardening** *(≈2 days)*
`AddHttpClient` + resilience handler (H-8, M-11). Thread `CancellationToken` end to end (M-24). Unify state persistence with atomic writes (H-6, I-6). Fix the holiday cache (H-7). Healthchecks, digest-pinned images, remove the published Postgres port and default password (H-11, M-21).

**Phase 5 — Docs and hygiene** *(≈half a day)*
README (PostgreSQL, AGPL-3.0, UTC semantics), reconcile versioning, add `CONTRIBUTING.md` + `SECURITY.md`, regenerate `.gitignore`, `<TreatWarningsAsErrors>`, delete the dead code in L-6/L-7.

---

## 10. Future Feature Suggestions

**Moved out of this review** — see [FUTURE_FEATURES.md](FUTURE_FEATURES.md).

Three proposals were developed there (F-1 self-monitoring with a health endpoint, metrics and a
dead-man's switch · F-2 multi-pair weighted DCA portfolio · F-3 pluggable DCA strategies), together
with their prerequisites and two runners-up. They are feature work, not defects, and are deliberately
kept out of the remediation scheduling in [docs/ROADMAP.md](docs/ROADMAP.md).

---

## Appendix · Coverage Gaps

| Critical behaviour | Covered? | Gap |
|---|---|---|
| DCA interval maths | Partial | One happy path. No div-by-zero, no negative balance, no `TimeSpan.MaxValue` path |
| Next-top-up date | Partial / **broken** | Weekday/weekend/holiday covered; `_Yesterday` fails today (H-12); December rollover untested |
| State persistence | **None** | The corrupt-file path added in `9940b7a` is untested; the test deletes the wrong file (`state.json` vs `state/state.json`) |
| HMAC-SHA512 signing | **None** | The one place where a silent error breaks every authenticated call. Trivially testable against Kraken's published vector |
| Nonce monotonicity | **None** | Millisecond resolution + `Parallelize(Workers = 0)` is a live collision risk |
| Watermark / incremental fetch | **None** | Never asserted; `MailGenereateTest` calls it and checks nothing |
| Pagination (`fetchAll`) | **None** | The recursive `ofs += 50` loop and the truncation bug (H-9) are untested |
| DTO parsers | **None** | `TickerInfoUnparsed.Parse`, `ClosedOrderUnparsed.ToModel`, `OrderParser.ToOrder` — pure functions, ideal targets |
| Report aggregation | **None** | Exercised, never asserted |
| HTML / CSV generation | **None** | Including the `NaN` path (M-1) and the culture bug (H-1) |
| Options validation | **None** | All five validators — which is how H-10 survived |
| `DcaWorker` decision logic | **None (unsafe)** | Runs the real worker against the live exchange and asserts only that a cancel succeeded |
| DB migration | **None** | Throws on the InMemory provider in every test, swallowed by the retry loop (25 s of the suite's runtime) |
| Daily/monthly scheduling | **None** | `HourOfDay` UTC-vs-local semantics unverified |
