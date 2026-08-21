# P4-01 · Unified atomic `JsonStateStore<T>`

|  |  |
|---|---|
| **Findings** | H-6, I-6, L-13 |
| **Phase** | 4 — Operational hardening |
| **Branch** | `refactor/p4-h6-json-state-store` |
| **Effort** | S–M (~5 h) |
| **Depends on** | Soft: **P3-01** (`IDcaStateStore` seam makes this a drop-in). Can be done before, with a slightly larger diff. |
| **Blocks** | — |
| **Conflict surface** | `src/Kbot.DcaService/Utility/DcaStateHandler.cs`, `src/Kbot.MailService/Models/HistoryState.cs`, `src/Kbot.DcaService/DcaWorker.cs`, `src/Kbot.MailService/MonthlyReporter.cs` (also P2-07) |

## Problem

Commit `9940b7a` hardened `DcaStateHandler` against corrupt JSON. **`HistoryState` — which shares the
same `state:/app/state` Docker volume — was left untouched.** A power loss mid-`WriteAllText` leaves a
partial file; `HistoryState.Load()` then throws `JsonException` from the unguarded
[DailyReporter.cs:19](../../src/Kbot.MailService/DailyReporter.cs#L19), making the mail service
permanently unstartable until the user manually deletes the file inside the volume.

Three further problems in `DcaStateHandler`
([DcaStateHandler.cs:8,14-60](../../src/Kbot.DcaService/Utility/DcaStateHandler.cs#L14-L60)):

1. `File.WriteAllText` is **not atomic** — a crash mid-write leaves truncated JSON.
2. The `catch` swallows *everything* (including `IOException`/`UnauthorizedAccessException`), deletes
   the file, and returns `LastInvestmentTime = DateTime.MinValue`. Nothing is logged — the class has no
   logger. That sentinel puts `nextOrderTime` in year 1, i.e. always in the past → **an immediate
   unscheduled buy**. A truncated write is silently converted into a purchase.
3. `StateFilePath = "state/state.json"` is relative to the CWD and the directory is never created;
   both `File.WriteAllText` and `File.Delete` throw `DirectoryNotFoundException` when the parent is
   missing, so `Save`'s catch block rethrows *out of the catch*. It works in Docker only because the
   Dockerfile does `mkdir /app/state`; a local `dotnet run` crashes the host on the first save.

Also **L-13**: `state.json` is rewritten on **every** loop iteration — ~8 600 SD-card writes/day on the
documented Raspberry Pi deployment, for a file that changes a few hundred times a month.

## Scope

### In scope
1. One `Kbot.Common/State/JsonStateStore<T>.cs` with an injected path, an `ILogger`, and
   `TimeProvider` (for the corrupt-file timestamp):
   ```csharp
   Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
   var tmp = _path + ".tmp";
   File.WriteAllText(tmp, json);
   File.Move(tmp, _path, overwrite: true);   // atomic replace on the same volume
   ```
2. On a corrupt read: log at `Error`, **move the bad file aside** as `state.json.corrupt-<timestamp>`
   rather than deleting it (it is forensic evidence of a crash), and return a seed value the caller
   supplies. Never invent `DateTime.MinValue`.
3. Seed semantics — the important safety property: `LastInvestmentTime` must seed to
   `timeProvider.GetUtcNow()`, **not** `MinValue`, so a state loss can never cause an immediate
   purchase. Same idea for `HistoryState`: seed from `MailOptions.HistoryStartDate`, which is already
   the configured intent.
4. Distinguish failure kinds: `JsonException`/`InvalidDataException` → corrupt-file path;
   `IOException`/`UnauthorizedAccessException` → **do not delete anything**, log and rethrow so the
   loop's backoff (**P1-04**) handles it. Deleting a file you could not read is how a transient
   permission error becomes data loss.
5. Migrate both call sites onto it (`DcaState`, `HistoryState`), delete the two bespoke handlers, and
   give each service its own subdirectory (`state/dca/state.json`, `state/mail/history.json`) or keep
   the current filenames — pick one, and update the Dockerfiles' `mkdir` accordingly.
6. **L-13**: only write when the value changed — `if (!EqualityComparer<T>.Default.Equals(value, _lastSaved))`.
   `DcaState` is a record, so structural equality works. Log at `Debug` when a write is skipped.
7. Add `history.json` (and `*.corrupt-*`) to `.gitignore` — `.gitignore` covers `*state.json` but not
   `history.json` (I-6, L-5).
8. Tests: fresh directory; missing directory; corrupt JSON → aside-file + seed; truncated JSON; IO
   error → rethrow, file untouched; unchanged value → no write (assert via file mtime); concurrent
   save+load.

### Out of scope
- Sharing vs splitting the Docker volume — document the decision in the PR; the compose change itself
  belongs to **P4-06** if a new volume is introduced.
- The send↔persist crash window (M-6) → **P4-09**.
- Loop-level exception handling → **P1-04**.

## Acceptance criteria

- `kill -9` during a save (simulate by writing to the `.tmp` and crashing) leaves the previous state
  intact and readable.
- A corrupt `state.json` produces an `Error` log, a `state.json.corrupt-<ts>` file, and a *safe* seed
  — verified by asserting no immediate buy is scheduled.
- A local `dotnet run` in a clean directory creates `state/` and saves without throwing.
- Repeated no-op cycles produce **no** file writes.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
rm -rf /tmp/kbot-state && mkdir -p /tmp/kbot-state && (cd /tmp/kbot-state && echo '{"broken":' > state.json)
# point the store at /tmp/kbot-state in a test and assert the aside-file behaviour
dotnet csharpier check .
```
