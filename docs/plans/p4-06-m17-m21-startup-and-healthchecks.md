# P4-06 · Startup ordering, healthchecks and pinned images

|  |  |
|---|---|
| **Findings** | M-17, M-21 |
| **Phase** | 4 — Operational hardening |
| **Branch** | `fix/p4-m17-m21-startup-and-healthchecks` |
| **Effort** | M (~5 h) |
| **Depends on** | ✅ **P1-08** — merged as PR #44, so this is **ready now**; soft **P1-04** |
| **Blocks** | `FUTURE_FEATURES.md` **F-1** (self-monitoring builds directly on this) |
| **Conflict surface** | `docker/example-compose.yaml` (P1-08 is merged: the `ports` block is already gone and `POSTGRES_PASSWORD` is already a placeholder — do not re-add either), both Dockerfiles, `src/Kbot.MailService/Database/MigrationService.cs`, `src/Kbot.MailService/Program.cs` |

## Problem

**M-17** — [MigrationService.cs:9-25](../../src/Kbot.MailService/Database/MigrationService.cs#L9-L25)
swallows total migration failure after 5 attempts and lets the host start against an **unusable
database**; errors go to `Console.WriteLine`, bypassing Serilog. `mail-service` has no
`depends_on`/healthcheck for `kraken-database`, and the 25 s of retries is easily exceeded on a
Raspberry Pi cold start. It also uses `Task.Delay(...).Wait(...)` inside an `async` method.

**M-21** — both Dockerfiles use floating base tags (`runtime:10.0`), have no `HEALTHCHECK` and no
`TZ`. Without a healthcheck, a worker hung in `Task.Delay` or blocked on SMTP looks perfectly healthy
and stops trading indefinitely — the project's core failure mode is *silence*.

## Scope

### In scope
1. `MigrationService`: `await` the delay, log through `ILogger` (not `Console`), use exponential
   backoff with a configurable overall deadline (default ~2 min, plenty for a Pi), and **throw** if
   migration ultimately fails. A mail service that cannot reach its database must not pretend to be
   running.
2. Compose: add a Postgres healthcheck and make `mail-service` wait for it:
   ```yaml
   kraken-database:
     healthcheck:
       test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER} -d $${POSTGRES_DB}"]
       interval: 10s
       timeout: 5s
       retries: 10
       start_period: 30s
   mail-service:
     depends_on:
       kraken-database:
         condition: service_healthy
   ```
3. Dockerfiles: pin base images by **digest** (`mcr.microsoft.com/dotnet/runtime:10.0@sha256:…`) with
   a comment naming the tag, so dependabot's `docker` ecosystem (added in **P1-05**) can bump them.
   Set `TZ=UTC` explicitly and `ENV DOTNET_EnableDiagnostics=0` for the runtime images.
4. Add a real `HEALTHCHECK` to both images. Options, in order of preference:
   - **(a)** Switch both workers to `Microsoft.NET.Sdk.Web` and expose `/health/live` + `/health/ready`
     via `AddHealthChecks()` — Kraken reachability, DB connectivity, holiday cache populated, and
     **last-successful-cycle age**. This is the F-1 shape and the right long-term answer.
   - **(b)** A file-based liveness probe: the worker touches `state/heartbeat` at the end of every
     cycle; `HEALTHCHECK CMD` fails if the file is older than `2 × MaxWaitTime`. No web dependency.
   Pick **(a)** if you are comfortable adding the web SDK, **(b)** otherwise. Whichever you pick, the
   probe must detect a *stalled* worker, not merely a live process — a `HEALTHCHECK` that always
   passes is worse than none.
5. Add `restart:` policy review and `stop_grace_period` matching the host shutdown timeout
   (see **P4-04**).
6. Document in the README (or defer to **P5-01**) what each health endpoint/probe means.

### Out of scope
- Prometheus metrics and the dead-man's-switch alert mail → `FUTURE_FEATURES.md` **F-1**.
- Removing the published Postgres port and the committed password → **P1-08** *(done — merged as #44)*.
- Cancellation/shutdown behaviour → **P4-04**.

## Acceptance criteria

- With Postgres deliberately unreachable, `mail-service` exits non-zero with a clear logged reason
  instead of running.
- `docker compose up` starts the DB first and the mail service only once it is healthy.
- Killing the worker's progress (simulate a hang) flips the container to `unhealthy` within
  `~2 × MaxWaitTime`.
- Base images are digest-pinned; `docker compose config` is valid.

## Verification

```bash
docker build -f docker/Kbot.DcaService.Dockerfile  -t kbot-dca:test  .
docker build -f docker/Kbot.MailService.Dockerfile -t kbot-mail:test .
docker compose -f docker/example-compose.yaml config >/dev/null && echo "compose ok"
docker compose -f docker/example-compose.yaml up -d
docker inspect --format '{{.State.Health.Status}}' dca-service mail-service
dotnet build Kbot.sln -warnaserror && dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
```
