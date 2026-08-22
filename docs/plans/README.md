# Remediation Plans — index & working protocol

One plan file per work item. Every plan is a **single branch and a single PR**, sized so it can be
reviewed and merged on its own. The dependency ordering, merge order and parallel batches live in
[../ROADMAP.md](../ROADMAP.md) — read that first if you are deciding *what* to pick up.

Source of all findings: [../../REVIEW.md](../../REVIEW.md). Future feature ideas were moved out to
[../../FUTURE_FEATURES.md](../../FUTURE_FEATURES.md) and are **not** part of this remediation set.

**Executing now:** [../HANDOFF.md](../HANDOFF.md) carries the active wave, ready-to-paste agent
prompts and the status board.

## Working protocol for agents

1. **Pick a plan whose `Depends on` row is empty or fully merged.** Do not start a plan whose
   prerequisites are still open — the roadmap says what unlocks what.
2. **Branch from `review-and-fix`** — the integration branch for all of this work — using the exact
   branch name in the plan's header table:
   ```bash
   git fetch origin
   git switch -c <branch-name> origin/review-and-fix
   ```
   Open the PR **into `review-and-fix`**, not `main`. `main` stays untouched until the maintainer
   merges the accumulated work in one go. See [../HANDOFF.md](../HANDOFF.md) §3.
3. **Stay inside the plan's scope.** Each plan has an explicit *Out of scope* list; the items in it
   belong to another branch and touching them creates merge conflicts for someone else. If you find
   a new problem, note it in the PR description — do not fix it here.
4. **Respect the `Conflict surface` row.** It names the files other in-flight plans also touch. Keep
   edits in those files minimal and local so a rebase stays trivial.
5. **Commit with conventional-commit messages** matching the repo history (`fix:`, `feat:`,
   `refactor:`, `test:`, `ci:`, `chore:`, `docs:`). One logical change per commit.
6. **Verify before opening the PR** — every plan lists the exact commands. At minimum:
   ```bash
   dotnet build Kbot.sln -warnaserror
   dotnet test Kbot.sln --filter "TestCategory!=LiveExchange"
   dotnet csharpier check .
   ```
   > ✅ **P1-01 is merged** (`58c2262`), so `dotnet test Kbot.sln` is safe by default — `.runsettings`
   > excludes the `LiveExchange` / `LiveApi` categories and those tests additionally require
   > `KBOT_ALLOW_LIVE_TRADING=1`. Never set that variable: `LiveExchange` places **real buy orders on
   > live Kraken** and sends real mail. See
   > [p1-01-c1-gate-live-trading-tests.md](p1-01-c1-gate-live-trading-tests.md).
7. **PR title** = plan ID + title (e.g. `fix: P1-02 guard the Kraken sentinel call sites (C-2, C-3)`).
   PR body: link the plan file, list the finding IDs closed, and state what you verified.

## Plan index

| Plan | Findings | Title | Branch |
|---|---|---|---|
| **Phase 1 — stop the bleeding** ||||
| P1-01 ✅ | C-1 | Gate the live-trading tests *(resolved — `58c2262`)* | `test/p1-c1-gate-live-trading-tests` |
| P1-02 | C-2, C-3 | Guard the Kraken sentinel call sites | `fix/p1-c2-c3-guard-worker-sentinels` |
| P1-03 | C-4 | Clamp the top-up day, persist state before bookkeeping | `fix/p1-c4-topup-day-clamp-and-state-order` |
| P1-04 | C-5 | Worker loop resilience and backoff | `fix/p1-c5-worker-loop-resilience` |
| P1-05 | H-3 | CI: build, test, format gate | `ci/p1-h3-build-test-lint-pipeline` |
| P1-06 | H-4 | Fix the inert `.dockerignore` and the `secrets.json` copy | `fix/p1-h4-dockerignore-and-secret-copy` |
| P1-07 | H-10 | Tighten the options validators | `fix/p1-h10-tighten-options-validators` |
| P1-08 | H-11 | Remove the committed DB password and published port | `fix/p1-h11-database-credentials-exposure` |
| P1-09 | M-16 | Stop logging the API secret | `fix/p1-m16-redact-secrets-in-logs` |
| P1-10 | H-12 | Deterministic `TimeComputeService` tests | `test/p1-h12-deterministic-timecompute-tests` |
| **Phase 2 — contract & numeric correctness** ||||
| P2-01 | I-1 | Replace the sentinel protocol with `KrakenResult<T>` | `refactor/p2-i1-kraken-result-protocol` |
| P2-02 | H-1 | Pin `InvariantCulture` on every wire value | `fix/p2-h1-invariant-culture` |
| P2-03 | H-2 | Move money to `decimal` | `refactor/p2-h2-decimal-money` |
| P2-04 | I-2 | Split domain model from EF entity | `refactor/p2-i2-split-domain-and-persistence` |
| P2-05 | I-3, M-3 | Resolve pair metadata from `AssetPairs` | `feat/p2-i3-asset-pair-metadata` |
| P2-06 | H-9 | Pagination integrity in `GetClosedOrders` | `fix/p2-h9-pagination-integrity` |
| P2-07 | H-5 | Monthly watermark derived from reported data | `fix/p2-h5-monthly-report-watermark` |
| P2-08 | I-5 | Shared `TradingOptions` across both services | `refactor/p2-i5-shared-trading-options` |
| P2-09 | M-18, M-19, M-20 | Harden the publish workflows | `ci/p2-workflow-hardening` |
| **Phase 3 — make it testable** ||||
| P3-01 | — | Extract seams and inject `TimeProvider` | `refactor/p3-testability-seams` |
| P3-02 | — | Extract the pure `DcaPlanner` | `refactor/p3-dca-planner` |
| P3-03 | M-25 + Appendix | Close the unit-test coverage gaps | `test/p3-coverage-gaps` |
| **Phase 4 — operational hardening** ||||
| P4-01 | H-6, I-6, L-13 | Unified atomic `JsonStateStore<T>` | `refactor/p4-h6-json-state-store` |
| P4-02 | H-7 | Holiday cache resilience | `fix/p4-h7-holiday-cache-resilience` |
| P4-03 | H-8, M-11 | `HttpClient` lifetimes and resilience handler | `refactor/p4-h8-httpclient-lifetimes-resilience` |
| P4-04 | M-24 | Thread `CancellationToken` end to end | `refactor/p4-m24-cancellation-propagation` |
| P4-05 | I-4 | Monotonic nonce and per-service API keys | `fix/p4-i4-nonce-monotonicity` |
| P4-06 | M-17, M-21 | Startup ordering, healthchecks, pinned images | `fix/p4-m17-m21-startup-and-healthchecks` |
| P4-07 | M-22, L-10 | Provider-agnostic mail transport | `refactor/p4-m22-mail-transport-mailkit` |
| P4-08 | M-1, M-7, M-8, M-12, M-13, L-12 | Reporting correctness batch | `fix/p4-reporting-correctness` |
| P4-09 | M-2, M-4, M-5, M-6 | DCA scheduling correctness batch | `fix/p4-dca-scheduling-correctness` |
| P4-10 | M-9, M-10, M-14, M-15, L-14, L-15 | Parsing and config robustness batch | `fix/p4-parsing-and-config-robustness` |
| **Phase 5 — docs & hygiene** ||||
| P5-01 | L-1, L-2, M-23 | README accuracy | `docs/p5-readme-accuracy` |
| P5-02 | L-3 | Reconcile versioning | `docs/p5-version-reconciliation` |
| P5-03 | L-4 | `CONTRIBUTING.md` and `SECURITY.md` | `docs/p5-contributing-and-security` |
| P5-04 | L-5, L-6, L-7, L-8, L-9, L-16 | Hygiene, dead code, config leftovers | `chore/p5-hygiene-dead-code-and-config` |
| P5-05 | L-11 | Naming consistency | `chore/p5-naming-consistency` |
