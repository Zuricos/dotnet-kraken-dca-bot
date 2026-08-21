# P1-05 · CI: build, test and format gate

|  |  |
|---|---|
| **Findings** | H-3 |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `ci/p1-h3-build-test-lint-pipeline` |
| **Effort** | S (~3 h) |
| **Depends on** | **P1-01** (the test filter must already be safe, otherwise CI trades money) |
| **Blocks** | P2-09 (workflow hardening), and every later phase benefits from the gate |
| **Conflict surface** | `.github/workflows/docker-dca.yml`, `.github/workflows/docker-mail.yml`, `.github/dependabot.yml` — shared with **P2-09**. Merge this one first. |

## Problem

The two workflows in [.github/workflows/](../../.github/workflows/) are the only ones, and each has
exactly two jobs (`compute-version`, `build-and-publish`). Neither runs `dotnet build`,
`dotnet test`, `dotnet format` or `csharpier --check` — grepping the directory for `dotnet` returns
nothing. Broken interval maths, corrupt state serialization or broken HMAC signing ships straight to
`ghcr.io/zuricos/kraken-dca-service:latest`.

Two compounding defects:

- **Dependabot's PRs trigger nothing.** It watches `directory: "."` and under central package
  management edits `/Directory.Packages.props`, which matches none of the workflows' `paths:`
  filters (`src/Kbot.Common/**`, `src/Kbot.<Service>/**`, `docker/**`) — despite being `COPY`'d into
  both images. The monthly grouped bump (commit `935ca73` touched **14 packages**) gets zero
  validation *and* builds no image, so GHCR keeps shipping old versions while `main` claims new ones.
- **The version gate is inert.** `if: ${{ needs.compute-version.outputs.needsBump }}` uses raw string
  truthiness; GitHub casts the literal string `"false"` to `true`. Use `== 'true'`.

## Scope

### In scope
1. New `.github/workflows/ci.yml`, on `push` (all branches) and `pull_request`, with **no `paths:`
   filter and no `branches:` filter** — remediation PRs target the `review-and-fix` integration
   branch, not `main`, so a `branches: [main]` filter would leave every one of them ungated:
   - `actions/checkout@<sha>`, `actions/setup-dotnet@<sha>` with the SDK version from
     `global.json`/`Directory.Build.props` (verify which pins .NET 10).
   - `dotnet restore Kbot.sln`
   - `dotnet build Kbot.sln --no-restore -c Release -warnaserror`
   - `dotnet test Kbot.sln --no-build -c Release --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi" --logger trx --collect:"XPlat Code Coverage"`
     (the `.runsettings` from P1-01 should make the filter redundant — keep both, belt and braces)
   - `dotnet csharpier check .`
   - Upload the trx + coverage as an artifact.
   - Add `concurrency: { group: ci-${{ github.ref }}, cancel-in-progress: true }`.
   - `permissions: { contents: read }` only.
2. Gate publishing on it: add `needs: [ci]` (or a `workflow_call` of `ci.yml`) to `build-and-publish`
   in **both** publish workflows, and fix the version gate to `== 'true'`.
3. Extend both publish workflows' `paths:` filters with the files that actually affect the image:
   `Directory.Packages.props`, `Directory.Build.props`, `nuget.config`, `Kbot.sln`, `global.json`
   (if present).
4. Extend [dependabot.yml](../../.github/dependabot.yml) with the `github-actions` and `docker`
   ecosystems (both `directory: "/"`, monthly, grouped), keeping the existing `nuget` block.
5. Make the CI job's failure signal unambiguous: no `continue-on-error`, no `|| true`.

### Out of scope
- Pinning the third-party `Zuricos/gh-actions/*` composite actions to SHAs, `concurrency:` on the
  publish workflows, and the `pull_request` permissions problem → **P2-09** (M-18, M-19, M-20).
- `<TreatWarningsAsErrors>` in the csproj/`Directory.Build.props` → **P5-04** (L-6). Use the
  `-warnaserror` CLI flag here so the two do not conflict.
- Adding a coverage threshold — nice to have, but the coverage baseline only becomes meaningful after
  **P3-03**. Note the intended threshold in the PR description instead.

## Acceptance criteria

- A PR that breaks a unit test, breaks the build, or violates csharpier formatting fails CI.
- A PR that only touches `Directory.Packages.props` runs CI **and** (on merge to `main`) rebuilds
  both images.
- The publish job does not run when CI fails.
- `needsBump == 'false'` genuinely skips the publish job (verify by reading the action's output
  contract, or by a `workflow_dispatch` dry run).

## Verification

```bash
# Locally reproduce exactly what CI runs:
dotnet restore Kbot.sln
dotnet build Kbot.sln --no-restore -c Release -warnaserror
dotnet test Kbot.sln --no-build -c Release --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
dotnet csharpier check .

# Validate workflow syntax before pushing:
gh workflow list
actionlint .github/workflows/*.yml   # if available
```
