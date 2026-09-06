# F10-4 — delete the echo

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F10 shipped in full at commit `724b600` (F10-3): the owner sends a plain message, the
chat model turns it into a `create_task` tool call, the task is stored, and the bot replies with
the title, the due time rendered in Asia/Jerusalem, and a Done button. The owner has verified
this on a real phone. In the owner's own words, once that reply exists: "every time i send a
message, once ai response, i want to delete my message so i wont see it, eventually i want only
things that i need to do without the messages that i send to the bot." F10-4 is the first step
toward that: after a successful capture, `MessageHandler` deletes the owner's own message,
leaving only its own reply. A refused capture leaves the owner's message alone. This is the only
change in this slice — no new button, no schema change, no change to what any reply says.

**Tech Stack:** `net10.0`, nullable enabled, warnings are errors — the existing stack. This slice
adds **no new NuGet package** — `Telegram.Bot` 22.10.2.1, already referenced by
`Assistant.Impl`, already exposes the delete call this slice needs — and **no database
migration**: nothing about `reminder_tasks` changes.

**Spec:** `docs/design/slice-1-reminders.md` §5.1 (the capture-path flow this slice appends one
step to, corrected in the same commit — see Decision 9), §6.4 (inline buttons — cited only to
confirm Done's own definition is untouched), §7.2 (unit vs. integration split — this slice adds
no unit test, argued in Decision 6), §7.3 (assertion standard), §12.1 (XML docs), §12.5 (primary
constructors), §12.6 (no emoji).

**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — the F10 entry (currently line
599, `**F10 · Store the parsed task and reply · observable**`). F10-3's own plan
(`docs/plans/2026-09-05-f10-3-the-reply-closes-the-loop.md`, Decision 9) specified in full how to
mark that entry done, but deferred actually doing so to whoever implemented F10-3 and obtained
the owner's real-phone verification — neither happened until now. This slice both finishes that
deferred correction and adds its own, since the owner's verification is now in hand.

---

## How this slice fits

F10 (F10-1 through F10-3) built the capture path end to end and stopped at "reply rendered with
inline keyboard" — the last step spec §5.1 named. F10-4 appends exactly one more step to that
flow: on a successful capture, delete the message that started it. It touches the same file
F10-3's own diff was almost entirely about, `MessageHandler.cs`, and needs no interface change,
no new `ErrorCode`, no new `Result<T>` shape — every dependency this slice needs is either
already injected into `MessageHandler` or already registered in the container for a sibling
handler (`CallbackRouter`) to use. Numbered F10-4 rather than a new feature, because it refines
the capture path F10 already built without changing what gets stored or what any reply says;
nothing downstream renumbers.

**The measured total, drafted in full and diffed, not estimated.** Every file below was written
out completely and diffed against the real working tree — the code files with `git diff` inside
an isolated worktree carrying these exact changes (built and tested green there, see
"Verified facts," below), the two documentation files with `git diff --no-index` against scratch
copies:

| File | + | - |
| :--- | ---: | ---: |
| `src/Assistant.Impl/Telegram/MessageHandler.cs` | 37 | 3 |
| `tests/Assistant.WireMock/TelegramStubs.cs` | 9 | 0 |
| `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs` | 68 | 0 |
| `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs` | 51 | 0 |
| **Code subtotal** | **165** | **3** |
| `docs/design/slice-1-reminders.md` | 12 | 0 |
| `docs/design/2026-08-22-slice-1-feature-backlog.md` | 27 | 2 |
| **Total** | **204** | **5** |

**Total changed lines: 209** (204 insertions + 5 deletions), far under the 1000-line budget and
a small fraction of F10-3's own 295. No file here approaches even F10-1's 120.

---

## Global Constraints

Every constraint the project's prior plans carry forward still applies here:

- `net10.0`; nullable enabled; warnings are errors; `CS1591`/`CS1573` are errors everywhere.
- **Every class taking arguments uses a primary constructor.** `MessageHandler` gains one more
  primary-constructor parameter, `ITelegramBotClient bot` — seven in total. No separate
  constructor is declared.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- Central package management; no inline `Version=`. Not exercised this slice — no package
  changes.
- No emoji anywhere: source, tests, docs, or commit messages.
- **Never run `docker compose down -v`.** Use `docker compose -f compose.test.yaml down` with no
  flags.
- **This slice modifies `tests/Assistant.WireMock/TelegramStubs.cs`, so, unlike F10-3, the
  integration suite needs the image rebuilt: `docker compose -f compose.test.yaml up -d --build`,
  not a bare `up -d`.** A stale `wiremock` image has no mapping for `deleteMessage` and every
  delete attempt 404s — see Decision 7 for what that does and does not break.
- PR budget: 1000 changed lines per PR, excluding the plan document. This slice measures at 209
  lines by the convention above — far under budget.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
  ```

---

## Verified facts this plan rests on

Every fact below was checked directly against the working tree at `724b600` (HEAD of
`feature/f10-4-delete-the-echo`, F10-3's own merge commit) or produced by a command actually run
during this planning session.

- **`Telegram.Bot` permits this.** The installed package's own XML documentation
  (`~/.nuget/packages/telegram.bot/22.10.2.1/lib/net6.0/Telegram.Bot.xml`) for
  `DeleteMessageRequest`/`TelegramBotClientExtensions.DeleteMessage` states: "Bots can delete
  **incoming** messages in private chats," among a longer list of limitations that includes "A
  message can only be deleted if it was sent less than 48 hours ago" — irrelevant here, since the
  delete happens seconds after the message arrives, well inside that window.
- **The exact API, verified by reflecting against the installed assembly, not assumed from
  version-mismatched documentation.** A throwaway console project referencing
  `Telegram.Bot` 22.10.2.1 (the version this repository's `Directory.Packages.props` pins)
  loaded `Telegram.Bot.dll` and reflected `TelegramBotClientExtensions.DeleteMessage`. Its exact
  signature:

  ```
  Task DeleteMessage(ITelegramBotClient botClient, ChatId chatId, int messageId, CancellationToken cancellationToken = default)
  ```

  It is a `static` extension method on `ITelegramBotClient`, declared in the `Telegram.Bot`
  namespace (the same namespace `SendMessage`, `EditMessageText`, `AnswerCallbackQuery`, and
  `GetUpdates` already live in and are already called from this way in
  `TelegramNotifier.cs` and `CallbackRouter.cs`), and it returns a bare `Task` — the wire
  envelope's `"result":true` is discarded by the SDK, exactly as `AnswerCallbackQuery`'s own
  `Task`-returning overload already discards its own boolean result. `ChatId` has an implicit
  conversion from `long`, so `settings.OwnerChatId` or a `long` captured from
  `update.Message.Chat.Id` passes directly, the same way `TelegramNotifier.SendAsync` already
  passes `settings.OwnerChatId` to `SendMessage`.
- **The exception hierarchy, also verified by reflection against the installed assembly.**
  `Telegram.Bot.Exceptions.RequestException : System.Exception` ("Represents a request error");
  `Telegram.Bot.Exceptions.ApiRequestException : Telegram.Bot.Exceptions.RequestException`
  ("Represents an API error" — thrown when Telegram itself answers `{"ok":false,...}`, e.g. the
  message was already deleted or the chat cannot be reached). Neither type derives from, or is
  derived from, `OperationCanceledException`/`TaskCanceledException` — the two branches share no
  ancestor below `Exception` itself.
- **`update.Message.Id` is already read as an `int` elsewhere in this codebase, for the same
  purpose.** `CallbackRouter.cs:72` destructures `Message: { Chat.Id: var chatId, Id: var
  messageId, Text: var messageText }` and passes `messageId` (an `int`) to
  `notifier.MarkCompletedTaskAsync(int messageId, ...)`. `DeleteMessage`'s own `messageId`
  parameter is likewise `int` — no cast needed.
- **`MessageHandler.cs` is 133 lines today** (confirmed by `wc -l`), with a six-parameter primary
  constructor `(TelegramSettings settings, INotifier notifier, IAiClient ai, IEnumerable
  <IAssistantTool> tools, ILocalTimeResolver clock, ILogger<MessageHandler> logger)`. Its guard
  clause is `if (update.Message is not { Chat.Id: var chatId, Text: { } text } || chatId !=
  settings.OwnerChatId) { return; }` — a message with no text (or from a non-owner) is discarded
  before any AI call, any reply, or any persistence. Its final line, on a successful capture, is
  `await notifier.SendTaskAsync(task.Id, reply, ct);`.
- **`ITelegramBotClient` is already registered as a singleton**, in `AddAssistantTelegram`
  (`ImplServiceCollectionExtensions.cs:40`: `services.AddSingleton<ITelegramBotClient>(client);`).
  `MessageHandler` is registered scoped (`AddAssistantListener`,
  `ImplServiceCollectionExtensions.cs:88`). A scoped service depending on a singleton has no
  captive-dependency problem — the unsafe direction is the reverse, a singleton capturing a
  scoped dependency, which does not occur here. `CallbackRouter`, also scoped, already depends on
  this exact singleton directly (`CallbackRouter.cs:45-49`). **No new DI registration is needed
  anywhere** — confirmed by reading `Program.cs`'s composition order, unchanged since F10-3.
- **`CallbackRouter` is the direct precedent for a Telegram update handler holding the bot
  client.** `internal sealed class CallbackRouter(TelegramSettings settings, ITelegramBotClient
  bot, INotifier notifier, IEnumerable<ITaskAction> actions) : ITelegramUpdateHandler` calls
  `bot.AnswerCallbackQuery(...)` directly, four times, for a Telegram-protocol operation
  (answering a callback query) that has no equivalent shape in `INotifier` and was never added to
  it.
- **The WireMock stub is a built container image, not a library.** `compose.test.yaml`'s
  `wiremock` service is `build: { context: ., dockerfile: tests/Assistant.WireMock/Dockerfile }`
  with no `image:` key — `docker compose up` builds it once and reuses the cached image on every
  later `up` unless told otherwise. `tests/Assistant.WireMock/TelegramStubs.cs` (74 lines today)
  maps exactly four paths: `/bot*/sendMessage`, `/bot*/answerCallbackQuery`,
  `/bot*/editMessageText`, `/bot*/getUpdates`. **There is no `/bot*/deleteMessage` mapping.** A
  delete call against the unmodified stub hits WireMock's own "no matching mapping" fallback,
  which is not the real Telegram envelope shape and throws inside the SDK's own deserialisation —
  caught by this slice's `try`/`catch` regardless (see Decision 7), but never actually
  succeeding, so a test waiting for a delete to land would time out.
- **CI needs no change, verified against `.github/workflows/ci.yml` directly and confirmed
  empirically, not assumed.** `ci.yml:37` runs `docker compose -f compose.test.yaml up -d --wait`
  — no `--build` flag. A GitHub Actions run starts from a fresh runner with no cached
  `wiremock` image under any name, and `docker compose up` builds a service's image whenever none
  already exists for it, `--build` or not. This was verified directly in this planning session:
  running `docker compose -p <throwaway-project> -f compose.test.yaml up -d --wait` with no
  `--build` flag, against a project name that had never been built before, built the `wiremock`
  image from scratch before starting the container. A **local** developer's already-built image
  is the one thing that would not pick up this change without an explicit `--build` — which is
  exactly why the Global Constraints section above calls it out for local runs.
- **The test-fixture pattern to copy.**
  `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs` (644 lines today) exposes
  `SentMessagesAsync()`, `EditedMessagesAsync()`, and `AnsweredCallbacksAsync()`, each filtering
  the admin request log by path suffix and deserialising into a record
  (`SendMessagePayload`, `EditMessageTextPayload`, `AnswerCallbackQueryPayload`, all declared at
  the bottom of the file), plus `WaitForSentMessagesAsync(count, deadline)` and
  `WaitForAnsweredCallbacksAsync(count, deadline)`, both polling loops with a 100ms interval and a
  `TimeoutException` on giving up.
- **Seeded message ids.** `WireMockFixture.SeedUpdatesAsync` sets
  `["message_id"] = u.UpdateId`, so `new InboundUpdate(10, OwnerChatId, "...")` produces a message
  whose id is `10`, decoded straight off the delete request in this slice's own positive test.
- **The synchronisation technique to reuse.**
  `Listener_StrangerSendsAMessage_OnlyTheOwnerIsAnswered`
  (`TelegramListenerTests.cs:163-177`), whose own `<remarks>` state: "Proving that nothing was
  sent to the stranger otherwise means waiting on a clock and hoping; putting the stranger first
  in the batch means that by the time the owner's reply arrives, the stranger's message has
  already been processed and skipped." `TelegramListener.PollOnceAsync` dispatches every update
  in one `getUpdates` response strictly in order, `await`ing each one's full handling
  (`TelegramListener.cs:79-84`) before moving to the next — this ordering guarantee, not a clock,
  is what makes proving an absence possible.
- **The AI stub answers every `/chat/completions` request identically, for the whole test, until
  re-seeded.** `WireMockFixture.SeedAiToolCallAsync`/`SeedAiAnswerAsync`/`SeedAiFailureAsync`/
  `SeedAiNoAnswerAsync` all install the same mapping GUID (`AiMapping`) with `bodyPattern: null`
  — an unconditional match. Two owner messages seeded in one `SeedUpdatesAsync` batch therefore
  receive the *same* canned tool call, whichever was seeded most recently — this shapes Decision 8
  below.
- **Baseline test counts, run directly, not assumed.** `dotnet build --no-restore` — `Build
  succeeded. 0 Warning(s). 0 Error(s).` `dotnet test tests/Assistant.UnitTests --no-build` — **57
  passed**, 0 failed. With `docker compose -f compose.test.yaml up -d`, `dotnet test
  tests/Assistant.IntegrationTests --no-build` — **68 passed**, 0 failed. Matches F10-3's own
  reported final state exactly.
- **This plan's own draft was built and tested green, not merely written.** All four code
  changes below were applied inside an isolated `git worktree` (never touching this working
  tree's `src/` or `tests/`), restored, built (`0 Warning(s)`, `0 Error(s)`), and run against a
  freshly rebuilt `wiremock` image: **70 passed**, 0 failed (the 68-test baseline plus this
  slice's two new tests). The two new tests were additionally run five more times in isolation
  (`dotnet test --filter`) with no failure, to check for the kind of timing flakiness a
  delete-ordering test is most at risk of. The worktree was then torn down (`docker compose
  down`, no `-v`) and removed.

---

## Inherited context: what this slice reads from earlier features

`MessageHandler`'s tool-dispatch and reply-rendering logic (F10-3) is consumed as-is and
unmodified except for the one new step this slice appends after the final `SendTaskAsync` call.
`INotifier`/`TelegramNotifier` (F6-3) are consumed as-is — nothing about the interface changes.
`ITelegramBotClient` and the precedent for a handler holding it directly (`CallbackRouter`, F6-2)
are read, not modified. `WireMockFixture`'s existing `SentMessagesAsync`/`WaitForSentMessagesAsync`
shape (F7, extended at F9a/F9b/F10-3) is extended in place, not replaced. `TelegramStubs.cs`'s
four existing mappings (F4b/F6-2/F6-3) are left untouched; a fifth is added beside them.

---

## Decisions

### 1. When the delete happens, and what a failure does — Rulings 1-3, not re-litigated

**Decision, exactly as given (owner-ruled):**

1. **Delete only on a successful capture.** Every failure branch in `MessageHandler` — the AI
   call failing, the model returning prose instead of a tool call, an unrecognised tool name, or
   any `ErrorCode` a tool's `ExecuteAsync` can return — leaves the owner's message untouched.
2. **Reply first, then delete.** `notifier.SendTaskAsync` is awaited to completion before
   `bot.DeleteMessage` is even attempted.
3. **A failed delete is logged at warning and never surfaced to the owner.** Deletion is
   best-effort.

**Why, restated rather than re-argued.** On a failure, the owner has nothing yet: no row was
written, and the only thing they have is what they typed. Deleting that would leave them holding
a "that didn't work" sentence with no record of the words that produced it — strictly worse than
leaving it in place. On a success, the task is already durable (the row exists) and the owner
already has the reply confirming it before the delete is ever attempted; if the delete fails, the
owner still has both of those. The reverse order — delete, then reply — has no such fallback: a
delete that succeeds followed by a reply that fails would leave the owner with neither their own
message nor a confirmation, for a task that in fact exists. Logging a failed delete at warning
records the fact for whoever reads the logs without treating it as anything the owner needs
telling — the task is saved and the reply is sent regardless of whether the echo of their own
message vanishes.

**Structurally, not merely by convention.** `MessageHandler`'s only calls to
`bot.DeleteMessage` sit after the method's one and only `SendTaskAsync` call, itself reached only
past every earlier `return` in the failure branches (see the code in Step 8). There is no path
from any failure branch to a delete call — the same shape F10-3's own Decision 6 established for
why a failure never carries a Done button (`SendTaskAsync` needs a `taskId` no failure path has
produced).

### 2. `MessageHandler` takes `ITelegramBotClient` directly — argued, not merely asserted

**Decision:** `MessageHandler` gains `ITelegramBotClient bot` as a seventh primary-constructor
parameter and calls `bot.DeleteMessage(chatId, messageId, ct)` directly. **No method is added to
`INotifier`.**

**The rejected alternative: `Task DeleteOwnersMessageAsync(int messageId, CancellationToken ct)`
on `INotifier`, implemented by `TelegramNotifier`, called through the `notifier` parameter
`MessageHandler` already has.** This was the more obvious-looking option, since `MessageHandler`
already depends on `INotifier` for its reply and it would have added no new constructor
parameter. It is rejected for a reason grounded in what `INotifier` actually is, not merely in
preferring fewer moving parts: `INotifier`'s own `<remarks>` (`src/Assistant.Interfaces/
INotifier.cs:6-15`) describe its members as "the channel-neutral handle an adapter needs to build
whatever affordance its own channel supports" — a description that fits a task id (any channel
can attach *something* to a task identifier) but does not fit "delete the message that named
this task." Deletion is not a data shape a future channel adapts to its own affordance; it is a
capability some channels simply do not have. Telegram's own permission model here is unusual
enough to quote directly (see "Verified facts," above): a bot may delete an **incoming** message
in a private chat, a capability that has no obvious equivalent for, say, an email or SMS channel,
where the sender's own client — not the assistant — is the only thing that can make a sent
message disappear. Adding `DeleteOwnersMessageAsync` to `INotifier` would force every future
notifier to either implement real deletion or silently no-op, and a silent no-op on an interface
member that looks like it does something is worse than the member not existing.

**The precedent this project already set for exactly this shape of problem.**
`CallbackRouter` — a Telegram update handler, not a notifier — already holds `ITelegramBotClient
bot` directly (`CallbackRouter.cs:45-49`) to call `bot.AnswerCallbackQuery(...)`, a
Telegram-protocol operation with no equivalent notion in `INotifier` and never added to it. F10-4
gives `MessageHandler` the identical shape for the identical kind of reason: a Telegram-specific
affordance reached by a Telegram-specific handler, not routed through the channel-neutral
interface a future channel would also have to implement.

### 3. Done is unchanged

Per the ruling: it still strikes the message through and clears the keyboard (spec §6.4).
Nothing in `CallbackRouter.cs`, `DoneAction.cs`, `ITaskAction.cs`, `CallbackCodec.cs`, or
`TaskActions.cs` is touched by this slice.

### 4. The exception to catch, and what the log line may contain

**Decision: catch `Telegram.Bot.Exceptions.RequestException`, log the message id alone, and pass
no exception object to the logger.**

**Narrow versus broad, argued rather than defaulted.** The narrowest option,
`Telegram.Bot.Exceptions.ApiRequestException`, would only catch a failure Telegram itself
reports (message already gone, chat unreachable) and would let a lower-level SDK failure — a
`RequestException` that never became an `ApiRequestException`, e.g. the HTTP call itself
failing — propagate uncaught, breaking a capture that already fully succeeded over a delete that
was never more than a courtesy. The broadest option, a bare `catch (Exception)`, would also catch
an `OperationCanceledException`/`TaskCanceledException` fired by the same `ct` this method
already threads through every other `await` — silently absorbing what is really a shutdown
signal, not a delete-specific failure. The chosen middle ground, `RequestException` (the base
type of both `RequestException` itself and `ApiRequestException`), covers every realistic
delete-specific failure — the SDK's own request-level exception and Telegram's own API-level
one — while remaining a **different branch of the exception hierarchy entirely** from
`OperationCanceledException` (verified by reflection, "Verified facts," above): catching
`RequestException` cannot ever catch a cancellation, so no `when (ex is not
OperationCanceledException)` guard is needed the way `TelegramListener.cs`'s own broader
`catch (Exception ex)` clauses require one.

**What the log line may say.** `logger.LogWarning("Could not delete the owner's message
{MessageId}.", messageId);` — no exception object, no exception message, no arguments. This
mirrors `MessageHandler`'s own existing precedent for `ModelNamedUnknownTool` (F10-3 Decision 2):
log the one identifier that matters and nothing else, on the grounds that the repository is
public and log output is public, and a future edit is less likely to widen a bare id into
something it should not than it is to widen `logger.LogWarning(ex, ...)` into interpolating one
of the exception's own properties. Telegram's own error strings for this call are generic
protocol text ("message to delete not found," "message can't be deleted") that never echoes a
task title or the owner's own words, so passing `ex` would in practice be safe content-wise —
but the id alone is already everything a maintainer needs to correlate the warning with a
specific update in the surrounding structured log, and omitting the exception object removes any
future temptation to log more.

### 5. The non-text-message guard needs no change

`MessageHandler`'s existing guard, `if (update.Message is not { Chat.Id: var chatId, Text: { }
text } || chatId != settings.OwnerChatId) { return; }`, already discards a non-text message (or
one from a non-owner) before any AI call, any reply, and any persistence — there is nothing to
delete because nothing was ever attempted, so this slice only adds `Id: var messageId` to the
same pattern to capture the id a successful path will need, changing no branch's outcome.

### 6. No unit test is added

Per spec §7.2, a unit test earns its place only for a combinatorial table, a pure mapper, or a
rule with no observable side effect. This slice's entire change is one more `await` on an
existing, already-integration-tested code path, with no new branching logic and no pure function
to isolate — `bot.DeleteMessage`'s own correctness is Telegram's SDK's concern, not this
project's, the same reasoning that already keeps `TelegramNotifier` and `CallbackRouter` free of
unit tests. `MessageHandler` has no unit tests today and gains none here.

### 7. The WireMock stub and fixture additions, and whether a waiting variant is needed

**The stub mapping.** `TelegramStubs.cs` gains a fifth mapping, `/bot*/deleteMessage`, answering
`{"ok":true,"result":true}` — the same envelope shape `answerCallbackQuery` already uses, since
both wrap a bare boolean result.

**The fixture additions.** `WireMockFixture` gains `DeletedMessagesAsync()` (a snapshot,
mirroring `SentMessagesAsync`), `WaitForDeletedMessagesAsync(count, deadline)` (a polling wait,
mirroring `WaitForSentMessagesAsync`), and `DeleteMessagePayload` (a record, mirroring
`SendMessagePayload`).

**Whether the waiting variant is actually needed, argued rather than added by reflex.** Yes, for
the positive test, and no, for the negative one — these are two different questions with two
different answers. A reply and its own delete are two independently timed HTTP calls: waiting
only for `WaitForSentMessagesAsync` proves the reply landed, but says nothing about whether the
delete that follows it — a separate, unawaited-by-the-test round trip — has also reached the
stub's request log yet. Checking `DeletedMessagesAsync()` immediately after the reply arrives
would race. `WaitForDeletedMessagesAsync` closes that race the same way its three siblings
already do. The negative test needs no such helper: it synchronises on a *second* message's
reply (Decision 8, below), and by the time that second reply has arrived, the first message's
entire handling — including any delete it might incorrectly have attempted — has already run to
completion, per `TelegramListener`'s own strictly sequential per-batch dispatch. A snapshot
suffices there because the synchronisation has already happened by the time it is taken.

### 8. The two test scenarios, and the technique that proves the negative one

**Exactly two new integration tests, one per named business behaviour, added to
`TelegramListenerTests.cs`.** No unit test (Decision 6); no change to the existing six test
methods.

**`Listener_OwnerSendsAMessageThatCaptures_DeletesTheOwnersMessage`.** Reuses the class's own
default seeded tool call (a due-time capture, unchanged since F10-3) and the class's own default
seeded update (message id `10`). Asserts, via `WaitForDeletedMessagesAsync(1, ReplyDeadline)`,
that exactly the right chat and exactly message id `10` were deleted — decoded off the delete
request itself, the same standard spec §7.3 already sets for a send (count, recipient, exact
target).

**`Listener_ARefusedCaptureIsFollowedByAnotherMessage_NeitherMessageIsDeleted`, and why it needs
two messages, not one.** Proving an absence needs a synchronisation device, not a sleep —
`Listener_StrangerSendsAMessage_OnlyTheOwnerIsAnswered`'s own `<remarks>` solve exactly this
problem, and this test reuses the same idea: seed a genuine refused capture (a `create_task` call
missing its title — the identical `ToolArgumentMissing` shape the existing
`Listener_ModelsToolCallCannotBeCarriedOut_RepliesWithNoButton` theory already proves produces a
reply with no button) as update `10`, and a second owner message as update `11` in the *same*
`SeedUpdatesAsync` batch. Because the shared AI mapping answers every `/chat/completions` call
identically until re-seeded (see "Verified facts"), both messages get the same missing-title tool
call and both are refused — which is exactly what is wanted here: by the time
`WaitForSentMessagesAsync(2, ReplyDeadline)` confirms the *second* reply arrived, the first
message's entire handling has already run to completion (`TelegramListener.PollOnceAsync`
dispatches a batch strictly in order, each update `await`ed before the next begins), so a
zero-count `DeletedMessagesAsync()` snapshot taken at that point proves neither message was
deleted — not merely that the first one probably wasn't by the time a fixed sleep happened to
elapse.

**Why one representative failure code is enough, not all six.** `CreateTaskToolTests` already
proves, at the tool level, that a missing-title call never reaches `taskService.CreateAsync`; the
existing six-row theory in this same file already proves each of the six failure codes produces
the right sentence with no button. This slice's own claim — that none of those six failure paths
ever calls `bot.DeleteMessage` — is a structural fact about `MessageHandler`'s own control flow
(Decision 1's own structural argument, above): every failure branch returns before the method's
one and only `SendTaskAsync` call, and the delete sits after that call, reachable from nowhere
else. Testing this once, on one representative failure, proves the wiring; repeating it across
all six would duplicate a claim that is true by construction for every one of them, which spec
§7.2 already forbids.

### 9. Documentation updates

**Spec §5.1 gains one line and one new paragraph.** The flow diagram's final step,
`reply rendered with inline keyboard`, gains `on a successful capture, delete the owner's own
message` beneath it, and a new `**Added at F10-4:**` paragraph (matching the section's own
existing `**Deferred:**` paragraphs' voice) records that this step was never part of the
original design — it is the owner's own product decision, made once the capture path actually
worked, and it names F10-5 as the slice that extends the same idea to the reminder message
itself. This is a structural change to a documented flow, which `AGENTS.md` requires be recorded
in the same commit that makes it.

**The backlog's F10 entry is finally marked done, and gains a fourth slice's own bullets.** Two
separate corrections, kept apart because they were decided at different times:

1. F10-3's own plan (Decision 9, Location 2) specified in full — `**done**` suffix, the
   `ReminderTask`-does-not-regain-`Notes` correction, and closing bullets naming the three
   pull requests and stating "F10 is closed" — but explicitly deferred writing any of it to
   whoever implemented that slice and obtained the owner's real-phone verification, since the
   planning session that wrote it could do neither. Neither happened until now. This slice
   applies that already-specified text, labelled `*Settled at F10-3, applied at F10-4:*` so the
   backlog stays honest about when the decision was made versus when it was recorded.
2. A new `*Settled at F10-4:*` block records this slice's own decision: the owner's message is
   deleted on a successful capture, `MessageHandler` reaches `ITelegramBotClient` directly rather
   than growing `INotifier` (Decision 2, above, restated in miniature), the delete is
   best-effort and logged by id alone (Decision 4), and F10-5 is named as the slice that stores
   the capture message's id on `reminder_tasks` so a fired reminder can delete it too.

**`docs/e2e-local.md` needs no change.** Read in full during this planning session: every step in
both its "Walkthrough against the stub" and "Walkthrough against real Telegram" sections seeds a
due task directly with a raw SQL `INSERT`, and neither walkthrough ever sends a plain-text
message to be parsed by the chat model at all — the free-text capture path F9/F10 built has never
been exercised by this document, even for F10's own reply-rendering behaviour. There is no
existing step describing message capture for this slice to append an observation to, and writing
an entirely new "capture via chat" walkthrough section is out of proportion for a slice this size
and not required by this task.

### 10. Sizing

Answered in full in "How this slice fits," above, with every file drafted completely and
measured — the four code files via `git diff` inside an isolated, build-and-test-verified
worktree, the two documentation files via `git diff --no-index` against scratch copies: **165
insertions / 3 deletions across the four `src`/`tests` files (168 changed), plus 39 insertions /
2 deletions across the two documentation files (41 changed) — 204 insertions / 5 deletions, 209
changed lines in total.** Far under the 1000-line budget, and roughly a tenth of F10-3's own 295.

---

## What this slice does NOT include

- **Storing the Telegram message id on `reminder_tasks`, and the fired reminder deleting the
  capture message.** That is **F10-5**, which needs a migration and changes
  `INotifier.SendTaskAsync` from `Task` to `Task<int>`. Named as the next slice; nothing here
  builds any part of it.
- **The Schedule button, `ITaskAction` gaining an argument, and `CallbackCodec`'s fourth
  segment.** That is **F11**.
- **Deleting the bot's own failure replies.** They stay, per Ruling 1.
- **Any change to what a reply says**, for a success or a failure. `MessageHandler`'s seven
  reply constants and its `DueTimeFormat` string are all unchanged from F10-3.
- **Any change to `INotifier`, `TelegramNotifier`, `CallbackRouter`, `DoneAction`, `ITaskAction`,
  `CallbackCodec`, or `TaskActions`.** Confirmed unaffected — see Decision 2 and Decision 3.
- **Any new production DI registration.** `ITelegramBotClient` is already a singleton
  `MessageHandler` can resolve with zero container changes (see "Verified facts").
  `ImplServiceCollectionExtensions.cs` is not touched by this slice.
- **Any unit test.** See Decision 6.
- **A new `ErrorCode` member.** A failed delete is not a capture failure — the capture already
  succeeded — so it has no reply-mapped sentence and needs no code to carry one.
- **`docs/e2e-local.md`.** See Decision 9.

---

## File Structure

```
src/Assistant.Impl/
    Telegram/MessageHandler.cs                          + ITelegramBotClient, + delete step

tests/Assistant.WireMock/
    TelegramStubs.cs                                    + deleteMessage mapping

tests/Assistant.IntegrationTests/
    Infrastructure/WireMockFixture.cs                   + DeleteMessagePayload,
                                                         + DeletedMessagesAsync,
                                                         + WaitForDeletedMessagesAsync
    Telegram/TelegramListenerTests.cs                   + 2 Facts

docs/design/
    slice-1-reminders.md                                §5.1 flow line + Added-at-F10-4 note
    2026-08-22-slice-1-feature-backlog.md                F10 entry: done marker + two
                                                         Settled-at blocks
```

No `Assistant.Contracts`, `Assistant.Interfaces`, `Assistant.Repository`, `Assistant.Worker`,
`Assistant.UnitTests`, or migration file is touched.

---

## Validation

**Test count arithmetic.** Baseline, run directly (see "Verified facts"): 57 unit, 68
integration.

- Unit: 57 + 0 = **57**. No unit test is added (Decision 6).
- Integration: `TelegramListenerTests.cs` goes from six test methods to eight, both new ones
  `[Fact]`s. 68 + 2 = **70**. No other integration test file changes.

**Expected final state: 57 unit, 70 integration — already confirmed, not merely predicted.**
This plan's own draft was built and run against these exact numbers inside an isolated worktree
during planning (see "Verified facts"): **70 passed**, 0 failed, with the two new tests
additionally repeated five times in isolation with no failure. The commands for the implementer
to reproduce this:

```bash
docker compose -f compose.test.yaml up -d --build
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
docker compose -f compose.test.yaml down
```

The `--build` flag is required here and was not for F10-3 — see Global Constraints.

---

## Steps

**Decisions this slice carries:** all ten, given in full above — 1, 3, and 5 restate owner
rulings and existing behaviour without change; 2, 4, 6, 7, 8, 9, and 10 are this plan's own
arguments.

**Consumes:** `MessageHandler`'s existing tool-dispatch and reply-rendering logic, `INotifier`/
`TelegramNotifier` (unchanged), `ITelegramBotClient` and the precedent for a handler holding it
(`CallbackRouter`, F6-2), `WireMockFixture`'s existing `SentMessagesAsync`/
`WaitForSentMessagesAsync` shape.
**Produces:** the `deleteMessage` stub mapping; `WireMockFixture.DeletedMessagesAsync`,
`WaitForDeletedMessagesAsync`, `DeleteMessagePayload`; `MessageHandler`'s delete step; two new
integration tests; the spec and backlog corrections.

One commit. The two new tests reference `WireMockFixture` members that do not exist until this
slice adds them, and a delete call against the unmodified stub image never actually succeeds
(Decision 7) — there is no smaller independently-buildable unit inside this slice than all of it
together, the same reasoning every prior slice in this feature has given for its own single
commit.

### Commit 1: delete the echo

**Files:**
- Modify: `tests/Assistant.WireMock/TelegramStubs.cs`
- Modify: `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`
- Modify: `src/Assistant.Impl/Telegram/MessageHandler.cs`
- Modify: `docs/design/slice-1-reminders.md`
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`

- [ ] **Step 1: Add the `deleteMessage` mapping to the stub**

In `tests/Assistant.WireMock/TelegramStubs.cs`, add a new constant after
`EditMessageTextResponse`:

```csharp

    private const string DeleteMessageResponse = """{"ok":true,"result":true}""";
```

and a new mapping inside `Install`, after the `editMessageText` mapping and before the
`getUpdates` one:

```csharp

        server
            .Given(Request.Create().WithPath("/bot*/deleteMessage").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(DeleteMessageResponse));
```

- [ ] **Step 2: Rebuild the stub image**

```bash
docker compose -f compose.test.yaml up -d --build
```

Expected: the `wiremock` service rebuilds (its `Dockerfile` copies
`tests/Assistant.WireMock/` fresh) and both containers report healthy.

- [ ] **Step 3: Add `DeleteMessagePayload` to the fixture**

In `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`, add a new record
immediately before `ReplyMarkupPayload`'s own declaration:

```csharp

/// <summary>
/// The body of a Telegram <c>deleteMessage</c> request.
/// </summary>
/// <param name="ChatId">The chat the deleted message lived in.</param>
/// <param name="MessageId">The message that was deleted.</param>
public sealed record DeleteMessagePayload(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("message_id")] int MessageId)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>Null when the request carried exactly the two named fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
```

- [ ] **Step 4: Add `DeletedMessagesAsync` to the fixture**

In the same file, add a new method immediately after `WaitForSentMessagesAsync`'s own closing
brace:

```csharp

    /// <summary>
    /// Returns the delete-message requests the stub received, in order.
    /// </summary>
    /// <returns>One payload per captured request.</returns>
    public async Task<IReadOnlyList<DeleteMessagePayload>> DeletedMessagesAsync()
    {
        var entries = await _http.GetFromJsonAsync<List<AdminLogEntry>>($"{Url}/__admin/requests")
                      ?? [];

        return entries
            .Where(entry => entry.Request.Path.EndsWith("/deleteMessage", StringComparison.Ordinal)
                            && entry.Request.Method == "POST")
            .Select(entry => JsonSerializer.Deserialize<DeleteMessagePayload>(entry.Request.Body)!)
            .ToList();
    }
```

- [ ] **Step 5: Add `WaitForDeletedMessagesAsync` to the fixture**

Immediately after the method added in Step 4:

```csharp

    /// <summary>
    /// Waits until the stub has received at least the given number of delete-message requests.
    /// </summary>
    /// <param name="count">How many deletes to wait for.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <returns>Every delete received, which may be more than requested.</returns>
    /// <exception cref="TimeoutException">Too few deletes arrived in time.</exception>
    /// <remarks>
    /// A reply and its own delete are two independently timed HTTP calls, so waiting only for
    /// <see cref="WaitForSentMessagesAsync"/> proves the reply landed, not that the delete which
    /// follows it has also reached the stub yet -- this polls the same way, for the call that
    /// comes second.
    /// </remarks>
    public async Task<IReadOnlyList<DeleteMessagePayload>> WaitForDeletedMessagesAsync(
        int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var deleted = await DeletedMessagesAsync();

            if (deleted.Count >= count)
            {
                return deleted;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Expected at least {count} delete(s) within {timeout.TotalSeconds:0.#}s; "
            + $"got {(await DeletedMessagesAsync()).Count}.");
    }
```

- [ ] **Step 6: Add the two new tests**

In `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`, add both methods
immediately before the class's closing brace (after
`Listener_ModelsToolCallCannotBeCarriedOut_RepliesWithNoButton`):

```csharp

    /// <summary>
    /// When the owner sends a message
    /// And the model calls create_task successfully
    /// Then the owner's own message is deleted.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerSendsAMessageThatCaptures_DeletesTheOwnersMessage()
    {
        // Arrange
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank tomorrow at 10"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var deleted = await wireMock.WaitForDeletedMessagesAsync(1, ReplyDeadline);
        var single = Assert.Single(deleted);
        Assert.Equal(OwnerChatId, single.ChatId);
        Assert.Equal(10, single.MessageId);
    }

    /// <summary>
    /// When the owner sends a message whose capture is refused
    /// And a second message from the owner is processed after it
    /// Then neither message is deleted.
    /// </summary>
    /// <remarks>
    /// The second message is a synchronisation device, not a second assertion -- the same
    /// technique <see cref="Listener_StrangerSendsAMessage_OnlyTheOwnerIsAnswered"/>'s own
    /// remarks use. Proving that nothing was deleted any other way means waiting on a clock and
    /// hoping; putting a second message after the first in the same batch means that by the time
    /// its own reply arrives, the first message's entire handling -- including whatever it would
    /// have deleted -- has already run to completion.
    /// </remarks>
    [Fact]
    public async Task Listener_ARefusedCaptureIsFollowedByAnotherMessage_NeitherMessageIsDeleted()
    {
        // Arrange
        await wireMock.SeedAiToolCallAsync("create_task", """{"due_at_local":"2026-08-26T10:00:00"}""");
        await wireMock.SeedUpdatesAsync(
            new InboundUpdate(10, OwnerChatId, "call the bank"),
            new InboundUpdate(11, OwnerChatId, "call the bank again"));

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await wireMock.WaitForSentMessagesAsync(2, ReplyDeadline);

        // Assert
        Assert.Empty(await wireMock.DeletedMessagesAsync());
    }
```

- [ ] **Step 7: Build and run the new tests — watch the positive one fail**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --no-build --filter "FullyQualifiedName~Listener_OwnerSendsAMessageThatCaptures_DeletesTheOwnersMessage|FullyQualifiedName~Listener_ARefusedCaptureIsFollowedByAnotherMessage_NeitherMessageIsDeleted"
```

Expected: `Listener_OwnerSendsAMessageThatCaptures_DeletesTheOwnersMessage` fails with a
`TimeoutException` from `WaitForDeletedMessagesAsync` — `MessageHandler` never calls
`bot.DeleteMessage` yet. `Listener_ARefusedCaptureIsFollowedByAnotherMessage_NeitherMessageIsDeleted`
passes already, for a reason worth being honest about rather than glossing over: a test that
proves an absence is trivially true before the code that could produce the presence exists. It
only becomes a meaningful regression guard once Step 8 gives `MessageHandler` something it could
wrongly do.

- [ ] **Step 8: Give `MessageHandler` the bot client and the delete step**

Replace `src/Assistant.Impl/Telegram/MessageHandler.cs` in full:

```csharp
using System.Globalization;
using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Sends the owner's plain-text message to the chat model, carries out the tool call it names,
/// replies with what was actually stored, and deletes the owner's own message once that reply
/// has been sent.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="bot">
/// The Telegram client, used only to delete the owner's own message after a successful capture.
/// Deletion is a Telegram affordance, not a channel-neutral one <see cref="INotifier"/> could
/// offer every future channel, so this handler reaches the client directly -- the same
/// precedent <see cref="CallbackRouter"/> already set for a Telegram update handler holding the
/// bot client.
/// </param>
/// <param name="notifier">Where the reply is delivered.</param>
/// <param name="ai">Reaches the configured chat model for an answer.</param>
/// <param name="tools">Every registered tool, matched against the model's tool call by name.</param>
/// <param name="clock">Renders a stored due instant back in the configured local zone.</param>
/// <param name="logger">
/// Where a tool call naming an unregistered tool is recorded, and where a failed best-effort
/// delete of the owner's own message is recorded.
/// </param>
/// <remarks>
/// The owner check lives inline here, on purpose: the assistant serves exactly one person, so
/// there is nothing to route between. Any future handler must apply the same check itself --
/// nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/> enforces it.
/// This handler is registered scoped and resolved fresh, inside a scope
/// <see cref="TelegramListener.DispatchAsync"/> opens per update, so every dependency here is
/// injected directly -- there is no captive-dependency concern the way there was when this
/// handler was a singleton.
/// <para>
/// Tool dispatch is a plain lookup against <paramref name="tools"/>, the same shape
/// <c>CallbackRouter</c> already uses to match an inbound key against
/// <c>IEnumerable&lt;ITaskAction&gt;</c>: an inbound name matched against a registered
/// collection, extended by adding a class and a registration, never by editing this method.
/// </para>
/// <para>
/// A successful capture also deletes the owner's own message, once the reply carrying it has
/// been sent -- never before, so a delete failure can never cost the owner the confirmation
/// that their task was saved. Any failure reply leaves the message in place, so the owner can
/// see what they typed and fix it rather than being left holding an unreadable failure with no
/// record of what they sent. The delete itself is best-effort: it can fail (the owner may have
/// deleted the message themselves in the meantime, or the request may simply fail), and by the
/// time it runs the task is already saved and the reply already sent, so a message that failed
/// to vanish is not worth an alarming sentence -- a failure is logged at warning and never
/// surfaced to the owner.
/// </para>
/// </remarks>
internal sealed class MessageHandler(
    TelegramSettings settings,
    ITelegramBotClient bot,
    INotifier notifier,
    IAiClient ai,
    IEnumerable<IAssistantTool> tools,
    ILocalTimeResolver clock,
    ILogger<MessageHandler> logger)
    : ITelegramUpdateHandler
{
    private const string DueTimeFormat = "dddd d MMMM yyyy, HH:mm";

    private const string Unreachable =
        "I could not reach the model just now. Send that again in a moment.";

    private const string NotUnderstoodAsATask =
        "I did not read that as a task. Try rephrasing it.";

    private const string DueTimeInPastReply =
        "That time has already passed. What time did you mean?";

    private const string DueTimeTooFarAheadReply =
        "That is more than two years away, which is probably not what you meant. "
        + "What time did you mean?";

    private const string DueTimeUnparseableReply =
        "I could not make sense of that time. What time did you mean?";

    private const string TitleMissingReply =
        "I did not catch what to call that. What should I call it?";

    private const string SomethingWentWrongReply =
        "Something went wrong on my end. Send that again in a moment.";

    /// <inheritdoc/>
    public UpdateType Handles => UpdateType.Message;

    /// <inheritdoc/>
    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (update.Message is not { Chat.Id: var chatId, Id: var messageId, Text: { } text } ||
            chatId != settings.OwnerChatId)
        {
            return;
        }

        var answer = await ai.AskAsync(text, ct);

        if (!answer.IsSuccess)
        {
            var failure = answer switch
            {
                { Error: ErrorCode.ModelReturnedNoToolCall } => NotUnderstoodAsATask,
                _ => Unreachable,
            };

            await notifier.SendAsync(failure, ct);
            return;
        }

        var toolCall = answer.Value!;
        var tool = tools.FirstOrDefault(t => t.Name == toolCall.Name);

        Result<ReminderTask> outcome;

        if (tool is null)
        {
            logger.LogWarning("The chat model called an unregistered tool {Tool}.", toolCall.Name);
            outcome = Result<ReminderTask>.Failure(ErrorCode.ModelNamedUnknownTool);
        }
        else
        {
            outcome = await tool.ExecuteAsync(toolCall.ArgumentsJson, ct);
        }

        if (!outcome.IsSuccess)
        {
            var failure = outcome switch
            {
                { Error: ErrorCode.DueTimeInPast } => DueTimeInPastReply,
                { Error: ErrorCode.DueTimeTooFarAhead } => DueTimeTooFarAheadReply,
                { Error: ErrorCode.DueTimeUnparseable } => DueTimeUnparseableReply,
                { Error: ErrorCode.ToolArgumentMissing } => TitleMissingReply,
                _ => SomethingWentWrongReply,
            };

            await notifier.SendAsync(failure, ct);
            return;
        }

        var task = outcome.Value!;
        var reply = task.DueAt is { } dueAt
            ? $"{task.Title} -- due {clock.ToLocal(dueAt).ToString(DueTimeFormat, CultureInfo.InvariantCulture)}."
            : $"{task.Title} -- saved with no reminder.";

        await notifier.SendTaskAsync(task.Id, reply, ct);

        try
        {
            await bot.DeleteMessage(chatId, messageId, ct);
        }
        catch (RequestException)
        {
            logger.LogWarning("Could not delete the owner's message {MessageId}.", messageId);
        }
    }
}
```

- [ ] **Step 9: Build and confirm zero warnings**

```bash
dotnet build --no-restore
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` No DI change is needed for this to
compile — `ITelegramBotClient` is already a registered singleton (see "Verified facts").

- [ ] **Step 10: Re-run the two new tests — both now pass**

```bash
dotnet test tests/Assistant.IntegrationTests --no-build --filter "FullyQualifiedName~Listener_OwnerSendsAMessageThatCaptures_DeletesTheOwnersMessage|FullyQualifiedName~Listener_ARefusedCaptureIsFollowedByAnotherMessage_NeitherMessageIsDeleted"
```

Expected: **2 passed**, 0 failed.

- [ ] **Step 11: Run the whole suite**

```bash
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
docker compose -f compose.test.yaml down
```

Expected: **57 passed** unit (0 failed). **70 passed** integration (0 failed).

- [ ] **Step 12: Update spec §5.1**

In `docs/design/slice-1-reminders.md`, in the flow diagram, change:

```
  → IAssistantTool → ITaskService → repository → Postgres
  → reply rendered with inline keyboard
```

to:

```
  → IAssistantTool → ITaskService → repository → Postgres
  → reply rendered with inline keyboard
  → on a successful capture, delete the owner's own message
```

and, after the section's existing two `**Deferred:**` paragraphs, add:

```
**Added at F10-4:** the final step was never part of this flow as originally designed. Once the
capture path actually worked end to end, the owner asked for the chat to stop reading as a
transcript of everything they typed and become a list of open tasks instead: after a successful
capture, the bot deletes the owner's own message, leaving only its own reply. The delete runs
only after the reply has been sent, and only on success — any failure reply (§5.4's guard
clauses, a malformed tool call, an unrecognised tool name) leaves the owner's message in place,
so they can see what they typed and fix it. The delete itself is best-effort and never surfaced
to the owner on failure, since by the time it runs the task is already saved and the reply
already sent. Storing the capture message's own id on `reminder_tasks`, so the reminder that
later fires can delete it too, is F10-5's.
```

- [ ] **Step 13: Correct the backlog's F10 entry**

In `docs/design/2026-08-22-slice-1-feature-backlog.md`, replace the F10 entry in full:

```
**F10 · Store the parsed task and reply · observable** — spec §5.1 · **done**
`ITaskService.CreateAsync`, the mapping extension methods, and the reply rendered with its
inline keyboard. `ReminderTask` does not regain `Notes` — despite this entry's own original
claim here and the §4 table's matching row (both corrected at F10-3), no slice of F10 ever wrote
a test that exercises it.
*Tests:* "call the bank tomorrow at 10" ends as a row with the right UTC instant and a reply
carrying the right buttons.
**Milestone: the full loop.** Talk to it, get reminded, tap Done.
*Settled at F10-3, applied at F10-4:*
- **Split across three pull requests**, not one: F10-1 (the writer), F10-2 (the tool executes),
  F10-3 (the reply closes the loop, commit 724b600). F10 stayed open, unmet by the `observable`
  tag, until F10-3 landed and the owner verified the full loop on a real phone — the same
  posture F6 held across its own three slices. Marking this entry done, and writing the two
  bullets below, was specified in full by F10-3's own plan and deferred to whoever implemented
  it and obtained that verification (F10-3 Decision 9) — neither happened until F10-4.
- **F10 is closed.** All three pull requests have landed, and the `observable` tag is met:
  talking to the bot ends in a stored row and a reply carrying the Done button, verified by the
  owner on a real phone; see `docs/e2e-local.md`.
*Settled at F10-4:*
- **The owner's own message is deleted once a capture succeeds**, so the chat reads as a list of
  open tasks rather than a transcript of everything the owner typed — the owner's own stated
  goal, not something spec §5.1 originally asked for (corrected there in the same commit). The
  delete runs only after the reply has already been sent, and only on a successful capture; any
  failure reply leaves the owner's message in place, so they can see what they typed and fix it.
  `MessageHandler` now takes `ITelegramBotClient` directly to do this — the precedent
  `CallbackRouter` already set for a Telegram update handler holding the bot client — rather
  than adding a delete method to `INotifier`, since deletion is a Telegram affordance a future
  channel may not have. The delete is best-effort: a failure is logged at warning, by message id
  alone, and never surfaced to the owner, since the task is already saved and the reply already
  sent by the time it runs. Storing the capture message's own id on `reminder_tasks`, so a fired
  reminder can delete it too, is F10-5's.
```

- [ ] **Step 14: Commit**

```bash
git add tests/Assistant.WireMock/TelegramStubs.cs \
        tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs \
        tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs \
        src/Assistant.Impl/Telegram/MessageHandler.cs \
        docs/design/slice-1-reminders.md \
        docs/design/2026-08-22-slice-1-feature-backlog.md
git commit
```

Message:

```
feat: F10-4 -- delete the echo

Once a capture succeeds, MessageHandler now deletes the owner's own
message, leaving only its own reply. A refused capture leaves the
message in place, so the owner can see what they typed and fix it. The
delete runs only after the reply has already been sent, so a delete
failure can never cost the confirmation that a task was saved.

MessageHandler gains ITelegramBotClient as a seventh primary-constructor
parameter and calls DeleteMessage(chatId, messageId, ct) directly --
the same precedent CallbackRouter already set for a Telegram update
handler holding the bot client, rather than a new INotifier method.
Deletion is a Telegram affordance a future channel may not have, so it
does not belong on the channel-neutral interface. The call is
best-effort: catching Telegram.Bot.Exceptions.RequestException, the
SDK's own base exception for both a request-level and an API-level
failure, logging the message id alone at warning, and never surfacing
anything to the owner.

TelegramStubs.cs gains a deleteMessage mapping, and WireMockFixture
gains DeletedMessagesAsync/WaitForDeletedMessagesAsync/
DeleteMessagePayload, mirroring the existing sendMessage helpers.
TelegramListenerTests gains two cases: a successful capture deletes
the owner's message, and a refused capture -- proven by seeding a
second message after the first and waiting for its own reply, the same
synchronisation technique OnlyTheOwnerIsAnswered already uses -- does
not.

Spec 5.1 gains the new flow step; the backlog's F10 entry is finally
marked done, applying the closure F10-3's own plan specified but could
not itself perform, and gains this slice's own settled bullets.

Tests: 57 unit, 70 integration, up from 57 / 68. Build clean, zero
warnings.

Plan: docs/plans/2026-09-06-f10-4-delete-the-echo.md

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

---

## Self-review

**This commit:**
- [ ] `MessageHandler`'s primary constructor takes exactly `(TelegramSettings settings,
      ITelegramBotClient bot, INotifier notifier, IAiClient ai, IEnumerable<IAssistantTool>
      tools, ILocalTimeResolver clock, ILogger<MessageHandler> logger)`, `bot` already resolvable
      in production with no new DI registration (Decision 2, "Verified facts")
- [ ] No method is added to `INotifier`; `TelegramNotifier.cs` is untouched (Decision 2)
- [ ] `bot.DeleteMessage(chatId, messageId, ct)` is called only after
      `notifier.SendTaskAsync` has been awaited, never before, and only on the success path
      (Decision 1)
- [ ] The `catch` clause names `Telegram.Bot.Exceptions.RequestException`, not
      `ApiRequestException` and not a bare `Exception`, and carries no `when` guard (Decision 4)
- [ ] The log line is `"Could not delete the owner's message {MessageId}."` with `messageId` as
      its only argument — no exception object, no arguments JSON (Decision 4)
- [ ] No `ErrorCode` member is added; no reply string changes; `Result<ReminderTask>`'s shape is
      untouched
- [ ] `CallbackRouter.cs`, `DoneAction.cs`, `ITaskAction.cs`, `CallbackCodec.cs`,
      `TaskActions.cs`, and `ImplServiceCollectionExtensions.cs` are unchanged (Decision 3,
      "What this slice does NOT include")
- [ ] `tests/Assistant.WireMock/TelegramStubs.cs` has exactly one new mapping,
      `/bot*/deleteMessage`, answering `{"ok":true,"result":true}`
- [ ] `WireMockFixture.cs` gains `DeletedMessagesAsync`, `WaitForDeletedMessagesAsync`, and
      `DeleteMessagePayload`, each mirroring an existing sibling's shape exactly
- [ ] Exactly two new `[Fact]`s in `TelegramListenerTests.cs`; no existing test method is
      changed
- [ ] The negative test seeds two updates in one `SeedUpdatesAsync` batch and synchronises on
      the second message's own reply, not a `Task.Delay` (Decision 8)
- [ ] Every new or changed public member carries a three-line-tag `<summary>` plus every
      `<param>`/`<returns>` `CS1591`/`CS1573` requires
- [ ] Test summaries are Gherkin (`When`/`And`/`Then`), one clause per line, in both new tests
- [ ] No emoji anywhere, including the commit message
- [ ] **No plan-internal decision citation (`Decision 1`, `(Decision 2)`, or similar) inside any
      C# code block, doc comment, or commit message** — every fenced code block above was
      re-read for this before the plan was committed
- [ ] Plain ASCII `--` used inside every C# doc comment, C# comment, and the commit message
      body; every markdown-prose block destined for `slice-1-reminders.md` or the backlog uses
      real em dashes, matching those documents' own established convention (verified by
      inspecting the byte content of F10-3's own already-applied backlog correction, which uses
      an em dash even though the plan that specified it showed the text with `--`)
- [ ] `docker compose -f compose.test.yaml up -d --build` (with `--build`) is the command given
      for local integration runs, not a bare `up -d`

**Whole feature (F10), once this lands:**
- [ ] The backlog's F10 entry is marked done and gains both a `*Settled at F10-3, applied at
      F10-4:*` block and a `*Settled at F10-4:*` block (Decision 9)
- [ ] Spec §5.1's flow diagram now ends with the delete step, and the section records why it was
      added (Decision 9)
- [ ] This slice's diff measures 209 lines by the convention argued in "How this slice fits";
      the running total across F10's four pull requests (120 + 383 + 295 + 209 = 1007 lines) is
      not itself a budget — each PR is independently under the 1000-line budget regardless of
      the sum, the same accounting F10-3 used for its own running total.
