# P5-03 · `CONTRIBUTING.md` and `SECURITY.md`

|  |  |
|---|---|
| **Findings** | L-4 |
| **Phase** | 5 — Docs & hygiene |
| **Branch** | `docs/p5-contributing-and-security` |
| **Effort** | S (~2 h) |
| **Depends on** | ✅ **P1-01** and ✅ **P1-05** (#49) — both merged, so this is **ready** |
| **Blocks** | — |
| **Conflict surface** | New files under `.github/`; `README.md` (also P5-01, P5-02) |

## Problem

There is no `CONTRIBUTING.md`, `SECURITY.md`, issue template, PR template or `CLAUDE.md`. For a
project that handles **exchange API keys**, having no private disclosure channel means full disclosure
by default. And nothing warns a new contributor that `dotnet test` trades real money.

## Scope

### In scope
1. `SECURITY.md` — a private disclosure channel (GitHub private vulnerability reporting is the
   zero-infrastructure option; enable it in repo settings and say so), a response-time expectation,
   the supported versions table, and explicit scope: what counts as a vulnerability here (key
   handling, the state/report data path, the container images) and what does not (trading losses from
   configuration).
2. `CONTRIBUTING.md`:
   - **Test safety** — the `LiveExchange`/`LiveApi` categories, the `KBOT_ALLOW_LIVE_TRADING` opt-in,
     and a plain statement that the live tests spend money (post-**P1-01**).
   - Local setup: .NET SDK version, `dotnet user-secrets` for the Kraken keys, running Postgres for
     the mail service.
   - Formatting: `dotnet tool restore` once, then `dotnet csharpier .` before committing; csharpier is
     a local tool pinned in `.config/dotnet-tools.json` and the `.editorconfig` is authoritative.
   - The CI gate — [ci.yml](../../.github/workflows/ci.yml), landed by **P1-05** in #49: build
     `-warnaserror`, the safe-filtered test suite, `csharpier check`, and publishing gated behind it.
   - Conventional-commit prefixes as used in the history (`fix:`, `feat:`, …).
   - Versioning model (**P5-02**) and where CHANGELOG entries go.
   - A pointer to `docs/ROADMAP.md` and `docs/plans/` for anyone looking for work.
3. `.github/ISSUE_TEMPLATE/bug_report.yml` and `feature_request.yml`, plus
   `.github/pull_request_template.md`. Keep them short. The bug template must ask for: service (dca /
   mail), image tag, the relevant config (with a "redact your keys" reminder), and log excerpt.
4. `CLAUDE.md` at the repo root — a brief orientation for AI agents working in this repo: project
   layout, the build/test/format commands, the "never run the live tests" rule, and the plan/branch
   protocol from `docs/plans/README.md`. Keep it under ~40 lines and factual.
5. Add a `## Contributing` pointer in the README.

### Out of scope
- README accuracy → **P5-01**. Versioning decisions → **P5-02**.
- Enabling private vulnerability reporting is a **repo settings** action for the maintainer — call it
  out explicitly in the PR body as a manual step.

## Acceptance criteria

- All five files exist and are linked from the README.
- The test-safety warning matches the actual gating mechanism.
- The bug template renders correctly (check the Preview tab on a draft issue).
- No file in this PR contains a real credential or a real email address other than the maintainer's
  chosen contact.

## Verification

```bash
ls -la SECURITY.md CONTRIBUTING.md CLAUDE.md .github/pull_request_template.md .github/ISSUE_TEMPLATE/
npx --yes markdown-link-check CONTRIBUTING.md SECURITY.md
```
