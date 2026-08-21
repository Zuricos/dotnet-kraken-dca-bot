# P2-01 · Replace the sentinel protocol with `KrakenResult<T>`

|  |  |
|---|---|
| **Findings** | I-1 |
| **Phase** | 2 — Contract & numeric correctness |
| **Branch** | `refactor/p2-i1-kraken-result-protocol` |
| **Effort** | M (~1 day) |
| **Depends on** | **P1-02** and **P1-04** merged (this replaces P1-02's tactical guards with a typed contract and relies on the loop being fault-tolerant) |
| **Blocks** | **P2-05**, **P2-06**, **P3-01** — and it is the single highest-value change in the review |
| **Conflict surface** | `src/Kbot.Common/Api/KrakenClient.cs` (also P2-02, P2-03, P2-06), `src/Kbot.DcaService/DcaWorker.cs`, `src/Kbot.MailService/Utility/OrderService.cs` |

## Problem

`KrakenClient` uses **four different in-band failure conventions**, none distinguishable from a
legitimate result:

| Method | Returns on failure | Indistinguishable from |
|---|---|---|
| `CheckBalance()` | `[]` | an account with no assets |
| `GetCurrentCryptoPrice()` | `0.0` | *(nothing — 0 is never a valid price)* |
| `SendOrder()` | `false` | a rejected order **and** a successfully-placed-but-unparseable one |
| `GetClosedOrders()` | `null` | mapped to "0 new orders" by the caller |

C-2, C-3, H-5 and the duplicate-buy exposure are all downstream of this one decision. The `SendOrder`
case is the most insidious: `false` is also returned when the response failed to parse *after*
successful submission, so `false` does **not** guarantee that no order exists.

Compounding it, `HasError`
([KrakenClient.cs:194-215](../../src/Kbot.Common/Api/KrakenClient.cs#L194-L215)) is a predicate that
*throws* — `ArgumentNullException` for a null response but `return true` for an API error, two
channels for one condition — and logs `"Could not query the balance"` for **every** endpoint, so a
failed closed-orders sync is reported as a balance failure.

## Scope

### In scope
1. Add the result type in `Kbot.Common`:
   ```csharp
   public readonly record struct KrakenResult<T>(T? Value, IReadOnlyList<string> Errors)
   {
     public bool IsSuccess => Errors.Count == 0 && Value is not null;
     public static KrakenResult<T> Ok(T value) => new(value, []);
     public static KrakenResult<T> Fail(params string[] errors) => new(default, errors);
   }
   ```
   Add a `TransportFailure` / `ApiError` / `ParseFailure` discriminator (enum or separate factory
   methods) — callers need to distinguish *retryable* from *terminal*, and P4-03's resilience handler
   will use it.
2. Change every public `KrakenClient` method to return `KrakenResult<T>`:
   `CheckBalance` → `KrakenResult<IReadOnlyDictionary<string, double>>`,
   `GetCurrentCryptoPrice` → `KrakenResult<double>` (`decimal` after **P2-03**),
   `SendOrder` → `KrakenResult<OrderPlacement>` carrying the transaction ids,
   `CancelOrder` → `KrakenResult<int>`,
   `GetClosedOrders` → `KrakenResult<ClosedOrders>`.
3. **`SendOrder` must distinguish submitted-but-unparseable from rejected.** If the HTTP call
   succeeded and Kraken's `error` array is empty but the body will not parse, return a distinct
   `Indeterminate` outcome — the caller must treat it as *possibly placed* (log loudly, persist
   state, do **not** retry immediately).
4. Replace `HasError` with a non-throwing
   `private bool TryGetResult<T>(ApiResponse<T>? response, string operation, out T result, out IReadOnlyList<string> errors)`.
   The `operation` argument fixes the wrong log message. This removes every `!` and every blanket
   `catch (Exception)` in the class — do remove them, do not leave both.
5. Update both consumers:
   - `DcaWorker.InvestmentCycle` — replace P1-02's guards with `if (!result.IsSuccess)` branches,
     preserving the `MaxWaitTime` early-return behaviour and the log messages.
   - `OrderService.FetchClosedOrdersAndSave` — a failed fetch must **not** be reported as "0 new
     orders"; propagate it so **P2-07** can refuse to advance the watermark. Return a result type or
     throw — pick one and state which in the PR.
   - `MailSenderService` — audit every `krakenClient` call site.
6. Keep `KrakenApi` internal-facing signatures as they are (they already return nullable
   `ApiResponse<T>?`); the lifetime/resilience work is **P4-03**.
7. Unit tests: a stubbed `HttpMessageHandler` covering, per method, (a) HTTP failure, (b) Kraken
   `error` array populated, (c) unparseable body, (d) success. That is the matrix that never existed.

### Out of scope
- `InvariantCulture` on the parses → **P2-02** (merge that first if you want to avoid touching the
  same lines twice; both orders work, coordinate in the PR).
- `double` → `decimal` → **P2-03**.
- Pagination integrity → **P2-06** (it *depends* on this plan's failure propagation).
- Retry/backoff/`HttpClient` lifetime → **P4-03**. Cancellation tokens → **P4-04**.

## Acceptance criteria

- No `KrakenClient` method returns a sentinel; every failure is representable and every caller
  handles the failure branch (the compiler enforces it for the nullable `Value`).
- `HasError` is gone.
- `grep -c 'catch (Exception' src/Kbot.Common/Api/KrakenClient.cs` → 0.
- The four-case test matrix passes for all five methods.
- Behaviour parity: with a working API, the bot places the same order it did before.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
git grep -n 'HasError' src/ || echo "sentinel protocol removed"
dotnet csharpier check .
```
