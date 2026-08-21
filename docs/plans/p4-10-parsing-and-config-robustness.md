# P4-10 · Parsing and config robustness batch

|  |  |
|---|---|
| **Findings** | M-9, M-10, M-14, M-15, L-14, L-15 |
| **Phase** | 4 — Operational hardening |
| **Branch** | `fix/p4-parsing-and-config-robustness` |
| **Effort** | S–M (~5 h) |
| **Depends on** | Soft: **P2-02** (culture) and **P2-03** (types) touch the same DTOs — merge those first |
| **Blocks** | — |
| **Conflict surface** | `src/Kbot.Common/Conversion/OrderParser.cs`, `src/Kbot.Common/Dtos/**`, `src/Kbot.Common/Api/KrakenApi.cs` (also P4-05), `src/Kbot.Common/Options/CultureOptions.cs`, `docker/stack.env` (also P1-08, P2-08) |

> Six independent robustness defects. One commit each.

## Findings and required fixes

### M-9 · `OrderParser` silently coerces unknown values
[OrderParser.cs:22-27](../../src/Kbot.Common/Conversion/OrderParser.cs#L22-L27) — any non-`"buy"`
becomes `Sell` and any non-`"limit"` becomes `Market`, so `stop-loss`, `take-profit` and
`settle-position` all land in the DB as `Market` and skew reports. Unknown status throws a bare
`ArgumentOutOfRangeException()` with no message and no value.
**Fix:** exhaustive `switch` with an explicit default that throws a message naming the field and the
unexpected value. Add the missing `OrderType` members Kraken actually returns (`stop-loss`,
`take-profit`, `stop-loss-limit`, `take-profit-limit`, `settle-position`) — **append** them to the
enum, never insert (see I-2/P2-04: they are persisted). Prefer string persistence (P2-04) before
extending the enums; if P2-04 is not merged, append only and say so in the PR.

### M-10 · `PostPrivateAsync` mutates the caller's dictionary
[KrakenApi.cs:67](../../src/Kbot.Common/Api/KrakenApi.cs#L67) — works only because every caller passes
a fresh dictionary; a trap for any retry wrapper.
**Fix:** copy before adding the nonce. *(Also listed in **P4-05** — whoever merges second drops the
duplicate.)*

### M-14 · Quoted values in `stack.env`
[stack.env:1,5,10-12](../../docker/stack.env#L1) — Compose strips quotes, but
`docker run --env-file` and systemd `EnvironmentFile=` do **not**. `new CultureInfo("\"de-CH\"")`
throws `CultureNotFoundException`.
**Fix:** remove every quote from `stack.env` (no value there needs them), and add a comment warning
that quotes are literal for non-compose consumers.

### M-15 · `CultureString` validated only for emptiness
[CultureOptions.cs:12,21-24](../../src/Kbot.Common/Options/CultureOptions.cs#L12) —
`new CultureInfo("bogus")` does **not** throw on ICU; it yields a synthetic culture, and the failure
surfaces later inside the swallowed holiday fetch.
**Fix:** validate against `CultureInfo.GetCultures(CultureTypes.AllCultures)` (or construct with
`new CultureInfo(name, useUserOverride: false)` and check `!culture.ThreeLetterISOLanguageName.Equals("ivl")`
— pick the reliable check and unit-test it with `"bogus"`, `"de-CH"`, `""`). Validate `CountyCode`
against the ISO-3166 country/subdivision shape the holiday API expects, and unit-test the
`CountyCode` → `CountryCode` derivation.

### L-14 · `#nullable disable` hides genuine nullability
`PublicHoliday.cs` has a file-wide `#nullable disable`, hiding that `Counties` genuinely *is* nullable
upstream. Several DTOs use `= null!;` on non-`required` members, converting a missing JSON field into a
downstream `NullReferenceException` instead of a clear `JsonException`.
**Fix:** remove the `#nullable disable`, mark `Counties` nullable, and replace `= null!;` with
`required` (or a nullable type). A missing field must fail at deserialization with a message naming
the field.

### L-15 · Endpoint paths derive from enum member names
`$"/0/private/{method}"` with no test or attribute pinning them — a routine rename silently produces a
404.
**Fix:** either annotate each member with `[EnumMember(Value = "…")]`/a `[Description]` and map
explicitly, or add a unit test asserting the exact expected path string for every member of
`PrivateMethod` and `PublicMethod`. The test is cheaper; do that at minimum.

## Out of scope
- Culture-invariant parsing → **P2-02**. Decimal types → **P2-03**. Enum persistence → **P2-04**.
- Nonce generation → **P4-05**.

## Acceptance criteria

- An unknown Kraken order type/status produces a logged, descriptive failure — never a silent
  `Market`/`Sell`.
- `docker run --env-file docker/stack.env alpine env` shows no literal quotes.
- `CultureOptions { CultureString = "bogus" }` fails validation at startup.
- A DTO with a missing required field throws `JsonException` naming the field, not an NRE later.
- Every enum → endpoint path is asserted by a test.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
docker run --rm --env-file docker/stack.env alpine env | grep -c '"' || echo "no literal quotes"
dotnet csharpier check .
```
