# P1-06 · Fix the inert `.dockerignore` and the `secrets.json` copy

|  |  |
|---|---|
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
