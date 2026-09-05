# F10-2 — the tool executes

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F10-1 gave `ITaskService` a place to write a task to, but nothing above it could reach
that method: `CreateTaskTool` was schema-only (F9b, 43 lines, verified below), and `IAssistantTool`
had no execution member at all. This slice is the middle third of F10's own three-slice split
(`docs/plans/2026-09-05-f10-1-the-writer.md`, "How F10 is sliced") and builds F10's whole
request-binding layer: `IAssistantTool.ExecuteAsync` — a deliberate modification to an existing
interface, exactly as F9b's own Decision 2 and F10-1's Decision 3 both anticipated and the owner
then settled as Ruling A — and `CreateTaskTool` gaining a primary constructor over `ITaskService`
and `ILocalTimeResolver` to parse the model's raw arguments, validate them, resolve any due time,
and call the writer, handing back the persisted `ReminderTask` itself. Nothing in this slice
touches Telegram or `MessageHandler`: the `ToolCallNotActedOnYet` placeholder F9b shipped is still
what the owner sees on a real phone after this slice merges. That is F10-3's job.

**Tech Stack:** `net10.0`, nullable enabled, warnings are errors — the existing stack. This slice
adds **one test-only NuGet package** (`Microsoft.Extensions.TimeProvider.Testing`, to
`Assistant.IntegrationTests` — Decision 8, below) and **no database migration**: every column this
slice's calls end up writing (`Title`, `Status`, `DueAt`, `CreatedAt`, `UpdatedAt`) was already
written by F10-1's `CreateAsync`; this slice only decides what gets passed into it.

**Spec:** `docs/design/slice-1-reminders.md` §5.1 (capture path flow: `IAssistantTool ->
ITaskService -> repository -> Postgres`), §5.4 (the time contract — resolution and its guard
clauses happen before anything is persisted), §7.2 (unit vs. integration split — a pure function
with no side effect stays a unit test; anything with an observable side effect needs a real
container), §7.7 (test-driven: failing test, watch it fail, then implement), §12.1 (XML docs),
§12.5 (primary constructors), §12.6 (no emoji).

**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F10's own entry and the "Settled
at F9b" list above it, which named `ExecuteAsync` arriving at F10 as a deliberate interface
modification and `CreateTaskRequest.DueAtLocal` staying a string "resolved at F10" — this is the
slice that resolves it, not F10-1, which took `dueAtUtc` already resolved as a parameter and never
read the string at all (F10-1 Decision 2).

---

## How this slice fits F10

F10-1 measured its own diff at 120 lines and named F10-2's own estimate "roughly 225-275 lines,"
reasoned from `IAssistantTool.cs` gaining one member (~20 lines), `CreateTaskTool.cs` growing a
primary constructor and an `ExecuteAsync` body (~90 lines), and a new integration test file (~140
lines). That estimate did not, and could not, anticipate two things the owner decided only once
this slice was actually being drafted: **Ruling 3** (`ILocalTimeResolver.Resolve` changes from
`Resolve(DateTime)` to `Resolve(string)`, so the resolver parses the model's raw text itself rather
than trusting a caller to have already parsed it), and **Rulings 1 and 2** together (a missing title
is a validation failure, surfaced through three new `ErrorCode` members rather than one). Both are
argued in full below (Decisions 1-3). Neither was foreseeable from F10-1's own text — F10-1's
Decision 2 explicitly scoped local-time *resolution* to this slice's tool, but said nothing about
*parsing*, because at the time `ILocalTimeResolver.Resolve` already took a `DateTime`, an
already-parsed value, and nothing in F10-1's plan proposed changing that.

**The measured total, and why it is said plainly to be low rather than smoothed over.** Summed
across the eight tracked files this slice touches (`git diff --numstat`, verified below): 180 lines
inserted, 38 deleted, net +142. The new, untracked test file,
`tests/Assistant.IntegrationTests/Tools/CreateTaskToolTests.cs`, is 203 lines on its own. Using raw
insertions plus the new file's full length — the fairer comparison against an *estimate* of "how
much new material has to be written," since several of this slice's insertions replace deleted
lines rather than purely growing a file, unlike every file F10-1 touched — gives **180 + 203 = 383
lines**, comfortably higher than F10-1's 225-275 estimate. **The estimate was low, and the reason is
exactly the one named above:** it did not anticipate Ruling 3's changes to `ILocalTimeResolver.cs`
(+8 net), `LocalTimeResolver.cs` (+8 net), and `LocalTimeResolverTests.cs` (+28 net, one new
five-case `[Theory]`), nor Rulings 1-2's three `ErrorCode` members (+17) and the tests that exercise
them (folded into the 203-line new file). Even so, 383 is far under the 1000-line PR budget, and the
running total across F10 so far — F10-1's 120 plus this slice's 383 — is 503, still comfortably
below budget with F10-3's own 200-250 estimate still to come.

**Why this is still one slice, not two.** Ruling 3's changes to `ILocalTimeResolver` could in
principle have been a fourth, separate PR ahead of this one. It is kept in this slice instead
because `CreateTaskTool.ExecuteAsync` is `Resolve`'s first real caller (Decision 7, below) — the
signature change and the code that finally exercises it were decided and drafted together, and
splitting them would produce a first commit that changes a well-tested interface for a caller that
does not exist yet, then a second commit that adds the caller, for no reviewability benefit: nobody
can evaluate whether `Resolve(string)`'s new parsing rules are right without also seeing the one
place that calls it with real, untrusted model text.

---

## Global Constraints

Every constraint the project's prior plans carry forward still applies here:

- `net10.0`; nullable enabled; warnings are errors; `CS1591`/`CS1573` are errors everywhere —
  confirmed by this plan's own `dotnet build --no-restore`, run against the actual working tree:
  zero warnings, zero errors, across the whole solution.
- **Every class taking arguments uses a primary constructor.** `CreateTaskTool` gains one this
  slice: `CreateTaskTool(ITaskService taskService, ILocalTimeResolver clock)`. `LocalTimeResolver`'s
  own primary constructor, `LocalTimeResolver(TimeZoneInfo zone, TimeProvider timeProvider)`, is
  unchanged — Decision 3, below, touches only the body of `Resolve`.
- Every enum's first member is `Unknown`, with no explicit numeric values, new members appended
  never inserted. **This slice appends three `ErrorCode` members** — `ToolArgumentsMalformed`,
  `ToolArgumentMissing`, `DueTimeUnparseable` — all three after the existing last member,
  `TaskAlreadyCompleted`, confirmed by reading the diff: every added line is inside the closing
  `}` of the enum, nothing is inserted between existing members, and `Unknown` remains first.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first, matched in both new test
  files. No Shouldly, no FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line — matched in both `CreateTaskToolTests.cs` and the
  appended `LocalTimeResolverTests.cs` theory.
- Central package management; no inline `Version=` (NU1008). This slice's one new package
  reference, `Microsoft.Extensions.TimeProvider.Testing` in
  `Assistant.IntegrationTests.csproj`, carries no `Version` attribute — the version, `10.9.0`, is
  pinned once in `Directory.Packages.props:20` and shared with `Assistant.UnitTests`, which already
  referenced the same package.
- No emoji anywhere: source, tests, docs, or commit messages.
- **Never run `docker compose down -v`.** Use `docker compose -f compose.test.yaml down` with no
  flags. This slice needs no such teardown-and-rebuild step — no schema change, no container image
  change.
- Integration tests need `docker compose -f compose.test.yaml up -d` first — **no `--build`**: this
  slice does not touch `tests/Assistant.WireMock/`.
- PR budget: 1000 changed lines per PR, excluding the plan. This slice measures at 383 lines by the
  convention above (see "How this slice fits F10") — comfortably under budget.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
  ```

---

## Verified facts this plan rests on

Every one of these was read from the actual working tree directly, or produced by running the
stated command myself — not recollection, and not carried over unverified from F10-1's own
"verified facts."

- **`git diff --stat` against `main` (0d430b8, F10-1's own merge commit) shows 8 modified files, 180
  insertions, 38 deletions.** Per-file breakdown (`git diff --numstat`):

  | File | + | - |
  | :--- | ---: | ---: |
  | `src/Assistant.Contracts/ErrorCode.cs` | 17 | 0 |
  | `src/Assistant.Impl/Services/LocalTimeResolver.cs` | 11 | 3 |
  | `src/Assistant.Impl/Tools/CreateTaskTool.cs` | 54 | 7 |
  | `src/Assistant.Interfaces/IAssistantTool.cs` | 29 | 4 |
  | `src/Assistant.Interfaces/ILocalTimeResolver.cs` | 19 | 11 |
  | `tests/Assistant.IntegrationTests/Ai/AiClientTests.cs` | 9 | 1 |
  | `tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj` | 1 | 0 |
  | `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs` | 40 | 12 |
  | **Total** | **180** | **38** |

  `tests/Assistant.IntegrationTests/Tools/CreateTaskToolTests.cs` is untracked and new, measured
  directly with `wc -l` at **203 lines**.
- **The build is clean.** `dotnet build --no-restore` across the whole solution: `Build succeeded.
  0 Warning(s). 0 Error(s).`
- **Test counts, run directly, not assumed:** `dotnet test tests/Assistant.UnitTests --no-build` —
  **56 passed**, 0 failed. `dotnet test tests/Assistant.IntegrationTests --no-build` — **61
  passed**, 0 failed, against the real `compose.test.yaml` containers. F10-1 left the suite at 51
  unit / 51 integration (its own "Verified facts"); this slice's own delta is +5 unit / +10
  integration, traced exactly in "Validation," below.
- **`ImplServiceCollectionExtensions.cs` registers exactly the three lines this slice's Decision 5
  depends on, confirmed by direct read:** line 53, `services.AddScoped<ITaskService,
  TaskService>();` inside `AddAssistantServices`; line 108,
  `services.AddSingleton<ILocalTimeResolver, LocalTimeResolver>();` inside `AddAssistantTime`; line
  132, `services.AddScoped<IAssistantTool, CreateTaskTool>();` inside `AddAssistantAi`. None of the
  three lines, nor any other line in this file, is touched by this slice's diff — the file does not
  appear in `git diff --stat` at all.
- **`src/Assistant.Impl/Ai/AiClient.cs` takes `IEnumerable<IAssistantTool> tools` as a primary
  constructor parameter (line 19) and calls `tools.Select(ToWireTool).ToList()` (inside `AskAsync`)
  to build the wire request's tool list — on every call, whether or not a tool is ever invoked.**
  This is the root of Decision 6's regression: describing a tool on the wire requires DI to
  construct it, in full, with every dependency it now carries.
- **`ILocalTimeResolver.Resolve` had exactly nine call sites in the whole repository before this
  slice**, verified with `git grep -n '\.Resolve(' 0d430b8 -- src tests`: all nine inside
  `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`, and all nine routed through a
  private helper, `private static DateTime Wall(string local) => DateTime.Parse(local,
  CultureInfo.InvariantCulture);`, at the bottom of that same file. **`Wall` did not survive this
  slice** — the diff deletes it outright, and all nine call sites now read `resolver.Resolve(local)`
  directly, passing the raw string, since `Resolve` itself parses now (Decision 3). No other file in
  `src/` or `tests/` called `Resolve` at all before this slice — confirmed by the same grep, and
  consistent with F8's own plan, which shipped the guard clauses with no production caller.
- **`Directory.Packages.props:20` already pins `Microsoft.Extensions.TimeProvider.Testing` at
  `10.9.0`**, consumed today by `Assistant.UnitTests` (`LocalTimeResolverTests.cs`'s own
  `FakeTimeProvider`, unchanged by this slice beyond the call-site signature update). This slice
  adds the same, already-pinned package to `Assistant.IntegrationTests.csproj` with no `Version`
  attribute — Decision 8 argues why that project now needs it.
- **`tests/Assistant.IntegrationTests/Infrastructure/PostgresFixture.cs`'s `CreateProvider()` calls
  only `AddAssistantRepository` and `AddAssistantServices`** — not `AddAssistantTime`, not
  `AddAssistantAi` — confirmed by reading the file in full. This is why
  `CreateTaskToolTests.InitializeAsync` builds its own `ServiceCollection` rather than calling
  `postgres.CreateProvider()`: it needs `AddAssistantTime` (for `ILocalTimeResolver`) and a direct
  `services.AddScoped<IAssistantTool, CreateTaskTool>();` registration — the identical line
  `AddAssistantAi` uses at `ImplServiceCollectionExtensions.cs:132`, duplicated here rather than
  pulling in `AddAssistantAi`'s whole Refit `IAiApi` client stack (an API key and base URL this
  suite has no reason to fake) for a suite that never reaches the model at all.
- **Test-count deltas, traced to their exact source, not just totaled:** unit tests grew by exactly
  5 — one new `[Theory]`, `Resolve_TextDoesNotMatchTheExpectedShape_IsRefused`, with 5
  `[InlineData]` cases, appended to `LocalTimeResolverTests.cs`; every other test in that file is
  unchanged except its call site (`Resolve(Wall(x))` -> `Resolve(x)`). Integration tests grew by
  exactly 10 — all from the new `CreateTaskToolTests.cs` (5 `[Fact]` methods + a 3-case `[Theory]` +
  a 2-case `[Theory]`); `AiClientTests.cs`'s own diff touches only its constructor and
  `InitializeAsync` — it has exactly 6 `[Fact]` methods before and after this slice, none added or
  removed.
- **`ITaskService.CreateAsync` and `ReminderTaskMappingExtensions.ToModel` (F10-1) are untouched by
  this slice** — neither `src/Assistant.Interfaces/ITaskService.cs` nor
  `src/Assistant.Impl/Services/TaskService.cs` nor
  `src/Assistant.Impl/Mapping/ReminderTaskMappingExtensions.cs` appears anywhere in this slice's
  diff. `CreateTaskTool.ExecuteAsync` calls `CreateAsync` exactly as F10-1 shaped it:
  `await taskService.CreateAsync(request, dueAtUtc, ct)`.

---

## Inherited context: what this slice reads from earlier features

`ILocalTimeResolver`/`LocalTimeResolver` (F8) are read in full and modified in place — the only
pre-existing modification this slice makes to an earlier feature's own interface, argued in
Decision 7. `CreateTaskRequest` (F9b) is consumed as-is, its two `[JsonPropertyName]`-attributed
properties (`Title`, `DueAtLocal`) bound directly by `JsonSerializer.Deserialize<CreateTaskRequest>`
inside `ExecuteAsync` — no change to the record itself. `IAssistantTool`'s `Name`, `Description`,
`ParametersJsonSchema` (F9b) are untouched; only `ExecuteAsync` is added, exactly as F9b's own
remarks and F10-1's Decision 3 both said would happen once `ITaskService` had a create method.
`ITaskService.CreateAsync` and `ReminderTaskMappingExtensions.ToModel` (F10-1) are consumed as a
single call, never modified. `AiClientTests` (F9a-3/F9b) is extended in place — a second
constructor parameter and two new lines in `InitializeAsync` — not replaced or restructured.
`TaskServiceTests`'s own `AsOf` convention (F5a, carried through F10-1) is reused verbatim in
`CreateTaskToolTests`.

---

## Decisions

### 1. A missing title is a validation failure, and it is enforced in the tool, not the service

**Decision, as the owner ruled it:** absent, empty, or whitespace-only `title` is refused before
`ITaskService.CreateAsync` is ever called:

```csharp
if (string.IsNullOrWhiteSpace(request.Title))
{
    return Result<ReminderTask>.Failure(ErrorCode.ToolArgumentMissing);
}
```

**Why the tool, not the service.** This is the same reasoning F10-1's Decision 2 already applied to
local-time resolution: `CreateTaskTool` is the layer translating between the model's world (raw,
untrusted JSON) and the domain's (a bound, valid request), so a well-formedness check belongs there,
not inside `TaskService.CreateAsync`, which F10-1's own Decision 1 built specifically to never fail
— reopening that invariant here would force a second signature change to `CreateAsync` for a
problem that has nothing to do with persistence. It is also the only place this check *can* live
cheaply: `JsonSerializer.Deserialize<CreateTaskRequest>` happily produces a non-null `Title` that is
`""` for a payload like `{"title":""}` — there is no way to make an empty string fail deserialization
itself without a custom converter, so validating immediately after binding, in the same method that
already handles the "didn't parse at all" case, is the natural single seam.

**Why one `ErrorCode` covers all three shapes (absent, empty, whitespace) rather than three.** This
is the mirror image of the argument Decision 2 makes in the other direction: absent, empty, and
whitespace-only title are, from the model's and the user's perspective, the identical situation —
"you didn't give me a real title" — and F10-3's reply is overwhelmingly likely to render the
identical sentence for all three, the same way Decision 8 (F10-1) drafted one shared sentence for
both due-time guard failures. Splitting these three into separate codes would produce three codes
with exactly one shared answer between them, the opposite of what Decision 2 argues three separate
codes should buy.

**Alternative considered and rejected: default an absent or blank title to a placeholder** (e.g.
`"Untitled task"`) rather than refusing. Rejected: the entire point of validating a tool call before
calling the writer (spec §5.4's "before anything is persisted," already established for due times
by F10-1 Decision 5) is refusing to persist something the user never actually asked for. A
placeholder title would silently create a real, completable task with a name nobody chose and no
signal to the user that anything went wrong — worse than the due-time guard's own failure mode,
which at least stops before writing anything at all. Consistency between the title guard and the
due-time guards (Decision 3, below) matters more than saving one round trip on a title the model
should not have omitted in the first place, since `title` is the tool's one `required` field per its
own `ParametersJsonSchema`.

### 2. Three new `ErrorCode` members, not one or two — the actual argument, not just the count

The implementation appends `ToolArgumentsMalformed`, `ToolArgumentMissing`, `DueTimeUnparseable` to
`ErrorCode` (`src/Assistant.Contracts/ErrorCode.cs`). The test this plan applies, per this task's own
framing: **would F10-3 say something genuinely different to the owner for each code?**

- **`ToolArgumentsMalformed`** — the model's entire arguments payload did not parse as JSON at all,
  or parsed but not as a usable object (`null`, a bare string, an array). This is a wire-level
  failure between the model and this assistant: the *user* did not type malformed JSON, the model
  did, so there is nothing the user said that they could usefully restate. A plausible F10-3
  rendering is not even "what did you mean?" — it might be closer to a generic "something went
  wrong, try again" or a message that never reaches the user's specific words at all.
- **`ToolArgumentMissing`** — the JSON parsed fine, but a field the tool requires (`title`) was
  absent or blank. Recoverable in the user's very next message, since the capture path has no
  multi-turn state to resume (F10-1's Decision 5 already relies on this same fact for the due-time
  codes). A plausible sentence: "What would you like me to call this?" — genuinely different from
  `ToolArgumentsMalformed`'s "something broke" framing.
- **`DueTimeUnparseable`** — the JSON parsed, `title` was present, but `due_at_local` did not match
  the exact wall-clock shape the schema asks for (garbage text, an embedded offset, a trailing `Z`).
  This sits functionally beside the two due-time codes F8 already shipped
  (`DueTimeInPast`/`DueTimeTooFarAhead`): all three exist to tell the user their stated time did not
  take. F10-1's own Decision 8 (Ruling E) has already drafted sentences for the other two — `"That
  time has already passed. What time did you mean?"` and `"That is more than two years away, which
  is probably not what you meant. What time did you mean?"` — and `DueTimeUnparseable` is a strong
  candidate for either reusing one of those verbatim or a third, barely-distinguishable variant
  ending in the identical `"What time did you mean?"`.

**Applying the test honestly, not just running through the motions:**
`ToolArgumentsMalformed` vs. `ToolArgumentMissing` clears the bar — these are different failure
classes at different layers (the whole payload vs. one field within it), and it is easy to imagine
F10-3 handling the first by not showing the user a "what did you mean" question at all, since there
is no coherent thing to ask them to clarify. That split earns its keep.

`DueTimeUnparseable` sitting apart from `DueTimeInPast`/`DueTimeTooFarAhead` is weaker. All three are
already, structurally, "the due time you gave didn't take, please restate it," and F10-1's own
Decision 8 table shows the other two already rendering as two sentences that differ only in their
middle clause. A single `DueTimeInvalid` (or reusing `DueTimeInPast`, since `Resolve` never reaches
its in-the-past/too-far-ahead comparisons without parsing first, so the codes are never ambiguous at
the call site regardless) could plausibly have served F10-3 identically, with one sentence
templated by cause instead of three near-identical codes each carrying their own sentence.

**My own conclusion, since the brief asks for it directly:** the three-way split is not wrong, but
it is uneven — `ToolArgumentsMalformed`/`ToolArgumentMissing` is a clean, well-motivated split;
`DueTimeUnparseable` as a peer of `DueTimeInPast`/`DueTimeTooFarAhead` rather than a shared code is
the weaker of the two decisions bundled into "three." I would not block this slice on it — three
correctly-named codes cost nothing today, `ErrorCode` grows by appending regardless of how many
members are added in one slice, and collapsing `DueTimeUnparseable` into an existing code later
would be a strictly smaller, backward-compatible change than splitting a shared code apart would be
if the reverse were tried. But if asked to bet, I would have proposed two new codes, not three,
letting `DueTimeUnparseable` fold into whichever due-time code's sentence F10-3 ends up writing
first, and letting F10-3's actual reply text (not this slice's guess at it) decide whether a third
sentence was ever needed at all.

### 3. `ILocalTimeResolver.Resolve` parses the string itself, and refuses an embedded offset or trailing `Z` rather than stripping it

**Decision, as the owner ruled it:** `Resolve(DateTime local)` becomes `Resolve(string local)`.
`LocalTimeResolver` parses with a fixed exact format:

```csharp
private const string WallClockFormat = "yyyy-MM-ddTHH:mm:ss";
...
if (!DateTime.TryParseExact(
        local, WallClockFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
{
    return Result<DateTimeOffset>.Failure(ErrorCode.DueTimeUnparseable);
}
```

Text carrying an explicit offset (`2026-08-26T10:00:00+03:00`) or a trailing `Z`
(`2026-08-26T10:00:00Z`) does not match `WallClockFormat` and is refused with
`DueTimeUnparseable` — not stripped and reparsed as a bare wall-clock reading.

**Why the resolver parses, not the tool.** This follows the same rule F10-1's Decision 2 and
Decision 10 already established for this exact type: `ILocalTimeResolver` is "the only thing in
this codebase that has an IANA zone of its own," and F9a-2's own rule, cited by F10-1, is that "the
zone keeps one owner." Parsing wall-clock text and interpreting it against a zone are not two
separable steps here — `WallClockFormat` has no offset component at all, so the moment text is
successfully parsed, it is already a bare wall-clock reading with no instant of its own, and
assigning it one is exactly what the rest of `Resolve` already does (the ambiguous-time and
spring-forward handling, unchanged by this slice). Moving parsing into `CreateTaskTool` instead
would mean the tool does a `DateTime.TryParseExact` of its own and hands `Resolve` an already-valid
`DateTime`, duplicating a piece of "how do I know this text names a valid wall-clock reading"
knowledge in a class that has no reason to own it — the same "second owner" problem Decision 10
(F10-1) already argued against for the opposite (UTC-to-local) direction.

**Why refusing an offset/`Z` beats stripping it.** The doc comment states the rule directly: *"an
embedded offset would already be a claim about an instant, not a reading."* The stronger argument is
what stripping would actually do: `ParametersJsonSchema`'s own description already tells the model
"ISO-8601 with no offset," so an offset in the text is the model deviating from its instructions —
and the model's likeliest reason for doing so is itself trying to correct for a time-zone
mismatch it has guessed at. Stripping the offset and treating the remainder as a bare local reading
would silently discard exactly the information that might explain the deviation, and would produce
an instant that is wrong in a *new* way (now off by whatever the discarded offset implied), silently
— no error, no signal, just a task due at the wrong time. Refusing loudly and asking the user to
restate the time (via `DueTimeUnparseable`, per Decision 2) is strictly safer: a wrong instant that
was never persisted costs nothing; a wrong instant that was persisted is the exact failure mode
Ruling E (F10-1 Decision 8) was written to make visible in the first place — "so a misread year or a
dropped due time is visible immediately, not after it has already surfaced as a silently wrong
reminder."

**Alternative considered and rejected: `DateTimeOffset.TryParse`, honoring an offset when present
and falling back to the configured zone otherwise.** Rejected: this would make `Resolve`'s guarantee
conditional on what the model happened to send — sometimes zone-driven, sometimes not — permanently
widening the resolver's testable surface for a case the schema already instructs the model not to
produce, for a benefit (tolerating a malformed-but-plausible input) that cuts against the "refuse
rather than guess" philosophy the past/future guards already establish.

**Alternative considered and rejected: strip a trailing `Z` or a `+HH:mm` suffix and reparse the
remainder.** Rejected for the reason argued above: this either double-applies a zone conversion (if
the stripped offset happens to already match the configured zone's current offset, by coincidence)
or silently produces a wrong instant (if it does not), with no signal to the user either way — the
worst of both refusing and honoring.

### 4. Where the new tests live: the tool in `Assistant.IntegrationTests`, the parse failure in `Assistant.UnitTests`

**Decision, as the owner ruled it:** `CreateTaskToolTests` lives in `Assistant.IntegrationTests`,
resolving `IEnumerable<IAssistantTool>` from a real container built over the real `PostgresFixture`
and matching by name:

```csharp
_sut = _provider.GetRequiredService<IEnumerable<IAssistantTool>>()
    .Single(tool => tool.Name == "create_task");
```

`Resolve`'s new parse-failure case (`DueTimeUnparseable` on malformed or offset-bearing text) is a
`[Theory]` appended to the existing `LocalTimeResolverTests` in `Assistant.UnitTests`, alongside its
sibling `Resolve` cases.

**Why the split matches spec §7.2, not just precedent.** `Resolve(string)` is a pure function —
string in, `Result<DateTimeOffset>` out, no side effect — which is precisely spec §7.2's unit-test
carve-out, and its five new `[InlineData]` cases (`"not a date"`, `""`, a bare date with no time, a
trailing `Z`, an explicit offset) are a combinatorial table over "text that is not the expected
shape," the same shape every other `Resolve` test in that file already takes.
`CreateTaskTool.ExecuteAsync`, by contrast, has an observable side effect — a row in Postgres — that
only a real database can prove, the same reasoning that put `TaskServiceTests` (F10-1) in
`Assistant.IntegrationTests` rather than `Assistant.UnitTests`.

**Why `IEnumerable<IAssistantTool>`, not `new CreateTaskTool(...)` or
`GetRequiredService<CreateTaskTool>()`.** Resolving the same way `MessageHandler` will at F10-3 —
matching a name against the registered collection — is what makes this test prove the thing that
actually matters: that the tool, *as wired in production DI*, can be constructed and executed
end to end. A direct `new CreateTaskTool(taskService, resolver)` would prove `ExecuteAsync`'s logic
but say nothing about whether the constructor's dependencies are actually satisfiable from the real
container — which is exactly the gap Decision 6, below, shows was not free.

**Why this test file builds its own `ServiceCollection` rather than calling
`postgres.CreateProvider()`.** `PostgresFixture.CreateProvider()` calls only
`AddAssistantRepository` and `AddAssistantServices` (verified above) — no `AddAssistantTime`, so no
`ILocalTimeResolver` to inject into `CreateTaskTool`, and no `IAssistantTool` registration at all.
`CreateTaskToolTests.InitializeAsync` therefore composes the exact services it needs directly:

```csharp
services.AddAssistantRepository(postgres.ConnectionString);
services.AddAssistantServices();
services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
services.AddSingleton<TimeProvider>(new FakeTimeProvider(AsOf));
services.AddScoped<IAssistantTool, CreateTaskTool>();
```

It deliberately does not call `AddAssistantAi` — that method also stands up the whole Refit
`IAiApi` client (`ImplServiceCollectionExtensions.cs`), which needs a base URL and API key this
suite has no reason to fake, for a test that never reaches the model. The one registration line it
does need from that method, `services.AddScoped<IAssistantTool, CreateTaskTool>();`, is duplicated
verbatim rather than pulling in everything else `AddAssistantAi` does. The `AddSingleton<TimeProvider>`
override after `AddAssistantServices()` replaces that method's own `TimeProvider.System`
registration — last registration wins for a singleton service in
`Microsoft.Extensions.DependencyInjection` — which is why this project needed `FakeTimeProvider`
here at all (Decision 8 below).

**Alternative considered and rejected: a hand-built fake `ITaskService` in `Assistant.UnitTests`,
avoiding the database entirely.** Rejected on the same grounds Decision 6 states for
`AiClientTests`: this project has no mocking library (confirmed: no Moq/NSubstitute/similar package
anywhere in `Directory.Packages.props`) and resolves real dependencies from real containers as
house style. A hand-rolled fake would need to reimplement enough of `TaskService` to be trusted,
which is more code than wiring the real one through `PostgresFixture`, for a strictly weaker
guarantee — it could not prove the round trip through Postgres the two happy-path tests actually
assert (`_repository.FindAsync(result.Value.Id, ...)` reading back what `ExecuteAsync` wrote).

### 5. Production DI needs no new line — verified, not assumed

**Decision (verification, not a new choice):** no line in `ImplServiceCollectionExtensions.cs`
changes. `CreateTaskTool` was already registered scoped
(`services.AddScoped<IAssistantTool, CreateTaskTool>();`, line 132, inside `AddAssistantAi`) before
this slice gave it a constructor to satisfy. Its two new dependencies are already registered:
`ITaskService` scoped (line 53, inside `AddAssistantServices`) and `ILocalTimeResolver` singleton
(line 108, inside `AddAssistantTime`).

**Why this lifetime mix is safe, stated explicitly rather than left implicit.** A scoped consumer
(`CreateTaskTool`) depending on a singleton (`ILocalTimeResolver`) is always safe — the singleton
simply outlives every scope that reads it, and `LocalTimeResolver` itself holds no
scope-lifetime state (its two primary-constructor fields, `TimeZoneInfo zone` and `TimeProvider
timeProvider`, are both effectively singleton-shaped already). A scoped consumer depending on
another scoped service resolved from the same scope (`ITaskService`) is equally safe — both live
and die with the same request. The unsafe direction — a singleton capturing a scoped dependency,
so a per-request lifetime gets trapped inside an object that outlives every request — does not occur
in either direction here. There is no captive-dependency defect to flag, and no new registration was
needed because the object graph `CreateTaskTool` now requires was already fully resolvable before
this slice; only `CreateTaskTool`'s own constructor was missing.

### 6. The `AiClientTests` regression, and why it is the most important thing in this slice

**What broke, and why.** `AiClient` (`src/Assistant.Impl/Ai/AiClient.cs:19`) takes
`IEnumerable<IAssistantTool> tools` as a primary-constructor dependency and calls
`tools.Select(ToWireTool).ToList()` inside `AskAsync` to build the wire request's tool list — on
every call, whether or not the model ever invokes a tool. Before this slice, `CreateTaskTool` took
no constructor arguments at all (F9b, schema-only), so resolving `IEnumerable<IAssistantTool>`
never needed anything beyond `CreateTaskTool`'s own no-op construction. `AiClientTests`'s container
called `AddAssistantServices()` (registering `ITaskService`) but never `AddAssistantRepository(...)`
— harmless, because nothing in that container's graph ever touched `ITaskRepository`. Once
`CreateTaskTool` gained `ITaskService` as a dependency (this slice), resolving
`IEnumerable<IAssistantTool>` — merely to *describe* tools on the wire — now requires DI to fully
construct `CreateTaskTool`, which requires a real `ITaskService`, which requires a real
`ITaskRepository` (`TaskService`'s own primary constructor: `repository`, `timeProvider`). Six of
`AiClientTests`'s tests failed with `Unable to resolve service for type 'ITaskRepository' while
attempting to activate 'TaskService'.`

**The fix applied.** `AiClientTests` takes a second constructor parameter, `PostgresFixture
postgres`, and `InitializeAsync` calls `services.AddAssistantRepository(postgres.ConnectionString);`
before `AddAssistantServices()`. Both fixtures already live in `IntegrationCollection` (the
`[Collection(IntegrationCollection.Name)]` attribute both classes already carried), so this needed
no new test infrastructure — only one added parameter and one added line. The suite still never
reads or writes a row; the class's own new `<param name="postgres">` doc comment says so explicitly:
*"This suite never reads or writes a row -- it is here because `AiClient` resolves every registered
`IAssistantTool` to describe it on the wire, and describing a tool now means constructing one that
can also execute."*

**Alternative considered and rejected: a stub `ITaskService` registered only for this test
container.** Rejected: this project carries no mocking library (confirmed: no
Moq/NSubstitute/similar reference anywhere in `Directory.Packages.props`) and its house style
resolves real dependencies from real containers everywhere else — introducing the project's first
hand-written fake service, purely to sidestep a real dependency chain in one file, would be a new
testing pattern adopted under the pressure of a failing test, for a cost (one fixture parameter, one
line) far smaller than the pattern it would introduce.

**Alternative considered and rejected: split `IAssistantTool` into a description-only interface and
a separate execution interface**, so `AiClient` could depend on only the description half and never
construct anything with real dependencies. Rejected on two grounds. First, it is exactly the
speculative abstraction this project has already tried once and reversed — F7's own "Settled at F7"
record describes introducing then deleting `OwnerOnlyUpdateHandler`, an abstraction extracted for a
single caller, because "an abstraction with one implementation is a guess" (cited by F10-1's
Decision 4 for the same reason). A split interface here would exist solely to fix one test's DI
wiring, with exactly one implementation regardless of which side of the split it sits on. Second,
and more directly: it would contradict Ruling A (F10-1 Decision 3) outright, which the owner already
settled specifically to keep tool description and tool execution as one shape, so that whatever
renders the capture reply reads the same object it dispatched through. Splitting the interface now
would re-litigate a decision the owner already made in the opposite direction, to solve a problem a
two-line test-fixture change solves without touching production code at all.

**Stated plainly, as the brief asks: this was not predicted.** Nothing in F10-1's own "Verified
facts" or "What this slice does NOT include" anticipated that giving `CreateTaskTool` constructor
dependencies would reach backward into a wire-description test that has nothing to do with
persistence. It was discovered by six failing tests — exactly the "failing test first" discipline
spec §7.7 already asks for — not predicted by any decision in the governing plan. Ruling A's own
named cost (F10-1: "F11 will have to modify `IAssistantTool` a second time") turns out to have a
second, smaller but real cost already paid here: any container that merely enumerates
`IAssistantTool` to describe tools, for any reason, must now be able to fully construct every
registered tool — forever, for as long as `IAssistantTool` carries both halves in one interface.
This is an ongoing property of Ruling A's shape, not a one-time fix, and future tools (F11) will
carry the same obligation for whatever container enumerates them.

### 7. This slice modifies an F8 interface — correct, not scope creep

**What changed.** `ILocalTimeResolver` (`src/Assistant.Interfaces/ILocalTimeResolver.cs`), shipped
at F8 (`docs/plans/2026-08-29-f8-local-time-resolver.md`), has its `Resolve` member's parameter type
changed from `DateTime` to `string`, and its class- and member-level `<remarks>`/doc comments
rewritten to describe parsing as part of the contract. `CurrentLocalTime` and `ZoneId` are
untouched.

**Why this is correct rather than scope creep.** `CreateTaskTool.ExecuteAsync` is `Resolve`'s
*first production caller*. Verified directly: `git grep -n '\.Resolve(' 0d430b8 -- src tests`
returns exactly nine matches, every one inside `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`,
every one routed through a private `Wall(string local) => DateTime.Parse(local,
CultureInfo.InvariantCulture)` helper. F8 shipped a fully guard-claused, fully tested `Resolve`
method with no caller anywhere outside its own test file — a correct interface, but one that had
never been asked to accept real, untrusted, model-supplied text, because nothing needed it to. F8's
own signature, `Resolve(DateTime local)`, quietly assumed parsing had already happened successfully
somewhere upstream; this slice is the first to discover that "somewhere upstream" was never actually
built, because nothing needed it to exist until `CreateTaskTool` had arguments to hand over. Changing
the interface here is F10-1's own Decision 2 and Decision 10 principle applied one more time: the
resolver is the one owner of everything zone-and-time-shaped, and parsing wall-clock text turns out
to be inseparable from that ownership the moment a real caller exists, for the reasons Decision 3,
above, argues in full.

**Whether `Wall` survived: it did not.** The diff deletes `Wall` outright; all nine of its former
call sites now read `resolver.Resolve(local)` directly, passing the raw string. This is the minimal
consequence of the signature change: `Wall`'s only job was converting a string to a `DateTime`
before handing it to the old `Resolve(DateTime)`, and that conversion now happens inside `Resolve`
itself via `DateTime.TryParseExact`.

### 8. The new package reference: `Microsoft.Extensions.TimeProvider.Testing` in `Assistant.IntegrationTests`

**What was added.** One line, versionless per central package management, to
`Assistant.IntegrationTests.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
```

`Directory.Packages.props:20` already pins this package at `10.9.0`, shared with
`Assistant.UnitTests`, which has referenced it since F8. This is a new *consumer* of an
already-centrally-pinned package, not a new version choice.

**Why this integration suite needs a `FakeTimeProvider` when `TaskServiceTests` (F10-1, the same
kind of test, over the same `PostgresFixture`) never did.** `TaskService.CreateAsync` has no notion
of "now" that can *reject* anything — it stamps `CreatedAt`/`UpdatedAt` with whatever
`timeProvider.GetUtcNow()` returns and never compares that value to anything, so `TaskServiceTests`
tolerates the real system clock (`TimeProvider.System`, installed by `AddAssistantServices`) without
any test becoming non-deterministic: nothing in that file asserts a relationship between "now" and a
stored value, only that the stored value equals whatever was passed in. `CreateTaskTool.ExecuteAsync`
is different: it calls `ILocalTimeResolver.Resolve`, whose two guard clauses
(`DueTimeInPast`/`DueTimeTooFarAhead`) explicitly compare the resolved instant against
`timeProvider.GetUtcNow()` *at the moment of the call*. A test asserting `"2026-08-25T10:00:00 local
is refused as in the past"` needs "now" pinned to a specific instant after that date, or the
assertion's truth would depend on what day the suite happens to run — exactly the non-determinism a
fixed `AsOf` (`new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero)`, reusing `TaskServiceTests`'s
own convention verbatim) exists to remove. `TaskServiceTests` never needed this because it never
calls anything that reads "now" as an input to a decision, only as an output to stamp.

### 9. Interaction with F10-1's Decision 10 (`ToLocal(DateTimeOffset utc)`) — conclusion: none

F10-1's Decision 10 commits F10-3 to adding `DateTimeOffset ToLocal(DateTimeOffset utc)` to
`ILocalTimeResolver`, implemented as a one-line `TimeZoneInfo.ConvertTime(utc, zone)`, reusing the
same `zone` field `Resolve` and `CurrentLocalTime` already close over.

**Checked directly against this slice's actual diff:** `LocalTimeResolver`'s primary constructor —
`LocalTimeResolver(TimeZoneInfo zone, TimeProvider timeProvider)` — is unchanged; `CurrentLocalTime`'s
implementation is unchanged; only `Resolve`'s body gained a `TryParseExact` call at its top,
consuming the new `WallClockFormat` constant. `ToLocal`'s planned body reads the identical `zone`
field this slice leaves untouched, and takes a `DateTimeOffset` in, not a `string` — it shares no
code path with `Resolve`'s new parsing branch at all.

**Conclusion, stated plainly since the brief asks for one either way: there is no interaction.**
F10-3 can add `ToLocal` exactly as F10-1's Decision 10 specified, with no adjustment needed on
account of this slice's signature change to `Resolve`. The two members solve opposite-direction
problems — local text to UTC instant, versus a UTC instant back to a local reading — that happen to
live on one interface because they share one zone (F9a-2's rule, cited throughout), not because they
share any parsing logic.

---

## What this slice does NOT include

- **Any `MessageHandler` change, any tool dispatch, any reply text.** F10-3's, per F10-1's Decisions
  4 and 8. The `ToolCallNotActedOnYet` placeholder is untouched and still what the owner sees on a
  real phone after this slice merges.
- **`ILocalTimeResolver.ToLocal`, or any other change to `CurrentLocalTime`/`ZoneId`.** F10-3's, per
  F10-1's Decision 10 and Decision 9, above — this slice touches only `Resolve`'s parameter type and
  parsing body.
- **`ReminderTask.Notes`, any migration, any `CreateTaskRequest` or `create_task` schema change.**
  F10-1's Decision 7 stands untouched; F10-3 still owns correcting the backlog's contradictory
  mentions.
- **Marking the backlog's F10 entry done.** F10 remains `observable`-and-unmet until F10-3 ships the
  reply — the same posture F10-1 took.
- **Any change to `ITaskService.CreateAsync`, `ReminderTaskMappingExtensions.ToModel`, or
  `TaskService.cs`.** F10-1's, consumed as a single call (`taskService.CreateAsync(request, dueAtUtc,
  ct)`) and otherwise untouched — none of the three files appears in this slice's diff.
- **Any `ITaskRepository`/`EfTaskRepository` change.** `AddAsync`/`FindAsync`/`GetDueRemindersAsync`
  already exist and are exactly what this slice's tests need.
- **Any new DI registration.** Decision 5, above — the object graph this slice's `CreateTaskTool`
  needs was already fully resolvable; only the class's own constructor was missing.
- **An `ErrorCode` for "the model named a tool that does not exist."** `create_task` is the only
  registered tool; F10-1's Decision 4 already flagged this gap for F10-3 to settle, once
  `MessageHandler`'s dispatch branch is the thing actually being written.

---

## File Structure

```
src/Assistant.Contracts/
    ErrorCode.cs                                       + 3 members (appended)

src/Assistant.Interfaces/
    IAssistantTool.cs                                  + ExecuteAsync
    ILocalTimeResolver.cs                               Resolve(DateTime) -> Resolve(string)

src/Assistant.Impl/
    Services/LocalTimeResolver.cs                       Resolve now parses its own input
    Tools/CreateTaskTool.cs                             + primary constructor, + ExecuteAsync

tests/Assistant.UnitTests/
    Services/LocalTimeResolverTests.cs                  call sites updated, + 1 Theory (5 cases)

tests/Assistant.IntegrationTests/
    Ai/AiClientTests.cs                                 + PostgresFixture parameter, DI fix
    Assistant.IntegrationTests.csproj                   + Microsoft.Extensions.TimeProvider.Testing
    Tools/CreateTaskToolTests.cs                        new, 203 lines
```

`src/Assistant.Impl/Telegram/`, `src/Assistant.Impl/Mapping/`, and
`docs/design/2026-08-22-slice-1-feature-backlog.md` are absent from this list, deliberately — see
"What this slice does NOT include." This slice sends nothing over Telegram and writes no mapping
code of its own.

---

## Validation

**Test count arithmetic.** Baseline (F10-1's own final state): 51 unit, 51 integration.

- Unit: 51 + 5 = **56**. The five new cases are one `[Theory]`
  (`Resolve_TextDoesNotMatchTheExpectedShape_IsRefused`) with five `[InlineData]` rows, appended to
  `LocalTimeResolverTests.cs`. Every other test in that file is unchanged except its call site.
- Integration: 51 + 10 = **61**. All ten are new, all in `CreateTaskToolTests.cs`: five `[Fact]`
  methods (title-and-resolvable-due-time happy path, no-due-time happy path, due-time-in-past,
  due-time-too-far-ahead, due-time-unparseable) plus a three-case `[Theory]`
  (`ExecuteAsync_TitleMissingEmptyOrBlank_IsRejected`) plus a two-case `[Theory]`
  (`ExecuteAsync_ArgumentsAreNotAUsableObject_IsRejected`). `AiClientTests.cs`'s own diff adds zero
  tests — six `[Fact]` methods before and after, confirmed by direct comparison — its change is DI
  wiring only (Decision 6).

**Both counts were run directly, not derived by arithmetic alone:**

```bash
docker compose -f compose.test.yaml up -d
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build          # 56 passed, 0 failed
dotnet test tests/Assistant.IntegrationTests --no-build   # 61 passed, 0 failed
docker compose -f compose.test.yaml down
```

**Why `CreateTaskTool.ExecuteAsync` gets no unit test of its own.** It has an observable side
effect — a row in Postgres — the moment its happy path runs, so per spec §7.2 it belongs entirely to
`Assistant.IntegrationTests`, the same status `TaskService.CreateAsync` (F10-1) already has. Its
failure paths (malformed JSON, missing title, three due-time guard failures) are pure branches with
no side effect individually, but they live in the same file rather than a separate unit-level one:
splitting "the same method's happy path and failure paths" across two projects, purely because some
branches happen not to write a row, would fragment one method's coverage for no reviewer benefit —
a reader checking `ExecuteAsync`'s behavior would need two files open instead of one.

**This slice still cannot be validated against real Telegram.** `CreateTaskTool.ExecuteAsync` is
reachable only from `CreateTaskToolTests` until F10-3 gives it a caller in `MessageHandler`. The only
observable proof this slice offers is the test suite above.

---

## Steps

Because this slice's code was drafted in full before this plan was finalized, the steps below
present the actual, final content of each change — not an approximation of it — in the order that
makes the failing-test-first discipline (spec §7.7) legible: interface signatures change first (so
the compiler immediately demands new bodies), then implementations, then the tests that exercise
them, then the regression this slice's own change caused and the fix for it.

**Decisions this slice carries:** 1 through 9, given in full above — 1, 2, and 3 carry the owner's
new rulings for this slice; 4 and 5 verify the owner's remaining two rulings; 6, 7, 8, and 9 record
findings this slice's own drafting surfaced rather than decisions the owner was asked to make.

**Consumes:** `ITaskService.CreateAsync`, `ReminderTaskMappingExtensions.ToModel` (F10-1),
`CreateTaskRequest`, `IAssistantTool`'s schema members (F9b), `ILocalTimeResolver`/`LocalTimeResolver`
(F8), `TaskServiceTests`'s `AsOf` convention and `PostgresFixture` (F5a-F10-1).
**Produces:** `IAssistantTool.ExecuteAsync`, `CreateTaskTool`'s primary constructor and
`ExecuteAsync` body, `ILocalTimeResolver.Resolve(string)`, three new `ErrorCode` members,
`CreateTaskToolTests`, the `AiClientTests` DI fix.

One commit. `IAssistantTool.ExecuteAsync`, `CreateTaskTool`'s constructor, and `Resolve`'s new
signature are mutually load-bearing — none compiles independently of the others, the same
"no smaller independently-buildable unit" reasoning F10-1 gave for its own single commit.

### Commit 1: `IAssistantTool.ExecuteAsync`, `CreateTaskTool`'s execution, and the resolver that now parses its own input

**Files:**
- Modify: `src/Assistant.Contracts/ErrorCode.cs`
- Modify: `src/Assistant.Interfaces/ILocalTimeResolver.cs`
- Modify: `src/Assistant.Impl/Services/LocalTimeResolver.cs`
- Modify: `src/Assistant.Interfaces/IAssistantTool.cs`
- Modify: `src/Assistant.Impl/Tools/CreateTaskTool.cs`
- Modify: `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`
- Create: `tests/Assistant.IntegrationTests/Tools/CreateTaskToolTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Ai/AiClientTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj`

- [x] **Step 1: Append three `ErrorCode` members**

In `src/Assistant.Contracts/ErrorCode.cs`, after `TaskAlreadyCompleted`'s closing comma:

```csharp

    /// <summary>
    /// A tool call's arguments could not be parsed as a JSON object at all. The model is not
    /// bound by a tool's declared schema, so this is reachable in practice, not only in theory.
    /// </summary>
    ToolArgumentsMalformed,

    /// <summary>
    /// A tool call's arguments parsed, but a field the tool requires was absent or blank.
    /// </summary>
    ToolArgumentMissing,

    /// <summary>
    /// A due time's text did not match the exact wall-clock shape the model is asked to supply,
    /// so no instant could be resolved from it at all.
    /// </summary>
    DueTimeUnparseable,
```

- [x] **Step 2: Change `ILocalTimeResolver.Resolve` to take the raw string**

In `src/Assistant.Interfaces/ILocalTimeResolver.cs`, `Resolve`'s signature and doc comment become:

```csharp
    /// <summary>
    /// Parses a wall-clock time in the configured zone and resolves it to the instant it names.
    /// </summary>
    /// <param name="local">
    /// The date and time as the user means it, in the exact form the model is asked to supply:
    /// ISO-8601 with no offset and no trailing zone designator, for example
    /// <c>2026-08-31T10:00:00</c>. Text that does not match this shape -- including text that is
    /// otherwise a valid date but carries an explicit offset or a trailing <c>Z</c> -- is refused
    /// rather than partially honoured: this project's times are always wall-clock readings with
    /// no instant of their own until this method assigns one, so an embedded offset would already
    /// be a claim about an instant, not a reading.
    /// </param>
    /// <returns>
    /// The instant, on UTC with a zero offset, or the reason it was refused:
    /// <see cref="ErrorCode.DueTimeUnparseable"/> when <paramref name="local"/> does not match
    /// the expected shape at all, <see cref="ErrorCode.DueTimeInPast"/> more than a minute before
    /// now, and <see cref="ErrorCode.DueTimeTooFarAhead"/> more than two years after it. A
    /// reading in a spring-forward gap resolves to the same reading past the gap; a reading in a
    /// fall-back hour resolves to the first of its two occurrences.
    /// </returns>
    Result<DateTimeOffset> Resolve(string local);
```

- [x] **Step 3: Build and watch it fail**

```bash
dotnet build --no-restore
```

Expected, and confirmed: `LocalTimeResolver.Resolve(DateTime local)` no longer satisfies the
interface (`CS0535`), and every one of `LocalTimeResolverTests.cs`'s nine `Resolve(Wall(...))` call
sites now passes a `DateTime` where the interface, once implemented against, will expect a `string`.

- [x] **Step 4: Implement parsing inside `LocalTimeResolver.Resolve`**

In `src/Assistant.Impl/Services/LocalTimeResolver.cs`, add `using System.Globalization;`, a format
constant, and replace the old `DateTime.SpecifyKind` line:

```csharp
    private const string WallClockFormat = "yyyy-MM-ddTHH:mm:ss";
```

```csharp
    /// <inheritdoc/>
    public Result<DateTimeOffset> Resolve(string local)
    {
        if (!DateTime.TryParseExact(
                local, WallClockFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
        {
            return Result<DateTimeOffset>.Failure(ErrorCode.DueTimeUnparseable);
        }

        // Falling back always lowers the offset, so the larger of an ambiguous reading's two
        // offsets is its first occurrence. GetUtcOffset and ConvertTimeToUtc both hand back the
        // second. A reading inside a spring-forward gap needs no such handling: GetUtcOffset
        // returns the offset in force before the gap, which names the same instant as the same
        // reading past it, whatever the gap's width.
        var offset = zone.IsAmbiguousTime(wall)
            ? zone.GetAmbiguousTimeOffsets(wall).Max()
            : zone.GetUtcOffset(wall);

        var instant = new DateTimeOffset(wall, offset).ToUniversalTime();
        var now = timeProvider.GetUtcNow();

        if (instant < now - PastTolerance)
        {
            return Result<DateTimeOffset>.Failure(ErrorCode.DueTimeInPast);
        }

        if (instant > now.AddYears(MaxYearsAhead))
        {
            return Result<DateTimeOffset>.Failure(ErrorCode.DueTimeTooFarAhead);
        }

        return Result<DateTimeOffset>.Success(instant);
    }
```

The ambiguous-time, spring-forward, past, and future logic beneath the new `TryParseExact` guard is
unchanged from F8 — only the top of the method, and the type it accepts, changed.

- [x] **Step 5: Update `LocalTimeResolverTests.cs`'s call sites and add the parse-failure theory**

Every `resolver.Resolve(Wall(x))` becomes `resolver.Resolve(x)`; the `Wall` helper is deleted; one
new theory is appended after `Resolve_TimeEitherSideOfAClockChange_IsUnmoved`:

```csharp

    /// <summary>
    /// When a due time's text does not match the exact wall-clock shape the model is asked to
    /// supply
    /// And it is resolved
    /// Then it is refused, whether the text is nonsense or merely names an instant of its own.
    /// </summary>
    /// <param name="local">Text that is not a bare wall-clock reading.</param>
    /// <remarks>
    /// A trailing <c>Z</c> or an explicit offset is deliberately refused rather than stripped and
    /// honoured: this project's times are always wall-clock readings with no instant of their
    /// own until this method assigns one, so text that already claims an instant is a different
    /// shape entirely, not a lenient variant of the expected one.
    /// </remarks>
    [Theory]
    [InlineData("not a date")]
    [InlineData("")]
    [InlineData("2026-08-17")]
    [InlineData("2026-08-17T10:00:00Z")]
    [InlineData("2026-08-17T10:00:00+02:00")]
    public void Resolve_TextDoesNotMatchTheExpectedShape_IsRefused(string local)
    {
        // Arrange
        var resolver = ResolverAt("2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(local);

        // Assert
        Assert.Equal(ErrorCode.DueTimeUnparseable, result.Error);
    }
```

- [x] **Step 6: Build and confirm the unit-test project compiles and passes**

```bash
dotnet test tests/Assistant.UnitTests --no-build
```

Expected, and confirmed: **56 passed**, 0 failed (51 baseline + 5 new cases).

- [x] **Step 7: Add `ExecuteAsync` to `IAssistantTool`**

In `src/Assistant.Interfaces/IAssistantTool.cs`, add `using Assistant.Contracts;` and
`using Assistant.Models;`, rewrite the interface's own `<remarks>`, and append:

```csharp
    /// <summary>
    /// Binds the model's raw arguments to this tool's own request shape and carries it out.
    /// </summary>
    /// <param name="argumentsJson">
    /// The model's arguments, as the raw JSON object text the wire carried. The model is not
    /// bound by <see cref="ParametersJsonSchema"/>: a required field may be absent, empty, or of
    /// the wrong shape, and the text itself may not parse as JSON at all.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The task this call created, or the reason it was refused:
    /// <see cref="ErrorCode.ToolArgumentsMalformed"/> when the arguments could not be parsed as
    /// a JSON object at all, <see cref="ErrorCode.ToolArgumentMissing"/> when a field this tool
    /// requires was absent or blank, or -- when a due time was given but could not be honoured --
    /// <see cref="ErrorCode.DueTimeUnparseable"/>, <see cref="ErrorCode.DueTimeInPast"/>, or
    /// <see cref="ErrorCode.DueTimeTooFarAhead"/>. Nothing is persisted on any failure path.
    /// </returns>
    Task<Result<ReminderTask>> ExecuteAsync(string argumentsJson, CancellationToken ct);
```

- [x] **Step 8: Give `CreateTaskTool` a primary constructor and implement `ExecuteAsync`**

In `src/Assistant.Impl/Tools/CreateTaskTool.cs`:

```csharp
internal sealed class CreateTaskTool(ITaskService taskService, ILocalTimeResolver clock)
    : IAssistantTool
{
```

```csharp
    /// <inheritdoc/>
    public async Task<Result<ReminderTask>> ExecuteAsync(string argumentsJson, CancellationToken ct)
    {
        CreateTaskRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CreateTaskRequest>(argumentsJson);
        }
        catch (JsonException)
        {
            return Result<ReminderTask>.Failure(ErrorCode.ToolArgumentsMalformed);
        }

        if (request is null)
        {
            return Result<ReminderTask>.Failure(ErrorCode.ToolArgumentsMalformed);
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result<ReminderTask>.Failure(ErrorCode.ToolArgumentMissing);
        }

        DateTimeOffset? dueAtUtc = null;

        if (request.DueAtLocal is not null)
        {
            var resolved = clock.Resolve(request.DueAtLocal);

            if (!resolved.IsSuccess)
            {
                return Result<ReminderTask>.Failure(resolved.Error!.Value);
            }

            dueAtUtc = resolved.Value;
        }

        return await taskService.CreateAsync(request, dueAtUtc, ct);
    }
```

- [x] **Step 9: Build and confirm the whole solution still compiles**

```bash
dotnet build --no-restore
```

Expected, and confirmed at draft time: `Assistant.Impl` and `Assistant.Interfaces` compile; the
integration test project fails to build, because `AiClientTests`'s container cannot yet construct
`CreateTaskTool` (Decision 6). This is the predicted, correct next failure, not a surprise.

- [x] **Step 10: Add `Microsoft.Extensions.TimeProvider.Testing` to `Assistant.IntegrationTests`**

```xml
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
```

- [x] **Step 11: Write `CreateTaskToolTests`**

Create `tests/Assistant.IntegrationTests/Tools/CreateTaskToolTests.cs` (203 lines; full content
verified in the working tree, `_sut` resolved via `IEnumerable<IAssistantTool>` per Decision 4):

```csharp
[Collection(IntegrationCollection.Name)]
public sealed class CreateTaskToolTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int NoLimit = 100;

    private static readonly DateTimeOffset AsOf = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider _provider = null!;

    private IAssistantTool _sut = null!;

    private ITaskRepository _repository = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddAssistantRepository(postgres.ConnectionString);
        services.AddAssistantServices();
        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(AsOf));
        services.AddScoped<IAssistantTool, CreateTaskTool>();
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<IEnumerable<IAssistantTool>>()
            .Single(tool => tool.Name == "create_task");
        _repository = _provider.GetRequiredService<ITaskRepository>();

        await postgres.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();
```

Seven test methods follow (two happy paths, three guard-clause rejections, and two
`[Theory]`-shaped rejection groups for a bad title and unparseable arguments), each asserting both
`result.IsSuccess`/`result.Error` and, on the happy paths, a fresh read from `_repository` — the same
"prove the mapper's output and the persisted row agree" pattern F10-1's own `TaskServiceTests`
established. Full text is in the working tree at the path above; every assertion in it was run and
passed (Step 13).

- [x] **Step 12: Fix the `AiClientTests` regression**

In `tests/Assistant.IntegrationTests/Ai/AiClientTests.cs`, add `using Assistant.Repository;`, a
second constructor parameter, and one line in `InitializeAsync`:

```csharp
/// <param name="postgres">
/// The shared database fixture. This suite never reads or writes a row -- it is here because
/// <c>AiClient</c> resolves every registered <see cref="IAssistantTool"/> to describe it on the
/// wire, and describing a tool now means constructing one that can also execute. See
/// <c>CreateTaskTool</c>'s own remarks for why that is one interface rather than two.
/// </param>
[Collection(IntegrationCollection.Name)]
public sealed class AiClientTests(WireMockFixture wireMock, PostgresFixture postgres) : IAsyncLifetime
{
```

```csharp
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantRepository(postgres.ConnectionString);
        services.AddAssistantServices();
```

- [x] **Step 13: Build and run the whole suite**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected, and confirmed: build succeeds solution-wide, zero warnings, zero errors. **56 passed** unit
(0 failed). **61 passed** integration (0 failed) — including all six previously-broken
`AiClientTests` cases and all ten new `CreateTaskToolTests` cases.

- [ ] **Step 14: Commit**

```bash
git add src/Assistant.Contracts/ErrorCode.cs \
        src/Assistant.Interfaces/ILocalTimeResolver.cs \
        src/Assistant.Impl/Services/LocalTimeResolver.cs \
        src/Assistant.Interfaces/IAssistantTool.cs \
        src/Assistant.Impl/Tools/CreateTaskTool.cs \
        tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs \
        tests/Assistant.IntegrationTests/Tools/CreateTaskToolTests.cs \
        tests/Assistant.IntegrationTests/Ai/AiClientTests.cs \
        tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj
git commit
```

Message:

```
feat: IAssistantTool.ExecuteAsync, the tool executes

CreateTaskTool gains a primary constructor over ITaskService and
ILocalTimeResolver and an ExecuteAsync body: parse the model's raw
arguments, refuse a missing arguments object or a blank title before
anything is persisted, resolve any due time, and call CreateAsync --
handing back the persisted ReminderTask itself, per the owner's
ruling on IAssistantTool.ExecuteAsync's shape.

ILocalTimeResolver.Resolve now parses the model's raw text itself
rather than trusting an already-parsed DateTime: this is Resolve's
first production caller, so the shape a real caller actually has to
hand over -- untrusted JSON text, not a pre-parsed value -- finally
exists to design against. An embedded offset or trailing Z is
refused rather than stripped: this project's times are wall-clock
readings with no instant of their own until Resolve assigns one, so
text that already claims an instant is a different shape, not a
lenient variant of the expected one.

Three ErrorCode members are appended: ToolArgumentsMalformed and
ToolArgumentMissing split the wire-level and field-level failure
classes a tool call can hit; DueTimeUnparseable covers text that
never reaches the past/future guards at all.

Giving CreateTaskTool constructor dependencies broke six AiClient
tests: AiClient resolves every registered IAssistantTool merely to
describe it on the wire, but DI must fully construct each one to do
so, and that container had AddAssistantServices without
AddAssistantRepository. Fixed by adding PostgresFixture to
AiClientTests rather than introducing this project's first mock --
the suite still never reads or writes a row.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

---

## Self-review

**This commit:**
- [ ] `ExecuteAsync`'s signature is `Task<Result<ReminderTask>> ExecuteAsync(string argumentsJson,
      CancellationToken ct)` everywhere it appears — interface and implementation — matching
      Ruling A (F10-1 Decision 3) exactly, no `ToolExecutionResult`
- [ ] `CreateTaskTool`'s primary constructor takes exactly `(ITaskService taskService,
      ILocalTimeResolver clock)`, both already registered in production DI with no new line
      (Decision 5)
- [ ] `ExecuteAsync` checks, in order: JSON parses to a non-null `CreateTaskRequest`; `Title` is not
      null/empty/whitespace; `DueAtLocal`, if present, resolves. Every check returns before
      `taskService.CreateAsync` is reached — nothing is persisted on any failure path, checked
      structurally, not merely tested (matching F10-1 Decision 5's own structural argument)
- [ ] `ILocalTimeResolver.Resolve(string local)` refuses text with an embedded offset or trailing
      `Z` rather than stripping it (Decision 3); `LocalTimeResolver`'s primary constructor is
      unchanged
- [ ] Exactly three `ErrorCode` members are appended, after `TaskAlreadyCompleted`, with `Unknown`
      still first — no member inserted, none renumbered
- [ ] `CreateTaskToolTests` resolves `IAssistantTool` via `IEnumerable<IAssistantTool>` from a real
      container, not `new CreateTaskTool(...)` and not `GetRequiredService<CreateTaskTool>()`
      directly (Decision 4)
- [ ] `AiClientTests` gains `PostgresFixture` and one `AddAssistantRepository` call, no mock, no
      stub, no interface split (Decision 6)
- [ ] `Microsoft.Extensions.TimeProvider.Testing` in `Assistant.IntegrationTests.csproj` carries no
      inline `Version=`
- [ ] No `ITaskService.CreateAsync`, `ReminderTaskMappingExtensions.ToModel`, `TaskService.cs`,
      `MessageHandler.cs`, or `ReminderTask.cs` change anywhere in this diff
- [ ] Every new/changed public member carries a three-line-tag `<summary>` plus every
      `<param>`/`<returns>` `CS1591`/`CS1573` requires — confirmed by a zero-warning, zero-error
      build
- [ ] Test summaries are Gherkin (`When`/`And`/`Then`), one clause per line, in both
      `CreateTaskToolTests.cs` and the new `LocalTimeResolverTests.cs` theory
- [ ] No emoji anywhere, including the commit message
- [ ] **No plan-internal decision citation (`Decision 1`, `(Decision 2)`, or similar) inside any C#
      code block, doc comment, or commit message** — every fenced code block above was re-read for
      this before the plan was committed
- [ ] Plain ASCII `--` used inside C# doc comments and the commit message body; this document's own
      prose uses real em dashes
- [ ] Test counts land at exactly 56 unit / 61 integration, both run and confirmed passing, not
      just arithmetic

**Whole feature (F10), once F10-3 also lands:**
- [ ] Tool dispatch lives inline in `MessageHandler`, matching `CallbackRouter`'s own precedent, per
      F10-1's Decision 4 (Ruling B) — unaffected by anything in this slice
- [ ] The capture reply renders `task.DueAt` back to local text via a new `ILocalTimeResolver.ToLocal`
      member, confirmed unaffected by this slice's `Resolve` signature change (Decision 9, above)
- [ ] `DueTimeUnparseable`'s reply sentence, once F10-3 writes it, is checked against this plan's own
      Decision 2 concern — whether it ends up textually identical to `DueTimeInPast`'s, which would
      confirm the weaker half of the three-code split rather than justify it after the fact
- [ ] `ReminderTask.Notes` still does not exist; F10-1's Decision 7 (Ruling F) is not silently
      reversed
- [ ] The backlog's F10 entry is marked done, and its `Notes` mentions corrected, only by F10-3
- [ ] Spec coverage across all three slices: §5.1 (the full flow, completed by F10-3), §5.4 (the
      guard clauses and the "before anything is persisted" ordering — this slice's own contribution
      is structural, per this file's Self-review above), §7.2 (no duplicated test coverage), §7.7
      (failing test first, Steps 3 and 9 above)
- [ ] This slice's diff measures 383 lines by the convention argued in "How this slice fits F10";
      the full feature's running total (120 + 383 = 503) stays comfortably under the 1000-line
      budget even summed, with F10-3's own 200-250 estimate still to come
