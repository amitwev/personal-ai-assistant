# F9b — parse a tool call out of the answer

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F9a made the assistant able to reach a chat model and return its answer to the owner
over Telegram, with no tools involved. This slice gives the model something to call: `tools` are
added to the chat request, `tool_calls` are parsed out of the response, and `IAiClient.AskAsync`
changes shape to return `Result<ToolCall>` in place of `Result<string>`. `CreateTaskTool` is the
first `IAssistantTool`, describing the `create_task` tool the model may invoke — it executes
nothing: `ITaskService` has no method to create a task until F10, and this slice does not add
one.

**Tech Stack:** .NET 10 (`net10.0`), nullable enabled, warnings are errors — the existing stack.
This slice adds **no new NuGet package**. The one new serialisation need — embedding a JSON
Schema object as a nested value on the wire request rather than as an escaped string — is met by
`System.Text.Json.Nodes.JsonNode`, already part of the BCL and already the type
`WireMockFixture.cs` builds its own seeded payloads from.

**Spec:** `docs/design/slice-1-reminders.md` §5.2 (system prompt — unchanged by this slice),
§5.3 (tools table — `create_task`'s parameters), §5.4 (time contract — `due_at_local`'s shape,
resolved at F10, not here), §3.3 (`Contracts` holds `CreateTaskRequest`), §3.4 (`Impl/Tools/`
already names `CreateTaskTool` as a resident), §3.6 (extension seams — the `IAssistantTool` row
already describes this slice's end state), §7.2 (one test owns one behaviour), §12.1 (XML docs),
§12.5 (primary constructors), §12.6 (no emoji).
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F9b's own entry, closed by
this slice; F10's entry marks the boundary this slice does not cross.

---

## Where this sits

F9a shipped as four independently reviewable PRs (settings, the clock and system prompt, reaching
the model, the owner gets the answer), each with its own plan document. F9b is smaller than any
single F9a slice needed to be split: Decision 4, below, counts the diff at roughly 650 lines
against the repository's 1000-line budget, comfortably inside it, so this ships as **one PR**,
written as three commits for review clarity — the wire and tool shapes, the failure mode Decision
1 below names, and the backlog record.

There is no F9c. F9b closes F9's own backlog entry (already split into F9a/F9b at F9a-4); F10
picks up immediately after, giving `ITaskService` a create method and `CreateTaskTool` something
to call.

---

## Global Constraints

Every constraint the F9a plans carried forward still applies here, unchanged:

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error everywhere.
- **Every class taking arguments uses a primary constructor.** No separate constructors.
  `CreateTaskTool` takes none, so it declares a plain constructor-less class — nothing to
  primary-construct.
- **CS9113 is an error**: a primary-constructor parameter nothing references fails the build.
  `AiClient` gains one new parameter, `IEnumerable<IAssistantTool> tools`, in the same commit
  that starts reading it — never declared a step ahead of its use.
- Every enum's first member is `Unknown`, with no explicit numeric values. New members are
  **appended**, never inserted. **This slice is not exempt: it appends
  `ErrorCode.ModelReturnedNoToolCall`** after `ModelReturnedNoAnswer`, the sixth member added
  since F5a. No existing member's implicit value moves.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- Central package management; no inline `Version=` (NU1008). Not exercised this slice — no
  package changes, per the Tech Stack line above.
- No emoji anywhere: source, tests, docs, or commit messages.
- **Never run `docker compose down -v`.** Use `docker compose -f compose.test.yaml down` with no
  flags.
- Integration tests need `docker compose -f compose.test.yaml up -d` first — **no `--build`**:
  this slice does not touch `tests/Assistant.WireMock/`. `AiStubs.cs`'s existing default mapping
  (a prose-only answer, `{"choices":[{"message":{"role":"assistant","content":"Stubbed
  answer."}}]}`) already exercises the "no tool call" path this slice adds a name for — it needs
  no change; see "Verified facts," below.
- PR budget: 1000 changed lines per PR, excluding the plan (which merges on its own, docs-only).
  Decision 4 counts this slice at roughly 650 lines and does not propose a split.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
  ```

---

## Verified facts this plan rests on

- **Every file this plan modifies was read in full at `c1d9a96`, HEAD of this branch, before any
  code block below was written.** `MessageHandler.cs` (53 lines), `TelegramListenerTests.cs` (139
  lines), `AiClient.cs` (50 lines), `AiClientTests.cs` (123 lines), `WireMockFixture.cs` (365
  lines), `ImplServiceCollectionExtensions.cs` (141 lines), `IAiClient.cs` (25 lines),
  `ErrorCode.cs` (43 lines), `ITaskService.cs` (38 lines) — line counts from `wc -l` at HEAD, not
  estimated.
- **The wire types already live one-record-per-file** (`AiRequest.cs`, `AiMessage.cs`,
  `AiResponse.cs`, `AiChoice.cs`, each in `src/Assistant.Impl/Ai/`), not the single `AiWire.cs`
  the F9a-3 plan proposed. F9a-3's own plan document describes `AiWire.cs`; the code that actually
  merged (`e1bcad3`) split it four ways instead, one file per type named for what it holds. This
  slice's four new wire types (`AiTool`, `AiFunctionDefinition`, `AiToolCall`, `AiFunctionCall`)
  follow the shipped convention, one file each, not the plan's.
- **`AiMessage.cs` already anticipates this slice, in its own doc comment.** `Content`'s
  `<param>` tag reads, verbatim: *"What was said, or <see langword="null"/> on a response that
  carries only tool calls (F9b) — harmless now, since F9a never sends or reads a null one."* This
  slice is where that null actually gets read.
- **`IAiClient.cs`'s own remarks already state this slice's obligation, verbatim:** *"this
  interface changes shape at F9b, when `AskAsync` starts returning `Result<ToolCall>` so a tool
  invocation can be parsed out of the answer."* Both facts confirm this plan is completing a
  seam F9a deliberately left open, not inventing a new one.
- **`ITaskService.cs` (`src/Assistant.Interfaces/ITaskService.cs`) declares exactly two methods:
  `MarkReminderSentAsync` and `GetDueRemindersAsync`. No create method exists.** Confirmed by
  reading the file directly, per this plan's brief. This is the fact Decision 2 rests on: an
  `IAssistantTool.ExecuteAsync` in this slice would have nothing real to call.
- **`Assistant.Contracts` holds exactly two files today, `ErrorCode.cs` and `Result.cs`** — no
  request or response type has shipped yet. `ToolCall` and `CreateTaskRequest` are the first.
- **`ConventionTests.Contracts_declares_no_interfaces`**
  (`tests/Assistant.UnitTests/Architecture/ConventionTests.cs:72`) fails the build the moment
  `Assistant.Contracts` declares a public interface — confirmed by reading the test. Neither new
  Contracts type in this slice is an interface, so this rule is inert here, not exercised.
- **Spec §3.4's `Impl/` folder tree already names `Impl/Tools/` as `CreateTaskTool`'s home**,
  separate from `Impl/Ai/`, which holds the transport (`IAiApi`, `AiClient`, `SystemPrompt`).
  `CreateTaskTool.cs` is created at that path, not alongside the wire types.
- **Spec §3.6's extension-seams table already lists `IAssistantTool` as a seam** with
  `CreateTask, ListTasks, UpdateTask, CompleteTask` as its eventual implementations — an
  end-state description, not a per-slice progress tracker. Shipping only `CreateTaskTool` this
  slice does not contradict it, and the table needs no edit.
- **The rejected monolith this project's early planning abandoned**
  (`docs/2026-08-16-personal-ai-assistant-slice1-plan.md`) drafted `IAssistantTool` with an
  `InvokeAsync(string argumentsJson, CancellationToken ct)` member and a `CreateTaskTool`
  constructor-injecting `ITaskService` directly, calling `_tasks.CreateAsync(...)` from inside
  the tool. Confirmed by reading the file (lines 1439–1470, 6836–6890). Decision 2, below,
  names this shape and rejects it for this slice on the same grounds the rest of this project's
  planning has rejected that monolith throughout: it bundles a feature this slice's own
  `ITaskService` cannot yet support.
- **`AiStubs.cs`'s existing default WireMock mapping needs no change.** It answers with
  `{"choices":[{"message":{"role":"assistant","content":"Stubbed answer."}}]}` — a message with
  content and no `tool_calls`. Once `AiClient` is hardened (Commit 2, below) this shape produces
  `ErrorCode.ModelReturnedNoToolCall`, which is exactly the right default for an unseeded manual
  run: no tests depend on the unseeded default, and it now fails informatively instead of
  crashing. `tests/Assistant.WireMock/` is therefore absent from this slice's file list, and no
  `--build` is needed to bring the stub up.
- **`docs/tech-debt.md`'s two entries are both unaffected.** The `Result`/`Result<T>` entry names
  no type this slice touches. The "each handler opens its own scope" entry's trigger is F6's
  second `ITelegramUpdateHandler`; this slice adds no handler, so its trigger does not fire here.

---

## Inherited context: what this slice reads from F9a

`AiClient` (F9a-3, hardened F9a-3 Commit 2) already owns the try/catch around the provider call,
already returns `ErrorCode.ModelUnavailable` on a transport failure and `ErrorCode.ModelReturnedNoAnswer`
on an empty `choices` array. This slice's `AiClient` keeps both branches untouched — a provider
failure is still a provider failure, an empty response is still empty — and adds exactly one new
branch below them: a non-empty response whose chosen message carries no tool call.

`SystemPrompt.Build()` (F9a-2) is called exactly once per request, unchanged; this slice adds no
new caller and no new content to the prompt text itself — spec §5.3's tools travel on the
request's own `tools` field, not folded into the prompt string.

`MessageHandler` (F9a-4) already resolves `IAiClient` from a per-call `IServiceScopeFactory`
scope, for the reason recorded in its own doc comment and in `docs/tech-debt.md`. This slice
changes what it does with the `Result` that comes back, not how it reaches `IAiClient`.

`WireMockFixture.SeedAiAnswerAsync` (F9a-3) is kept exactly as shipped and gets a second job in
this slice: it already produces a message with `content` and no `tool_calls`, which is precisely
the shape Decision 1's new failure mode needs to seed. No change to that method.

---

## Decisions

### 1. What happens when the model replies with prose instead of a tool call

The wire format allows `content` **or** `tool_calls` on an assistant message — never a
requirement that one be present. Once `AskAsync` returns `Result<ToolCall>`, a prose-only reply
has nowhere to go inside a successful result.

**Decision: treat it as a named failure.** `ErrorCode` gains one appended member,
`ModelReturnedNoToolCall` — *"The chat model was reached and answered, but without calling any
tool."* `AiClient.AskAsync` returns `Result<ToolCall>.Failure(ErrorCode.ModelReturnedNoToolCall)`
when the chosen `choice.Message.ToolCalls` is null or empty, and `MessageHandler` maps that one
code to a fixed, plain sentence telling the owner their message was not read as a task, distinct
from the existing "I could not reach the model" sentence used for `ModelUnavailable` and
`ModelReturnedNoAnswer`.

**Alternatives considered:**

- **A result type that carries either prose or a tool call.** Something like
  `Result<Either<string, ToolCall>>`, or a hand-rolled discriminated union with a `Kind`
  discriminator and two nullable payload fields. Rejected: C# has no built-in union type, so this
  means hand-building one — a `Kind` enum plus two nullable properties plus the discipline never
  to read the wrong one — to serve a case no shipped feature actually needs to *do* anything with
  yet. `docs/tech-debt.md`'s own `Result`/`Result<T>` entry already declines a comparable
  duplication-avoidance move ("one generic type, dropping the non-generic") on cost grounds; this
  would add complexity in the same neighbourhood, for less benefit, since nothing downstream reads
  the prose branch even if the type existed to carry it.
- **Naming the new code `ModelDidNotCallATool`.** Reads naturally, but breaks the established
  grammar: every prior model-facing code in this enum is `Model` + past-tense verb + object
  (`ModelUnavailable` is the odd one out only in having no object) — `ModelReturnedNoAnswer` is
  the direct sibling this new member sits next to, differing from it only in *what* came back
  empty. `ModelReturnedNoToolCall` reads as that sibling on sight; `ModelDidNotCallATool` does
  not signal the relationship at all.
- **Sending `tool_choice: "required"` so the provider cannot return prose at all.** The provider
  supports it: OpenRouter advertises `tool_choice` alongside `tools` and `structured_outputs` for
  every model this project has run against. Setting it would make this failure mode nearly
  unreachable rather than merely named. Rejected for this slice on backlog §1's own terms: a new
  field on `AiRequest` needs a test asserting it lands on the wire, and that test would assert a
  value nothing downstream reads. The request therefore goes out with the provider default,
  `auto` — a decision recorded here, not an omission. It is also the default the later features
  want: F13's raw-capture fallback (spec §5.6), and any reply that is not a task at all, both
  need the model free to answer without calling a tool, so `required` would have to be lifted
  again. Once F10 closes the loop and there is a real prose rate to look at, `tool_choice` is the
  lever to reach for, with observed behaviour to justify it.

**Why the recommendation holds:** YAGNI, plus the failure path already exists as working
machinery from F9a — `Result<T>.Failure(ErrorCode)` and `MessageHandler`'s existing branch on
`result.IsSuccess` need only a second branch, not new infrastructure. F13 ("never lose a
capture," spec §5.6) is where the real fallback story belongs: saving the raw text as an undated
task when every provider or every parse fails. Building that here, before F10 even gives
`ITaskService` anywhere to save a task, would be exactly the kind of speculative reach this
project's YAGNI rule (backlog §1) forbids.

### 2. What `IAssistantTool` carries at F9b

`CreateTaskTool` cannot execute anything today: `ITaskService` has no create method until F10
(confirmed directly — "Verified facts," above).

**Decision: at F9b the interface carries only what a tool definition needs on the wire
request** — `Name`, `Description`, `ParametersJsonSchema` — and nothing about invocation.

```csharp
public interface IAssistantTool
{
    string Name { get; }
    string Description { get; }
    string ParametersJsonSchema { get; }
}
```

**Say this plainly, not softened:** adding an `ExecuteAsync` member at F10 is a **modification**
to this interface, not an extension by a new class. `IAssistantTool` is a young interface — one
implementation, one slice old — and this project has already accepted the identical trade once
this quarter: `IAiClient.AskAsync` changed its return type at this very slice, and F9a's own
plan (and the backlog's "Settled at F9a" record) named that a modification outright rather than
dressing it up as something else, because `IAiClient` has exactly one production implementation
and is a transport abstraction, not one of spec §3.6's *behaviour* seams. `IAssistantTool`
genuinely is one of those seams — the backlog's own text says so ("The seam F9b actually grows
is `IAssistantTool`") — but a seam having several implementations eventually does not mean every
member has to arrive on day one. Growing a *member* on an interface that already has multiple
implementations is a heavier edit than growing one that has exactly one; at F9b, `IAssistantTool`
still has exactly one (`CreateTaskTool`), so the cost of this particular modification, when F10
pays it, is the cheapest it will ever be.

**Alternative considered and rejected: define `ExecuteAsync` now, with no implementation to
call.** This is precisely the shape the rejected monolith drafted — `InvokeAsync(string
argumentsJson, CancellationToken ct)` returning a `string` result, with `CreateTaskTool`
constructor-injecting `ITaskService` and calling `_tasks.CreateAsync(...)` directly from inside
the tool (confirmed by reading that file — "Verified facts," above). It fails on this project's
own terms twice over. First, backlog §1's definition of done: "no code was added that nothing
exercises" — an `ExecuteAsync` with no real `ITaskService.CreateAsync` to call could only be
implemented by throwing `NotImplementedException`, returning a hardcoded string, or — the
monolith's own route — pulling `ITaskService.CreateAsync` and the mapping it needs into this
slice regardless, which is exactly the scope creep the F9a/F9b split exists to prevent. Second,
it collapses the transport concern (what goes on the wire, parsed back out) into the same
interface as the domain concern (what happens when a tool actually runs) a slice before the
domain side has anywhere to land. Keeping them apart for one slice costs one interface edit at
F10; merging them now costs writing throwaway code today.

### 3. Where `ToolCall` and `CreateTaskRequest` live, and what shape they take

**`ToolCall` lives in `Assistant.Contracts`,** next to `ErrorCode` and `Result<T>` — the same
project the backlog already names for `CreateTaskRequest`. `Assistant.Contracts` "declares no
interfaces" (build-enforced), not "declares no non-interface types besides request/response
shapes" — spec §3.2 describes it as "what a caller speaks to the application," and a parsed tool
call is exactly that: the shape one collaborator (`IAiClient`) hands to another
(`MessageHandler`, and later F10's dispatcher) across a project boundary. It cannot live in
`Assistant.Interfaces`: `Interfaces` references `Models` and `Contracts` (spec §3.2), and
`IAiClient.AskAsync` — declared in `Interfaces` — needs to name `Result<ToolCall>` in its own
signature, which only resolves if `ToolCall` sits somewhere `Interfaces` already reaches. It
cannot live in `Assistant.Impl`, for the same reason in reverse: `Interfaces` does not reference
`Impl`, and never will (`Impl → Interfaces`, not the other way).

```csharp
public sealed record ToolCall(string Name, string ArgumentsJson);
```

**`ToolCall` carries the raw arguments JSON string, not already-bound typed arguments.** The
alternative — `ToolCall<T>` or a `ToolCall` holding a pre-deserialized `object`/`CreateTaskRequest`
— was considered and rejected: binding requires knowing *which* type to bind to, which requires
knowing which tool was called, which is exactly the dispatch step `IAssistantTool` does not yet
perform (Decision 2). `AiClient` parses the wire far enough to know a tool was named and what
raw text it was handed — it has no way to know, and no business deciding, that `"create_task"`
means `CreateTaskRequest` specifically. That binding is the calling tool's job, arriving at F10
alongside `ExecuteAsync`. `Assistant.Contracts` "references nothing" (spec §3.2); a `ToolCall`
that already carried a bound `CreateTaskRequest` would still respect that rule today, since both
types would live in the same project — but it would not respect it going forward, the moment a
second tool (`ListTasksTool`, say) needed its own bound shape and `ToolCall` had to choose one
type argument or grow a discriminated case per tool. A raw string defers that problem entirely,
at the cost of one `JsonSerializer.Deserialize<CreateTaskRequest>(...)` call wherever the binding
eventually happens.

**`CreateTaskRequest`, also in `Contracts`,** carries exactly the fields this slice's own test
exercises — `Title` and `DueAtLocal` — not the full `title`, `due_at_local`, `notes`, `priority`
spec §5.3's table eventually wants. `Notes` and `Priority` wait for F10 and F12 respectively,
the features that give `ReminderTask` itself somewhere to put them (the backlog's own F10 and F12
entries say so: "`ReminderTask` regains `Notes`," "`ReminderTask` regains `Priority`" — neither
field exists on the model today). Advertising them on the wire schema before anything downstream
can accept them would be exactly the "contract type property... the same feature exercises with
a test" YAGNI violation backlog §1 rules out.

```csharp
public sealed record CreateTaskRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("due_at_local")] string? DueAtLocal);
```

**`DueAtLocal` is `string?`, never `DateTime?` or `DateTimeOffset?`.** Spec §5.4 is explicit that
the model returns "an absolute local ISO string... no offset" and that resolving it — converting
local wall-clock text to a UTC instant against the configured IANA zone, applying the past/future
guard clauses — is `LocalTimeResolver.Resolve`'s job. **That resolution is F10's work, not this
slice's**: F10's `CreateTaskTool.ExecuteAsync` (once it exists) is the seam where
`ILocalTimeResolver.Resolve(DateTime.Parse(request.DueAtLocal))` gets called, turning this raw
string into the `Result<DateTimeOffset>` spec §5.4's guard table describes, with a failure
becoming a follow-up question to the user rather than a silent store. Typing `DueAtLocal` as a
parsed `DateTime` here would either duplicate that resolution early (nowhere to put the zone) or
silently discard the "no offset, local-not-UTC" distinction the string preserves — `DateTime`
carries a `Kind` that `System.Text.Json`'s default parsing would set to `Unspecified` for a
no-offset string, which is *accidentally* correct today only because nothing reads it yet.
Staying a string keeps the seam visible instead of pre-committing to an interpretation this slice
has no resolver call site for.

**Both records need explicit `[JsonPropertyName]` attributes, not the naming policy `IAiApi`
enjoys.** `JsonNamingPolicy.SnakeCaseLower`, configured once on `AddAssistantAi`'s
`RefitSettings.ContentSerializer`, only applies to traffic through `IAiApi` — the Refit client.
Deserialising a `ToolCall.ArgumentsJson` string happens with a plain
`JsonSerializer.Deserialize<CreateTaskRequest>(json)` call, wherever binding eventually runs
(this slice's own test today; F10's dispatcher later), with no naming policy in scope. Without
the attributes, `"due_at_local"` would not bind to `DueAtLocal` — .NET's default matching is
case-insensitive, not snake-to-Pascal-aware. `WireMockFixture.cs`'s own `SendMessagePayload` and
`AiRequestPayload` records already carry this exact pattern, for the identical reason: they too
are deserialised outside `IAiApi`'s configured pipeline.

### 4. Whether F9b fits one PR

The repository owner's budget is 1000 changed lines per PR, excluding the plan document. Each
estimate below is grounded in the size of the closest existing analogue at HEAD, read directly
(see "Verified facts"), not guessed.

| File | Change | Basis | Est. lines |
| :--- | :--- | :--- | ---: |
| `src/Assistant.Impl/Ai/AiTool.cs` | new | `AiChoice.cs`, 7 lines, one-field wrapper record | 12 |
| `src/Assistant.Impl/Ai/AiFunctionDefinition.cs` | new | `AiRequest.cs`, 11 lines, three-param record | 18 |
| `src/Assistant.Impl/Ai/AiToolCall.cs` | new | `AiChoice.cs` shape, three params, longer remark | 20 |
| `src/Assistant.Impl/Ai/AiFunctionCall.cs` | new | `AiMessage.cs`, 13 lines | 16 |
| `src/Assistant.Impl/Ai/AiRequest.cs` | modify | current 11 lines, +1 param +1 `<param>` | 8 |
| `src/Assistant.Impl/Ai/AiMessage.cs` | modify | current 13 lines, +1 optional param | 10 |
| `src/Assistant.Impl/Ai/AiClient.cs` | modify | current 50 lines, new dependency + rebuilt body | 55 |
| `src/Assistant.Interfaces/IAiClient.cs` | modify | current 25 lines, signature + remarks rewritten | 16 |
| `src/Assistant.Interfaces/IAssistantTool.cs` | new | `ITaskService.cs`, 38 lines, comparable interface | 32 |
| `src/Assistant.Impl/Tools/CreateTaskTool.cs` | new | `SystemPrompt.cs`, 38 lines, one-class Impl file | 48 |
| `src/Assistant.Contracts/ToolCall.cs` | new | two-param `Contracts` record with remarks | 18 |
| `src/Assistant.Contracts/CreateTaskRequest.cs` | new | `SendMessagePayload`-style attributed record | 26 |
| `src/Assistant.Contracts/ErrorCode.cs` | modify | F9a-3's own two-member addition (~14 for two) | 7 |
| `src/Assistant.Impl/Telegram/MessageHandler.cs` | modify | current 53 lines, new branch + two consts | 30 |
| `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` | modify | current 141 lines, one registration line | 4 |
| `tests/.../Infrastructure/WireMockFixture.cs` | modify | current 365 lines, one method + two payload types | 75 |
| `tests/.../Ai/AiClientTests.cs` | modify | current 123 lines, six tests replacing four | 160 |
| `tests/.../Telegram/TelegramListenerTests.cs` | modify | current 139 lines, rename + reseed + one test | 45 |
| `docs/design/2026-08-22-slice-1-feature-backlog.md` | modify | F8/F9a's own "Settled at" entries, comparable size | 50 |
| **Total** | | | **650** |

650 is comfortably under 1000, and under the roughly-700 line this plan's brief names as the
point to start considering a split — so **no split.** It is larger than F9a-3's ~260-line
happy-path-plus-hardening estimate (the largest of F9a's four slices by code) because this slice
touches more *files*, not more logic per file: four small new wire records, two new `Contracts`
types, one new `Interfaces` type, and test-fixture growth to seed and read back the new wire
shape, where F9a-3 built one client against types that already had somewhere to live. Commit
boundaries (Steps, below) keep each individual commit small enough to review in one sitting
regardless.

---

## What this slice does NOT include

- **`ITaskService.CreateAsync`, any migration, any `ReminderTask` model property.** F10's. Nothing
  in this slice writes a row, and `CreateTaskTool` has no dependency capable of writing one.
- **`IAssistantTool.ExecuteAsync` or any dispatch from a parsed `ToolCall` to the tool it names.**
  Decision 2. F10 adds both, once there is something real for `ExecuteAsync` to call.
- **Resolving `CreateTaskRequest.DueAtLocal` against `ILocalTimeResolver`.** Decision 3 names the
  seam; F10 is where the call actually happens.
- **`ListTasksTool`, `UpdateTaskTool`, `CompleteTaskTool`.** Spec §5.3's table names all four
  tools; only `create_task` ships here. The other three arrive with the features that give them
  something to call, the same reasoning Decision 2 applies to `CreateTaskTool`'s own execution.
- **`Notes` and `Priority` on `CreateTaskRequest`.** Decision 3. Both wait for the features that
  give `ReminderTask` a field to put them in — F10 and F12 respectively.
- **Multi-turn tool results.** The wire format's `id` field on a tool call exists so a subsequent
  message can reply in a `tool` role, echoing it back. This slice parses `id` off the wire (onto
  `AiToolCall`, the internal wire type) and drops it at the boundary into `ToolCall`, the public
  shape `AiClient` returns — nothing sends a follow-up turn yet, so nothing needs it preserved.
- **A dedicated unit test for `CreateTaskTool`.** Its three properties are exercised end-to-end by
  `AiClientTests`' wire-placement test, which reads `Name` back off the request `IAiApi` actually
  received through the full `AddAssistantAi` container — the same "no unit test for behaviour
  integration already covers" reasoning spec §7.2 and `AGENTS.md` already state.
- **`FallbackChatClient`, Polly, retry, circuit breaking, the raw-capture safety net of spec
  §5.6.** F13's, unchanged from every prior slice's deferral of the same ground.
- **The "typing…" indicator.** Deferred again at F9a-4; still not this slice's concern.
- **`response_format` and structured outputs.** The provider supports both alongside tool calling
  — OpenRouter advertises `response_format` and `structured_outputs` for the models this project
  runs against — and this slice deliberately uses `tools` instead. A `response_format` schema pins
  one shape to the whole response, which serves a single-purpose extraction call; spec §5.3's
  table names four tools the model must eventually choose between, and choosing is exactly what
  `tool_calls` is for. Structured outputs would have to be replaced at the second tool rather than
  extended to it.
- **Any change to `AiStubs.cs` or the WireMock stub image.** "Verified facts," above, explains
  why the existing default mapping already serves this slice correctly.

---

## File Structure

```
src/Assistant.Contracts/
    ToolCall.cs                            new                                  (Commit 1)
    CreateTaskRequest.cs                   new                                  (Commit 1)
    ErrorCode.cs                           + ModelReturnedNoToolCall            (Commit 2)

src/Assistant.Interfaces/
    IAssistantTool.cs                      new                                  (Commit 1)
    IAiClient.cs                           AskAsync -> Result<ToolCall>         (Commit 1)

src/Assistant.Impl/
    Ai/AiTool.cs                           new                                  (Commit 1)
    Ai/AiFunctionDefinition.cs             new                                  (Commit 1)
    Ai/AiToolCall.cs                       new                                  (Commit 1)
    Ai/AiFunctionCall.cs                   new                                  (Commit 1)
    Ai/AiRequest.cs                        + Tools                             (Commit 1)
    Ai/AiMessage.cs                        + ToolCalls                         (Commit 1)
    Ai/AiClient.cs                         builds tools, parses tool_calls     (Commit 1);
                                            named failure on a prose reply     (Commit 2)
    Tools/CreateTaskTool.cs                new                                  (Commit 1)
    Telegram/MessageHandler.cs             branches on the result              (Commit 1);
                                            names the prose case                (Commit 2)
    ImplServiceCollectionExtensions.cs     + CreateTaskTool registration       (Commit 1)

tests/Assistant.IntegrationTests/
    Infrastructure/WireMockFixture.cs      + SeedAiToolCallAsync, wire payloads (Commit 1)
    Ai/AiClientTests.cs                    tool-call tests replace text tests  (Commit 1);
                                            + prose-is-refused test            (Commit 2)
    Telegram/TelegramListenerTests.cs      rename + reseed                     (Commit 1);
                                            + prose-reply test                  (Commit 2)

docs/
    design/2026-08-22-slice-1-feature-backlog.md
                                            F9b marked done, settled list      (Commit 3)
```

`tests/Assistant.WireMock/` is absent from this list — "Verified facts," above, explains why the
existing default mapping needs no change. `docs/design/slice-1-reminders.md` is absent too:
§5.2, §5.3, §5.4, §3.4 and §3.6 were all read (per the brief this plan was given) and none needs a
correction — they already describe the shape this slice builds toward, not a shape it
contradicts.

---

## Validation

**Test count arithmetic.** The unit suite stays at **41**, unchanged — this slice adds no unit
test file (see "What this slice does NOT include," above, on why `CreateTaskTool` gets no unit
test of its own). The integration suite starts at **32** (F9a-4's own count, confirmed
unchanged since — no integration test file has moved since that slice merged).

- `AiClientTests.cs`: 4 tests today. Commit 1 replaces the two success-path tests with three
  (`AskAsync_ProviderCallsATool_ReturnsItsNameAndArguments`,
  `AskAsync_ProviderCallsCreateTask_ArgumentsParseAsACreateTaskRequest`,
  `AskAsync_AnyText_PlacesThePromptTheModelAndTheToolOnTheWire`) and keeps the two failure-path
  tests unchanged — 5 after Commit 1. Commit 2 adds
  `AskAsync_ProviderRepliesWithProse_IsRefusedAsNoToolCall` — **6** after Commit 2.
- `TelegramListenerTests.cs`: 3 tests today. Commit 1 renames one, changes none of the other two's
  count — 3 after Commit 1. Commit 2 adds
  `Listener_ModelRepliesWithProse_TellsTheOwnerItWasNotReadAsATask` — **4** after Commit 2.

32 − 4 (old `AiClientTests` count) + 6 (new) − 3 (old `TelegramListenerTests` count) + 4 (new) =
**35** expected after this slice.

```bash
docker compose -f compose.test.yaml up -d
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

**This slice cannot be validated by running the app against real Telegram in any way that
demonstrates new behaviour to the owner.** `MessageHandler` will reply to a real message with
either the fixed "understood but cannot save yet" sentence or the fixed "did not read that as a
task" sentence — never with a saved task, because nothing saves one until F10. The same
acceptance F9a-3 recorded for itself ("nothing calls `IAiClient` until a later slice") applies
here in a smaller form: this slice's job is proving the tool call is parsed correctly against a
stub, not proving the product's capture path works end to end. That milestone is F10's, and the
backlog already names it one ("Milestone: the full loop").

---

## Steps

**Decisions this slice carries:** 1–4, given in full above.

**Consumes:** `IAiClient`/`AiClient` (F9a-3), `MessageHandler` (F9a-4), `WireMockFixture` (F9a-3).
**Produces:** `IAssistantTool`, `CreateTaskTool`, `ToolCall`, `CreateTaskRequest`, the tool-aware
`AiClient`, `ErrorCode.ModelReturnedNoToolCall`, the updated `MessageHandler`.

Three commits. **Do not merge them.** Commit 1 ships the happy path with one deliberate gap:
a prose-only reply crashes rather than failing gracefully, the same "leave a real gap, then close
it with a failing test" shape F9a-3 used for its own two commits. Commit 2 closes the gap.
Commit 3 records the backlog entry as done — a docs-only change, reviewed separately from
behaviour, matching F9a-4's own reasoning for splitting its docs commit out.

### Commit 1: the model can call a tool, and the assistant parses it out

**Files:**
- Create: `src/Assistant.Contracts/ToolCall.cs`
- Create: `src/Assistant.Contracts/CreateTaskRequest.cs`
- Create: `src/Assistant.Interfaces/IAssistantTool.cs`
- Modify: `src/Assistant.Interfaces/IAiClient.cs`
- Create: `src/Assistant.Impl/Ai/AiTool.cs`
- Create: `src/Assistant.Impl/Ai/AiFunctionDefinition.cs`
- Create: `src/Assistant.Impl/Ai/AiToolCall.cs`
- Create: `src/Assistant.Impl/Ai/AiFunctionCall.cs`
- Modify: `src/Assistant.Impl/Ai/AiRequest.cs`
- Modify: `src/Assistant.Impl/Ai/AiMessage.cs`
- Modify: `src/Assistant.Impl/Ai/AiClient.cs`
- Create: `src/Assistant.Impl/Tools/CreateTaskTool.cs`
- Modify: `src/Assistant.Impl/Telegram/MessageHandler.cs`
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`
- Modify: `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`
- Modify: `tests/Assistant.IntegrationTests/Ai/AiClientTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`

- [ ] **Step 1: Add the two `Contracts` types**

Create `src/Assistant.Contracts/ToolCall.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>
/// One tool invocation the model asked for, parsed out of its answer.
/// </summary>
/// <param name="Name">
/// The tool's name, matching an <c>IAssistantTool.Name</c> sent on the request.
/// </param>
/// <param name="ArgumentsJson">
/// The model's arguments, as the raw JSON object text the wire carried. Binding it to a
/// specific tool's own request shape, such as <see cref="CreateTaskRequest"/>, is the calling
/// tool's job, not the transport's.
/// </param>
public sealed record ToolCall(string Name, string ArgumentsJson);
```

Create `src/Assistant.Contracts/CreateTaskRequest.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Assistant.Contracts;

/// <summary>
/// The arguments the model supplies on a <c>create_task</c> tool call.
/// </summary>
/// <param name="Title">Short description of what needs doing.</param>
/// <param name="DueAtLocal">
/// An absolute local datetime as the model returns it: ISO-8601 with no offset, for example
/// <c>2026-09-01T10:00:00</c>. <see langword="null"/> when the user gave no time. Stays a
/// string rather than a parsed <see cref="DateTime"/>: resolving it against the configured zone
/// is <c>ILocalTimeResolver.Resolve</c>'s job, arriving at F10.
/// </param>
/// <remarks>
/// Property names carry explicit <see cref="JsonPropertyNameAttribute"/> values because nothing
/// deserialising a <see cref="ToolCall.ArgumentsJson"/> string applies a naming policy — unlike
/// <c>IAiApi</c>'s own traffic, which goes through Refit's configured snake-case serializer.
/// <c>WireMockFixture</c>'s own payload records use the identical pattern for the identical
/// reason.
/// </remarks>
public sealed record CreateTaskRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("due_at_local")] string? DueAtLocal);
```

- [ ] **Step 2: Add `IAssistantTool` and change `IAiClient`'s return type**

Create `src/Assistant.Interfaces/IAssistantTool.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>
/// One capability the chat model may invoke.
/// </summary>
/// <remarks>
/// Carries only what a tool definition needs on the wire request. No execution member exists
/// yet: <see cref="ITaskService"/> has no method to create a task until F10, so an
/// <c>ExecuteAsync</c> here would have nothing real to call. Adding one then is a deliberate
/// modification to this interface, not an extension by a new class.
/// </remarks>
public interface IAssistantTool
{
    /// <summary>
    /// The tool's name as sent on the wire request and echoed back on a tool call.
    /// </summary>
    /// <value>Lowercase snake case, for example <c>create_task</c>.</value>
    string Name { get; }

    /// <summary>
    /// What the tool does, written for the model rather than a developer.
    /// </summary>
    /// <value>A plain-language instruction telling the model when to call this tool.</value>
    string Description { get; }

    /// <summary>
    /// The JSON Schema object describing the tool's parameters.
    /// </summary>
    /// <value>Raw JSON text: a <c>type: object</c> schema with <c>properties</c> and <c>required</c>.</value>
    string ParametersJsonSchema { get; }
}
```

Replace `src/Assistant.Interfaces/IAiClient.cs` in full:

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// Reaches a chat model with the owner's text and returns the tool call it chose.
/// </summary>
/// <remarks>
/// A transport abstraction, not one of spec §3.6's behaviour seams: it has exactly one
/// production implementation, <c>AiClient</c>, and still does after this slice changes
/// <c>AskAsync</c>'s return type — a modification, not an extension. The seam this slice grows
/// is <see cref="IAssistantTool"/>, not this interface.
/// </remarks>
public interface IAiClient
{
    /// <summary>
    /// Sends the owner's text, the system prompt, and every registered tool definition to the
    /// configured model, and returns the tool call it chose.
    /// </summary>
    /// <param name="userText">What the owner said.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The tool call the model asked for, or the reason none came back.
    /// </returns>
    Task<Result<ToolCall>> AskAsync(string userText, CancellationToken ct);
}
```

- [ ] **Step 3: Update `MessageHandler` to compile against the new return type**

`MessageHandler` is the only other direct caller of `IAiClient.AskAsync` in `src/`. Updating it
now, before `AiClient` itself is touched, means the build check in Step 4 shows exactly one
error instead of two unrelated ones — `MessageHandler`'s own ternary
(`answer.IsSuccess ? answer.Value! : Unreachable`) does not compile once `Result<T>.Value` is a
`ToolCall?` instead of a `string?`, since a conditional expression needs both branches to share a
type. Replace `src/Assistant.Impl/Telegram/MessageHandler.cs` in full. This step gives every
failure the same generic reply — Decision 1's distinct sentence for a prose reply arrives in
Commit 2, once `ErrorCode.ModelReturnedNoToolCall` exists to branch on.

```csharp
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Sends the owner's plain-text message to the chat model and replies once it names a tool.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="notifier">Where the reply is delivered.</param>
/// <param name="scopeFactory">
/// Opens the scope <see cref="IAiClient"/> is resolved from, because this handler is a
/// singleton and a Refit client is a typed <see cref="System.Net.Http.HttpClient"/> --
/// capturing one directly would pin its message handler and defeat the factory's handler
/// rotation. <see cref="Assistant.Impl.Services.Jobs.DueReminderJob"/> already solves the
/// identical problem for <see cref="ITaskService"/>, in its own words: "Opens the scope
/// [the service] is resolved from, because this job is a singleton and the service depends on
/// the scoped database context."
/// </param>
/// <remarks>
/// The owner check lives inline here, on purpose: the assistant serves exactly one person, so
/// there is nothing to route between. Any future handler must apply the same check itself --
/// nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/> enforces it.
/// </remarks>
internal sealed class MessageHandler(
    TelegramSettings settings, INotifier notifier, IServiceScopeFactory scopeFactory)
    : ITelegramUpdateHandler
{
    private const string Unreachable =
        "I could not reach the model just now. Send that again in a moment.";

    private const string ToolCallNotActedOnYet =
        "Got it -- I understood that as a task, but I cannot save it yet.";

    /// <inheritdoc/>
    public UpdateType Handles => UpdateType.Message;

    /// <inheritdoc/>
    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (update.Message is not { Chat.Id: var chatId, Text: { } text } ||
            chatId != settings.OwnerChatId)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var ai = scope.ServiceProvider.GetRequiredService<IAiClient>();
        var result = await ai.AskAsync(text, ct);

        await notifier.SendAsync(result.IsSuccess ? ToolCallNotActedOnYet : Unreachable, ct);
    }
}
```

- [ ] **Step 4: Build and watch the expected compile error**

This build is scoped to `Assistant.Worker.csproj` rather than the whole solution: `Worker`
references every `src/` project (Models, Contracts, Interfaces, Repository, Impl), so this
catches every production-code error, but it does not pull in the test projects — `AiClientTests`
and `TelegramListenerTests` still reference the old `Result<string>` shape at this point in the
plan (Steps 12–13 fix them) and would otherwise report unrelated failures of their own.

```bash
dotnet build --no-restore src/Assistant.Worker/Assistant.Worker.csproj
```

Expected: fails to compile. `Assistant.Impl.Ai.AiClient` still implements the old
`Task<Result<string>> AskAsync(...)` signature, which no longer matches `IAiClient`'s member —
`CS0535` ("does not implement interface member"). This is the only error expected: `MessageHandler`,
the one other place in `src/` that called `IAiClient.AskAsync` directly, was already brought in
line with the new signature in Step 3.

- [ ] **Step 5: Add the four new wire types**

Create `src/Assistant.Impl/Ai/AiTool.cs`:

```csharp
namespace Assistant.Impl.Ai;

/// <summary>
/// One tool definition offered to the model on a request, in the OpenAI-compatible
/// function-calling shape.
/// </summary>
/// <param name="Type">Always <c>function</c> -- the only tool type this wire format defines.</param>
/// <param name="Function">The tool's name, description and parameter schema.</param>
internal sealed record AiTool(string Type, AiFunctionDefinition Function);
```

Create `src/Assistant.Impl/Ai/AiFunctionDefinition.cs`:

```csharp
using System.Text.Json.Nodes;

namespace Assistant.Impl.Ai;

/// <summary>
/// The name, description and parameter schema of one tool definition sent on a request.
/// </summary>
/// <param name="Name">The tool's name, echoed back on any call the model makes to it.</param>
/// <param name="Description">What the tool does, written for the model rather than a developer.</param>
/// <param name="Parameters">The JSON Schema object describing the tool's arguments.</param>
/// <remarks>
/// <see cref="Parameters"/> is a <see cref="JsonNode"/>, not a <see cref="System.Text.Json.JsonElement"/>
/// parsed from a <see cref="System.Text.Json.JsonDocument"/>: a <see cref="JsonNode"/> is fully
/// garbage-collected and carries no disposal lifetime to trip over, where a <c>JsonElement</c>
/// stops being readable the moment its parent <c>JsonDocument</c> is disposed.
/// <c>WireMockFixture</c> already builds its own seeded payloads from the same
/// <see cref="System.Text.Json.Nodes"/> types, for the same reason.
/// </remarks>
internal sealed record AiFunctionDefinition(string Name, string Description, JsonNode Parameters);
```

Create `src/Assistant.Impl/Ai/AiToolCall.cs`:

```csharp
namespace Assistant.Impl.Ai;

/// <summary>
/// One invocation the model asked for, carried on an assistant message's <c>tool_calls</c> array.
/// </summary>
/// <param name="Id">
/// The provider's identifier for this call. Unused today -- nothing sends a follow-up turn that
/// would need to echo it back -- and dropped rather than carried onto <c>ToolCall</c>, the
/// public shape <see cref="AiClient"/> returns.
/// </param>
/// <param name="Type">Always <c>function</c> -- the only tool call type this wire format defines.</param>
/// <param name="Function">The tool name and arguments the model chose.</param>
internal sealed record AiToolCall(string Id, string Type, AiFunctionCall Function);
```

Create `src/Assistant.Impl/Ai/AiFunctionCall.cs`:

```csharp
namespace Assistant.Impl.Ai;

/// <summary>
/// The tool name and arguments carried on one tool call.
/// </summary>
/// <param name="Name">The tool's name, matching an <see cref="AiFunctionDefinition"/> sent on the request.</param>
/// <param name="Arguments">
/// The model's arguments, as a JSON object serialised to a string rather than nested -- the
/// OpenAI-compatible wire format's own shape, not a choice this project made.
/// </param>
internal sealed record AiFunctionCall(string Name, string Arguments);
```

- [ ] **Step 6: Extend `AiRequest` and `AiMessage`**

Replace `src/Assistant.Impl/Ai/AiRequest.cs` in full:

```csharp
namespace Assistant.Impl.Ai;

/// <summary>
/// A request to the OpenAI-compatible chat API, which OpenRouter, OpenAI, Groq and a local
/// Ollama all serve.
/// </summary>
/// <param name="Model">The model slug to request, such as <c>anthropic/claude-sonnet-5</c>.</param>
/// <param name="Messages">The conversation so far, system prompt first.</param>
/// <param name="MaxTokens">The maximum number of tokens the model may return.</param>
/// <param name="Tools">Every tool definition offered to the model on this request.</param>
internal sealed record AiRequest(
    string Model, IReadOnlyList<AiMessage> Messages, int MaxTokens, IReadOnlyList<AiTool> Tools);
```

Replace `src/Assistant.Impl/Ai/AiMessage.cs` in full:

```csharp
namespace Assistant.Impl.Ai;

/// <summary>
/// One turn in the chat API's conversation, on either side of the wire.
/// </summary>
/// <param name="Role">
/// Who is speaking: <c>system</c>, <c>user</c>, or <c>assistant</c>.
/// </param>
/// <param name="Content">
/// What was said, or <see langword="null"/> on a response that carries only tool calls.
/// </param>
/// <param name="ToolCalls">
/// The tool calls the model asked for, or <see langword="null"/> on a request message, or on a
/// response that answered with <paramref name="Content"/> instead.
/// </param>
internal sealed record AiMessage(string Role, string? Content, IReadOnlyList<AiToolCall>? ToolCalls = null);
```

The default `= null` on `ToolCalls` keeps `AiClient`'s two existing call sites,
`new AiMessage("system", prompt.Build())` and `new AiMessage("user", userText)`, compiling
unchanged.

- [ ] **Step 7: Build and watch the compile errors move**

```bash
dotnet build --no-restore src/Assistant.Worker/Assistant.Worker.csproj
```

Expected: still fails, same scoped project. `AiClient.AskAsync` now fails on two fronts — its own
return type still reads `Task<Result<string>>` against `IAiClient`'s `Task<Result<ToolCall>>`
(`CS0535`, carried over from Step 4), and its `new AiRequest(settings.Model, [...], settings.MaxTokens)`
call is now missing the required fourth positional argument, `Tools` (`CS7036`). Both are the
predicted consequence of Steps 5 and 6 with `AiClient` not yet touched — `MessageHandler` stays
clean, already fixed in Step 3.

- [ ] **Step 8: Rewrite `AiClient`'s happy path — deliberately leaving one gap**

Replace `src/Assistant.Impl/Ai/AiClient.cs` in full. This step's `AskAsync` dereferences the
parsed tool call with `!` rather than guarding it — the same "leave a real gap, prove it crashes"
shape F9a-3 used for its own Commit 1. Commit 2, below, closes it.

```csharp
using System.Text.Json.Nodes;
using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;

namespace Assistant.Impl.Ai;

/// <summary>
/// Reaches the configured chat endpoint with the owner's text, the system prompt and every
/// registered tool definition, and returns the tool call the model chose.
/// </summary>
/// <param name="api">The Refit client for the chat endpoint.</param>
/// <param name="prompt">Builds the system prompt sent as the first message.</param>
/// <param name="settings">The configured model and token limit.</param>
/// <param name="tools">Every tool definition offered to the model on the request.</param>
/// <param name="logger">Where a provider failure or an empty answer is recorded.</param>
internal sealed class AiClient(
    IAiApi api, SystemPrompt prompt, AiSettings settings, IEnumerable<IAssistantTool> tools,
    ILogger<AiClient> logger) : IAiClient
{
    /// <inheritdoc/>
    public async Task<Result<ToolCall>> AskAsync(string userText, CancellationToken ct)
    {
        AiResponse response;
        try
        {
            response = await api.AskAsync(
                new AiRequest(
                    settings.Model,
                    [new AiMessage("system", prompt.Build()),
                     new AiMessage("user", userText)],
                    settings.MaxTokens,
                    tools.Select(ToWireTool).ToList()),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reaching the chat model failed.");
            return Result<ToolCall>.Failure(ErrorCode.ModelUnavailable);
        }

        var choice = response.Choices.FirstOrDefault();

        if (choice is null)
        {
            logger.LogError("The chat model returned no answer.");
            return Result<ToolCall>.Failure(ErrorCode.ModelReturnedNoAnswer);
        }

        var call = choice.Message.ToolCalls?.FirstOrDefault();

        return Result<ToolCall>.Success(new ToolCall(call!.Function.Name, call.Function.Arguments));
    }

    private static AiTool ToWireTool(IAssistantTool tool) =>
        new("function", new AiFunctionDefinition(
            tool.Name, tool.Description, JsonNode.Parse(tool.ParametersJsonSchema)!));
}
```

- [ ] **Step 9: Build and confirm it compiles**

```bash
dotnet build --no-restore src/Assistant.Worker/Assistant.Worker.csproj
```

Expected: builds clean, zero warnings. `call!` suppresses the nullable-dereference warning the
same way F9a-3 Commit 1's `response.Choices[0].Message.Content!` did — the compiler is satisfied;
the runtime gap (a null `call` dereferenced) is still there, on purpose, until Commit 2. This
succeeds even though `CreateTaskTool` (Step 10) does not exist yet: `AiClient`'s new
`IEnumerable<IAssistantTool> tools` constructor parameter only needs the interface to exist, not
a concrete implementation — DI registration is a runtime concern, not a compile-time one.

- [ ] **Step 10: Register `CreateTaskTool`, add its file**

Create `src/Assistant.Impl/Tools/CreateTaskTool.cs`:

```csharp
using Assistant.Interfaces;

namespace Assistant.Impl.Tools;

/// <summary>
/// Describes the <c>create_task</c> tool offered to the model on every chat request.
/// </summary>
/// <remarks>
/// Carries no behaviour: the model can call this tool and <c>AiClient</c> parses the call out of
/// the answer, but nothing dispatches to an implementation or writes a row until
/// <see cref="ITaskService"/> grows a create method at F10. <c>due_at_local</c> is the only
/// optional field the schema advertises today; <c>notes</c> and <c>priority</c> wait for the
/// features that give <c>ReminderTask</c> somewhere to put them.
/// </remarks>
internal sealed class CreateTaskTool : IAssistantTool
{
    /// <inheritdoc/>
    public string Name => "create_task";

    /// <inheritdoc/>
    public string Description =>
        "Create a task the user wants to be reminded about. Use this whenever the user mentions "
        + "something they need to do. Supply due_at_local whenever the user states or implies a "
        + "time.";

    /// <inheritdoc/>
    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "title": {
              "type": "string",
              "description": "Short description of what needs doing."
            },
            "due_at_local": {
              "type": "string",
              "description": "Absolute local datetime, ISO-8601 with no offset, e.g. 2026-08-31T10:00:00. Omit if the user gave no time."
            }
          },
          "required": ["title"]
        }
        """;
}
```

In `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`, add one `using`:

```csharp
using Assistant.Impl.Tools;
```

alongside the file's existing `using Assistant.Impl.Ai;` line, and add one line to
`AddAssistantAi`'s body, immediately after `services.AddSingleton<SystemPrompt>();`:

Before:

```csharp
        services.AddSingleton(settings);
        services.AddSingleton<SystemPrompt>();
        services.AddRefitGeneratedClient<IAiApi>(new RefitSettings
```

After:

```csharp
        services.AddSingleton(settings);
        services.AddSingleton<SystemPrompt>();
        services.AddScoped<IAssistantTool, CreateTaskTool>();
        services.AddRefitGeneratedClient<IAiApi>(new RefitSettings
```

- [ ] **Step 11: Extend `WireMockFixture` to seed and read back a tool call**

In `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`, insert a new method
immediately after the existing `SeedAiAnswerAsync` (leave that method untouched):

```csharp
    /// <summary>
    /// Makes the stub answer the next chat request with a call to the given tool.
    /// </summary>
    /// <param name="toolName">The tool the model calls.</param>
    /// <param name="argumentsJson">The arguments JSON, exactly as the wire carries it.</param>
    /// <returns>A task that completes once the mapping is installed.</returns>
    public Task SeedAiToolCallAsync(string toolName, string argumentsJson) =>
        PutMappingAsync(AiMapping, "/chat/completions", priority: 1,
            bodyPattern: null, statusCode: 200,
            responseBody: new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["message"] = new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = null,
                        ["tool_calls"] = new JsonArray(new JsonObject
                        {
                            ["id"] = "call_1",
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = toolName,
                                ["arguments"] = argumentsJson,
                            },
                        }),
                    },
                }),
            },
            delayMs: null);
```

Extend `AiRequestPayload` with a `Tools` property, and add the two new payload records
immediately after `AiMessagePayload` at the bottom of the file:

Before:

```csharp
public sealed record AiRequestPayload(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<AiMessagePayload> Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens)
{
```

After:

```csharp
public sealed record AiRequestPayload(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<AiMessagePayload> Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("tools")] IReadOnlyList<AiToolPayload> Tools)
{
```

Append, after the existing `AiMessagePayload` record at the end of the file:

```csharp
/// <summary>
/// One tool definition within a captured chat request.
/// </summary>
/// <param name="Type">Always <c>function</c> on the requests this project sends.</param>
/// <param name="Function">The tool's name and description.</param>
public sealed record AiToolPayload(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] AiFunctionPayload Function)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>Null when the tool carried exactly the two expected fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// The name and description within one captured tool definition.
/// </summary>
/// <param name="Name">The tool's name.</param>
/// <param name="Description">What the tool does.</param>
public sealed record AiFunctionPayload(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>Null when the function carried exactly the two expected fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
```

- [ ] **Step 12: Rewrite `AiClientTests`**

Replace `tests/Assistant.IntegrationTests/Ai/AiClientTests.cs` in full:

```csharp
using System.Text.Json;
using Assistant.Contracts;
using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.IntegrationTests.Ai;

/// <summary>
/// Test class for <see cref="IAiClient"/>.
/// </summary>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class AiClientTests(WireMockFixture wireMock) : IAsyncLifetime
{
    private const string Model = "test-model";
    private const int MaxTokens = 100;

    private ServiceProvider _provider = null!;

    private IAiClient _sut = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantServices();
        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
        services.AddAssistantAi(new AiSettings
        {
            ApiKey = "test-key", BaseUrl = wireMock.Url, Model = Model, MaxTokens = MaxTokens,
        });
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<IAiClient>();

        await wireMock.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When the provider calls a tool
    /// And the model is asked
    /// Then the tool's name and raw arguments come back as the result's value.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderCallsATool_ReturnsItsNameAndArguments()
    {
        // Arrange
        await wireMock.SeedAiToolCallAsync("create_task", """{"title":"call the bank"}""");

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("create_task", result.Value!.Name);
        Assert.Equal("""{"title":"call the bank"}""", result.Value.ArgumentsJson);
    }

    /// <summary>
    /// When the provider calls create_task with a title and a due time
    /// And the arguments are parsed as a CreateTaskRequest
    /// Then both fields come back exactly as the model sent them.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderCallsCreateTask_ArgumentsParseAsACreateTaskRequest()
    {
        // Arrange
        await wireMock.SeedAiToolCallAsync(
            "create_task", """{"title":"call the bank","due_at_local":"2026-09-01T10:00:00"}""");

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        var request = JsonSerializer.Deserialize<CreateTaskRequest>(result.Value!.ArgumentsJson);
        Assert.Equal("call the bank", request!.Title);
        Assert.Equal("2026-09-01T10:00:00", request.DueAtLocal);
    }

    /// <summary>
    /// When the model is asked
    /// Then the system prompt is sent as the first message with role system
    /// And the owner's text is sent as the second message with role user
    /// And the configured model, token limit and tool definition go on the wire.
    /// </summary>
    [Fact]
    public async Task AskAsync_AnyText_PlacesThePromptTheModelAndTheToolOnTheWire()
    {
        // Arrange
        await wireMock.SeedAiToolCallAsync("create_task", """{"title":"call the bank"}""");

        // Act
        await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        var request = Assert.Single(await wireMock.AiRequestsAsync());
        Assert.Equal(Model, request.Model);
        Assert.Equal(MaxTokens, request.MaxTokens);
        Assert.Equal(2, request.Messages.Count);
        Assert.Equal("system", request.Messages[0].Role);
        Assert.Equal("user", request.Messages[1].Role);
        Assert.Equal("call the bank tomorrow at 10", request.Messages[1].Content);
        var tool = Assert.Single(request.Tools);
        Assert.Equal("create_task", tool.Function.Name);
    }

    /// <summary>
    /// When the provider answers with a server error
    /// And the model is asked
    /// Then the call is refused as unavailable, not thrown.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderReturnsAServerError_IsRefusedAsUnavailable()
    {
        // Arrange
        await wireMock.SeedAiFailureAsync();

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ModelUnavailable, result.Error);
    }

    /// <summary>
    /// When the provider answers with no candidate messages
    /// And the model is asked
    /// Then the call is refused as having returned nothing.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderReturnsNoChoices_IsRefusedAsNoAnswer()
    {
        // Arrange
        await wireMock.SeedAiNoAnswerAsync();

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ModelReturnedNoAnswer, result.Error);
    }
}
```

- [ ] **Step 13: Update `TelegramListenerTests`**

Replace `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs` in full:

```csharp
using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Assistant.IntegrationTests.Telegram;

/// <summary>
/// Test class for the inbound listener registered via <c>AddAssistantListener</c>.
/// </summary>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class TelegramListenerTests(WireMockFixture wireMock) : IAsyncLifetime
{
    private const string BotToken = "123456:TESTTOKEN";
    private const long OwnerChatId = 100200300L;
    private const long StrangerChatId = 999888777L;

    private const string AcknowledgedButNotSavedYet =
        "Got it -- I understood that as a task, but I cannot save it yet.";

    private static readonly TimeSpan ReplyDeadline = TimeSpan.FromSeconds(10);

    private ServiceProvider _provider = null!;

    private IHostedService _sut = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantServices();
        services.AddAssistantTelegram(new TelegramSettings
        {
            BotToken = BotToken, OwnerChatId = OwnerChatId, BaseUrl = wireMock.Url,
        });
        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
        services.AddAssistantAi(new AiSettings
        {
            ApiKey = "test-key", BaseUrl = wireMock.Url, Model = "test-model", MaxTokens = 100,
        });
        services.AddAssistantListener();
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetServices<IHostedService>().Single();

        await wireMock.ResetAsync();
        await wireMock.SeedAiToolCallAsync("create_task", """{"title":"call the bank"}""");
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }

    /// <summary>
    /// When the owner sends a message
    /// And the model calls create_task
    /// Then the owner is told the task was understood but cannot be saved yet.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerSendsAMessage_RepliesThatItUnderstoodTheTask()
    {
        // Arrange
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal(AcknowledgedButNotSavedYet, sent[0].Text);
    }

    /// <summary>
    /// When someone other than the owner sends a message
    /// And the owner sends one in the same batch
    /// Then only the owner is answered.
    /// </summary>
    /// <remarks>
    /// The owner's message is a synchronisation device, not a second assertion. Proving
    /// that nothing was sent to the stranger otherwise means waiting on a clock and
    /// hoping; putting the stranger first in the batch means that by the time the owner's
    /// reply arrives, the stranger's message has already been processed and skipped.
    /// <para>
    /// This test does not check the reply's exact text: that check duplicated
    /// <see cref="Listener_OwnerSendsAMessage_RepliesThatItUnderstoodTheTask"/>, which spec
    /// §7.2 forbids. What this test alone proves is that the stranger's message produced no
    /// second reply.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Listener_StrangerSendsAMessage_OnlyTheOwnerIsAnswered()
    {
        // Arrange
        await wireMock.SeedUpdatesAsync(
            new InboundUpdate(10, StrangerChatId, "let me in"),
            new InboundUpdate(11, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Single(sent);
    }

    /// <summary>
    /// When a message has been answered
    /// And the listener keeps polling
    /// Then it is not answered again.
    /// </summary>
    /// <remarks>
    /// The only test in this suite that waits on wall-clock time, and it is worth the
    /// cost: a listener that fails to advance its offset is served the same update on
    /// every poll, and the stub answers an unadvanced poll with no delay at all.
    /// </remarks>
    [Fact]
    public async Task Listener_MessageAlreadyAnswered_DoesNotAnswerItAgain()
    {
        // Arrange
        var settle = TimeSpan.FromSeconds(3);
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        await Task.Delay(settle);

        // Assert
        Assert.Single(await wireMock.SentMessagesAsync());
    }
}
```

- [ ] **Step 14: Bring up the stub, run the touched suites, and watch them pass**

```bash
docker compose -f compose.test.yaml up -d
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~AiClientTests|FullyQualifiedName~TelegramListenerTests"
```

Expected: zero warnings; `AiClientTests` 5 passed; `TelegramListenerTests` 3 passed. Nothing here
seeds a prose-only reply, so Commit 1's `call!` gap is never exercised by any test yet — it stays
dormant until Commit 2 writes the test that reaches it.

- [ ] **Step 15: Run the whole suite**

```bash
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

Expected: unit tests unchanged at 41; integration tests at 32 − 4 − 3 (old counts for the two
files just rewritten, `AiClientTests` and `TelegramListenerTests`) + 5 + 3 (their new counts) =
**33**.

- [ ] **Step 16: Commit**

```bash
git add src/Assistant.Contracts/ToolCall.cs src/Assistant.Contracts/CreateTaskRequest.cs \
        src/Assistant.Interfaces/IAssistantTool.cs src/Assistant.Interfaces/IAiClient.cs \
        src/Assistant.Impl/Ai/AiTool.cs src/Assistant.Impl/Ai/AiFunctionDefinition.cs \
        src/Assistant.Impl/Ai/AiToolCall.cs src/Assistant.Impl/Ai/AiFunctionCall.cs \
        src/Assistant.Impl/Ai/AiRequest.cs src/Assistant.Impl/Ai/AiMessage.cs \
        src/Assistant.Impl/Ai/AiClient.cs src/Assistant.Impl/Tools/CreateTaskTool.cs \
        src/Assistant.Impl/Telegram/MessageHandler.cs \
        src/Assistant.Impl/ImplServiceCollectionExtensions.cs \
        tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs \
        tests/Assistant.IntegrationTests/Ai/AiClientTests.cs \
        tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs
git commit
```

Message:

```
feat: the model can call create_task, and the assistant parses it out

IAiClient.AskAsync returns Result<ToolCall> in place of Result<string>.
AiClient builds a tools array from every registered IAssistantTool and
parses tool_calls back out of the response; CreateTaskTool describes
create_task and executes nothing, because ITaskService has no create
method until F10. ToolCall and CreateTaskRequest land in Contracts,
carrying raw text rather than a bound value -- deciding which tool's
shape to bind against is the calling tool's job, not the transport's.

MessageHandler compiles against the new return type with one reply for
success and one for any failure; the next commit gives a model's prose
reply its own name instead of folding it into "could not reach the
model," and closes the one gap left here on purpose: AiClient
dereferences a parsed tool call with the null-forgiving operator, so a
prose-only answer crashes rather than failing gracefully, exactly the
gap the next commit's first failing test proves is real.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

### Commit 2: a prose reply is a named failure, not a crash

**Files:**
- Modify: `src/Assistant.Contracts/ErrorCode.cs`
- Modify: `src/Assistant.Impl/Ai/AiClient.cs`
- Modify: `src/Assistant.Impl/Telegram/MessageHandler.cs`
- Modify: `tests/Assistant.IntegrationTests/Ai/AiClientTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`

- [ ] **Step 1: Append the new error code first**

The test written in Step 2 references `ErrorCode.ModelReturnedNoToolCall` by name. Adding the
member before the test exists means the test fails at runtime, for the reason this commit is
about — not at compile time with `CS0117`, for an enum member the test outran.

At the **end** of the `ErrorCode` enum in `src/Assistant.Contracts/ErrorCode.cs`, after
`ModelReturnedNoAnswer`:

```csharp

    /// <summary>
    /// The chat model was reached and answered, but without calling any tool.
    /// </summary>
    ModelReturnedNoToolCall,
```

- [ ] **Step 2: Write the failing tests**

Append to `tests/Assistant.IntegrationTests/Ai/AiClientTests.cs`, inside the class, after
`AskAsync_ProviderReturnsNoChoices_IsRefusedAsNoAnswer`:

```csharp

    /// <summary>
    /// When the provider answers with prose instead of calling a tool
    /// And the model is asked
    /// Then the call is refused as having named no tool, not thrown.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderRepliesWithProse_IsRefusedAsNoToolCall()
    {
        // Arrange
        await wireMock.SeedAiAnswerAsync("Sure, I can help with that.");

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ModelReturnedNoToolCall, result.Error);
    }
```

Append to `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`, inside the class,
after `Listener_MessageAlreadyAnswered_DoesNotAnswerItAgain`, and add a second private constant
next to `AcknowledgedButNotSavedYet`:

```csharp
    private const string NotUnderstoodAsATask =
        "I did not read that as a task. Try rephrasing it.";
```

```csharp

    /// <summary>
    /// When the model replies with prose instead of calling a tool
    /// And the owner sent the message that produced it
    /// Then the owner is told the message was not read as a task.
    /// </summary>
    [Fact]
    public async Task Listener_ModelRepliesWithProse_TellsTheOwnerItWasNotReadAsATask()
    {
        // Arrange
        await wireMock.SeedAiAnswerAsync("Sure, tell me more.");
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "hello"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal(NotUnderstoodAsATask, sent[0].Text);
    }
```

- [ ] **Step 3: Run them and watch them fail for the right reason**

```bash
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~AiClientTests"
```

Expected: `AskAsync_ProviderRepliesWithProse_IsRefusedAsNoToolCall` fails with an **unhandled
`NullReferenceException`**, not a failed assertion — `call!.Function.Name` dereferences a `call`
that is genuinely null, because `SeedAiAnswerAsync`'s response carries `content` and no
`tool_calls`. The five pre-existing tests still pass unchanged.

```bash
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~TelegramListenerTests"
```

Expected: `Listener_ModelRepliesWithProse_TellsTheOwnerItWasNotReadAsATask` fails with a
**`TimeoutException`** after the 10-second `ReplyDeadline`, not a failed assertion or a crash
visible to the test runner — `TelegramListener.DispatchAsync` wraps every handler call in its own
try/catch (`src/Assistant.Impl/Telegram/TelegramListener.cs`, confirmed by reading it directly)
and logs the same `NullReferenceException` rather than letting it propagate, so no reply is ever
sent and `WaitForSentMessagesAsync` exhausts its deadline. This is the predicted failure for this
step, and it is slower than the `AiClientTests` failure above for exactly this reason — the
listener's own defence against one bad update taking down the whole poll loop, which this step is
incidentally proving still works, not a flaw in the test.

- [ ] **Step 4: Harden `AiClient`**

In `src/Assistant.Impl/Ai/AiClient.cs`, replace the method's last two statements:

Before:

```csharp
        var call = choice.Message.ToolCalls?.FirstOrDefault();

        return Result<ToolCall>.Success(new ToolCall(call!.Function.Name, call.Function.Arguments));
```

After:

```csharp
        var call = choice.Message.ToolCalls?.FirstOrDefault();

        if (call is null)
        {
            logger.LogError("The chat model replied without calling a tool.");
            return Result<ToolCall>.Failure(ErrorCode.ModelReturnedNoToolCall);
        }

        return Result<ToolCall>.Success(new ToolCall(call.Function.Name, call.Function.Arguments));
```

- [ ] **Step 5: Branch `MessageHandler` on the new code**

In `src/Assistant.Impl/Telegram/MessageHandler.cs`, add a new constant next to
`ToolCallNotActedOnYet`:

```csharp
    private const string NotUnderstoodAsATask =
        "I did not read that as a task. Try rephrasing it.";
```

Add `using Assistant.Contracts;` to the file's usings, alongside the existing ones, and replace
`HandleAsync`'s last two statements:

Before:

```csharp
        var result = await ai.AskAsync(text, ct);

        await notifier.SendAsync(result.IsSuccess ? ToolCallNotActedOnYet : Unreachable, ct);
```

After:

```csharp
        var result = await ai.AskAsync(text, ct);

        var reply = result switch
        {
            { IsSuccess: true } => ToolCallNotActedOnYet,
            { Error: ErrorCode.ModelReturnedNoToolCall } => NotUnderstoodAsATask,
            _ => Unreachable,
        };

        await notifier.SendAsync(reply, ct);
```

- [ ] **Step 6: Run them and watch them pass**

```bash
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~AiClientTests|FullyQualifiedName~TelegramListenerTests"
```

Expected: `AiClientTests` 6 passed; `TelegramListenerTests` 4 passed.

- [ ] **Step 7: Run the whole suite**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

Expected: zero warnings; unit tests unchanged at 41 (`ConventionTests` inspects `ErrorCode` by
reflection and needs no change of its own, the same way every prior `ErrorCode` addition needed
none); integration tests at **35** (see "Test count arithmetic," above).

- [ ] **Step 8: Commit**

```bash
git add src/Assistant.Contracts/ErrorCode.cs src/Assistant.Impl/Ai/AiClient.cs \
        src/Assistant.Impl/Telegram/MessageHandler.cs \
        tests/Assistant.IntegrationTests/Ai/AiClientTests.cs \
        tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs
git commit
```

Message:

```
feat: a prose reply is a named failure, not a crash

AiClient now checks the parsed tool call for null and returns
Result<ToolCall>.Failure(ErrorCode.ModelReturnedNoToolCall) instead of
letting a NullReferenceException propagate -- the gap the previous
commit left on purpose. MessageHandler tells the owner their message
was not read as a task, distinct from the existing "could not reach
the model" sentence used for a transport failure or an empty answer.

The AiClientTests failure was immediate; the TelegramListenerTests one
only surfaced after a 10-second timeout, because TelegramListener
already catches and logs a handler's exception per update rather than
letting one bad message take down the poll loop -- itself proof that
defence works, not a flaw in the test.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

### Commit 3: record F9b as done

**Files:**
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`

- [ ] **Step 1: Mark F9b done and record what it settled**

Replace the F9b entry in full:

Before:

```markdown
**F9b · Parse a tool call out of the answer** — spec §5.2, §5.3, §12.3
`IAssistantTool`, `CreateTaskTool` as its first implementation, tool definitions added to the
chat request, `tool_calls` parsed out of the response, `CreateTaskRequest` in `Contracts`, and
`IAiClient.AskAsync` changed to return `Result<ToolCall>` in place of `Result<string>`.
*Tests:* free text produces the expected tool call against a WireMock'd provider.
```

After:

```markdown
**F9b · Parse a tool call out of the answer** — spec §5.2, §5.3, §12.3 · **done**
`IAssistantTool`, `CreateTaskTool` as its first implementation, tool definitions added to the
chat request, `tool_calls` parsed out of the response, `CreateTaskRequest` in `Contracts`, and
`IAiClient.AskAsync` changed to return `Result<ToolCall>` in place of `Result<string>`.
*Tests:* free text produces the expected tool call against a WireMock'd provider.
*Settled at F9b:*
- **`IAssistantTool` carries no execution member.** `Name`, `Description` and
  `ParametersJsonSchema` are enough to put a tool definition on the wire request; `ExecuteAsync`
  arrives at F10 as a deliberate modification to this interface, once `ITaskService` has a create
  method for it to call. Considered and rejected: defining it now against nothing, the shape an
  earlier, abandoned full-slice draft took.
- **`ToolCall` and `CreateTaskRequest` both live in `Contracts`**, next to `ErrorCode` and
  `Result<T>` — the first two request/response-style types that project has shipped. `ToolCall`
  carries the model's arguments as a raw JSON string, not a bound value: `AiClient` knows a tool
  was named, not which typed shape to bind its arguments against, so binding stays the calling
  tool's job, arriving with dispatch at F10.
- **`CreateTaskRequest.DueAtLocal` stays a string.** Resolving it against the configured zone is
  `ILocalTimeResolver.Resolve`'s job, called from F10's `CreateTaskTool.ExecuteAsync` once that
  exists — not this slice's, and not a job a parsed `DateTime` could do without a zone to resolve
  against yet. `Notes` and `Priority` are absent from the schema entirely, deferred to F10 and
  F12, the features that give `ReminderTask` somewhere to put them.
- **A model's prose reply is a named failure**, `ErrorCode.ModelReturnedNoToolCall`, appended
  after `ModelReturnedNoAnswer` — distinct from a transport failure or an empty response, both
  already named at F9a. `MessageHandler` gives it its own sentence, telling the owner their
  message was not read as a task rather than folding it into "could not reach the model."
- **Shipped in two commits, in one PR, on purpose**, mirroring F9a-3's own shape: the happy path
  first, with the parsed tool call dereferenced by the null-forgiving operator rather than
  guarded, so a prose-only reply crashed for real before `ModelReturnedNoToolCall` gave that
  failure a name. The crash surfaced two ways — an immediate `NullReferenceException` at the
  `AiClientTests` level, and a `TimeoutException` at the `TelegramListenerTests` level, because
  `TelegramListener` already catches and logs a handler's exception per update rather than
  propagating it.
- **`IAiApi`'s tool-definition JSON Schema travels as a `System.Text.Json.Nodes.JsonNode`, not a
  `JsonElement`.** A `JsonNode` carries no disposal lifetime to trip over; a `JsonElement` parsed
  from a `JsonDocument` stops being readable once that document is disposed.
  `WireMockFixture`'s own seeded payloads already use the same `JsonNode` family.
- **No new NuGet package.** The wire and tool-definition types this slice adds are all `System.Text.Json`.
```

- [ ] **Step 2: Run the whole suite once more, then commit**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

Expected: unchanged from Commit 2 — 41 unit, 35 integration, zero warnings. This commit touches
no source or test file, so nothing here should move.

```bash
git add docs/design/2026-08-22-slice-1-feature-backlog.md
git commit
```

Message:

```
docs: record what F9b settled

F9b's own backlog entry gains a done tag and the settled list every
closed feature in this backlog carries -- where IAssistantTool and
ToolCall ended up, why DueAtLocal stayed a string, and the two-commit
shape that proved the prose-reply gap was real before naming it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

---

## Self-review

**Commit 1 (feature):**
- [ ] `IAiClient.AskAsync` and `AiClient.AskAsync` both read `Task<Result<ToolCall>>` — grep
      confirms no lingering `Result<string>` reference in either file
- [ ] `AiClient`'s new `tools` parameter is read inside the same commit that declares it — no
      CS9113 risk
- [ ] `AiRequest.Tools` and `AiMessage.ToolCalls` both compile against every existing call site
      without a cascading change — `ToolCalls`'s default `null` is why `AiMessage`'s two call
      sites in `AiClient` need no edit
- [ ] `CreateTaskTool` declares no constructor at all — nothing to primary-construct
- [ ] `CreateTaskRequest` and `ToolCall`'s wire-facing properties carry explicit
      `[JsonPropertyName]` where they are deserialised outside `IAiApi`'s configured pipeline
- [ ] `docker compose down`, never `down -v`; no `--build` used anywhere in this slice

**Commit 2 (the named failure):**
- [ ] Both new tests were watched failing for a genuine unhandled exception or timeout (Step 3)
      before this commit's hardening step (Step 4) ran, not a failed assertion
- [ ] `ErrorCode.ModelReturnedNoToolCall` is appended at the end of the enum; no existing member's
      implicit value moved
- [ ] `MessageHandler`'s switch expression names `ErrorCode.ModelReturnedNoToolCall` explicitly,
      falling through to `Unreachable` for every other failure

**Commit 3 (docs):**
- [ ] F9b's backlog entry carries `**done**` and a `*Settled at F9b:*` list in the same style as
      F8's and F9a's own entries
- [ ] Build and both test suites still green after this commit, unchanged from Commit 2's numbers

**Whole feature, once all three commits land:**
- [ ] Every new public member has a three-line `<summary>`; every test summary is Gherkin
- [ ] Every class taking arguments uses a primary constructor
- [ ] No emoji anywhere, including all three commit messages
- [ ] **No plan-internal decision citation (`Decision 1`, `(decision 2)`, or similar) inside any
      C# code block, doc comment, or commit message** — every fenced code block in this document
      was re-read for this before the plan was committed; prose sections cite decisions by number
      freely, code blocks never do
- [ ] Type names are consistent across every step that mentions them: `AiTool`,
      `AiFunctionDefinition`, `AiToolCall`, `AiFunctionCall`, `IAssistantTool`, `CreateTaskTool`,
      `ToolCall`, `CreateTaskRequest` each appear with the identical spelling everywhere they are
      used, including the backlog's settled-list prose
- [ ] No placeholder text anywhere in this document — no "TBD," no "similar to the step above,"
      no elided error handling
- [ ] Spec coverage: §5.2 (system prompt, untouched), §5.3 (`create_task`'s parameters, partially
      shipped, the rest named as deferred), §5.4 (`due_at_local`'s string shape and the F10 seam),
      §3.3/§3.4/§3.6 (all confirmed already accurate, none edited), §12.1/§12.5/§12.6 (docs,
      primary constructors, no emoji, all followed) — every section this plan's brief named is
      addressed somewhere above
- [ ] Each commit's diff stays well inside the 650-line estimate Decision 4 gives for the whole
      slice, comfortably under the 1000-line budget
