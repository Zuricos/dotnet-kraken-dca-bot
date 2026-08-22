# Handoff 001 · Wave 0

**Created:** 2026-08-21 · **Base commit:** `f8b248b` (`review-and-fix`) · **Status:** open

This document is self-contained. Anyone — a fresh Claude session, an agent, or a human — can execute
Wave 0 from here without reading anything that came before. Update the status board in §7 as PRs land,
then follow §8 to produce the next handoff.

---

## 1. State of play

| | |
|---|---|
| Repo | `dotnet-kraken-dca-bot` — a .NET 10 Kraken DCA bot (6 projects: `Kbot.Common`, `Kbot.DcaService`, `Kbot.MailService` + 3 test projects) |
| What exists | A full code review ([REVIEW.md](../REVIEW.md)), a phased roadmap ([ROADMAP.md](ROADMAP.md)) and 37 branch-sized implementation plans ([plans/](plans/)) |
| What has been fixed | **5 of 64 findings.** C-1 (P1-01), C-2 / C-3 (P1-02), C-4 (P1-03) and H-11 (P1-08) are merged. The rest are open. |
| Branch state | `main` = upstream, untouched. `review-and-fix` = `main` + the review + these docs, and **the integration branch all work merges into**. P1-01 (`58c2262`), P1-02 (#43) and P1-08 (#44) have landed there; everything else is still open. |

**The one thing to know:** the review's verdict is *"not safe to run unattended with real money until
C-1 … C-5 are fixed."* Those five findings are owned by plans **P1-01, P1-02, P1-03, P1-04**. They are
all in Wave 0 and they are the point of this handoff. C-1, C-2, C-3 and C-4 are closed; **C-5
(P1-04) is what is left before the bot is safe.**

---

## 2. Two hard rules

1. ~~**Never run the full test suite with credentials configured.**~~ **Resolved by P1-01**
   (`58c2262`). `dotnet test Kbot.sln` is now safe by default: `.runsettings` excludes the
   `LiveExchange` and `LiveApi` categories, and each of those tests also refuses to run without
   `KBOT_ALLOW_LIVE_TRADING=1`. Do **not** set that variable with real credentials configured — the
   `LiveExchange` tests place real orders and send real mail.
2. **Stay inside your plan's scope.** Every plan has an explicit *Out of scope* list naming the plan
   that owns each deferred item. Fixing something outside your scope creates a merge conflict for
   another agent. Note it in the PR body instead.

---

## 3. Base branch — `review-and-fix` is the integration branch

Every work branch **bases on `review-and-fix`** and **PRs back into `review-and-fix`**. `main` stays
untouched; the maintainer merges the accumulated work into it when they choose. No plan targets `main`.

**Do this once before dispatching anything** — `review-and-fix` is currently local-only, and GitHub
cannot open a PR against a branch it does not have:

```bash
git push -u origin review-and-fix
```

Every prompt in §5 starts from `origin/review-and-fix` on that basis. As PRs land, keep in-flight
branches current so each PR diff stays limited to its own plan:

```bash
git fetch origin && git rebase origin/review-and-fix
```

If you ever see a plan or an older doc say "base on `main`", it is stale — this section wins.

---

## 4. Wave 0 — nine plans, no prerequisites, fully parallel

| # | Plan | Branch | Findings | Effort |
|---|---|---|---|---|
| ~~1~~ | ~~[P1-01](plans/p1-01-c1-gate-live-trading-tests.md)~~ ✅ merged | `test/p1-c1-gate-live-trading-tests` | **C-1** | S |
| ~~2~~ | ~~[P1-02](plans/p1-02-c2-c3-guard-worker-sentinels.md)~~ ✅ merged | `fix/p1-c2-c3-guard-worker-sentinels` | **C-2, C-3** | S |
| ~~3~~ | ~~[P1-03](plans/p1-03-c4-topup-day-clamp-and-state-order.md)~~ ✅ merged | `fix/p1-c4-topup-day-clamp-and-state-order` | **C-4** | S |
| 4 | [P1-04](plans/p1-04-c5-worker-loop-resilience.md) | `fix/p1-c5-worker-loop-resilience` | **C-5** | S |
| 5 | [P1-06](plans/p1-06-h4-dockerignore-and-secret-copy.md) | `fix/p1-h4-dockerignore-and-secret-copy` | H-4 | S |
| 6 | [P1-07](plans/p1-07-h10-tighten-options-validators.md) | `fix/p1-h10-tighten-options-validators` | H-10 | S |
| ~~7~~ | ~~[P1-08](plans/p1-08-h11-database-credentials-exposure.md)~~ ✅ merged | `fix/p1-h11-database-credentials-exposure` | H-11 | S |
| 8 | [P1-09](plans/p1-09-m16-redact-secrets-in-logs.md) | `fix/p1-m16-redact-secrets-in-logs` | M-16 | XS |
| 9 | [P2-02](plans/p2-02-h1-invariant-culture.md) | `fix/p2-h1-invariant-culture` | H-1 | M |

Rows 1–4 close the five critical findings and are the priority. Row 9 is a Phase-2 plan that happens
to have no prerequisites — take it only if capacity is left over.

### Merge-order constraints inside Wave 0

Development is parallel; **merging** has two ordering constraints, both from shared files:

- `src/Kbot.DcaService/DcaWorker.cs` → merge **P1-02 → P1-03 → P1-04** (P1-02 and P1-03 are merged)
- `src/Kbot.DcaService/Utility/TimeComputeService.cs` → planned **P1-03 → P1-02**; P1-02 got there
  first, and both are now merged

The suggested merge order for the critical four was **P1-03 → P1-02 → P1-04**, but P1-02 merged
first, so **P1-03 built on it** and **P1-04 rebases onto both** — they are already in `DcaWorker.cs`
and `TimeComputeService.cs`, and branching from `review-and-fix` picks them up.
P1-01 was independent and is merged, which is what makes the suite safe for everyone else.
Each conflict is a small local edit; rebasing is a two-minute job, not a redesign.

---

## 5. Ready-to-paste agent prompts

One prompt per plan. Each is self-contained: paste it into a fresh session or an agent, nothing else
needed. Substitute the base branch if you took the alternative in §3.

<details>
<summary><b>P1-01 · Gate the live-trading tests (C-1) — ✅ merged as <code>58c2262</code>, nothing to do</b></summary>

```
Work in the repo dotnet-kraken-dca-bot.

Read docs/plans/p1-01-c1-gate-live-trading-tests.md and implement exactly that plan. It is the
spec — follow its Scope section, respect its Out of scope list, and satisfy every acceptance
criterion.

Branch: git switch -c test/p1-c1-gate-live-trading-tests origin/review-and-fix

Context you need: this repo's test suite currently places REAL buy orders on live Kraken and sends
real email. Your job is to make `dotnet test Kbot.sln` safe by default. Until your change is in,
do NOT run the full suite yourself — run individual filtered tests only.

Rules:
- Stay in scope. Do not fix findings the plan lists as belonging to another plan.
- Conventional commit messages (test:, fix:, refactor:, ci:, chore:, docs:), one logical change each.
- Verify with the commands in the plan's Verification section before you finish.
- Before opening the PR, close the plan out in a final docs: commit on your branch — the plan
  file's Status row and Resolution section, the §7 status board row, the ROADMAP tables, the
  REVIEW.md finding callout and any cross-plan note your change makes stale. The exact list is
  "Closing out a plan" in docs/plans/README.md. Phrase it as already merged.
- Then open a PR into review-and-fix titled "test: P1-01 gate the live-trading tests (C-1)", body linking
  docs/plans/p1-01-c1-gate-live-trading-tests.md, listing what you verified, and naming anything
  you deliberately left out.
```
</details>

<details>
<summary><b>P1-02 · Guard the Kraken sentinel call sites (C-2, C-3) — ✅ merged, nothing to do</b></summary>

```
Work in the repo dotnet-kraken-dca-bot.

Read docs/plans/p1-02-c2-c3-guard-worker-sentinels.md and implement exactly that plan. It is the
spec — follow its Scope section, respect its Out of scope list, and satisfy every acceptance
criterion.

Branch: git switch -c fix/p1-c2-c3-guard-worker-sentinels origin/review-and-fix

Context you need: KrakenClient signals every error with an in-band sentinel ([], 0.0, false, null)
and DcaWorker checks none of them. A failed ticker call returns 0.0, which cascades into placing
orders every 10 seconds until the fiat balance is gone. A failed balance call returns [] and the
worker indexes it directly, crashing the host into a Docker restart loop. You are adding the
guards. The typed-result redesign that replaces this tactically-guarded code is a LATER plan
(P2-01) — do not attempt it here.

Conflict note: P1-03 and P1-04 also edit src/Kbot.DcaService/DcaWorker.cs, and P1-03 also edits
TimeComputeService.cs. Keep your edits local and minimal so a rebase stays trivial.

Rules:
- Stay in scope. Do not fix findings the plan lists as belonging to another plan.
- `dotnet test Kbot.sln` is safe by default since P1-01 landed. Never set `KBOT_ALLOW_LIVE_TRADING=1`. Run filtered
  tests only.
- Conventional commit messages, one logical change each.
- Verify with the commands in the plan's Verification section before you finish.
- Before opening the PR, close the plan out in a final docs: commit on your branch — the plan
  file's Status row and Resolution section, the §7 status board row, the ROADMAP tables, the
  REVIEW.md finding callout and any cross-plan note your change makes stale. The exact list is
  "Closing out a plan" in docs/plans/README.md. Phrase it as already merged.
- Then open a PR into review-and-fix titled "fix: P1-02 guard the Kraken sentinel call sites (C-2, C-3)",
  body linking the plan file, listing what you verified, and naming anything left out.
```
</details>

<details>
<summary><b>P1-03 · Clamp the top-up day, persist state before bookkeeping (C-4) — ✅ merged as #45, nothing to do</b></summary>

```
Work in the repo dotnet-kraken-dca-bot.

Read docs/plans/p1-03-c4-topup-day-clamp-and-state-order.md and implement exactly that plan. It is
the spec — follow its Scope section, respect its Out of scope list, and satisfy every acceptance
criterion.

Branch: git switch -c fix/p1-c4-topup-day-clamp-and-state-order origin/review-and-fix

Context you need: DefaultTopupDayOfMonth accepts 1-31, but new DateTime(2026, 4, 31) throws. The
throw happens AFTER a successful order is sent and BEFORE the state is persisted, so the host dies,
Docker restarts it, the pre-order state is reloaded and it buys again. You are clamping the day and
reordering the persist.

Conflict note: P1-02 is merged, so review-and-fix already carries its guards in DcaWorker.cs and
TimeComputeService.cs — the top of InvestmentCycle and ComputeNextInvestmentInterval. Leave them
alone. P1-04 also edits DcaWorker.cs. Keep edits local and minimal.

Rules:
- Stay in scope. Do not fix findings the plan lists as belonging to another plan.
- `dotnet test Kbot.sln` is safe by default since P1-01 landed. Never set `KBOT_ALLOW_LIVE_TRADING=1`. Run filtered
  tests only.
- Conventional commit messages, one logical change each.
- Verify with the commands in the plan's Verification section before you finish.
- Before opening the PR, close the plan out in a final docs: commit on your branch — the plan
  file's Status row and Resolution section, the §7 status board row, the ROADMAP tables, the
  REVIEW.md finding callout and any cross-plan note your change makes stale. The exact list is
  "Closing out a plan" in docs/plans/README.md. Phrase it as already merged.
- Then open a PR into review-and-fix titled "fix: P1-03 clamp the top-up day and persist state before
  bookkeeping (C-4)", body linking the plan file, listing what you verified, and naming anything
  left out.
```
</details>

<details>
<summary><b>P1-04 · Worker loop resilience and backoff (C-5)</b></summary>

```
Work in the repo dotnet-kraken-dca-bot.

Read docs/plans/p1-04-c5-worker-loop-resilience.md and implement exactly that plan. It is the
spec — follow its Scope section, respect its Out of scope list, and satisfy every acceptance
criterion.

Branch: git switch -c fix/p1-c5-worker-loop-resilience origin/review-and-fix

Context you need: neither BackgroundService loop has any exception handling, so every throw in this
codebase stops the host; `restart: unless-stopped` then restarts it, and each restart with stale
state is an opportunity for an unscheduled buy. You are adding try/catch + exponential backoff to
both worker loops (DCA and mail) so transient faults are survived rather than escalated. You are
NOT fixing the individual throws — other plans own those.

Conflict note: P1-02 is merged into src/Kbot.DcaService/DcaWorker.cs and P1-03 also edits it; you
merge last of the three. P2-07 also edits DailyReporter.cs.

Rules:
- Stay in scope. Do not fix findings the plan lists as belonging to another plan.
- `dotnet test Kbot.sln` is safe by default since P1-01 landed. Never set `KBOT_ALLOW_LIVE_TRADING=1`. Run filtered
  tests only.
- Conventional commit messages, one logical change each.
- Verify with the commands in the plan's Verification section before you finish.
- Before opening the PR, close the plan out in a final docs: commit on your branch — the plan
  file's Status row and Resolution section, the §7 status board row, the ROADMAP tables, the
  REVIEW.md finding callout and any cross-plan note your change makes stale. The exact list is
  "Closing out a plan" in docs/plans/README.md. Phrase it as already merged.
- Then open a PR into review-and-fix titled "fix: P1-04 worker loop resilience and backoff (C-5)", body
  linking the plan file, listing what you verified, and naming anything left out.
```
</details>

<details>
<summary><b>P1-06 · Fix the inert .dockerignore and the secrets.json copy (H-4)</b></summary>

```
Work in the repo dotnet-kraken-dca-bot.

Read docs/plans/p1-06-h4-dockerignore-and-secret-copy.md and implement exactly that plan. It is the
spec — follow its Scope section, respect its Out of scope list, and satisfy every acceptance
criterion.

Branch: git switch -c fix/p1-h4-dockerignore-and-secret-copy origin/review-and-fix

Context you need: docker/.dockerignore is in a directory Docker never consults for this build
context, so it has no effect — the whole repo including .git is uploaded, and the csprojs
deliberately copy secrets.json to the publish output. You are moving the file to the repo root and
removing those copy directives.

Rules:
- Stay in scope. Do not fix findings the plan lists as belonging to another plan.
- `dotnet test Kbot.sln` is safe by default since P1-01 landed. Never set `KBOT_ALLOW_LIVE_TRADING=1`.
- Conventional commit messages, one logical change each.
- Verify with the commands in the plan's Verification section, including the docker build.
- Before opening the PR, close the plan out in a final docs: commit on your branch — the plan
  file's Status row and Resolution section, the §7 status board row, the ROADMAP tables, the
  REVIEW.md finding callout and any cross-plan note your change makes stale. The exact list is
  "Closing out a plan" in docs/plans/README.md. Phrase it as already merged.
- Then open a PR into review-and-fix titled "fix: P1-06 move .dockerignore to the repo root and stop copying
  secrets.json (H-4)", body linking the plan file and listing what you verified.
```
</details>

<details>
<summary><b>P1-07 · Tighten the options validators (H-10)</b></summary>

```
Work in the repo dotnet-kraken-dca-bot.

Read docs/plans/p1-07-h10-tighten-options-validators.md and implement exactly that plan. It is the
spec — follow its Scope section, respect its Out of scope list, and satisfy every acceptance
criterion.

Branch: git switch -c fix/p1-h10-tighten-options-validators origin/review-and-fix

Context you need: every numeric validator rule uses >= 0 instead of > 0, so WaitOptions with
MinWaitTime = MaxWaitTime = 00:00:00 passes validation and turns the trading loop into a busy loop
hammering Kraken. MinOrderVolume = 0 or AskMultiplier = 0 reproduces a critical finding. There is
also no WaitOptions section in appsettings.json, so an omitted env var is silently zero. You are
tightening the validators, adding safe defaults, and unit-testing every rule (there is zero
validator coverage today, which is how this survived).

Conflict note: P1-03 is merged, so BalanceOptions.cs already validates DefaultTopupDayOfMonth as
1-28 and TopUpDayClampTest.cs already covers that rule — leave both alone. P1-02 already added the
Enum.IsDefined check on OrderOptions.Type; do not add a second one.

Rules:
- Stay in scope. Do not fix findings the plan lists as belonging to another plan.
- `dotnet test Kbot.sln` is safe by default since P1-01 landed. Never set `KBOT_ALLOW_LIVE_TRADING=1`. Run filtered
  tests only.
- Conventional commit messages, one logical change each.
- Verify with the commands in the plan's Verification section before you finish.
- Before opening the PR, close the plan out in a final docs: commit on your branch — the plan
  file's Status row and Resolution section, the §7 status board row, the ROADMAP tables, the
  REVIEW.md finding callout and any cross-plan note your change makes stale. The exact list is
  "Closing out a plan" in docs/plans/README.md. Phrase it as already merged.
- Then open a PR into review-and-fix titled "fix: P1-07 tighten the options validators (H-10)", body linking
  the plan file, listing what you verified, and naming anything left out.
```
</details>

<details>
<summary><b>P1-08 · Remove the committed DB password and published port (H-11) — ✅ merged as #44, nothing to do</b></summary>

```
Work in the repo dotnet-kraken-dca-bot.

Read docs/plans/p1-08-h11-database-credentials-exposure.md and implement exactly that plan. It is
the spec — follow its Scope section, respect its Out of scope list, and satisfy every acceptance
criterion.

Branch: git switch -c fix/p1-h11-database-credentials-exposure origin/review-and-fix

Context you need: a Postgres password is committed in docker/stack.env AND baked into
src/Kbot.MailService/appsettings.json (therefore into the published image), while the compose file
publishes 5432 on all host interfaces. The documented deployment is a Raspberry Pi on a home LAN.
You are removing the port mapping, replacing the password with a placeholder, and requiring the
connection string from configuration with a fail-fast guard.

Note in your PR body that any user who already deployed with the default password must rotate it —
that is an operator action, not something you can fix in code.

Rules:
- Stay in scope. Do not fix findings the plan lists as belonging to another plan (compose
  healthchecks and depends_on belong to P4-06).
- `dotnet test Kbot.sln` is safe by default since P1-01 landed. Never set `KBOT_ALLOW_LIVE_TRADING=1`.
- Conventional commit messages, one logical change each.
- Verify with the commands in the plan's Verification section before you finish.
- Before opening the PR, close the plan out in a final docs: commit on your branch — the plan
  file's Status row and Resolution section, the §7 status board row, the ROADMAP tables, the
  REVIEW.md finding callout and any cross-plan note your change makes stale. The exact list is
  "Closing out a plan" in docs/plans/README.md. Phrase it as already merged.
- Then open a PR into review-and-fix titled "fix: P1-08 remove the committed DB password and published
  Postgres port (H-11)", body linking the plan file and listing what you verified.
```
</details>

<details>
<summary><b>P1-09 · Stop logging the API secret (M-16)</b></summary>

```
Work in the repo dotnet-kraken-dca-bot.

Read docs/plans/p1-09-m16-redact-secrets-in-logs.md and implement exactly that plan. It is the
spec — follow its Scope section, respect its Out of scope list, and satisfy every acceptance
criterion.

Branch: git switch -c fix/p1-m16-redact-secrets-in-logs origin/review-and-fix

Context you need: Secrets and MailSecrets are records, so the synthesized ToString() prints the
Kraken API secret and the Gmail app password in clear text. This codebase logs whole records and
retains log files 31 days on a shared Docker volume. You are overriding PrintMembers on both,
removing one dead local, and adding the regression test that keeps it fixed.

This is the smallest plan in the set — expect well under an hour.

Rules:
- Stay in scope. Log sink configuration and secret templates belong to other plans.
- `dotnet test Kbot.sln` is safe by default since P1-01 landed. Never set `KBOT_ALLOW_LIVE_TRADING=1`. Run filtered
  tests only.
- Conventional commit messages, one logical change each.
- Verify with the commands in the plan's Verification section before you finish.
- Before opening the PR, close the plan out in a final docs: commit on your branch — the plan
  file's Status row and Resolution section, the §7 status board row, the ROADMAP tables, the
  REVIEW.md finding callout and any cross-plan note your change makes stale. The exact list is
  "Closing out a plan" in docs/plans/README.md. Phrase it as already merged.
- Then open a PR into review-and-fix titled "fix: P1-09 stop logging the API secret (M-16)", body linking the
  plan file and listing what you verified.
```
</details>

<details>
<summary><b>P2-02 · Pin InvariantCulture on every wire value (H-1) — optional overflow work</b></summary>

```
Work in the repo dotnet-kraken-dca-bot.

Read docs/plans/p2-02-h1-invariant-culture.md and implement exactly that plan. It is the spec —
follow its Scope section, respect its Out of scope list, and satisfy every acceptance criterion.

Branch: git switch -c fix/p2-h1-invariant-culture origin/review-and-fix

Context you need: Kraken returns all numerics as strings and every one is parsed with the ambient
culture. Under de-DE, double.Parse("0.00005") returns 5 — a 100,000x error; under fr-FR it throws.
It is latent only because the containers happen to run invariant, and CultureOptions.CultureString
already exists to invite someone to change that. You are pinning InvariantCulture everywhere on the
wire/file boundary and making it structurally enforced via CA1305/CA1304/CA1310 as errors.

Conflict note: P2-01 and P2-03 will later rewrite the same parse sites. Merging this first keeps
those diffs smaller — say in your PR that the *Unparsed -> Parse() layer is intended to be deleted
by P2-03.

Rules:
- Stay in scope. Do NOT change double to decimal — that is P2-03.
- `dotnet test Kbot.sln` is safe by default since P1-01 landed. Never set `KBOT_ALLOW_LIVE_TRADING=1`. Run filtered
  tests only.
- Conventional commit messages, one logical change each.
- Verify with the commands in the plan's Verification section, including the de-DE / fr-FR runs.
- Before opening the PR, close the plan out in a final docs: commit on your branch — the plan
  file's Status row and Resolution section, the §7 status board row, the ROADMAP tables, the
  REVIEW.md finding callout and any cross-plan note your change makes stale. The exact list is
  "Closing out a plan" in docs/plans/README.md. Phrase it as already merged.
- Then open a PR into review-and-fix titled "fix: P2-02 pin InvariantCulture on every wire value (H-1)", body
  linking the plan file and listing what you verified.
```
</details>

---

## 6. Definition of done, per PR

> There is no CI yet — plan **P1-05** adds it; P1-01 has merged, so it is unblocked. Until then these
> checks are yours to run locally. Note that the two existing publish workflows are filtered to
> `pull_request: branches: [main]`, so PRs into `review-and-fix` trigger **nothing** — no image
> is built or pushed by this work.

- Every acceptance criterion in the plan is met.
- `dotnet build Kbot.sln -warnaserror` clean.
- `dotnet csharpier check .` clean (the repo is csharpier-formatted; `.editorconfig` is authoritative).
- Tests pass under the safe filter
  (applied by default through `.runsettings` since P1-01 landed).
- PR body links the plan file, lists what was verified, and names anything deliberately left out.
- Nothing outside the plan's scope changed — **except the close-out docs, which every PR carries**:
  the plan file's Status row and Resolution section, the §7 status board row, the §4 and §5 entries,
  the ROADMAP tables, the REVIEW.md finding callout, the plan index, and any cross-plan guidance the
  merge makes stale. The exact list is
  [Closing out a plan](plans/README.md#closing-out-a-plan). Write it in the tense that is true after
  the merge, so `review-and-fix` never needs a follow-up docs commit.

---

## 7. Status board

Every PR updates its own row, on its own branch, before it is opened — see
[Closing out a plan](plans/README.md#closing-out-a-plan). `—` = not started.

| Plan | Branch | Status | PR | Merged |
|---|---|---|---|---|
| P1-01 | `test/p1-c1-gate-live-trading-tests` | ✅ resolved | #42 | `58c2262` (2026-08-22) |
| P1-02 | `fix/p1-c2-c3-guard-worker-sentinels` | ✅ resolved | #43 | via #43 |
| P1-03 | `fix/p1-c4-topup-day-clamp-and-state-order` | ✅ resolved | #45 | via #45 |
| P1-04 | `fix/p1-c5-worker-loop-resilience` | — | | |
| P1-06 | `fix/p1-h4-dockerignore-and-secret-copy` | — | | |
| P1-07 | `fix/p1-h10-tighten-options-validators` | — | | |
| P1-08 | `fix/p1-h11-database-credentials-exposure` | ✅ resolved | #44 | via #44 |
| P1-09 | `fix/p1-m16-redact-secrets-in-logs` | — | | |
| P2-02 | `fix/p2-h1-invariant-culture` | — | | |

**Milestone M1 ("safe to run") is reached when P1-01, P1-02, P1-03 and P1-04 are all merged.**
P1-01, P1-02 and P1-03 are merged; **P1-04 is the last one.**

---

## 8. What the next handoff looks like

Each merge unlocks specific plans. From [ROADMAP.md](ROADMAP.md) §4:

| When this merges | These become ready |
|---|---|
| ✅ P1-01 *(merged)* | **P1-05** (`ci/p1-h3-build-test-lint-pipeline`) — **now ready**; CI could not be wired up before the tests were safe |
| ✅ P1-03 *(merged)* | **P1-10** (`test/p1-h12-deterministic-timecompute-tests`) — **now ready**; the clamp it has to assert is in |
| ✅ P1-02 *(merged)* **and** P1-04 | **P2-01** (`refactor/p2-i1-kraken-result-protocol`) — the highest-value change in the review; now waiting on P1-04 alone |
| ✅ P1-03 *(merged)* **and** P1-07 | **P2-08** (`refactor/p2-i5-shared-trading-options`) — now waiting on P1-07 alone |
| ✅ P1-08 *(merged)* | **P4-06** (`fix/p4-m17-m21-startup-and-healthchecks`) — **now ready**; the compose file no longer carries the port mapping or the password |
| P1-05 | **P2-09** (`ci/p2-workflow-hardening`) |
| P2-02 | **P2-03** (`refactor/p2-h2-decimal-money`) |

Also ready at any time, needing nothing from Wave 0: **P4-01, P4-02, P4-03, P4-05, P4-07, P4-10** —
Phase 4 is unusually independent, and it is where spare parallel capacity should go while Phase 2 is
in flight. See [ROADMAP.md](ROADMAP.md) §4 Wave 1 for the full list.

To produce Handoff 002: copy this file to `docs/HANDOFF-002.md`, set the base commit, list the plans
that are now ready, and write one prompt per plan in the §5 format.

---

## 9. Bootstrap prompt for a fresh session

Paste this into a new session that has no context at all:

```
Work in the repo dotnet-kraken-dca-bot. Read docs/HANDOFF.md — it is a self-contained handoff for
the first wave of remediation work on this repository. Follow it: check the status board in §7 to
see what is still open, then either dispatch the ready-to-paste agent prompts in §5, or implement
one plan yourself. Whichever you do, the plan's PR also carries its own close-out docs — see
"Closing out a plan" in docs/plans/README.md.

Two hard rules before you touch anything: (1) `dotnet test Kbot.sln` places real buy orders on live
Kraken and sends real email only if you opt in with KBOT_ALLOW_LIVE_TRADING=1 (P1-01 is merged) — never do; (2) each plan in
docs/plans/ has an explicit Out of scope list — respect it, because another plan owns those items
and overlapping edits create merge conflicts.
```
