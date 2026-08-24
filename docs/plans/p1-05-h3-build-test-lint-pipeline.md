# P1-05 · CI: build, test and format gate

|  |  |
|---|---|
| **Status** | ✅ **Resolved** — merged into `review-and-fix` via PR #49 |
| **Findings** | H-3 |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `ci/p1-h3-build-test-lint-pipeline` |
| **Effort** | S (~3 h) |
| **Depends on** | **P1-01** (the test filter must already be safe, otherwise CI trades money) |
| **Blocks** | P2-09 (workflow hardening), and every later phase benefits from the gate |
| **Conflict surface** | `.github/workflows/docker-dca.yml`, `.github/workflows/docker-mail.yml`, `.github/dependabot.yml` — shared with **P2-09**. This one merged first, so P2-09 branches off a `review-and-fix` that already has the `ci` job, the fixed version gate and the extended `paths:` filters. |

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

---

## Resolution

Merged into `review-and-fix` from `ci/p1-h3-build-test-lint-pipeline` as PR #49. **H-3 is closed**:
nothing reaches `ghcr.io` any more without a green `dotnet build -warnaserror`, a green test run and a
clean `csharpier check`, and a change that only touches the shared build files no longer slips through
unbuilt and unvalidated.

What landed:

- [.github/workflows/ci.yml](../../.github/workflows/ci.yml) — new. One `ci` job on `ubuntu-latest`:
  `dotnet tool restore`, `dotnet restore Kbot.sln`, `dotnet build --no-restore -c Release
  -warnaserror`, `dotnet test --no-build -c Release` under the `TestCategory!=LiveExchange&
  TestCategory!=LiveApi` filter with `--logger trx --collect:"XPlat Code Coverage"`, then
  `dotnet csharpier check .`, then the trx + Cobertura upload. `permissions: contents: read` only,
  `concurrency` with `cancel-in-progress`, no `continue-on-error` and no `|| true`. Deliberately no
  `paths:` and no `branches:` filter — remediation PRs target `review-and-fix`, so the
  `branches: [main]` filter the publish workflows use would leave every one of them ungated.
- [.github/workflows/docker-dca.yml](../../.github/workflows/docker-dca.yml),
  [.github/workflows/docker-mail.yml](../../.github/workflows/docker-mail.yml) — a `ci` job that
  calls `ci.yml`, and `build-and-publish` now has `needs: [compute-version, ci]`, so a red build
  skips it. The inert `if: ${{ needs.compute-version.outputs.needsBump }}` is now `== 'true'`;
  `compute-version`'s `check_bump_version` step emits the literal strings `true` / `false`, and bare
  truthiness was reading `"false"` as true. Both the trigger `paths:` and the `compute-version`
  `paths:` input gained `Directory.Build.props`, `Directory.Packages.props`, `nuget.config` and
  `Kbot.sln`.
- [.github/dependabot.yml](../../.github/dependabot.yml) — `github-actions` and `docker` ecosystems
  added, monthly and grouped; the `nuget` block is untouched.

Four things the scope implied but did not spell out:

- **[.config/dotnet-tools.json](../../.config/dotnet-tools.json)** pins csharpier 1.3.0 as a local
  tool. `dotnet csharpier check .` — the command this plan and
  [README.md](README.md#working-protocol-for-agents) both name — only resolves for a *local* tool; with
  csharpier installed globally it fails with *"dotnet-csharpier does not exist"*. The alternative was an
  unpinned `dotnet tool install -g` in CI, which would move the goalposts on every csharpier release.
- **The `compute-version` `paths:` input** had to change alongside the trigger `paths:`. It is the
  `change_path` `PaulHatch/semantic-version` counts commits against, so extending only the trigger
  would give a `Directory.Packages.props`-only commit a CI run but still `needs_bump=false` and no
  image — half of acceptance criterion 2.
- **`concurrency.group` is `ci-${{ github.workflow }}-${{ github.ref }}`**, not the plan's
  `ci-${{ github.ref }}`. Under `workflow_call` both `github.workflow` and `github.ref` are the
  *caller's*, so the plan's literal group would put the direct CI run and the two publish-triggered
  runs of a `main` push in one group with `cancel-in-progress: true` — they would cancel each other.
- `artifacts/` is now gitignored, since the test step writes its trx and coverage there.

The `docker` Dependabot block points at `/docker` rather than the plan's `/`: that ecosystem scans the
named directory for Dockerfiles and compose files, and all of this repo's live under `docker/`, so `/`
would have left the block inert — the same class of bug this plan exists to fix.

Tests: none added; this plan *is* the test harness. The gate's two failure modes were verified by
deliberately breaking the tree and reverting — an unformatted file makes `csharpier check` exit 1, and
an unused local makes `-warnaserror` turn CS0219 into `error CS0219` and fail the build.

Verified: `dotnet build Kbot.sln -warnaserror` clean (0 warnings), 64 tests pass under the default
filter, `csharpier check .` clean across 81 files. All four YAML files parse and the job graphs read
back as intended. Acceptance criterion 4 was checked against `compute-version`'s output contract
rather than a `workflow_dispatch` dry run.

Deliberately not done: SHA-pinning `Zuricos/gh-actions/*`, `concurrency:` on the publish workflows and
the `pull_request` permissions problem → **P2-09** (M-18, M-19, M-20). `<TreatWarningsAsErrors>` in
`Directory.Build.props` → **P5-04** (L-6); the `-warnaserror` CLI flag is used here so the two do not
collide. Base-image digest pinning → **P4-06** (M-21). No coverage threshold: coverage is collected and
uploaded but nothing fails on it, because the baseline is only meaningful after **P3-03** — the
intended first gate is roughly 60% lines / 50% branches, ratcheted upward.

Follow-ups unblocked: **P2-09**, and **P5-03** / **P5-04**, which both wanted a working gate to point
at. Every plan from here on gets its `build`, `test` and `csharpier` run automatically on the PR.
