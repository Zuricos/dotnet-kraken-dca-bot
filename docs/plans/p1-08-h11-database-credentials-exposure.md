# P1-08 · Remove the committed DB password and the published Postgres port

|  |  |
|---|---|
| **Status** | ✅ **Resolved** — merged into `review-and-fix` via PR #44 |
| **Findings** | H-11 |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `fix/p1-h11-database-credentials-exposure` |
| **Effort** | S (~1 h) |
| **Depends on** | — (start immediately) |
| **Blocks** | P4-06 (also edits `example-compose.yaml`) — **unblocked by this merge** |
| **Conflict surface** | `docker/stack.env`, `docker/example-compose.yaml` (also P4-06), `src/Kbot.MailService/appsettings.json` |

## Problem

`POSTGRES_PASSWORD="NotYourK3yNotYourCoin$"` is committed in
[docker/stack.env:19,23](../../docker/stack.env#L19) **and** baked into
[src/Kbot.MailService/appsettings.json:3](../../src/Kbot.MailService/appsettings.json#L3), therefore
into the published image. [docker/example-compose.yaml:31-32](../../docker/example-compose.yaml#L31-L32)
publishes `ports: - "5432:5432"`, binding Postgres to **all** host interfaces.

Unlike the Kraken keys and the SMTP password — correctly kept out of the repo and injected via docker
secrets — this credential is in git and is the default every user inherits. The README's target
deployment is a Raspberry Pi on a home LAN: anyone on that network (or the internet, if the Pi is
port-forwarded) can read the user's complete trading history using a password published on GitHub.
The `ports` mapping is not needed at all — `mail-service` reaches the DB by service name over the
compose network.

## Scope

### In scope
1. Delete the `ports:` block from `kraken-database` in `example-compose.yaml`. If a local debugging
   escape hatch is wanted, add a commented-out `# - "127.0.0.1:5432:5432"` with a one-line warning.
2. Replace the literal password in `docker/stack.env` with an obvious placeholder in **both** places
   (`POSTGRES_PASSWORD` and the `ConnectionStrings__Kraken` string): `<CHANGE_ME>`. Add a comment
   above them stating that the two must match and that this file is committed, so real secrets belong
   in the docker secret files instead.
3. Remove the connection string from `src/Kbot.MailService/appsettings.json` entirely and require it
   from configuration. Fail fast with a clear message if `ConnectionStrings:Kraken` is absent —
   check where `AddDbContextFactory` reads it (`src/Kbot.MailService/Utility/ServiceCollectionExtension.cs`)
   and add an explicit null/empty guard there rather than letting Npgsql throw.
4. Prefer moving the DB credential out of `stack.env` and into the existing docker-secret mechanism:
   document `ConnectionStrings__Kraken` in `docker.mail.secrets-template.json` as the recommended
   location and leave `stack.env` with only the non-secret settings. Keep `POSTGRES_PASSWORD` in
   `stack.env` as a placeholder because the Postgres image needs it, and say so in a comment.
5. Update the README's deployment section to say "set your own password in both places before first
   start" (a 3-line note; the larger README rewrite is **P5-01**).
6. Confirm no other committed file contains the old password: `git grep -n 'NotYourK3y'` must return
   nothing after the change.

### Out of scope
- Rotating any password that a user may already have deployed — call it out in the PR body as an
  operator action, and note the breaking-change entry needed in the CHANGELOG (**P5-02**).
- `depends_on` / healthcheck for `kraken-database` → **P4-06** (M-17, M-21).
- Rewriting git history to purge the password — not worth it; it is a documented default, not a live
  credential. State this explicitly in the PR so the decision is recorded.

## Acceptance criteria

- `git grep -n 'NotYourK3y'` → no matches.
- `docker compose -f docker/example-compose.yaml config` is valid and exposes no host port for
  `kraken-database`.
- Mail service startup with no `ConnectionStrings__Kraken` fails immediately with a message naming
  the setting.
- Mail service starts normally when the connection string is provided via env var or docker secret.

## Verification

```bash
git grep -n 'NotYourK3y' || echo "clean"
docker compose -f docker/example-compose.yaml config >/dev/null && echo "compose ok"
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
```

---

## Resolution

Merged into `review-and-fix` from `fix/p1-h11-database-credentials-exposure` as PR #44. **H-11 is
closed**: no committed file carries a usable database password any more, the database is no longer
reachable from the host network, and a mail service without a configured connection string refuses to
start instead of falling back to a password published on GitHub.

What landed:

- [docker/example-compose.yaml](../../docker/example-compose.yaml) — the `ports: - "5432:5432"` block
  is gone from `kraken-database`. `mail-service` reaches the database by service name over the compose
  network, so no host port is needed; a loopback-only `# - "127.0.0.1:5432:5432"` stays commented out
  as a local debugging escape hatch, with a one-line warning about what publishing it costs.
- [docker/stack.env](../../docker/stack.env) — `ConnectionStrings__Kraken` is removed from this
  committed file and documented in the mail-service secret template instead. `POSTGRES_PASSWORD`
  stays, because the postgres image only accepts its password as an environment variable, but as the
  placeholder `<CHANGE_ME>`; a comment block says the file is committed, that real secrets belong in
  the docker secret files, and that the two passwords must match.
- [src/Kbot.MailService/appsettings.json](../../src/Kbot.MailService/appsettings.json) — the whole
  `ConnectionStrings` block is deleted, so the credential is no longer baked into the published image.
- [src/Kbot.MailService/Utility/ServiceCollectionExtension.cs](../../src/Kbot.MailService/Utility/ServiceCollectionExtension.cs)
  — an explicit null/empty guard before `AddDbContextFactory` throws `InvalidOperationException`
  naming `ConnectionStrings:Kraken` and both places it can be supplied, rather than letting Npgsql
  fail later with an opaque error.
- [src/docker.mail.secrets-template.json](../../src/docker.mail.secrets-template.json) — documents
  `ConnectionStrings:Kraken` as the recommended home for the credential, alongside the Kraken and
  SMTP secrets that were already handled that way.
- [README.md](../../README.md) — a three-line note under Installation: set your own password in both
  places before the first start, and rotate it if you already deployed an earlier version.

Two things the scope implied but did not spell out:

- `ConnectionStrings__Kraken` had to be *removed* from `stack.env` rather than left as a placeholder.
  `AddEnvironmentVariables()` is registered after the secret file, so an env var would have silently
  overridden the docker secret the plan asks operators to use.
- [src/Kbot.MailService/secrets-template.json](../../src/Kbot.MailService/secrets-template.json) got
  the same entry. With no fallback in `appsettings.json`, local development needs the connection
  string in user secrets, and the template is where a developer looks for the shape.

Tests: [ConnectionStringGuardTest.cs](../../test/Kbot.MailService.Test/ConnectionStringGuardTest.cs) —
a missing and a blank `ConnectionStrings:Kraken` each fail fast with a message naming the setting, and
a configured one composes the service graph normally.

Verified: `dotnet build Kbot.sln -warnaserror` clean, 49 tests pass under the default filter
(rebased onto P1-03),
`csharpier check .` clean, `docker compose -f docker/example-compose.yaml config` valid with no
published port on any service.

`git grep -n 'NotYourK3y'` is clean across every shipped file
(`-- ':!REVIEW.md' ':!docs/'`). The literal survives only in the REVIEW.md finding text — the
historical record the close-out protocol says to leave alone — and in this plan's own Problem and
Verification sections, which quote it to describe the bug.

Deliberately not done:

- Rotating a password an operator already deployed — that is an operator action, called out in the PR
  body and in the README note. The breaking-change CHANGELOG entry → **P5-02**.
- Rewriting git history to purge the password. Recording the decision: it is a documented default
  rather than any single deployment's live credential, every reachable copy is now a placeholder or a
  historical reference, and a force-pushed rewrite of a public repository costs every fork more than
  it buys. Rotation is the mitigation.
- `depends_on` and a healthcheck for `kraken-database`, and pinning `postgres:latest` → **P4-06**
  (M-17, M-21). Only the `ports` block was touched, so that plan's diff on the same file stays small.
- The larger README rewrite → **P5-01**.

Follow-ups unblocked: **P4-06** — its only hard prerequisite was this plan.
