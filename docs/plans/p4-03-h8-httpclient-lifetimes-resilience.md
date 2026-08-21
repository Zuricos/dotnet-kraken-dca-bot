# P4-03 · `HttpClient` lifetimes and a resilience handler

|  |  |
|---|---|
| **Findings** | H-8, M-11 |
| **Phase** | 4 — Operational hardening |
| **Branch** | `refactor/p4-h8-httpclient-lifetimes-resilience` |
| **Effort** | S–M (~5 h) |
| **Depends on** | Soft: **P3-01** (interfaces make the registration change smaller) |
| **Blocks** | **P4-04** (cancellation threading assumes the typed-client shape) |
| **Conflict surface** | `src/Kbot.Common/Api/KrakenApi.cs`, `src/Kbot.Common/Api/KrakenClient.cs`, both `ServiceCollectionExtension.cs`, `src/Kbot.Common/Helpers/HolidayService.cs` (also P4-02) |

## Problem

Three interlocking problems:

1. `KrakenApi` constructs its own `HttpClient` in a field initializer
   ([KrakenApi.cs:16-19](../../src/Kbot.Common/Api/KrakenApi.cs#L16-L19)) and is registered
   `AddTransient` ([Dca ServiceCollectionExtension.cs:19-20](../../src/Kbot.DcaService/Utility/ServiceCollectionExtension.cs#L19-L20)).
   Both services resolve it from the **root** provider, which tracks root-resolved transient
   `IDisposable`s until process shutdown — so every resolution permanently leaks an `HttpClient` +
   handler + connection pool. `HolidayService.FetchHolidaysFromRemote` has the same `new HttpClient()`
   per call.
2. `KrakenClient.Dispose()` disposes the injected `KrakenApi`
   ([KrakenClient.cs:217-220](../../src/Kbot.Common/Api/KrakenClient.cs#L217-L220)) — **a dependency it
   does not own**, which DI will dispose again. Harmless today, but it actively blocks the correct fix:
   the moment `KrakenApi` becomes a singleton, the first `KrakenClient` disposal kills the shared
   `HttpClient` and every later call throws `ObjectDisposedException` with no obvious culprit.
3. In MailService the transients are captured by a **singleton** `MailSenderService`
   ([Mail ServiceCollectionExtension.cs:26-31](../../src/Kbot.MailService/Utility/ServiceCollectionExtension.cs#L26-L31))
   — a textbook captive dependency. It works only by accident, because `OrderService` uses
   `IDbContextFactory` rather than an injected `DbContext`. The declared lifetimes are a lie, and the
   test project registers `MailSenderService` as *transient*, so tests exercise different lifetimes
   than production.

Related **M-11**: `EnsureSuccessStatusCode()` runs *before* the body is read
([KrakenApi.cs:40,85](../../src/Kbot.Common/Api/KrakenApi.cs#L40)), discarding Kraken's
`{"error":[…]}` payload. 429 handling exists only on the private path; there is no `Retry-After`
handling, no retry, and no `HttpClient.Timeout` override.

## Scope

### In scope
1. Typed client registration in both services:
   ```csharp
   services.AddHttpClient<KrakenApi>(c =>
   {
     c.BaseAddress = new Uri("https://api.kraken.com");
     c.Timeout = TimeSpan.FromSeconds(30);
   })
   .AddStandardResilienceHandler();     // Microsoft.Extensions.Http.Resilience
   ```
   Add the package to `Directory.Packages.props`. Configure the resilience handler deliberately:
   retry only **idempotent** operations. `AddOrder` must **not** be retried automatically — a retried
   order is a duplicate buy. Split the registration (a resilient client for reads, a non-retrying one
   for `AddOrder`) or disable retries for that path explicitly and document it in a comment.
2. Drop `IDisposable` from both `KrakenApi` and `KrakenClient`; delete `KrakenClient.Dispose()`.
   Register `KrakenClient` as a singleton (it is stateless).
3. Do the same for the holiday HTTP call: `AddHttpClient<HolidayApiClient>` instead of
   `new HttpClient()` per call.
4. Fix the MailService lifetimes so the declared graph is true: singleton services may only depend on
   singletons and factories. Make `MailSenderService`/`OrderService` singletons over
   `IDbContextFactory` (already correct) and the typed clients. Align the **test** registrations with
   production — a shared DI setup helper in the test project is the cleanest way.
5. **M-11**: read the body **before** checking the status code, so Kraken's error array survives an
   HTTP 4xx/5xx. Honour `Retry-After` on 429 (the resilience handler can do this — verify it is
   configured to). Remove the ad-hoc 429 special case once the handler covers it, or keep it and make
   it return a typed retryable failure (**P2-01**).
6. Verify no root-provider transient `IDisposable` remains:
   `git grep -n 'AddTransient' src/` and justify each remaining one in the PR.

### Out of scope
- `CancellationToken` propagation → **P4-04**.
- Nonce monotonicity → **P4-05**.
- Result protocol → **P2-01** (merge that first if possible; the failure classification here depends
  on it).

## Acceptance criteria

- Zero `new HttpClient()` in `src/`: `git grep -n 'new HttpClient' src/` → nothing.
- Neither `KrakenApi` nor `KrakenClient` implements `IDisposable`.
- Long-running smoke test (100 sequential cycles against a stub server) shows a constant number of
  sockets/handlers — no growth.
- An HTTP 500 with a Kraken error body surfaces the **Kraken** error message, not just the status code.
- `AddOrder` is never retried automatically — assert with a stub handler that returns 500 once and
  counts requests.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
git grep -n 'new HttpClient' src/ || echo "clean"
dotnet csharpier check .
```
