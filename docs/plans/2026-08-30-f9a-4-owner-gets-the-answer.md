# F9a-4 — the owner gets the model's answer

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F9a makes the assistant able to reach a chat model and return its answer to the owner
over Telegram — no tools yet; parsing a `create_task` call out of the answer is F9b. This
document is F9a's **fourth and final** independently reviewable PR. `MessageHandler` stops
echoing F7's placeholder and replies with the model's actual answer instead, and the design
documents are corrected to record what all three prior slices actually settled — several of them
still describe a shape the code abandoned partway through slice 3.

**Tech Stack:** .NET 10 (`net10.0`), nullable enabled, warnings are errors — the existing stack.
This slice adds no new NuGet package: `MessageHandler` gains a fourth collaborator
(`IServiceScopeFactory`, part of the DI container slice 1 already references), and its test gains
a fourth settings registration (`AiSettings`/`AddAssistantAi`, both shipped in slice 1 and
extended in slice 3). No wire type changes, so no WireMock image rebuild.

**Spec:** `docs/design/slice-1-reminders.md` §5.1 (flow — the interface name in the diagram is
corrected here), §3.4 (`Ai/` folder tree — corrected here), §3.6 (extension seams — the client
row is removed here, not renamed), §5.5 (provider routing — corrected here, deferred by slice 3
on purpose), §7.2 (one test owns one behaviour), §7.5 (architecture-tests list — corrected here),
§12.3 (Refit registration — corrected here), §12.6 (no emoji).
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F9's entry, still unsplit,
becomes F9a (closed by this slice) and F9b.

---

## Where this sits

F9a ships as four independently reviewable PRs rather than one. Precedent: F8 shipped its plan
and its code together in one PR and broke this repository's 1000-line budget (1243 plan + 598
code = 1841 lines); F9a's plan is split by PR instead, each slice getting its own document.

1. **Slice 1 — AI settings.** `AiSettings`, `appsettings.json`, `.env.example`, a minimal
   `AddAssistantAi`, and the `Program.cs` chain link. Merged as `987ad21`.
2. **Slice 2 — the clock and the system prompt.** `ILocalTimeResolver` gains `CurrentLocalTime`
   and `ZoneId`; `SystemPrompt` builds the text sent to the model. Merged as `3b136fe`.
3. **Slice 3 — reach the model.** Refit, the wire types, `IAiClient`, `AiClient`, failure
   handling, and the WireMock stub. Merged as `e1bcad3`. Landed with two renames the rejected
   monolith this plan was split from never anticipated: `IChatClient`/`ChatCompletionsClient`
   were rejected on review in favour of `IAiClient`/`AiClient`, because "Completions" named the
   vendor's own endpoint rather than anything the class does, and "Client" read as outbound in
   the `HttpClient` sense.
4. **Slice 4 — the owner gets the model's answer (this document).** `MessageHandler` replaces
   the echo with a real call, and the design documents catch up with reality.

This is the last of the four; there is no slice 5.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error everywhere.
- **Every class taking arguments uses a primary constructor.** No separate constructors.
- Every enum's first member is `Unknown`, with no explicit numeric values. New members are
  **appended**, never inserted. (Nothing in this slice touches an enum.)
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- Central package management; no inline `Version=` (NU1008). Not exercised this slice — no
  package changes.
- No emoji anywhere: source, tests, docs, or commit messages.
- **Never run `docker compose down -v`.** Use `docker compose -f compose.test.yaml down` with no
  flags.
- Integration tests need `docker compose -f compose.test.yaml up -d` first — **no `--build`**
  this time: neither commit touches `tests/Assistant.WireMock/`, so the stub image from slice 3
  is still current.
- PR budget: 1000 changed lines per PR, excluding the plan (which merges on its own, docs-only).
  The rejected monolith this plan was split from estimated this slice's code at ~150 lines, the
  smallest of the four by code. The docs commit below touches six files, every edit a sentence, a
  table row, or a handful of lines — comfortably under budget combined with the code commit.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
  ```

---

## Verified facts this plan rests on

- **`MessageHandler.cs` (21 lines) and `TelegramListenerTests.cs` (125 lines) were read in full,
  as they stand at `e1bcad3`, before any code block below was written.** Both match what F7 and
  slice 3 actually shipped; the parts that do not change below are lifted from the real files,
  not reconstructed from the rejected monolith's own rendering of them.
- `src/Assistant.Interfaces/IAiClient.cs` and `src/Assistant.Impl/Ai/AiClient.cs` confirm
  `IAiClient.AskAsync(string userText, CancellationToken ct)` returns `Result<string>` today.
  `Assistant.Contracts/Result.cs` confirms `Result<T>.IsSuccess` and `.Value` are the members to
  read on the way out.
- `src/Assistant.Impl/Services/Jobs/DueReminderJob.cs`'s `scopeFactory` parameter doc reads,
  verbatim: *"Opens the scope `ITaskService` is resolved from, because this job is a singleton
  and the service depends on the scoped database context."* Quoted exactly in decision 1, below.
- `src/Assistant.Worker/Program.cs`'s composition chain already reads
  `.AddAssistantAi(builder.Configuration.Read<AiSettings>())`, sitting between
  `.AddAssistantTime(...)` and `.AddAssistantScheduler()`. `AddAssistantListener()`, unchanged
  since F7, registers `services.AddSingleton<ITelegramUpdateHandler, MessageHandler>()`. Both
  confirmed by reading the files directly — this slice's diff has no `Program.cs` hunk, because
  nothing about *how* `MessageHandler` is constructed or registered changes, only what it does
  once it is running.
- `AddAssistantAi` in `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` registers the Refit
  client with `services.AddRefitGeneratedClient<IAiApi>(...)` — confirmed by reading the file.
  `AddRefitClient<T>()`, which spec §12.3 still names, is Refit 15's reflection path and needs a
  `Refit.Reflection` package this project does not take; it does not work here. Corrected in the
  docs commit below.
- `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs` already exposes
  `SeedAiAnswerAsync(string answer)` (shipped in slice 3) — not `SeedChatAnswerAsync`, the name
  the rejected monolith used. This slice's test file calls the real method directly; the fixture
  itself needs no change and is absent from this slice's file list.
- **Test counts, counted directly rather than trusted from the monolith.** At `e1bcad3`:
  - Unit suite, **41**: 2 (`TimeSettingsTests`) + 4 (`ConfigurationExtensionsTests`: a 2-case
    `[Theory]` + 2 `[Fact]`) + 1 (`ReminderSchedulerTests`) + 1 (`ScheduledJobBaseTests`) + 6
    (`ConventionTests`) + 8 (`DependencyRuleTests`: a 3-case `[Theory]` + a 4-case `[Theory]` + 1
    `[Fact]`) + 2 (`SystemPromptTests`) + 17 (`LocalTimeResolverTests`: a 2-case `[Theory]` + 5
    `[Fact]` + two more 2-case `[Theory]`s + a 4-case `[Theory]` + 2 `[Fact]`) = 41.
  - Integration suite, **32**: 4 (`TaskRepositoryTests`) + 9 (`DueReminderQueryTests`) + 1
    (`ReminderTaskSchemaTests`) + 4 (`TelegramNotifierTests`) + 3 (`TelegramListenerTests`) + 4
    (`DueReminderJobTests`) + 3 (`TaskServiceTests`) + 4 (`AiClientTests`) = 32.

  This slice changes neither total: one integration test is renamed, one assertion is trimmed,
  and nothing is added or removed.
- `docs/design/slice-1-reminders.md` still names `Microsoft.Extensions.AI` (the header Stack
  line), `IAnthropicApi`/`IOpenRouterApi`/`IChatClient` (§3.4, §3.6, §5.1, §5.5), and
  `AddRefitClient<T>()` (§12.3) — confirmed by reading the file directly at `e1bcad3`. Slice 3's
  own plan explicitly deferred every one of these corrections to this slice ("A later slice
  corrects the spec document itself; this one does not touch it"); none of it happened in slices
  1–3.
- `docs/design/2026-08-22-slice-1-feature-backlog.md`'s F9 entry (three lines, naming
  `IChatClient`/`IAnthropicApi`) is still the single, unsplit entry it was before slice 1 —
  confirmed by reading the file.
- **Beyond what the monolith flagged:** `AGENTS.md`'s project-map row for `Assistant.WireMock`
  still reads *"Stub API server (Telegram today)"* — stale since slice 3 gave that same server a
  second job, answering `/chat/completions` too. The monolith's own PR 4 never checked this line.
- **Beyond what the monolith flagged:** `docs/e2e-local.md` never mentions `AiSettings` in either
  of its two walkthroughs. Since slice 1 (`987ad21`), `Program.cs` reads `AiSettings`
  unconditionally while composing, and `AiSettings.ApiKey` has no default anywhere —
  `appsettings.json` ships defaults for `BaseUrl`, `Model` and `MaxTokens`, but a bearer-token
  secret cannot ship one. Every worker run this document walks through — including the
  pure-reminder path that never touches `IAiClient` at all — has required a real or placeholder
  `AiSettings__ApiKey` since slice 1 merged, undocumented until now. The monolith's own
  self-review only asked whether this document "still describes reality"; it does not, and this
  is why.
- `README.md`'s Contributing section already reads *"Telegram and the LLM APIs are stubbed with
  WireMock"* (plural) — accurate as written, confirmed by reading the file. No change.
- `src/Assistant.Impl/Settings/AiSettings.cs` itself says "chat-completions endpoint" three times,
  in its own doc comments — pre-existing since slice 1, not touched by any slice since. It is
  outside this slice's file list; this dispatch may not modify a source file, so it is recorded
  here rather than fixed.

---

## Inherited context: what this slice reads from slices 1–3

`AiSettings` (slice 1) and `SystemPrompt` (slice 2) already have their first real caller, from
slice 3: `AiClient.AskAsync` builds the system prompt once per request and sends it as
`messages[0]`. This slice adds no new caller of either — `MessageHandler` never touches
`AiSettings` or `SystemPrompt` directly, only `IAiClient`.

`IAiClient` and its one production implementation, `AiClient` (slice 3, merged `e1bcad3`),
return `Result<string>`: the model's answer on success, or one of `ErrorCode.ModelUnavailable` /
`ErrorCode.ModelReturnedNoAnswer` on failure — never an exception, for either failure mode this
project has a name for. This slice's `MessageHandler` is the interface's first caller outside its
own test suite.

`DueReminderJob` (F5b) is the precedent decision 1, below, leans on directly: it already resolves
`ITaskService` from a fresh `IServiceScopeFactory.CreateScope()` inside its own tick, for the
identical reason this slice now applies to `IAiClient`.

`WireMockFixture.SeedAiAnswerAsync` (slice 3) is used as shipped — this slice's test file is its
second caller, after `AiClientTests`.

---

## Decisions this slice makes

Numbered 1–2 here. The monolith these four PRs were split from numbered its full, four-slice
decision set A–O; these two carried letters G and L there. Renumbered for this standalone
document, since it carries only these two.

### 1. `MessageHandler` takes `IServiceScopeFactory`, not `IAiClient`

`TelegramListener` is a singleton `BackgroundService` injecting
`IEnumerable<ITelegramUpdateHandler>`, so every handler it holds is constructed once and lives
for the process's whole lifetime. A Refit client is a typed `HttpClient`; capturing one directly
in a singleton pins its message handler and defeats `IHttpClientFactory`'s handler rotation — the
same category of bug `HttpClient` gives every singleton that holds one directly.

`DueReminderJob` already solved this identical problem for `ITaskService`, in its own doc
comment: *"Opens the scope `ITaskService` is resolved from, because this job is a singleton and
the service depends on the scoped database context."*

`MessageHandler` solves it the same way for `IAiClient`, resolving it from a fresh
`IServiceScopeFactory.CreateScope()` inside `HandleAsync` rather than through its constructor.
F10 will need the identical scope for `ITaskService`, once a captured task is actually stored.

### 2. F7's echo test changes, and that is the point

`TelegramListenerTests.Listener_OwnerSendsAMessage_RepliesWithTheirText` asserts the echo and
becomes an assertion on the model's answer — renamed to
`Listener_OwnerSendsAMessage_RepliesWithTheModelsAnswer`.

Its "message already answered" sibling needs no change beyond the new registrations to compose:
it never asserted on reply text, only on count. Its "only the owner is answered" sibling is not
fully unchanged, though — read closely, it asserts `Assert.Equal("call the bank", ...)` against
the echoed text, which stops being true once the reply is the model's answer. This PR drops that
text check down to `Assert.Single(sent)`, keeping the "exactly one reply, and the stranger did
not get it" assertion the test's name promises, and leaving the reply's exact content to the
renamed test — which is what spec §7.2 already asks for ("One test per behaviour, at the
highest-fidelity level that can reach it"). This is a de-duplication, not a weakened test: the
renamed test now owns the reply's content outright, and two tests asserting the same string was
never buying extra confidence, only a second place to update it.

---

## What this slice does NOT include

- **Tools.** `IAssistantTool`, `CreateTaskTool`, `tool_calls` parsing, and `IAiClient.AskAsync`
  changing to return `Result<ToolCall>` — all F9b.
- **Storing anything.** No `ITaskService.CreateAsync`, no migration, no model property. F10.
- **`FallbackChatClient`, Polly, retry, circuit breaking, the per-minute call cap.** Spec §5.5's
  correction, below, restates the open question the monolith raised — whether OpenRouter's own
  routing makes a fallback decorator redundant — without resolving it. F13's own concern.
- **The "typing…" indicator, deferred again.** Spec §5.1 deferred it to F9 because F7 had no wait
  worth covering. F9a does have one now, but the indicator needs an `INotifier` member and a
  4-second refresh loop cancelled when the reply lands, which belongs with F10's *kept* reply
  rather than F9a's throwaway prose. Recorded again in the backlog entry this slice writes.
- **F13's own backlog entry**, which still names `IOpenRouterApi` and `FallbackChatClient` — left
  alone. F13 is unscheduled, and its own plan will settle its naming against whatever this
  project looks like when it is actually written, the same way this slice settles F9a's.
- **`AiSettings.cs`'s "chat-completions endpoint" wording**, pre-existing since slice 1 — flagged
  in "Verified facts," above, not fixed. It is source, not a design document, and outside this
  slice's file list.

---

## File Structure

```
src/Assistant.Impl/
    Telegram/MessageHandler.cs                 echo -> the model's answer          (Commit 1)

tests/Assistant.IntegrationTests/
    Telegram/TelegramListenerTests.cs          rename + trim + AiSettings          (Commit 1)

docs/
    design/slice-1-reminders.md                header, §3.4, §3.6, §5.1, §5.5,
                                                §7.5, §12.3 corrected               (Commit 2)
    design/2026-08-22-slice-1-feature-backlog.md
                                                F9 entry split into F9a/F9b         (Commit 2)
    e2e-local.md                               AiSettings__ApiKey requirement      (Commit 2)

AGENTS.md                                      WireMock project-map row corrected  (Commit 2)
README.md                                      checked, no change                  --
```

`Program.cs` is deliberately absent from this list: slice 1 already added
`.AddAssistantAi(builder.Configuration.Read<AiSettings>())` to the chain, slice 3 only ever
extended that method's body, and `MessageHandler` is resolved by `AddAssistantListener`, which
this slice does not change either. Nothing this PR does touches composition — `MessageHandler`
now reaches `IAiClient` through an `IServiceScopeFactory` it already receives as a constructor
parameter, not through a new registration.

---

## Validation

**Test count arithmetic.** The unit suite stays at **41**, unchanged — this slice touches no unit
test file. The integration suite stays at **32**, also unchanged: `TelegramListenerTests` gets one
test renamed and one assertion trimmed, and no test method is added or removed, so the same
per-file breakdown in "Verified facts," above, still sums to 32 after this slice.

```bash
docker compose -f compose.test.yaml up -d
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

**Validation proper is the end-to-end moment** — the first point since F7 the owner can watch the
whole pipeline work for real, no stub involved. Set a real `TelegramSettings__BotToken`,
`TelegramSettings__OwnerChatId`, and now a real `AiSettings__ApiKey`: `AiSettings.Validate()`
throws `ConfigurationErrorsException` naming `AiSettings.ApiKey` without one, exactly like every
other required setting in this project — `BaseUrl`, `Model` and `MaxTokens` already have
defaults in `appsettings.json`, but a bearer-token secret cannot ship one. Point
`DatabaseSettings__ConnectionString` at a real local Postgres, then:

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project src/Assistant.Worker
```

From the owner's own Telegram account, send the bot a plain-language message, e.g.
`call the bank tomorrow at 10`. Expected: within a few seconds, the bot replies in the same chat
with the model's own generated answer, not an echo — the exact wording is the model's, so nothing
here pins it to a literal string. `docs/e2e-local.md`'s real-Telegram walkthrough is corrected in
this slice's docs commit to record the same `AiSettings:ApiKey` requirement as a user secret,
alongside the bot token and chat id it already asked for.

---

## Steps

**Decisions this slice carries:** 1–2, given in full above.

**Consumes:** `IAiClient`/`AddAssistantAi` (slice 3, merged `e1bcad3`). **Produces:** the updated
`MessageHandler`, the corrected design documents.

Two commits: code first, then docs. **Do not merge them into one** — the docs commit corrects
facts that span all four slices, not just this one, and reviewing that separately from a
behaviour change is easier for both halves.

### Commit 1: reply with the model's answer instead of an echo

**Files:**
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`
- Modify: `src/Assistant.Impl/Telegram/MessageHandler.cs`

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
        await wireMock.SeedAiAnswerAsync(ModelAnswer);
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
    /// This test no longer checks the reply's exact text: with the reply now the model's
    /// answer rather than an echo, that check duplicated
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

`TimeSettings` and `AiSettings` both live in `Assistant.Impl.Settings`, already imported by this
file's existing `using Assistant.Impl.Settings;` — no new `using` needed.

- [ ] **Step 2: Run them and watch the renamed test fail for the right reason**

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
        var answer = await ai.AskAsync(text, ct);

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

Expected: zero warnings; unit tests unchanged at 41; every integration test green at 32 — this
commit renames one test and trims one assertion, and adds or removes none.

- [ ] **Step 6: Commit**

```bash
git add tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs \
        src/Assistant.Impl/Telegram/MessageHandler.cs
git commit
```

Message:

```
feat: reply with the model's answer instead of an echo

MessageHandler resolves IAiClient from a per-call DI scope, the way
DueReminderJob already resolves ITaskService -- a Refit client is a typed
HttpClient, and capturing one in a singleton handler would pin its message
handler and defeat handler-factory rotation. When the model cannot be
reached, the owner gets a fixed apology instead of an exception. No
Program.cs change: slice 1 already wired AddAssistantAi into the chain, and
this PR only ever consumes IAiClient through the scope factory
MessageHandler already received.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

### Commit 2: record what F9a settled

**Files:**
- Modify: `docs/design/slice-1-reminders.md`
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`
- Modify: `AGENTS.md`
- Modify: `docs/e2e-local.md`
- Check (no change expected): `README.md`

- [ ] **Step 7: Correct the spec's stack line**

The document header (before §1) reads:

```
**Stack:** C# / .NET 10 (LTS), PostgreSQL 16, Telegram Bot API, `Microsoft.Extensions.AI`
```

`Microsoft.Extensions.AI` is never referenced anywhere this feature touches — §5.5 and §12.3 both
already mandate Refit. Correct the line to:

```
**Stack:** C# / .NET 10 (LTS), PostgreSQL 16, Telegram Bot API, Refit
```

- [ ] **Step 8: Correct §3.4's `Ai/` folder line**

Spec §3.4's `Assistant.Impl/` tree is what F9a most directly implements, and its `Ai/` line still
names the two Refit interfaces decision 1 (slice 3) retired, plus an adapter list that never
matched what shipped. Every other line of the tree is untouched; only the `Ai/` line and its
continuation change, and both replacement lines keep opening their description at the same
column the rest of the tree uses.

Before:

```
├─ Ai/             IAnthropicApi, IOpenRouterApi (Refit), the IChatClient adapters,
│                  FallbackChatClient
```

After:

```
├─ Ai/             IAiApi (Refit), AiClient (the IAiClient adapter), SystemPrompt.
│                  FallbackChatClient is undecided, see §5.5.
```

- [ ] **Step 9: Correct §3.6's extension-seams table**

§3.6's table still lists a client-interface row as one of the project's extension seams. It is
not one, and has not been since slice 3 shipped: `IAiClient` has exactly one production
implementation and is a transport abstraction rather than a seam that grows by adding a class,
the same reasoning `IAiClient.cs`'s own remarks record today. Every other row in the table
genuinely does grow by adding a class — `ITaskAction`, `IAssistantTool`, `IScheduledJob`,
`INotifier`, `TimeProvider` — so the fix is deletion, not a rename.

Delete this row, the table's last, immediately after the `TimeProvider` row:

```
| `IChatClient` | `AnthropicChatClient`, `OpenRouterChatClient`, `FallbackChatClient` | Provider change is configuration |
```

- [ ] **Step 10: Correct §5.1's flow diagram**

The flow diagram's interface name is stale; the step itself — a tool call following the model's
answer — is still F9a's eventual target shape and is not touched, since F9b is what actually adds
it.

Before:

```
  → IChatClient → tool call
```

After:

```
  → IAiClient → tool call
```

- [ ] **Step 11: Correct and extend §5.5**

Replace §5.5's full body with:

```markdown
### 5.5 Provider routing and fallback

**Corrected at F9a:** every provider this project reaches is exercised through **one** Refit
interface, `IAiApi` (§12.3), named for the OpenAI-compatible chat API that OpenRouter, OpenAI,
Groq and a local Ollama all serve — not `IAnthropicApi`/`IOpenRouterApi` as this section
originally named them. Anthropic is ruled out of slice 1 entirely; OpenRouter is the provider
`AiSettings` ships a default for, and switching to any other OpenAI-compatible endpoint is a
configuration change (`AiSettings.BaseUrl`, `AiSettings.Model`), not a new type. `AiClient` is
the one `IAiClient` adapter, translating between the wire format and the project's own request
and response types.

`FallbackChatClient` is a decorator wrapping a primary and a secondary `IAiClient`, with Polly
for timeout and circuit-breaking. Neither concrete client is aware of the other; only the
composition root changes when providers change.

**Open question, raised at F9a, not resolved here:** OpenRouter is itself a router — a request
against one upstream model can already fail over to another model before OpenRouter ever answers
the caller. Going OpenRouter-first for the primary (and, plausibly, the fallback too) may make
`FallbackChatClient` redundant with routing OpenRouter already does. This section owes an answer
before F13 builds `FallbackChatClient`; F9a does not decide it.
```

- [ ] **Step 12: Correct §7.5's architecture-tests list**

§7.5's "Namespace-level, inside `Impl`" list still forbids `Impl.Services` from referencing
`Microsoft.Extensions.AI` types — a library this feature never references. One line, corrected in
the same commit as the header line it shares its stale reasoning with.

Before:

```
- `Impl.Services` referencing `Telegram.Bot` or `Microsoft.Extensions.AI` types
```

After:

```
- `Impl.Services` referencing `Telegram.Bot`, the OpenAI-compatible wire types, or `Refit` types
```

- [ ] **Step 13: Correct §12.3's registration sentence**

§12.3's illustrative Refit interface (`IAnthropicApi`, `/v1/messages`) stays exactly as written —
it is a generic illustration of the shape a Refit interface takes for some hypothetical external
API, not a claim about `Assistant.Impl.Ai`'s own types, so it does not contradict decision 1's
vendor-neutral naming for the types this project actually ships. The one sentence describing how
registration works is a factual claim, though, and it is wrong: `AddRefitClient<T>()` is Refit
15's reflection path and needs a `Refit.Reflection` package this project does not take.
`AddAssistantAi` calls `AddRefitGeneratedClient<T>()` instead.

Before:

```
Registered via `AddRefitClient<T>()` with the base address, auth headers, and the Polly resilience handler attached at the `HttpClient` level, so retry and circuit-breaking are configuration rather than code inside the adapter.
```

After:

```
Registered via `AddRefitGeneratedClient<T>()` — Refit 15's source-generator path; `AddRefitClient<T>()` is the reflection path and needs a `Refit.Reflection` package this project does not take — with the base address, auth headers, and the Polly resilience handler attached at the `HttpClient` level, so retry and circuit-breaking are configuration rather than code inside the adapter.
```

- [ ] **Step 14: Split the backlog's F9 entry into F9a and F9b**

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
`IAiClient`, `IAiApi` via Refit against the OpenAI-compatible chat API, `AiSettings`, the system
prompt carrying the current local time, and `MessageHandler` replying with the model's answer
instead of an echo. No tools yet: parsing a `create_task` call out of the answer is F9b. Shipped
as four independently reviewable PRs — settings, the clock and system prompt, reaching the model,
and this reply.
*Tests:* free text gets an answer back from a WireMock'd provider; a provider failure and an
empty answer are each refused with a named `ErrorCode` instead of crashing the listener.
*Settled at F9a:*
- **OpenRouter, not Anthropic, and nothing is named after a vendor.** The repository owner ruled
  Anthropic out of slice 1 entirely. `IAiApi`, `AiClient` and `AiStubs` are named for the
  OpenAI-compatible chat API, which OpenRouter, OpenAI, Groq and a local Ollama all serve, so
  moving providers is a change to `AiSettings.BaseUrl` and `AiSettings.Model`, never a new type.
  Spec §5.5 named `IAnthropicApi`/`IOpenRouterApi`; corrected here. The transport interface
  itself went through two names before landing: `IChatClient`/`ChatCompletionsClient` were
  rejected on review — "Completions" named the vendor's own endpoint rather than anything the
  class does, and "Client" read as outbound in the `HttpClient` sense — before
  `IAiClient`/`AiClient` shipped.
- **The chat API turned out simpler than Anthropic's would have been.** The system prompt is
  `messages[0]` with `role: "system"`, not a separate top-level `system` field, so no record
  property is named `System` and the namespace-shadowing trap that shape would set up next to
  `System.*` types never arises.
- **`AiSettings` lives in `Impl/Settings/`,** joining `TelegramSettings`, `TimeSettings` and
  `DatabaseSettings` — configuration is not an `Ai/` concern, even though spec §3.4 places the
  Refit interface itself in `Impl/Ai/`. Shipped alone, first, together with a minimal
  `AddAssistantAi` that registers it — extended in place across later PRs, never recreated.
- **`AiSettings.BaseUrl` is required,** unlike `TelegramSettings.BaseUrl`. Telegram's is nullable
  because absent means "the real Telegram"; there is no single "the" chat API provider, so
  `appsettings.json` ships OpenRouter's address as a changeable default instead.
- **`IAiClient.AskAsync` returns `Result<string>` at F9a.** It changes shape at F9b, to
  `Result<ToolCall>`, once there is a tool call to parse out of the answer — a modification, not
  an extension, and accepted because `IAiClient` is a transport abstraction rather than one of
  spec §3.6's behaviour seams: it has exactly one production implementation today and will still
  have exactly one at F9b. The seam F9b actually grows is `IAssistantTool`. §3.6's own table
  named this interface as a seam; corrected here by removing the row rather than renaming it.
- **`ILocalTimeResolver` gained `CurrentLocalTime` and `ZoneId`,** the members F8's own "Settled
  at F8" note deferred until something needed to state "now" in the user's zone. Both live on the
  resolver, not injected as a raw `TimeZoneInfo` into `SystemPrompt`, so the zone keeps exactly
  one owner.
- **The offset formatter renders a half-hour zone as `UTC+10:30`, not `UTC+11` or `UTC+10`.**
  Tested against `Australia/Lord_Howe`, for the same reason F8 tested its gap and ambiguity rules
  there: a round-hour zone cannot catch a formatter that silently drops minutes.
- **The system prompt names the configured zone twice, never a literal.** It appears once next to
  the current time and once in "All times the user gives are `<zone>` local" — both reads from
  `ILocalTimeResolver.ZoneId`.
- **`MessageHandler` takes `IServiceScopeFactory`, not `IAiClient`, directly.** `TelegramListener`
  injects `IEnumerable<ITelegramUpdateHandler>` and is itself a singleton `BackgroundService`, so
  every handler is a singleton too. A Refit client is a typed `HttpClient`; capturing one in a
  singleton would pin its message handler and defeat the factory's handler rotation.
  `DueReminderJob` already solved this identical problem for `ITaskService`; `MessageHandler` now
  solves it the same way, and F10 will need the same scope for `ITaskService` too.
- **A provider failure is an answer, not a crash — shipped in two commits, in one PR, on
  purpose.** The happy-path `AiClient` went in first, with no `ErrorCode` and no `try`/`catch`,
  so the failing-test step for the 500 and empty-choices cases showed a real crash
  (`Refit.ApiException`, and an `ArgumentOutOfRangeException` off an empty array) before
  `ModelUnavailable` and `ModelReturnedNoAnswer` gave those two failures a name and a graceful
  `Result<string>.Failure`.
- **`ErrorCode` gained `ModelUnavailable` and `ModelReturnedNoAnswer`, appended** after
  `DueTimeTooFarAhead` — no existing member's numeric value moved.
- **Integration tests do not pin the clock.** No assertion in this feature reads the system
  prompt's time content at either level — `SystemPromptTests` owns that ground with
  `FakeTimeProvider`, so `AiClientTests` and `TelegramListenerTests` both run against
  `AddAssistantServices()`'s default `TimeProvider.System`, unmodified.
- **F7's echo test became an assertion on the model's answer**, as the backlog always intended.
  Its "only the owner is answered" sibling lost its own copy of the reply's exact text: checking
  it there duplicated the renamed test, which spec §7.2 forbids — a de-duplication, not a
  weakened test; the renamed test alone now owns the reply's content.
- **`.env.example` carries `AiSettings__ApiKey`, `AiSettings__Model` and `AiSettings__BaseUrl`**,
  replacing two dead keys (`LLM__ANTHROPIC__APIKEY`, `LLM__OPENROUTER__APIKEY`) that predated
  every naming convention in this repository and that no code had ever read.
- **The default model is `anthropic/claude-sonnet-5`, verified present in OpenRouter's live
  model list.** A model slug naming a vendor is unavoidable and is not what the vendor-neutral
  type naming above is about. `.env.example` names `anthropic/claude-haiku-4.5`, also verified
  present, as the cheaper alternative.
- **The "typing…" indicator stays deferred, again.** Spec §5.1 deferred it to F9 because F7 had
  no wait to cover. F9a does have one now, but the indicator needs an `INotifier` member and a
  refresh loop that belongs with F10's kept reply, not F9a's throwaway prose — a fresh deferral,
  not an inherited one.

**F9b · Parse a tool call out of the answer** — spec §5.2, §5.3, §12.3
`IAssistantTool`, `CreateTaskTool` as its first implementation, tool definitions added to the
chat request, `tool_calls` parsed out of the response, `CreateTaskRequest` in `Contracts`, and
`IAiClient.AskAsync` changed to return `Result<ToolCall>` in place of `Result<string>`.
*Tests:* free text produces the expected tool call against a WireMock'd provider.
```

- [ ] **Step 15: Correct `AGENTS.md`'s project-map row**

`AGENTS.md`'s `Assistant.WireMock` row has read "(Telegram today)" since before this feature; it
stopped being accurate the moment slice 3 gave the same container a second endpoint to answer.
Nothing else in `AGENTS.md` needs a change: its command list introduces no new command, and the
Refit rule in `docs/conventions.md` §12.3 already covered this feature before it existed, the
same way F8 found `DependencyRuleTests` already covered it.

Before:

```
| `Assistant.WireMock` | Stub API server (Telegram today) run as the `wiremock` service in `compose.test.yaml`, port 58080 | nothing |
```

After:

```
| `Assistant.WireMock` | Stub API server (Telegram and the chat endpoint) run as the `wiremock` service in `compose.test.yaml`, port 58080 | nothing |
```

- [ ] **Step 16: Correct `docs/e2e-local.md`**

Three edits, all in service of the same fact: `AiSettings.ApiKey` has been required at startup
since slice 1, and this document has never said so.

First, add the two new settings to "How configuration resolves"'s list:

Before:

```
Settings bind by section name, double underscore for nesting:

- `TelegramSettings__BotToken`
- `TelegramSettings__OwnerChatId`
- `TelegramSettings__BaseUrl`
- `DatabaseSettings__ConnectionString`
```

After:

```
Settings bind by section name, double underscore for nesting:

- `TelegramSettings__BotToken`
- `TelegramSettings__OwnerChatId`
- `TelegramSettings__BaseUrl`
- `DatabaseSettings__ConnectionString`
- `AiSettings__ApiKey`
- `AiSettings__BaseUrl`
```

Second, extend the stub run's command (step 3 of the stub walkthrough) so the worker starts and
stays hermetic — the same WireMock container already answers `/chat/completions`, at weak
priority, with a default stubbed answer nobody in this walkthrough needs to read:

Before:

```bash
DatabaseSettings__ConnectionString="Host=localhost;Port=55432;Database=assistant_e2e;Username=assistant;Password=assistant" \
TelegramSettings__BotToken="111111:AAFakeTokenForLocalStubRunsOnly_xxxxx" \
TelegramSettings__OwnerChatId="<your-chat-id>" \
TelegramSettings__BaseUrl="http://localhost:58080" \
dotnet run --project src/Assistant.Worker
```

After:

```bash
DatabaseSettings__ConnectionString="Host=localhost;Port=55432;Database=assistant_e2e;Username=assistant;Password=assistant" \
TelegramSettings__BotToken="111111:AAFakeTokenForLocalStubRunsOnly_xxxxx" \
TelegramSettings__OwnerChatId="<your-chat-id>" \
TelegramSettings__BaseUrl="http://localhost:58080" \
AiSettings__ApiKey="stub-key-not-checked" \
AiSettings__BaseUrl="http://localhost:58080" \
dotnet run --project src/Assistant.Worker
```

Add a sentence after the command explaining why: `AiSettings.ApiKey` has no default anywhere, so
every worker run needs one from here on, even this one, which never sends the owner a message;
`AiSettings.BaseUrl` is pointed at the same stub `TelegramSettings.BaseUrl` already uses so the
walkthrough stays fully offline.

Third, extend the real-Telegram walkthrough with the third secret it now needs, alongside the two
it already sets:

Before:

```
Two things differ from the stub run: you supply a real bot token, and you omit
`TelegramSettings__BaseUrl` so the client talks to `api.telegram.org` instead of the stub.
```

```bash
dotnet user-secrets set "TelegramSettings:BotToken" "<token from BotFather>" \
  --project src/Assistant.Worker
dotnet user-secrets set "TelegramSettings:OwnerChatId" "<your-chat-id>" \
  --project src/Assistant.Worker
```

After:

```
Three things differ from the stub run: you supply a real bot token, you omit
`TelegramSettings__BaseUrl` so the client talks to `api.telegram.org` instead of the stub, and
you supply a real `AiSettings:ApiKey` instead of the stub run's throwaway one --
`AiSettings__BaseUrl` needs no override here, since `appsettings.json`'s own default already
points at the real OpenRouter endpoint.
```

```bash
dotnet user-secrets set "TelegramSettings:BotToken" "<token from BotFather>" \
  --project src/Assistant.Worker
dotnet user-secrets set "TelegramSettings:OwnerChatId" "<your-chat-id>" \
  --project src/Assistant.Worker
dotnet user-secrets set "AiSettings:ApiKey" "<your OpenRouter key>" \
  --project src/Assistant.Worker
```

Further down, where the document says the full run needs "no environment variables at all"
because "user secrets supply the token," extend that to "the token and the model key" — the same
sentence, one clause longer, since the fact it is stating just grew a third input.

- [ ] **Step 17: Check `README.md`**

No change. Its Contributing section already reads "Telegram and the LLM APIs are stubbed with
WireMock" (plural), which was already true in spirit and is now true in fact; its Quickstart says
to fill in `.env.example`'s values generically rather than enumerating them, so slice 1's
`.env.example` edit needed no matching README update and neither does this slice's. Its
"Deliberate limitations" section does not mention the model provider at all, and this feature
adds no new deliberate limitation.

- [ ] **Step 18: Run the whole suite once more, then commit**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

Expected: unchanged from Commit 1 — 41 unit, 32 integration, zero warnings. This commit touches
no source or test file, so nothing here should move.

```bash
git add docs/design/slice-1-reminders.md docs/design/2026-08-22-slice-1-feature-backlog.md \
        AGENTS.md docs/e2e-local.md
git commit
```

Message:

```
docs: record what F9a settled

The spec's header and 5.5 both named IAnthropicApi/IOpenRouterApi and
Microsoft.Extensions.AI; neither survived contact with the OpenAI-compatible
chat API this feature actually used, or with IAiClient's own rename partway
through slice 3. Both are corrected here, 5.5 is left owing an answer on
whether FallbackChatClient is still worth building once OpenRouter's own
routing is accounted for, and AGENTS.md and the local end-to-end walkthrough
are brought back in line with what AiSettings has required at startup since
slice 1.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

---

## Self-review

**Commit 1 (code):**
- [ ] `MessageHandler`'s constructor declares `IServiceScopeFactory`, not `IAiClient` (grep the
      file — `IAiClient` appears only inside `HandleAsync`)
- [ ] `Listener_StrangerSendsAMessage_OnlyTheOwnerIsAnswered` asserts `Assert.Single(sent)`, no
      exact text — a deliberate de-duplication (decision 2), called out in the report
- [ ] `Listener_MessageAlreadyAnswered_DoesNotAnswerItAgain`'s body is byte-for-byte unchanged;
      only `InitializeAsync`'s registrations grew
- [ ] This commit's diff has no `Program.cs` hunk
- [ ] Unit tests unchanged at 41; integration tests all green at 32
- [ ] `docker compose down`, never `down -v`

**Commit 2 (docs):**
- [ ] No section of `docs/design/slice-1-reminders.md` still names `Microsoft.Extensions.AI`,
      `IAnthropicApi`, `IOpenRouterApi` or `IChatClient` as current design
- [ ] §3.6's table no longer lists a client interface as an extension seam
- [ ] The backlog's F9 entry is gone, replaced by F9a (marked done) and F9b
- [ ] `docs/e2e-local.md`'s stub-run command and real-Telegram secrets both mention `AiSettings`
- [ ] AGENTS.md's correction, or a README change if one turned out to be needed after all, is
      recorded in the report
- [ ] Build and both test suites still green after this commit, unchanged from Commit 1's numbers

**Whole feature, once both commits land:**
- [ ] Every new public member has a three-line `<summary>`; every test summary is Gherkin
- [ ] Every class taking arguments uses a primary constructor
- [ ] No emoji anywhere, including both commit messages
- [ ] No plan-internal decision citation (`(decision 1)`, `(decision G)`, or similar) inside any
      C# code block, doc comment, or commit message — scanned by hand before each commit
- [ ] Each commit stayed comfortably under the 1000-changed-line PR budget, combined
