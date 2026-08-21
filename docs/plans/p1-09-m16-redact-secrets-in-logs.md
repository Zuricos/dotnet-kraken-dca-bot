# P1-09 · Stop logging the API secret

|  |  |
|---|---|
| **Findings** | M-16 |
| **Phase** | 1 — Stop the bleeding |
| **Branch** | `fix/p1-m16-redact-secrets-in-logs` |
| **Effort** | XS (~30 min) |
| **Depends on** | — (start immediately) |
| **Blocks** | — |
| **Conflict surface** | `src/Kbot.Common/Options/Secrets.cs`, `src/Kbot.MailService/Options/MailSecrets.cs` — no overlap with other plans |

## Problem

[Secrets.cs:5-9](../../src/Kbot.Common/Options/Secrets.cs#L5-L9) is a `record`, so the compiler
synthesizes a `ToString()` that prints every member:
`Secrets { ApiKey = …, ApiSecret = U0VDUkVU }`. The codebase logs whole records (e.g.
`logger.LogInformation("Sending order: {OrderData}", orderRequest)`), Serilog writes to a file sink
retained 31 days on a shared volume, and any future `LogDebug(secrets.Value)` or an exception message
that formats the options object leaks the key. The same applies to `MailSecrets` (the Gmail app
password).

## Scope

### In scope
1. Override `PrintMembers` on both records so the synthesized `ToString()` cannot leak:
   ```csharp
   public record Secrets
   {
     public required string ApiKey { get; init; }
     public required string ApiSecret { get; init; }

     protected virtual bool PrintMembers(StringBuilder builder)
     {
       builder.Append("ApiKey = <redacted>, ApiSecret = <redacted>");
       return true;
     }
   }
   ```
   Consider keeping a non-secret discriminator such as the API key's first 4 characters
   (`ApiKey = AbCd…`) — useful when debugging which key is loaded, and not a credential on its own.
   Decide one way and document it in the PR.
2. Do the same for `MailSecrets` (redact the password, keep the address).
3. Grep for any log statement that passes a secret-bearing object and fix it:
   `git grep -nE 'Log(Information|Debug|Error|Warning).*(secrets|Secrets)'`.
4. Remove the dead `var s = secrets.Value.ApiKey;` at
   [KrakenApi.cs:64](../../src/Kbot.Common/Api/KrakenApi.cs#L64) — it is assigned and never used
   (a CS0219 the build does not surface today).
5. Add a unit test asserting `new Secrets { ApiKey = "k", ApiSecret = "s" }.ToString()` contains
   neither `"k"` nor `"s"`. This is the guard that keeps the fix from regressing when someone adds a
   member.

### Out of scope
- Log sink configuration, retention, `Log.CloseAndFlush()` → **P5-04** (L-8).
- Per-service API keys and nonce handling → **P4-05** (I-4).
- Secret template placeholders → **P5-04** (L-16).

## Acceptance criteria

- `ToString()` on both records redacts the credential fields.
- New unit test covers it.
- No log statement anywhere passes a `Secrets`/`MailSecrets` instance as a structured value.
- Build clean; no unused-local warning at `KrakenApi.cs:64`.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
git grep -nE 'Log(Information|Debug|Error|Warning).*[Ss]ecrets' || echo "no secret logging"
dotnet csharpier check .
```
