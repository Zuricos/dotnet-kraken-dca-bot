# P4-05 · Monotonic nonce and per-service API keys

|  |  |
|---|---|
| **Findings** | I-4 |
| **Phase** | 4 — Operational hardening |
| **Branch** | `fix/p4-i4-nonce-monotonicity` |
| **Effort** | S (~3 h) |
| **Depends on** | — (independent; touches one method) |
| **Blocks** | — |
| **Conflict surface** | `src/Kbot.Common/Api/KrakenApi.cs` (also P4-03, P4-04), `src/docker.*.secrets-template.json`, `README.md` (also P5-01) |

## Problem

Both `docker.dca.secrets-template.json` and `docker.mail.secrets-template.json` define a `Secrets`
section with `ApiKey`/`ApiSecret`, and nothing suggests they should differ.
[KrakenApi.cs:66](../../src/Kbot.Common/Api/KrakenApi.cs#L66) uses
`DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` as the nonce, with no monotonicity enforcement.

Kraken requires a **strictly increasing nonce per API key**. Three collision paths:

1. Two private calls in the same millisecond → `EAPI:Invalid nonce`.
2. **Two processes sharing one key** → interleaved requests arrive out of nonce order.
3. NTP stepping the clock backwards → the key is locked out until wall-clock catches up.

The failure is invisible: the error lands in the `error` array, `HasError` logs it as "Could not query
the balance", and the caller gets a sentinel (I-1).

Related **M-10**: `PostPrivateAsync` **mutates the caller's dictionary** (`body.Add("nonce", …)`) —
it works only because every caller passes a fresh one, and it is a trap for any retry wrapper. Fix it
here, since a retry wrapper is exactly what **P4-03** adds.

## Scope

### In scope
1. Process-wide monotonic nonce generator, microsecond resolution, seeded from the clock and never
   allowed to go backwards:
   ```csharp
   internal static class NonceGenerator
   {
     private static long _last;
     public static long Next()
     {
       while (true)
       {
         var candidate = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000, Volatile.Read(ref _last) + 1);
         var prior = Interlocked.CompareExchange(ref _last, candidate, candidate - 1 /* see note */);
         // implement with a proper CAS loop on the observed value
       }
     }
   }
   ```
   Implement it as a clean CAS loop over `Interlocked.CompareExchange(ref _last, candidate, observed)`;
   the sketch above is intent, not code. Unit-test that 100 000 concurrent calls yield strictly
   increasing, unique values.
2. Stop mutating the caller's dictionary (**M-10**): build a new dictionary inside `PostPrivateAsync`
   (`new Dictionary<string, object>(body) { ["nonce"] = nonce }`).
3. **Document per-service keys.** Update both secret templates with a comment, and add a README
   section stating:
   - each service needs its **own** Kraken API key (removes collision path 2);
   - the mail service's key should be restricted to *Query Closed Orders & Trades* with **no trade
     permission** (least privilege — a compromised mail container cannot trade).
   Consider making it enforceable: log a `Warning` at startup if both services are configured with the
   same key. Since they are separate processes, the practical version is a documented note plus a
   distinct config key name — say what you chose in the PR.
4. Clock-step defence: because the generator never returns a value ≤ the last one, an NTP step
   backwards no longer locks the key out. Add a `Warning` log if the wall clock is observed moving
   backwards by more than a second — that is worth knowing about on a Pi.
5. Persisting the nonce across restarts is **not** required (a restart takes longer than a
   millisecond), but note the assumption in a comment.

### Out of scope
- Error surfacing for `EAPI:Invalid nonce` → **P2-01** (the typed result makes it visible).
- Retry policy → **P4-03**.

## Acceptance criteria

- 100 000 concurrent `Next()` calls: strictly increasing, no duplicates.
- A simulated backwards clock step does not produce a non-increasing nonce.
- `PostPrivateAsync` leaves the caller's dictionary unmodified (unit test).
- Both secret templates and the README document per-service keys and the read-only mail key.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
