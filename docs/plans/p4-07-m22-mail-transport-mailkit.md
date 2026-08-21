# P4-07 · Provider-agnostic mail transport

|  |  |
|---|---|
| **Findings** | M-22, L-10 |
| **Phase** | 4 — Operational hardening |
| **Branch** | `refactor/p4-m22-mail-transport-mailkit` |
| **Effort** | M (~5 h) |
| **Depends on** | Soft: **P3-01** (`IMailTransport` seam) |
| **Blocks** | — |
| **Conflict surface** | `src/Kbot.MailService/Utility/MailSenderService.cs` (also P2-05, P2-07, P4-08), `src/Kbot.MailService/Options/MailSecrets.cs`, `src/Kbot.MailService/Utility/CsvService.cs` |

## Problem

**M-22** — Gmail is hardwired: `new SmtpClient("smtp.gmail.com", 587)`
([MailSenderService.cs:141-145](../../src/Kbot.MailService/Utility/MailSenderService.cs#L141-L145)),
and `MailSecretsValidator` rejects any password that is not exactly 16 characters (a Gmail app
password), so **no other provider can be used**. There is no `Timeout`, so the default 100 s blocks
the single reporting loop. `System.Net.Mail.SmtpClient` is documented as not recommended for new
development.

**L-10** — `MailMessage`, the CSV `MemoryStream` and its `StreamWriter` are never disposed, and
`Encoding.UTF8` emits a BOM, which some CSV readers surface as a stray `ï»¿` in the first header cell.

## Scope

### In scope
1. Replace `System.Net.Mail` with **MailKit** (`MailKit` / `MimeKit`; add to
   `Directory.Packages.props`) behind an `IMailTransport` interface so the reporting code never touches
   SMTP types.
2. Make the provider configurable: `MailOptions.Smtp { Host, Port, UseStartTls, UserName }` +
   `MailSecrets.Password`. Default the host/port to Gmail's values so existing deployments keep
   working with no config change, and say so in the PR + CHANGELOG note (**P5-02**).
3. Fix `MailSecretsValidator`: validate non-empty, not length-16. Keep a `Warning` (not an error) if a
   Gmail host is configured with a password that is not 16 characters — that is a genuinely useful
   hint, just not a hard rule.
4. Set an explicit timeout (30 s) and cancellation support (**P4-04** threading).
5. Dispose everything: `using` for the message, the stream, the writer, the SMTP client. Use
   `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` for the CSV so no BOM is written.
6. Send failures must be typed and non-fatal to the loop (**P1-04** handles the retry) — but a failed
   *report* send must not advance the watermark (**P2-07**). Verify that interaction explicitly and
   note it in the PR.
7. Test with a fake `IMailTransport`: assert the subject, the HTML body content, the attachment name,
   the CSV bytes (no BOM), and that a transport failure surfaces as a typed failure rather than an
   exception escaping the reporter.

### Out of scope
- Report content correctness (`NaN`, market-order price, grouping) → **P4-08**.
- Watermark semantics → **P2-07**.
- Two-way mail/Telegram commands → `FUTURE_FEATURES.md` (runners-up).

## Acceptance criteria

- A non-Gmail SMTP host works (verify against a local `mailhog`/`smtp4dev` container).
- No `System.Net.Mail` reference remains: `git grep -n 'System.Net.Mail' src/` → nothing.
- CSV attachment has no BOM: `head -c 3 report.csv | xxd` shows the first header character.
- No undisposed `IDisposable` in the mail path (analyzer CA2000 clean, or justified).
- A 60-second SMTP stall does not block the loop beyond the configured timeout.

## Verification

```bash
docker run -d --rm -p 2525:1025 -p 8025:8025 --name smtp4dev rnwood/smtp4dev
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
# point MailOptions__Smtp__Host=localhost, Port=2525 and send a report; inspect http://localhost:8025
docker stop smtp4dev
dotnet csharpier check .
```
