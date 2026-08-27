# Conventions

Extracted from [`docs/design/slice-1-reminders.md`](./design/slice-1-reminders.md).
If this document and the spec ever disagree, the spec is authoritative.

## Reference rules (spec §3.2)

```
Models      →  (nothing)
Contracts   →  (nothing — BCL only)
Interfaces  →  Models, Contracts
Repository  →  Interfaces, Models
Impl        →  Interfaces, Contracts, Models        never Repository
Worker      →  everything
UnitTests   →  Impl, Interfaces, Contracts, Models
IntegrationTests → Worker (boots the real host)
```

Three rules carry the weight of this layout:

**`Contracts` holds request/response types, not interfaces.** It is what a caller speaks to the application: `CreateTaskRequest`, `TaskResponse`, `ListTasksRequest`. In slice 1 those callers are the LLM tool invocations and the button callbacks; when an HTTP API arrives in a later slice it consumes the same types unchanged. `Contracts` references nothing, so it stays a pure vocabulary.

**`Interfaces` holds every interface and may reference `Models`.** This is what keeps the design simple: internal calls pass `ReminderTask` directly, so there is no obligatory mapping layer between every collaborator. Mapping survives only where something genuinely external is on the other side — an LLM tool parameter, a Telegram payload.

**`Impl` never references `Repository`.** Services live in `Impl` and call the repository — but they call it through `ITaskRepository`, which lives in `Interfaces`. C# requires a reference only for types a project *names*, and `TaskService(ITaskRepository repo)` names nothing from `Repository`. The reference is therefore unnecessary, and omitting it makes EF Core structurally unreachable from every service and adapter: not by convention, not by a package trick, but because there is nothing for the compiler to bind to.

Registration happens where registration belongs — the composition root:

```csharp
builder.Services.AddAssistantRepository(connectionString);  // from Repository
builder.Services.AddAssistantServices();                     // from Impl
```

`AddAssistantServices()` registers `TaskService`, the jobs, the actions, and the adapters, all binding to interfaces. At runtime the `Repository` assembly is present regardless, because `Worker` references it and .NET copies transitive dependencies to the output folder.

Two constraints preserve the boundary:

1. `Repository`'s public surface is exactly its repository implementations plus `AddAssistantRepository(this IServiceCollection, string connectionString)`, which registers `AppDbContext` and applies migrations internally. `Worker` never names an EF type; `Repository`'s EF package references are marked `PrivateAssets="compile"` so their compile-time assets do not flow outward while their runtime assets still do. **Not `"all"`** — that withholds the runtime assets too, so `Npgsql.EntityFrameworkCore.PostgreSQL.dll` is never copied to `Worker`'s output and `UseNpgsql` throws at startup. Verified empirically in F1: with `"all"` the provider assembly is absent from `Worker`'s output; with `"compile"` it is present and naming a `DbContext` in `Worker` source still fails to compile (CS0234), which is the property this rule exists to protect.
2. Repository methods return **materialised** results — `IReadOnlyList<ReminderTask>`, never `IQueryable<T>`. A queryable would leak EF back out through the interface and move query composition into the services.

The one case that would genuinely force a reference is a transaction spanning multiple repository calls. Slice 1 has none — capture is a single write, reminder delivery is send-then-single-update, the daily brief is one insert. Should one arise later, the answer is an `IUnitOfWork` in `Interfaces`, not a project reference.

Consequence for design: repository methods are named by intent (`GetDueRemindersAsync(DateTimeOffset now, int limit)`) rather than being generic composable queries. This is a better boundary anyway — the query lives next to the index built for it.

## 12. Code conventions

These are build-enforced where possible, because a convention that only lives in a document is a convention that erodes. They live in `docs/conventions.md` in the repository, referenced from `AGENTS.md`.

### 12.1 XML documentation comments

Every public type and member carries XML documentation, following the [recommended tags](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/recommended-tags).

**The rule that matters: document the contract, never the implementation.** A comment describes what a caller can rely on — what it does, what it returns, what it throws, what the valid inputs are. It does not narrate how the body works. Implementation detail in a doc comment is worse than no comment, because it goes stale silently while the summary still reads as authoritative.

Required:

| Tag | Applies to | Purpose |
| :--- | :--- | :--- |
| `<summary>` | every public type and member | What it is. Shown in IntelliSense. |
| `<param>` | every parameter | What the caller must supply |
| `<returns>` | every non-void method | What comes back, including what a null or empty result means |
| `<exception>` | every exception thrown deliberately | What causes it |
| `<value>` | properties | What the value represents |
| `<typeparam>` | generic types and methods | Constraints and intent |

Used where they add something:

- `<remarks>` — context that does not belong in a one-line summary: ordering guarantees, idempotency, thread safety, why a decision was made
- `<inheritdoc/>` — on implementations of an interface, so the contract is documented once. This is the default for everything in `Impl` implementing something from `Interfaces`.
- `<see cref="..."/>` and `<seealso>` — link related types instead of naming them in prose
- `<paramref>`, `<typeparamref>` — reference parameters inside prose
- `<c>` for inline code, `<code>` for blocks, `<example>` for non-obvious usage
- `<para>`, `<list>` for structure in longer remarks

**Enforcement.** `Directory.Build.props` sets `GenerateDocumentationFile=true`, and `CS1591` (missing XML comment for publicly visible type or member) is escalated to an error alongside the existing warnings-as-errors. Undocumented public API therefore fails the build rather than fails review.

Illustration of the distinction:

```csharp
/// <summary>
/// Moves a task's due time forward by <paramref name="duration"/> and re-arms its reminder.
/// </summary>
/// <param name="id">Identifier of the task to snooze.</param>
/// <param name="duration">How far forward to move the due time. Must be positive.</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>
/// A successful <see cref="Result"/>, or a failure when the task does not exist
/// or has already been completed or cancelled.
/// </returns>
/// <remarks>
/// Snoozing clears the reminder-sent marker, so the task will fire again at its new
/// due time. Snoozing a task whose reminder has not yet fired is valid and simply
/// moves it. See <see cref="ITaskService.RescheduleAsync"/> for setting an absolute time.
/// </remarks>
Task<Result> SnoozeAsync(Guid id, TimeSpan duration, CancellationToken ct);
```

The `<remarks>` states a guarantee a caller depends on. It does not say "loads the entity, adds the timespan, and calls SaveChangesAsync" — that is implementation, and it is what the code already says.

### 12.2 Mapping is extension methods

All mapping between models, requests, and responses is written as extension methods in `Impl/Mapping`, grouped in `static class`es by the type being mapped.

```csharp
public static class ReminderTaskMappingExtensions
{
    /// <summary>Projects a task onto the response shape returned to callers.</summary>
    /// <param name="task">The task to project.</param>
    /// <returns>A response carrying the caller-visible fields of <paramref name="task"/>.</returns>
    public static TaskResponse ToResponse(this ReminderTask task) => ...;

    /// <summary>Builds a new task from a creation request.</summary>
    public static ReminderTask ToModel(this CreateTaskRequest request, DateTimeOffset dueAtUtc) => ...;
}
```

Naming is by destination: `ToResponse()`, `ToModel()`, `ToRequest()`, `ToNotification()`. No mapping library — explicit methods fail visibly when a property is added, and the round-trip tests in §7.2 cover the case where one is forgotten.

### 12.3 Refit for HTTP clients

Every HTTP API this project calls itself is expressed as a Refit interface. No `HttpClient` is used directly, and no request is composed by hand.

```csharp
/// <summary>Anthropic Messages API.</summary>
public interface IAnthropicApi
{
    /// <summary>Sends a message request and returns the model's reply.</summary>
    /// <param name="request">The message request, including any tool definitions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The model response, which may contain tool-use blocks.</returns>
    [Post("/v1/messages")]
    Task<AnthropicMessageResponse> CreateMessageAsync(
        [Body] AnthropicMessageRequest request,
        CancellationToken ct = default);
}
```

Registered via `AddRefitClient<T>()` with the base address, auth headers, and the Polly resilience handler attached at the `HttpClient` level, so retry and circuit-breaking are configuration rather than code inside the adapter.

Why this suits the project: a Refit interface *is* the contract, so it is legible and reviewable; and because the base address is a registration concern, pointing it at WireMock in tests requires no production seam.

**Exception:** where a maintained typed SDK already exists, it is used rather than reimplemented. `Telegram.Bot` is an SDK, not a raw HTTP client, and stays as-is — its base address is likewise configurable, so it remains WireMock-testable.

### 12.4 `AGENTS.md`

The repository root carries `AGENTS.md` so that an AI coding agent — or a new human contributor — can build, test, and run everything without reverse-engineering the project. `CLAUDE.md` points at it rather than duplicating it, so there is one file to keep true.

It contains, and nothing more than:

1. **What this is**, in three sentences.
2. **Every command, copy-pasteable and verified to work**: restore, build, unit tests, bring up `compose.test.yaml`, integration tests, run locally, apply migrations, run the prompt evals. Each with what it requires — Docker running, `.env` populated.
3. **Project map** — the six projects, one line each, and the reference rules from §3.2 with a pointer to the architecture tests that enforce them.
4. **The conventions from this section**, or a pointer to `docs/conventions.md`.
5. **What not to do** — do not add a project reference from `Impl` to `Repository` (§3.2), do not put behaviour on models (§4.1), do not mutate a task outside `TaskService` (§4.2), do not write a unit test for something an integration test covers (§7.2), do not use `HttpClient` directly (§12.3).
6. **Where the design lives** — `docs/design/`, and the instruction to read the relevant spec before making a structural change.

**It must stay honest.** A stale `AGENTS.md` actively misleads, whereas a missing one merely slows people down. Every command in it is one CI already runs, so drift shows up as a failing build rather than as a contributor's wasted afternoon.

### 12.5 Primary constructors

**Every class that takes constructor arguments declares them as a primary constructor.** No class
declares a separate constructor.

```csharp
internal sealed class EfTaskRepository(AssistantDbContext db) : ITaskRepository
{
    public async Task AddAsync(ReminderTask task, CancellationToken ct)
    {
        db.ReminderTasks.Add(task);
        await db.SaveChangesAsync(ct);
    }
}
```

The parameter is in scope for every member, so the assign-to-a-readonly-field ceremony disappears
along with the field itself. Base calls come along too:
`internal sealed class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)`.

Two consequences worth knowing before you hit them:

- **Parameters are documented on the class.** A primary constructor has no doc comment of its own,
  so its `<param name="...">` tags belong on the class-level block next to `<summary>`. Omitting
  one is `CS1573`, which is an error in `src/`.
- **A field initializer cannot reference another field.** Where one dependency is derived from
  another, the derived one becomes an expression-bodied property rather than a field:

  ```csharp
  private readonly ServiceProvider _provider = postgres.CreateProvider();

  private ITaskRepository Sut => _provider.GetRequiredService<ITaskRepository>();
  ```

**This rule is not build-enforced, and that is deliberate.** The compiler emits an ordinary
constructor either way, so no reflection test can tell the two apart. The analyzer that can
(`IDE0290`) needs `.editorconfig`, which this project does not use (§12.7). It is a review rule,
checked by reading.

### 12.6 No emoji

**No file in this repository contains an emoji.** Not source, not tests, not documentation, not
commit messages, and not the text the bot sends. A friendly tone is not an exception clause;
nothing here needs decoration to read as approachable, and the rule does not bend for a message
that only the assistant will ever read.

The case against them is concrete, not aesthetic. Emoji render at inconsistent widths across
fonts and terminals, and this project's documents are full of ASCII diagrams and reference
tables — §3.2, the directory tree in §11.1 — whose alignment depends on every character being
one column wide; a pictogram silently breaks that for whoever's renderer disagrees with the
author's. In a diff, an emoji is one opaque glyph: `git diff` shows that the line changed, not
what changed, and a reviewer cannot tell which pictogram replaced which without opening a
codepoint table. They are not greppable without already knowing the codepoint — you cannot
search a codebase for a character you cannot type. And inside a message body an emoji is one
more character that has to survive `ParseMode.Html` escaping intact, on top of the escaping debt
the feature backlog already owes to F7 — one more way for a reminder to fail for a reason that
has nothing to do with what it says. None of that buys anything a word would not.

So: use the word, or use nothing. The due-reminder message is the task title alone, with no
prefix — it arrives from the assistant, in a chat only the assistant writes to, so there is no
reader for a pictogram to orient and nothing left for it to disambiguate.

**This rule is not build-enforced, and that is deliberate — the same way §12.5 is honest about
`IDE0290`.** Catching it would need a Unicode-range scan wired into CI, and nothing here runs
one. It is a review rule, checked by reading.

### 12.7 Deferred conventions

| Item | Trigger |
| :--- | :--- |
| Configurable timezone in the prompt and resolver | A second user, or a trip that makes it personally annoying |
| Localisation of bot messages | Same |
| Analyzer package beyond the built-in rules | When style debates start costing review time |
