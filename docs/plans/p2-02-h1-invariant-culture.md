# P2-02 · Pin `InvariantCulture` on every wire value

|  |  |
|---|---|
| **Findings** | H-1 |
| **Phase** | 2 — Contract & numeric correctness |
| **Branch** | `fix/p2-h1-invariant-culture` |
| **Effort** | M (~4–6 h) |
| **Depends on** | — (can start immediately; coordinate with **P2-01**/**P2-03**, which touch the same parse sites) |
| **Blocks** | **P2-03** (do the culture fix first, then the type change — smaller diffs, clearer review) |
| **Conflict surface** | `src/Kbot.Common/Dtos/*.cs`, `src/Kbot.Common/Api/KrakenClient.cs` (also P2-01, P2-03), `src/Kbot.MailService/Utility/CsvService.cs` (also P4-08), `src/Kbot.MailService/Options/MailOptions.cs` |

## Problem

Kraken returns all numerics as strings (`"vol":"0.00005"`). Every one is converted with
`double.Parse(s)` / `int.Parse(s)` with **no `IFormatProvider`**, binding to
`CultureInfo.CurrentCulture`. There is not a single `InvariantCulture` in `Kbot.Common`:

| Culture | `double.Parse("0.00005")` |
|---|---|
| Invariant / `de-CH` | `0.00005` ✅ |
| `de-DE` | **`5`** — `.` treated as a group separator → **100 000× error** |
| `fr-FR` | **`FormatException`** |

The same class of bug corrupts the monthly CSV, which formats with `$"{...:F8}"` under the ambient
culture — under `de-DE` the decimal comma splits cells and the file becomes unparseable.

**Why it is latent today:** the Docker base images set no `LANG`, so containers run invariant. But
`CultureOptions.CultureString` exists and is validated — its presence invites someone to set
`CultureInfo.DefaultThreadCurrentCulture`, which would silently corrupt every balance, price, volume
and fee. It is one env var away.

Sites: [KrakenClient.cs:30](../../src/Kbot.Common/Api/KrakenClient.cs#L30) ·
[ClosedOrderInfo.cs:28-31](../../src/Kbot.Common/Dtos/ClosedOrderInfo.cs#L28-L31) ·
[TickerInfo.cs:40-76](../../src/Kbot.Common/Dtos/TickerInfo.cs#L40-L76) ·
[Balance.cs:23-26](../../src/Kbot.Common/Dtos/Balance.cs#L23-L26) ·
[CsvService.cs:20-22](../../src/Kbot.MailService/Utility/CsvService.cs#L20-L22) ·
[MailOptions.cs:14](../../src/Kbot.MailService/Options/MailOptions.cs#L14)

## Scope

### In scope
1. Every `*.Parse` / `*.TryParse` / `ToString` / interpolation that touches a **wire or file** value
   gets `CultureInfo.InvariantCulture`. Sweep with:
   ```bash
   git grep -nE '(double|int|long|decimal|DateTime(Offset)?)\.(Try)?Parse\(' src/
   git grep -nE ':F[0-9]|ToString\(\)' src/
   ```
2. Prefer `TryParse` with an explicit failure path over `Parse` in DTO conversion: a malformed field
   should produce a clear, logged failure (feeding **P2-01**'s `ParseFailure`), not an exception
   swallowed three frames up.
3. Enforce it structurally so it cannot regress. Add to `.editorconfig`:
   ```ini
   dotnet_diagnostic.CA1305.severity = error   # Specify IFormatProvider
   dotnet_diagnostic.CA1304.severity = error   # Specify CultureInfo
   dotnet_diagnostic.CA1310.severity = error   # StringComparison for culture-sensitive compare
   ```
   and enable analyzers in `Directory.Build.props` (`<EnableNETAnalyzers>true</EnableNETAnalyzers>`,
   `<AnalysisMode>` at least `Recommended`). This is the acceptance test that matters.
4. CSV: format **every** numeric and date with `InvariantCulture`; the CSV is a machine-readable
   attachment, not display text. Use `CultureInfo.InvariantCulture` explicitly, and use `"O"` or an
   explicit `yyyy-MM-dd` for the date column.
5. Human-facing HTML mail may legitimately use the configured culture — decide explicitly, keep it
   confined to `HtmlService`, and add a comment saying why the two differ.
6. `MailOptions.HistoryStartDateDto` ([MailOptions.cs:14](../../src/Kbot.MailService/Options/MailOptions.cs#L14))
   parses a configured date string: `DateTimeOffset.Parse(..., CultureInfo.InvariantCulture,
   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)`.
7. Add a regression test that runs the DTO parsers and the CSV writer under
   `CultureInfo.CurrentCulture = new CultureInfo("de-DE")` and `"fr-FR"` and asserts identical output
   to invariant. Set the culture per-test (`CultureInfo.CurrentCulture = ...` in `[TestInitialize]`,
   restored in `[TestCleanup]`) — this is the test that would have caught the whole finding.

### Out of scope
- Changing `double` to `decimal` → **P2-03**.
- Deleting the `*Unparsed` → `Parse()` layer in favour of a `JsonConverter<decimal>` → do it in
  **P2-03**, where the types change anyway. Note the intent in this PR.
- `stack.env` quoting (`"de-CH"` → `CultureNotFoundException`) → **P4-10** (M-14).
- `CultureString` validation → **P4-10** (M-15).

## Acceptance criteria

- CA1305/CA1304/CA1310 are errors and the solution builds clean.
- The `de-DE` / `fr-FR` regression tests pass.
- `git grep -nE '\.Parse\([^,)]*\)' src/` returns no numeric or date parse without a provider.
- The CSV produced under any culture is byte-identical.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY=false LANG=de_DE.UTF-8 \
  dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .
```
