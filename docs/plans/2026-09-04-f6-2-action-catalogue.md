# F6-2 — the action catalogue

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F6-2's own plan (`docs/plans/2026-09-03-f6-2-route-the-tap.md`) shipped `ITaskAction`
resolved by a bare `Key` string — `DoneAction.Key => "done"`, matched at `CallbackRouter.cs:71`
with `a.Key == actionKey`. In review of PR #24 the owner asked for a single, shared catalogue of
every task-action definition instead: one place that declares each action's key, label and
description, with the description explicitly requested "for other developers to understand the
actions." This plan builds that catalogue as **two more commits appended to the branch PR #24
already carries** — not a new slice, not a new pull request. After it lands, `ITaskAction` names
its catalogue entry (`TaskActionDefinition Definition`) instead of a loose string, and `DoneAction`
is the catalogue's first — and today only — declared action.

**Tech Stack:** Unchanged from F6-2's own plan: .NET 10 (`net10.0`), nullable enabled, warnings are
errors, `CS1591` an error everywhere. This plan adds **no new NuGet package** and **no new project
reference or `.csproj` change of any kind** — the catalogue lives in `Assistant.Contracts`, which
`Assistant.Interfaces` and `Assistant.Impl` already reference (verified below), and both test
projects touched here already reference `Assistant.Contracts` too.

**Spec:** No section of `docs/design/slice-1-reminders.md` documents a shared action catalogue
directly — this request came from the owner in review of PR #24, not from the spec. The closest
existing precedent is §6.4's own button table (`Button` / `Action` / `Effect` columns,
`slice-1-reminders.md:431-436`), which already pairs a human-readable label (`Done`, `Snooze 1h`,
`Tomorrow`, `Edit`) with the `ITaskAction` that runs it — cited in Decision 3 below as evidence the
spec's own vocabulary already separates a label from a key. §6.4's sentence "Actions are
`ITaskAction` implementations resolved by key" (`slice-1-reminders.md:429`) remains true after this
change — it names a resolution rule, not the property that used to carry the key directly — so no
spec edit is needed; verified by rereading §6.4 in full, not assumed.

**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md`'s F6 entry (line 274) and its own
"Settled at F6-2" note (line 283, added by F6-2's own plan) — unchanged by this plan, since nothing
here reopens how F6 is sliced across pull requests. F11's entry (line 596, `SnoozeAction`,
`RescheduleAction`, `EditAction`) is the catalogue's next real consumer, not this plan — see "What
this slice does NOT include." Section 1 (line 15: YAGNI, the 1000-line PR budget) governs Decision
9 below directly.

**Also read:** `docs/plans/2026-09-03-f6-2-route-the-tap.md` — the format this plan matches, and
the plan that put `ITaskAction`/`DoneAction`/`CallbackRouter` on this branch in the first place.
`AGENTS.md` — conventions. `tests/Assistant.UnitTests/Architecture/ConventionTests.cs` and
`DependencyRuleTests.cs` — the two structural tests this plan's file placement must keep green.

---

## Why this is two more commits on an open PR, not a new plan's own PR

PR #24 (`feat: F6-2 - the tap is routed and answered`) is open and under review. Its branch,
`feature/f6-2-route-the-tap`, already carries the three commits F6-2's own plan specified plus
review fixups on top of them — verified directly with `git log main..HEAD --oneline`, which shows
seven commits including the plan-document commit itself. This document does not restart that
history or open a second PR: it specifies two more commits to append to the same branch, reviewed
as part of the same PR, because the catalogue is a direct refinement of code that PR already
introduces (`ITaskAction`, `DoneAction`, `CallbackRouter`) rather than a new capability that earns
its own slice. Per this repository's own convention of one plan document per unit of reviewable
work, this plan numbers its own two commits **Commit 1** and **Commit 2**, understood to land after
whatever is already on the branch when work begins — the implementer re-verifies the branch tip in
Step 1 below rather than assuming a specific commit SHA.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error everywhere.
- **Every class taking arguments uses a primary constructor.** No separate constructors. Neither
  new type in this plan is a class that takes constructor arguments — `TaskActionDefinition` is a
  positional record, `TaskActions` is a static class with no constructor at all — so this rule is
  not exercised by new code, only respected by not violating it.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag, for a simple member. Test
  summaries are Gherkin (`When` / `And` / `Then`), one clause per line, which can run longer than
  three lines when a scenario has more than one clause — matching `CallbackCodecTests`' own
  existing multi-clause summaries.
- Positional record parameters are documented with `<param name="...">`, matching `ToolCall.cs` and
  `CreateTaskRequest.cs`, both already in `Assistant.Contracts`.
- Central package management; no inline `Version=`. Not exercised this plan — no package changes.
- No emoji anywhere: source, tests, docs, or commit messages.
- C# comments and XML docs use plain ASCII double dashes `--`, never em dashes.
- **Never run `docker compose down -v`.** Use `docker compose -f compose.test.yaml down` with no
  flags. Not exercised by this plan's own validation, which needs no new Docker mapping — see
  Decision 9's blast-radius note — but stated because the implementer will still be running the
  existing integration suite.
- **Never run `dotnet run --project src/Assistant.Worker` or any `send-test-message` command.**
  They need real secrets and send a real message to the owner's phone. Nothing in this plan needs
  either.
- PR budget: 1000 changed lines per PR, excluding the plan. Decision 9 does this arithmetic in
  full against PR #24's current standing.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
  ```

---

## Verified facts this plan rests on

Every one of these was read from the working tree at HEAD of `feature/f6-2-route-the-tap`
directly, or produced by actually building the exact code this plan specifies in an isolated,
disposable `git worktree` (removed before this plan was written) — not recollection, and not a
plan-time guess about whether the code compiles.

- **PR #24 is open, `feat: F6-2 - the tap is routed and answered`, `+2970/-27`** (`gh pr view 24`).
  Excluding `docs/plans/` — computed directly with `git diff main...HEAD --numstat -- .
  ':!docs/plans/'` and summed — the PR stands at **745 added / 27 deleted = 772 changed lines**.
  The ceiling is 1000 changed lines per PR (backlog §1). **Headroom: 228 changed lines.** (The
  remaining 2225 added lines belong entirely to `docs/plans/2026-09-03-f6-2-route-the-tap.md`,
  confirmed by `wc -l` on that file matching `2970 - 745` exactly.)
- **Project references, read from the `.csproj` files directly:** `Assistant.Contracts.csproj` has
  no `<ItemGroup>` at all — no `ProjectReference` of any kind.
  `Assistant.Interfaces.csproj` references `Assistant.Models` and `Assistant.Contracts`.
  `Assistant.Impl.csproj` references `Assistant.Interfaces`, `Assistant.Contracts`, and
  `Assistant.Models`. `Assistant.Contracts` is therefore the only assembly both
  `Assistant.Interfaces` and `Assistant.Impl` can see — confirming the catalogue has exactly one
  legal home.
- **`tests/Assistant.UnitTests/Architecture/ConventionTests.cs:93`,
  `Interfaces_declares_no_concrete_public_classes()`, fails the build on any public,
  non-abstract class in `Assistant.Interfaces`.** A `record` is a class under the hood, so a public
  `TaskActionDefinition` declared in `Assistant.Interfaces` would trip this test. Line 72,
  `Contracts_declares_no_interfaces()`, forbids only public interfaces in `Assistant.Contracts` —
  a record and a static class are both legal there, and neither existing rule inspects
  `Assistant.Contracts` for anything else.
- **`tests/Assistant.UnitTests/Architecture/DependencyRuleTests.cs`** forbids
  `Microsoft.EntityFrameworkCore`, `Npgsql`, `Telegram.Bot` in `Assistant.Models`
  (lines 31-33), and those three plus `Refit` in `Assistant.Interfaces` (lines 56-59). Both new
  Contracts files touch none of this — plain records and a static class over `string` and
  `IReadOnlyList<T>`.
- **`src/Assistant.Contracts/` holds exactly four files today**, all confirmed by reading each in
  full: `CreateTaskRequest.cs` (a positional record, `<param>`-documented), `ErrorCode.cs`,
  `Result.cs` (`Result` and `Result<T>`, both `public readonly record struct`), `ToolCall.cs`
  (`public sealed record ToolCall(string Name, string ArgumentsJson);`, `<param>`-documented). Every
  data type in this project today is a positional record or record struct — none uses `required`
  ... `init` properties. That style appears instead in `src/Assistant.Impl/Settings/`
  (`DatabaseSettings.cs`, `TelegramSettings.cs`, `TimeSettings.cs`), where every property is
  `public required <T> ... { get; init; }` — because those types bind from configuration, which
  needs settable properties, not a positional constructor. `TaskActionDefinition` has no
  configuration-binding need, so the Contracts convention, not the Settings one, applies.
- **`src/Assistant.Interfaces/ITaskAction.cs` is exactly 29 lines today.** Line 20 is
  `string Key { get; }`, documented with a three-line `<summary>` and a `<value>Lowercase, for
  example <c>done</c>.</value>`. Its own `<remarks>` already say snooze, reschedule and edit
  actions "follow at F11, each adding one more implementation rather than changing this one" —
  language this plan's own edit to the same `<remarks>` block preserves.
- **`src/Assistant.Impl/Services/Actions/DoneAction.cs` line 13 is exactly
  `public string Key => "done";`.**
- **`src/Assistant.Impl/Telegram/CallbackRouter.cs` line 71 is exactly
  `var action = actions.FirstOrDefault(a => a.Key == actionKey);`.** Its own `<param
  name="actions">` doc, line 17, reads `Every registered task action, resolved by <see
  cref="ITaskAction.Key"/>.`
- **The literal `"done"` appears at exactly six call sites**, grepped directly:
  `tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs:18,34,41` and
  `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs:88,121,173`.
- **`src/Assistant.Interfaces/IAssistantTool.cs` is the existing Name+Description precedent.** Its
  `Description` (`<value>A plain-language instruction telling the model when to call this
  tool.</value>`) is consumed: `src/Assistant.Impl/Ai/AiClient.cs:69` reads
  `tool.Name, tool.Description, JsonNode.Parse(tool.ParametersJsonSchema)!)` inside
  `ToWireTool`, sending it on the wire request. `TaskActionDefinition.Description` has no analogous
  call site anywhere in the repository — grepped for any reference to a `.Description` read off an
  `ITaskAction`/`TaskActionDefinition`-shaped value and found none, because neither type exists
  yet.
- **`CallbackCodec.Encode` interpolates the action key straight into
  `$"{Prefix}:{action}:{...}"`, and `TryDecode` rejects any input whose `Split(':')` length is not
  exactly 3** (`CallbackCodec.cs`, read in full). A key containing a colon would therefore encode
  into a button that renders correctly and is undecodable forever the moment it is tapped — the
  concrete failure mode Decision 7's colon-guard test exists to catch.
- **`tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs` (187 lines) already builds
  the full DI container.** `InitializeAsync` (lines 45-67) calls `services.BuildServiceProvider()`
  and stores the result in the field `_provider` (declared line 38), which stays alive for the
  whole test class via `[Collection(IntegrationCollection.Name)]` and its `PostgresFixture`/
  `WireMockFixture` constructor parameters. Every registered `ITaskAction` (today, only
  `DoneAction`, via `services.AddAssistantServices()`) is reachable from `_provider` without
  building a second container.
- **Baseline test counts, run directly against this branch's actual HEAD, not assumed:**
  `dotnet build --no-restore -c Release` is clean, zero warnings, zero errors.
  `dotnet test tests/Assistant.UnitTests` reports **49 passed**.
  `dotnet test tests/Assistant.IntegrationTests` (against the already-running
  `compose.test.yaml` stack) reports **44 passed**. Both match F6-2's own plan's stated ending
  counts exactly, confirming nothing has drifted since that plan's Commit 3 landed.
- **This plan's exact code was built and tested before being written down.** Every file in the
  Steps below was copied into a throwaway `git worktree` off this branch's HEAD, built with
  `dotnet build --no-restore -c Release`, and run. Result: **zero warnings, zero errors**;
  `tests/Assistant.UnitTests` **51 passed** (49 baseline + 2 new `TaskActionsTests`);
  `tests/Assistant.IntegrationTests --filter "FullyQualifiedName~CallbackRouterTests"` **6 passed**
  (5 baseline + 1 new registration-guard fact). The worktree was removed immediately after, and no
  file in the real working tree was created or modified to produce this plan. The named-arguments
  construction site the orchestrator specified as a correction (`new(Key: "done", Label: "Done",
  Description: "...")` against the positional record `TaskActionDefinition`) was included in this
  exact build and compiles without a warning.
- **Measured, not estimated, line cost.** Diffing the built files against the real repository
  files (`diff -u`, counted directly) gives: two brand-new Contracts files (+25, +27), one new unit
  test file (+34), `ITaskAction.cs` (+8/-7), `DoneAction.cs` (+1/-1), `CallbackRouter.cs` (+4/-2),
  `CallbackRouterTests.cs` (+23/-3). **Total: 122 added, 13 deleted, 135 changed lines** — see
  Decision 9's table for the per-file breakdown.

---

## Decisions

### 1. The catalogue lives in `Assistant.Contracts`, in two new files

**Decision:** `src/Assistant.Contracts/TaskActionDefinition.cs` declares one type:

```csharp
public sealed record TaskActionDefinition(string Key, string Label, string Description);
```

`src/Assistant.Contracts/TaskActions.cs` declares the catalogue itself, with `Done` declared before
`All`.

**Why `Assistant.Contracts`, not `Assistant.Interfaces`.** The verified facts above leave exactly
one candidate: `Assistant.Contracts` is the only assembly both `Assistant.Interfaces` (which must
name `TaskActionDefinition` on `ITaskAction.Definition`) and `Assistant.Impl` (which must name
`TaskActions.Done` inside `DoneAction`) can see, because `Assistant.Contracts` itself references
nothing and is referenced by both. `Assistant.Interfaces` is ruled out directly by
`ConventionTests.cs:93` — a record is a class, and that test fails the build on any public,
non-abstract class living there. This is not a stylistic preference; it is the one placement this
repository's own architecture tests permit.

**Why `Done` is declared before `All`.** `TaskActions.All`'s own initializer reads `TaskActions.Done`
(`[Done]`, a one-element collection expression). C# runs a type's static member initializers in
textual declaration order, and unlike a local variable's definite-assignment rules, referencing a
not-yet-initialized static member from another static initializer in the same type is legal C# —
it would read that member's default value (`null`, for a reference-type record) rather than
failing to compile. But the nullable-reference-type analyzer does catch it: reversing the order
produces `CS8601: Possible null reference assignment`, and `Directory.Build.props:7` sets
`TreatWarningsAsErrors`, so that warning fails the build in this repository rather than shipping.
This was confirmed empirically, not merely reasoned about: compiling the reversed order in
`Assistant.Contracts` produces exactly that error. Declaring `Done` first is therefore the natural,
readable order and the one that builds — not a defense against an undetectable bug.

**Why two files, not one.** `TaskActionDefinition` is the shape; `TaskActions` is the data. Keeping
them separate matches the existing precedent one level up: `ITaskAction` (the interface, in
`Assistant.Interfaces`) and `DoneAction` (an instance of it, in `Assistant.Impl`) already live in
different files because one is a contract and the other is content conforming to it. A single file
mixing a type definition with its own catalogue of instances would read as one thing when it is
conceptually two, and would make a future `git blame` on "what does the catalogue contain" also
surface the shape's own documentation as noise.

### 2. The construction site uses named arguments; `TaskActionDefinition` itself stays a plain positional record

**Decision:** `TaskActionDefinition` stays exactly the one-line positional record Decision 1
specifies — `<param>`-documented, no properties, no `required`/`init`. Only `TaskActions.Done`'s
construction site changes, using named arguments against the positional record:

```csharp
public static TaskActionDefinition Done { get; } = new(
    Key: "done",
    Label: "Done",
    Description: "Marks the task complete. Refused when the task is already complete.");
```

**Why.** With one declared action this reads fine either way, but Decision 1 already commits this
catalogue to growing — F11 adds three more entries (Decision 8's "What this slice does NOT
include"). At four entries, `new("snooze", "Snooze", "Clears the reminder and fires again in the
given duration.")` is three bare strings in a row with no label telling a reader which is which;
named arguments keep every future declaration self-describing without changing the type at all.

**Cost, measured rather than assumed.** Named arguments on a positional record cost nothing
structurally: `TaskActionDefinition.cs` is identical to Decision 1's one-liner, and its `<param>`
documentation is unaffected. The alternative — turning `Key`, `Label`, `Description` into
`required ... { get; init; }` properties, constructed with an object initializer — would read
similarly at the call site, but would expand `TaskActionDefinition.cs` by roughly 18 lines once
each property carries its own three-line `<summary>` (matching `TelegramSettings.cs`'s own style
for its three `required` properties), against 228 lines of total headroom this plan cannot spend
carelessly (Decision 9). It would also break with every existing type in `Assistant.Contracts` —
`ToolCall`, `CreateTaskRequest`, `Result`, `Result<T>` are all positional records; the
`required`/`init` shape appears only in `src/Assistant.Impl/Settings/`, where configuration binding
genuinely needs settable properties, a need `TaskActionDefinition` does not share.

**Reversibility, named plainly.** If the owner would rather have the object-initializer form
instead, the change is confined to `TaskActionDefinition.cs` and `TaskActions.cs`'s own
construction site — `DoneAction.cs`, `ITaskAction.cs`, `CallbackRouter.cs`, and every test in this
plan read only `.Key`, `.Label`, `.Description` off the finished object, and do not care which
syntax built it. Nothing downstream would need to change.

### 3. The middle field is named `Label`, not `Name` — flagged for the owner's veto

**Decision:** the human-readable field is `Label`.

**Why, argued honestly.** The owner's own request used "name/key/description." `Label` is this
plan's proposal instead, because next to `Key`, the word "Name" does not say which of the two is
the identifier — both a key and a name could reasonably be called "the action's name." `Label`
says plainly, on its own, that it is the text a human reads on the button, which is exactly what
this field is for. Spec §6.4's own button table (`slice-1-reminders.md:431`) already calls its
first column `Button`, not `Name` — a second piece of evidence that this project's own vocabulary
already reaches for a rendering-flavoured word over "name" when the field means "what a person
sees," the same distinction `Label` draws here.

**This is the one naming choice in this plan open to the owner's veto in review.** Everything else
above is argued to a conclusion; this one is a judgment call stated with its reasoning, not hidden
behind a settled tone. If the owner prefers `Name`, the change is mechanical and small: rename the
positional parameter, its `<param>` tag, and the two call sites that read it (none exist yet
outside this plan's own new files, since nothing renders a button using `Label` until F6-3).

### 4. `Description` is never read at runtime, and the code says so

**Decision:** `TaskActionDefinition`'s `<remarks>` state plainly that `Description` has no runtime
consumer and exists for the person reading the catalogue.

**Why this needs saying, not just being true.** The owner asked for `Description` explicitly, "for
other developers to understand the actions" — a stated purpose this plan takes at face value and
does not second-guess. But nothing in this codebase sends a description anywhere: there is no
`/help` command, and actions are never described to the chat model the way tools are. Verified
directly by grep: no call site references a `.Description` off an `ITaskAction`/
`TaskActionDefinition`-shaped value, because until this plan lands neither type exists, and
`IAssistantTool.Description` — the one existing precedent for a field with this name — **is**
consumed, at `AiClient.cs:69`, sent on the wire inside `ToWireTool`. A future contributor skimming
both types side by side, seeing one `Description` sent on the wire and a same-named one that is
not, has a real chance of concluding the second one is dead code, or losing time hunting for the
call site that transmits it. The `<remarks>` block heads that off directly, by name, contrasting
the two.

### 5. `ITaskAction.Key` is replaced by `TaskActionDefinition Definition { get; }`, not added alongside it

**Decision:** `ITaskAction` drops `string Key { get; }` entirely and gains
`TaskActionDefinition Definition { get; }` in its place. `DoneAction.Key => "done"` becomes
`DoneAction.Definition => TaskActions.Done`. `CallbackRouter.cs:71` becomes
`actions.FirstOrDefault(a => a.Definition.Key == actionKey)`.

**Why replace, not extend.** Two sources for the same key would drift the moment a second action
existed: nothing would stop `SnoozeAction.Key` and `TaskActions.Snooze.Key` from disagreeing, and
the type system would not catch it — only a test could, and only if someone wrote one. Removing
`Key` outright makes that drift structurally impossible rather than merely tested against.

**Blast radius, named exactly.** This is a breaking interface change, and its entire reach is: one
implementation (`DoneAction`), one consumer (`CallbackRouter`), and three test call sites
(`CallbackRouterTests.cs:88,121,173`, per Decision 6). `CallbackCodecTests.cs`'s three `"done"`
literals are untouched — see Decision 6 for why. No DI registration changes: `ITaskAction` and
`DoneAction` stay registered exactly as `ImplServiceCollectionExtensions.cs` already has them,
because only the interface's member shape changed, not what implements or consumes it.

### 6. `CallbackCodecTests` keeps its `"done"` literals; `CallbackRouterTests` switches to `TaskActions.Done.Key`

**Decision:** the three `"done"` literals in `tests/Assistant.UnitTests/Telegram/
CallbackCodecTests.cs` (lines 18, 34, 41) are left exactly as they are. The three in
`tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs` (lines 88, 121, 173) become
`TaskActions.Done.Key`.

**Why the codec's tests don't change.** `CallbackCodec.Encode`/`TryDecode` take a bare `string
action` parameter and know nothing about `ITaskAction` or the catalogue — its own file, read in
full, names neither type. `"done"` in `CallbackCodecTests` is an arbitrary sample string standing
in for "some action key," chosen for readability the same way `Guid.Empty` stands in for "some
task id" in the same file. Coupling the codec's own tests to `TaskActions.Done.Key` would assert a
relationship the production code does not have — the codec would pass its own tests unchanged even
if the catalogue were deleted entirely.

**Why the router's tests do change.** `CallbackRouterTests` genuinely resolves an action through
the catalogue now: `CallbackRouter.HandleAsync` decodes a key off the wire and matches it against
`a.Definition.Key` on each registered `ITaskAction`. A test that still hand-writes `"done"` would
keep passing today by coincidence — nothing would break if `TaskActions.Done.Key` were ever
changed to something else, because the test's literal and the catalogue's declared value would
have silently diverged. `TaskActions.Done.Key` at the call site makes the test fail loudly instead,
which is what a test resolving through the catalogue should do.

### 7. Three guard tests, protecting the safe-extension path the catalogue exists for

**Decision:** two new unit tests in `tests/Assistant.UnitTests/Contracts/TaskActionsTests.cs`, and
one new integration `[Fact]` appended to the existing `CallbackRouterTests`.

- **Unit: every declared key is unique across the catalogue.** With one entry today this is
  trivially true — its value is entirely in what it protects once F11 adds three more.
- **Unit: no declared key contains `:`.** `CallbackCodec.TryDecode` splits its input on `:` and
  rejects anything whose segment count is not exactly 3 (verified fact above); a key containing a
  colon would produce a button that renders and is undecodable forever the moment it is tapped —
  a concrete, already-identified failure mode, not a hypothetical one.
- **Integration, one `[Fact]` appended to `CallbackRouterTests`:** every declared definition
  resolves to a registered `ITaskAction`, checked in both directions.

**Why the integration test lives inside the existing `CallbackRouterTests`, not a new fixture
class.** That class already builds the full container via `_provider = services.
BuildServiceProvider()` in `InitializeAsync` (verified fact above) — a new fixture class would pay
the full `PostgresFixture`/`WireMockFixture`/`IntegrationCollection` setup cost again for
roughly 20 lines of ceremony, to run one assertion that needs none of Postgres or WireMock at all.
Appending the `[Fact]` reuses infrastructure that already exists for a reason unrelated to this
test, at effectively zero marginal cost.

**Why it must resolve through a scope, and compare both directions.** `ITaskAction`
implementations are registered `AddScoped` (unchanged by this plan), so resolving them from
`_provider` directly, rather than from `_provider.CreateScope().ServiceProvider`, would either
throw or silently resolve nothing depending on the container's validation mode — the test opens
`using var scope = _provider.CreateScope();` and resolves from `scope.ServiceProvider`. Comparing
ordered key sequences in both directions —

```csharp
Assert.Equal(
    TaskActions.All.Select(d => d.Key).Order(),
    resolved.Select(a => a.Definition.Key).Order());
```

— catches the two ways this list of declarations and that DI registration list can drift apart
independently: a definition declared in `TaskActions.All` with no matching `AddScoped<ITaskAction,
...>()` call (a button that renders and answers "That button is no longer valid." forever), and a
registered `ITaskAction` whose `Definition` was never added to `TaskActions.All` (an action that
works when tapped but that no future button-rendering code, including F6-3's, would ever know to
offer). A one-directional `Assert.Contains` would catch only the first.

### 8. Two commits

**Decision:** Commit 1 adds the two Contracts files plus `TaskActionsTests.cs` — nothing consumes
the catalogue yet, and the build and unit suite are green on their own. Commit 2 changes
`ITaskAction`, `DoneAction`, `CallbackRouter`, the three `CallbackRouterTests` call sites, and adds
the registration `[Fact]` — full suite green.

**Why this split.** It is the same shape F6-2's own Commit 2/Commit 3 boundary already used: build
the seam with nothing wired to it first, wire it in second. A reviewer can approve Commit 1 in
isolation — a pure addition, provably inert, with its own passing tests — before evaluating whether
the wiring in Commit 2 is done correctly. Splitting any finer (for example, `ITaskAction` and
`DoneAction` in one commit, `CallbackRouter` in another) would leave an intermediate commit where
the build is green but the interface's one implementation and its one consumer disagree about
`Key` versus `Definition` — an artificial, uncompilable midpoint this plan does not manufacture.

### 9. Does this fit inside PR #24's remaining budget?

**The arithmetic, done explicitly.** PR #24 stands at 772 changed lines today, excluding
`docs/plans/` (verified fact above). The ceiling is 1000. **Headroom: 228 changed lines.**

This plan's actual cost was measured, not estimated, by building the exact code in the Steps below
inside a disposable worktree and diffing it against the real files:

| File | Change | Added | Deleted |
| :--- | :--- | ---: | ---: |
| `src/Assistant.Contracts/TaskActionDefinition.cs` | new | 25 | 0 |
| `src/Assistant.Contracts/TaskActions.cs` | new | 27 | 0 |
| `tests/Assistant.UnitTests/Contracts/TaskActionsTests.cs` | new | 34 | 0 |
| `src/Assistant.Interfaces/ITaskAction.cs` | modify | 8 | 7 |
| `src/Assistant.Impl/Services/Actions/DoneAction.cs` | modify | 1 | 1 |
| `src/Assistant.Impl/Telegram/CallbackRouter.cs` | modify | 4 | 2 |
| `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs` | modify | 23 | 3 |
| **Total** | | **122** | **13** |

**135 changed lines**, against 228 headroom — leaving 93 lines to spare even after this plan lands,
and confirming the orchestrator's own roughly-160 estimate was, if anything, conservative.

**What gets cut first if the implementer overruns anyway** — stated even though the measured total
leaves comfortable headroom, because a real implementation can still drift from a plan's exact
wording (a longer `<remarks>` block chosen in review, for instance). In the order given in Decision
7 above: **first**, the unit test asserting every declared key is unique — with one catalogue entry
today it protects nothing yet exercised, the same "generality with no test able to exercise its one
observable effect" this project's own backlog §1 already rules out elsewhere, and it is trivially
restored the day F11 adds a second entry. **Second**, if still over budget, the unit test asserting
no declared key contains a colon — this one guards a concrete, already-identified wire-format bug
rather than a hypothetical one, so it is cut only after the weaker test and only under real
pressure. **Never cut:** the integration registration-guard `[Fact]`. It is the one test that
catches the catalogue's actual failure mode in production — a definition with no matching DI
registration shipping as a button that renders and silently never works — which is a materially
worse outcome than either unit test's gap, and it is also the cheapest of the three to keep,
appended to a fixture that already exists (Decision 7).

---

## What this slice does NOT include

- **`SnoozeAction`, `RescheduleAction`, `EditAction`, or any other entry beyond `Done`.** F11 adds
  the next three, extending `TaskActions.All` by one `TaskActionDefinition` and one `ITaskAction`
  implementation per action — exactly the extension path Decision 7's guard tests exist to keep
  safe, not exercised early.
- **Any `arg` field on `TaskActionDefinition`, or any change to `CallbackCodec`.** F6-2's own
  Decision 4 already left `CallbackCodec.TryDecode` blind to the optional trailing `:<arg>`
  segment spec §6.4 describes, for the same YAGNI reason: nothing yet needs it. This plan does not
  reopen that decision.
- **Any consumer of `Description` at runtime.** No `/help` command, no message to the chat model.
  Decision 4 states this in the code itself so it is not later mistaken for an oversight.
- **Any DI registration change.** `ITaskAction`/`DoneAction` stay registered exactly as
  `ImplServiceCollectionExtensions.cs` already has them; only the interface member they satisfy
  changed shape.
- **Any `.csproj` change.** No new package, no new project reference, no new
  `InternalsVisibleTo` — every project this plan touches already references every project it
  needs to.
- **Any edit to `docs/design/slice-1-reminders.md`.** §6.4's own text remains accurate unedited,
  per the Spec section above.
- **A button anywhere new, or any change to F6-3's own scope.** This plan only reshapes how an
  already-existing action declares itself; it renders nothing.

---

## File Structure

```
src/Assistant.Contracts/
    TaskActionDefinition.cs                   new                                     (Commit 1)
    TaskActions.cs                             new                                     (Commit 1)

src/Assistant.Interfaces/
    ITaskAction.cs                             Key replaced by Definition             (Commit 2)

src/Assistant.Impl/
    Services/Actions/DoneAction.cs            Key replaced by Definition             (Commit 2)
    Telegram/CallbackRouter.cs                resolves via a.Definition.Key          (Commit 2)

tests/Assistant.UnitTests/
    Contracts/TaskActionsTests.cs             new                                     (Commit 1)

tests/Assistant.IntegrationTests/
    Telegram/CallbackRouterTests.cs           "done" -> TaskActions.Done.Key,
                                               + registration-guard fact              (Commit 2)
```

`tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs` is absent from this list, deliberately —
Decision 6's whole point is that it needs no change.

---

## Validation

**Test count arithmetic.** Baseline, run directly against this branch's actual HEAD (see "Verified
facts"): 49 unit, 44 integration.

- Commit 1 adds `TaskActionsTests.cs` to `Assistant.UnitTests`: two `[Fact]` methods — unit:
  49 + 2 = **51** after Commit 1. Integration stays **44** — no integration test file is touched.
- Commit 2 adds one `[Fact]` to the existing `CallbackRouterTests.cs`: integration:
  44 + 1 = **45** after Commit 2. Unit stays **51** — no unit test file is touched.

Expected final state: **51 unit, 45 integration.** Both numbers were independently confirmed by
actually running the exact code in the verification worktree (see "Verified facts").

**Split between `Assistant.UnitTests` and `Assistant.IntegrationTests`, justified per spec §7.2,**
the same standard F6-2's own plan applied:

- Key uniqueness and the colon guard are pure functions over `TaskActions.All`, with no side effect
  and no DI involved — spec §7.2's carve-out for a pure function belongs at the unit level, the
  same reasoning F6-2's own plan already applied to `CallbackCodec`.
- The registration-guard fact has an observable side effect only a real container produces — a
  missing `AddScoped` registration — so it cannot be written as a unit test at all; there is no
  unit-level substitute for "does the DI container actually have this."

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests

docker compose -f compose.test.yaml up -d          # no --build needed -- Assistant.WireMock is untouched
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

**What this plan can and cannot show on a real phone.** Nothing changes observably from the
outside — Decision 5's whole point is that `ITaskAction`'s public shape changed while its
behaviour did not. The only observable proof this plan offers is the test suite above: the Done
button still completes a task, still says "Already done." on a second tap, and still answers a
malformed or unrecognised callback politely — exactly as F6-2's own `CallbackRouterTests` already
proved, now resolving through `TaskActions.Done.Key` instead of a hand-typed literal, plus the new
guarantee that every declared action really is wired up.

---

## Steps

**Decisions this slice carries:** 1-9, given in full above.

**Consumes:** `ITaskAction`, `DoneAction`, `CallbackRouter`, `CallbackCodec`,
`CallbackRouterTests`/`PostgresFixture`/`WireMockFixture`/`IntegrationCollection` (all F6-2, already
on this branch).
**Produces:** `TaskActionDefinition`, `TaskActions`, `ITaskAction.Definition`, and the three test
call sites and one new guard fact that prove the catalogue and its one consumer agree.

Two commits. Commit 1 adds a self-contained, fully unit-tested catalogue with no consumer and no
DI registration yet. Commit 2 wires `ITaskAction`'s one implementation and one consumer onto it and
proves the wiring is complete.

### Commit 1: the catalogue itself

**Files:**
- Create: `src/Assistant.Contracts/TaskActionDefinition.cs`
- Create: `src/Assistant.Contracts/TaskActions.cs`
- Create: `tests/Assistant.UnitTests/Contracts/TaskActionsTests.cs`

- [ ] **Step 1: Confirm the branch tip and re-verify the facts this plan rests on**

```bash
git log main..HEAD --oneline
dotnet build --no-restore -c Release
dotnet test tests/Assistant.UnitTests
```

Expected: build clean, zero warnings; unit tests **49 passed**. If either number differs from this
plan's "Verified facts" section, stop and reconcile before continuing — something has changed on
the branch since this plan was written.

- [ ] **Step 2: Create `TaskActionDefinition.cs`**

```csharp
namespace Assistant.Contracts;

/// <summary>
/// One action an inline button can perform on a task, declared once so every consumer -- the
/// router that resolves it, and a future button that renders it -- shares the same definition.
/// </summary>
/// <param name="Key">
/// The action's key, as carried on the wire inside the callback codec. Must never contain a
/// colon -- <c>CallbackCodec.TryDecode</c> splits its input on <c>:</c>, so a key that contained
/// one would render a button that is undecodable forever once tapped.
/// </param>
/// <param name="Label">
/// The text a human reads on the button itself.
/// </param>
/// <param name="Description">
/// What the action does, written for a developer reading this catalogue.
/// </param>
/// <remarks>
/// <see cref="Description"/> has no runtime consumer. Nothing sends it anywhere -- there is no
/// <c>/help</c> command, and actions are never described to the chat model, unlike
/// <c>IAssistantTool.Description</c>, which <c>AiClient.ToWireTool</c> does send on the wire. It
/// exists solely for the person reading this catalogue: do not delete it as dead code, and do
/// not go looking for the call site that transmits it -- there is not one.
/// </remarks>
public sealed record TaskActionDefinition(string Key, string Label, string Description);
```

- [ ] **Step 3: Create `TaskActions.cs`**

```csharp
namespace Assistant.Contracts;

/// <summary>
/// Every action an inline button can perform on a task, in the one place both
/// <c>CallbackRouter</c> and a future button-rendering caller can read.
/// </summary>
/// <remarks>
/// <see cref="Done"/> is declared before <see cref="All"/> because C# runs a type's static
/// member initializers in declaration order, and <see cref="All"/>'s own initializer reads
/// <see cref="Done"/> -- reversing the order compiles cleanly but leaves <see cref="All"/>
/// holding a <see langword="null"/> element, since <see cref="Done"/> would not yet have run.
/// </remarks>
public static class TaskActions
{
    /// <summary>
    /// The Done button's definition.
    /// </summary>
    public static TaskActionDefinition Done { get; } = new(
        Key: "done",
        Label: "Done",
        Description: "Marks the task complete. Refused when the task is already complete.");

    /// <summary>
    /// Every declared action, in declaration order.
    /// </summary>
    public static IReadOnlyList<TaskActionDefinition> All { get; } = [Done];
}
```

- [ ] **Step 4: Create `TaskActionsTests.cs`**

```csharp
using Assistant.Contracts;

namespace Assistant.UnitTests.Contracts;

/// <summary>
/// Test class for <see cref="TaskActions"/>.
/// </summary>
public sealed class TaskActionsTests
{
    /// <summary>
    /// When every declared action's key is compared against the others
    /// Then no two keys are equal.
    /// </summary>
    [Fact]
    public void All_EveryDeclaredKey_IsUnique()
    {
        // Act
        var keys = TaskActions.All.Select(d => d.Key).ToList();

        // Assert
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    /// <summary>
    /// When every declared action's key is inspected
    /// Then none contains a colon.
    /// </summary>
    [Fact]
    public void All_EveryDeclaredKey_ContainsNoColon()
    {
        // Assert
        Assert.All(TaskActions.All, d => Assert.DoesNotContain(":", d.Key));
    }
}
```

- [ ] **Step 5: Build and run the new unit tests**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --filter "FullyQualifiedName~TaskActionsTests"
```

Expected: zero warnings; `TaskActionsTests` **2 passed**.

- [ ] **Step 6: Run the whole suite**

```bash
dotnet test tests/Assistant.UnitTests
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

Expected: unit tests **51 passed** (49 + 2, see "Test count arithmetic"); integration tests **44
passed**, unchanged — nothing in this commit touches `ITaskAction`, `DoneAction`, or any
integration test file.

- [ ] **Step 7: Commit**

```bash
git add src/Assistant.Contracts/TaskActionDefinition.cs \
        src/Assistant.Contracts/TaskActions.cs \
        tests/Assistant.UnitTests/Contracts/TaskActionsTests.cs
git commit
```

Message:

```
feat: add the shared task-action catalogue

TaskActionDefinition (Key, Label, Description) and TaskActions.All
give every task action one shared, static definition instead of the
bare Key string ITaskAction carries today. The owner asked for this
in review of PR #24, Description included, explicitly for other
developers reading the catalogue -- it has no runtime consumer, and
TaskActionDefinition's own remarks say so, contrasting it with
IAssistantTool.Description, which AiClient does send on the wire.

Both types live in Assistant.Contracts: it is the only assembly both
Assistant.Interfaces and Assistant.Impl reference, and
ConventionTests.Interfaces_declares_no_concrete_public_classes rules
out Assistant.Interfaces directly, since a record is a class.

Nothing consumes the catalogue yet -- ITaskAction still declares Key,
not Definition. That change, and DoneAction/CallbackRouter's updates
to match, are the next commit, so this one stays a pure, provably
inert addition with its own passing tests.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

### Commit 2: `ITaskAction` names its catalogue entry

**Files:**
- Modify: `src/Assistant.Interfaces/ITaskAction.cs`
- Modify: `src/Assistant.Impl/Services/Actions/DoneAction.cs`
- Modify: `src/Assistant.Impl/Telegram/CallbackRouter.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs`

- [ ] **Step 1: Rewrite `ITaskAction.cs` in full**

Replace the entire file:

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// One action an inline button's tap can perform on a task.
/// </summary>
/// <remarks>
/// Resolved by matching <see cref="Definition"/>'s <see cref="TaskActionDefinition.Key"/> against
/// the callback codec's decoded action segment. A caller that finds no implementation whose key
/// matches produces a polite reply rather than throwing, per spec 6.4. <c>DoneAction</c> is the
/// first implementation; snooze, reschedule and edit actions follow at F11, each adding one more
/// implementation rather than changing this one.
/// </remarks>
public interface ITaskAction
{
    /// <summary>
    /// This action's entry in the shared catalogue.
    /// </summary>
    /// <value>Key, label and description all come from <see cref="TaskActions"/>.</value>
    TaskActionDefinition Definition { get; }

    /// <summary>
    /// Performs the action against the given task.
    /// </summary>
    /// <param name="taskId">The task the button referred to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    Task<Result> ExecuteAsync(Guid taskId, CancellationToken ct);
}
```

- [ ] **Step 2: Rewrite `DoneAction.cs` in full**

Replace the entire file:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services.Actions;

/// <summary>
/// Completes a task in response to its Done button being tapped.
/// </summary>
/// <param name="taskService">The single writer for tasks.</param>
internal sealed class DoneAction(ITaskService taskService) : ITaskAction
{
    /// <inheritdoc/>
    public TaskActionDefinition Definition => TaskActions.Done;

    /// <inheritdoc/>
    public Task<Result> ExecuteAsync(Guid taskId, CancellationToken ct) =>
        taskService.CompleteAsync(taskId, ct);
}
```

- [ ] **Step 3: Update `CallbackRouter.cs`'s doc comment and resolution line**

In `src/Assistant.Impl/Telegram/CallbackRouter.cs`:

Before:

```csharp
/// <param name="actions">Every registered task action, resolved by <see cref="ITaskAction.Key"/>.</param>
```

After:

```csharp
/// <param name="actions">
/// Every registered task action, resolved by matching <see cref="TaskActionDefinition.Key"/>
/// against each one's <see cref="ITaskAction.Definition"/>.
/// </param>
```

Before:

```csharp
        var action = actions.FirstOrDefault(a => a.Key == actionKey);
```

After:

```csharp
        var action = actions.FirstOrDefault(a => a.Definition.Key == actionKey);
```

No other line in this file changes.

- [ ] **Step 4: Update `CallbackRouterTests.cs`'s using directives and its three `"done"` literals**

In `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs`:

Before:

```csharp
using Assistant.Impl;
using Assistant.Impl.Settings;
```

After:

```csharp
using Assistant.Contracts;
using Assistant.Impl;
using Assistant.Impl.Settings;
```

Then, at each of the three call sites (lines 88, 121, 173 before this edit):

Before:

```csharp
        var data = CallbackCodec.Encode("done", task.Id);
```

After, at all three sites:

```csharp
        var data = CallbackCodec.Encode(TaskActions.Done.Key, task.Id);
```

- [ ] **Step 5: Append the registration-guard fact to `CallbackRouterTests.cs`**

Immediately before the class's closing brace, after
`Listener_StrangerTapsTheButton_AnswersButLeavesTheTaskUntouched`'s own closing brace:

```csharp

    /// <summary>
    /// When every ITaskAction registered in the real container is resolved from a scope
    /// Then its key set is exactly the catalogue's declared key set, in both directions.
    /// </summary>
    [Fact]
    public void ITaskAction_RegisteredImplementations_MatchTheCatalogueKeysExactly()
    {
        // Arrange
        using var scope = _provider.CreateScope();

        // Act
        var resolved = scope.ServiceProvider.GetServices<ITaskAction>();

        // Assert
        Assert.Equal(
            TaskActions.All.Select(d => d.Key).Order(),
            resolved.Select(a => a.Definition.Key).Order());
    }
```

- [ ] **Step 6: Build and run the changed tests**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --filter "FullyQualifiedName~TaskActionsTests"
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~CallbackRouterTests"
```

Expected: zero warnings; `TaskActionsTests` still **2 passed**; `CallbackRouterTests` **6 passed**
(the four existing test methods, five cases, plus the one new registration-guard fact).

- [ ] **Step 7: Run the whole suite**

```bash
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

Expected: unit tests **51 passed**, unchanged from Commit 1; integration tests **45 passed** (44 +
1, see "Test count arithmetic").

- [ ] **Step 8: Commit**

```bash
git add src/Assistant.Interfaces/ITaskAction.cs \
        src/Assistant.Impl/Services/Actions/DoneAction.cs \
        src/Assistant.Impl/Telegram/CallbackRouter.cs \
        tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs
git commit
```

Message:

```
feat: ITaskAction names its catalogue entry instead of a loose key

ITaskAction.Key is replaced -- not extended alongside -- by
TaskActionDefinition Definition. Two sources for the same key could
drift the moment a second action existed; this makes that
structurally impossible instead of merely tested against.
DoneAction.Definition => TaskActions.Done is now its only knowledge
of its own key, label and description. CallbackRouter resolves an
inbound action key by matching actions.FirstOrDefault(a =>
a.Definition.Key == actionKey), replacing the bare a.Key comparison.

CallbackRouterTests' three "done" literals become TaskActions.Done.Key,
so a future change to the catalogue's own declared key would fail
these tests loudly rather than silently diverging from a hand-typed
string. CallbackCodecTests keeps its own "done" literals unchanged --
the codec takes a bare string and knows nothing about the catalogue,
so coupling its tests to TaskActions would assert a relationship the
production code does not have.

A new integration fact resolves every registered ITaskAction from a
real scope and compares its key set against TaskActions.All in both
directions, catching the catalogue's actual failure mode: a
declaration with no matching DI registration ships as a button that
renders and answers "That button is no longer valid." forever, and a
registration with no matching declaration is invisible to anything
that will one day render buttons from the catalogue.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

---

## Self-review

**Commit 1 (the catalogue):**
- [ ] `TaskActionDefinition` is a one-line positional record with `<param>`-documented `Key`,
      `Label`, `Description` — no properties, no `required`/`init`
- [ ] `TaskActions.Done` is declared textually before `TaskActions.All` in the file
- [ ] `TaskActions.Done`'s construction site uses named arguments (`Key:`, `Label:`,
      `Description:`); `TaskActionDefinition.cs` itself is unaffected by that choice
- [ ] Both new files live in `Assistant.Contracts`, not `Assistant.Interfaces`
- [ ] `TaskActionDefinition`'s `<remarks>` state plainly that `Description` has no runtime
      consumer, and contrast it by name with `IAssistantTool.Description`/`AiClient.ToWireTool`
- [ ] Neither new type is registered in DI, and neither is referenced from any file outside this
      commit's own three — confirmed by re-reading `ImplServiceCollectionExtensions.cs` and
      `CallbackRouter.cs` after Commit 1's diff, both untouched
- [ ] `TaskActionsTests.cs`'s two facts each assert one thing: key uniqueness, and the colon guard
      `CallbackCodec.TryDecode`'s wire format depends on

**Commit 2 (`ITaskAction` names its entry):**
- [ ] `ITaskAction.Key` is gone entirely — not left alongside `Definition`
- [ ] `DoneAction.Definition => TaskActions.Done` is the only place `DoneAction` names its own key,
      label or description
- [ ] `CallbackRouter.cs`'s only two changed lines are the `<param name="actions">` doc comment and
      the `FirstOrDefault` predicate; no other line in the file differs from HEAD
- [ ] `CallbackCodecTests.cs` is not modified — its three `"done"` literals are untouched, per
      Decision 6
- [ ] `CallbackRouterTests.cs`'s three former `"done"` literals are now `TaskActions.Done.Key`, and
      no other literal in that file changed
- [ ] The new registration-guard fact resolves `ITaskAction` from `_provider.CreateScope()`, not
      from `_provider` directly — `ITaskAction` is registered `AddScoped`
- [ ] The registration-guard fact compares ordered key sequences in both directions
      (`TaskActions.All` against the resolved set, via `.Order()` on both sides), not a
      one-directional `Assert.Contains`
- [ ] No `.csproj` file is touched by either commit

**Whole plan, once both commits land:**
- [ ] Every new public member has XML docs; `<summary>` blocks are three lines for a simple member,
      Gherkin (`When`/`And`/`Then`, one clause per line) for every test
- [ ] No emoji anywhere, including both commit messages
- [ ] C# comments and doc comments use `--`, never an em dash; this plan's own prose uses real em
      dashes, matching the surrounding plan documents
- [ ] No `<see cref="...">` in any doc comment points at a type in a project that does not
      reference the type's own project — checked directly: `TaskActionDefinition.cs`'s `<remarks>`
      names `IAssistantTool` and `AiClient.ToWireTool` as plain `<c>` text, not `<see cref>`,
      because `Assistant.Contracts` does not and must not reference `Assistant.Interfaces` or
      `Assistant.Impl`
- [ ] Type and member names are spelled identically everywhere they appear: `TaskActionDefinition`,
      `TaskActions.Done`, `TaskActions.All`, `ITaskAction.Definition`, `Key`, `Label`,
      `Description`
- [ ] No placeholder text ships inside a source file
- [ ] This plan's diff stays under PR #24's remaining budget: Decision 9 measured 135 changed
      lines against 228 headroom, by actually building the code, not estimating it
- [ ] Both commit messages end with the required trailer, after a blank line
