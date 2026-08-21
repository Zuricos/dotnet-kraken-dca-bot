# P3-01 · Extract seams and inject `TimeProvider`

|  |  |
|---|---|
| **Findings** | Review §9 Phase 3; enables M-25 and the whole Appendix coverage table |
| **Phase** | 3 — Make it testable |
| **Branch** | `refactor/p3-testability-seams` |
| **Effort** | M (~1–1.5 days) |
| **Depends on** | **P2-01** and **P2-03** merged (interfaces should be extracted over the *final* signatures, not the sentinel ones) |
| **Blocks** | **P3-02**, **P3-03**, and simplifies **P4-01**…**P4-04** |
| **Conflict surface** | Wide by design: both `ServiceCollectionExtension.cs`, `KrakenClient`, `HolidayService`, `DcaStateHandler`, `MailSenderService`. **Merge this when the Phase-4 branches are not mid-flight**, or land it first and rebase them. |

## Problem

`Kbot.Common` has no interfaces at all — `sealed` classes, `internal` methods, no `InternalsVisibleTo`
— so there is no seam to substitute a fake. That is precisely why the only tests that exist trade real
money, and why H-1 and I-1 went unnoticed. There are eight direct `DateTime.UtcNow` calls in the
scheduling paths, so nothing time-dependent can be tested deterministically.

`HolidayService` also does not belong in `Kbot.Common`: it is three responsibilities in one class
(HTTP client, mutable cache with an eviction policy, hosted service), it is used **only** by
`Kbot.DcaService`, and it drags a full `Microsoft.Extensions.Hosting` dependency into a library that
`Kbot.MailService` also consumes.

## Scope

### In scope
1. Extract interfaces, keeping the concrete classes as the only implementations:
   - `IKrakenClient` (`Kbot.Common.Api`) — the five public methods, post-**P2-01** signatures
   - `IHolidayService` — `bool IsHoliday(DateTime date)` (may already exist from **P1-10**; keep one)
   - `IDcaStateStore` — `DcaState Load()` / `void Save(DcaState)`, replacing the `static`
     `DcaStateHandler` (this is what lets **P4-01** swap in `JsonStateStore<T>` without touching the
     worker)
   - `IMailTransport` — send(subject, htmlBody, attachments), so mail tests stop sending mail
   - `IOrderRepository` (or keep `OrderService` concrete but behind an interface) for the report paths
   - `INextRunCalculator` for the daily/monthly scheduling decision
2. Register `TimeProvider.System` in both services and replace **every** `DateTime.UtcNow` /
   `DateTimeOffset.UtcNow` in `DcaWorker`, `TimeComputeService`, `DailyReporter`, `MonthlyReporter`,
   `MailSenderService` and `OrderService` with `timeProvider.GetUtcNow()`. Sweep:
   ```bash
   git grep -nE 'DateTime(Offset)?\.UtcNow' src/
   ```
   Nothing in `src/` should match afterwards except the `TimeProvider` registration itself.
3. Move `HolidayService` from `Kbot.Common/Helpers/` into `Kbot.DcaService`, and split it into three
   pieces: a typed HTTP client (`HolidayApiClient`), a cache (`HolidayCache`), and the `IHostedService`
   that warms/refreshes it. Do **not** fix its bugs here — that is **P4-02** — but the split makes
   that fix small.
4. Drop `Microsoft.Extensions.Hosting` from `Kbot.Common.csproj` if nothing else needs it.
5. Fix the lifetime lie while you are in the registration code **only if it is trivial** — otherwise
   leave it: the real fix is **P4-03**. Note in the PR which registrations are still wrong.
6. Do not change behaviour anywhere. This branch should be provably behaviour-neutral: the existing
   tests must pass untouched (except for constructor/DI updates).

### Out of scope
- The pure `DcaPlanner` extraction → **P3-02**.
- New tests beyond what is needed to keep the suite compiling → **P3-03**.
- Holiday cache bug fixes → **P4-02**. `HttpClient` lifetimes → **P4-03**. State store →
  **P4-01**.

## Acceptance criteria

- `git grep -nE 'DateTime(Offset)?\.UtcNow' src/` → only the `TimeProvider` registration.
- Every collaborator of `DcaWorker`, `DailyReporter` and `MonthlyReporter` is an interface or
  `TimeProvider`.
- `Kbot.Common` no longer references `Microsoft.Extensions.Hosting`.
- The existing test suite passes with no behavioural assertions changed.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
git grep -nE 'DateTime(Offset)?\.UtcNow' src/
dotnet csharpier check .
```
