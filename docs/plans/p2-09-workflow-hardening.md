# P2-09 · Harden the publish workflows

|  |  |
|---|---|
| **Findings** | M-18, M-19, M-20 |
| **Phase** | 2 — Contract & numeric correctness (infrastructure track) |
| **Branch** | `ci/p2-workflow-hardening` |
| **Effort** | S (~2 h) |
| **Depends on** | **P1-05** (same files; merge P1-05 first) |
| **Blocks** | — |
| **Conflict surface** | `.github/workflows/docker-dca.yml`, `.github/workflows/docker-mail.yml`, `.github/workflows/ci.yml` |

## Problem

- **M-18** — third-party composite actions are pinned to mutable `@main` while the job holds
  `contents: write`, `packages: write`, `id-token: write` and receives `GITHUB_TOKEN`:
  [docker-dca.yml:26,49,58](../../.github/workflows/docker-dca.yml#L26). A compromised or simply
  changed upstream `main` runs with full write access to this repo and its registry.
- **M-19** — the `pull_request` trigger runs the **publish** job with those write permissions for
  same-repo branches; fork PRs always fail at the push step, giving outside contributors confusing red
  CI. (The workflow correctly uses `pull_request`, not `pull_request_target`.)
- **M-20** — no `concurrency:` groups. A commit touching `Kbot.Common` starts both workflows
  simultaneously, each computing a version and pushing a git tag.

## Scope

### In scope
1. Pin every third-party action to a full commit SHA with a trailing version comment:
   ```yaml
   uses: Zuricos/gh-actions/compute-version@<40-char-sha>   # v1.2.3
   ```
   Do the same for `actions/*` (they are first-party but pinning is uniform and dependabot's
   `github-actions` ecosystem — added in P1-05 — will keep them fresh).
2. Split build from publish on PRs: on `pull_request`, build the image **without pushing**
   (`push: false` / no registry login, no attestation, no tag push) and drop the write permissions to
   `contents: read`. Push only on `push` to `main` and `workflow_dispatch`. This makes fork PRs green.
3. Add `concurrency` to both publish workflows:
   ```yaml
   concurrency:
     group: publish-${{ github.workflow }}-${{ github.ref }}
     cancel-in-progress: false      # never cancel a half-finished registry push
   ```
   Serialise tag pushing across the two workflows if they can both bump the same tag — use a shared
   group name for the tag-push step, or move tagging into a single workflow.
4. Set `permissions:` at the top of each workflow to the least privilege the *default* jobs need, and
   raise it per job only where required.
5. Review the tag-push race: both workflows compute a version independently from a shared `VERSION`
   file (see L-3/**P5-02**). Note in the PR whether tagging should move to one workflow; do not
   redesign versioning here.

### Out of scope
- Adding CI itself → **P1-05**.
- Reconciling `VERSION` / CHANGELOG / compose tags → **P5-02** (L-3).
- Base-image pinning inside the Dockerfiles → **P4-06** (M-21).

## Acceptance criteria

- `git grep -n '@main' .github/workflows/` → no matches.
- A PR from a fork runs the build and passes without needing write permissions.
- Two pushes in quick succession do not produce two concurrent tag pushes.
- Publishing from `main` still works end to end (verify with a `workflow_dispatch` run).

## Verification

```bash
actionlint .github/workflows/*.yml     # if available
gh workflow view "Publish - DCA Service"
# after merge, dispatch once and confirm the image digest + tag land as before
```
