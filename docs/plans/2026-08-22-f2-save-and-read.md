# F2 — Save a task and read it back

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** `ITaskRepository.AddAsync` and `FindAsync`, implemented by `EfTaskRepository`, proven by
a database round-trip that fails when a property stops being persisted.

**Architecture:** `EfTaskRepository` is internal to `Assistant.Repository` and registered by
`AddAssistantRepository`, so no project outside that assembly names an EF type. It saves on every
call — there is no unit of work, because every caller writes one task at a time. `FindAsync`
reads with `AsNoTracking`, and the round-trip test reads through a *second* service provider, so
the assertion cannot be satisfied from EF's change tracker.

**Tech Stack:** EF Core 10.0.11, Npgsql provider 10.0.3, xUnit 2.9.3, Respawn, Docker Compose.

**Spec:** `docs/design/slice-1-reminders.md` §4.1, §4.2, §4.3.
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F2.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error in `src/` only.
- **EF Core types appear only in `Assistant.Repository`.** `Impl` never references `Repository`.
- All instants UTC. `DateTimeOffset` **must** have `Offset == TimeSpan.Zero`.
- Plain xUnit `Assert`. No Shouldly, no FluentAssertions. `Assert.Equal(expected, actual)` —
  expected first. `Assert.ThrowsAny<T>` / `ThrowsAnyAsync<T>`, never `Assert.Throws<T>`.
- Every `<summary>` is three lines: open tag, text, close tag.
- Every enum's first member is `Unknown`, with no explicit numeric values.
- Central package management: a `PackageReference` with an inline `Version=` is an error (NU1008).
- **YAGNI:** this feature introduces nothing it does not test.
- PR budget: 1000 lines. Estimated ~260 of code and tests, plus this plan document (~690),
  which rides along in the same pull request as F1's did.

---

## Decisions this plan makes — review these first

### A. `GetDueRemindersAsync` is removed from `ITaskRepository`, and returns at F3

`ITaskRepository` currently declares it. Nothing implements it and nothing calls it:

```
$ grep -rn "GetDueRemindersAsync" --include="*.cs" src tests
src/Assistant.Interfaces/ITaskRepository.cs:32:    Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(
```

It survived the YAGNI reset by accident. It cannot simply be left alone: C# requires
`EfTaskRepository` to implement every interface member, so keeping it forces one of two things —
a `NotImplementedException` body that ships to production, or F3's query written and untested
inside F2. Backlog §1 forbids both ("a feature may only introduce an interface member that the
same feature exercises with a test").

Removing it now and re-adding it at F3 is additive in the direction you want: F3 grows the
interface rather than replacing a stub.

### B. `AddAsync` lets the `PostgresException` propagate — no translation yet

The F1 review left this open. The answer is not to translate, because the vocabulary for a
translated error does not exist: `Result` and `ErrorCode` live in `Contracts`, which the backlog
brings to life at **F5** with `TaskService`. Inventing them at F2 builds types nothing consumes
for three features.

So `AddAsync` is thin, the exception reaches the caller, and the test asserts it by constraint
name. When `TaskService` arrives at F5 as the single writer (spec §4.2), translation belongs
there — not in the repository, which stays a persistence detail.

### C. `FindAsync` uses `AsNoTracking`, and the round-trip test reads through a second provider

This is the decision that determines whether the headline test means anything.

If the write and the read share one `DbContext`, EF's identity map answers the read from memory:
`DbSet.Find` checks the tracker before querying at all, and a tracked LINQ query re-uses the
instance it already has rather than the values it just read. The test would then pass while
asserting nothing about the database — it would compare an object with itself.

Two independent defences, so correctness does not depend on which one bites:

1. `FindAsync` calls `AsNoTracking()`, so a returned entity is always materialised from row data.
2. The test writes through one provider, disposes it, and reads through a second.

`AsNoTracking` also matches how the system will actually mutate: F5 introduces
`ITaskRepository.UpdateAsync`, so callers hand back a modified object explicitly rather than
relying on tracked change detection.

### D. Test instants must be microsecond-aligned literals, never `DateTimeOffset.UtcNow`

Postgres `timestamptz` stores microseconds; .NET ticks are 100ns. Sub-microsecond precision is
silently truncated. Measured against the real database:

```
original ticks = 639229914001234567  (2026-08-22T10:30:00.1234567+00:00)
readback ticks = 639229914001234560  (2026-08-22T10:30:00.1234560+00:00)
equal          = False
tick delta     = 7
```

A test using `UtcNow` is a coin flip that depends on the host clock's resolution. On this Mac
`UtcNow` happened to land on a microsecond boundary and the same probe reported `equal = True` —
so the naive version passes locally and can fail on Linux CI. Every instant in these tests is
therefore a literal with at most 6 fractional digits.

### E. `Assert.Equivalent(expected, actual, strict: true)` is the round-trip assertion — plus one explicit offset check

`Assert.Equivalent` with `strict: true` compares every property, so a property added later is
covered without editing the test. Measured behaviour in xUnit 2.9.3:

| Case | Result |
| :--- | :--- |
| Identical objects | passes |
| One property changed | `EquivalentException`: *"Mismatched value on member 'Title'"* |
| Nullable property silently dropped | `EquivalentException` |
| **Same instant, different offset (`+03:00` vs `+00:00`)** | **passes — not detected** |

That last row is why the test carries a separate assertion that each returned
`DateTimeOffset.Offset` is `TimeSpan.Zero`. `DateTimeOffset` equality compares instants, so
`Assert.Equivalent` alone cannot enforce the project's "UTC with a zero offset" rule.

### F. Correction to the backlog's F2 entry

It cites spec §4.4 for the round-trip test. §4.4's round-trip is **model → response → model** —
the hand-written `Contracts` mapper, which does not exist until F10. F2's round-trip is
**model → Postgres → model**. Both are real and they catch different defects: a mapper that
forgets a field, versus a column mapping that forgets one. Task 4 corrects the citation.

---

## What F2 does NOT include, and why

| Excluded | Returns at |
| :--- | :--- |
| `GetDueRemindersAsync` and `idx_tasks_due_pending` | F3 |
| `UpdateAsync` | F5 — the scheduler marking a reminder sent is the first writer |
| `ITaskService`, `Result`, `ErrorCode` | F5 (spec §4.2) |
| Error translation, retries, `DbUpdateException` unwrapping | F5, with the single writer |
| `notes`, `priority`, `delivery_attempts`, `completed_at` | F10, F12, F11, F6 |
| Any mapping to or from `Contracts` types | F10 (spec §4.4) |

---

## File Structure

| Path | Responsibility |
| :--- | :--- |
| `src/Assistant.Interfaces/ITaskRepository.cs` | **Modify.** Drop `GetDueRemindersAsync`, add `FindAsync`. |
| `src/Assistant.Repository/Repositories/EfTaskRepository.cs` | **Create.** The only implementation. Internal sealed. |
| `src/Assistant.Repository/RepositoryServiceCollectionExtensions.cs` | **Modify.** Register `ITaskRepository`. |
| `tests/Assistant.IntegrationTests/Infrastructure/PostgresFixture.cs` | **Modify.** Add `CreateProvider()`. |
| `tests/Assistant.IntegrationTests/Repositories/TaskRepositoryTests.cs` | **Create.** Round-trip, miss, and constraint tests. |
| `tests/Assistant.IntegrationTests/Schema/ReminderTaskSchemaTests.cs` | **Modify.** Task 4 applies the retirement check. |
| `docs/design/2026-08-22-slice-1-feature-backlog.md` | **Modify.** Record what F2 settled. |

**Interfaces produced** (later features consume these):
- `ITaskRepository.AddAsync(ReminderTask task, CancellationToken ct)` → `Task`
- `ITaskRepository.FindAsync(Guid id, CancellationToken ct)` → `Task<ReminderTask?>`
- `PostgresFixture.CreateProvider()` → `ServiceProvider` (caller owns and disposes it)

---

## Test design

**Equivalence classes for `FindAsync`:** the id matches a row (returns it), the id matches
nothing (returns `null`). There is no third class — the parameter is a `Guid`, so there is no
malformed input to partition and no boundary to probe.

**Equivalence classes for `AddAsync`:** a row the constraints accept (persists), and a row
`ck_reminder_tasks_status_known` rejects (throws). The second is the F1 carry-over.

**Deliberately not tested, and why:**

| Not tested | Reason |
| :--- | :--- |
| Npgsql throwing on a non-zero `DateTimeOffset` offset | Framework behaviour. We assert *our* values come back at zero offset instead. |
| `SaveChangesAsync` writing a row at all | Every test here depends on it; a separate assertion adds nothing. |
| Concurrent writers, transactions, retries | No feature has two writers yet. F5 introduces the single-writer rule. |
| `ck_reminder_tasks_sent_requires_due` through the application | Unreachable until F5 sets `ReminderSentAt`. Its raw-SQL test stays. |

---

## Task 1: `FindAsync` on the interface, and `EfTaskRepository`

**Files:**
- Modify: `src/Assistant.Interfaces/ITaskRepository.cs`
- Create: `src/Assistant.Repository/Repositories/EfTaskRepository.cs`
- Modify: `src/Assistant.Repository/RepositoryServiceCollectionExtensions.cs:21-26`
- Modify: `tests/Assistant.IntegrationTests/Infrastructure/PostgresFixture.cs`
- Test: `tests/Assistant.IntegrationTests/Repositories/TaskRepositoryTests.cs`

**Interfaces:**
- Consumes: `PostgresFixture.ConnectionString`, `PostgresFixture.ResetAsync()`,
  `PostgresCollection.Name` (all from F1); `AddAssistantRepository(IServiceCollection, string)`.
- Produces: `ITaskRepository.AddAsync`, `ITaskRepository.FindAsync`,
  `PostgresFixture.CreateProvider()`.

- [ ] **Step 1: Reshape the interface**

Replace the body of `src/Assistant.Interfaces/ITaskRepository.cs`. Keep the file's existing
`using` and class-level `<remarks>` exactly as they are; replace only the members.

```csharp
    /// <summary>
    /// Adds a new task.
    /// </summary>
    /// <param name="task">The task to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task AddAsync(ReminderTask task, CancellationToken ct);

    /// <summary>
    /// Returns the task with the given identifier.
    /// </summary>
    /// <param name="id">The identifier to look for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The task, or <see langword="null"/> when no row carries that identifier. The result is
    /// not change-tracked: mutations go through the task service, never by writing to this
    /// object and expecting it to be saved.
    /// </returns>
    Task<ReminderTask?> FindAsync(Guid id, CancellationToken ct);
```

`GetDueRemindersAsync` and its doc comment are deleted. See Decision A.

- [ ] **Step 2: Add `CreateProvider` to the fixture**

Append this method to `PostgresFixture`, after `ResetAsync`. It needs no new `using`: the file
already has `Assistant.Repository` and `Microsoft.Extensions.DependencyInjection`.

```csharp
    /// <summary>
    /// Builds a service provider wired to the test database.
    /// </summary>
    /// <returns>
    /// A provider the caller owns and must dispose. Each call produces an independent
    /// <c>DbContext</c>, which is what lets a test read a row back without the change tracker
    /// answering from memory.
    /// </returns>
    public ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddAssistantRepository(ConnectionString);
        return services.BuildServiceProvider();
    }
```

- [ ] **Step 3: Write the failing round-trip test**

Create `tests/Assistant.IntegrationTests/Repositories/TaskRepositoryTests.cs`:

```csharp
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.IntegrationTests.Repositories;

/// <summary>
/// Proves that a task survives a write and a read through separate database contexts.
/// </summary>
/// <remarks>
/// Every instant here is a literal with at most six fractional digits. Postgres
/// <c>timestamptz</c> holds microseconds while .NET ticks are 100ns, so a value taken from
/// <c>DateTimeOffset.UtcNow</c> can be truncated on read, and whether it is depends on the host
/// clock's resolution — which makes it pass on one machine and fail on another.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TaskRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;

    public TaskRepositoryTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => _postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// When a task is added through one context
    /// And read back through a different one
    /// Then every property holds the value it was written with.
    /// </summary>
    [Fact]
    public async Task AddAsync_ThenFindAsync_PreservesEveryProperty()
    {
        // Arrange
        var expected = new ReminderTask
        {
            Id = Guid.NewGuid(),
            Title = "call the bank",
            Status = ReminderStatus.Pending,
            DueAt = new DateTimeOffset(2026, 8, 22, 10, 30, 0, TimeSpan.Zero),
            ReminderSentAt = null,
            CreatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
        };

        await using (var writer = _postgres.CreateProvider())
        {
            await writer.GetRequiredService<ITaskRepository>()
                .AddAsync(expected, CancellationToken.None);
        }

        // Act
        await using var reader = _postgres.CreateProvider();
        var actual = await reader.GetRequiredService<ITaskRepository>()
            .FindAsync(expected.Id, CancellationToken.None);

        // Assert
        Assert.NotNull(actual);
        Assert.Equivalent(expected, actual, strict: true);
        Assert.Equal(TimeSpan.Zero, actual.DueAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, actual.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, actual.UpdatedAt.Offset);
    }

    /// <summary>
    /// When no row carries the requested identifier
    /// And FindAsync is called with it
    /// Then null is returned rather than an exception.
    /// </summary>
    [Fact]
    public async Task FindAsync_IdMatchesNothing_ReturnsNull()
    {
        // Arrange
        await using var provider = _postgres.CreateProvider();
        var repository = provider.GetRequiredService<ITaskRepository>();

        // Act
        var actual = await repository.FindAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Null(actual);
    }
}
```

The three `Offset` assertions exist because `Assert.Equivalent` treats `+03:00` and `+00:00` as
equal when they name the same instant — measured, see Decision E.

- [ ] **Step 4: Run the tests and watch them fail**

```bash
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests
```

Expected: compilation fails — `ITaskRepository` has no registration, so
`GetRequiredService<ITaskRepository>` throws at runtime, and `FindAsync` does not yet exist on
any implementation. This is the red state.

- [ ] **Step 5: Write `EfTaskRepository`**

Create `src/Assistant.Repository/Repositories/EfTaskRepository.cs`:

```csharp
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Repository.Repositories;

/// <summary>
/// Entity Framework implementation of <see cref="ITaskRepository"/>.
/// </summary>
/// <remarks>
/// Internal by design: callers resolve <see cref="ITaskRepository"/> from the container, so no
/// project outside this assembly names an Entity Framework type. Each method saves immediately.
/// There is no unit of work because every caller writes one task at a time, and introducing one
/// before a caller needs it would be a guess.
/// </remarks>
internal sealed class EfTaskRepository : ITaskRepository
{
    private readonly AssistantDbContext _db;

    /// <summary>
    /// Initialises the repository with the context it reads and writes through.
    /// </summary>
    /// <param name="db">The assistant's database context.</param>
    public EfTaskRepository(AssistantDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task AddAsync(ReminderTask task, CancellationToken ct)
    {
        _db.ReminderTasks.Add(task);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public Task<ReminderTask?> FindAsync(Guid id, CancellationToken ct) =>
        _db.ReminderTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
}
```

`AsNoTracking` is deliberate and `DbSet.FindAsync` is deliberately **not** used — it consults the
change tracker before the database, which would let a same-context read answer from memory. See
Decision C.

- [ ] **Step 6: Register it**

In `RepositoryServiceCollectionExtensions.cs`, add `using Assistant.Interfaces;` and
`using Assistant.Repository.Repositories;` at the top, then add one line to
`AddAssistantRepository`:

```csharp
    public static IServiceCollection AddAssistantRepository(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AssistantDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ITaskRepository, EfTaskRepository>();
        return services;
    }
```

Update that method's `<summary>` to say it registers the context *and the repositories*.

- [ ] **Step 7: Run the tests and watch them pass**

```bash
dotnet build
dotnet test tests/Assistant.IntegrationTests
```

Expected: `0 Warning(s)`, `0 Error(s)`, 4 tests passing — the 2 schema tests from F1 plus the 2
added here.

- [ ] **Step 8: Prove the round-trip test can actually fail**

This is the step that decides whether Task 1 delivered anything. Temporarily add
`builder.Ignore(x => x.ReminderSentAt);` to `ReminderTaskConfiguration.Configure`, set the test's
`ReminderSentAt` to `new DateTimeOffset(2026, 8, 22, 10, 31, 0, TimeSpan.Zero)`, and run.

Expected: `EquivalentException` naming member `ReminderSentAt`.

Revert both edits and re-run to confirm green. Do not commit either edit.

- [ ] **Step 9: Commit**

```bash
git add src/Assistant.Interfaces/ITaskRepository.cs \
        src/Assistant.Repository/Repositories/EfTaskRepository.cs \
        src/Assistant.Repository/RepositoryServiceCollectionExtensions.cs \
        tests/Assistant.IntegrationTests/Infrastructure/PostgresFixture.cs \
        tests/Assistant.IntegrationTests/Repositories/TaskRepositoryTests.cs
git commit -m "feat: add and read back a task through EfTaskRepository"
```

---

## Task 2: `AddAsync` surfaces the status constraint

**Files:**
- Modify: `tests/Assistant.IntegrationTests/Repositories/TaskRepositoryTests.cs`

**Interfaces:**
- Consumes: `ITaskRepository.AddAsync`, `PostgresFixture.CreateProvider()` (Task 1).
- Produces: nothing new.

This is the follow-up the F1 review deferred: the constraint is currently proven only by a
raw-SQL insert, which says nothing about what happens when application code produces the row.
`new ReminderTask()` has `Status = Unknown` by the enum convention, so forgetting to set the
status is the easiest mistake a caller can make.

- [ ] **Step 1: Write the failing test**

Add to `TaskRepositoryTests`, and add `using Npgsql;` to the file's usings:

```csharp
    /// <summary>
    /// When a task is added with the Unknown status a caller gets by forgetting to set one
    /// And AddAsync writes it
    /// Then the database refuses the row, naming ck_reminder_tasks_status_known.
    /// </summary>
    [Fact]
    public async Task AddAsync_StatusLeftUnknown_IsRejectedByTheDatabase()
    {
        // Arrange
        await using var provider = _postgres.CreateProvider();
        var repository = provider.GetRequiredService<ITaskRepository>();
        var task = new ReminderTask
        {
            Id = Guid.NewGuid(),
            Title = "status never set",
            CreatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
        };

        // Act
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => repository.AddAsync(task, CancellationToken.None));

        // Assert
        var postgres = FindPostgresException(ex);
        Assert.NotNull(postgres);
        Assert.Equal("ck_reminder_tasks_status_known", postgres.ConstraintName);
    }

    /// <summary>
    /// Walks an exception chain to the provider exception underneath it.
    /// </summary>
    /// <param name="ex">The exception the repository threw.</param>
    /// <returns>The innermost <see cref="PostgresException"/>, or null if there is none.</returns>
    /// <remarks>
    /// Entity Framework wraps provider exceptions, but this project cannot name the wrapper:
    /// the EF packages are marked <c>PrivateAssets="compile"</c> in Assistant.Repository, so
    /// they do not flow here at compile time. Asserting on the Npgsql exception is the stronger
    /// test anyway, because it does not depend on how EF chooses to wrap.
    /// </remarks>
    private static PostgresException? FindPostgresException(Exception? ex)
    {
        while (ex is not null and not PostgresException)
        {
            ex = ex.InnerException;
        }

        return ex as PostgresException;
    }
```

Note `Status` is not assigned at all — that is the point of the test.

- [ ] **Step 2: Run it**

```bash
dotnet test tests/Assistant.IntegrationTests
```

This test passes on its first run, because the constraint and `AddAsync` both already exist.
That is expected and is not a TDD failure: the test documents a decision (Decision B — the
exception propagates untranslated) rather than driving new production code. Task 3 is what proves
it can fail.

**Do not add an EF Core package reference to the test project to name `DbUpdateException`.** It
will not compile, and this was measured rather than assumed:

```
$ dotnet build tests/Assistant.IntegrationTests
error CS0234: The type or namespace name 'EntityFrameworkCore' does not exist
              in the namespace 'Microsoft'
```

`PrivateAssets="compile"` on the EF packages in `Assistant.Repository` stops them flowing here at
compile time — the same boundary F1 established so that EF stays inside one assembly. That is why
the test walks the exception chain to the `PostgresException` instead. Adding the reference would
weaken the boundary to buy a weaker assertion.

- [ ] **Step 3: Commit**

```bash
git add tests/Assistant.IntegrationTests/Repositories/TaskRepositoryTests.cs
git commit -m "test: prove AddAsync surfaces the status constraint untranslated"
```

---

## Task 3: Apply the retirement check to the F1 raw-SQL test

**Files:**
- Modify: `tests/Assistant.IntegrationTests/Schema/ReminderTaskSchemaTests.cs`

**Interfaces:**
- Consumes: the test added in Task 2.
- Produces: nothing.

`ReminderTaskSchemaTests` carries `<remarks>` describing exactly this check. Task 3 runs it.

- [ ] **Step 1: Run the check**

Temporarily delete this line from `ReminderTaskConfiguration.Configure`:

```csharp
t.HasCheckConstraint("ck_reminder_tasks_status_known", "status <> 0");
```

Then regenerate the migration and run the full suite:

```bash
dotnet ef migrations remove --force --project src/Assistant.Repository --startup-project src/Assistant.Worker
dotnet ef migrations add InitialCreate --project src/Assistant.Repository --startup-project src/Assistant.Worker
docker compose -f compose.test.yaml down -v && docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests
```

The `down -v` matters: Respawn truncates tables but does not drop constraints, so without a fresh
volume the old constraint is still there and the check proves nothing.

- [ ] **Step 2: Act on the result**

- **If `AddAsync_StatusLeftUnknown_IsRejectedByTheDatabase` fails** — the new test covers the
  constraint, so delete `Insert_StatusUnknown_ViolatesStatusKnownConstraint` and remove its
  `<remarks>` block. Update the class-level `<remarks>` so it refers only to the remaining test.
- **If only `Insert_StatusUnknown_ViolatesStatusKnownConstraint` fails** — the raw-SQL test is
  still the sole guard. Keep it, and replace its `<remarks>` with a line recording that the check
  was run at F2 and it stays.

- [ ] **Step 3: Restore and verify**

Put the `HasCheckConstraint` line back, regenerate the migration, recreate the volume, and run
the suite. The generated migration must be byte-identical to the committed one — confirm with
`git diff src/Assistant.Repository/Migrations/`. If it is not, stop and investigate before
committing; a changed migration hash means the schema moved.

- [ ] **Step 4: Commit**

```bash
git add tests/Assistant.IntegrationTests/Schema/ReminderTaskSchemaTests.cs
git commit -m "test: apply the F1 retirement check to the status constraint test"
```

---

## Task 4: Record what F2 settled

**Files:**
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`

- [ ] **Step 1: Update the F2 entry**

- Replace the §4.4 citation with §4.1 and §4.3, and state that the round-trip here is
  model → Postgres → model. §4.4's model → response → model round-trip belongs to F10 with the
  hand-written mappers. See Decision F.
- Record that `AddAsync` surfaces the database exception untranslated, and that translation
  arrives with `TaskService` at F5.
- Delete the *Carried from F1* paragraph, which Tasks 2 and 3 have now discharged.

- [ ] **Step 2: Update the F3 entry**

Note that F3 re-adds `GetDueRemindersAsync` to `ITaskRepository`, which F2 removed as unconsumed.

- [ ] **Step 3: Full verification**

```bash
dotnet build
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
```

Expected: `0 Warning(s)`, `0 Error(s)`, 12 unit tests, and 4 or 5 integration tests depending on
Task 3's outcome.

- [ ] **Step 4: Commit and open the pull request**

```bash
git add docs/design/2026-08-22-slice-1-feature-backlog.md
git commit -m "docs: record the decisions F2 settled"
git push -u origin feature/f2-save-and-read
```

Open the PR against `main`. Do not merge.

---

## Self-review

**Spec coverage.** §4.1 — the model is unchanged and still behaviour-free; the architecture test
enforcing that already exists and stays green. §4.3 — no schema change; F2 only reads and writes
the F1 table. §4.2 — not implemented here, and deliberately so: `TaskService` is F5's, and this
plan states that in Decision B and in the exclusions table. §4.4 — explicitly out of scope, with
the backlog's misattribution corrected in Task 4.

**Placeholder scan.** No TBDs. Two steps (Task 2 Step 2, Task 3 Step 2) branch on a measured
outcome rather than naming one — each spells out both branches and what to do in either, which is
a decision procedure, not a placeholder.

**Type consistency.** `ITaskRepository.FindAsync(Guid, CancellationToken)` → `Task<ReminderTask?>`
is used identically in the interface, `EfTaskRepository`, and all three tests.
`PostgresFixture.CreateProvider()` returns `ServiceProvider` (not `IServiceProvider`) because the
tests `await using` it, and `IServiceProvider` is not disposable.

**Resolved during review.** An earlier draft left Task 2 branching on whether the test project
could name `DbUpdateException`. Measured: it cannot (`CS0234`), because `PrivateAssets="compile"`
keeps EF Core out of every consumer's compile surface. The task now states one path.

**Remaining risk.** Task 3 Step 3 requires the regenerated migration to be byte-identical to the
committed one. EF migration scaffolding embeds a timestamp in the *file name*, and Task 3
regenerates under the same name via `migrations remove` first — but if the regenerated file
differs in any way beyond formatting, the task says stop rather than commit, which is the right
failure mode.
