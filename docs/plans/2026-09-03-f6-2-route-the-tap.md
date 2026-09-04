# F6-2 — the tap is routed and answered

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F6-1 gave `ReminderTask` a `CompletedAt` and `ITaskService.CompleteAsync`, but nothing in
the running system can reach it yet — there is no Telegram code at all on this branch. This slice
builds the machinery a tapped button needs before it can exist: `ITaskAction` resolved by key,
`DoneAction` as its first implementation, `CallbackRouter` as a second `ITelegramUpdateHandler`
for `UpdateType.CallbackQuery`, the `v1:<action>:<base64-id>` callback codec, an inline keyboard
tap always answered, the tapped message edited in place, and — the same pull request
`docs/tech-debt.md` already names for this — the per-update scope moved out of each handler and
into `TelegramListener.DispatchAsync`. No button exists after this slice merges. None is supposed
to: F6-3 attaches the first one.

**Tech Stack:** .NET 10 (`net10.0`), nullable enabled, warnings are errors — the existing stack.
This slice adds **no new NuGet package**. It adds one build-config line: a second
`InternalsVisibleTo` entry on `Assistant.Impl.csproj`, granting `Assistant.IntegrationTests`
access to the internal callback codec its own tests need to construct valid callback data.

**Spec:** `docs/design/slice-1-reminders.md` §5.1 (the reply-rendering step this slice does not
touch), §6.4 (inline buttons — callback format, the effects table, and the three required
behaviours this slice's `CallbackRouter` implements), §6.5 (failure handling — Telegram 429/400,
unhandled exceptions caught and logged), §7.2 (unit vs. integration split), §7.3 (assertion
standard — count, recipient, exact text), §7.4 (required scenarios — "Done tapped twice," and the
non-whitelisted-sender scenario, extended here to a non-owner tap), §12.1 (XML docs), §12.5
(primary constructors), §12.6 (no emoji).
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F6's own entry (line 274, under
"Reminder path — no AI, no credentials") and section 1 (YAGNI, Open/Closed, the 1000-line budget).
**Also read:** `docs/tech-debt.md`'s "Each handler opens its own scope, rather than the dispatcher
opening one" entry (lines 70-134) — this slice is the pull request it names as the right place to
pay itself off, and this plan's Commit 1 does exactly that, then updates the entry to say so.

---

## How F6 is sliced

F6-1 merged as commit `bc0c9cc` on `main` — `ReminderTask.CompletedAt`, its migration, the
`ck_reminder_tasks_completed_consistency` check constraint, and `ITaskService.CompleteAsync` with
its idempotency. Verified directly: `git log --oneline -1 bc0c9cc` reads
`feat: F6-1 - the completed column and the writer (#23)`, and `TaskService.cs` (71 lines) already
carries `CompleteAsync` exactly as F6-1's own plan specified it.

- **F6-1, the column and the writer (done, merged).** No Telegram, no buttons, no callback
  handling.
- **F6-2 (this plan), the tap is routed and answered** — `ITaskAction` and `DoneAction`,
  `CallbackRouter` as a second `ITelegramUpdateHandler` for `UpdateType.CallbackQuery`, the
  `v1:<action>:<base64-id>` callback codec, always answering the callback query, editing the
  original message in place, and moving the per-update scope from `MessageHandler` into
  `TelegramListener.DispatchAsync`, exactly as `docs/tech-debt.md`'s "Each handler opens its own
  scope" entry says this pull request should.
- **F6-3, the button appears** — a channel-neutral button contract, the `INotifier` surface that
  carries it, `TelegramNotifier` sending `reply_markup`, and `DueReminderJob` attaching a Done
  button to the reminder.

**Nothing in F6-2 renders a button.** F6-3 is the first commit anywhere in this repository's
history that puts an inline keyboard on a Telegram message. That is deliberate, and it is F6-1's
own Decision 1 restated, not a new argument: spec §6.4 requires a callback query always be
answered "or Telegram shows a spinner indefinitely," so the machinery that answers ships before
the affordance that can be tapped. A consequence worth stating plainly, the same way F6-1's own
plan stated its own: this slice does not mark the backlog's F6 entry done. F6 carries the
backlog's `observable` tag, and F6-2 alone produces nothing a phone can show — there is still no
button anywhere. The entry stays open until F6-3's own plan closes it.

---

## Global Constraints

Every constraint this project's prior plans carry forward still applies here:

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error everywhere.
- **Every class taking arguments uses a primary constructor.** No separate constructors.
- Every enum's first member is `Unknown`, with no explicit numeric values. This slice adds no new
  enum member — `ErrorCode.TaskAlreadyCompleted` (F6-1) already carries every reason `DoneAction`
  needs to report.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions. `Assert.Equivalent(expected, actual, strict: true)` for wire payloads, matching
  `TelegramNotifierTests`' own existing use.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- Central package management; no inline `Version=`. Not exercised this slice — no package changes.
- No emoji anywhere: source, tests, docs, or commit messages, or bot message text (conventions
  §12.6). Every reply string this slice adds — `"That button is no longer valid."`,
  `"Already done."`, `"I could not find that task."` — is plain words, no decoration.
- **Never run `docker compose down -v`.** Use `docker compose -f compose.test.yaml down` with no
  flags.
- **Integration tests need `docker compose -f compose.test.yaml up -d --build`, not plain
  `up -d`, from Commit 3 onward.** Commit 3 changes `tests/Assistant.WireMock/TelegramStubs.cs`,
  and `compose.test.yaml`'s `wiremock` service builds that project from a Dockerfile
  (`compose.test.yaml:16-19`) rather than pulling a published image — a container already running
  from a stale image will not see the new mappings without a rebuild. Commits 1 and 2 do not touch
  `Assistant.WireMock`, so plain `up -d` still suffices for those two.
- PR budget: 1000 changed lines per PR, excluding the plan. Decision 8 counts this slice at
  roughly 900 lines and does not propose a split, though it comes closer to the ceiling than any
  prior slice in this repository and says so.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
  ```

---

## Verified facts this plan rests on

Every one of these was read from the working tree at `bc0c9cc`, HEAD of this branch, directly —
`cat -n`, `grep -n`, `wc -l`, or (for the two facts marked below) a real request captured off a
real WireMock container — not recollection.

- **`TaskService.CompleteAsync` already exists and is exactly the shape F6-1's own plan specified**
  (`src/Assistant.Impl/Services/TaskService.cs:48-70`, 71 lines total): find, guard `TaskNotFound`,
  guard `TaskAlreadyCompleted`, stamp `Status`/`CompletedAt`/`UpdatedAt`, save, return
  `Result.Success()`. `ITaskService.CompleteAsync` (`src/Assistant.Interfaces/ITaskService.cs:39-54`)
  returns `Task<Result>`, no payload — `DoneAction` gets no task title back from it.
- **`ITelegramUpdateHandler` (33 lines) has exactly two members**: `UpdateType Handles { get; }`
  and `Task HandleAsync(Update update, CancellationToken ct)`. Its own remarks
  (`ITelegramUpdateHandler.cs:9-14`) explain it lives in `Assistant.Impl.Telegram`, not
  `Assistant.Interfaces`, because it names `Telegram.Bot.Types.Update`, and
  `DependencyRuleTests.Interfaces_do_not_depend_on_infrastructure_libraries` fails the build if
  `Assistant.Interfaces` references `Telegram.Bot`. `ITaskAction`, this slice's new interface,
  names nothing from `Telegram.Bot` — only `Guid`, `CancellationToken`, and `Result` — so this
  reasoning does not apply to it, and it goes in `Assistant.Interfaces` like every other interface.
- **`MessageHandler` (67 lines) is a singleton today**
  (`ImplServiceCollectionExtensions.cs:79`, `services.AddSingleton<ITelegramUpdateHandler,
  MessageHandler>();`), and opens its own scope to resolve `IAiClient` inside `HandleAsync`
  (`MessageHandler.cs:54-55`) because `IAiClient` is scoped
  (`ImplServiceCollectionExtensions.cs:140`) and a singleton cannot constructor-inject a scoped
  service. Its own remarks (`MessageHandler.cs:24-28`) say the owner check "lives inline here, on
  purpose... Any future handler must apply the same check itself — nothing in
  `ITelegramUpdateHandler` or `TelegramListener` enforces it." The check compares
  `update.Message.Chat.Id` against `settings.OwnerChatId`, not `Message.From.Id` — the backlog's
  own F7 "Settled at F7" note explains why (line ~ backlog `2026-08-22-slice-1-feature-backlog.md`
  under F7, "The whitelist compares `Message.Chat.Id`, not `Message.From.Id`").
- **`TelegramListener` (105 lines) computes `_allowedUpdates` as a field initializer over an
  injected `IEnumerable<ITelegramUpdateHandler>`** (`TelegramListener.cs:39`, exactly the line
  `docs/tech-debt.md:99` cites), and `DispatchAsync` (`TelegramListener.cs:90-104`) iterates the
  same injected `handlers` field directly, filtering `.Where(h => h.Handles == update.Type)`, each
  call wrapped in its own try/catch.
- **`docs/tech-debt.md`'s second entry (lines 70-134) already prescribes this slice's Commit 1
  verbatim**, code sketch included (lines 108-114): `TelegramListener.DispatchAsync` opens one
  scope per update and resolves handlers from it, and handlers go back to plain constructor
  injection. Its own "Scope when it is picked up" (lines 129-134) names this exact pull request as
  the right place, and states the entry's own name for the trigger — "F6 ... introduces
  `ICallbackHandler` and `CallbackRouter`" — a claim Decision 1 below finds is only half right.
- **The backlog's F6 entry (`2026-08-22-slice-1-feature-backlog.md:274-282`) names
  `ICallbackHandler` + `CallbackRouter` as a pair**, and separately states "the handler must apply
  the owner check itself, because there is no base class doing it" (same entry, line ~279-280).
  The backlog's own governing rule, section 1: "An abstraction with one implementation is a guess,
  not a seam; interfaces appear when a second implementation or a real extension point does." F7's
  own entry (`2026-08-22-slice-1-feature-backlog.md`, "Settled at F7") records this rule being
  applied twice in the same feature: `ITelegramUpdateHandler` kept as one class first, split out
  once a real second implementation (`CallbackRouter` — this slice) was about to exist, and a base
  class (`OwnerOnlyUpdateHandler`) built and then dropped for having exactly one subclass.
- **`INotifier` (23 lines) declares exactly one method**, `Task SendAsync(string text,
  CancellationToken ct)`, whose own remarks (`INotifier.cs:6-10`) state "the recipient is
  configuration, not a parameter... Rendering is the caller's job — a notifier delivers text it is
  given and never sees a database shape." `TelegramNotifier` (36 lines) is its one implementation;
  its own remarks (`TelegramNotifier.cs:13-24`) state escaping "happens here, not at call sites,
  because this is the only type that knows it is sending HTML," and that a text-versus-markup
  distinction is deferred to F10 "with a test that demands it" — this slice is not F10, and adds
  no such distinction, but Decision 2 below examines that same stated principle directly against
  the method this slice adds, rather than assuming it still applies.
- **`Telegram.Bot` 22.10.2.1 (`Directory.Packages.props:29`) exposes `AnswerCallbackQuery` and
  two `EditMessageText` overloads as extension methods on `ITelegramBotClient`.** Verified by
  loading the installed package assembly
  (`~/.nuget/packages/telegram.bot/22.10.2.1/lib/net6.0/Telegram.Bot.dll`) with reflection:

  ```
  AnswerCallbackQuery(ITelegramBotClient, string callbackQueryId, string? text = null,
      bool showAlert = false, string? url = null, int? cacheTime = null,
      CancellationToken cancellationToken = null)
  EditMessageText(ITelegramBotClient, ChatId chatId, int messageId, string text,
      ParseMode parseMode = None, InlineKeyboardMarkup? replyMarkup = null, ...) -> Task<Message>
  ```

  `CallbackQuery` (same assembly) carries `Id` (string), `From` (User), `Message` (Message),
  `Data` (string), `ChatInstance` (string). `Message` carries both `Id` and `MessageId` (both
  `int`, neither marked `[Obsolete]`); this plan uses `.Id`, matching `Update.Id`'s already-used
  spelling at `TelegramListener.cs:65`.
- **What actually lands on the wire for these two calls, captured off a real WireMock container,
  not inferred from the SDK's request-class attributes** (those attributes turned out to be
  unreliable: `AnswerCallbackQueryRequest.CallbackQueryId` and `EditMessageTextRequest.MessageId`
  both carry `[JsonIgnore]` in the SDK's own source, which does not mean they are absent from the
  wire — the SDK serialises `RequestBase<T>` through its own mechanism). A throwaway
  `TelegramBotClient` pointed at a locally running `compose.test.yaml` stub, calling
  `AnswerCallbackQuery("cb-1", cancellationToken: default)` then
  `EditMessageText(555L, 42, "call the bank", ParseMode.Html, cancellationToken: default)`,
  produced these exact captured request bodies (`GET /__admin/requests` against the stub):

  ```json
  {"callback_query_id":"cb-1"}
  {"chat_id":555,"message_id":42,"text":"call the bank","parse_mode":"Html"}
  ```

  Calling `AnswerCallbackQuery("cb-2", text: "Already done.", ...)` produced
  `{"callback_query_id":"cb-2","text":"Already done."}`. Three facts this proves directly: unset
  optional parameters (`showAlert`, `url`, `cacheTime`) are omitted from the wire entirely, not
  sent as `false`/`null`; **omitting `replyMarkup` sends no `reply_markup` field at all** — this is
  what Decision 3 below rests on; and the field set/order/names match this plan's
  `AnswerCallbackQueryPayload`/`EditMessageTextPayload` test records exactly.
- **The callback data budget.** `Convert.ToBase64String(Guid.Empty.ToByteArray())` is
  `"AAAAAAAAAAAAAAAAAAAAAA=="`, 24 characters (computed directly, not estimated: a 16-byte input
  produces 24 base64 characters including two `=` padding characters). `"v1:done:"` is 8
  characters, so `v1:done:<base64-id>` is 32 characters — matching spec §6.4's own "roughly 33
  bytes against Telegram's 64-byte limit" closely enough to confirm this is the encoding the spec
  had in mind (a GUID's raw 16 bytes through standard, padded Base64), not a hex or base64url
  variant.
- **`Assistant.Impl.csproj` (31 lines) grants `InternalsVisibleTo` to exactly one project today**
  (`Assistant.Impl.csproj:22`, `<InternalsVisibleTo Include="Assistant.UnitTests" />`).
  `Assistant.IntegrationTests` is not granted access to `Assistant.Impl` internals anywhere in the
  repository (confirmed by reading the file in full) — needed because this slice's integration
  tests construct callback data with the internal `CallbackCodec.Encode`, the same reasoning
  Decision 4 gives for keeping that method at all.
- **`WireMockFixture` (434 lines) seeds only `message`-shaped updates.** `SeedUpdatesAsync`
  (`WireMockFixture.cs:134-158`) builds `{"update_id":..., "message": {...}}` bodies; there is no
  method that builds a `callback_query`-shaped update anywhere in the file. `SentMessagesAsync`
  (`WireMockFixture.cs:94-104`) filters strictly on `/sendMessage`; there is no equivalent for
  `/answerCallbackQuery` or `/editMessageText`. `SendMessagePayload`
  (`WireMockFixture.cs:338-353`) carries `[JsonExtensionData] Extra`, and `TelegramNotifierTests`
  uses `Assert.Equivalent(expected, actual, strict: true)` against it — confirmed directly
  (`TelegramNotifierTests.cs:60`) — so an unnamed field on a captured request fails the assertion
  rather than being silently dropped. This slice's new payload records follow the identical shape.
- **`tests/Assistant.WireMock/TelegramStubs.cs` (53 lines) answers exactly two paths**:
  `/bot*/sendMessage` and `/bot*/getUpdates`. Neither `/bot*/answerCallbackQuery` nor
  `/bot*/editMessageText` has a mapping. Without one, a call to either method from inside the real
  listener gets WireMock's own 404 `"No matching mapping found"` body, which
  `Telegram.Bot`'s response deserialiser rejects with `ApiRequestException` — confirmed directly:
  this is the exact failure the throwaway probe above hit before a mapping existed.
- **Baseline test counts, run directly against this branch's HEAD, not assumed**: `dotnet test
  tests/Assistant.UnitTests` reports **41 passed**; `dotnet test tests/Assistant.IntegrationTests`
  (against `docker compose -f compose.test.yaml up -d`, no `--build` needed at baseline) reports
  **39 passed**. Matches the brief's stated figures and F6-1's own plan's stated ending counts.
- **`ConventionTests`/`DependencyRuleTests` (158 + 117 lines) do not need changes.** Grepped and
  read both files in full: no rule inspects `Assistant.Impl.Telegram` or
  `Assistant.Impl.Services.Actions` by name, no rule counts `ITelegramUpdateHandler`
  implementations, and `Only_TaskService_references_ITaskRepository_in_Impl`
  (`DependencyRuleTests.cs:84-95`) only flags a type whose constructor or fields carry
  `ITaskRepository` — `DoneAction` and `CallbackRouter` both stop at `ITaskService`, never
  `ITaskRepository`, so neither trips it. Spec §7.5's own namespace rule — "`Impl.Services.Jobs`
  or `Impl.Services.Actions` referencing repository interfaces — they go through `ITaskService`" —
  is where this slice's `Impl.Services.Actions` namespace choice for `DoneAction` comes from
  directly; that namespace is named in the spec before this slice ever creates a file in it.
- **`ReminderTaskBuilder.BuildReminderTask`'s default title is `"call the bank"`**
  (`ReminderTaskBuilder.cs:30`), reused without override by every new test in this plan, matching
  `DueReminderJobTests`' own existing convention of asserting against `task.Title` rather than a
  test-local literal.
- **`IntegrationCollection` (20 lines) already fixtures both `PostgresFixture` and
  `WireMockFixture` together**, and its own remarks say why: "a job test needs a database and the
  stub together." `DueReminderJobTests` (139 lines) is the existing template for a test class that
  needs both — this slice's `CallbackRouterTests` follows its exact constructor and lifecycle
  shape.

---

## Inherited context: what this slice reads from earlier features

`ITelegramUpdateHandler`, `MessageHandler`, `TelegramListener`, `TelegramNotifier` (F7),
`INotifier` (F4a), `ITaskService`/`TaskService`/`ErrorCode.TaskAlreadyCompleted` (F5a, F6-1),
`IAssistantTool`/`CreateTaskTool` and `IScheduledJob`/`ReminderScheduler` (F5b, F9a — the two
existing precedents for "resolved by key/kind from an injected `IEnumerable<T>`," which
`CallbackRouter`'s resolution of `ITaskAction` by `Key` and `TelegramListener`'s resolution of
handlers by `Handles` both follow), and `WireMockFixture`/`PostgresFixture`/`IntegrationCollection`
/`ReminderTaskBuilder` (F1-F7 test infrastructure) are all read in full at HEAD and none is
restructured beyond what Commit 1 explicitly does to `TelegramListener`/`MessageHandler` to pay off
the named tech debt.

---

## Decisions

### 1. `ICallbackHandler` does not get built

**Decision:** this slice introduces `CallbackRouter` as a second `ITelegramUpdateHandler`
directly. It does not introduce `ICallbackHandler`, even though both the backlog's F6 entry and
`docs/tech-debt.md`'s own trigger text name the pair together.

**Why:** `ICallbackHandler` would exist to be implemented by exactly one class,
`CallbackRouter` — there is no second thing in this design that routes a callback query, the way
there is no second thing that resolves a chat model's answer or runs a scheduled tick without
going through the one seam that already exists for that. The backlog's own governing rule, quoted
above, calls this shape "a guess, not a seam." This project has already run this exact experiment
inside F7 itself and reversed it: `ITelegramUpdateHandler` was tried as a single concrete class
first, split into an interface once a real second implementation was imminent, and a base class
(`OwnerOnlyUpdateHandler`) was built and dropped within the same feature for having exactly one
subclass. `ICallbackHandler` sitting *above* `ITelegramUpdateHandler` — which already exists,
already supports multiple handlers dispatched by `Handles`, and already is the seam a second
update kind plugs into — would be a second abstraction doing the first one's job a level higher,
not a new seam. The actual seam this design needs, and the one spec §6.4 itself describes, is
`ITaskAction` resolved by key — `DoneAction` is its first implementation, with three more
(`SnoozeAction`, `RescheduleAction`, `EditAction`) arriving at F11
(`2026-08-22-slice-1-feature-backlog.md:585-586`). That interface earns its keep the moment
`DoneAction` exists, on exactly the same YAGNI reading `IAssistantTool` and `IScheduledJob` already
justify with one implementation each today (`CreateTaskTool`, `ReminderScheduler`'s own jobs) —
because the design already commits to more implementations arriving, not because a hypothetical
second one might.

**What this makes of `docs/tech-debt.md`'s own text:** its "Scope when it is picked up" section
says "F6 ... introduces `ICallbackHandler` and `CallbackRouter`, a second `ITelegramUpdateHandler`
that will need a scope for the same reason `MessageHandler` does." The second half of that sentence
is exactly right and is what Commit 1 below implements; the first half named a type this plan does
not build. Commit 1 corrects the entry's own wording in the same commit that resolves it, per
`AGENTS.md`'s rule that a structural decision changes the document that stated it.

**Cost if this is wrong.** If a second, genuinely different way of routing a callback query
appears later — not a new *action* (which `ITaskAction` already extends without touching
`CallbackRouter`), but a second *router*, for example one that handles inline-query callbacks
rather than message-attached ones — extracting `ICallbackHandler` then costs exactly what
extracting `ITelegramUpdateHandler` cost at F7: renaming `CallbackRouter`'s single class into an
interface plus one implementation, updating one DI registration line, and nothing else, because
`CallbackRouter`'s own logic never assumed it was the only handler of its kind. That is a small,
mechanical refactor of code this pull request is already touching, not a structural rework.

**Alternative considered: build `ICallbackHandler` because the backlog says to.** Rejected. The
backlog's own section 1 outranks any single feature entry's prose when the two disagree — the
entry is a plan for how to build the spec, not the spec itself, and section 1 states the rule this
entry's own wording happens not to follow. Building an interface because an earlier document named
it, after that document's own rule argues against it, would be exactly the kind of guess this
project has already twice reversed the cost of writing.

### 2. `INotifier` grows `MarkCompletedTaskAsync`; the router does not talk to `ITelegramBotClient` for the edit

**Decision:** `INotifier` gains a second method, `Task MarkCompletedTaskAsync(int messageId,
string text, CancellationToken ct)`. `TelegramNotifier` implements it by wrapping `text` in
`<s>...</s>` and calling `bot.EditMessageText`, reusing its own existing private `Escape`.
`CallbackRouter` calls `INotifier.MarkCompletedTaskAsync`, not `ITelegramBotClient.EditMessageText`
directly, for the edit. It does call `ITelegramBotClient.AnswerCallbackQuery` directly — that half
stays outside `INotifier` entirely.

**Why `MarkCompletedTaskAsync`, not a name tied to Telegram's rendering.** `INotifier` is the
channel-neutral seam: it is what a future non-Telegram notifier implements, not only today's
Telegram one. Naming the method after strike-through — the one typographic effect this adapter
happens to use — ties a channel-neutral interface to a single channel's presentation: any channel
that cannot render strike-through would have to no-op it or fake it, with no way for the interface
to say "I show completion differently." `MarkCompletedTaskAsync` names the intent instead, leaving
each adapter free to choose its own rendering — Telegram strikes the title through; something else
could prefix `[done]`, or change color, without `INotifier` itself changing at all.

**The "rendering is the caller's job" objection does not survive contact with the code.**
`INotifier`'s own remarks about `SendAsync` say a notifier "delivers text it is given" and that
"rendering is the caller's job" — the line an earlier draft of this decision leaned on to justify a
presentation-flavoured name. But the method this slice adds already wraps `text` in `<s>...</s>`
inside `TelegramNotifier` before sending it — that *is* rendering, and it happens in the adapter,
not the caller. The principle was not protecting anything here; only the old name was, by matching
the interface method's name to the one rendering choice Telegram happens to make. Named for intent
instead, the method still fits the escaping/rendering split `INotifier`'s remarks describe, stated
accurately rather than stretched: the caller supplies plain text and an intent — mark this
complete — and the adapter, today only `TelegramNotifier`, owns both escaping it and choosing how
completion looks, the same way it already owns escaping for `SendAsync`.

**Simplicity at the call site.** `MarkCompletedTaskAsync` says what the call is for without
requiring the reader to know Telegram's markup vocabulary — `CallbackRouter` calls
`notifier.MarkCompletedTaskAsync(...)` when an action succeeds, and that call reads correctly
whether or not the reader has ever seen an `<s>` tag. A name that only parses once the reader
already knows Telegram's own rendering choice is not simpler for being shorter; it is simpler to
misread.

**Why the edit goes through `INotifier`:** `TelegramNotifier`'s own remarks already state the
reason before this slice ever touches the file — "Escaping happens here, not at call sites,
because this is the only type that knows it is sending HTML." An edited message's text needs the
exact same three-character HTML escaping `SendAsync` already performs, in the exact same order the
existing regression test (`TelegramNotifierTests.SendAsync_TextContainsAngleBracketsAndAmpersand_
EscapesAllThreeInOrder`) protects. If `CallbackRouter` composed and escaped HTML itself, that
invariant would live in two places, and the bug that test exists to catch — escaping `<`/`>`
before `&`, re-escaping the ampersand the first two replacements introduce — becomes reachable a
second, independent way. Growing `INotifier` keeps "the one place that knows it is sending HTML"
true in fact, not just in the comment.

**Why `AnswerCallbackQuery` does not move into `INotifier`.** `INotifier`'s own contract is
"delivers a message to the person the assistant works for." Answering a callback query delivers
nothing — it dismisses a loading spinner on a tapped button, a Telegram Bot API mechanic with no
channel-neutral analogue at all. Folding it into `INotifier` would make the interface's next
channel-neutral consumer (F6-3's button contract, or any future channel) implement a method that
means nothing outside Telegram's own callback protocol. `CallbackRouter` already lives in
`Assistant.Impl.Telegram` and already needs `ITelegramBotClient` for nothing else — it is the
right, and only, place for this call.

**How this interacts with F6-3, named directly rather than left implicit.** F6-3 grows `INotifier`
too — a channel-neutral button contract on `SendAsync` (or a new overload), so `DueReminderJob` can
attach a Done button. That is a *different* operation (send-with-buttons) added for a *different*
reason (a message needs buttons when it is first sent) than this slice's addition
(mark-an-existing-message-as-done). The two do not need to anticipate each other's shape: F6-3 does
not touch `MarkCompletedTaskAsync`, and this slice's `MarkCompletedTaskAsync` does not touch
`reply_markup` at all — see Decision 3.

**Alternative considered: name the method `NotifyCompletedTaskAsync` (or "send done task"),
reasoning that `INotifier`'s whole job is delivery.** Rejected, for a concrete reason rather than a
stylistic one: this method does not deliver anything. It calls `EditMessageText` against a
`messageId` that already exists — nothing new arrives on the owner's phone; an existing message
changes appearance in place. A `Notify*`/`Send*` name promises a new message, which never comes.
`Mark*` is correct because it names a change to something that already exists, the same distinction
`SendAsync` itself already draws by reserving `Send*` for the one method that actually sends
something new.

**Alternative considered: `CallbackRouter` calls `ITelegramBotClient.EditMessageText` directly,
duplicating or exposing `Escape`.** Rejected for the DRY reason above. A weaker variant — making
`TelegramNotifier.Escape` `internal static` so `CallbackRouter` can call it directly and build its
own `EditMessageText` call — was also considered and rejected: it still leaves the *decision* of
what wraps what (`<s>...</s>`, `ParseMode.Html`, which chat, whether `Escape` runs at all) outside
the one type whose whole job is knowing that. `INotifier` growing a named, narrow method
(`MarkCompletedTaskAsync`, not a generic `EditAsync` with formatting options) keeps that decision
inside `TelegramNotifier` and the interface change to exactly what this slice needs — nothing more
speculative than that.

### 3. There are no buttons to remove yet, and `MarkCompletedTaskAsync` says so

**Decision:** `TelegramNotifier.MarkCompletedTaskAsync` calls `bot.EditMessageText` with no
`replyMarkup` argument at all. It neither clears an existing keyboard nor preserves one — it sends
no instruction about the keyboard whatsoever, and the empirically verified fact above ("What
actually lands on the wire") confirms that is exactly what omitting the parameter does: no
`reply_markup` field appears on the wire, and Telegram leaves whatever keyboard the message already
has untouched.

**Why this is correct today and not a bug:** no code path in this repository, at this commit,
attaches a keyboard to any message. `DueReminderJob.cs:33` still calls
`notifier.SendAsync(task.Title, ct)` — plain text, no buttons — and F6-3 is the first feature that
changes that. A reminder message this slice's `CallbackRouter` could ever be asked to edit
therefore never has a keyboard to clear. Passing an explicit empty `InlineKeyboardMarkup` now, to
"future-proof" against F6-3, would be code with no test able to exercise its one observable effect
(clearing a keyboard that cannot exist yet) — exactly what the backlog's definition of done rules
out.

**What F6-3 must do about it, stated so it is not silently inherited as a bug.**
`MarkCompletedTaskAsync`'s own `<remarks>` say this directly: once F6-3 attaches the first
keyboard, this exact call site must pass an explicit empty (or updated) `InlineKeyboardMarkup`, or
a completed reminder keeps its dead Done button visible under the struck-through title — tappable,
routed correctly by `CallbackCodec`/`CallbackRouter` (which still work fine on an already-completed
task, answering "Already done."), but visually wrong. This is not silent: it is named in the
interface's own `<remarks>` and, in the Telegram-specific terms F6-3's implementer will actually
need, in `TelegramNotifier.MarkCompletedTaskAsync`'s own `<remarks>` too — whichever doc comment
that implementer opens first before touching this call site, the same way `TelegramNotifier`'s
class remarks already name F10 as the feature that must revisit its own text-versus-markup
assumption.

**Alternative considered: pass an explicit empty keyboard now, "for correctness."** Rejected per
the reasoning above — it is not more correct today, since there is nothing to clear; it is
speculative generality wearing a correctness argument, the same shape backlog §1 already rules out
for a property or table nothing yet exercises.

### 4. The codec: one type, `Encode` and `TryDecode`, no `arg` support yet

**Decision:** `CallbackCodec` is one `internal static class` in `Assistant.Impl.Telegram`, carrying
both `Encode(string action, Guid taskId) -> string` and `TryDecode(string data, out string action,
out Guid taskId) -> bool`. Neither method knows about the optional trailing `:<arg>` segment spec
§6.4's grammar describes (`v1:<action>:<base64-id>[:<arg>]`) — `TryDecode` accepts exactly three
colon-separated segments and rejects anything else, including four.

**One type, not two.** Encode and decode must agree on the exact same wire grammar by
construction — the version prefix, the segment order, the base64 alphabet, the 16-byte length
check. Splitting them into an `Encoder`/`Decoder` pair buys no independence (nothing in this
design needs to encode without also being able to decode, or vice versa) and creates exactly the
drift risk a shared type exists to prevent: two files that must agree on a format, able to change
independently.

**Is `Encode` YAGNI in this slice? Argued honestly, not assumed.** Nothing in `src/` calls
`CallbackCodec.Encode` — F6-3 is the first production caller, when `DueReminderJob` needs to put a
real Done button on a real message. But `Encode` has a real caller *in this slice's own test
suite*: `CallbackRouterTests` seeds every callback-query update it tests by calling
`CallbackCodec.Encode("done", task.Id)`, not by hand-writing a base64 string inline. That is not a
loophole around "no code that nothing exercises" — it is the same pattern this project's own
`ReminderTaskSchemaTests` already uses for a check constraint nothing in `src/` could trigger yet
(F5a's raw-SQL fixture, kept precisely because it is the *only* thing that exercises the rule). The
alternative — the test hand-rolling `Convert.ToBase64String(taskId.ToByteArray())` itself — would
mean two independent implementations of the same wire format existing simultaneously, one in
`src/` (unused until F6-3) and one duplicated inside test arrangement code, free to drift apart
silently. Writing `Encode` once and having the test use it is strictly safer than either building
nothing or building it twice.

**Is decode's `arg`-blindness a gap? Named, not papered over.** `CallbackCodec.TryDecode` today
returns `false` for `"v1:snooze:<base64-id>:1h"` — a well-formed string per spec §6.4's own
grammar, rejected purely because this build does not yet parse a fourth segment. This is
deliberate and mirrors this slice's own `ITaskAction.ExecuteAsync(Guid taskId, CancellationToken
ct)`, which likewise has nowhere to put an argument: `DoneAction` is the only implementation, and
it needs none. Building `arg`-aware parsing into the codec now, while leaving `ITaskAction`
argument-blind, would be an inconsistent half-measure — generality added in one place with no way
to reach it from the other. F11, which adds the first argument-taking action (`SnoozeAction`),
extends both together: `ITaskAction.ExecuteAsync` gains an `arg` parameter and `CallbackCodec`
gains the fourth segment, in the same commit, for the same reason a repository method grows when
the feature that needs it arrives. Until then, a stale `v1:` string carrying a trailing argument
(left over from a hypothetical future build, replayed against this one after a rollback) decodes
to `false` and produces the same polite "no longer valid" reply as any other malformed string —
safe, not silently wrong.

**What happens to a malformed or unrecognised `v1:` string:** `TryDecode` returns `false` for a
wrong prefix, a wrong segment count, a non-base64 id segment, or a base64 segment that is not
exactly 16 bytes. `CallbackRouter` treats a `false` decode and a well-formed-but-unregistered
action key identically — both produce `"That button is no longer valid."`, always answered, no
edit, no exception. Spec §6.4's own words, quoted above, "an unrecognised key produces a polite
message," apply to both failure shapes because from the router's perspective they are the same
failure: nothing it knows how to run.

### 5. The scope move, concretely

**Decision:** implemented exactly as `docs/tech-debt.md:108-114` already sketches, with one
addition it didn't need to spell out (computing `_allowedUpdates` once at startup rather than in a
field initializer).

**`TelegramListener` after:** constructor drops `IEnumerable<ITelegramUpdateHandler> handlers` and
takes `IServiceScopeFactory scopeFactory` instead. `_allowedUpdates` is no longer `readonly` or a
field initializer — it is computed once, at the top of `ExecuteAsync`, from a scope opened and
disposed for that one purpose, before the poll loop starts. `DispatchAsync` opens a fresh scope
per update (`using var scope = scopeFactory.CreateScope();`) and resolves
`scope.ServiceProvider.GetServices<ITelegramUpdateHandler>()` fresh each time, exactly as the
tech-debt entry's own code sketch shows.

**`MessageHandler` after:** goes back to plain constructor injection —
`MessageHandler(TelegramSettings settings, INotifier notifier, IAiClient ai)` — dropping
`IServiceScopeFactory` and the `using var scope = ...` block from `HandleAsync` entirely, along
with the now-inapplicable remarks about captive dependencies.

**Registration after:** `MessageHandler` moves from `AddSingleton<ITelegramUpdateHandler,
MessageHandler>()` to `AddScoped<ITelegramUpdateHandler, MessageHandler>()`. `TelegramListener`
itself stays a singleton — `AddHostedService<T>` always registers one — which is exactly why it
needs `IServiceScopeFactory` rather than plain injection of the (now scoped) handlers.

**How this is verified as behaviour-preserving, not merely reshuffled — the same technique F7's
own reversal used.** `TelegramListenerTests.cs` is not modified by this slice at all. Its three
assertions (owner gets the reply it expects, a stranger produces no second reply, an already-
answered update is not answered again) pass unchanged before and after Commit 1, which is what
proves the refactor changed nothing observable.

### 6. The owner check, and why a stranger's tap is still answered

**Decision:** `CallbackRouter.HandleAsync` applies the owner check itself, the same way
`MessageHandler` does and for the same reason its own remarks state — nothing in
`ITelegramUpdateHandler` or `TelegramListener` enforces it, so every handler owns it individually.
Unlike `MessageHandler`, which returns silently for a non-owner with **no** reply of any kind, a
non-owner's callback query **is still answered** — `bot.AnswerCallbackQuery(callbackQueryId,
cancellationToken: ct)`, no text — before `HandleAsync` returns. The tapped `ITaskAction` never
runs and nothing is edited.

**Why this diverges from `MessageHandler`'s silent-ignore, deliberately.** A plain message that
gets no reply costs the sender nothing visible — Telegram shows nothing either way, and spec §7.4
already tests exactly this ("Message from a non-whitelisted sender | Ignored; no LLM call
recorded"). A callback query is different in kind: spec §6.4's rule 1 is unconditional — "Always
answer the callback query, or Telegram shows a spinner indefinitely" — and that spinner sits on
the tapper's own client regardless of who they are. Nothing in this project's whitelisting exists
to punish a stranger for tapping; it exists so a stranger cannot change the owner's data. Answering
without acting satisfies both: the spinner clears, and no task is touched. This project has no
group-chat or multi-user surface today, so this branch is realistically only reachable in a test —
but it is exactly this kind of reachable-only-in-a-test branch spec §7.4 and the backlog's own F7
precedent (`Listener_StrangerSendsAMessage_OnlyTheOwnerIsAnswered`) already treat as worth a named
test, not skipped as unreachable.

### 7. `AnswerCallbackQuery` is always the last Telegram call in every branch

**Decision:** every reachable path through `CallbackRouter.HandleAsync` ends with exactly one call
to `bot.AnswerCallbackQuery`, and nothing after it. Where an edit is also needed (a successful
`ITaskAction`), the edit happens *before* the answer, not after.

**Why this order, not the reverse.** Two independent reasons converge on the same ordering. First,
semantically: whether the callback query should be answered with `"Already done."`,
`"That button is no longer valid."`, or nothing at all depends on the *outcome* of running the
action, so the answer cannot be composed before the action runs regardless of edit timing;
ordering the edit before the answer, once the outcome is known, means the answer is the one
observable signal that "this update is now fully handled," which the failure paths (owner check,
malformed codec, unknown action) already satisfy trivially since they do nothing else at all.
Second, practically: it gives this slice's own integration tests a single, uniform synchronisation
point — `wireMock.WaitForAnsweredCallbacksAsync(1, deadline)` is correct to wait on in every test,
because by the time an answer is observable in the stub's request log, any edit that update could
still produce has already been issued and is waited-for for free, rather than needing a second,
separate wait with its own race.

**The cost, named rather than hidden.** If `MarkCompletedTaskAsync` itself throws — a Telegram 400,
a network fault — the callback query is left unanswered on that one update, the same residual risk
`TelegramListener.DispatchAsync`'s own outer try/catch already accepts for every handler today (a
thrown exception is logged and the loop continues, exactly as it does for `MessageHandler`). This
slice does not add a second, handler-local try/catch to guarantee an answer even under an
unexpected exception; doing so would be new defensive machinery this project's existing risk
tolerance does not apply anywhere else, and the "always answered" required scenario (backlog's own
F6 Tests line) is satisfied by every *reachable* branch answering, not by surviving an injected
fault nothing else in this codebase is asked to survive either.

### 8. Does this fit one pull request?

Each estimate is grounded in the size of the closest existing analogue at HEAD, read directly (see
"Verified facts"), the same discipline F6-1's own Decision 6 used — or, for genuinely new files
with no close analogue, the actual line count of the code this plan specifies below.

| File | Change | Basis | Est. lines |
| :--- | :--- | :--- | ---: |
| `src/Assistant.Impl/Telegram/TelegramListener.cs` | modify | full before/after in Commit 1, below | 40 |
| `src/Assistant.Impl/Telegram/MessageHandler.cs` | modify | full before/after in Commit 1, below | 25 |
| `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` | modify, across Commits 1 and 3 | one registration line changed, three added | 15 |
| `docs/tech-debt.md` | modify | a "Resolved at F6-2" note appended to the entry | 35 |
| `src/Assistant.Interfaces/ITaskAction.cs` | new | `IScheduledJob.cs`, 14 lines, plus one more member and full docs | 24 |
| `src/Assistant.Impl/Services/Actions/DoneAction.cs` | new | `CreateTaskTool.cs`'s own shape, scaled down — one line of real logic | 17 |
| `src/Assistant.Impl/Telegram/CallbackCodec.cs` | new | full text in Commit 2, below | 60 |
| `tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs` | new | full text in Commit 2, below | 75 |
| `src/Assistant.Interfaces/INotifier.cs` | modify | one method, doc-heavy like the existing one | 22 |
| `src/Assistant.Impl/Telegram/TelegramNotifier.cs` | modify | one nine-line method | 10 |
| `src/Assistant.Impl/Telegram/CallbackRouter.cs` | new | full text in Commit 3, below | 90 |
| `src/Assistant.Impl/Assistant.Impl.csproj` | modify | one `InternalsVisibleTo` line | 1 |
| `tests/Assistant.WireMock/TelegramStubs.cs` | modify | two more mappings matching the existing two | 20 |
| `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs` | modify | full text in Commit 3, below | 125 |
| `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs` | new | full text in Commit 3, below | 210 |
| `docs/design/2026-08-22-slice-1-feature-backlog.md` | modify | F6 entry reworded, one "Settled at F6-2" note added | 20 |
| **Total** | | | **789** |

789 is under the 1000-line budget, with roughly 200 lines of headroom — but it is the largest
single-PR estimate any plan in this repository has produced so far (F9a's four-way split was the
previous ceiling-driven case, and F6-1 itself estimated 264). Two things keep this one PR rather
than a further split, named rather than assumed. First, the natural seams inside F6-2 do not
produce two *independently observable* halves the way F6-1/F6-2/F6-3 do at the feature level:
Commit 1 (the scope move) is observable only as "nothing broke," Commit 2 (the codec and the
action) is observable only as a pure-function unit test, and only Commit 3 makes anything a phone
or an integration test could show actually happen — splitting before Commit 3 would ship two pull
requests where the first proves nothing new works. Second, roughly 335 of the 789 lines
(`CallbackCodecTests.cs` plus `CallbackRouterTests.cs`) are tests, and per this project's own
stated review-effort carve-out for generated migration files (backlog §1), a thorough test suite
proving idempotency, the owner check, and the two distinct malformed/unrecognised-input paths is
exactly what this slice's own testing section asks for — cutting it down would not shrink the
feature, only the confidence in it.

---

## What this slice does NOT include

- **A button anywhere.** No `reply_markup`, no `InlineKeyboardMarkup`, nothing `DueReminderJob`
  sends changes. All F6-3, per "How F6 is sliced."
- **`ICallbackHandler`.** Decision 1.
- **`SnoozeAction`, `RescheduleAction`, `EditAction`, or an `arg` on `ITaskAction.ExecuteAsync` or
  `CallbackCodec`.** All F11, per Decision 4.
- **Clearing or setting `reply_markup` on an edited message.** Decision 3 — there is nothing to
  clear yet.
- **Any `ITaskRepository`/`EfTaskRepository` change.** `DoneAction` reaches `ITaskService` only;
  `CallbackRouter` reaches `ITaskService` only.
- **Any change to `TaskService.CompleteAsync` or `ITaskService`'s existing surface.** F6-1 already
  shipped exactly what this slice needs.
- **A `docker compose up -d --build` requirement for Commits 1 and 2.** Only Commit 3 touches
  `Assistant.WireMock`.

---

## File Structure

```
src/Assistant.Interfaces/
    ITaskAction.cs                            new                                     (Commit 2)
    INotifier.cs                              + MarkCompletedTaskAsync                (Commit 3)

src/Assistant.Impl/
    Services/Actions/DoneAction.cs            new                                     (Commit 2)
    Telegram/CallbackCodec.cs                 new                                     (Commit 2)
    Telegram/CallbackRouter.cs                new                                     (Commit 3)
    Telegram/TelegramListener.cs              scope moved into DispatchAsync          (Commit 1)
    Telegram/MessageHandler.cs                back to plain constructor injection     (Commit 1)
    Telegram/TelegramNotifier.cs              + MarkCompletedTaskAsync                (Commit 3)
    ImplServiceCollectionExtensions.cs        registrations updated                   (Commits 1, 3)
    Assistant.Impl.csproj                     + InternalsVisibleTo IntegrationTests   (Commit 3)

tests/Assistant.UnitTests/
    Telegram/CallbackCodecTests.cs            new                                     (Commit 2)

tests/Assistant.WireMock/
    TelegramStubs.cs                          + answerCallbackQuery, editMessageText  (Commit 3)

tests/Assistant.IntegrationTests/
    Infrastructure/WireMockFixture.cs         + callback-query seeding/observation    (Commit 3)
    Telegram/CallbackRouterTests.cs           new                                     (Commit 3)

docs/
    tech-debt.md                              entry resolved                         (Commit 1)
    design/2026-08-22-slice-1-feature-backlog.md
                                               F6 entry corrected                     (Commit 3)
```

`tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs` is absent from this list,
deliberately — Decision 5's whole point is that it needs no change to prove the refactor correct.

---

## Validation

**Test count arithmetic.** Baseline: 41 unit, 39 integration (see "Verified facts").

- Commit 1 adds no test file and no test method — it is a refactor proven by an *unchanged*
  existing suite. Unit stays **41**, integration stays **39**.
- Commit 2 adds `CallbackCodecTests.cs` to `Assistant.UnitTests`: `Encode_KnownTaskId_...` (1),
  `TryDecode_WellFormedString_...` (1), and a `[Theory]` with 6 `[InlineData]` malformed-input
  cases — unit: 41 + 1 + 1 + 6 = **49** after Commit 2. Integration stays **39** — no integration
  test file is touched.
- Commit 3 adds `CallbackRouterTests.cs` to `Assistant.IntegrationTests`: three `[Fact]` methods
  (`Listener_OwnerTapsDone_...`, `Listener_DoneTappedOnAnAlreadyCompletedTask_...`,
  `Listener_StrangerTapsTheButton_...`) plus one `[Theory]` with 2 `[InlineData]` cases
  (`Listener_UnrecognisedCallbackData_...`) — integration: 39 + 3 + 2 = **44** after Commit 3.
  Unit stays **49** — no unit test file is touched.

Expected final state: **49 unit, 44 integration.**

**Split between `Assistant.UnitTests` and `Assistant.IntegrationTests`, justified per spec §7.2:**

- `CallbackCodec` is a pure function over strings and a `Guid` — no side effect, no I/O, exactly
  spec §7.2's carve-out 2 ("mapper round-trips... pure functions, nothing to integrate") applied to
  a codec instead of a mapper. Its round-trip and its six malformed-input cases would each cost a
  full listener start/stop and a WireMock round-trip to prove at the integration level, for zero
  additional confidence over a millisecond-scale unit test — exactly the "two places to update, no
  extra confidence" duplication §7.2 forbids, run in reverse (choosing the cheap level, not adding
  a redundant expensive one).
- Every rule `CallbackRouter`/`DoneAction` enforces has an observable side effect — a database row
  changing, a specific wire request reaching the stub, or the deliberate *absence* of one (no edit,
  no status change) — so §7.2's third carve-out (rules with *no* observable effect) does not apply
  to any of them, and none is duplicated as a unit test.

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests

docker compose -f compose.test.yaml up -d          # Commits 1 and 2 only
docker compose -f compose.test.yaml up -d --build   # Commit 3 onward -- Assistant.WireMock changed
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

**What this slice can and cannot show on a real phone.** Nothing — there is still no button
anywhere a phone could tap, per "What this slice does NOT include." The only observable proof this
slice offers is the test suite above: a callback query carrying a valid, well-formed `v1:done:...`
payload completes the task, strikes the message through, and is answered; a second such tap is
answered "Already done." with no second edit; a malformed or unrecognised payload is still
answered, politely, with no edit; and a non-owner's tap is answered but changes nothing. F6's own
`observable` milestone still belongs to F6-3.

---

## Steps

**Decisions this slice carries:** 1-8, given in full above.

**Consumes:** `ITelegramUpdateHandler`, `MessageHandler`, `TelegramListener`, `TelegramNotifier`,
`INotifier` (F7/F4a), `ITaskService`/`ErrorCode.TaskAlreadyCompleted` (F6-1),
`IAssistantTool`/`IScheduledJob` (F5b/F9a, as the precedent for resolve-by-key), `WireMockFixture`/
`PostgresFixture`/`IntegrationCollection`/`ReminderTaskBuilder` (F1-F7 test infrastructure).
**Produces:** the scope-per-update fix, `ITaskAction`/`DoneAction`, `CallbackCodec`,
`INotifier.MarkCompletedTaskAsync`/`TelegramNotifier`'s implementation, `CallbackRouter`, and the
WireMock/fixture growth needed to observe all of it.

Three commits. Commit 1 is a pure refactor with no new behaviour, verified by an unchanged existing
test file — it can be reviewed and merged on its own even if Commits 2 and 3 were to change later.
Commit 2 adds two new, disconnected, fully unit-tested types with no DI registration and no
consumer yet — nothing in the running system calls them. Commit 3 wires everything together:
`CallbackRouter`, its DI registration, `INotifier`'s growth, the WireMock/fixture growth needed to
see any of it, and the integration tests that prove it end to end.

### Commit 1: pay off the scope-per-handler tech debt

**Files:**
- Modify: `src/Assistant.Impl/Telegram/TelegramListener.cs`
- Modify: `src/Assistant.Impl/Telegram/MessageHandler.cs`
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`
- Modify: `docs/tech-debt.md`

- [ ] **Step 1: Rewrite `TelegramListener.cs` in full**

Replace the entire file:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Polls Telegram for inbound updates and dispatches each one to the handlers that claim it.
/// </summary>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="scopeFactory">
/// Opens one scope per update to resolve the registered handlers, and one further scope at
/// startup to compute <c>allowedUpdates</c>. Handlers are registered scoped, not singleton, so
/// they can constructor-inject scoped dependencies directly -- <see cref="CallbackRouter"/>
/// resolves <c>ITaskAction</c> implementations that ultimately reach the scoped database context.
/// See the "Each handler opens its own scope" entry in docs/tech-debt.md.
/// </param>
/// <param name="timeProvider">Supplies the delay applied after a failed poll.</param>
/// <param name="logger">Where a failure is recorded.</param>
/// <remarks>
/// The offset is advanced before an update is dispatched, not after. Dispatching first would
/// re-poll an update whose handler always throws, forever and at full speed, so one
/// malformed message would wedge the assistant and hammer Telegram. Advancing first
/// costs at most one dropped reply instead. This is the opposite of the reminder path's
/// send-then-mark ordering, because there a lost message is the product's core failure
/// while here it costs the owner one retype.
/// <para>
/// An update no handler claims is ignored silently rather than logged as unexpected: Telegram's
/// documentation states that <c>allowedUpdates</c> does not affect updates already queued before
/// the call that set it, so a kind this listener did not ask for can still arrive.
/// </para>
/// </remarks>
internal sealed class TelegramListener(
    ITelegramBotClient bot,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<TelegramListener> logger) : BackgroundService
{
    private const int LongPollSeconds = 30;

    private static readonly TimeSpan PollFailureBackoff = TimeSpan.FromSeconds(5);

    private UpdateType[] _allowedUpdates = [];

    private int? _offset;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            _allowedUpdates = scope.ServiceProvider
                .GetServices<ITelegramUpdateHandler>()
                .Select(h => h.Handles)
                .Distinct()
                .ToArray();
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PollOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure.
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var updates = await FetchAsync(ct);

        foreach (var update in updates)
        {
            _offset = update.Id + 1;

            await DispatchAsync(update, ct);
        }
    }

    private async Task<Update[]> FetchAsync(CancellationToken ct)
    {
        try
        {
            return await bot.GetUpdates(
                offset: _offset,
                limit: null,
                timeout: LongPollSeconds,
                allowedUpdates: _allowedUpdates,
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Polling Telegram for updates failed; the loop continues.");
            await Task.Delay(PollFailureBackoff, timeProvider, ct);
            return [];
        }
    }

    private async Task DispatchAsync(Update update, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        foreach (var handler in scope.ServiceProvider
            .GetServices<ITelegramUpdateHandler>()
            .Where(h => h.Handles == update.Type))
        {
            try
            {
                await handler.HandleAsync(update, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex, "Handling update {UpdateId} failed; the loop continues.", update.Id);
            }
        }
    }
}
```

- [ ] **Step 2: Rewrite `MessageHandler.cs` in full**

Replace the entire file:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Sends the owner's plain-text message to the chat model and replies once it names a tool.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="notifier">Where the reply is delivered.</param>
/// <param name="ai">Reaches the configured chat model for an answer.</param>
/// <remarks>
/// The owner check lives inline here, on purpose: the assistant serves exactly one person, so
/// there is nothing to route between. Any future handler must apply the same check itself --
/// nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/> enforces it.
/// This handler is registered scoped and resolved fresh, inside a scope
/// <see cref="TelegramListener.DispatchAsync"/> opens per update, so <see cref="IAiClient"/> is
/// injected directly -- there is no captive-dependency concern the way there was when this
/// handler was a singleton.
/// </remarks>
internal sealed class MessageHandler(TelegramSettings settings, INotifier notifier, IAiClient ai)
    : ITelegramUpdateHandler
{
    private const string Unreachable =
        "I could not reach the model just now. Send that again in a moment.";

    private const string ToolCallNotActedOnYet =
        "Got it -- I understood that as a task, but I cannot save it yet.";

    private const string NotUnderstoodAsATask =
        "I did not read that as a task. Try rephrasing it.";

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

        var result = await ai.AskAsync(text, ct);

        var reply = result switch
        {
            { IsSuccess: true } => ToolCallNotActedOnYet,
            { Error: ErrorCode.ModelReturnedNoToolCall } => NotUnderstoodAsATask,
            _ => Unreachable,
        };

        await notifier.SendAsync(reply, ct);
    }
}
```

- [ ] **Step 3: Update `AddAssistantListener`'s registration**

In `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`:

Before:

```csharp
    public static IServiceCollection AddAssistantListener(this IServiceCollection services)
    {
        services.AddSingleton<ITelegramUpdateHandler, MessageHandler>();
        services.AddHostedService<TelegramListener>();
        return services;
    }
```

After:

```csharp
    public static IServiceCollection AddAssistantListener(this IServiceCollection services)
    {
        services.AddScoped<ITelegramUpdateHandler, MessageHandler>();
        services.AddHostedService<TelegramListener>();
        return services;
    }
```

`CallbackRouter`'s own registration line is added in Commit 3, alongside the new type — adding it
here would register a handler with no file yet to back it.

- [ ] **Step 4: Resolve the tech-debt entry**

In `docs/tech-debt.md`, append immediately after the entry's existing "Scope when it is picked up"
paragraph (after the line ending "...simplify `MessageHandler` to match."):

```markdown

**Resolved at F6-2.** `TelegramListener.DispatchAsync` opens one scope per update and resolves
`ITelegramUpdateHandler` from it; `MessageHandler` is back to plain constructor injection of
`IAiClient`; both are registered scoped. One correction to this entry's own text, made in the same
commit that resolves it: the second handler this entry predicted, `CallbackRouter`, shipped without
a paired `ICallbackHandler` interface: nothing in this design routes a callback query a second,
different way, so a router-level interface with one implementation would be exactly the guess this
project has already twice reversed the cost of writing. `CallbackRouter` implements
`ITelegramUpdateHandler` directly, the same as `MessageHandler` does.
```

- [ ] **Step 5: Build and run the untouched suite unchanged**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~TelegramListenerTests"
```

Expected: zero warnings; unit tests **41 passed**, unchanged; `TelegramListenerTests` **4 passed**,
the identical count and the identical four test names as before this commit — this is the proof
the refactor changed nothing observable, per Decision 5.

- [ ] **Step 6: Run the whole integration suite**

```bash
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

Expected: **39 passed**, unchanged from baseline.

- [ ] **Step 7: Commit**

```bash
git add src/Assistant.Impl/Telegram/TelegramListener.cs \
        src/Assistant.Impl/Telegram/MessageHandler.cs \
        src/Assistant.Impl/ImplServiceCollectionExtensions.cs \
        docs/tech-debt.md
git commit
```

Message:

```
refactor: move the per-update scope into TelegramListener

TelegramListener.DispatchAsync now opens one scope per update and
resolves ITelegramUpdateHandler from it, rather than each handler
opening its own scope inside HandleAsync. MessageHandler goes back to
plain constructor injection of IAiClient. Both are registered scoped
instead of singleton, which is what makes this legal -- a singleton
still cannot constructor-inject a scoped service, but a handler
resolved fresh per update from a scope can.

_allowedUpdates can no longer be a field initializer over an injected
handler collection, since handlers are no longer resolvable at
construction: it is computed once, from a throwaway scope opened at
the top of ExecuteAsync, before the poll loop starts.

This is the fix docs/tech-debt.md's "Each handler opens its own
scope" entry already named and scoped to this exact pull request --
the one about to add the second ITelegramUpdateHandler. That entry is
resolved in this commit; TelegramListenerTests passes unchanged
before and after, which is what proves the refactor preserved
behaviour rather than merely reshuffling it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

### Commit 2: the seam — `ITaskAction`, `DoneAction`, the callback codec

**Files:**
- Create: `src/Assistant.Interfaces/ITaskAction.cs`
- Create: `src/Assistant.Impl/Services/Actions/DoneAction.cs`
- Create: `src/Assistant.Impl/Telegram/CallbackCodec.cs`
- Create: `tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs`

- [ ] **Step 1: Create `ITaskAction.cs`**

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// One action an inline button's tap can perform on a task.
/// </summary>
/// <remarks>
/// Resolved by <see cref="Key"/> against the callback codec's decoded action segment. A caller
/// that finds no implementation whose key matches produces a polite reply rather than throwing,
/// per spec 6.4. <c>DoneAction</c> is the first implementation; snooze, reschedule and edit
/// actions follow at F11, each adding one more implementation rather than changing this one.
/// </remarks>
public interface ITaskAction
{
    /// <summary>
    /// The action's key, as carried on the wire inside the callback codec.
    /// </summary>
    /// <value>Lowercase, for example <c>done</c>.</value>
    string Key { get; }

    /// <summary>
    /// Performs the action against the given task.
    /// </summary>
    /// <param name="taskId">The task the button referred to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    Task<Result> ExecuteAsync(Guid taskId, CancellationToken ct);
}
```

- [ ] **Step 2: Create `DoneAction.cs`**

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
    public string Key => "done";

    /// <inheritdoc/>
    public Task<Result> ExecuteAsync(Guid taskId, CancellationToken ct) =>
        taskService.CompleteAsync(taskId, ct);
}
```

- [ ] **Step 3: Create `CallbackCodec.cs`**

```csharp
namespace Assistant.Impl.Telegram;

/// <summary>
/// Encodes and decodes the <c>callback_data</c> string carried on an inline button.
/// </summary>
/// <remarks>
/// The wire format is <c>v1:&lt;action&gt;:&lt;base64-id&gt;</c>, per spec 6.4. The version
/// prefix means a button left in chat history from a build that no longer understands its exact
/// format degrades to a polite reply instead of throwing. <see cref="TryDecode"/> only ever reads
/// this exact three-segment shape -- it has no notion yet of the optional trailing
/// <c>:&lt;arg&gt;</c> segment spec 6.4 also describes; nothing produces or consumes one until an
/// argument-taking action arrives.
/// </remarks>
internal static class CallbackCodec
{
    private const string Prefix = "v1";

    /// <summary>
    /// Builds the callback data string for one button.
    /// </summary>
    /// <param name="action">The action's key, matching <c>ITaskAction.Key</c>.</param>
    /// <param name="taskId">The task the button refers to.</param>
    /// <returns>
    /// A string of the form <c>v1:&lt;action&gt;:&lt;base64-id&gt;</c> -- 32 characters for the
    /// four-letter key <c>done</c>, comfortably inside Telegram's 64-byte callback data limit.
    /// </returns>
    public static string Encode(string action, Guid taskId) =>
        $"{Prefix}:{action}:{Convert.ToBase64String(taskId.ToByteArray())}";

    /// <summary>
    /// Attempts to decode a callback data string.
    /// </summary>
    /// <param name="data">The raw string from <c>CallbackQuery.Data</c>.</param>
    /// <param name="action">The decoded action key, or empty when decoding fails.</param>
    /// <param name="taskId">The decoded task identifier, or <see cref="Guid.Empty"/> when decoding fails.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="data"/> is a well-formed
    /// <c>v1:&lt;action&gt;:&lt;base64-id&gt;</c> string; <see langword="false"/> for anything
    /// else, including a different version prefix, a wrong number of segments, or an id segment
    /// that is not valid base64 encoding exactly 16 bytes.
    /// </returns>
    public static bool TryDecode(string data, out string action, out Guid taskId)
    {
        action = string.Empty;
        taskId = Guid.Empty;

        var parts = data.Split(':');

        if (parts.Length != 3 || parts[0] != Prefix)
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length != 16)
        {
            return false;
        }

        action = parts[1];
        taskId = new Guid(bytes);
        return true;
    }
}
```

- [ ] **Step 4: Create `CallbackCodecTests.cs`**

```csharp
using Assistant.Impl.Telegram;

namespace Assistant.UnitTests.Telegram;

/// <summary>
/// Test class for <see cref="CallbackCodec"/>.
/// </summary>
public sealed class CallbackCodecTests
{
    /// <summary>
    /// When a known task id is encoded
    /// Then the exact wire string is produced.
    /// </summary>
    [Fact]
    public void Encode_KnownTaskId_ProducesTheExpectedString()
    {
        // Act
        var data = CallbackCodec.Encode("done", Guid.Empty);

        // Assert
        Assert.Equal("v1:done:AAAAAAAAAAAAAAAAAAAAAA==", data);
    }

    /// <summary>
    /// When a string is encoded for a task
    /// And that same string is decoded
    /// Then the original action and task id are recovered.
    /// </summary>
    [Fact]
    public void TryDecode_WellFormedString_RecoversTheActionAndTaskId()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var data = CallbackCodec.Encode("done", taskId);

        // Act
        var decoded = CallbackCodec.TryDecode(data, out var action, out var recoveredId);

        // Assert
        Assert.True(decoded);
        Assert.Equal("done", action);
        Assert.Equal(taskId, recoveredId);
    }

    /// <summary>
    /// When a string does not match the v1:&lt;action&gt;:&lt;base64-id&gt; shape
    /// Then it is not decoded.
    /// </summary>
    [Theory]
    [InlineData("garbage")]
    [InlineData("v1:done")]
    [InlineData("v2:done:AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("v1:done:not-valid-base64!!")]
    [InlineData("v1:done:AAAA")]
    [InlineData("v1:done:AAAAAAAAAAAAAAAAAAAAAA==:1h")]
    public void TryDecode_MalformedOrUnsupportedStrings_Fails(string data)
    {
        // Act
        var decoded = CallbackCodec.TryDecode(data, out var action, out var taskId);

        // Assert
        Assert.False(decoded);
        Assert.Equal(string.Empty, action);
        Assert.Equal(Guid.Empty, taskId);
    }
}
```

- [ ] **Step 5: Build and run the new unit tests**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --filter "FullyQualifiedName~CallbackCodecTests"
```

Expected: zero warnings; `CallbackCodecTests` **8 passed** (1 `[Fact]` + 1 `[Fact]` + 1 `[Theory]`
with 6 `[InlineData]` cases).

- [ ] **Step 6: Run the whole suite**

```bash
dotnet test tests/Assistant.UnitTests
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

Expected: unit tests **49 passed** (41 + 8, see "Test count arithmetic"); integration tests **39
passed**, unchanged — nothing in this commit registers `DoneAction` or touches Telegram.

- [ ] **Step 7: Commit**

```bash
git add src/Assistant.Interfaces/ITaskAction.cs \
        src/Assistant.Impl/Services/Actions/DoneAction.cs \
        src/Assistant.Impl/Telegram/CallbackCodec.cs \
        tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs
git commit
```

Message:

```
feat: ITaskAction, DoneAction, and the callback codec

ITaskAction is resolved by Key, the same shape IAssistantTool and
IScheduledJob already use for a seam with more than one real
implementation coming -- DoneAction is the first of four, the other
three (snooze, reschedule, edit) arriving at F11. DoneAction is a
one-line pass-through onto TaskService.CompleteAsync, already shipped
at F6-1.

CallbackCodec encodes and decodes the v1:<action>:<base64-id> wire
format spec 6.4 defines. Encode has no production caller yet -- F6-3's
button is the first -- but this slice's own integration tests are a
real one, seeding callback-query updates through it rather than
duplicating the base64 encoding by hand. Decode accepts exactly three
segments; the spec's optional trailing :<arg> segment is left unparsed
until F11's first argument-taking action gives it something to carry.

Neither new type is wired into DI yet -- nothing in the running system
calls either one until CallbackRouter arrives.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

### Commit 3: the router — `CallbackRouter`, the edit, and the tests that prove it

**Files:**
- Modify: `src/Assistant.Interfaces/INotifier.cs`
- Modify: `src/Assistant.Impl/Telegram/TelegramNotifier.cs`
- Create: `src/Assistant.Impl/Telegram/CallbackRouter.cs`
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`
- Modify: `src/Assistant.Impl/Assistant.Impl.csproj`
- Modify: `tests/Assistant.WireMock/TelegramStubs.cs`
- Modify: `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`
- Create: `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs`
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`

- [ ] **Step 1: Add `MarkCompletedTaskAsync` to `INotifier`**

In `src/Assistant.Interfaces/INotifier.cs`, append inside the interface, after `SendAsync`:

```csharp

    /// <summary>
    /// Updates a previously sent message to reflect that the task it announced is now complete.
    /// </summary>
    /// <param name="messageId">Identifier of the message to edit.</param>
    /// <param name="text">
    /// The plain, unescaped text the message originally carried. The adapter escapes it and
    /// applies its own rendering for completion; callers must not pre-format or pre-escape.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the edit has been accepted.</returns>
    /// <remarks>
    /// Sends no keyboard instruction, so whatever inline keyboard the message already carries, if
    /// any, is left exactly as it is -- there is nothing to clear yet, because nothing in this
    /// codebase attaches a keyboard to a message before this method might edit it. F6-3, which
    /// attaches the first one, must revisit this call to pass an explicit empty keyboard, or a
    /// completed reminder keeps its dead Done button visible under a message that already shows
    /// the task as done.
    /// </remarks>
    Task MarkCompletedTaskAsync(int messageId, string text, CancellationToken ct);
```

- [ ] **Step 2: Implement it in `TelegramNotifier`**

In `src/Assistant.Impl/Telegram/TelegramNotifier.cs`, after `SendAsync`:

Before:

```csharp
    /// <inheritdoc/>
    public async Task SendAsync(string text, CancellationToken ct) =>
        await bot.SendMessage(settings.OwnerChatId, Escape(text), ParseMode.Html, cancellationToken: ct);
```

After:

```csharp
    /// <inheritdoc/>
    public async Task SendAsync(string text, CancellationToken ct) =>
        await bot.SendMessage(settings.OwnerChatId, Escape(text), ParseMode.Html, cancellationToken: ct);

    /// <inheritdoc/>
    /// <remarks>
    /// Renders completion by wrapping the escaped text in an inline &lt;s&gt; element -- this
    /// adapter's own choice of how to show completion, not part of the interface's contract. F6-3
    /// must revisit this call once it attaches the first inline keyboard, or a completed reminder
    /// keeps a dead Done button visible under the struck-through title.
    /// </remarks>
    public async Task MarkCompletedTaskAsync(int messageId, string text, CancellationToken ct) =>
        await bot.EditMessageText(
            settings.OwnerChatId, messageId, $"<s>{Escape(text)}</s>", ParseMode.Html, cancellationToken: ct);
```

The new method is inserted directly above the file's existing `// "&" must be replaced first. ...`
comment and `Escape` method, both of which are otherwise untouched by this step.

- [ ] **Step 3: Create `CallbackRouter.cs`**

```csharp
using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Routes an inline button's tap to the <see cref="ITaskAction"/> its callback data names, then
/// always answers the callback query.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="notifier">Where the completed-task edit is delivered on a successful action.</param>
/// <param name="actions">Every registered task action, resolved by <see cref="ITaskAction.Key"/>.</param>
/// <remarks>
/// The callback query is answered last in every branch, after any edit a successful action
/// triggers, never before -- every reachable path through <see cref="HandleAsync"/> ends with
/// exactly one call to <see cref="ITelegramBotClient"/>'s answer method and nothing after it, so
/// observing that one call is enough to know the whole update has been fully handled.
/// <para>
/// The owner check lives inline here, the same as <see cref="MessageHandler"/>'s own remarks
/// explain: nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/>
/// enforces it. Unlike <see cref="MessageHandler"/>, a non-owner's tap is still answered -- spec
/// 6.4 requires every callback query to be answered, owner or not, or Telegram leaves that
/// tapper's own client spinning -- but the action itself never runs and nothing is edited.
/// </para>
/// </remarks>
internal sealed class CallbackRouter(
    TelegramSettings settings,
    ITelegramBotClient bot,
    INotifier notifier,
    IEnumerable<ITaskAction> actions) : ITelegramUpdateHandler
{
    private const string ThatButtonIsNoLongerValid = "That button is no longer valid.";

    private const string AlreadyDone = "Already done.";

    private const string CouldNotFindThatTask = "I could not find that task.";

    /// <inheritdoc/>
    public UpdateType Handles => UpdateType.CallbackQuery;

    /// <inheritdoc/>
    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (update.CallbackQuery is not
            {
                Id: var callbackQueryId,
                Data: { } data,
                Message: { Chat.Id: var chatId, Id: var messageId, Text: { } messageText },
            })
        {
            return;
        }

        if (chatId != settings.OwnerChatId)
        {
            await bot.AnswerCallbackQuery(callbackQueryId, cancellationToken: ct);
            return;
        }

        if (!CallbackCodec.TryDecode(data, out var actionKey, out var taskId))
        {
            await bot.AnswerCallbackQuery(callbackQueryId, ThatButtonIsNoLongerValid, cancellationToken: ct);
            return;
        }

        var action = actions.FirstOrDefault(a => a.Key == actionKey);

        if (action is null)
        {
            await bot.AnswerCallbackQuery(callbackQueryId, ThatButtonIsNoLongerValid, cancellationToken: ct);
            return;
        }

        var result = await action.ExecuteAsync(taskId, ct);

        if (result.IsSuccess)
        {
            await notifier.MarkCompletedTaskAsync(messageId, messageText, ct);
        }

        var reply = result switch
        {
            { IsSuccess: true } => null,
            { Error: ErrorCode.TaskAlreadyCompleted } => AlreadyDone,
            _ => CouldNotFindThatTask,
        };

        await bot.AnswerCallbackQuery(callbackQueryId, reply, cancellationToken: ct);
    }
}
```

- [ ] **Step 4: Register `CallbackRouter` and `DoneAction`**

In `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`:

Before:

```csharp
    /// <summary>
    /// Registers the inbound Telegram listener.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddAssistantTelegram</c> for the client and the owner's chat id, and
    /// <c>AddAssistantServices</c> for the <see cref="TimeProvider"/> the failure backoff uses.
    /// </remarks>
    public static IServiceCollection AddAssistantListener(this IServiceCollection services)
    {
        services.AddScoped<ITelegramUpdateHandler, MessageHandler>();
        services.AddHostedService<TelegramListener>();
        return services;
    }
```

After:

```csharp
    /// <summary>
    /// Registers the inbound Telegram listener.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddAssistantTelegram</c> for the client and the owner's chat id, and
    /// <c>AddAssistantServices</c> for the <see cref="TimeProvider"/> the failure backoff uses and
    /// the <see cref="ITaskService"/> <see cref="Telegram.CallbackRouter"/>'s actions reach.
    /// Handlers and task actions are registered scoped, not singleton, so
    /// <see cref="Telegram.TelegramListener"/> can resolve them from a scope it opens per update;
    /// see docs/tech-debt.md.
    /// </remarks>
    public static IServiceCollection AddAssistantListener(this IServiceCollection services)
    {
        services.AddScoped<ITelegramUpdateHandler, MessageHandler>();
        services.AddScoped<ITelegramUpdateHandler, CallbackRouter>();
        services.AddScoped<ITaskAction, DoneAction>();
        services.AddHostedService<TelegramListener>();
        return services;
    }
```

This method's file already has `using Assistant.Impl.Services.Jobs;` and `using
Assistant.Impl.Telegram;` for other registrations in the same file; `Assistant.Impl.Services.Actions`
(for `DoneAction`) needs a new `using` line alongside them.

- [ ] **Step 5: Grant `Assistant.IntegrationTests` access to `Assistant.Impl` internals**

In `src/Assistant.Impl/Assistant.Impl.csproj`:

Before:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Assistant.UnitTests" />
  </ItemGroup>
```

After:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Assistant.UnitTests" />
    <InternalsVisibleTo Include="Assistant.IntegrationTests" />
  </ItemGroup>
```

Needed because `CallbackRouterTests`, below, calls the internal `CallbackCodec.Encode` to build
valid callback data — the same reasoning Decision 4 gives for not duplicating that logic by hand
inside the test.

- [ ] **Step 6: Add the two missing mappings to the WireMock stub**

In `tests/Assistant.WireMock/TelegramStubs.cs`:

Before:

```csharp
    private const string SendMessageResponse = """
        {"ok":true,"result":{"message_id":1,"date":1756000000,
         "chat":{"id":1,"type":"private"},"text":"stubbed"}}
        """;

    private const string NoUpdatesResponse = """{"ok":true,"result":[]}""";
```

After:

```csharp
    private const string SendMessageResponse = """
        {"ok":true,"result":{"message_id":1,"date":1756000000,
         "chat":{"id":1,"type":"private"},"text":"stubbed"}}
        """;

    private const string AnswerCallbackQueryResponse = """{"ok":true,"result":true}""";

    private const string NoUpdatesResponse = """{"ok":true,"result":[]}""";
```

And, inside `Install`, after the existing `sendMessage` mapping and before the `getUpdates` one:

```csharp
        server
            .Given(Request.Create().WithPath("/bot*/sendMessage").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(SendMessageResponse));

        server
            .Given(Request.Create().WithPath("/bot*/answerCallbackQuery").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(AnswerCallbackQueryResponse));

        server
            .Given(Request.Create().WithPath("/bot*/editMessageText").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(SendMessageResponse));
```

`editMessageText` reuses `SendMessageResponse` deliberately — `EditMessageText`'s client-side
return type is `Task<Message>`, the identical envelope shape `sendMessage` already returns, so a
second, differently-worded constant would name nothing different. Neither new mapping needs
`.AtPriority(...)`: no test customises either response the way tests customise `getUpdates`, so
the plain default priority `sendMessage`'s own mapping already uses is correct here too.

- [ ] **Step 7: Grow `WireMockFixture` to seed and observe callback queries**

In `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`, add the seeding method
directly after `SeedUpdatesAsync`:

```csharp

    /// <summary>
    /// Makes the stub serve the given callback-query updates to the next getUpdates poll.
    /// </summary>
    /// <param name="updates">The updates to serve, in the order Telegram would.</param>
    /// <returns>A task that completes once both mappings are installed.</returns>
    /// <remarks>
    /// Shares <see cref="SeedUpdatesAsync"/>'s own two-mapping, drained-by-offset shape and its
    /// two mapping ids: a test seeds one call or the other, never both, so there is nothing to
    /// double-book.
    /// </remarks>
    public async Task SeedCallbackQueryUpdatesAsync(params InboundCallbackQuery[] updates)
    {
        var pending = new JsonArray(updates.Select(u => (JsonNode)new JsonObject
        {
            ["update_id"] = u.UpdateId,
            ["callback_query"] = new JsonObject
            {
                ["id"] = u.CallbackQueryId,
                ["from"] = new JsonObject { ["id"] = u.ChatId, ["is_bot"] = false, ["first_name"] = "Owner" },
                ["message"] = new JsonObject
                {
                    ["message_id"] = u.MessageId,
                    ["date"] = 1756000000L,
                    ["chat"] = new JsonObject { ["id"] = u.ChatId, ["type"] = "private" },
                    ["text"] = u.MessageText,
                },
                ["chat_instance"] = "test-instance",
                ["data"] = u.Data,
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
```

Directly after `SentMessagesAsync`, add the two observation methods:

```csharp

    /// <summary>
    /// Returns the answer-callback-query requests the stub received, in order.
    /// </summary>
    /// <returns>One payload per captured request.</returns>
    public async Task<IReadOnlyList<AnswerCallbackQueryPayload>> AnsweredCallbacksAsync()
    {
        var entries = await _http.GetFromJsonAsync<List<AdminLogEntry>>($"{Url}/__admin/requests")
                      ?? [];

        return entries
            .Where(entry => entry.Request.Path.EndsWith("/answerCallbackQuery", StringComparison.Ordinal)
                            && entry.Request.Method == "POST")
            .Select(entry => JsonSerializer.Deserialize<AnswerCallbackQueryPayload>(entry.Request.Body)!)
            .ToList();
    }

    /// <summary>
    /// Returns the edit-message-text requests the stub received, in order.
    /// </summary>
    /// <returns>One payload per captured request.</returns>
    public async Task<IReadOnlyList<EditMessageTextPayload>> EditedMessagesAsync()
    {
        var entries = await _http.GetFromJsonAsync<List<AdminLogEntry>>($"{Url}/__admin/requests")
                      ?? [];

        return entries
            .Where(entry => entry.Request.Path.EndsWith("/editMessageText", StringComparison.Ordinal)
                            && entry.Request.Method == "POST")
            .Select(entry => JsonSerializer.Deserialize<EditMessageTextPayload>(entry.Request.Body)!)
            .ToList();
    }
```

Directly after `WaitForSentMessagesAsync`, add:

```csharp

    /// <summary>
    /// Waits until the stub has received at least the given number of answered callback queries.
    /// </summary>
    /// <param name="count">How many answers to wait for.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <returns>Every answer received, which may be more than requested.</returns>
    /// <exception cref="TimeoutException">Too few answers arrived in time.</exception>
    public async Task<IReadOnlyList<AnswerCallbackQueryPayload>> WaitForAnsweredCallbacksAsync(
        int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var answered = await AnsweredCallbacksAsync();

            if (answered.Count >= count)
            {
                return answered;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Expected at least {count} answered callback(s) within {timeout.TotalSeconds:0.#}s; "
            + $"got {(await AnsweredCallbacksAsync()).Count}.");
    }
```

Directly after the `InboundUpdate` record, add:

```csharp

/// <summary>
/// An inbound Telegram callback-query update, as
/// <see cref="WireMockFixture.SeedCallbackQueryUpdatesAsync"/> serves it.
/// </summary>
/// <param name="UpdateId">Telegram's identifier for the update.</param>
/// <param name="CallbackQueryId">Telegram's identifier for the callback query itself.</param>
/// <param name="ChatId">The chat the tap appears to come from.</param>
/// <param name="MessageId">The identifier of the message the tapped button was attached to.</param>
/// <param name="MessageText">The current text of that message.</param>
/// <param name="Data">The callback data carried on the tapped button.</param>
public sealed record InboundCallbackQuery(
    int UpdateId, string CallbackQueryId, long ChatId, int MessageId, string MessageText, string Data);
```

Directly after `SendMessagePayload`'s closing brace, add:

```csharp

/// <summary>
/// The body of a Telegram <c>answerCallbackQuery</c> request.
/// </summary>
/// <param name="CallbackQueryId">The callback query being answered.</param>
/// <param name="Text">The toast text shown to the tapper, or null when none was sent.</param>
public sealed record AnswerCallbackQueryPayload(
    [property: JsonPropertyName("callback_query_id")] string CallbackQueryId,
    [property: JsonPropertyName("text")] string? Text)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>Null when the request carried exactly the named fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// The body of a Telegram <c>editMessageText</c> request.
/// </summary>
/// <param name="ChatId">The chat the edited message lives in.</param>
/// <param name="MessageId">The message being edited.</param>
/// <param name="Text">The replacement text as it went over the wire.</param>
/// <param name="ParseMode">How Telegram is told to interpret the text.</param>
public sealed record EditMessageTextPayload(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("message_id")] int MessageId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parse_mode")] string ParseMode)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>Null when the request carried exactly the named fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
```

- [ ] **Step 8: Create `CallbackRouterTests.cs`**

```csharp
using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.Impl.Telegram;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Models;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Assistant.IntegrationTests.Infrastructure.ReminderTaskBuilder;

namespace Assistant.IntegrationTests.Telegram;

/// <summary>
/// Test class for the callback-query handler registered via <c>AddAssistantListener</c>.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class CallbackRouterTests(PostgresFixture postgres, WireMockFixture wireMock) : IAsyncLifetime
{
    private const string BotToken = "123456:TESTTOKEN";
    private const long OwnerChatId = 100200300L;
    private const long StrangerChatId = 999888777L;
    private const int MessageId = 55;
    private const string CallbackQueryId = "cb-1";

    private static readonly TimeSpan AnswerDeadline = TimeSpan.FromSeconds(10);

    private ServiceProvider _provider = null!;

    private IHostedService _sut = null!;

    private ITaskRepository _repository = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantRepository(postgres.ConnectionString);
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
        _repository = _provider.GetRequiredService<ITaskRepository>();

        await postgres.ResetAsync();
        await wireMock.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }

    /// <summary>
    /// When the owner taps Done on a pending task
    /// Then the task is completed
    /// And the reminder message is edited to show it struck through
    /// And the callback query is answered.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerTapsDone_CompletesTheTaskAndStrikesThroughTheMessage()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode("done", task.Id);
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, task.Title, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        var expectedAnswer = new AnswerCallbackQueryPayload(CallbackQueryId, null);
        Assert.Equivalent(expectedAnswer, Assert.Single(answered), strict: true);

        var expectedEdit = new EditMessageTextPayload(OwnerChatId, MessageId, $"<s>{task.Title}</s>", "Html");
        Assert.Equivalent(expectedEdit, Assert.Single(await wireMock.EditedMessagesAsync()), strict: true);

        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(ReminderStatus.Completed, stored!.Status);
        Assert.NotNull(stored.CompletedAt);
    }

    /// <summary>
    /// When the owner taps Done on a task that is already completed
    /// Then the callback query is answered that it is already done
    /// And the message is not edited again
    /// And the stored completion instant is unchanged.
    /// </summary>
    [Fact]
    public async Task Listener_DoneTappedOnAnAlreadyCompletedTask_AnswersAlreadyDoneWithoutEditingAgain()
    {
        // Arrange
        var originalCompletedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var task = BuildReminderTask(status: ReminderStatus.Completed, completedAt: originalCompletedAt);
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode("done", task.Id);
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, task.Title, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        var expectedAnswer = new AnswerCallbackQueryPayload(CallbackQueryId, "Already done.");
        Assert.Equivalent(expectedAnswer, Assert.Single(answered), strict: true);

        Assert.Empty(await wireMock.EditedMessagesAsync());

        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(originalCompletedAt, stored!.CompletedAt);
    }

    /// <summary>
    /// When the callback data is malformed or names an action nothing implements
    /// Then the callback query is still answered
    /// And nothing is edited.
    /// </summary>
    [Theory]
    [InlineData("garbage")]
    [InlineData("v1:archive:AAAAAAAAAAAAAAAAAAAAAA==")]
    public async Task Listener_UnrecognisedCallbackData_StillAnswersButEditsNothing(string data)
    {
        // Arrange
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, "call the bank", data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        Assert.Equal(CallbackQueryId, Assert.Single(answered).CallbackQueryId);
        Assert.Empty(await wireMock.EditedMessagesAsync());
    }

    /// <summary>
    /// When someone other than the owner taps a button
    /// Then the callback query is still answered
    /// And the task is left untouched.
    /// </summary>
    [Fact]
    public async Task Listener_StrangerTapsTheButton_AnswersButLeavesTheTaskUntouched()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode("done", task.Id);
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, StrangerChatId, MessageId, task.Title, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        Assert.Equal(CallbackQueryId, Assert.Single(answered).CallbackQueryId);

        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(ReminderStatus.Pending, stored!.Status);
    }
}
```

`AddAssistantTime`/`AddAssistantAi` are registered even though no test in this class exercises the
model, for the same reason `TelegramListenerTests.InitializeAsync` already registers them: `
AddAssistantListener` registers `MessageHandler` too, and `TelegramListener.DispatchAsync` (and the
one-off startup scope that computes `_allowedUpdates`) resolves *every* registered
`ITelegramUpdateHandler` through `GetServices<T>()` before filtering by `Handles` — constructing
`MessageHandler` at all requires `IAiClient` to be resolvable, whether or not a message-shaped
update ever arrives. `TelegramListenerTests.cs` needs the identical two lines for the identical
reason, confirmed by reading its own `InitializeAsync` directly.

- [ ] **Step 9: Correct the backlog's F6 entry**

In `docs/design/2026-08-22-slice-1-feature-backlog.md`:

Before:

```markdown
**F6 · Complete a task from a button · observable** — spec §6.4
`ITaskAction` + `DoneAction`, `ICallbackHandler` + `CallbackRouter`, the `v1:<action>:<id>`
callback codec, in-place message edit, and `ITaskService.CompleteAsync`. `ReminderTask` regains
`CompletedAt`, which also brings back the `ck_completed_consistency` check constraint. Depends on
F7's `TelegramListener`: a callback query arrives on the same `getUpdates` stream. F6 adds a
`CallbackQuery` handler and registers it, and `allowedUpdates` follows on its own — but the
handler must apply the owner check itself, because there is no base class doing it.
*Tests:* one tap completes; a second tap says "already done" rather than erroring; the callback
query is always answered.
```

After:

```markdown
**F6 · Complete a task from a button · observable** — spec §6.4
`ITaskAction` + `DoneAction`, `CallbackRouter`, the `v1:<action>:<id>` callback codec, in-place
message edit, and `ITaskService.CompleteAsync`. `ReminderTask` regains `CompletedAt`, which also
brings back the `ck_completed_consistency` check constraint. Depends on F7's `TelegramListener`: a
callback query arrives on the same `getUpdates` stream. F6 adds a `CallbackQuery` handler and
registers it, and `allowedUpdates` follows on its own — but the handler must apply the owner check
itself, because there is no base class doing it.
*Tests:* one tap completes; a second tap says "already done" rather than erroring; the callback
query is always answered.
*Settled at F6-2:*
- **No `ICallbackHandler`.** This entry originally named the pair together, and
  `docs/tech-debt.md`'s own "Each handler opens its own scope" entry repeated the claim. Built as
  one abstraction fewer than planned: `CallbackRouter` implements `ITelegramUpdateHandler`
  directly, the same as `MessageHandler` does, because nothing in this design routes a callback
  query a second, different way — the real seam is `ITaskAction`, resolved by key, which this
  entry already names correctly.
- **Split across three pull requests**, not one: F6-1 (the column and the writer), F6-2 (this
  entry's routing and answering machinery), F6-3 (the button itself). F6 stays open, unmet by the
  `observable` tag, until F6-3 lands — see F6-1's and F6-2's own plans for why the button ships
  last.
```

- [ ] **Step 10: Rebuild the WireMock stub image and bring the stack up**

```bash
docker compose -f compose.test.yaml down
docker compose -f compose.test.yaml up -d --build
```

`--build` is required from this step onward: `tests/Assistant.WireMock/TelegramStubs.cs` changed,
and `compose.test.yaml`'s `wiremock` service builds that project from its own Dockerfile rather
than pulling a published image.

- [ ] **Step 11: Build and run the new integration tests**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~CallbackRouterTests"
```

Expected: zero warnings; `CallbackRouterTests` **5 passed** (3 `[Fact]` + 1 `[Theory]` with 2
`[InlineData]` cases).

- [ ] **Step 12: Run the whole suite**

```bash
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

Expected: unit tests **49 passed**, unchanged from Commit 2; integration tests **44 passed** (39 +
5, see "Test count arithmetic").

- [ ] **Step 13: Commit**

```bash
git add src/Assistant.Interfaces/INotifier.cs \
        src/Assistant.Impl/Telegram/TelegramNotifier.cs \
        src/Assistant.Impl/Telegram/CallbackRouter.cs \
        src/Assistant.Impl/ImplServiceCollectionExtensions.cs \
        src/Assistant.Impl/Assistant.Impl.csproj \
        tests/Assistant.WireMock/TelegramStubs.cs \
        tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs \
        tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs \
        docs/design/2026-08-22-slice-1-feature-backlog.md
git commit
```

Message:

```
feat: CallbackRouter answers and routes the tap

CallbackRouter is a second ITelegramUpdateHandler, for
UpdateType.CallbackQuery. It applies the owner check inline, the same
as MessageHandler does, but a non-owner's tap is still answered --
spec 6.4 requires every callback query be answered regardless of who
sent it, or Telegram leaves that tapper's own client spinning; only
the action itself, and the edit, are gated on being the owner.

Malformed callback data and a well-formed but unregistered action key
produce the identical outcome: a polite "no longer valid" answer, no
edit, no exception. A successful action's message is edited to show
its title struck through, through a new INotifier.MarkCompletedTaskAsync
that keeps HTML escaping in the one place that already owns it,
TelegramNotifier. That edit sends no reply_markup at all -- nothing in
this codebase attaches a keyboard to a message yet, so there is
nothing to clear; F6-3 must revisit this exact call once it attaches
the first one.

The callback query is answered last in every branch, after any edit,
never before: every reachable path ends with exactly one answer call,
which is also what lets this slice's own tests synchronise on a
single wait rather than racing two.

tests/Assistant.WireMock/TelegramStubs.cs now answers
answerCallbackQuery and editMessageText, which means the stub
container needs `--build` from this commit onward, not just `up -d`.
WireMockFixture grows matching seed and observation methods, verified
against the exact wire shapes captured off a live stub rather than
inferred from the SDK's own request-class attributes.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

---

## Self-review

**Commit 1 (the scope move):**
- [ ] `TelegramListener`'s constructor takes `IServiceScopeFactory`, not
      `IEnumerable<ITelegramUpdateHandler>`; `_allowedUpdates` is computed once in `ExecuteAsync`
      from a throwaway scope, not as a field initializer
- [ ] `DispatchAsync` opens one scope per update and resolves handlers from it
- [ ] `MessageHandler` takes `IAiClient` by plain constructor injection; no
      `IServiceScopeFactory`, no `using Microsoft.Extensions.DependencyInjection;`, no scope block
      remain in the file
- [ ] `MessageHandler`'s registration changed from `AddSingleton` to `AddScoped`
- [ ] `TelegramListenerTests.cs` is not modified, and passes with the identical four test names
      before and after this commit
- [ ] `docs/tech-debt.md`'s entry gets a "Resolved at F6-2" note in the same commit that resolves
      it, and that note corrects the entry's own `ICallbackHandler` claim rather than repeating it

**Commit 2 (the seam):**
- [ ] `ITaskAction` lives in `Assistant.Interfaces`, not `Assistant.Impl` — it names nothing from
      `Telegram.Bot`, so `ITelegramUpdateHandler`'s own reasoning for staying in `Impl` does not
      apply to it
- [ ] `DoneAction` lives in `Assistant.Impl.Services.Actions`, matching spec §7.5's own namespace
      naming, and reaches only `ITaskService`, never `ITaskRepository`
- [ ] `CallbackCodec.TryDecode` rejects any string with a segment count other than exactly 3 —
      including four, the arg-bearing shape spec 6.4 describes but this slice does not parse
- [ ] Neither `ITaskAction`/`DoneAction` nor `CallbackCodec` is registered in DI by this commit —
      confirmed by re-reading `ImplServiceCollectionExtensions.cs` after Commit 2's diff
- [ ] `CallbackCodecTests`'s fixed-string test (`Encode_KnownTaskId_...`) asserts against a value
      computed independently (`Convert.ToBase64String(Guid.Empty.ToByteArray())`,
      `"AAAAAAAAAAAAAAAAAAAAAA=="`), not copied from the production code under test

**Commit 3 (the router):**
- [ ] `INotifier.MarkCompletedTaskAsync` takes `messageId` and `text`, not a chat id — the
      recipient stays configuration, matching `SendAsync`'s own existing contract
- [ ] `TelegramNotifier.MarkCompletedTaskAsync` reuses the existing private `Escape`, not a
      duplicate implementation
- [ ] The `EditMessageText` call inside `MarkCompletedTaskAsync` passes no `replyMarkup` argument
      at all
- [ ] `CallbackRouter.HandleAsync`'s owner-check branch still calls `AnswerCallbackQuery` before
      returning — a non-owner's tap is answered, not silently dropped
- [ ] Every reachable branch in `CallbackRouter.HandleAsync` calls `AnswerCallbackQuery` exactly
      once, and it is always the last call in that branch
- [ ] The malformed-codec branch and the unrecognised-action-key branch produce the identical
      reply text and the identical (no) edit
- [ ] `Assistant.Impl.csproj` grants `InternalsVisibleTo` to `Assistant.IntegrationTests`, and
      `CallbackRouterTests` is the reason: it calls `CallbackCodec.Encode` directly
- [ ] `TelegramStubs.cs`'s two new mappings carry no `.AtPriority(...)`, matching `sendMessage`'s
      own existing mapping, since no test overrides either response
- [ ] Every new `WireMockFixture` payload record carries `[JsonExtensionData] Extra`, matching
      `SendMessagePayload`'s existing pattern, so `Assert.Equivalent(..., strict: true)` catches an
      unexpected wire field rather than silently dropping it
- [ ] `CallbackRouterTests`'s four test methods (five cases) each assert one business outcome; none
      duplicates another's assertion
- [ ] The backlog's F6 entry is corrected in the same commit that makes `CallbackRouter` not pair
      with `ICallbackHandler`, per `AGENTS.md`'s rule

**Whole feature, once all three commits land:**
- [ ] Every new public member has a three-line `<summary>`; every test summary is Gherkin
- [ ] Every class taking arguments uses a primary constructor
- [ ] No emoji anywhere, including all three commit messages and every reply string
- [ ] **No plan-internal decision citation inside any C# code block, doc comment, or commit
      message** — every fenced code block above was re-read for this before the plan was committed
- [ ] No `<see cref="...">` in any doc comment points at a type in a project that does not
      reference the type's own project — checked directly: `ITaskAction.cs`'s remarks name
      `DoneAction` and `CallbackCodec` as plain `<c>` code text, not `<see cref>`, because
      `Assistant.Interfaces` does not and must not reference `Assistant.Impl`
- [ ] Type and member names are consistent everywhere they appear: `ITaskAction.Key`,
      `CallbackCodec.Encode`/`TryDecode`, `CallbackRouter`, `MarkCompletedTaskAsync`,
      `"That button is no longer valid."`, `"Already done."`, `"I could not find that task."` each
      carry the identical spelling in every step, decision, and commit message that names them
- [ ] No placeholder text ships inside a source file
- [ ] Spec coverage: §5.1 (untouched, noted rather than silently skipped), §6.4 (the codec, the
      effects table's `Done` row, all three required behaviours), §6.5 (exceptions caught and
      logged by the existing dispatcher, not duplicated per-handler), §7.2 (the unit/integration
      split), §7.3 (assertion standard — count, recipient, exact text, on every wire assertion),
      §7.4 (idempotency and the extended non-owner scenario), §12.1/§12.5/§12.6 — every section
      this plan's brief named is addressed somewhere above
- [ ] This slice's diff stays under the 1000-line budget (Decision 8: ~789 estimated), and the
      table names exactly which two files (the two new test files) carry most of it
