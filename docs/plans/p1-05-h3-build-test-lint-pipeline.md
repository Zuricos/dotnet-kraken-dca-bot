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
- **[global.json](../../global.json)** pins the SDK to 10.0.300 with `rollForward: latestPatch`, and
  `setup-dotnet` reads it via `global-json-file`. A floating `10.0.x` next to `-warnaserror` means the
  first SDK shipping a new default warning reddens an unrelated PR, and a gate that reddens for
  reasons its author did not cause is a gate people learn to click through. The drift was already
  real: the runner resolved **10.0.400** while the maintainer built on 10.0.300. Because the file now
  exists it also joins both publish `paths:` filters and the `compute-version` input, per scope item 3.
- **`timeout-minutes: 15`** on the job. The default is 360, this suite holds socket-opening tests held
  back only by a filter, and the job is a `workflow_call` dependency of both publish workflows — so a
  hang would sit on a release for six hours instead of going red. The real run is ~45 s.

### One correction to the belt-and-braces claim in scope item 1

Scope item 1 says to keep both the `--filter` and `.runsettings`, "belt and braces". **They do not
stack.** `test/Directory.Build.props` applies `.runsettings` only when `VSTestTestCaseFilter == ''`, so
passing `--filter` takes the settings file out of the picture: CI has exactly **one** selection-level
exclusion, not two. The job is still safe, but through three *independent* layers rather than two
stacked filters — the `--filter` itself, `KBOT_ALLOW_LIVE_TRADING="0"` pinned at workflow level, and
`LiveGuard.RequireOptIn()`. Do not weaken the `--filter` expecting a runsettings backstop.

The same misreading had a live consequence in the protocol docs, fixed here: `docs/plans/README.md`
told every agent to run `dotnet test Kbot.sln --filter "TestCategory!=LiveExchange"` before opening a
PR, which **selects** the 8 `LiveApi` tests (72 selected versus 64 for the bare command). Four of them
call live Kraken *private* endpoints signed with the developer's real keys. **`LiveGuard` guards only
the `LiveExchange` tests**, so nothing else stopped them. The documented command is now bare
`dotnet test Kbot.sln`. Adding `LiveGuard.RequireOptIn()` to the `LiveApi` tests is a code change this
plan does not own — **P1-01's gating is incomplete and this deserves its own finding.**

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

### Things the gate does *not* buy, so nobody over-trusts it

- **#48's `KBOT0001` guard is only partially enforced by CI.** The `GuardLocalOnlyFilesOutOfOutput`
  target does run here, since this workflow builds `Kbot.sln`, and it catches an explicit
  `<None Include="secrets.json" CopyToOutputDirectory="…" />`. But the shape H-4 actually had —
  `<None Update="…">` metadata on a **gitignored** file — produces no MSBuild item when the file is
  absent, and CI checks out from git. That exact regression is caught on a developer machine only.
- **Widening `compute-version`'s `change_path` retroactively changes the commit count.** The action
  runs `bump_each_commit: true` with `minor_pattern: "feat"`, and the version is a count of matching
  commits since the last `dca-` / `mail-` tag. Commits like `935ca73 fix: Bump the all group` and
  `33b2353 feat: dotnet 10 update` now count, so the next publish may be a **minor** jump rather than
  the patch anyone expects. Forward-only, no tag collision — but **P5-02** should not have to
  rediscover why the numbers stepped.
- **After #48 lands, its new root `.dockerignore` is in neither publish `paths:` filter** (`docker/**`
  covered the old location). A change to the file defining the whole build context would trigger no
  publish — the same bug class this plan fixed. It does not exist on this branch, so **P2-09** owns it.
- **Bare `push:` also fires on tag pushes**, so each release's `push-tag` step adds one more CI run
  against a just-tested ref; `tags-ignore: ['**']` is the one-line fix if it ever becomes noise. And
  `push` + `pull_request` both fire for a same-repo PR branch, so a PR into `main` touching both
  services is up to **four** CI runs (branch push, `pull_request`, and both publish workflows' `ci`
  children), not the three stated earlier. At ~45 s a run this is immaterial.
- **The `ci.yml` `cancel-in-progress: true` reaches into the publish workflows.** A
  `workflow_dispatch` while a push-triggered run of the same publish workflow is in flight shares the
  concurrency group and cancels the older run's `ci` child, skipping its `build-and-publish`. That is
  fail-closed and arguably right, but "I re-ran it manually and the earlier image never appeared" is
  expected rather than a bug. Belongs with **P2-09**'s publish `concurrency:` work.
