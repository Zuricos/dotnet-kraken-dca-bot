# P5-02 · Reconcile versioning

|  |  |
|---|---|
| **Findings** | L-3 |
| **Phase** | 5 — Docs & hygiene |
| **Branch** | `docs/p5-version-reconciliation` |
| **Effort** | S (~2 h) |
| **Depends on** | **P2-09** (whatever it decides about tag pushing), and ideally merged **after** the breaking changes so their CHANGELOG entries are accurate |
| **Blocks** | — |
| **Conflict surface** | `VERSION`, `CHANGELOG.md`, `docker/example-compose.yaml` (also P1-08, P4-06), `.github/workflows/**` (also P1-05, P2-09) |

## Problem

Three disagreeing versions: [VERSION](../../VERSION) = `1.2.9`, the newest
[CHANGELOG.md](../../CHANGELOG.md) entry = `v1.1.0`, and
[example-compose.yaml](../../docker/example-compose.yaml) pins `:2.0.1`. The CHANGELOG has had no entry
in ~20 months despite the .NET 10 migration, the project restructure, and the **breaking SQLite →
PostgreSQL switch** — a silent data-loss path for anyone upgrading. The workflows compute per-service
versions independently, so one root `VERSION` file cannot represent both images.

## Scope

### In scope
1. Decide and document the versioning model. Recommended: **per-service versions** derived by the
   existing `compute-version` action (which already takes a `prefix: dca`/`mail`), with the root
   `VERSION` file either deleted or repurposed as a repo-level marker that nothing consumes. Whatever
   you choose, write it down in `CONTRIBUTING.md` (**P5-03**) or a `docs/VERSIONING.md`.
2. Reconstruct the CHANGELOG from git history for the missing ~20 months, at release granularity, with
   a prominent **BREAKING** section for:
   - SQLite → PostgreSQL (data migration path: there is none — say so explicitly, and describe what a
     user must do)
   - .NET 10 / project restructure
   - central package management
   - every breaking config change from this remediation effort (**P1-08** DB credentials,
     **P2-08** `TradingOptions` renames, **P4-07** SMTP settings, **P4-09** state-file shape)
   Use the Keep-a-Changelog format the file already uses.
3. Pin `example-compose.yaml` to versions that actually exist in GHCR, and add a comment telling users
   to pin rather than track `latest`.
4. Make the mismatch impossible to repeat: add a CI check — extend the `ci` job in
   [ci.yml](../../.github/workflows/ci.yml), which **P1-05** landed in #49 — that fails if
   the CHANGELOG's newest version does not match the computed version, or if `example-compose.yaml`
   references a tag that does not exist in the registry. Keep it simple — a grep-and-compare script is
   enough.

### Out of scope
- Workflow tag-push serialisation → **P2-09**.
- README content → **P5-01**.

## Acceptance criteria

- `VERSION`, `CHANGELOG.md` and `example-compose.yaml` tell one consistent story.
- Every breaking change since `v1.1.0` has a CHANGELOG entry with an upgrade note.
- The new CI check fails on a deliberately mismatched version (test it once, then revert the test).

## Verification

```bash
head -30 CHANGELOG.md
cat VERSION
grep -n 'image:' docker/example-compose.yaml
gh api /users/zuricos/packages/container/kraken-dca-service/versions --jq '.[].metadata.container.tags' | head
```
