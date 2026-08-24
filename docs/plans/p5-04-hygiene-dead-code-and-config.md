# P5-04 · Hygiene, dead code and config leftovers

|  |  |
|---|---|
| **Findings** | L-5, L-6, L-7, L-8, L-9, L-16 |
| **Phase** | 5 — Docs & hygiene |
| **Branch** | `chore/p5-hygiene-dead-code-and-config` |
| **Effort** | S–M (~5 h) |
| **Depends on** | ✅ **P1-05** (#49) — CI already builds with the `-warnaserror` CLI flag, so `<TreatWarningsAsErrors>` will surprise nobody; drop the flag from `ci.yml` when you add the property, so the two do not both claim it. Still best taken after Phase 2/4, so you are not deleting code another branch is editing |
| **Blocks** | — |
| **Conflict surface** | `.gitignore`, `Directory.Build.props`, both `Program.cs`, both `ServiceCollectionExtension.cs`, `src/Kbot.MailService/Utility/HtmlService.cs`, `src/Kbot.Common/Dtos/Balance.cs`, secret templates |

> Six low-severity items. One commit each; the dead-code deletions are trivially reviewable that way.

## Findings and required fixes

### L-5 · `.gitignore` is a Python template
~190 irrelevant lines with .NET rules appended. The security-relevant patterns are correct and no
secret has ever been committed (verified over the full history). Gaps: `.vscode/` is
untracked-but-unignored, and `history.json` is not covered.
**Fix:** regenerate from the .NET template (`dotnet new gitignore`), then re-add the project-specific
entries: `*state.json`, `history.json`, `*.corrupt-*`, `secrets.json`, `.vscode/` (or commit a
deliberate `.vscode/` set), `logs/`, `state/`. Diff the before/after ignore behaviour on a dirty tree
so nothing that should be tracked becomes ignored.

### L-6 · Dead code
`Balance`/`BalanceUnparsed` entirely unreachable; `ApiUtility.ToQueryString(Dictionary<string,object>)`
unused; `PrivateMethod.OpenOrders` and `CancelOrderResponse.Pending` unreferenced;
`var s = secrets.Value.ApiKey;` assigned and never used (a CS0219 the build does not surface);
duplicate log line in `GetClosedOrders`.
**Fix:** delete all of it. Do **not** delete `PrivateMethod`/`CancelOrderResponse` members if
**P2-04** made the enums string-persisted — check first. Then add
`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to `Directory.Build.props` and fix whatever it
surfaces. *(If **P2-06** already removed the duplicate log line, skip that item.)*

### L-7 · Configuration setup no-ops
`AddUserSecrets<Secrets>()` targets `Kbot.Common`, which has **no `UserSecretsId`** — a silent no-op
duplicated across 5 call sites. The trailing `.Build()` on the `ConfigurationManager` chain discards
its result. `services.AddOptions()` is redundant.
**Fix:** remove the redundant `AddUserSecrets<Secrets>()` calls (keep the assembly-based one, which
works), drop the dangling `.Build()`, drop `AddOptions()`. Verify user secrets still load in each test
project and in both services (each has its own `UserSecretsId`).

### L-8 · Serilog configuration is partly inert
The file sink specifies **both** `outputTemplate` and `formatter` (different overloads —
`outputTemplate` is ignored), plus `archiveEvery`/`archivePath`, which are not parameters of
`Serilog.Sinks.File` 7.0.0 at all and are silently dropped. `archivePath: /logs/archive` is outside the
mounted volume anyway. There is no `Log.CloseAndFlush()` on shutdown.
**Fix:** pick one of `outputTemplate` **or** `formatter`; remove the non-existent parameters; use
`rollingInterval` + `retainedFileCountLimit` for rotation inside the mounted `logs/` volume; add
`Log.CloseAndFlush()` (or `builder.Services.AddSerilog(...)` with the host lifetime handling it) so the
last lines before shutdown are not lost. Verify by tailing the file during a `docker stop`.

### L-9 · Stray literal in every monthly report
[HtmlService.cs:119](../../src/Kbot.MailService/Utility/HtmlService.cs#L119) renders a literal `";` —
a leftover from a pre-raw-string version.
**Fix:** delete it. Add an assertion to a report-rendering test so it cannot come back.

### L-16 · Secret template inconsistencies
Malformed placeholder `"<kraken api public key"` (missing `>`) in both mail secret templates, and
inconsistent `secrets.template.json` vs `secrets-template.json` naming across five files.
**Fix:** correct the placeholders, standardise on **one** naming convention
(`secrets-template.json`), rename the outliers, and update every reference (README, compose, csproj,
`.dockerignore`).

## Out of scope
- Naming/typo renames in code → **P5-05** (L-11).
- Anything with a behavioural consequence beyond the above — if a deletion changes behaviour, stop and
  say so in the PR.

## Acceptance criteria

- `dotnet build Kbot.sln` clean with `<TreatWarningsAsErrors>` enabled.
- `git status` on a fresh clone + build shows no unexpectedly ignored or unexpectedly tracked files.
- A monthly report contains no `";`.
- Log files rotate inside `logs/` and the final line before shutdown is present.
- All secret templates parse as JSON and use one naming convention.

## Verification

```bash
dotnet build Kbot.sln
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
for f in $(git ls-files '*template*.json'); do python3 -c "import json,sys;json.load(open('$f'))" && echo "$f ok"; done
dotnet csharpier check .
```
