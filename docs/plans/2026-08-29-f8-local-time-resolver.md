# F8 — Resolve local time

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** `ILocalTimeResolver.Resolve` — the one place that turns the wall-clock time a user
means into the instant the assistant stores — with the four guards spec §5.4 puts on it.

**Tech Stack:** .NET 10, xUnit 2.9.3, `Microsoft.Extensions.TimeProvider.Testing`. No new
packages. No Docker: every test in this feature is a unit test.

**Spec:** `docs/design/slice-1-reminders.md` §5.4 (the time contract), §3.4 (where the class
lives), §3.6 (seams), §11.4 (the zone is never a literal), §12.1 (documentation).
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F8.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error everywhere.
- **Every class taking arguments uses a primary constructor.** No separate constructors.
- **CS9113 is an error**: a primary-constructor parameter nothing references fails the build.
  Do not declare a parameter one task ahead of the task that uses it.
- Every enum's first member is `Unknown`, with no explicit numeric values. New members are
  **appended**, never inserted.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- Central package management; no inline `Version=` (NU1008).
- No emoji anywhere: source, tests, docs, or commit messages.
- **Never run `docker compose down -v`.** This feature needs no containers at all.
- PR budget: 1000 changed lines excluding this plan. Estimated ~420.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
  ```

---

## Verified facts this plan rests on

Every constant below was read out of the system tzdata before this plan was written, not
recalled. .NET on Linux and macOS reads the same database. **Do not adjust a constant to make a
test pass** — if one disagrees with .NET, stop and report it; a disagreement means the machine's
tzdata differs from the plan's, which is a finding, not a typo.

`Asia/Jerusalem`, 2026:

| Wall clock (local) | UTC offset | Instant |
| :--- | :--- | :--- |
| `2026-01-15T10:00:00` | +02:00 | `2026-01-15T08:00:00Z` |
| `2026-03-27T01:30:00` | +02:00 | `2026-03-26T23:30:00Z` |
| `2026-03-27T02:30:00` | **does not exist** | — |
| `2026-03-27T03:30:00` | +03:00 | `2026-03-27T00:30:00Z` |
| `2026-08-17T10:00:00` | +03:00 | `2026-08-17T07:00:00Z` |
| `2026-10-25T00:30:00` | +03:00 | `2026-10-24T21:30:00Z` |
| `2026-10-25T01:30:00` | **happens twice**: +03:00, then +02:00 | `…T22:30:00Z` / `…T23:30:00Z` |
| `2026-10-25T02:30:00` | +02:00 | `2026-10-25T00:30:00Z` |

- Spring forward: 2026-03-27, local **02:00 becomes 03:00**. The gap is one hour wide.
- Fall back: 2026-10-25, local **02:00 becomes 01:00**. The 01:00 hour happens twice.
- Both 2026 transitions are months apart, and both are hours long, not days.

`Australia/Lord_Howe`, 2026 — a zone whose gap is **thirty minutes**, not an hour:

| Wall clock (local) | UTC offset | Instant |
| :--- | :--- | :--- |
| `2026-10-04T02:15:00` | **does not exist** | — |
| `2026-04-05T01:45:00` | **happens twice**: +11:00, then +10:30 | `…T14:45:00Z` / `…T15:15:00Z` |

- Spring forward: 2026-10-04, local **02:00 becomes 02:30**. Half an hour wide.
- Fall back: 2026-04-05, local **01:30 becomes 01:00**.

**What a .NET probe showed**, run against this tzdata before the plan was finished:

- `GetUtcOffset` for a time **inside a gap** returns the offset in force *before* the
  transition: `+02:00` for Jerusalem, `+10:30` for Lord Howe. Building the instant from that
  offset names exactly the same moment as shifting the reading past the gap and using the
  offset after it, because `(L + D) - (o + D)` and `L - o` are the same instant for any gap
  width `D`. Verified equal in both zones. **The spring-forward rule needs no code.**
- `GetUtcOffset` for an **ambiguous** time returns the *smaller* offset, which is the second
  occurrence: `+02:00` for Jerusalem, `+10:30` for Lord Howe. Wrong in both.
  **The fall-back rule does need code.**
- A one-hour gap shift, hardcoded, gives the right answer in Jerusalem and the wrong one in
  Lord Howe — `2026-10-03T16:15:00Z` instead of `15:45:00Z`.
- `DateTime.Parse("2026-08-17T10:00:00", CultureInfo.InvariantCulture).Kind` is `Unspecified`.
- Both zone identifiers resolve by their IANA names on this platform.
- The finished algorithm — the one Task 2 Step 4 arrives at, with no gap branch — was compiled
  under `Nullable=enable` and `TreatWarningsAsErrors=true` and run against **every constant in
  both tables above and all four guard boundaries**. All fourteen agreed. `Result<T>` as Task 1
  Step 1 writes it compiles clean: an unconstrained `T?` needs no constraint and raises no
  warning. So a failing test in this feature means the code disagrees with the plan, not that
  the plan's arithmetic is wrong.

Facts about the code this plan touches:

- `src/Assistant.Impl/Assistant.Impl.csproj` **already** carries
  `<InternalsVisibleTo Include="Assistant.UnitTests" />`. `LocalTimeResolver` can be `internal
  sealed` like every other service in `Impl`, and the unit tests can still construct it. Do not
  add a second `InternalsVisibleTo`, and do not make the class public to get at it.
- `Assistant.UnitTests` already references `Microsoft.Extensions.TimeProvider.Testing`,
  `Microsoft.Extensions.Configuration`, `Assistant.Impl` and `Assistant.Contracts`. No project or
  package reference changes anywhere in this feature.
- `AddAssistantServices` already registers `TimeProvider.System` as a singleton.
- `Assistant.Interfaces` already references `Assistant.Contracts` — `ITaskService` returns
  `Result` today.
- `ConfigurationExtensions.Read<T>` binds the section **named after the type**, so `TimeSettings`
  reads the `TimeSettings` section. It throws when the section is absent, so the default must
  ship in `appsettings.json`.

---

## Decisions this plan makes — review these first

### A. `Result<T>` joins `Result` in Contracts

`ILocalTimeResolver` has to return either an instant or a reason, and the codebase already
routes refusals through `Result` and `ErrorCode`. `Result<T>` is the smallest generalisation of
what is already there, and F8 exercises both of its arms with tests, which is what the backlog's
YAGNI rule asks of a new contract type.

Rejected: throwing for the two guard failures. A due time a minute in the past is an ordinary
outcome of ordinary user input — the assistant is meant to ask a question about it, not to
unwind a stack.

### B. The resolver takes a `DateTime`, not the model's ISO string

The model returns `2026-08-17T10:00:00`. `System.Text.Json` already turns exactly that into a
`DateTime` with `Kind == Unspecified`, so F9's `CreateTaskRequest` gets the parse for free and
this feature stays a time-zone concern with no parse error code and no test surface duplicating
the deserialiser.

The parameter is documented as a wall-clock reading: **any** `DateTimeKind` is treated as a
reading of the clock on the wall in the configured zone. This is why Step 1 of the algorithm
forces the kind to `Unspecified` — `TimeZoneInfo` throws on a `DateTime` whose kind contradicts
the zone it is being converted against, and a caller that hands over `DateTime.UtcNow` deserves
a defined answer rather than an exception.

### C. `ILocalTimeResolver` exists even though it has one implementation

The backlog names it (`ILocalTimeResolver` + `LocalTimeResolver`), and every service this
project registers already sits behind an interface with exactly one implementation —
`ITaskService`/`TaskService`, `INotifier`/`TelegramNotifier`. The backlog's "an abstraction with
one implementation is a guess" rule is aimed at speculative seams; this one is mandated by the
feature's own entry and consumed by F9 and F10.

The real payment comes at F9: the agent path is unit-testable against a resolver that says
"refused" without arranging a fake clock at every call site.

### D. The guards live in the resolver, not in "the service"

Spec §5.4 says "the service applies guard clauses". The backlog's F8 entry says the guards are
part of F8, and at F8 there is no capture service — `ITaskService` has no `CreateAsync` until
F10. Putting the guards in the resolver keeps the whole §5.4 time contract in one testable
place and keeps F8 from growing a service method nothing calls.

**Ruling:** guards go in the resolver. Task 4 corrects §5.4 so the spec stops describing a
split that does not exist.

### E. Order of operations: pick the offset, then judge the instant

The guards judge an instant, so the offset has to be settled first. A reading in the fall-back
hour names two different instants an hour apart, and near a boundary that is the difference
between "a minute in the past" and "an hour in the future" — the resolver must not answer that
question before it has decided which of the two the user meant.

Nothing else needs ordering, because there is nothing else: Decision F removes the only other
step this method might have had.

### F. The spring-forward gap needs no code, and that is verified rather than assumed

§5.4 requires a time in the gap to "shift forward past the gap", and the obvious implementation
is an `IsInvalidTime` branch that widens the reading by the gap's width. The probe above shows
that branch cannot change any answer: `GetUtcOffset` returns the pre-transition offset for a
time inside a gap, and `(L + D) - (o + D)` is the same instant as `L - o` whatever `D` is.
Confirmed in Israel's one-hour gap and Lord Howe's thirty-minute one.

The branch is four lines that provably do nothing, and the backlog's YAGNI rule admits nothing
that no test needs. **Ruling: no gap branch.** The rule is not dropped — it is tested, in two
zones with different gap widths, and Task 2 Step 5 proves those tests are the only thing
holding it.

The counter-argument, recorded because it is not weak: Decision G establishes that .NET's
default is *not* trustworthy here, so leaning on a different default for the gap is
inconsistent. What settles it is that the gap identity is arithmetic — there is no second
answer .NET could reasonably give — while the ambiguity default is a policy choice, and .NET
chose the opposite of what §5.4 asks for.

### G. The ambiguous hour is resolved explicitly, because the default is wrong

`TimeZoneInfo.ConvertTimeToUtc` resolves an ambiguous time to **standard** time — the *second*
occurrence. §5.4 requires the first. This is why the implementation never calls
`ConvertTimeToUtc` at all: it selects the offset itself and constructs the `DateTimeOffset`
from it, which makes the choice visible in the code instead of relying on a default that says
the opposite of what the spec asks for.

`GetAmbiguousTimeOffsets(...).Max()` is the first occurrence in every zone: falling back always
lowers the offset, so the larger of the two is always the earlier instant.

`GetUtcOffset` makes the same wrong choice as `ConvertTimeToUtc` — the probe returned `+02:00`
for Jerusalem's repeated hour and `+10:30` for Lord Howe's, the second occurrence each time. So
neither of the two obvious calls can be left to its default.

### H. The zone is configuration with a shipped default, and it is validated at startup

Spec §11.4: "No code names a zone literal; it is bound from configuration and injected into
`LocalTimeResolver`." So `TimeSettings.IanaTimeZone` is read through the existing
`Read<T>` path, and `appsettings.json` carries `Asia/Jerusalem` as the default so a fresh clone
runs without configuring anything.

`Validate()` resolves the identifier and rethrows as `ConfigurationErrorsException`. A typo then
stops the host while it is composing, naming the setting — instead of throwing
`TimeZoneNotFoundException` from inside the first captured task, hours later, in a log nobody is
reading.

**This contradicts spec §2 and §12.7, which both call the zone fixed for slice 1.** §11.4 is
explicit that it governs "every mention of Jerusalem below and above", and §5.4 independently
says the resolver "takes the configured IANA zone, never a hardcoded one". Two sections against
one, and the two agree with each other in detail.

**Ruling:** implement §5.4 and §11.4. One zone for the whole bot, bound from configuration,
defaulting to `Asia/Jerusalem`. *Per-user* zones stay deferred, which is what §12.7's trigger
("a second user") was actually about. Task 4 corrects §2's row and §12.7's entry so the spec
says one thing.

### I. `AddAssistantTime` is a new extension method, not a parameter on `AddAssistantServices`

Adding a `TimeSettings` parameter to `AddAssistantServices` would change a signature every
caller already uses. `AddAssistantTelegram(settings)` is the established shape for a
registration that needs configuration. `AddAssistantTime` follows it.

It registers the resolved `TimeZoneInfo` as a singleton beside the existing `TimeProvider`, so
the resolver takes two framework abstractions and no settings object, and the zone identifier is
resolved once, at composition, by the same code path that validated it.

### J. No integration tests

Spec §7.2: unit tests cover only what integration does not already reach. This feature touches
no database, no HTTP, and no container. Its entire surface is a pure function of a wall clock, a
zone and a clock reading, and all three are injectable. An integration test here would start
Postgres to test arithmetic.

---

## What F8 does NOT include

- **No current-local-time member.** F9's system prompt needs "Current time: … Asia/Jerusalem
  (UTC+3)". Nothing needs it today, and an interface member no test exercises is exactly what
  the backlog's YAGNI rule forbids. F9 adds it to `ILocalTimeResolver`.
- **No `CreateTaskRequest`,** no `Contracts` type beyond `Result<T>`. F9.
- **No `ITaskService` change.** F10 is the first feature that stores a captured task.
- **No `ReminderTask.Notes`.** F10.
- **No change to the prompt, the listener, the scheduler, or any job.** Nothing calls the
  resolver yet; F9 is the first caller. This is deliberate — the feature is a self-contained
  unit with its own tests, exactly as the backlog splits it.

---

## File Structure

```
src/Assistant.Contracts/
    Result.cs                             + Result<T>
    ErrorCode.cs                          + DueTimeInPast, DueTimeTooFarAhead

src/Assistant.Interfaces/
    ILocalTimeResolver.cs                 new

src/Assistant.Impl/
    Services/LocalTimeResolver.cs         new
    Settings/TimeSettings.cs              new
    ImplServiceCollectionExtensions.cs    + AddAssistantTime

src/Assistant.Worker/
    Program.cs                            + AddAssistantTime(...)
    appsettings.json                      + TimeSettings section

tests/Assistant.UnitTests/
    Services/LocalTimeResolverTests.cs    new
    Configuration/TimeSettingsTests.cs    new

.env.example                              + the zone override
README.md                                 the "one timezone" limitation is now configured
docs/design/slice-1-reminders.md          §2, §5.4, §12.7 corrected
docs/design/2026-08-22-slice-1-feature-backlog.md
                                          F8 done, and what it settled
```

Branch: `feature/f8-local-time-resolver`, cut from `main`.

---

## Task 1: A local time becomes an instant, or the assistant asks

The conversion and both guards. The fall-back hour is Task 2 — this task deliberately resolves
it to the wrong occurrence, and Task 2's tests are what make it right. The spring-forward gap is
already correct here, for the reason Decision F gives.

**Files:**
- Modify: `src/Assistant.Contracts/Result.cs`
- Modify: `src/Assistant.Contracts/ErrorCode.cs`
- Create: `src/Assistant.Interfaces/ILocalTimeResolver.cs`
- Create: `src/Assistant.Impl/Services/LocalTimeResolver.cs`
- Create: `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`

**Produces:** `Result<T>`, `ErrorCode.DueTimeInPast`, `ErrorCode.DueTimeTooFarAhead`,
`ILocalTimeResolver.Resolve(DateTime) -> Result<DateTimeOffset>`, and
`internal sealed class LocalTimeResolver(TimeZoneInfo zone, TimeProvider timeProvider)`.

- [ ] **Step 1: Add `Result<T>` beside `Result`**

Append to `src/Assistant.Contracts/Result.cs`, below the existing `Result`:

```csharp
/// <summary>
/// The outcome of an operation that either produces a value or is refused for a stated reason.
/// </summary>
/// <typeparam name="T">What a successful operation produces.</typeparam>
/// <param name="Value">
/// The value produced, or the default when the operation was refused.
/// </param>
/// <param name="Error">
/// The reason it was refused, or <see langword="null"/> when it succeeded.
/// </param>
/// <remarks>
/// The non-generic <see cref="Result"/> stays: most operations in this project succeed without
/// producing anything, and giving them a meaningless type argument would read worse than having
/// two types.
/// </remarks>
public readonly record struct Result<T>(T? Value, ErrorCode? Error)
{
    /// <summary>
    /// Whether the operation was carried out.
    /// </summary>
    public bool IsSuccess => Error is null;

    /// <summary>
    /// The outcome of an operation that produced a value.
    /// </summary>
    /// <param name="value">What it produced.</param>
    /// <returns>A successful result carrying the value.</returns>
    public static Result<T> Success(T value) => new(value, null);

    /// <summary>
    /// The outcome of an operation that was refused.
    /// </summary>
    /// <param name="error">Why it was refused.</param>
    /// <returns>A failed result carrying the reason and no value.</returns>
    public static Result<T> Failure(ErrorCode error) => new(default, error);
}
```

- [ ] **Step 2: Append the two error codes**

At the **end** of the `ErrorCode` enum in `src/Assistant.Contracts/ErrorCode.cs`, after
`DueTimeMissing`. Appending keeps every existing member's numeric value unchanged.

```csharp
    /// <summary>
    /// The requested time is more than a minute in the past.
    /// </summary>
    DueTimeInPast,

    /// <summary>
    /// The requested time is more than two years ahead, which is far more likely a misread
    /// year than a real intention.
    /// </summary>
    DueTimeTooFarAhead,
```

- [ ] **Step 3: Write the interface**

Create `src/Assistant.Interfaces/ILocalTimeResolver.cs`:

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// Turns a wall-clock time in the assistant's configured zone into the instant it names.
/// </summary>
/// <remarks>
/// The model returns absolute local times with no offset (spec §5.4), so something has to say
/// which zone they belong to and what to do when the wall clock is not a reliable guide: the
/// hour that does not exist on a spring-forward night, the hour that happens twice on a
/// fall-back night, and times so far from now that the model has most likely misread the date.
/// </remarks>
public interface ILocalTimeResolver
{
    /// <summary>
    /// Resolves a wall-clock time in the configured zone to the instant it names.
    /// </summary>
    /// <param name="local">
    /// The date and time as the user means it. Any <see cref="DateTimeKind"/> is read as a
    /// wall-clock time in the configured zone, never as an instant.
    /// </param>
    /// <returns>
    /// The instant, on UTC with a zero offset, or the reason it was refused:
    /// <see cref="ErrorCode.DueTimeInPast"/> more than a minute before now, and
    /// <see cref="ErrorCode.DueTimeTooFarAhead"/> more than two years after it. A time in a
    /// spring-forward gap resolves to the same wall-clock reading past the gap; a time in a
    /// fall-back hour resolves to the first of its two occurrences.
    /// </returns>
    Result<DateTimeOffset> Resolve(DateTime local);
}
```

- [ ] **Step 4: Write the failing tests**

Create `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`:

```csharp
using System.Globalization;
using Assistant.Contracts;
using Assistant.Impl.Services;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.UnitTests.Services;

/// <summary>
/// Test class for <see cref="LocalTimeResolver"/>.
/// </summary>
public sealed class LocalTimeResolverTests
{
    private static readonly TimeZoneInfo Jerusalem =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    /// <summary>
    /// When a due time names a reading of the wall clock in the configured zone
    /// And it is resolved
    /// Then the instant carries the offset in force on that date, summer or winter.
    /// </summary>
    /// <param name="local">The wall-clock reading the user meant.</param>
    /// <param name="expectedUtc">The instant it names.</param>
    [Theory]
    [InlineData("2026-08-17T10:00:00", "2026-08-17T07:00:00Z")]
    [InlineData("2026-01-15T10:00:00", "2026-01-15T08:00:00Z")]
    public void Resolve_TimeInEitherSeason_ReturnsTheInstantThatReadingNames(
        string local, string expectedUtc)
    {
        // Arrange
        var resolver = ResolverAt("2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(Wall(local));

        // Assert
        Assert.Equal(Instant(expectedUtc), result.Value);
    }

    /// <summary>
    /// When a time is resolved
    /// Then it comes back on UTC rather than on the zone's own offset.
    /// </summary>
    /// <remarks>
    /// Every instant this project stores is UTC with a zero offset, and
    /// <see cref="DateTimeOffset"/> equality compares points in time regardless of offset — so
    /// without this, no other assertion in the file would notice the offset drifting.
    /// </remarks>
    [Fact]
    public void Resolve_AnyTime_ReturnsTheInstantOnUtc()
    {
        // Arrange
        var resolver = ResolverAt("2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(Wall("2026-08-17T10:00:00"));

        // Assert
        Assert.Equal(TimeSpan.Zero, result.Value.Offset);
    }

    /// <summary>
    /// When the time given has already passed by more than a minute
    /// And it is resolved
    /// Then it is refused, so the assistant can ask instead of reminding at once.
    /// </summary>
    [Fact]
    public void Resolve_MoreThanAMinuteInThePast_IsRefused()
    {
        // Arrange
        var resolver = ResolverAt("2026-08-17T07:00:00Z");

        // Act
        var result = resolver.Resolve(Wall("2026-08-17T09:58:00"));

        // Assert
        Assert.Equal(ErrorCode.DueTimeInPast, result.Error);
    }

    /// <summary>
    /// When the time given is exactly one minute old
    /// And it is resolved
    /// Then it is accepted, because only more than a minute is refused.
    /// </summary>
    [Fact]
    public void Resolve_ExactlyOneMinuteInThePast_IsAccepted()
    {
        // Arrange
        var resolver = ResolverAt("2026-08-17T07:00:00Z");

        // Act
        var result = resolver.Resolve(Wall("2026-08-17T09:59:00"));

        // Assert
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// When the time given is more than two years ahead
    /// And it is resolved
    /// Then it is refused, because a misread year is likelier than the intention.
    /// </summary>
    [Fact]
    public void Resolve_MoreThanTwoYearsAhead_IsRefused()
    {
        // Arrange
        var resolver = ResolverAt("2026-08-17T07:00:00Z");

        // Act
        var result = resolver.Resolve(Wall("2028-08-17T10:01:00"));

        // Assert
        Assert.Equal(ErrorCode.DueTimeTooFarAhead, result.Error);
    }

    /// <summary>
    /// When the time given is exactly two years ahead
    /// And it is resolved
    /// Then it is accepted, because only more than two years is refused.
    /// </summary>
    [Fact]
    public void Resolve_ExactlyTwoYearsAhead_IsAccepted()
    {
        // Arrange
        var resolver = ResolverAt("2026-08-17T07:00:00Z");

        // Act
        var result = resolver.Resolve(Wall("2028-08-17T10:00:00"));

        // Assert
        Assert.True(result.IsSuccess);
    }

    private static LocalTimeResolver ResolverAt(string utcNow) =>
        new(Jerusalem, new FakeTimeProvider(Instant(utcNow)));

    private static DateTime Wall(string local) =>
        DateTime.Parse(local, CultureInfo.InvariantCulture);

    private static DateTimeOffset Instant(string utc) =>
        DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);
}
```

Two things to check while writing this, both of which have bitten this repository before:

- `DateTime.Parse("2026-08-17T10:00:00", CultureInfo.InvariantCulture)` must produce
  `DateTimeKind.Unspecified`. It does. If a future edit adds `DateTimeStyles.AssumeUniversal`
  or similar, every conversion test starts lying.
- The `2028-08-17T10:01` row depends on August being daylight time (+03:00) in 2028 as it is in
  2026. Israel's rule has been stable since 2013. If .NET disagrees, report it rather than
  editing the constant.

- [ ] **Step 5: Run them and watch them fail for the right reason**

```bash
dotnet test tests/Assistant.UnitTests --filter "FullyQualifiedName~LocalTimeResolverTests"
```

Expected: it does not compile — `LocalTimeResolver` does not exist. That is the correct
failure. Do not proceed on a filter that matched nothing: a `--filter` naming a class that does
not exist **exits 0**, which is not a passing test, and this repository has already been caught
by that once.

- [ ] **Step 6: Write the resolver**

Create `src/Assistant.Impl/Services/LocalTimeResolver.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services;

/// <summary>
/// Resolves wall-clock times against the single zone the assistant is configured for.
/// </summary>
/// <param name="zone">The zone every wall-clock time is read in.</param>
/// <param name="timeProvider">The clock the past and future guards judge against.</param>
internal sealed class LocalTimeResolver(TimeZoneInfo zone, TimeProvider timeProvider)
    : ILocalTimeResolver
{
    private static readonly TimeSpan PastTolerance = TimeSpan.FromMinutes(1);

    private const int MaxYearsAhead = 2;

    /// <inheritdoc/>
    public Result<DateTimeOffset> Resolve(DateTime local)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var instant = new DateTimeOffset(wall, zone.GetUtcOffset(wall)).ToUniversalTime();
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
}
```

`SpecifyKind` is what lets the parameter accept any kind: `TimeZoneInfo` rejects a `DateTime`
whose kind contradicts the zone, and a caller passing `DateTime.UtcNow` should get the
documented answer, not an exception.

- [ ] **Step 7: Run them and watch them pass**

```bash
dotnet test tests/Assistant.UnitTests --filter "FullyQualifiedName~LocalTimeResolverTests"
```

Expected: 7 passed (2 theory rows plus 5 facts). If the count is lower, the filter is wrong.

- [ ] **Step 8: Run the whole unit suite**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
```

Expected: zero warnings, and every previously green test still green. `ConventionTests`
inspects `Assistant.Contracts` by reflection, so `Result<T>` and the two new `ErrorCode`
members are checked automatically by rules that already exist.

- [ ] **Step 9: Commit**

```bash
git add src/Assistant.Contracts src/Assistant.Interfaces/ILocalTimeResolver.cs \
        src/Assistant.Impl/Services/LocalTimeResolver.cs \
        tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs
git commit
```

Message:

```
feat: resolve a local wall-clock time to the instant it names

Adds Result<T> beside the existing Result, ILocalTimeResolver, and the two
guards spec 5.4 puts on a due time: more than a minute in the past and more
than two years ahead are refused so the assistant can ask, rather than stored
so it can surprise someone.

The clock-change edges are not handled yet.
```

---

## Task 2: A clock change does not move a reminder to the wrong hour

**Files:**
- Modify: `src/Assistant.Impl/Services/LocalTimeResolver.cs`
- Modify: `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`

**Consumes:** everything Task 1 produced. **Produces:** nothing new; `Resolve` gains its last
behaviour.

**Read Decision F before starting.** One of §5.4's two clock-change rules is already satisfied
by Task 1's code and needs tests but no implementation; the other needs both. A task report that
does not say which was which has not done this task.

- [ ] **Step 1: Let a test name its zone**

The tests pin Jerusalem in a field today. Replace that field and the `ResolverAt` helper with:

```csharp
    private static LocalTimeResolver ResolverIn(string zoneId, string utcNow) =>
        new(TimeZoneInfo.FindSystemTimeZoneById(zoneId), new FakeTimeProvider(Instant(utcNow)));

    private static LocalTimeResolver ResolverAt(string utcNow) =>
        ResolverIn("Asia/Jerusalem", utcNow);
```

Every test Task 1 wrote keeps calling `ResolverAt` and keeps passing, untouched.

- [ ] **Step 2: Write the failing tests**

Add to `LocalTimeResolverTests`, above the private helpers:

```csharp
    /// <summary>
    /// When the time given falls in the hour a spring-forward night skips
    /// And it is resolved
    /// Then it names the instant that same reading names past the gap.
    /// </summary>
    /// <param name="zoneId">The zone whose clocks move.</param>
    /// <param name="local">A reading inside the gap.</param>
    /// <param name="expectedUtc">The instant it names.</param>
    /// <remarks>
    /// Lord Howe Island is here because its gap is half an hour wide. Israel's is a full hour,
    /// which is the same width as the offset change, so an implementation that confuses the two
    /// cannot be caught in Israel alone.
    /// </remarks>
    [Theory]
    [InlineData("Asia/Jerusalem", "2026-03-27T02:30:00", "2026-03-27T00:30:00Z")]
    [InlineData("Australia/Lord_Howe", "2026-10-04T02:15:00", "2026-10-03T15:45:00Z")]
    public void Resolve_TimeInsideASpringForwardGap_NamesTheInstantPastTheGap(
        string zoneId, string local, string expectedUtc)
    {
        // Arrange
        var resolver = ResolverIn(zoneId, "2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(Wall(local));

        // Assert
        Assert.Equal(Instant(expectedUtc), result.Value);
    }

    /// <summary>
    /// When the time given falls in the hour a fall-back night repeats
    /// And it is resolved
    /// Then it lands on the first of the two occurrences.
    /// </summary>
    /// <param name="zoneId">The zone whose clocks move.</param>
    /// <param name="local">A reading inside the repeated hour.</param>
    /// <param name="expectedUtc">The instant of its first occurrence.</param>
    [Theory]
    [InlineData("Asia/Jerusalem", "2026-10-25T01:30:00", "2026-10-24T22:30:00Z")]
    [InlineData("Australia/Lord_Howe", "2026-04-05T01:45:00", "2026-04-04T14:45:00Z")]
    public void Resolve_TimeInsideAFallBackHour_TakesTheFirstOccurrence(
        string zoneId, string local, string expectedUtc)
    {
        // Arrange
        var resolver = ResolverIn(zoneId, "2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(Wall(local));

        // Assert
        Assert.Equal(Instant(expectedUtc), result.Value);
    }

    /// <summary>
    /// When the time given sits either side of a clock change without touching it
    /// And it is resolved
    /// Then it is left exactly where it is.
    /// </summary>
    /// <param name="local">A reading just outside a transition.</param>
    /// <param name="expectedUtc">The instant it names.</param>
    [Theory]
    [InlineData("2026-03-27T01:30:00", "2026-03-26T23:30:00Z")]
    [InlineData("2026-03-27T03:30:00", "2026-03-27T00:30:00Z")]
    [InlineData("2026-10-25T00:30:00", "2026-10-24T21:30:00Z")]
    [InlineData("2026-10-25T02:30:00", "2026-10-25T00:30:00Z")]
    public void Resolve_TimeEitherSideOfAClockChange_IsUnmoved(
        string local, string expectedUtc)
    {
        // Arrange
        var resolver = ResolverAt("2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(Wall(local));

        // Assert
        Assert.Equal(Instant(expectedUtc), result.Value);
    }
```

The third test is the one that stops "handle the clock change" from becoming "move everything in
March". Without it, an implementation that shifts every time in the transition month would still
pass the first two.

- [ ] **Step 3: Run them and watch exactly the right ones fail**

```bash
dotnet test tests/Assistant.UnitTests --filter "FullyQualifiedName~LocalTimeResolverTests"
```

Expected, precisely:

- `Resolve_TimeInsideASpringForwardGap_NamesTheInstantPastTheGap` — **both rows pass already.**
  Decision F explains why. **If either row fails, Decision F is wrong: stop and report it. Do
  not add a branch to make it pass.**
- `Resolve_TimeInsideAFallBackHour_TakesTheFirstOccurrence` — **both rows fail.** Jerusalem
  gives `2026-10-24T23:30:00Z` and Lord Howe gives `2026-04-04T15:15:00Z`: the second
  occurrence in each.
- `Resolve_TimeEitherSideOfAClockChange_IsUnmoved` — all four rows pass already.

Two failures, six passes, nothing else. Any other shape means something in Task 1 is wrong.

- [ ] **Step 4: Choose the first occurrence**

In `Resolve`, replace this line:

```csharp
        var instant = new DateTimeOffset(wall, zone.GetUtcOffset(wall)).ToUniversalTime();
```

with:

```csharp
        // Falling back always lowers the offset, so the larger of an ambiguous reading's two
        // offsets is its first occurrence. GetUtcOffset and ConvertTimeToUtc both hand back the
        // second. A reading inside a spring-forward gap needs no such handling: GetUtcOffset
        // returns the offset in force before the gap, which names the same instant as the same
        // reading past it, whatever the gap's width.
        var offset = zone.IsAmbiguousTime(wall)
            ? zone.GetAmbiguousTimeOffsets(wall).Max()
            : zone.GetUtcOffset(wall);

        var instant = new DateTimeOffset(wall, offset).ToUniversalTime();
```

Nothing else in the method changes.

- [ ] **Step 5: Prove each test is held by the line that claims to hold it**

Three scratch checks. **None of them is committed**; every one is reverted before Step 6.

1. Put `zone.GetUtcOffset(wall)` back in place of the conditional. Expected: the two fall-back
   rows fail, everything else passes. This is what makes the new line load-bearing rather than
   decorative. Restore it.
2. Add the gap branch Decision F rejected, in the form a reader would most likely reach for:
   `if (zone.IsInvalidTime(wall)) { wall += TimeSpan.FromHours(1); }` at the top of the method.
   Expected: the **Lord Howe** gap row fails with `2026-10-03T16:15:00Z`, and the Jerusalem gap
   row still passes. This is the check that shows why a second zone is in the file at all.
   Remove it.
3. Add the measured form instead:
   `if (zone.IsInvalidTime(wall)) { wall += zone.GetUtcOffset(wall.AddDays(1)) - zone.GetUtcOffset(wall.AddDays(-1)); }`.
   Expected: **every test passes.** That is the evidence the branch changes no answer, which is
   the whole of Decision F. Remove it.

Record all three outcomes in the task report, each as what you expected and what you saw. If
check 3 changes any result, Decision F is wrong and that is the finding — report it rather than
keeping the branch quietly.

- [ ] **Step 6: Run the whole unit suite**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
```

Expected: zero warnings, and 15 test cases in `LocalTimeResolverTests` — 5 facts and 10 theory
rows (2 seasons, 2 gaps, 2 fall-back hours, 4 either side of a change).

- [ ] **Step 7: Commit**

```bash
git add src/Assistant.Impl/Services/LocalTimeResolver.cs \
        tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs
git commit
```

Message:

```
feat: resolve the fall-back hour to its first occurrence

GetUtcOffset and ConvertTimeToUtc both resolve an ambiguous local time to
standard time, which is its second occurrence. Spec 5.4 asks for the first,
so the offset is chosen here rather than defaulted.

The spring-forward gap needs no code: GetUtcOffset returns the offset in
force before the gap, which names the same instant as the same reading past
the gap, for any gap width. Tests in two zones -- one with an hour-wide gap,
one with a half-hour one -- are what hold that.
```

---

## Task 3: The zone comes from configuration

**Files:**
- Create: `src/Assistant.Impl/Settings/TimeSettings.cs`
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`
- Modify: `src/Assistant.Worker/Program.cs`
- Modify: `src/Assistant.Worker/appsettings.json`
- Modify: `.env.example`
- Create: `tests/Assistant.UnitTests/Configuration/TimeSettingsTests.cs`

**Consumes:** `LocalTimeResolver` from Tasks 1 and 2. **Produces:** `TimeSettings`,
`AddAssistantTime`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Assistant.UnitTests/Configuration/TimeSettingsTests.cs`:

```csharp
using System.Configuration;
using Assistant.Impl.Configuration;
using Assistant.Impl.Settings;
using Microsoft.Extensions.Configuration;

namespace Assistant.UnitTests.Configuration;

/// <summary>
/// Test class for <see cref="TimeSettings"/>.
/// </summary>
public sealed class TimeSettingsTests
{
    /// <summary>
    /// When the configured zone is not an identifier this machine knows
    /// And configuration is read
    /// Then startup fails, naming the value that was wrong.
    /// </summary>
    [Fact]
    public void Read_ZoneIsNotAKnownIdentifier_Throws()
    {
        // Arrange
        var configuration = BuildConfiguration("Asia/Jerusalum");

        // Act
        var exception = Record.Exception(() => configuration.Read<TimeSettings>());

        // Assert
        var error = Assert.IsType<ConfigurationErrorsException>(exception);
        Assert.Contains("Asia/Jerusalum", error.Message);
    }

    /// <summary>
    /// When the configured zone is the one the repository ships as its default
    /// And configuration is read
    /// Then it is accepted.
    /// </summary>
    [Fact]
    public void Read_ZoneIsAKnownIdentifier_ReturnsSettings()
    {
        // Arrange
        var configuration = BuildConfiguration("Asia/Jerusalem");

        // Act
        var settings = configuration.Read<TimeSettings>();

        // Assert
        Assert.Equal("Asia/Jerusalem", settings.IanaTimeZone);
    }

    private static IConfiguration BuildConfiguration(string zone) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TimeSettings:IanaTimeZone"] = zone,
            })
            .Build();
}
```

The second test is not ceremony: it is the only thing standing between a `Validate` that rejects
everything and a host that will not start.

- [ ] **Step 2: Run them and watch them fail**

```bash
dotnet test tests/Assistant.UnitTests --filter "FullyQualifiedName~TimeSettingsTests"
```

Expected: does not compile, `TimeSettings` does not exist.

- [ ] **Step 3: Write the settings type**

Create `src/Assistant.Impl/Settings/TimeSettings.cs`:

```csharp
using System.Configuration;
using Assistant.Interfaces;

namespace Assistant.Impl.Settings;

/// <summary>
/// Configuration for the zone the assistant reads and writes wall-clock times in.
/// </summary>
/// <remarks>
/// One zone serves the whole assistant, because it serves one person. Per-user zones are
/// deferred (spec §12.7); binding this one from configuration rather than naming it in code is
/// not (spec §11.4) — a hardcoded zone would block every contributor outside Israel in their
/// first five minutes.
/// </remarks>
public sealed class TimeSettings : IValidatableConfig
{
    /// <summary>
    /// The IANA identifier of the assistant's zone, such as <c>Asia/Jerusalem</c>.
    /// </summary>
    public required string IanaTimeZone { get; init; }

    /// <inheritdoc/>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(IanaTimeZone))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(TimeSettings)}.{nameof(IanaTimeZone)} is missing or empty.");
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(IanaTimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ConfigurationErrorsException(
                $"{nameof(TimeSettings)}.{nameof(IanaTimeZone)} is '{IanaTimeZone}', which this "
                + "machine does not know. Use an IANA identifier such as 'Asia/Jerusalem'.", ex);
        }
    }
}
```

- [ ] **Step 4: Register the resolver**

Add to `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`, after `AddAssistantServices`:

```csharp
    /// <summary>
    /// Registers the resolver that turns local wall-clock times into instants.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <param name="settings">
    /// Validated time configuration. Read it with <c>IConfiguration.Read</c> so an unknown zone
    /// stops the host here, while it is composing, rather than at the first captured task.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddAssistantServices</c> for the <see cref="TimeProvider"/> the past and
    /// future guards read.
    /// </remarks>
    public static IServiceCollection AddAssistantTime(
        this IServiceCollection services, TimeSettings settings)
    {
        services.AddSingleton(TimeZoneInfo.FindSystemTimeZoneById(settings.IanaTimeZone));
        services.AddSingleton<ILocalTimeResolver, LocalTimeResolver>();
        return services;
    }
```

The file already has `using Assistant.Impl.Services;`, `using Assistant.Impl.Settings;` and
`using Assistant.Interfaces;`. Add none, remove none.

- [ ] **Step 5: Ship the default and wire the host**

`src/Assistant.Worker/appsettings.json` — add a sibling to `Logging`:

```json
  "TimeSettings": {
    "IanaTimeZone": "Asia/Jerusalem"
  }
```

`src/Assistant.Worker/Program.cs` — one line, immediately after `AddAssistantServices()`:

```csharp
builder.Services.AddAssistantTime(builder.Configuration.Read<TimeSettings>());
```

It must come after `AddAssistantServices`, which registers the `TimeProvider` the resolver
takes. It must stay below the `send-test-message` early return, which deliberately builds a
host with nothing but Telegram configured.

`.env.example` — append, so an operator can see the knob exists:

```
# The zone every time the assistant reads or writes is in.
# Any IANA identifier; defaults to Asia/Jerusalem when unset.
TimeSettings__IanaTimeZone=
```

- [ ] **Step 6: Run the tests**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
```

Expected: 17 more than the suite had before this feature, zero warnings.

- [ ] **Step 7: Prove the host still composes**

```bash
DatabaseSettings__ConnectionString="Host=localhost;Port=1;Database=x;Username=x;Password=x" \
  dotnet run --project src/Assistant.Worker
```

Expected: it fails trying to reach Postgres on port 1, **not** on configuration. A
`ConfigurationErrorsException` naming `TimeSettings` means the section did not ship correctly in
`appsettings.json`. Stop the process once you have seen which error came out.

Then prove the validation fires:

```bash
TimeSettings__IanaTimeZone="Asia/Nowhere" \
DatabaseSettings__ConnectionString="Host=localhost;Port=1;Database=x;Username=x;Password=x" \
  dotnet run --project src/Assistant.Worker
```

Expected: `ConfigurationErrorsException` naming `TimeSettings.IanaTimeZone` and
`Asia/Nowhere`, before anything touches the database. Record both outputs in the task report.

- [ ] **Step 8: Commit**

```bash
git add src/Assistant.Impl/Settings/TimeSettings.cs \
        src/Assistant.Impl/ImplServiceCollectionExtensions.cs \
        src/Assistant.Worker/Program.cs src/Assistant.Worker/appsettings.json \
        .env.example tests/Assistant.UnitTests/Configuration/TimeSettingsTests.cs
git commit
```

Message:

```
feat: bind the assistant's zone from configuration

No code names a zone literal, per spec 11.4. appsettings.json ships
Asia/Jerusalem so a fresh clone runs unconfigured, and TimeSettings.Validate
resolves the identifier at startup so a typo stops the host while it is
composing rather than inside the first captured task.
```

---

## Task 4: Record what F8 settled, and correct what it contradicts

Documentation only. No code changes.

**Files:**
- Modify: `docs/design/slice-1-reminders.md`
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`
- Modify: `README.md`

- [ ] **Step 1: Correct §5.4, which puts the guards in the wrong place**

In `docs/design/slice-1-reminders.md` §5.4, the sentence reads "…converts local → UTC, and the
service applies guard clauses before anything is persisted". No service applies them: the
resolver does, and returns a `Result<DateTimeOffset>` its caller turns into a question. Rewrite
the sentence to say so, keep the table exactly as it is, and add one sentence recording that
the first occurrence of an ambiguous time has to be chosen explicitly because
`TimeZoneInfo.ConvertTimeToUtc` resolves to the second.

- [ ] **Step 2: Make §2 and §12.7 agree with §5.4 and §11.4**

§2's table row reads `Timezone | Asia/Jerusalem, fixed for slice 1 | YAGNI. Configurable zones
are a later slice (§12.7)`, and §12.7 defers "Configurable timezone in the prompt and resolver".
Both contradict §5.4 ("the configured IANA zone, never a hardcoded one") and §11.4 ("No code
names a zone literal").

Correct both to what is now true and what stays deferred: **one** zone serves the whole
assistant and is bound from configuration, defaulting to `Asia/Jerusalem`; what is deferred is a
zone *per user*, and the trigger is still a second user. §12.7's row about the **prompt** stays
— F9 has not been built, and the prompt does not read `TimeSettings` yet.

- [ ] **Step 3: Record F8 as done**

In the backlog, change the F8 heading to end in `· **done**`, matching F2 through F7. Keep the
entry's existing description. Append a `*Settled at F8:*` list covering:

- The guards live in the resolver, not in a service — and why (§5.4 said otherwise; F10 is the
  first feature with a service that could hold them).
- `Result<T>` joined `Result`; both stay, and why.
- The resolver takes a `DateTime`, not the model's ISO string, and F9's `CreateTaskRequest` is
  where the parse lands.
- The spring-forward gap needs no branch at all: `GetUtcOffset` returns the pre-transition
  offset for a time inside a gap, which names the same instant as shifting the reading past it,
  for any gap width. Probed in two zones before the code was written, and held by tests in both.
  Record the arithmetic, so the next reader does not re-add the branch.
- Both `ConvertTimeToUtc` and `GetUtcOffset` resolve an ambiguous time to the **second**
  occurrence, so §5.4's "first occurrence" had to be selected by hand.
- The tests run against `Australia/Lord_Howe` as well as the configured zone, because a
  half-hour gap is the only thing that can tell a correct implementation from a lucky one.
- The zone is configuration with a default in `appsettings.json`, validated at startup; the
  three-way spec contradiction that was found and how it was ruled.
- No current-local-time member: F9 adds it when the prompt needs it.

- [ ] **Step 4: Correct the README's limitation**

`README.md` currently says:

> **One timezone.** Currently fixed to `Asia/Jerusalem`. Making this
> configurable is a small change and a welcome pull request.

The second sentence is no longer true — it is configurable now. Rewrite the bullet to say that
the assistant runs in one zone, set by `TimeSettings__IanaTimeZone` and defaulting to
`Asia/Jerusalem`, and that what does not exist is a *per-user* zone. Keep it to the same two or
three lines the neighbouring bullets run to, and keep the honest framing the section has: this
is a stated limitation, not a feature.

- [ ] **Step 5: Commit**

```bash
git add docs README.md
git commit
```

Message:

```
docs: record the decisions F8 settled

The spec said the guards live in a service and that the zone is fixed for
slice 1; neither survived contact with 5.4 and 11.4, which say the opposite
and say it in more detail. Both are corrected here rather than left for a
reader to discover.
```

---

## Self-review

- [ ] `dotnet clean && dotnet build` — zero warnings, zero errors
- [ ] Unit tests green, 17 more than before this feature (20 -> 37)
- [ ] Integration tests untouched and still green — this feature adds none and changes none
- [ ] No new package reference and no new project reference anywhere
- [ ] No second `InternalsVisibleTo`; `LocalTimeResolver` is `internal sealed`
- [ ] `ErrorCode`'s new members were **appended**, so no existing member's value moved
- [ ] Every new public member has a three-line `<summary>`; every test summary is Gherkin,
      one clause per line
- [ ] Every class taking arguments uses a primary constructor
- [ ] No code anywhere names a time zone literal — grep for `Asia/` in `src/` and expect
      exactly one hit, in `appsettings.json`, which is configuration, plus the two in
      `TimeSettings`'s error message and doc comment, which are examples for an operator
- [ ] All three scratch checks in Task 2 Step 5 were actually performed, each one's result
      recorded, and all three edits reverted rather than committed
- [ ] No `IsInvalidTime` branch was added back. If one seems necessary, Decision F is wrong and
      that is a finding to report, not a line to write
- [ ] Both `dotnet run` checks in Task 3 Step 7 were performed and both outputs recorded
- [ ] `docs/e2e-local.md` still describes reality — the worker now reads a `TimeSettings`
      section; confirm the runbook does not tell anyone to start from a config that lacks it
- [ ] `AGENTS.md` needs no change: no command, project, or convention moved
- [ ] Spec §2, §5.4 and §12.7 now agree with each other and with the code
- [ ] No emoji in any changed file, including commit messages
- [ ] Diff under 1000 lines excluding this plan
