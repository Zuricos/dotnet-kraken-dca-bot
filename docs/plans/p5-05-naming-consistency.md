# P5-05 · Naming consistency

|  |  |
|---|---|
| **Findings** | L-11 |
| **Phase** | 5 — Docs & hygiene |
| **Branch** | `chore/p5-naming-consistency` |
| **Effort** | S (~3 h) — but a **wide** diff |
| **Depends on** | Everything else you intend to merge. **Merge this last.** |
| **Blocks** | — |
| **Conflict surface** | Very wide — renames touch most files. It is mechanical, so rebasing *onto* it is easy, but rebasing *it* onto other work is not. Land it when the queue is empty. |

## Problem

Accumulated naming defects, none functional, all friction:

| Current | Problem | Suggested |
|---|---|---|
| `InklusiveFeeMultiplier` | German/English hybrid | `FeeInclusiveMultiplier` |
| `currentInvertval` (×2) | typo | `currentInterval` |
| `vor` (validation list, all four validators) | meaningless name | `errors` |
| `costForVolume` | actually "cost of one min-size order" | `costPerMinOrder` |
| `AggregatedOrder.OrderType` | holds Buy/Sell, not Limit/Market | `Side` (or `BuyOrSell`) |
| `MailGenereateTest.cs` | filename typo | `MailGenerateTest.cs` |
| `ApiTestPublic` | tests are mostly *private* endpoints | `KrakenClientLiveTests` |
| `cl_ord_id` | snake_case in a public C# signature | `clientOrderId` (keep the wire name in the DTO/serializer only) |
| `CountyCode` vs derived `CountryCode` | county/country confusion | `RegionCode` + `CountryCode` |

## Scope

### In scope
1. All renames in the table, applied consistently — including config keys where they are user-facing.
   **`OrderOptions__Fee`-derived `InklusiveFeeMultiplier` is a computed property, so renaming it is
   safe; `CultureOptions__CountyCode` is an env var and renaming it is a breaking config change.**
   Either keep the old env key as a deprecated alias (bind both, warn on the old one) or list it as
   breaking in the CHANGELOG (**P5-02**). Say which you chose.
2. Use the IDE/Roslyn rename so references, XML docs and tests all move together. Do not hand-edit.
3. Keep the **wire** names untouched: Kraken's `cl_ord_id`, `descr`, `vol`, `ordertxid` etc. must stay
   exactly as the API expects — rename only the C# identifiers, and make sure the
   `[JsonPropertyName]`/serializer mapping still emits the original wire keys. This is the one way this
   PR could break production; verify it with a serialization test per changed DTO.
4. No behaviour change, no logic edits. If you find a bug while renaming, note it in the PR and leave
   it.

### Out of scope
- Dead-code deletion → **P5-04**.
- Any structural refactor → Phase 3.

## Acceptance criteria

- `git grep -n 'Inklusive\|Invertval\|Genereate\|CountyCode'` → no matches (except a documented
  deprecated config alias, if you kept one).
- Every serialized wire payload is byte-identical before and after — assert with round-trip tests on
  `OrderRequest`, `ClosedOrderUnparsed`, `TickerInfoUnparsed` and the cancel request.
- `dotnet build` clean, full test suite green.

## Verification

```bash
dotnet build Kbot.sln -warnaserror
dotnet test Kbot.sln --filter "TestCategory!=LiveExchange&TestCategory!=LiveApi"
git grep -n 'Inklusive\|Invertval\|Genereate\|CountyCode' || echo "clean"
dotnet csharpier check .
```
