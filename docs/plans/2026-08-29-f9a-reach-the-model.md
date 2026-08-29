# F9a — Reach the model

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** the owner messages the bot, the bot asks a chat model, and the model's answer comes
back. No tools yet — parsing a `create_task` call out of the answer is F9b.

**Shipping structure:** this plan merges first, on its own, as a docs-only PR — reviewed and
merged before any F9a code is written. The implementation then ships as **four** separate,
independently reviewable code PRs, in the order below. Each PR section states its own files, its
own commit(s), and its own validation method. Precedent for the split: F8 shipped its plan and its
code together in one PR and broke this repository's 1000-line budget (1243 plan + 598 code = 1841
lines); an earlier version of this document repeated that mistake at larger scale. This
restructure ships the plan alone and the code in four PRs small enough to review individually.

| PR | Ships | ~Code lines | Validation |
| :--- | :--- | ---: | :--- |
| **1** | Settings (`AiSettings`) | ~90 | `dotnet run` — boots with the key set; fails fast, naming the missing value, without it |
| **2** | The clock and the system prompt | ~120 | Unit tests only — no Docker, no new NuGet package |
| **3** | Reach the model | ~260 | `dotnet test` against the WireMock stub — cannot be run end to end yet |
| **4** | The owner gets the model's answer | ~150 | End to end — a real Telegram message in, a real OpenRouter answer out |

**Tech Stack:** .NET 10, xUnit 2.9.3, Refit 15.2.0 + Refit.HttpClientFactory 15.2.0 (new to this
repository), WireMock.Net (existing, gains a second stub endpoint),
`Microsoft.Extensions.TimeProvider.Testing` (existing in `Assistant.UnitTests`, newly referenced
by `Assistant.IntegrationTests`).

**Spec:** `docs/design/slice-1-reminders.md` §5.1 (flow), §5.2 (system prompt), §5.5 (provider
routing — corrected here), §7.1–§7.3 (testing strategy), §12.1, §12.3, §12.5, §12.6.
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F9, split here into F9a (this
plan) and F9b.

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
  `--build`, because this feature changes the WireMock stub image.
- PR budget: 1000 changed lines per PR, excluding this plan (which merges on its own, docs-only).
  This plan is split into four PRs — PR 1 ~90, PR 2 ~120, PR 3 ~260, PR 4 ~150 code lines — every
  one comfortably under budget by itself.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
  ```

---

## Verified facts this plan rests on

- `Refit` and `Refit.HttpClientFactory`'s current stable version is **15.2.0** (nuget.org flat
  container, checked 2026-08-29).
- OpenRouter's `GET https://openrouter.ai/api/v1/models` answered with 396 models on 2026-08-29.
  Slug format is `vendor/model`; `anthropic/claude-sonnet-5` and `anthropic/claude-haiku-4.5` are
  both present. Both slugs are verified; this plan invents no others.
- 2026-08-16 is a Sunday, so spec §5.2's example prompt string is internally consistent and
  usable verbatim as a test fixture (PR 2).
- `JsonNamingPolicy.SnakeCaseLower` exists in .NET 8 and later, so `max_tokens` needs no
  per-property `[JsonPropertyName]` attribute anywhere production code writes the wire types.
- `DependencyRuleTests.Interfaces_do_not_depend_on_infrastructure_libraries` in
  `tests/Assistant.UnitTests/Architecture/DependencyRuleTests.cs` already carries
  `[InlineData("Refit")]` — confirmed by reading the file before this plan was written. Its own
  remarks say it was written ahead of this feature, so it needs no change here; PR 3 gives it the
  first thing it has ever had to catch.
- **Unverified, flagged rather than assumed:** Refit 15's source generator is expected to accept
  an `internal` interface for `IChatCompletionsApi`. **Step instruction, PR 3:** if the build
  fails on the generated client, make `IChatCompletionsApi` `public` instead. It stays inside
  `Assistant.Impl`, which `Assistant.Worker` already references, so nothing leaks past the
  composition root. Record in the PR report which of the two happened.

Facts about the code this plan touches:

- `src/Assistant.Impl/Assistant.Impl.csproj` already carries
  `<InternalsVisibleTo Include="Assistant.UnitTests" />`. Every new internal type this plan adds
  under `Impl/Ai/` is constructible from `Assistant.UnitTests` the same way `LocalTimeResolver`
  already is. `Assistant.IntegrationTests` gets no such visibility — it reaches everything through
  `Assistant.Worker`'s public composition root, exactly as F5b and F7 already established.
- `AddAssistantServices()` registers `TimeProvider.System` as a singleton. .NET DI resolves the
  **last** registration for a non-enumerable dependency, so a test that calls
  `services.AddSingleton<TimeProvider>(fakeTimeProvider)` after `AddAssistantServices()` gets the
  fake, with no change to `AddAssistantServices()` itself.
- `ConfigurationExtensions.Read<T>` binds the section **named after the type**, so `AiSettings`
  reads the `AiSettings` section and throws `ConfigurationErrorsException` when the section is
  absent — so whatever defaults this plan wants a fresh clone to have must ship in
  `appsettings.json`.
- `TelegramSettings.BaseUrl` is the existing precedent for a nullable, optional base URL —
  decision D below explains why `AiSettings.BaseUrl` is not modelled the same way.
- `ITelegramUpdateHandler` stays `internal` to `Assistant.Impl.Telegram`. F7 already ruled it
  cannot move to `Assistant.Interfaces`, because it names `Telegram.Bot.Types.Update` and
  `DependencyRuleTests` forbids that reference from `Interfaces`. PR 4 modifies an existing
  implementation of it; it does not reopen that ruling.
- `tests/Assistant.WireMock/Dockerfile` copies the whole `tests/Assistant.WireMock/` directory
  before publishing, so a new `.cs` file in that project needs no Dockerfile change — only the
  `--build` the Global Constraints already call for.

---

## Decisions this plan makes — review these first

Each decision is claimed by exactly one PR below; the PR sections name their governing letters
rather than repeating this text.

### A. OpenRouter, not Anthropic — and nothing is named after a vendor — *(PR 3)*

The repository owner ruled Anthropic out of slice 1 entirely. The endpoint this feature reaches
speaks the **OpenAI chat-completions** format, which OpenRouter, OpenAI, Groq and a local Ollama
all serve, so the types are named for the wire format, not the vendor: `IChatCompletionsApi`,
`ChatCompletionsClient`, `ChatCompletionStubs`. Moving providers becomes a change to
`AiSettings.BaseUrl` and `AiSettings.Model` and nothing else.

Spec §5.5 names `IAnthropicApi`/`IOpenRouterApi`. That naming is superseded; PR 4 records it.

### B. The chat-completions format is simpler than Anthropic's here — *(PR 3)*

The system prompt is just `messages[0]` with `role: "system"`, rather than a separate top-level
`system` field the way Anthropic's Messages API shapes it. No record property in this feature is
named `System`, so the namespace-shadowing trap a `System` property would have set up next to
`System.*` never arises.

At F9b, `tool_calls[].function.arguments` arrives as a JSON **string** in this format, not a
nested object. `Assistant.Contracts` will never need a `JsonElement` to represent it — a plain
`string` deserialises and later re-parses like any other.

### C. `AiSettings` lives in `Impl/Settings/`, with the other settings — *(PR 1)*

`TelegramSettings`, `TimeSettings` and `DatabaseSettings` are all there; configuration is not an
`Ai/` concern, even though spec §3.4 places the Refit interfaces themselves in `Impl/Ai/`. The
name is `AiSettings` because this repository's convention is `<Subsystem>Settings`, and spec §3.4
already names the subsystem folder `Ai`.

### D. `BaseUrl` is required here, unlike `TelegramSettings.BaseUrl` — *(PR 1)*

Telegram's `BaseUrl` is nullable because absent means "the real Telegram" — there is exactly one
real Telegram API, so a missing value has an unambiguous meaning. There is no single "the"
chat-completions provider; that is the entire point of decision A. So `appsettings.json` ships
`https://openrouter.ai/api/v1` as a changeable default, and validation requires an absolute URI
rather than treating absence as meaningful.

### E. `IChatClient` returns `Result<string>` at F9a and `Result<ToolCall>` at F9b — *(PR 3)*

That is a modification to an existing interface, not an extension by a new class — and it is
acceptable here because `IChatClient` is a transport abstraction, not one of spec §3.6's
behaviour seams. Those seams grow by adding a class (`IAssistantTool`'s implementations,
`IScheduledJob`'s implementations); `IChatClient` has exactly one production implementation
today and will still have exactly one at F9b. `IAssistantTool` is the seam F9b actually extends.

Recorded honestly as a cost, not hidden: a caller of `IChatClient.CompleteAsync` written against
F9a's shape will not compile unchanged after F9b.

### F. `ILocalTimeResolver` grows two members, and the zone keeps one owner — *(PR 2)*

F8's own "Settled at F8" note deferred exactly this: "F9 adds a member for the current local time
when the system prompt needs one to state 'now' in the user's zone." This plan adds
`DateTimeOffset CurrentLocalTime { get; }` and `string ZoneId { get; }`. Both live on the
resolver, rather than `SystemPrompt` taking an injected `TimeZoneInfo` and a `TimeProvider`
directly, so the zone continues to have exactly one owner (`ILocalTimeResolver`) and
`SystemPrompt` continues to have exactly one collaborator.

### G. `MessageHandler` takes `IServiceScopeFactory`, not `IChatClient` — *(PR 4)*

`TelegramListener` is a singleton `BackgroundService` injecting `IEnumerable<ITelegramUpdateHandler>`,
so every handler it holds is constructed once and lives for the process's whole lifetime. A Refit
client is a typed `HttpClient`; capturing one directly in a singleton pins its message handler and
defeats `IHttpClientFactory`'s handler rotation — the same category of bug `HttpClient` gives every
singleton that holds one directly.

`DueReminderJob` already solved this identical problem for `ITaskService`, in its own doc comment:

> "Opens the scope `ITaskService` is resolved from, because this job is a singleton and the
> service depends on the scoped database context."

`MessageHandler` solves it the same way for `IChatClient`, resolving it from a fresh
`IServiceScopeFactory.CreateScope()` inside `HandleAsync` rather than through its constructor.
F10 will need the identical scope for `ITaskService`, once a captured task is actually stored.

### H. The offset formatter handles half-hour zones — *(PR 2)*

`UTC+3` when the offset's minutes are zero, `UTC+10:30` otherwise. F8's own
`Australia/Lord_Howe` fixture exists precisely because Jerusalem's round-hour offsets cannot
catch a formatter that silently drops a half-hour remainder — the same reasoning F8 gave for
testing its gap and ambiguity rules in two zones, reused here for the same class of bug.

### I. The system prompt names the configured zone twice, never a literal — *(PR 2)*

Spec §5.2's example sentence reads "All times the user gives are Jerusalem local." That second
mention becomes the configured identifier too, read from `ILocalTimeResolver.ZoneId` exactly like
the first, because a literal there would reintroduce exactly what spec §11.4 forbids — and would
do it quietly, since the first mention (inside "Current time: …") already reads from
configuration and would look correct on a glance that missed the second.

### J. The prompt's content is a unit-test concern; the integration test asserts placement, not text — *(PR 2)*

Spec §7.2 forbids duplication between the two suites. PR 2's unit test owns the prompt's content,
pinned with `FakeTimeProvider` — asserted with `Assert.Contains` against the current-time
substring rather than the full sentence; see PR 2's own steps for why `Contains` replaced an
exact-string match here. PR 3's integration test asserts only that the system prompt is
`messages[0]` with `role: "system"`, that the user's text is `messages[1]` with `role: "user"`,
and that the configured model and max-tokens went out on the wire. It never re-checks the
prompt's content, in either form.

### K. Integration tests pin the clock with `FakeTimeProvider` — *(PR 3)*

`AddAssistantServices()` registers `TimeProvider.System`; a test registers `FakeTimeProvider`
afterwards and last-wins on resolve (see "Facts about the code this plan touches," above). Spec
§7.3's requirement is that every time-based assertion be an absolute instant — this plan's
integration tests make no assertion on the prompt's time content at all (decision J keeps that in
PR 2's unit test), so nothing here strictly needs the pin. It is applied anyway in
`ChatClientTests` (PR 3), because `ChatCompletionsClient` is the first `Impl` type whose happy
path reads wall-clock time on every call, and removing real wall-clock as an input the pipeline
touches costs one line and forecloses a class of test flake before it can exist, rather than after
a first flaky run proves it possible.

### L. F7's echo test changes, and that is the point — *(PR 4)*

`TelegramListenerTests.Listener_OwnerSendsAMessage_RepliesWithTheirText` asserts the echo and must
become an assertion on the model's answer — renamed, in PR 4, to
`Listener_OwnerSendsAMessage_RepliesWithTheModelsAnswer`.

Its "message already answered" sibling needs no change beyond the new registrations to compose:
it never asserted on reply text, only on count. Its "only the owner is answered" sibling is not
fully unchanged, though — read closely, it asserts `Assert.Equal("call the bank", ...)` against
the echoed text, which stops being true once the reply is the model's answer. PR 4 drops that
text check down to `Assert.Single(sent)`, keeping the "exactly one reply, and the stranger did not
get it" assertion the test's name promises, and leaving the reply's exact content to the renamed
test — which is what spec §7.2 already asks for: one test owns one behaviour, not two owning the
same one. Call this out in the PR 4 report so nobody mistakes the trimmed assertion for a
weakened test rather than a de-duplicated one.

### M. `.env.example` loses two lines that nothing reads — *(PR 1)*

`LLM__ANTHROPIC__APIKEY` and `LLM__OPENROUTER__APIKEY` predate every naming convention in this
repository, and no code anywhere binds them — grep confirms zero references outside the file
itself. They are replaced by `AiSettings__ApiKey`, `AiSettings__Model` and `AiSettings__BaseUrl`.

### N. The default model ships as `anthropic/claude-sonnet-5` — *(PR 1)*

Verified present in OpenRouter's live model list on 2026-08-29 (see "Verified facts," above). This
is a model **slug**, not a code identifier — a slug naming a vendor is unavoidable under decision
A, since OpenRouter's own catalogue is vendor-prefixed; decision A is about type and file names,
not about the string values `appsettings.json` and `.env.example` carry. `.env.example`'s comment
names `anthropic/claude-haiku-4.5` as the cheaper alternative — also verified present. Neither
slug is invented.

### O. `AiSettings` gets no dedicated unit test file — *(PR 1)*

`tests/Assistant.UnitTests/Configuration/ConfigurationExtensionsTests.cs` already proves
`IConfiguration.Read<T>`'s entire mechanism, generically, using `TelegramSettings` as its
vehicle: an absent section throws, a mandatory value missing from an otherwise-present section
throws through `IValidatableConfig.Validate`, and a fully-populated section binds and returns
unchanged. `AiSettings` binds through that same `Read<T>`, so a settings-specific test proving
the same three facts a second time would be the duplication spec §7.2 forbids, and five tests
over `AiSettings.Validate`'s five guard clauses — one per `if` — is implementation testing, which
this repository's owner rules out on principle.

`TimeSettingsTests` is not a precedent for keeping such a file. Read closely, it asserts nothing
about a null or empty `IanaTimeZone` — that ground is exactly what `ConfigurationExtensionsTests`
already covers, generically, for every settings type. What `TimeSettingsTests` earns its place by
testing is the one thing `ConfigurationExtensionsTests` cannot reach: a value that is present,
non-empty, and still wrong — an identifier this machine's tzdata does not know, rejected by name.
`AiSettings` has no rule of that shape. Every one of its checks — an empty key, a relative URL, an
empty model, a non-positive token limit — is a guard clause, and the mechanism that runs a guard
clause and turns its failure into `ConfigurationErrorsException` is exactly what
`Read_MandatoryValueMissing_Throws` already exercises, generically, today.

`AiSettings.Validate()` itself is unchanged, and it still runs at every startup through `Read<T>`.
Dropping the automated test is not dropping the code or its verification: PR 1's own manual
`dotnet run` check — a missing `AiSettings__ApiKey` surfacing a `ConfigurationErrorsException`
naming it — is what proves `Validate()` fires in practice, and it proves it the moment `AiSettings`
exists, not three PRs later.

---

## What F9a does NOT include

- **The "typing…" indicator.** Spec §5.1 deferred it *to* F9 because F7 had no wait worth
  covering — F7's reply was an instant echo. F9a does have a wait now, so this is a fresh
  deferral, not an inherited one: it needs an `INotifier` member and a 4-second refresh loop
  cancelled when the reply lands, and it belongs with F10's *kept* reply rather than with F9a's
  throwaway prose. Recorded again in the backlog at PR 4.
- **Tools.** `IAssistantTool`, `CreateTaskTool`, `CreateTaskRequest`, `ToolCall`, tool definitions
  on the request, `tool_calls` parsing — all F9b.
- **Storing anything.** No `ITaskService.CreateAsync`, no migration, no model property. F10.
- **`FallbackChatClient`, Polly, retry, circuit breaking, the per-minute call cap** (spec §5.5,
  §5.6). PR 4 flags a question spec §5.5 now owes an answer to before F13 builds any of this,
  and does not answer it here.
- **Persisting inbound messages to `chat_messages`** (spec §5.1, deferred to F13).

---

## File Structure

28 rows, verified to match the union of all four PRs' file lists exactly
(`ImplServiceCollectionExtensions.cs` appears in both PR 1 and PR 3 — PR 1 creates
`AddAssistantAi` with a settings-only body, PR 3 only ever appends to it; `Program.cs` is touched
once, in PR 1, and never again).

```
src/Assistant.Contracts/
    ErrorCode.cs                           + ModelUnavailable, ModelReturnedNoAnswer   (PR 3)

src/Assistant.Interfaces/
    ILocalTimeResolver.cs                  + CurrentLocalTime, ZoneId                  (PR 2)
    IChatClient.cs                         new                                         (PR 3)

src/Assistant.Impl/
    Services/LocalTimeResolver.cs          + CurrentLocalTime, ZoneId                  (PR 2)
    Ai/SystemPrompt.cs                     new                                         (PR 2)
    Settings/AiSettings.cs                 new                                         (PR 1)
    Ai/ChatCompletionWire.cs               new                                         (PR 3)
    Ai/IChatCompletionsApi.cs              new                                         (PR 3)
    Ai/ChatCompletionsClient.cs            new, happy path (Commit 1); hardened (Commit 2) (PR 3)
    ImplServiceCollectionExtensions.cs     + AddAssistantAi, settings only then extended
                                              with the client                             (PR 1, PR 3)
    Telegram/MessageHandler.cs             modified                                    (PR 4)
    Assistant.Impl.csproj                  + Refit, Refit.HttpClientFactory            (PR 3)

src/Assistant.Worker/
    Program.cs                             + AddAssistantAi(...) link in the chain      (PR 1)
    appsettings.json                       + AiSettings section                        (PR 1)

Directory.Packages.props                   + Refit, Refit.HttpClientFactory            (PR 3)
.env.example                               - LLM__*, + AiSettings__*                   (PR 1)

tests/Assistant.UnitTests/
    Services/LocalTimeResolverTests.cs     + CurrentLocalTime, ZoneId tests            (PR 2)
    Ai/SystemPromptTests.cs                new                                         (PR 2)

tests/Assistant.WireMock/
    ChatCompletionStubs.cs                 new                                         (PR 3)
    Program.cs                             + ChatCompletionStubs.Install(server)       (PR 3)

tests/Assistant.IntegrationTests/
    Assistant.IntegrationTests.csproj      + Microsoft.Extensions.TimeProvider.Testing (PR 3)
    Infrastructure/WireMockFixture.cs      + chat-completion seeding and read-back     (PR 3)
    Ai/ChatClientTests.cs                  new, happy path (Commit 1); + 2 tests (Commit 2) (PR 3)
    Telegram/TelegramListenerTests.cs      modified                                    (PR 4)

docs/design/slice-1-reminders.md           Stack line, §3.4, §5.5, §7.5 corrected      (PR 4)
docs/design/2026-08-22-slice-1-feature-backlog.md
                                           F9 split into F9a (done) and F9b            (PR 4)
AGENTS.md                                  checked; no change                          (PR 4)
README.md                                  checked; no change                          (PR 4)
```

**This plan's own branch:** `feature/f9a-reach-the-model` (docs-only, cut from `main`). Suggested
branch names for the four code PRs, following this repository's `feature/f<id>-<kebab-slug>`
convention: `feature/f9a-1-ai-settings`, `feature/f9a-2-clock-and-system-prompt`,
`feature/f9a-3-reach-the-model`, `feature/f9a-4-model-answers-the-owner`. PR 1 and PR 2 touch
disjoint files and can merge in either order; PR 3 needs both (`AiSettings` and `AddAssistantAi`
from PR 1, to extend; `SystemPrompt` from PR 2); PR 4 needs PR 3's hardened `IChatClient`.

---
## PR 1: Settings

**Decisions this PR carries:** C, D, M, N, O.

**Files:**
- Create: `src/Assistant.Impl/Settings/AiSettings.cs`
- Modify: `src/Assistant.Worker/appsettings.json`
- Modify: `.env.example`
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`
- Modify: `src/Assistant.Worker/Program.cs`

**Produces:** `AiSettings`, and a minimal `AddAssistantAi` that registers it — extended by PR 3,
never touched in `Program.cs` again after this PR.

**The open question this PR has to resolve:** `AiSettings` is read at startup, but
`AddAssistantAi`'s eventual body — the Refit client, `SystemPrompt` — doesn't exist until PR 3.
The rejected option was reading `AiSettings` in `Program.cs` and discarding the result until PR 3
had something to hand it to: that line would exist only to be deleted once `AddAssistantAi`
existed, which is a modification wearing an extension's clothes, and it breaks a standing owner
preference for code that is open for extension, closed for modification — write it once, then
only ever add to it. So this PR creates the real `AddAssistantAi` now, with a body that does only
what this PR needs (register the validated settings). `Program.cs` gets exactly one line, added to
the existing chain, matching `TelegramSettings`'s and `TimeSettings`'s own precedent exactly:
`builder.Configuration.Read<T>()` threaded straight in as the argument to the registration method
that consumes it. `Read<T>` validates as a *side effect* of binding — it calls
`settings.Validate()` before returning — so this one line is what makes this PR's fail-fast check
(Step 6, below) work, with no discard and no bespoke mechanism. PR 3 only ever appends to
`AddAssistantAi`'s body; PR 4 needs no `Program.cs` change at all (confirmed in PR 4, below).

- [ ] **Step 1: Write `AiSettings`**

No failing test precedes this step. Decision O explains why: `ConfigurationExtensionsTests`
already proves `Read<T>`'s mechanism generically, using `TelegramSettings` as its vehicle, and
`AiSettings`'s own checks are guard clauses of exactly the shape that test already exercises — a
settings-specific copy of the same three facts would be the duplication spec §7.2 forbids.
`Validate()` itself runs unchanged; this PR's own manual `dotnet run` check (Step 6, below) is
what proves it fires in practice, not a dedicated unit test.

Create `src/Assistant.Impl/Settings/AiSettings.cs`:

```csharp
using System.Configuration;
using Assistant.Interfaces;

namespace Assistant.Impl.Settings;

/// <summary>
/// Configuration for the chat-completions endpoint the assistant reaches for an answer.
/// </summary>
/// <remarks>
/// One provider serves the whole assistant. Unlike <see cref="TelegramSettings.BaseUrl"/>,
/// <see cref="BaseUrl"/> here is required: there is no single "the" chat-completions provider
/// the way there is a single real Telegram API, so a value must always be supplied, and
/// <c>appsettings.json</c> ships OpenRouter's address as a changeable default (decision D).
/// </remarks>
public sealed class AiSettings : IValidatableConfig
{
    /// <summary>
    /// The API key sent as a bearer token on every request.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// The chat-completions endpoint's base address, such as
    /// <c>https://openrouter.ai/api/v1</c>.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// The model slug to request, such as <c>anthropic/claude-sonnet-5</c>.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// The maximum number of tokens the model may return.
    /// </summary>
    public required int MaxTokens { get; init; }

    /// <inheritdoc/>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(ApiKey)} is missing or empty.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(BaseUrl)} is '{BaseUrl}', which is not an "
                + "absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(Model)} is missing or empty.");
        }

        if (MaxTokens <= 0)
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(MaxTokens)} is {MaxTokens}, which is not "
                + "positive.");
        }
    }
}
```

- [ ] **Step 2: Ship the default and update `.env.example`**

`src/Assistant.Worker/appsettings.json` — add a sibling to `TimeSettings`:

```json
  "AiSettings": {
    "BaseUrl": "https://openrouter.ai/api/v1",
    "Model": "anthropic/claude-sonnet-5",
    "MaxTokens": 1024
  }
```

`ApiKey` is deliberately absent — like `TelegramSettings.BotToken`, it is a secret and never
ships a default; a fresh clone without `AiSettings__ApiKey` set fails `Validate()` naming exactly
that (Step 6, below).

`.env.example` — replace the two lines decision M retires:

```
# Anthropic API key (primary LLM provider)
LLM__ANTHROPIC__APIKEY=

# OpenRouter API key (fallback provider; optional)
LLM__OPENROUTER__APIKEY=
```

with:

```
# API key for the chat-completions endpoint AiSettings__BaseUrl points at.
AiSettings__ApiKey=

# Model slug to request. Defaults to anthropic/claude-sonnet-5; the cheaper
# anthropic/claude-haiku-4.5 is a good alternative.
AiSettings__Model=

# Chat-completions endpoint base address. Defaults to OpenRouter; point this
# at any OpenAI-compatible endpoint instead (OpenAI, Groq, a local Ollama).
AiSettings__BaseUrl=
```

- [ ] **Step 3: Create `AddAssistantAi`, registering only the settings**

Add to `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`, after `AddAssistantTime`:

```csharp
    /// <summary>
    /// Registers the chat-completions endpoint's settings.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <param name="settings">
    /// Validated chat-model configuration. Read it with <c>IConfiguration.Read</c> so a missing
    /// key or an unusable base address stops the host here, while it is composing, rather than at
    /// the first message the owner sends.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Registers only <see cref="AiSettings"/> for now; the chat-completions client itself is
    /// added to this method's body once there is something to build it from
    /// (<c>IChatCompletionsApi</c>, <c>SystemPrompt</c>). This method's signature, and the
    /// settings registration above, do not change when that happens — only the body grows.
    /// </remarks>
    public static IServiceCollection AddAssistantAi(
        this IServiceCollection services, AiSettings settings)
    {
        services.AddSingleton(settings);
        return services;
    }
```

No new `using` is needed — `Assistant.Impl.Settings` is already imported in this file, for
`TelegramSettings` and `TimeSettings`.

- [ ] **Step 4: Wire `AddAssistantAi` into `Program.cs`'s existing chain**

Add one link, matching `AddAssistantTime`'s own call shape exactly — `Read<AiSettings>()` threaded
straight in as the argument, never stored, never discarded:

```csharp
builder.Services.AddAssistantRepository(builder.Configuration.Read<DatabaseSettings>().ConnectionString)
                .AddAssistantServices()
                .AddAssistantTime(builder.Configuration.Read<TimeSettings>())
                .AddAssistantAi(builder.Configuration.Read<AiSettings>())
                .AddAssistantScheduler()
                .AddAssistantListener();
```

**Placement, chosen deliberately:** after `AddAssistantTime`, before `AddAssistantScheduler` and
`AddAssistantListener`. `Program.cs` is not touched again after this PR — PR 3 only ever appends to
`AddAssistantAi`'s body, and PR 4 needs no `Program.cs` change at all — so this position has to be
right the first time. From PR 3 onward, `AddAssistantAi`'s own `<remarks>` document that it
requires `AddAssistantTime` for the `ILocalTimeResolver` `SystemPrompt` reads from; placing the
call after `AddAssistantTime` keeps the chain reading top to bottom as a dependency order, the same
convention `AddAssistantListener`'s own `<remarks>` already follow ("Requires `AddAssistantTelegram`
… and `AddAssistantServices`…"). .NET's DI does not actually require this — a singleton's
dependencies resolve lazily at construction, not at registration — so it is a readability
convention this file already has, not a correctness requirement.

**The `send-test-message` diagnostic is unaffected.** That branch (`Program.cs`, before this
chain) builds a minimal host and returns early, before this chain ever runs — so a missing
`AiSettings__ApiKey` cannot break it:
`DOTNET_ENVIRONMENT=Development dotnet run --project src/Assistant.Worker -- send-test-message`
still works with no `AiSettings` configured at all, exactly as it did before this PR.

- [ ] **Step 5: Confirm nothing broke**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
```

Expected: zero warnings, 37 passed — the F8 baseline, unchanged (decision O: no dedicated
`AiSettings` test file).

- [ ] **Step 6: Prove the host boots with the key set, and fails fast without it**

Assumes a local `.env`/user secrets already carry `TelegramSettings__BotToken` and
`TelegramSettings__OwnerChatId`, per `AGENTS.md`'s "Run locally" section — the same assumption
F8's own Task 3 Step 7 made.

```bash
DatabaseSettings__ConnectionString="Host=localhost;Port=1;Database=x;Username=x;Password=x" \
AiSettings__ApiKey="test-key" \
DOTNET_ENVIRONMENT=Development \
  dotnet run --project src/Assistant.Worker
```

Expected: it fails trying to reach Postgres on port 1, **not** on configuration — proving the
shipped `AiSettings` defaults (`BaseUrl`, `Model`, `MaxTokens`) plus a supplied `ApiKey` are
enough to compose and pass `Validate()`. Stop the process once you have seen the error.

Then, omitting `AiSettings__ApiKey`:

```bash
DatabaseSettings__ConnectionString="Host=localhost;Port=1;Database=x;Username=x;Password=x" \
DOTNET_ENVIRONMENT=Development \
  dotnet run --project src/Assistant.Worker
```

Expected: `System.Configuration.ConfigurationErrorsException: AiSettings.ApiKey is missing or
empty.`, thrown before anything touches Postgres — the process never reaches `builder.Build()`.
Record both outputs in the PR report.

- [ ] **Step 7: Commit**

```bash
git add src/Assistant.Impl/Settings/AiSettings.cs src/Assistant.Worker/appsettings.json \
        .env.example src/Assistant.Impl/ImplServiceCollectionExtensions.cs \
        src/Assistant.Worker/Program.cs
git commit
```

Message:

```
feat: add AiSettings and a minimal AddAssistantAi

AiSettings joins TelegramSettings, TimeSettings and DatabaseSettings in
Impl/Settings, validated the same way: an absolute BaseUrl, a non-empty
Model, a non-empty ApiKey and a positive MaxTokens fail fast at startup
rather than on the first message the owner sends. AddAssistantAi ships now,
registering only the settings, rather than Program.cs threading a
throwaway read until the client exists -- its body grows in a later
change, but its signature and this first registration do not.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

---
## PR 2: The clock and the system prompt

**Decisions this PR carries:** F, H, I, J.

**Validation:** unit tests only —

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
```

No Docker, no new NuGet package. This PR cannot be validated by running the app — `SystemPrompt`
has no caller until PR 3 builds `ChatCompletionsClient` — and that is accepted: this PR exists to
get the clock and the prompt text right in isolation, not to prove the app boots.

`ILocalTimeResolver` and `LocalTimeResolver` gain `CurrentLocalTime` and `ZoneId`; a new
`src/Assistant.Impl/Ai/SystemPrompt.cs` builds the text every call to the model starts with.

**Files:**
- Modify: `src/Assistant.Interfaces/ILocalTimeResolver.cs`
- Modify: `src/Assistant.Impl/Services/LocalTimeResolver.cs`
- Modify: `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`
- Create: `src/Assistant.Impl/Ai/SystemPrompt.cs`
- Create: `tests/Assistant.UnitTests/Ai/SystemPromptTests.cs`

**Produces:** `ILocalTimeResolver.CurrentLocalTime`, `ILocalTimeResolver.ZoneId`,
`internal sealed class SystemPrompt(ILocalTimeResolver clock)`.

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
time regardless of offset (the same reasoning `LocalTimeResolverTests` already documents on
`Resolve_AnyTime_ReturnsTheInstantOnUtc`), so without the second assertion a resolver that
returned `now` unconverted, still on UTC's zero offset, would pass the first line and hide the bug.

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

Expected: 17 passed (the 15 F8 already established, plus these 2).

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
    /// Lord Howe's one-off half-hour daylight shift runs 2026-10-04 to 2026-04-05 (the F8 plan's
    /// verified table). 2026-08-16 falls outside that window, so the zone is on its year-round
    /// base offset, standard time, UTC+10:30 -- not the shifted UTC+11.
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
substring, not `Assert.Equal` against the whole sentence. Rationale: the prompt's trailing
instructional prose ("All times the user gives are … Return absolute local ISO-8601 datetimes
with no offset.") is expected to be rewritten during F9b, and an exact-string assertion would
break on every wording change while catching nothing extra. `Contains` still fails if the clock is
wrong, the zone is hardcoded, or the half-hour offset formatting regresses — the three things
these tests exist to catch — without coupling the suite to prose PR 2 does not own.

Reasoning behind the second fixture's instant, spelled out per the task brief's own requirement:
2026-08-16T20:40:00Z is 07:10 local in `Australia/Lord_Howe` (UTC+10:30 applied to 20:40 rolls
past midnight to the next day) — Monday 17 August, since 16 August 2026 is a Sunday. Lord Howe's
DST year runs 2026-10-04 (spring forward) to 2026-04-05 (fall back, following year) per F8's own
verified table; 16 August sits in neither direction of that window, so the zone is on its
year-round standard offset, UTC+10:30, not the DST UTC+11.

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
/// The zone is read from <see cref="ILocalTimeResolver.ZoneId"/> rather than named as a literal
/// (spec §11.4), and it appears twice in the built text (decision I) so that editing either
/// mention into a hardcoded zone leaves the other visibly disagreeing with it.
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

Expected: zero warnings, 41 total (the 37 baseline after F8, plus the 4 this PR adds).

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
user's zone. SystemPrompt is that something: it builds spec 5.2's prompt
text, naming the configured zone twice rather than once, so a literal
creeping into either mention would leave the two visibly disagreeing.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

---
## PR 3: Reach the model

**Decisions this PR carries:** A, B, E, K.

**Consumes:** `AiSettings` and the minimal `AddAssistantAi` (PR 1), `SystemPrompt` (PR 2).
**Produces:** `IChatClient`, `ChatCompletionsClient`, the extended `AddAssistantAi`,
`ErrorCode.ModelUnavailable`, `ErrorCode.ModelReturnedNoAnswer`.

`AiSettings` already has a consumer, from PR 1 — this PR **extends** `AddAssistantAi`'s existing
body, it does not create the method. `SystemPrompt` gets its first real caller here
(`ChatCompletionsClient`). The wire types, the Refit interface, `IChatClient`, and the
WireMock/integration-test infrastructure all ship in this one PR too — but as **two commits**, not
one; **do not merge them into one commit.**

**Why two commits:** Commit 1 ships a happy-path-only `ChatCompletionsClient` with **no `ILogger`
parameter** (unreferenced, it would trip CS9113) and **no reference to `ErrorCode`** — a provider
failure or an empty `choices` array is left to crash, same as any unhandled exception. That is
deliberate, applying the Global Constraints' "never reference a symbol ahead of the step that
needs it" across commits instead of across PRs. Commit 2 appends `ModelUnavailable` and
`ModelReturnedNoAnswer` to `ErrorCode`, hardens the client, and its own tests fail first on a
genuine `Refit.ApiException` (500) and an `ArgumentOutOfRangeException` (empty `choices`) — proof
the crash Commit 1 left in place was real, not merely asserted. Mirrors F8's own Task 1/Task 2
split, where Task 1 deliberately left a bug for Task 2 to fix.

**Validation:** `dotnet test` against the WireMock stub, after both commits:

```bash
docker compose -f compose.test.yaml up -d --build
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~ChatClientTests"
docker compose -f compose.test.yaml down
```

This PR cannot be validated by running the app, because nothing calls `IChatClient` until PR 4
wires it into `MessageHandler`. The owner has explicitly accepted this — this PR's job is proving
the client is correct against a stub, not proving the whole pipeline works.

### Commit 1: the happy path

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Assistant.Impl/Assistant.Impl.csproj`
- Modify: `tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj`
- Create: `tests/Assistant.WireMock/ChatCompletionStubs.cs`
- Modify: `tests/Assistant.WireMock/Program.cs`
- Modify: `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`
- Create: `tests/Assistant.IntegrationTests/Ai/ChatClientTests.cs`
- Create: `src/Assistant.Impl/Ai/ChatCompletionWire.cs`
- Create: `src/Assistant.Impl/Ai/IChatCompletionsApi.cs`
- Create: `src/Assistant.Interfaces/IChatClient.cs`
- Create: `src/Assistant.Impl/Ai/ChatCompletionsClient.cs` (happy path only)
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`

- [ ] **Step 1: Add the two packages**

`Directory.Packages.props` — insert alphabetically, between `Npgsql.EntityFrameworkCore.PostgreSQL`
and `Respawn`:

```xml
    <PackageVersion Include="Refit" Version="15.2.0" />
    <PackageVersion Include="Refit.HttpClientFactory" Version="15.2.0" />
```

`src/Assistant.Impl/Assistant.Impl.csproj` — insert alphabetically, between
`Microsoft.Extensions.Logging.Abstractions` and `System.Configuration.ConfigurationManager`:

```xml
    <PackageReference Include="Refit" />
    <PackageReference Include="Refit.HttpClientFactory" />
```

`tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj` — insert alphabetically,
between `coverlet.collector` and `Microsoft.NET.Test.Sdk` (needed for `FakeTimeProvider`,
decision K):

```xml
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
```

```bash
dotnet restore
```

Expected: restores clean. Nothing references the new packages yet, so nothing else changes.

- [ ] **Step 2: Add the chat-completions stub to the WireMock image**

Create `tests/Assistant.WireMock/ChatCompletionStubs.cs`:

```csharp
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Assistant.WireMock;

/// <summary>
/// The chat-completions endpoint this stub answers.
/// </summary>
/// <remarks>
/// The path is <c>/chat/completions</c> with no prefix: tests point <c>AiSettings.BaseUrl</c> at
/// this fixture's own address directly, while production points it at
/// <c>https://openrouter.ai/api/v1</c>, which already carries the version segment. The default
/// mapping answers at weak priority (100) so a locally-run worker never logs "No matching mapping
/// found"; tests install a stronger-priority mapping of their own.
/// </remarks>
internal static class ChatCompletionStubs
{
    private const string DefaultAnswerResponse = """
        {"choices":[{"message":{"role":"assistant","content":"Stubbed answer."}}]}
        """;

    /// <summary>
    /// Installs the chat-completions mapping on the given server.
    /// </summary>
    /// <param name="server">The running stub server.</param>
    public static void Install(WireMockServer server)
    {
        server
            .Given(Request.Create().WithPath("/chat/completions").UsingPost())
            .AtPriority(100)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(DefaultAnswerResponse));
    }
}
```

Modify `tests/Assistant.WireMock/Program.cs`, adding one call:

```csharp
using Assistant.WireMock;
using WireMock.Server;
using WireMock.Settings;

using var server = WireMockServer.Start(new WireMockServerSettings
{
    Urls = ["http://0.0.0.0:8080"],
    StartAdminInterface = true,
});

TelegramStubs.Install(server);
ChatCompletionStubs.Install(server);

Console.WriteLine("Stub API listening on http://0.0.0.0:8080");

await Task.Delay(Timeout.Infinite);
```

- [ ] **Step 3: Extend `WireMockFixture` with chat-completion seeding and read-back**

`PutMappingAsync` is generalised to take the path and status code, so both the existing Telegram
seeder and the new chat-completions seeders share it — the Telegram-specific `{"ok":true,"result":...}`
envelope moves to `SeedUpdatesAsync`'s own call sites, since a chat-completions response carries no
such envelope. Replace the full contents of
`tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs` with:

```csharp
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>
/// The stub API service defined in <c>compose.test.yaml</c>.
/// </summary>
/// <remarks>
/// The server runs in its own container, so nothing here can read its in-memory state. Requests
/// are read back over the admin API instead, and cleared between tests the way Respawn clears
/// tables — a shared stub accumulates requests exactly as a shared database accumulates rows.
/// </remarks>
public sealed class WireMockFixture : IAsyncLifetime
{
    private const string DefaultUrl = "http://localhost:58080";

    private static readonly Guid PendingUpdatesMapping =
        new("f7000000-0000-0000-0000-000000000001");

    private static readonly Guid DrainedUpdatesMapping =
        new("f7000000-0000-0000-0000-000000000002");

    private static readonly Guid ChatCompletionMapping =
        new("f9a00000-0000-0000-0000-000000000001");

    private readonly HttpClient _http = new();

    /// <summary>
    /// The stub's base address.
    /// </summary>
    /// <value>
    /// The value of <c>ASSISTANT_TEST_STUB</c> when set, otherwise the fixed compose port.
    /// </value>
    public string Url { get; } =
        Environment.GetEnvironmentVariable("ASSISTANT_TEST_STUB") ?? DefaultUrl;

    /// <summary>
    /// Waits until the stub answers on its admin API.
    /// </summary>
    /// <returns>A task that completes once the stub is ready.</returns>
    /// <exception cref="InvalidOperationException">
    /// The stub did not answer within the 60 second deadline.
    /// </exception>
    public async Task InitializeAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await _http.GetAsync($"{Url}/__admin/mappings");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException(
            "The stub API did not become available within 60s. Run: docker compose -f compose.test.yaml up -d",
            last);
    }

    /// <summary>
    /// Forgets every request the stub has received.
    /// </summary>
    /// <returns>A task that completes once the request log is empty and any seeded mapping is gone.</returns>
    public async Task ResetAsync()
    {
        foreach (var id in new[] { PendingUpdatesMapping, DrainedUpdatesMapping, ChatCompletionMapping })
        {
            (await _http.DeleteAsync($"{Url}/__admin/mappings/{id}")).Dispose();
        }

        (await _http.DeleteAsync($"{Url}/__admin/requests")).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Returns the send-message requests the stub received, in order.
    /// </summary>
    /// <returns>One payload per captured request.</returns>
    public async Task<IReadOnlyList<SendMessagePayload>> SentMessagesAsync()
    {
        var entries = await _http.GetFromJsonAsync<List<AdminLogEntry>>($"{Url}/__admin/requests")
                      ?? [];

        return entries
            .Where(entry => entry.Request.Path.EndsWith("/sendMessage", StringComparison.Ordinal)
                            && entry.Request.Method == "POST")
            .Select(entry => JsonSerializer.Deserialize<SendMessagePayload>(entry.Request.Body)!)
            .ToList();
    }

    /// <summary>
    /// Returns the chat-completions requests the stub received, in order.
    /// </summary>
    /// <returns>One payload per captured request.</returns>
    public async Task<IReadOnlyList<ChatCompletionRequestPayload>> ChatCompletionRequestsAsync()
    {
        var entries = await _http.GetFromJsonAsync<List<AdminLogEntry>>($"{Url}/__admin/requests")
                      ?? [];

        return entries
            .Where(entry => entry.Request.Path.EndsWith("/chat/completions", StringComparison.Ordinal)
                            && entry.Request.Method == "POST")
            .Select(entry => JsonSerializer.Deserialize<ChatCompletionRequestPayload>(entry.Request.Body)!)
            .ToList();
    }

    /// <summary>
    /// Makes the stub serve the given updates to the next getUpdates poll.
    /// </summary>
    /// <param name="updates">The updates to serve, in the order Telegram would.</param>
    /// <returns>A task that completes once both mappings are installed.</returns>
    /// <remarks>
    /// Two mappings, drained by the offset in the request body rather than by a call
    /// count: once the caller polls with an offset past the last update it gets an
    /// empty result and keeps getting one, which is what real Telegram does. A
    /// listener that never advances its offset therefore keeps being served the same
    /// updates, which is the defect this shape exists to expose.
    /// </remarks>
    public async Task SeedUpdatesAsync(params InboundUpdate[] updates)
    {
        var pending = new JsonArray(updates.Select(u => (JsonNode)new JsonObject
        {
            ["update_id"] = u.UpdateId,
            ["message"] = new JsonObject
            {
                ["message_id"] = u.UpdateId,
                ["date"] = 1756000000L,
                ["chat"] = new JsonObject { ["id"] = u.ChatId, ["type"] = "private" },
                ["text"] = u.Text,
            },
        }).ToArray());

        var nextOffset = updates.Max(u => u.UpdateId) + 1;

        await PutMappingAsync(PendingUpdatesMapping, "/bot*/getUpdates", priority: 10,
            bodyPattern: null, statusCode: 200,
            responseBody: new JsonObject { ["ok"] = true, ["result"] = pending }, delayMs: null);

        await PutMappingAsync(DrainedUpdatesMapping, "/bot*/getUpdates", priority: 1,
            bodyPattern: $"*\"offset\":{nextOffset}*", statusCode: 200,
            responseBody: new JsonObject { ["ok"] = true, ["result"] = new JsonArray() },
            delayMs: 1000);
    }

    /// <summary>
    /// Makes the stub answer the next chat-completions request with the given answer text.
    /// </summary>
    /// <param name="answer">The model's answer.</param>
    /// <returns>A task that completes once the mapping is installed.</returns>
    public Task SeedChatAnswerAsync(string answer) =>
        PutMappingAsync(ChatCompletionMapping, "/chat/completions", priority: 1,
            bodyPattern: null, statusCode: 200,
            responseBody: new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = answer },
                }),
            },
            delayMs: null);

    /// <summary>
    /// Makes the stub answer the next chat-completions request with a server error.
    /// </summary>
    /// <returns>A task that completes once the mapping is installed.</returns>
    public Task SeedChatFailureAsync() =>
        PutMappingAsync(ChatCompletionMapping, "/chat/completions", priority: 1,
            bodyPattern: null, statusCode: 500,
            responseBody: new JsonObject { ["error"] = "stubbed provider failure" },
            delayMs: null);

    /// <summary>
    /// Makes the stub answer the next chat-completions request with no candidate answers.
    /// </summary>
    /// <returns>A task that completes once the mapping is installed.</returns>
    public Task SeedChatNoAnswerAsync() =>
        PutMappingAsync(ChatCompletionMapping, "/chat/completions", priority: 1,
            bodyPattern: null, statusCode: 200,
            responseBody: new JsonObject { ["choices"] = new JsonArray() },
            delayMs: null);

    /// <summary>
    /// Waits until the stub has received at least the given number of messages.
    /// </summary>
    /// <param name="count">How many messages to wait for.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <returns>Every message received, which may be more than requested.</returns>
    /// <exception cref="TimeoutException">Too few messages arrived in time.</exception>
    public async Task<IReadOnlyList<SendMessagePayload>> WaitForSentMessagesAsync(
        int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var sent = await SentMessagesAsync();

            if (sent.Count >= count)
            {
                return sent;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Expected at least {count} message(s) within {timeout.TotalSeconds:0.#}s; "
            + $"got {(await SentMessagesAsync()).Count}.");
    }

    /// <summary>
    /// Releases the HTTP client.
    /// </summary>
    /// <returns>A completed task.</returns>
    public Task DisposeAsync()
    {
        _http.Dispose();
        return Task.CompletedTask;
    }

    private async Task PutMappingAsync(
        Guid id, string path, int priority, string? bodyPattern, int statusCode,
        JsonObject responseBody, int? delayMs)
    {
        var request = new JsonObject
        {
            ["Path"] = new JsonObject
            {
                ["Matchers"] = new JsonArray(new JsonObject
                {
                    ["Name"] = "WildcardMatcher",
                    ["Pattern"] = path,
                }),
            },
            ["Methods"] = new JsonArray("POST"),
        };

        if (bodyPattern is not null)
        {
            request["Body"] = new JsonObject
            {
                ["Matcher"] = new JsonObject
                {
                    ["Name"] = "WildcardMatcher",
                    ["Pattern"] = bodyPattern,
                },
            };
        }

        var response = new JsonObject
        {
            ["StatusCode"] = statusCode,
            ["Headers"] = new JsonObject { ["Content-Type"] = "application/json" },
            ["Body"] = responseBody.ToJsonString(),
        };

        if (delayMs is not null)
        {
            response["Delay"] = delayMs;
        }

        var mapping = new JsonObject
        {
            ["Guid"] = id.ToString(),
            ["Priority"] = priority,
            ["Request"] = request,
            ["Response"] = response,
        };

        using var content = new StringContent(
            mapping.ToJsonString(), Encoding.UTF8, "application/json");

        (await _http.PostAsync($"{Url}/__admin/mappings", content)).EnsureSuccessStatusCode();
    }

    private sealed record AdminLogEntry(
        [property: JsonPropertyName("Request")] AdminRequest Request);

    private sealed record AdminRequest(
        [property: JsonPropertyName("Path")] string Path,
        [property: JsonPropertyName("Method")] string Method,
        [property: JsonPropertyName("Body")] string Body);
}

/// <summary>
/// The body of a Telegram <c>sendMessage</c> request.
/// </summary>
/// <param name="ChatId">The recipient.</param>
/// <param name="Text">The message body as it went over the wire.</param>
/// <param name="ParseMode">How Telegram is told to interpret the text.</param>
public sealed record SendMessagePayload(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parse_mode")] string ParseMode)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>
    /// Null when the request carried exactly the three expected fields. Populated otherwise, which
    /// makes <c>Assert.Equivalent(strict: true)</c> fail — without this, extra fields are silently
    /// discarded during deserialisation and the assertion cannot see them.
    /// </value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// An inbound Telegram update, as <see cref="WireMockFixture.SeedUpdatesAsync"/> serves it.
/// </summary>
/// <param name="UpdateId">Telegram's identifier for the update.</param>
/// <param name="ChatId">The chat the message appears to come from.</param>
/// <param name="Text">The message body.</param>
public sealed record InboundUpdate(int UpdateId, long ChatId, string Text);

/// <summary>
/// The body of a chat-completions request, as the assistant sends it.
/// </summary>
/// <param name="Model">The requested model slug.</param>
/// <param name="Messages">The conversation sent, system prompt first.</param>
/// <param name="MaxTokens">The token limit sent with the request.</param>
public sealed record ChatCompletionRequestPayload(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatCompletionMessagePayload> Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>Null when the request carried exactly the three expected fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// One message within a captured chat-completions request.
/// </summary>
/// <param name="Role">Who is speaking: <c>system</c> or <c>user</c>.</param>
/// <param name="Content">What was said.</param>
public sealed record ChatCompletionMessagePayload(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content)
{
    /// <summary>
    /// Any field on the wire that this message does not name.
    /// </summary>
    /// <value>Null when the message carried exactly the two expected fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
```

- [ ] **Step 4: Write the failing happy-path tests**

Create `tests/Assistant.IntegrationTests/Ai/ChatClientTests.cs`:

```csharp
using System.Globalization;
using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.IntegrationTests.Ai;

/// <summary>
/// Test class for <see cref="IChatClient"/>.
/// </summary>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class ChatClientTests(WireMockFixture wireMock) : IAsyncLifetime
{
    private const string Model = "test-model";
    private const int MaxTokens = 100;

    private ServiceProvider _provider = null!;

    private IChatClient _sut = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddAssistantServices();
        services.AddSingleton<TimeProvider>(
            new FakeTimeProvider(DateTimeOffset.Parse("2026-08-16T20:40:00Z", CultureInfo.InvariantCulture)));
        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
        services.AddAssistantAi(new AiSettings
        {
            ApiKey = "test-key", BaseUrl = wireMock.Url, Model = Model, MaxTokens = MaxTokens,
        });
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<IChatClient>();

        await wireMock.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When the provider answers with a candidate message
    /// And the model is asked
    /// Then its text comes back as the result's value.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_ProviderAnswers_ReturnsItsText()
    {
        // Arrange
        await wireMock.SeedChatAnswerAsync("Noted -- I will remind you.");

        // Act
        var result = await _sut.CompleteAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Noted -- I will remind you.", result.Value);
    }

    /// <summary>
    /// When the model is asked
    /// Then the system prompt is sent as the first message with role system
    /// And the owner's text is sent as the second message with role user
    /// And the configured model and token limit go on the wire.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_AnyText_PlacesThePromptAndTheModelCorrectlyOnTheWire()
    {
        // Arrange
        await wireMock.SeedChatAnswerAsync("Noted.");

        // Act
        await _sut.CompleteAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        var request = Assert.Single(await wireMock.ChatCompletionRequestsAsync());
        Assert.Equal(Model, request.Model);
        Assert.Equal(MaxTokens, request.MaxTokens);
        Assert.Equal(2, request.Messages.Count);
        Assert.Equal("system", request.Messages[0].Role);
        Assert.Equal("user", request.Messages[1].Role);
        Assert.Equal("call the bank tomorrow at 10", request.Messages[1].Content);
    }
}
```

This test asserts placement and wire values only, never the prompt's text — decision J keeps that
in PR 2's `SystemPromptTests`. The clock is pinned per decision K even though no assertion here
reads it, so the request pipeline's only unpinned input is removed before any test needs it to be.

- [ ] **Step 5: Bring up the stub with the new image, and watch the tests fail**

```bash
docker compose -f compose.test.yaml up -d --build
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~ChatClientTests"
```

Expected: does not compile. `IChatClient` reference aside, `AddAssistantAi` does not exist yet.

- [ ] **Step 6: Write the wire types, the Refit interface, `IChatClient` and its happy-path client**

Create `src/Assistant.Impl/Ai/ChatCompletionWire.cs`:

```csharp
namespace Assistant.Impl.Ai;

/// <summary>
/// A chat-completions request, shaped for the OpenAI wire format OpenRouter, OpenAI, Groq
/// and a local Ollama all serve.
/// </summary>
/// <param name="Model">The model slug to request, such as <c>anthropic/claude-sonnet-5</c>.</param>
/// <param name="Messages">The conversation so far, system prompt first.</param>
/// <param name="MaxTokens">The maximum number of tokens the model may return.</param>
internal sealed record ChatCompletionRequest(
    string Model, IReadOnlyList<ChatCompletionMessage> Messages, int MaxTokens);

/// <summary>
/// One turn in a chat-completions conversation, on either side of the wire.
/// </summary>
/// <param name="Role">
/// Who is speaking: <c>system</c>, <c>user</c>, or <c>assistant</c>.
/// </param>
/// <param name="Content">
/// What was said, or <see langword="null"/> on a response that carries only tool calls (F9b) —
/// harmless now, since F9a never sends or reads a null one.
/// </param>
internal sealed record ChatCompletionMessage(string Role, string? Content);

/// <summary>
/// A chat-completions response, carrying every answer the model offered.
/// </summary>
/// <param name="Choices">
/// The model's candidate answers. Empty when the provider accepted the request but produced
/// nothing.
/// </param>
internal sealed record ChatCompletionResponse(IReadOnlyList<ChatCompletionChoice> Choices);

/// <summary>
/// One candidate answer within a chat-completions response.
/// </summary>
/// <param name="Message">The answer itself, in the same shape a request message takes.</param>
internal sealed record ChatCompletionChoice(ChatCompletionMessage Message);
```

One file rather than four, because these four types are one wire contract and meaningless apart —
`Contracts/Result.cs` is this repository's own precedent for two related types sharing a file, and
this is the same reasoning stretched to four.

Create `src/Assistant.Impl/Ai/IChatCompletionsApi.cs`:

```csharp
using Refit;

namespace Assistant.Impl.Ai;

/// <summary>
/// The OpenAI-shaped chat-completions endpoint, reachable at any provider that speaks it.
/// </summary>
/// <remarks>
/// Named for the wire format, not a vendor (decision A): OpenRouter, OpenAI, Groq and a local
/// Ollama all serve this same shape, so moving providers is a change to
/// <see cref="Assistant.Impl.Settings.AiSettings.BaseUrl"/> and nothing in this interface.
/// </remarks>
internal interface IChatCompletionsApi
{
    /// <summary>
    /// Sends a chat-completions request and returns the provider's response.
    /// </summary>
    /// <param name="request">The model, conversation and token limit to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The provider's response, including every candidate answer it offered.</returns>
    [Post("/chat/completions")]
    Task<ChatCompletionResponse> CompleteAsync([Body] ChatCompletionRequest request, CancellationToken ct);
}
```

Create `src/Assistant.Interfaces/IChatClient.cs`:

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// Reaches a chat model with the owner's text and returns its answer.
/// </summary>
/// <remarks>
/// A transport abstraction, not one of spec §3.6's behaviour seams (decision E): this interface
/// changes shape at F9b, when <c>CompleteAsync</c> starts returning <c>Result&lt;ToolCall&gt;</c>
/// so a tool invocation can be parsed out of the answer. F9b's growing seam is
/// <c>IAssistantTool</c>, not this one.
/// </remarks>
public interface IChatClient
{
    /// <summary>
    /// Sends the owner's text to the configured model and returns its answer.
    /// </summary>
    /// <param name="userText">What the owner said.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The model's answer, or the reason it could not be reached.
    /// </returns>
    Task<Result<string>> CompleteAsync(string userText, CancellationToken ct);
}
```

Create `src/Assistant.Impl/Ai/ChatCompletionsClient.cs` — happy path only, per this commit's own
scope, described above: no `ILogger`, no `ErrorCode`.

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Ai;

/// <summary>
/// Reaches the configured chat-completions endpoint with the owner's text and the system
/// prompt, and returns the model's answer.
/// </summary>
/// <param name="api">The Refit client for the chat-completions endpoint.</param>
/// <param name="prompt">Builds the system prompt sent as the first message.</param>
/// <param name="settings">The configured model and token limit.</param>
internal sealed class ChatCompletionsClient(
    IChatCompletionsApi api, SystemPrompt prompt, AiSettings settings) : IChatClient
{
    /// <inheritdoc/>
    public async Task<Result<string>> CompleteAsync(string userText, CancellationToken ct)
    {
        var response = await api.CompleteAsync(
            new ChatCompletionRequest(
                settings.Model,
                [new ChatCompletionMessage("system", prompt.Build()),
                 new ChatCompletionMessage("user", userText)],
                settings.MaxTokens),
            ct);

        return Result<string>.Success(response.Choices[0].Message.Content!);
    }
}
```

**Extend `AddAssistantAi` in `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` — do not
create it, it already exists from PR 1.** Add these usings at the top of the file, alongside the
existing ones:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Impl.Ai;
using Refit;
```

Before, as PR 1 shipped it — settings only:

```csharp
    public static IServiceCollection AddAssistantAi(
        this IServiceCollection services, AiSettings settings)
    {
        services.AddSingleton(settings);
        return services;
    }
```

After — replace the `<summary>` and `<remarks>`, and append to the body. The
`services.AddSingleton(settings);` line and the method's signature do not change:

```csharp
    /// <summary>
    /// Registers the chat-completions client the assistant reaches for an answer.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <param name="settings">
    /// Validated chat-model configuration. Read it with <c>IConfiguration.Read</c> so a missing
    /// key or an unusable base address stops the host here, while it is composing, rather than at
    /// the first message the owner sends.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddAssistantTime</c> for the <see cref="ILocalTimeResolver"/> the system
    /// prompt reads the current time from — the reason PR 1 placed this call after
    /// <c>AddAssistantTime</c> in <c>Program.cs</c>'s chain.
    /// </remarks>
    public static IServiceCollection AddAssistantAi(
        this IServiceCollection services, AiSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<SystemPrompt>();
        services.AddRefitClient<IChatCompletionsApi>(new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }),
        })
        .ConfigureHttpClient(http =>
        {
            http.BaseAddress = new Uri(settings.BaseUrl);
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });
        services.AddScoped<IChatClient, ChatCompletionsClient>();
        return services;
    }
```

`Program.cs` needs no change: PR 1 already threads `builder.Configuration.Read<AiSettings>()` into
`.AddAssistantAi(...)` in the right chain position, and that call's shape does not depend on what
the method's body does.

- [ ] **Step 7: Run them and watch them pass**

```bash
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~ChatClientTests"
```

Expected: 2 passed. If the source generator rejects `internal interface IChatCompletionsApi`,
apply the Step-instruction fallback from "Verified facts" above — make it `public` — and record
which path was taken.

- [ ] **Step 8: Run the whole suite**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
```

Expected: zero warnings; unit tests unchanged at 41; every previously green integration test still
green, plus these 2 (`ChatClientTests`).

- [ ] **Step 9: Commit 1**

```bash
git add Directory.Packages.props src/Assistant.Impl/Assistant.Impl.csproj \
        tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj \
        src/Assistant.Impl/Ai/ChatCompletionWire.cs src/Assistant.Impl/Ai/IChatCompletionsApi.cs \
        src/Assistant.Interfaces/IChatClient.cs src/Assistant.Impl/Ai/ChatCompletionsClient.cs \
        src/Assistant.Impl/ImplServiceCollectionExtensions.cs \
        tests/Assistant.WireMock/ChatCompletionStubs.cs tests/Assistant.WireMock/Program.cs \
        tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs \
        tests/Assistant.IntegrationTests/Ai/ChatClientTests.cs
git commit
```

Message:

```
feat: reach a chat-completions endpoint and get an answer back

IChatClient, ChatCompletionsClient and IChatCompletionsApi speak the OpenAI
chat-completions wire format (decision A), which OpenRouter, OpenAI, Groq
and a local Ollama all serve, so a provider change is AiSettings.BaseUrl
and AiSettings.Model and nothing else. This ships the happy path only: a
provider failure still propagates as an exception, and ChatCompletionsClient
takes no ILogger yet. The next commit turns a provider failure into an
answer instead of a crash.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

### Commit 2: a provider failure becomes an answer, not a crash

**Files:**
- Modify: `src/Assistant.Contracts/ErrorCode.cs`
- Modify: `src/Assistant.Impl/Ai/ChatCompletionsClient.cs`
- Modify: `tests/Assistant.IntegrationTests/Ai/ChatClientTests.cs`

**Consumes:** `ChatCompletionsClient`'s happy path (Commit 1). **Produces:**
`ErrorCode.ModelUnavailable`, `ErrorCode.ModelReturnedNoAnswer`, the hardened
`ChatCompletionsClient`.

- [ ] **Step 1: Write the failing tests**

Append to `ChatClientTests`, and add `services.AddLogging();` to `InitializeAsync` — needed once
`ChatCompletionsClient` takes an `ILogger<ChatCompletionsClient>` in this commit's Step 3:

```csharp
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantServices();
        // ...unchanged from Commit 1...
```

```csharp
    /// <summary>
    /// When the provider answers with a server error
    /// And the model is asked
    /// Then the call is refused as unavailable, not thrown.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_ProviderReturnsAServerError_IsRefusedAsUnavailable()
    {
        // Arrange
        await wireMock.SeedChatFailureAsync();

        // Act
        var result = await _sut.CompleteAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ModelUnavailable, result.Error);
    }

    /// <summary>
    /// When the provider answers with no candidate messages
    /// And the model is asked
    /// Then the call is refused as having returned nothing.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_ProviderReturnsNoChoices_IsRefusedAsNoAnswer()
    {
        // Arrange
        await wireMock.SeedChatNoAnswerAsync();

        // Act
        var result = await _sut.CompleteAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ModelReturnedNoAnswer, result.Error);
    }
```

Add `using Assistant.Contracts;` to the file's usings for `ErrorCode`.

- [ ] **Step 2: Run them and watch them fail for the right reason**

```bash
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~ChatClientTests"
```

Expected: both new tests fail with an **unhandled exception**, not a failed assertion —
`CompleteAsync_ProviderReturnsAServerError_IsRefusedAsUnavailable` surfaces a `Refit.ApiException`
for the 500; `CompleteAsync_ProviderReturnsNoChoices_IsRefusedAsNoAnswer` surfaces an
`ArgumentOutOfRangeException` off `response.Choices[0]` on an empty array. Both fail for the
title's own reason: a provider failure currently crashes. The two happy-path tests from Commit 1
still pass unchanged.

- [ ] **Step 3: Append the two error codes**

At the **end** of the `ErrorCode` enum in `src/Assistant.Contracts/ErrorCode.cs`, after
`DueTimeTooFarAhead`:

```csharp
    /// <summary>
    /// The chat model could not be reached, or it responded with an error.
    /// </summary>
    ModelUnavailable,

    /// <summary>
    /// The chat model was reached but returned no usable answer.
    /// </summary>
    ModelReturnedNoAnswer,
```

- [ ] **Step 4: Harden `ChatCompletionsClient`**

Replace the full contents of `src/Assistant.Impl/Ai/ChatCompletionsClient.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;

namespace Assistant.Impl.Ai;

/// <summary>
/// Reaches the configured chat-completions endpoint with the owner's text and the system
/// prompt, and returns the model's answer.
/// </summary>
/// <param name="api">The Refit client for the chat-completions endpoint.</param>
/// <param name="prompt">Builds the system prompt sent as the first message.</param>
/// <param name="settings">The configured model and token limit.</param>
/// <param name="logger">Where a provider failure is recorded.</param>
internal sealed class ChatCompletionsClient(
    IChatCompletionsApi api, SystemPrompt prompt, AiSettings settings,
    ILogger<ChatCompletionsClient> logger) : IChatClient
{
    /// <inheritdoc/>
    public async Task<Result<string>> CompleteAsync(string userText, CancellationToken ct)
    {
        ChatCompletionResponse response;
        try
        {
            response = await api.CompleteAsync(
                new ChatCompletionRequest(
                    settings.Model,
                    [new ChatCompletionMessage("system", prompt.Build()),
                     new ChatCompletionMessage("user", userText)],
                    settings.MaxTokens),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reaching the chat model failed.");
            return Result<string>.Failure(ErrorCode.ModelUnavailable);
        }

        var answer = response.Choices.FirstOrDefault()?.Message.Content;

        if (string.IsNullOrWhiteSpace(answer))
        {
            logger.LogError("The chat model returned no answer.");
            return Result<string>.Failure(ErrorCode.ModelReturnedNoAnswer);
        }

        return Result<string>.Success(answer);
    }
}
```

`AddAssistantAi` needs no change: `services.AddScoped<IChatClient, ChatCompletionsClient>()`
already resolves whatever constructor `ChatCompletionsClient` currently has, and
`ILogger<ChatCompletionsClient>` resolves from the `AddLogging()` Step 1 added to the test.

- [ ] **Step 5: Run them and watch them pass**

```bash
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~ChatClientTests"
```

Expected: 4 passed.

- [ ] **Step 6: Run the whole suite**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
```

Expected: zero warnings; unit tests unchanged at 41 (`ConventionTests` inspects `ErrorCode` by
reflection and needs no change of its own, the same way F8 recorded for its two additions);
integration tests all green.

- [ ] **Step 7: Commit 2**

```bash
git add src/Assistant.Contracts/ErrorCode.cs src/Assistant.Impl/Ai/ChatCompletionsClient.cs \
        tests/Assistant.IntegrationTests/Ai/ChatClientTests.cs
git commit
```

Message:

```
feat: turn a provider failure into an answer, not a crash

ChatCompletionsClient now catches a failed request and an empty answer and
returns Result<string>.Failure with a new ErrorCode instead of letting the
exception (Refit.ApiException, or an index out of range on an empty choices
array) propagate. Both new codes are appended -- no existing member's value
moved.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

---
## PR 4: The owner gets the model's answer

**Decisions this PR carries:** G, L.

**Consumes:** `IChatClient`/`AddAssistantAi` (PR 3, hardened). **Produces:** the updated
`MessageHandler`, the corrected design docs.

**`Program.cs` is not in this PR's file list, on purpose.** PR 1 already added
`.AddAssistantAi(builder.Configuration.Read<AiSettings>())` to the chain, and PR 3 only ever
extended that method's body — `Program.cs` itself never needed a second look. Nothing else this PR
does touches composition: `MessageHandler` is resolved by `AddAssistantListener`, which this PR
does not change, and it now reaches `IChatClient` through a `IServiceScopeFactory` it already
receives, not through a new registration. Checked and confirmed empty: this PR's diff has no
`Program.cs` hunk.

**Files:**
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`
- Modify: `src/Assistant.Impl/Telegram/MessageHandler.cs`
- Modify: `docs/design/slice-1-reminders.md`
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`
- Check (no change expected): `AGENTS.md`, `README.md`

**Validation:** the end-to-end moment — the first point since F7 the owner can watch the whole
pipeline work for real, no stub involved. Ensure `.env`/user secrets carry a real
`TelegramSettings__BotToken`, `TelegramSettings__OwnerChatId`, and now a real `AiSettings__ApiKey`;
point `DatabaseSettings__ConnectionString` at a real local Postgres; then:

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project src/Assistant.Worker
```

From the owner's own Telegram account, send the bot a plain-language message, e.g.
`call the bank tomorrow at 10`. Expected: within a few seconds, the bot replies in the same chat
with the model's own generated answer, not an echo — the exact wording is the model's, so nothing
here pins it to a literal string. `docs/e2e-local.md` walks through the stub-vs-real-Telegram
split this depends on; Self-review, below, asks whether it still describes reality now that
`AiSettings` is part of the worker's configuration surface.

### Code: reply with the model's answer instead of an echo

- [ ] **Step 1: Update the listener tests first**

Replace the full contents of `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`:

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
    private const string ModelAnswer = "Got it -- I will remind you.";

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
        await wireMock.SeedChatAnswerAsync(ModelAnswer);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }

    /// <summary>
    /// When the owner sends a message
    /// And the listener is running
    /// Then a reply comes back carrying the model's answer.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerSendsAMessage_RepliesWithTheModelsAnswer()
    {
        // Arrange
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal(ModelAnswer, sent[0].Text);
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
    /// This test no longer checks the reply's exact text (decision L): with the reply now the
    /// model's answer rather than an echo, that check duplicated
    /// <see cref="Listener_OwnerSendsAMessage_RepliesWithTheModelsAnswer"/>, which spec §7.2
    /// forbids. What this test alone proves is that the stranger's message produced no second
    /// reply.
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
    /// Breaking the offset advance to confirm this test could fail measured 3704
    /// replies inside the 3-second settle window, not two, so this cannot fail
    /// marginally. A false pass would need the machine to make no progress for the
    /// whole window.
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

- [ ] **Step 2: Run them and watch the first one fail for the right reason**

```bash
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~TelegramListenerTests"
```

Expected: `Listener_OwnerSendsAMessage_RepliesWithTheModelsAnswer` fails — the reply is still
`"call the bank"`, the echo, not `ModelAnswer`. The other two tests pass unchanged: neither one's
assertion depends on what `MessageHandler` sends back, only on how many replies went out and to
whom.

- [ ] **Step 3: Update `MessageHandler`**

Replace the full contents of `src/Assistant.Impl/Telegram/MessageHandler.cs`:

```csharp
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Sends the owner's plain-text message to the chat model and replies with its answer.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="notifier">Where the reply is delivered.</param>
/// <param name="scopeFactory">
/// Opens the scope <see cref="IChatClient"/> is resolved from, because this handler is a
/// singleton and a Refit client is a typed <see cref="System.Net.Http.HttpClient"/> —
/// capturing one directly would pin its message handler and defeat the factory's handler
/// rotation. <see cref="Assistant.Impl.Services.Jobs.DueReminderJob"/> already solves the
/// identical problem for <see cref="ITaskService"/>, in its own words: "Opens the scope
/// [the service] is resolved from, because this job is a singleton and the service depends on
/// the scoped database context."
/// </param>
/// <remarks>
/// The owner check lives inline here, on purpose: the assistant serves exactly one person, so
/// there is nothing to route between. Any future handler must apply the same check itself —
/// nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/> enforces it.
/// </remarks>
internal sealed class MessageHandler(
    TelegramSettings settings, INotifier notifier, IServiceScopeFactory scopeFactory)
    : ITelegramUpdateHandler
{
    private const string Unreachable =
        "I could not reach the model just now. Send that again in a moment.";

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
        var chat = scope.ServiceProvider.GetRequiredService<IChatClient>();
        var answer = await chat.CompleteAsync(text, ct);

        await notifier.SendAsync(answer.IsSuccess ? answer.Value! : Unreachable, ct);
    }
}
```

- [ ] **Step 4: Run them and watch them pass**

```bash
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~TelegramListenerTests"
```

Expected: 3 passed.

- [ ] **Step 5: Run the whole suite**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

Expected: zero warnings; unit tests unchanged at 41; every integration test green, including the
4 `ChatClientTests` PR 3 added (this PR changes `TelegramListenerTests`' assertions but adds no
new test).

- [ ] **Step 6: Commit**

```bash
git add tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs \
        src/Assistant.Impl/Telegram/MessageHandler.cs
git commit
```

Message:

```
feat: reply with the model's answer instead of an echo

MessageHandler resolves IChatClient from a per-call DI scope, the way
DueReminderJob already resolves ITaskService -- a Refit client is a typed
HttpClient, and capturing one in a singleton handler would pin its message
handler and defeat handler-factory rotation. When the model cannot be
reached, the owner gets a fixed apology instead of an exception. No
Program.cs change: PR 1 already wired AddAssistantAi into the chain, and
this PR only ever consumes IChatClient through the scope factory
MessageHandler already received.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

### Docs: record what F9a settled

- [ ] **Step 7: Correct the spec's stack line**

The document header (before §1) reads:

```
**Stack:** C# / .NET 10 (LTS), PostgreSQL 16, Telegram Bot API, `Microsoft.Extensions.AI`
```

`Microsoft.Extensions.AI` is never referenced anywhere this feature touches — §5.5 and §12.3 both
already mandate Refit, and the more specific sections win, exactly as F8 ruled its own three-way
contradiction between §5.4/§11.4 and §2/§12.7 in F8's favour. Correct the line to:

```
**Stack:** C# / .NET 10 (LTS), PostgreSQL 16, Telegram Bot API, Refit
```

- [ ] **Step 8: Correct and extend §5.5**

Replace §5.5's full body with:

```markdown
### 5.5 Provider routing and fallback

**Corrected at F9a:** every provider this project reaches is exercised through **one** Refit
interface, `IChatCompletionsApi` (§12.3), named for the OpenAI chat-completions wire format that
OpenRouter, OpenAI, Groq and a local Ollama all serve — not `IAnthropicApi`/`IOpenRouterApi` as
this section originally named them. Anthropic is ruled out of slice 1 entirely; OpenRouter is the
provider `AiSettings` ships a default for, and switching to any other OpenAI-shaped endpoint is a
configuration change (`AiSettings.BaseUrl`, `AiSettings.Model`), not a new type.
`ChatCompletionsClient` is the one `IChatClient` adapter, translating between the wire format and
the project's own request and response types.

`FallbackChatClient` is a decorator wrapping a primary and a secondary `IChatClient`, with Polly
for timeout and circuit-breaking. Neither concrete client is aware of the other; only the
composition root changes when providers change.

**Open question, raised at F9a, not resolved here:** OpenRouter is itself a router — a request
against one upstream model can already fail over to another model before OpenRouter ever answers
the caller. Going OpenRouter-first for the primary (and, plausibly, the fallback too) may make
`FallbackChatClient` redundant with routing OpenRouter already does. This section owes an answer
before F13 builds `FallbackChatClient`; F9a does not decide it.
```

Note in the PR report, but do not fix in this step: §7.5's architecture-tests list still says
"`Impl.Services` referencing `Telegram.Bot` or `Microsoft.Extensions.AI` types," which is stale in
the same direction as the line just corrected. Step 12, below, fixes it in the same PR rather than
leaving it for a future pass.

- [ ] **Step 9: Split the backlog's F9 entry into F9a and F9b**

In `docs/design/2026-08-22-slice-1-feature-backlog.md`, replace the existing F9 entry:

```markdown
**F9 · Send to the model and get a tool call** — spec §5.2, §5.3, §12.3
`IChatClient`, `IAnthropicApi` via Refit, the system prompt carrying current local time, and
`CreateTaskTool` as the first `IAssistantTool`. Adds `CreateTaskRequest` to `Contracts`
(`Result` and `ErrorCode` arrived at F5a).
*Tests:* free text produces the expected tool call against a WireMock'd provider.
```

with:

```markdown
**F9a · Reach the model** — spec §5.1, §5.2, §5.5, §12.3 · **done**
`IChatClient`, `IChatCompletionsApi` via Refit against the OpenAI chat-completions wire format,
`AiSettings`, the system prompt carrying the current local time, and `MessageHandler` replying
with the model's answer instead of an echo. No tools yet: parsing a `create_task` call out of the
answer is F9b. Shipped as four PRs — settings, the clock and system prompt, reaching the model,
and this reply — after this feature's own plan merged docs-only first.
*Tests:* free text gets an answer back from a WireMock'd provider; a provider failure and an
empty answer are each refused with a named `ErrorCode` instead of crashing the listener.
*Settled at F9a:*
- **OpenRouter, not Anthropic, and nothing is named after a vendor.** The repository owner ruled
  Anthropic out of slice 1 entirely. `IChatCompletionsApi`, `ChatCompletionsClient` and
  `ChatCompletionStubs` are named for the OpenAI chat-completions wire format, which OpenRouter,
  OpenAI, Groq and a local Ollama all serve, so moving providers is a change to
  `AiSettings.BaseUrl` and `AiSettings.Model`, never a new type. Spec §5.5 named
  `IAnthropicApi`/`IOpenRouterApi`; corrected here.
- **The chat-completions format turned out simpler than Anthropic's would have been.** The
  system prompt is `messages[0]` with `role: "system"`, not a separate top-level `system` field,
  so no record property is named `System` and the namespace-shadowing trap that shape would set
  up next to `System.*` types never arises.
- **`AiSettings` lives in `Impl/Settings/`,** joining `TelegramSettings`, `TimeSettings` and
  `DatabaseSettings` — configuration is not an `Ai/` concern, even though spec §3.4 places the
  Refit interfaces themselves in `Impl/Ai/`. Shipped alone, first, in its own PR, together with a
  minimal `AddAssistantAi` that registers it — extended in place across later PRs, never
  recreated, per the repository owner's preference for code open to extension, closed to
  modification.
- **`AiSettings.BaseUrl` is required,** unlike `TelegramSettings.BaseUrl`. Telegram's is nullable
  because absent means "the real Telegram"; there is no single "the" chat-completions provider,
  so `appsettings.json` ships OpenRouter's address as a changeable default instead.
- **`IChatClient.CompleteAsync` returns `Result<string>` at F9a.** It changes shape at F9b, to
  `Result<ToolCall>`, once there is a tool call to parse out of the answer — a modification, not
  an extension, and accepted because `IChatClient` is a transport abstraction rather than one of
  spec §3.6's behaviour seams. The seam F9b actually grows is `IAssistantTool`.
- **`ILocalTimeResolver` gained `CurrentLocalTime` and `ZoneId`,** the member F8's own "Settled
  at F8" note deferred until something needed to state "now" in the user's zone. Both live on the
  resolver, not injected as a raw `TimeZoneInfo` into `SystemPrompt`, so the zone keeps exactly
  one owner. Its own unit test asserts a substring of the built prompt, not the whole sentence, so
  a later rewrite of the prompt's instructional prose will not break it needlessly.
- **The offset formatter renders a half-hour zone as `UTC+10:30`, not `UTC+11` or `UTC+10`.**
  Tested against `Australia/Lord_Howe`, for the same reason F8 tested its gap and ambiguity rules
  there: a round-hour zone cannot catch a formatter that silently drops minutes.
- **The system prompt names the configured zone twice, never a literal.** It appears once next
  to the current time and once in "All times the user gives are `<zone>` local" — both reads from
  `ILocalTimeResolver.ZoneId`, so a literal cannot creep into either without the two visibly
  disagreeing.
- **`MessageHandler` takes `IServiceScopeFactory`, not `IChatClient`, directly.**
  `TelegramListener` injects `IEnumerable<ITelegramUpdateHandler>` and is itself a singleton
  `BackgroundService`, so every handler is a singleton too. A Refit client is a typed
  `HttpClient`; capturing one in a singleton would pin its message handler and defeat the
  factory's handler rotation. `DueReminderJob` already solved this identical problem for
  `ITaskService`; `MessageHandler` now solves it the same way, and F10 will need the same scope
  for `ITaskService` too.
- **A provider failure is an answer, not a crash — shipped in two commits, in one PR, on
  purpose.** The happy-path `ChatCompletionsClient` went in first, with no `ErrorCode` and no
  `try`/`catch`, so the failing-test step for the 500 and empty-choices cases showed a real crash
  (`Refit.ApiException`, and an `ArgumentOutOfRangeException` off an empty array) before
  `ModelUnavailable` and `ModelReturnedNoAnswer` gave those two failures a name and a graceful
  `Result<string>.Failure`.
- **`ErrorCode` gained `ModelUnavailable` and `ModelReturnedNoAnswer`, appended** after
  `DueTimeTooFarAhead` — no existing member's numeric value moved.
- **F7's echo test became an assertion on the model's answer**, as the backlog always intended.
  Its "only the owner is answered" sibling lost its own copy of the reply's exact text: checking
  it there duplicated the renamed test, which spec §7.2 forbids.
- **`.env.example` lost `LLM__ANTHROPIC__APIKEY` and `LLM__OPENROUTER__APIKEY`**, which predated
  every naming convention in this repository and which no code has ever read. Replaced by
  `AiSettings__ApiKey`, `AiSettings__Model` and `AiSettings__BaseUrl`.
- **The default model is `anthropic/claude-sonnet-5`, verified present in OpenRouter's live
  model list on 2026-08-29.** A model slug naming a vendor is unavoidable and is not what the
  vendor-neutral naming above is about. `.env.example` names `anthropic/claude-haiku-4.5`, also
  verified present, as the cheaper alternative.
- **The "typing…" indicator stays deferred, again.** Spec §5.1 deferred it to F9 because F7 had
  no wait to cover. F9a does have one, but the indicator needs an `INotifier` member and a
  refresh loop that belongs with F10's kept reply, not F9a's throwaway prose — a fresh deferral,
  not an inherited one.

**F9b · Parse a tool call out of the answer** — spec §5.2, §5.3, §12.3
`IAssistantTool`, `CreateTaskTool` as its first implementation, tool definitions added to the
chat-completions request, `tool_calls` parsed out of the response, `CreateTaskRequest` in
`Contracts`, and `IChatClient.CompleteAsync` changed to return `Result<ToolCall>` in place of
`Result<string>`.
*Tests:* free text produces the expected tool call against a WireMock'd provider.
```

- [ ] **Step 10: Check `AGENTS.md` and `README.md`**

`AGENTS.md` — no change. Its project map, command list, and conventions section name no command,
project, or convention this feature moved: `Assistant.Impl` still contains "services, jobs,
adapters," which already covers `Ai/`; no new build or test command was introduced; the Refit
rule in `docs/conventions.md` §12.3 already covered this feature before it existed, the same way
F8 found `DependencyRuleTests` already covered it.

`README.md` — no change. Its Contributing section already reads "Telegram and the LLM APIs are
stubbed with WireMock" (plural), which was already true in spirit and is now true in fact; its
Quickstart says to fill in `.env.example`'s values generically rather than enumerating them, so
PR 1's `.env.example` edit needs no matching README update. Its "Deliberate limitations" section
does not mention the model provider at all, and this feature adds no new deliberate limitation —
`FallbackChatClient`'s absence is already covered by "no accounts" being the only limitation this
document commits to naming exhaustively for slice 1.

- [ ] **Step 11: Correct §3.4, which still describes the folder this feature just changed**

Spec §3.4's `Assistant.Impl/` tree is what F9a most directly implements, and its `Ai/` line
still names the two types decision A retired. Left uncorrected, the spec describes a folder
layout the code contradicts the moment this feature merges — the exact failure spec §12.4
warns against for `AGENTS.md`, applied here to the design spec itself.

Before (the fenced tree's `Ai/` line and its continuation):

```
├─ Ai/             IAnthropicApi, IOpenRouterApi (Refit), the IChatClient adapters,
│                  FallbackChatClient
```

After:

```
├─ Ai/             IChatCompletionsApi (Refit), ChatCompletionsClient (the IChatClient
│                  adapter), SystemPrompt. FallbackChatClient is undecided, see §5.5.
```

Every other line of the tree — `Services/`, `Mapping/`, `Tools/`, `Telegram/`, `Scheduling/` —
is untouched; only the `Ai/` line and its continuation change, and the box-drawing alignment
(description text starting in the same column on every line, per §12.6's fixed-width reasoning)
carries over unchanged: both replacement lines still open their description at the same column
the rest of the tree uses.

- [ ] **Step 12: Correct §7.5's architecture-tests list, stale for the same reason**

§7.5's "Namespace-level, inside `Impl`" list still forbids `Impl.Services` from referencing
`Microsoft.Extensions.AI` types — a library this feature never references, per decision A. This
is one line, in a file this PR already has open for the same underlying correction (the header
Stack line, Step 7), so it is fixed here rather than left for a second documentation pass.

Before:

```
- `Impl.Services` referencing `Telegram.Bot` or `Microsoft.Extensions.AI` types
```

After:

```
- `Impl.Services` referencing `Telegram.Bot`, the chat-completions wire types, or `Refit` types
```

- [ ] **Step 13: Commit**

```bash
git add docs/design/slice-1-reminders.md docs/design/2026-08-22-slice-1-feature-backlog.md
git commit
```

Message:

```
docs: record what F9a settled, and split F9 into F9a/F9b

The spec's header and 5.5 both named IAnthropicApi/IOpenRouterApi and
Microsoft.Extensions.AI; neither survived contact with the OpenAI
chat-completions format this feature actually used. Both are corrected
here, and 5.5 is left owing an answer on whether FallbackChatClient is
still worth building once OpenRouter's own routing is accounted for.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

---

## Self-review

Grouped by PR, since each merges independently; the last group only applies once all four merged.

**PR 1 (settings):**
- [ ] `dotnet build` zero warnings; unit tests green, 37 total, unchanged from F8 (decision O)
- [ ] Both `dotnet run` checks produce the recorded outputs: fails on Postgres port 1 with the key
      set, throws `ConfigurationErrorsException` naming `AiSettings.ApiKey` without it
- [ ] `AddAssistantAi`'s body is exactly `services.AddSingleton(settings); return services;` —
      no Refit client, no `SystemPrompt`, nothing this PR does not need yet
- [ ] `Program.cs`'s chain reads `.AddAssistantAi(builder.Configuration.Read<AiSettings>())`,
      positioned after `AddAssistantTime` and before `AddAssistantScheduler` — no bare discarded
      `Read<AiSettings>()` statement anywhere

**PR 2 (the clock and the system prompt):**
- [ ] Unit tests green, 41 total (37 + 4)
- [ ] Both `SystemPromptTests` assertions use `Assert.Contains`, not `Assert.Equal` on the full
      sentence
- [ ] No time zone literal in `src/` outside `appsettings.json` and `TimeSettings`'s own doc
      comment/error message (grep `Asia/`, `Australia/`)

**PR 3 (reach the model):**
- [ ] Integration tests green (4 `ChatClientTests`, 2 per commit); `down`, never `down -v`
- [ ] Commit 1's `ChatCompletionsClient` genuinely could not compile with `ErrorCode` referenced
      or an `ILogger` parameter — the two-commit boundary was not silently merged
- [ ] Commit 1's diff to `ImplServiceCollectionExtensions.cs` only appends to `AddAssistantAi`'s
      existing body — `services.AddSingleton(settings);` and the method signature, both from
      PR 1, are unchanged; the method was extended, not recreated
- [ ] Commit 2's two new tests were watched failing with an **unhandled exception**
      (`Refit.ApiException`, `ArgumentOutOfRangeException`), not a failed assertion, before the fix
- [ ] The Refit-internal-interface fallback was recorded as taken or not, in the PR report
- [ ] No vendor name in any **type** name in `src/` (grep `Anthropic`, `OpenRouter`; config values
      and model slugs are exempt)
- [ ] `ErrorCode`'s two new members were **appended**; no existing value moved
- [ ] `DependencyRuleTests`'s pre-existing `[InlineData("Refit")]` row was not modified
- [ ] No inline `Version=` in `Directory.Packages.props` or any `.csproj`

**PR 4 (the owner gets the model's answer):**
- [ ] `MessageHandler`'s constructor declares `IServiceScopeFactory`, not `IChatClient` (grep the
      file — `IChatClient` appears only inside `HandleAsync`)
- [ ] `Listener_StrangerSendsAMessage_OnlyTheOwnerIsAnswered` asserts `Assert.Single(sent)`, no
      exact text — a deliberate de-duplication (decision L), called out in the PR report
- [ ] This PR's diff has no `Program.cs` hunk — `AddAssistantAi` was wired into the chain in PR 1
      and never needed a second look
- [ ] The end-to-end validation actually ran: a real message sent, a real model answer received
- [ ] `docs/e2e-local.md` still describes reality now that `AiSettings` is part of the worker's
      configuration surface
- [ ] AGENTS.md/README.md changes, or the decision not to change them, are recorded in the report
- [ ] No section of `docs/design/slice-1-reminders.md` still names `Microsoft.Extensions.AI`,
      `IAnthropicApi` or `IOpenRouterApi` as current design (outside recorded-as-superseded text)

**Whole feature, once all four PRs have merged:**
- [ ] Every new public member has a three-line `<summary>`; every test summary is Gherkin
- [ ] Every class taking arguments uses a primary constructor
- [ ] No emoji anywhere, including commit messages, across all four PRs
- [ ] Each PR stayed under the 1000-changed-line budget on its own
