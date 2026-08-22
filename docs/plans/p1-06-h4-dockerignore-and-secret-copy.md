# P1-06 · Fix the inert `.dockerignore` and the `secrets.json` copy

|  |  |
|---|---|
| **Status** | ✅ **Resolved** — merged into `review-and-fix` via PR #48 |
| **Findings** | H-4 |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `fix/p1-h4-dockerignore-and-secret-copy` |
| **Effort** | S (~1 h) |
| **Depends on** | — (start immediately) |
| **Blocks** | — |
| **Conflict surface** | `src/Kbot.DcaService/Kbot.DcaService.csproj`, `src/Kbot.MailService/Kbot.MailService.csproj` (also P5-04) |

## Problem

Both workflows build with `context: .` (repo root) and `dockerfile: docker/Kbot.*.Dockerfile`.
BuildKit resolves `<dockerfile-path>.dockerignore` first, then `<context>/.dockerignore`. **Neither
exists** — there is no `docker/Kbot.DcaService.Dockerfile.dockerignore` and no root `.dockerignore`.
The only file in the tree is [docker/.dockerignore](../../docker/.dockerignore), a path Docker never
consults for this context, so its entries (`**/secrets.json`, `**/appsettings.Development.json`,
`**/bin`, `**/obj`, `**/.git`) have **no effect**: the whole repo including `.git/` history and all
build output is uploaded into the build context.

This matters because
[Kbot.DcaService.csproj:19-21](../../src/Kbot.DcaService/Kbot.DcaService.csproj#L19-L21) declares
`<None Update="secrets.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory>`, so a developer's
local `secrets.json` is copied to the publish output *by design* and would land inside the shipped
image via `COPY src/ ./`.

## Scope

### In scope
1. `git mv docker/.dockerignore .dockerignore` — repo root. Verify the entries still make sense for a
   root context (they are written with `**/` prefixes, so they do).
2. Add root-context-specific entries: `.git`, `.github`, `docs`, `test`, `**/*.trx`,
   `**/TestResults`, `*.md`, `btc_adress_qr.png`, `**/state`, `**/logs`.
   Keep `**/secrets.json`, `**/secrets*-template.json`, `**/appsettings.Development.json`.
3. Delete the `<None Update="secrets.json">` and `<None Update="state.json">` item groups from both
   service csprojs. Secrets arrive via `/run/secrets/dca-secrets` (already handled in
   `ServiceCollectionExtension.Setup`) and the state file lives at `state/state.json`.
4. Verify no code path reads a `secrets.json` next to the binary after the change (grep for
   `secrets.json`), and that `dotnet publish` output no longer contains it.
5. Confirm the build context shrinks: `docker build` should no longer log a multi-hundred-megabyte
   context transfer.

### Out of scope
- Floating base-image tags, `HEALTHCHECK`, `TZ` → **P4-06** (M-21).
- Secret template naming/placeholder fixes → **P5-04** (L-16).
- Workflow changes → **P1-05** / **P2-09**.

## Acceptance criteria

- `docker build -f docker/Kbot.DcaService.Dockerfile -t kbot-dca:test .` succeeds and the reported
  build context is small (no `.git`).
- A deliberately created `src/Kbot.DcaService/secrets.json` does **not** appear in
  `docker run --rm kbot-dca:test ls -la /app`.
- `dotnet publish src/Kbot.DcaService -c Release -o /tmp/pub && ls /tmp/pub | grep -c secrets.json` → `0`.
- Both images still start and read config from env vars / mounted secrets.

## Verification

```bash
docker build -f docker/Kbot.DcaService.Dockerfile  -t kbot-dca:test  .
docker build -f docker/Kbot.MailService.Dockerfile -t kbot-mail:test .
docker run --rm --entrypoint ls kbot-dca:test -la /app
dotnet build Kbot.sln -warnaserror
```

---

## Resolution

Merged into `review-and-fix` from `fix/p1-h4-dockerignore-and-secret-copy` as PR #48. **H-4 is
closed**: a developer's local `secrets.json` can no longer reach the build context, the publish
output or the shipped image, and the build context is 380 KB instead of 41 MB.

What landed:

- [.dockerignore](../../.dockerignore) — `git mv` from `docker/.dockerignore` to the repo root, the
  only place BuildKit consults for `context: .`, so the entries take effect at all for the first
  time. Extended with the root-context entries the scope lists (`.github`, `docs`, `test`,
  `**/TestResults`, `**/*.trx`, `*.md`, `btc_adress_qr.png`, `**/state`, `**/logs`), and three
  inherited patterns that matched nothing in this repo were replaced by ones that do: `docker`
  (the `**/Dockerfile*` / `**/compose*` prefixes never matched `Kbot.*.Dockerfile` or
  `example-compose.yaml`, and the directory also carries `stack.env`), `**/*.env` (`**/.env` never
  matched `docker/stack.env`) and `**/secrets*template.json` (covers both the
  `secrets-template.json` and `secrets.template.json` spellings). The context is now exactly what
  the Dockerfiles copy: `nuget.config`, both `Directory.*.props` and `src/`.
- [src/Kbot.DcaService/Kbot.DcaService.csproj](../../src/Kbot.DcaService/Kbot.DcaService.csproj) —
  the `<None Update="secrets.json">` and `<None Update="state.json">` item groups with
  `CopyToOutputDirectory=Always` are deleted; `appsettings.json` is still copied. Nothing read the
  copied secret file: the DCA configuration chain is `appsettings.json` →
  `appsettings.{env}.json` → `/run/secrets/dca-secrets` → user secrets → environment variables, and
  the state file is read from `state/state.json` in the mounted volume, so the copy was pure
  leakage and removing it changes no behaviour.
- [Directory.Build.props](../../Directory.Build.props) — one `$(DefaultItemExcludes)` line for
  `**/*secrets.json;**/*state.json`.

Deleting the `<None>` item groups was necessary but **not sufficient**, which the scope did not
anticipate: `Microsoft.NET.Sdk.Worker.props` globs `**/*.json` into `Content` with both
`CopyToOutputDirectory` and `CopyToPublishDirectory` set, so `secrets.json` was still in the
publish output afterwards — and always had been for **Kbot.MailService**, which never had any
`<None>` items at all. That glob's `Exclude` honours `$(DefaultItemExcludes)`, so the exclusion is
declared once in `Directory.Build.props` using the shapes `.gitignore` already treats as
local-only. `src/Kbot.MailService/Kbot.MailService.csproj` is therefore unchanged, which also keeps
P5-04's conflict surface clean.

Tests: none added — this plan changes no code path, and the guarantee is verified against the built
images rather than in the test suite. P1-05's CI pipeline is the durable regression guard.

Verified: `docker build --no-cache` of both Dockerfiles succeeds; a `FROM busybox / COPY . /ctx`
probe shows the context going from 41.25 MB (including `.git`, `docs/`, `test/`, six `bin`/`obj`
trees and three planted secret/state files) to 380 KB with none of them; a deliberately planted
`src/Kbot.DcaService/secrets.json` is readable at `/app/secrets.json` in an image built from
`origin/review-and-fix` and absent from the image built on this branch; `docker run --rm
--entrypoint ls kbot-dca:test -la /app` shows binaries plus `appsettings.json`, `logs/` and
`state/` only, same for `kbot-mail:test`; `dotnet publish src/Kbot.DcaService -c Release -o /tmp/pub
&& ls /tmp/pub | grep -c secrets.json` → `0`; both images start under `--network none --env-file
docker/stack.env` and read their configuration from the environment; `dotnet build Kbot.sln
-warnaserror` clean, 64 tests pass under the default filter, `csharpier check .` clean.

Deliberately not done: floating base-image tags, `HEALTHCHECK`, `TZ` → **P4-06** (M-21); secret
template naming and placeholder fixes, including the two `src/docker.*.secrets-template.json`
placeholder files that stay in the build context → **P5-04** (L-16); workflow changes →
**P1-05** / **P2-09**; trimming the inherited Java/Node/Helm entries from `.dockerignore` →
**P5-04**.

Follow-ups unblocked: none — P1-06 blocked no other plan.
