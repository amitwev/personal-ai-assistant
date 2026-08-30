# F9a-2 — the clock and the system prompt

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F9a makes the assistant able to reach a chat model and return its answer to the owner
over Telegram — no tools yet; parsing a `create_task` call out of the answer is F9b. This
document is F9a's **second of four** independently reviewable PRs. It ships no network call and
no chat client: it gives `ILocalTimeResolver` the two members a prompt needs to state "now" in
the assistant's own zone, and it introduces `SystemPrompt`, the class that builds the text every
call to the model will start with — read by slice 3's client, not called by anything yet.

**Tech Stack:** .NET 10 (`net10.0`), nullable enabled, warnings are errors — the existing stack,
unchanged. This slice adds no new NuGet package and no new project reference; Refit, WireMock.Net's
second stub, and `Microsoft.Extensions.TimeProvider.Testing`'s move into
`Assistant.IntegrationTests` all arrive in slice 3.

**Spec:** `docs/design/slice-1-reminders.md` §5.2 (system prompt — this slice implements its
example sentence as a test fixture verbatim), §5.4 (time contract — the configured IANA zone,
never a hardcoded one, is the ground `CurrentLocalTime` and `ZoneId` stand on), and §11's closing
note on timezone references ("No code names a zone literal; it is bound from configuration").
§5.1 (flow) and §5.5 (provider routing) are slice 3 and slice 4's concern, not this one's.
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F9, split into F9a (of which
this is slice 2 of four) and F9b.

---

## Where this sits

F9a ships as four independently reviewable PRs rather than one. Precedent: F8 shipped its plan
and its code together in one PR and broke this repository's 1000-line budget (1243 plan + 598
code = 1841 lines); F9a's plan is split by PR instead, each slice getting its own document.

1. **Slice 1 — AI settings.** `AiSettings`, `appsettings.json`, `.env.example`, a minimal
   `AddAssistantAi`, and the `Program.cs` chain link. Merged as `987ad21`.
2. **Slice 2 — the clock and the system prompt (this document).** `ILocalTimeResolver` gains
   `CurrentLocalTime` and `ZoneId`; a new `SystemPrompt` builds the text sent to the model.
3. **Slice 3 — reach the model.** Refit, the wire types, `IChatClient`, `ChatCompletionsClient`,
   failure handling, and the WireMock stub.
4. **Slice 4 — the owner gets the model's answer.** `MessageHandler` replaces F7's echo with a
   real call to the model, plus the design-doc corrections that follow from it.

Slices 3 and 4 each get their own plan document, written after the slice before it has merged.
This document covers slice 2 only — it is not a guide to implementing slices 3 or 4.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error everywhere.
- **Every class taking arguments uses a primary constructor.** No separate constructors.
- **CS9113 is an error**: a primary-constructor parameter nothing references fails the build.
  Never declare a parameter one step ahead of the step that uses it.
- Every enum's first member is `Unknown`, with no explicit numeric values. New members are
  **appended**, never inserted.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- Central package management; no inline `Version=` (NU1008).
- No emoji anywhere: source, tests, docs, or commit messages.
- **Never run `docker compose down -v`.** Use `docker compose -f compose.test.yaml down` with no
  flags.
- Integration tests need `docker compose -f compose.test.yaml up -d --build` first — and
  `--build`, because the feature this slice belongs to changes the WireMock stub image (slice 3).
- PR budget: 1000 changed lines per PR, excluding the plan (which merges on its own, docs-only).
  The rejected monolith this plan was split from estimated this slice at ~120 code lines,
  comfortably under budget.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
  ```

---

## Verified facts this plan rests on

- 2026-08-16 is a Sunday — confirmed independently before this plan was written. Spec §5.2's
  example prompt string ("Sunday 16 August 2026, 23:40, Asia/Jerusalem (UTC+3)") is internally
  consistent and usable verbatim as this slice's test fixture.
- `Australia/Lord_Howe`'s 2026 daylight-saving window runs 2026-10-04 (spring forward) to
  2026-04-05 of the following year (fall back) — F8's own verified table. 2026-08-16 falls
  outside that window in both directions, so at the instant this slice's second `SystemPrompt`
  test fixture uses, the zone sits on its year-round standard offset, `UTC+10:30`, not the
  daylight `UTC+11`.
- `src/Assistant.Impl/Assistant.Impl.csproj:20` already carries
  `<InternalsVisibleTo Include="Assistant.UnitTests" />` — confirmed by reading the file before
  this plan was written. `SystemPrompt` can therefore be `internal sealed`, exactly like
  `LocalTimeResolver` already is, and stay constructible from `Assistant.UnitTests` with no
  csproj change.

---

## Inherited context: the prompt becomes `messages[0]` (governs slices 2 and 3)

The chat-completions format this assistant will speak (OpenRouter, and anything else
OpenAI-compatible — a ruling recorded in slice 1's own inherited-context section, not repeated
here) puts the system prompt in the same array as every other message, tagged by role, rather
than in a separate top-level field the way Anthropic's Messages API shapes it. So
`SystemPrompt.Build()` returns a plain `string` with no notion of role baked in — the wire type
that wraps it as `{"role": "system", "content": ...}` belongs to slice 3, not this one. Recorded
here because it explains why `SystemPrompt` is deliberately thin: a value built once and handed
off, not a message object that owns a role of its own.

Slice 1's own inherited context — vendor-neutral naming, OpenRouter as the default provider —
does not recur here. `SystemPrompt` takes only `ILocalTimeResolver` and never reads `AiSettings`;
which provider eventually answers the call has no bearing on what the clock says, so nothing
about slice 1's provider-routing ruling shapes this slice's design.

---

## Decisions this slice makes

Numbered 1–4 here. The plan these four PRs were split from numbered its full, four-slice
decision set A–O; these four carried letters F, H, I and J there. Renumbered for this standalone
document, since it carries only these four.

### 1. `ILocalTimeResolver` grows two members, and the zone keeps one owner

F8's own "Settled at F8" note deferred exactly this: "No current-local-time member: F9 adds it
when the prompt needs it." This slice adds `DateTimeOffset CurrentLocalTime { get; }` and
`string ZoneId { get; }`. Both live on the resolver, rather than `SystemPrompt` taking an
injected `TimeZoneInfo` and a `TimeProvider` directly, so the zone continues to have exactly one
owner (`ILocalTimeResolver`) and `SystemPrompt` continues to have exactly one collaborator.

### 2. The offset formatter handles half-hour zones

`UTC+3` when the offset's minutes are zero, `UTC+10:30` otherwise. F8's own
`Australia/Lord_Howe` fixture exists precisely because Jerusalem's round-hour offsets cannot
catch a formatter that silently drops a half-hour remainder — the same reasoning F8 gave for
testing its gap and ambiguity rules in two zones, reused here for the same class of bug.

### 3. The system prompt names the configured zone twice, never a literal

Spec §5.2's example sentence reads "All times the user gives are Jerusalem local." That second
mention becomes the configured identifier too, read from `ILocalTimeResolver.ZoneId` exactly like
the first, because a literal there would reintroduce exactly what spec §11's closing note on
timezone references forbids ("No code names a zone literal") — and would do it quietly, since the
first mention (inside "Current time: …") already reads from configuration and would look correct
on a glance that missed the second.

### 4. The prompt's content is a unit-test concern; slice 3's integration test asserts placement, not text

Spec §7.2 forbids duplication between the two suites. This slice's unit test owns the prompt's
content, pinned with `FakeTimeProvider` — asserted with `Assert.Contains` against the
current-time substring rather than the full sentence, because the prompt's trailing instructional
prose is expected to be rewritten during F9b, and an exact-string assertion would break on every
wording change while catching nothing extra. `Assert.Contains` still fails if the clock is wrong,
if the zone is hardcoded rather than read from configuration, or if the half-hour offset
formatting regresses — the three things these tests exist to catch. Slice 3, when it is planned,
is expected to assert only that the system prompt lands as `messages[0]` with `role: "system"` —
never re-checking the prompt's content in either form; that division of ownership is what makes
this slice's `Assert.Contains` assertions the prompt's only test of its content, not merely the
first of two.

---

## What this slice does NOT include

- **A caller for `SystemPrompt`.** Nothing constructs it outside its own test file — no DI
  registration, and `AddAssistantAi`'s body is not touched. The chat client that calls `Build()`
  and sends its text as `messages[0]` is slice 3's job (see "Where this sits," above), which has
  its own plan document not yet written.
- **Any change to `AiSettings`, `AddAssistantAi`, or `Program.cs`.** Slice 1 already shipped these
  (merged as `987ad21`); this slice does not touch them.
- **Any new NuGet package, project reference, or Docker/WireMock change.** This slice touches five
  files, all of them already inside `Assistant.Interfaces`, `Assistant.Impl`, or
  `Assistant.UnitTests`.
- **The prompt's eventual rewrite.** F9b is expected to add tool-calling instructions to the
  prompt's trailing prose. Decision 4, above, is why this slice's tests use `Assert.Contains`
  rather than an exact match, so that future rewrite does not break them.

---

## File Structure

```
src/Assistant.Interfaces/
    ILocalTimeResolver.cs                  + CurrentLocalTime, ZoneId

src/Assistant.Impl/
    Services/LocalTimeResolver.cs          + CurrentLocalTime, ZoneId
    Ai/SystemPrompt.cs                     new

tests/Assistant.UnitTests/
    Services/LocalTimeResolverTests.cs     + CurrentLocalTime, ZoneId tests
    Ai/SystemPromptTests.cs                new
```

---

## Validation

This slice is unit tests only — no Docker, no new NuGet package, nothing to run end to end:

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
```

It cannot be validated by running the app. `SystemPrompt` has no caller until slice 3 builds
`ChatCompletionsClient` and wires it in — this slice adds no DI registration and makes no change
to `Program.cs`. The owner has already accepted this: this slice exists to get the clock and the
prompt text right in isolation, not to prove the app boots (slice 1's own Steps 5–6 already did
that; the app-boots concern returns with slice 3's own validation).

**Test count arithmetic.** Slice 1 left the unit suite at 37, unchanged, because it added no
dedicated test file of its own (its decision 5).
`tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs` carries 15 test cases today — read
and counted before this plan was written: 2 (`Resolve_TimeInEitherSeason_...`, a `[Theory]`) + 1
(`Resolve_AnyTime_ReturnsTheInstantOnUtc`) + 1 (`Resolve_MoreThanAMinuteInThePast_IsRefused`) + 1
(`Resolve_ExactlyOneMinuteInThePast_IsAccepted`) + 1 (`Resolve_MoreThanTwoYearsAhead_IsRefused`) +
1 (`Resolve_ExactlyTwoYearsAhead_IsAccepted`) + 2 (`Resolve_TimeInsideASpringForwardGap_...`, a
`[Theory]`) + 2 (`Resolve_TimeInsideAFallBackHour_...`, a `[Theory]`) + 4
(`Resolve_TimeEitherSideOfAClockChange_IsUnmoved`, a `[Theory]`) = 15. This slice adds:

- 2 cases to that file (`CurrentLocalTime_AnyInstant_CarriesTheZonesOffsetAtThatInstant`,
  `ZoneId_AnyResolver_IsTheConfiguredZonesIdentifier`) → 17.
- 2 cases in the new `tests/Assistant.UnitTests/Ai/SystemPromptTests.cs`
  (`Build_JerusalemInAugust_StatesTheExactCurrentTime`,
  `Build_LordHoweOffsetIsNotARoundHour_RendersTheMinutes`).

37 + 2 + 2 = **41** expected total after this slice.

---

## Steps

**Decisions this slice carries:** 1–4, given in full above.

**Files:**
- Modify: `src/Assistant.Interfaces/ILocalTimeResolver.cs`
- Modify: `src/Assistant.Impl/Services/LocalTimeResolver.cs`
- Modify: `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`
- Create: `src/Assistant.Impl/Ai/SystemPrompt.cs`
- Create: `tests/Assistant.UnitTests/Ai/SystemPromptTests.cs`

**Produces:** `ILocalTimeResolver.CurrentLocalTime`, `ILocalTimeResolver.ZoneId`, and
`internal sealed class SystemPrompt(ILocalTimeResolver clock)` — unregistered and uncalled until
slice 3 gives it a caller.

- [ ] **Step 1: Write the failing tests for the two new resolver members**

Append to `LocalTimeResolverTests`, above the private helpers (the file already has
`ResolverAt`/`ResolverIn`, unchanged by this task):

```csharp
    /// <summary>
    /// When the current instant is read
    /// Then it carries the offset in force in the configured zone at that instant.
    /// </summary>
    [Fact]
    public void CurrentLocalTime_AnyInstant_CarriesTheZonesOffsetAtThatInstant()
    {
        // Arrange
        var resolver = ResolverAt("2026-08-16T20:40:00Z");

        // Act
        var now = resolver.CurrentLocalTime;

        // Assert
        Assert.Equal(Instant("2026-08-16T20:40:00Z"), now);
        Assert.Equal(TimeSpan.FromHours(3), now.Offset);
    }

    /// <summary>
    /// When the zone identifier is read
    /// Then it is the identifier the resolver was constructed with.
    /// </summary>
    [Fact]
    public void ZoneId_AnyResolver_IsTheConfiguredZonesIdentifier()
    {
        // Arrange
        var resolver = ResolverIn("Australia/Lord_Howe", "2026-08-16T20:40:00Z");

        // Act & Assert
        Assert.Equal("Australia/Lord_Howe", resolver.ZoneId);
    }
```

The first test asserts two things on purpose: `DateTimeOffset` equality alone compares points in
time regardless of offset (the same reasoning this file already documents on
`Resolve_AnyTime_ReturnsTheInstantOnUtc`), so without the second assertion a resolver that
returned `now` unconverted, still on UTC's zero offset, would pass the first line and hide the
bug.

- [ ] **Step 2: Run them and watch them fail**

```bash
dotnet test tests/Assistant.UnitTests --filter "FullyQualifiedName~LocalTimeResolverTests"
```

Expected: does not compile. `ILocalTimeResolver` has no `CurrentLocalTime` or `ZoneId` member yet.

- [ ] **Step 3: Add the two members**

In `src/Assistant.Interfaces/ILocalTimeResolver.cs`, add above `Resolve`:

```csharp
    /// <summary>
    /// The current instant, expressed as a wall-clock reading in the configured zone.
    /// </summary>
    /// <value>
    /// Read fresh from the injected clock on every access, so a caller driving a
    /// <c>FakeTimeProvider</c> sees an advance without re-resolving anything.
    /// </value>
    DateTimeOffset CurrentLocalTime { get; }

    /// <summary>
    /// The IANA identifier of the zone every wall-clock time on this assistant is read in.
    /// </summary>
    /// <value>
    /// The same identifier <c>TimeSettings.IanaTimeZone</c> was bound from at startup.
    /// </value>
    string ZoneId { get; }
```

In `src/Assistant.Impl/Services/LocalTimeResolver.cs`, add above `Resolve`:

```csharp
    /// <inheritdoc/>
    public DateTimeOffset CurrentLocalTime =>
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);

    /// <inheritdoc/>
    public string ZoneId => zone.Id;
```

Nothing else in either file changes.

- [ ] **Step 4: Run them and watch them pass**

```bash
dotnet test tests/Assistant.UnitTests --filter "FullyQualifiedName~LocalTimeResolverTests"
```

Expected: 17 passed (the 15 already in the file before this step, plus these 2).

- [ ] **Step 5: Write the failing `SystemPrompt` tests**

Create `tests/Assistant.UnitTests/Ai/SystemPromptTests.cs`:

```csharp
using System.Globalization;
using Assistant.Impl.Ai;
using Assistant.Impl.Services;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.UnitTests.Ai;

/// <summary>
/// Test class for <see cref="SystemPrompt"/>.
/// </summary>
public sealed class SystemPromptTests
{
    /// <summary>
    /// When the prompt is built for a round-hour offset
    /// Then it states the exact current time, the zone, and the offset with no minutes shown.
    /// </summary>
    [Fact]
    public void Build_JerusalemInAugust_StatesTheExactCurrentTime()
    {
        // Arrange
        var prompt = PromptIn("Asia/Jerusalem", "2026-08-16T20:40:00Z");

        // Act
        var text = prompt.Build();

        // Assert
        Assert.Contains("Sunday 16 August 2026, 23:40, Asia/Jerusalem (UTC+3)", text);
    }

    /// <summary>
    /// When the prompt is built for a half-hour offset
    /// Then the offset is rendered with minutes, not rounded away.
    /// </summary>
    /// <remarks>
    /// Lord Howe's one-off half-hour daylight shift runs 2026-10-04 to 2026-04-05 (F8's verified
    /// table). 2026-08-16 falls outside that window, so the zone is on its year-round base
    /// offset, standard time, UTC+10:30 -- not the shifted UTC+11.
    /// </remarks>
    [Fact]
    public void Build_LordHoweOffsetIsNotARoundHour_RendersTheMinutes()
    {
        // Arrange
        var prompt = PromptIn("Australia/Lord_Howe", "2026-08-16T20:40:00Z");

        // Act
        var text = prompt.Build();

        // Assert
        Assert.Contains("Monday 17 August 2026, 07:10, Australia/Lord_Howe (UTC+10:30)", text);
    }

    private static SystemPrompt PromptIn(string zoneId, string utcNow) =>
        new(new LocalTimeResolver(
            TimeZoneInfo.FindSystemTimeZoneById(zoneId),
            new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture))));
}
```

Both assertions use `Assert.Contains` against the "Current time: …, `<zone>` (`<offset>`)"
substring, not `Assert.Equal` against the whole sentence — decision 4, above, gives the
rationale: the prompt's trailing instructional prose ("All times the user gives are … Return
absolute local ISO-8601 datetimes with no offset.") is expected to be rewritten during F9b, and
an exact-string assertion would break on every wording change while catching nothing extra.
`Assert.Contains` still fails if the clock is wrong, the zone is hardcoded, or the half-hour
offset formatting regresses — the three things these tests exist to catch — without coupling the
suite to prose this slice does not own.

Reasoning behind the second fixture's instant, spelled out: 2026-08-16T20:40:00Z is 07:10 local
in `Australia/Lord_Howe` (UTC+10:30 applied to 20:40 rolls past midnight to the next day) —
Monday 17 August, since 16 August 2026 is a Sunday (verified fact, above). Lord Howe's daylight
year runs 2026-10-04 (spring forward) to 2026-04-05 of the following year (fall back), per F8's
own verified table (verified fact, above); 16 August sits in neither direction of that window, so
the zone is on its year-round standard offset, UTC+10:30, not the daylight UTC+11.

- [ ] **Step 6: Run them and watch them fail**

```bash
dotnet test tests/Assistant.UnitTests --filter "FullyQualifiedName~SystemPromptTests"
```

Expected: does not compile. `Assistant.Impl.Ai.SystemPrompt` does not exist.

- [ ] **Step 7: Write `SystemPrompt`**

Create `src/Assistant.Impl/Ai/SystemPrompt.cs`:

```csharp
using System.Globalization;
using Assistant.Interfaces;

namespace Assistant.Impl.Ai;

/// <summary>
/// Builds the system prompt sent as the first message on every call to the chat model.
/// </summary>
/// <param name="clock">Supplies the current time and the zone it is read in.</param>
/// <remarks>
/// The zone is read from <see cref="ILocalTimeResolver.ZoneId"/> rather than named as a literal,
/// and it appears twice in the built text (decision 3, above) so that editing either mention
/// into a hardcoded zone leaves the other visibly disagreeing with it.
/// </remarks>
internal sealed class SystemPrompt(ILocalTimeResolver clock)
{
    /// <summary>
    /// Builds the prompt text for the current instant.
    /// </summary>
    /// <returns>
    /// The current time in the configured zone, that zone's identifier named twice, and the two
    /// instructions the model needs to answer with an absolute local time.
    /// </returns>
    public string Build() =>
        $"Current time: {clock.CurrentLocalTime.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)}, "
        + $"{clock.ZoneId} ({FormatOffset(clock.CurrentLocalTime.Offset)}). "
        + $"All times the user gives are {clock.ZoneId} local. "
        + "Return absolute local ISO-8601 datetimes with no offset.";

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var magnitude = offset.Duration();
        return magnitude.Minutes == 0
            ? $"UTC{sign}{magnitude.Hours}"
            : $"UTC{sign}{magnitude.Hours}:{magnitude.Minutes:00}";
    }
}
```

- [ ] **Step 8: Run them and watch them pass**

```bash
dotnet test tests/Assistant.UnitTests --filter "FullyQualifiedName~SystemPromptTests"
```

Expected: 2 passed.

- [ ] **Step 9: Run the whole unit suite**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
```

Expected: zero warnings, 41 total (the 37 baseline after slice 1, plus the 4 this slice adds —
see "Validation," above, for the arithmetic).

- [ ] **Step 10: Commit**

```bash
git add src/Assistant.Interfaces/ILocalTimeResolver.cs \
        src/Assistant.Impl/Services/LocalTimeResolver.cs \
        src/Assistant.Impl/Ai/SystemPrompt.cs \
        tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs \
        tests/Assistant.UnitTests/Ai/SystemPromptTests.cs
git commit
```

Message:

```
feat: know the current time in the assistant's own zone

ILocalTimeResolver gains CurrentLocalTime and ZoneId -- the member F8's own
"Settled at F8" note deferred until something needed to state "now" in the
user's zone. SystemPrompt is that something: it builds the system prompt
text, naming the configured zone twice rather than once, so a literal
creeping into either mention would leave the two visibly disagreeing.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

---

## Self-review

- [ ] `dotnet build --no-restore` — zero warnings, zero errors
- [ ] `dotnet test tests/Assistant.UnitTests` — 41 passed (37 baseline + 4 this slice adds)
- [ ] No new package reference and no new project reference anywhere
- [ ] `ILocalTimeResolver.CurrentLocalTime` and `ZoneId` both carry a three-line `<summary>` and
      a `<value>` tag; `LocalTimeResolver`'s two implementations both carry `<inheritdoc/>`
- [ ] `SystemPrompt` is `internal sealed`, takes `ILocalTimeResolver` through a primary
      constructor, and is constructible from `Assistant.UnitTests` with no csproj change
      (verified fact, above)
- [ ] `SystemPromptTests` asserts with `Assert.Contains`, never `Assert.Equal` against the whole
      built string
- [ ] Both `SystemPromptTests` fixtures state a current-time substring that matches its zone's
      real offset on 2026-08-16 — `UTC+3` for Jerusalem, `UTC+10:30` for Lord Howe — not a
      rounded or invented one
- [ ] `SystemPrompt` reads `ILocalTimeResolver.ZoneId` for both mentions of the zone in its built
      text; no zone literal appears anywhere in `SystemPrompt.cs`
- [ ] Neither `AiSettings`, `AddAssistantAi`, nor `Program.cs` is touched
- [ ] No DI registration added for `SystemPrompt` — it has no caller until slice 3
- [ ] Every new public member (`ILocalTimeResolver.CurrentLocalTime`,
      `ILocalTimeResolver.ZoneId`) and the new internal type (`SystemPrompt`, its constructor
      parameter, `Build`) carries a three-line `<summary>`
- [ ] No emoji in any changed file, including the commit message
- [ ] Diff comfortably under the 1000-line PR budget (~120 lines estimated by the rejected
      monolith this plan was split from)
