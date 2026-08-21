# P5-01 · README accuracy

|  |  |
|---|---|
| **Findings** | L-1, L-2, M-23 |
| **Phase** | 5 — Docs & hygiene |
| **Branch** | `docs/p5-readme-accuracy` |
| **Effort** | S (~2 h) |
| **Depends on** | Nothing technically, but the README should describe the **final** behaviour — merge after the Phase-1/2 branches that change documented behaviour (**P1-08**, **P2-08**, **P4-09**), or write it against the current state and update again. |
| **Blocks** | — |
| **Conflict surface** | `README.md` (also P4-05, P5-02, P5-03) |

## Problem

- **L-1** — **README says SQLite (`Kraken.db`); the code is PostgreSQL only.** No SQLite package exists
  anywhere in the solution. A user provisioning storage per the README never creates the Postgres
  service, so the mail service starts and silently records nothing.
- **L-2** — README calls the license "custom"; `LICENSE` is verbatim **AGPL-3.0** (661 lines, matching
  commit `6ac4228`). AGPL's network-copyleft obligation is materially different from "custom" — this
  misleads contributors and downstream users.
- **M-23** — `MailOptions.HourOfDay` is **UTC-only**
  ([DailyReporter.cs:22-30](../../src/Kbot.MailService/DailyReporter.cs#L22-L30)). A Swiss user setting
  `6` gets mail at 07:00 CET / 08:00 CEST, shifting on the DST boundary. The README does not say UTC.

## Scope

### In scope
1. Replace every SQLite reference with PostgreSQL, including the storage-provisioning instructions,
   and document the `ConnectionStrings__Kraken` requirement (post-**P1-08**: no default password).
2. State the license correctly: **AGPL-3.0**, with a one-line summary of the network-copyleft
   obligation and a link to `LICENSE`.
3. Document **UTC semantics** everywhere a time appears: `MailOptions__HourOfDay`,
   `BalanceOptions__DefaultTopupDayOfMonth` (the top-up shift is computed against 00:00 **UTC**, not
   the configured `CultureOptions` timezone), and the daily/monthly report times. Add a short
   "All times are UTC" note near the config table.
4. Correct the **Features** list. It currently advertises *"Support for multiple cryptocurrencies"*,
   which the single-`CryptoPair` worker does not do — either mark it as planned (link
   `FUTURE_FEATURES.md` **F-2**) or remove it. Do not leave it as a claim.
5. Bring the documented algorithm in line with the implementation (REVIEW.md §8 lists the
   divergences): the "wait half the remaining time and re-poll" behaviour, and that
   `TimeUntilNextTopUp` is a snapshot refreshed only after a successful order (until **P4-09** changes
   it — if that is merged, document the new behaviour instead).
6. Add a `## Running the tests` section with the live-trading warning (coordinate with **P1-01**,
   which adds a short version; keep one copy).
7. Add a `## Documentation` section linking `REVIEW.md`, `docs/ROADMAP.md`, `docs/plans/` and
   `FUTURE_FEATURES.md`.
8. Note the per-service Kraken API key recommendation from **P4-05** if that is merged.

### Out of scope
- Version reconciliation → **P5-02**.
- `CONTRIBUTING.md` / `SECURITY.md` → **P5-03**.
- Any code change. If the README and the code disagree and the code is wrong, that belongs to the
  owning plan — open an issue and link it.

## Acceptance criteria

- `git grep -in 'sqlite\|Kraken\.db' README.md` → no matches.
- The license section names AGPL-3.0.
- Every configured time value is documented as UTC.
- No feature claim in the README is unimplemented without being marked as planned.
- A reader following the README end to end reaches a working deployment (walk it through yourself).

## Verification

```bash
git grep -in 'sqlite\|Kraken\.db' README.md || echo "clean"
git grep -in 'custom' README.md
# markdown link check, if available:
npx --yes markdown-link-check README.md
```
