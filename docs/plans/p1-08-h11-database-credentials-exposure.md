# P1-08 · Remove the committed DB password and the published Postgres port

|  |  |
|---|---|
| **Findings** | H-11 |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `fix/p1-h11-database-credentials-exposure` |
| **Effort** | S (~1 h) |
| **Depends on** | — (start immediately) |
| **Blocks** | — (P4-06 also edits `example-compose.yaml`; merge this first) |
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
