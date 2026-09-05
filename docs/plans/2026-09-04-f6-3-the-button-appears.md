# F6-3 — the button appears

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F6-2 built every piece a tapped button needs — `ITaskAction`, `DoneAction`,
`TaskActions`/`TaskActionDefinition`, `CallbackCodec`, `CallbackRouter`, and
`INotifier.MarkCompletedTaskAsync` — except a button to tap. This slice attaches the first one:
`INotifier` grows `SendTaskAsync(Guid taskId, string text, CancellationToken ct)`,
`TelegramNotifier` implements it by rendering `TaskActions.All` as an `InlineKeyboardMarkup`, and
`DueReminderJob` calls it instead of `SendAsync`. The same `MarkCompletedTaskAsync` F6-2 added now
clears that keyboard explicitly on completion — sending no `reply_markup` instruction was correct
while nothing existed to clear (F6-2's own Decision 3), and that condition ends here. F6 is
tagged **observable** in the backlog; F6-1 and F6-2 could not meet that bar. This slice closes it.

> **Amendment (made during review):** the plan below prescribes rendering `TaskActions.All`
> through `.Select(...)`. The implementation instead constructs the single `Done` button
> directly — `TaskActions.All` has exactly one entry, so a `.Select` over it is machinery for a
> plurality that does not exist, and the `IEnumerable<InlineKeyboardButton>` constructor overload
> that a `.Select` would bind to silently commits every action to one row, a layout decision that
> properly belongs to F11 once it must also decide which actions a given reminder shows. The test
> that pins this behaviour never asserted "one button per catalogue entry" in the first place. The
> sections below still describe the original `.Select` form and are left as written, being the
> plan as reviewed.

**Tech Stack:** .NET 10 (`net10.0`), nullable enabled, warnings are errors — the existing stack.
This slice adds **no new NuGet package** — `Telegram.Bot` 22.10.2.1
(`Directory.Packages.props:29`) already exposes `InlineKeyboardMarkup`/`InlineKeyboardButton` in
`Telegram.Bot.Types.ReplyMarkups` — and **no new build-config line**: `SendTaskAsync` is a new
method on an interface `TelegramNotifier` already implements, and `TelegramNotifier` is already
`INotifier`'s sole registered implementation (`ImplServiceCollectionExtensions.cs:41`,
`services.AddSingleton<INotifier, TelegramNotifier>();`), so no `.csproj` change and no DI
registration change of any kind.

**Spec:** `docs/design/slice-1-reminders.md` §6.4 (inline buttons — the callback format and the
button/action/effect table this slice's keyboard renders from), §7.2 (unit vs. integration split),
§7.3 (assertion standard — count, recipient, exact text, exact buttons), §12.1 (XML docs), §12.5
(primary constructors), §12.6 (no emoji).
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F6's own entry (line 274,
`observable`, "Settled at F6-2" at line 283) and section 1 (YAGNI, Open/Closed, the 1000-line
budget, and the definition of done's "must also be demonstrable on a real phone" clause for
`observable` features, which this slice is what finally satisfies).
**Also read:** `docs/plans/2026-09-03-f6-2-route-the-tap.md` and
`docs/plans/2026-09-04-f6-2-action-catalogue.md` — the two prior plans on this feature, and the
format this plan matches.

---

## How F6 is sliced

- **F6-1, the column and the writer (merged, `bc0c9cc`).** `ReminderTask.CompletedAt`, its
  migration and check constraint, `ITaskService.CompleteAsync`. No Telegram, no buttons. Verified
  directly: `git log --oneline -1 bc0c9cc` reads `feat: F6-1 - the completed column and the writer
  (#23)`.
- **F6-2, the tap is routed and answered (merged, `7291a64`).** `ITaskAction` + `DoneAction`,
  `TaskActions`/`TaskActionDefinition` (the shared action catalogue, appended to the same pull
  request in review as two extra commits), `CallbackCodec`, `CallbackRouter` as a second
  `ITelegramUpdateHandler`, `INotifier.MarkCompletedTaskAsync`, and the per-update scope moved into
  `TelegramListener.DispatchAsync`. Verified directly: `git log --oneline -1 7291a64` reads `feat:
  F6-2 - the tap is routed and answered (#24)`. Nothing in F6-2 renders a button — it is the
  machinery a tap needs before an affordance to trigger it can exist.
- **F6-3 (this plan), the button appears.** `INotifier.SendTaskAsync`, `TelegramNotifier`'s
  implementation of it, `DueReminderJob` calling it instead of `SendAsync`, and
  `MarkCompletedTaskAsync` clearing the keyboard it can now assume might exist. This is the first
  commit anywhere in this repository's history that puts an inline keyboard on a Telegram message,
  and it closes F6: the backlog's `observable` tag is met here, not before.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error everywhere.
- **Every class taking arguments uses a primary constructor.** No separate constructors. Not
  exercised by new code this slice — see "What this slice does NOT include."
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions. `Assert.Equivalent(expected, actual, strict: true)` for wire payloads, matching
  every existing use in `TelegramNotifierTests`/`CallbackRouterTests`.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When`/`And`/`Then`), one clause per line.
- Central package management; no inline `Version=`. Not exercised this slice — no package changes.
- No emoji anywhere: source, tests, docs, commit messages, or bot message text (conventions §12.6).
- C# comments and `///` docs use plain ASCII double dashes `--`, never an em dash. This document's
  own prose uses real em dashes, matching the surrounding plan documents.
- **Never run `docker compose down -v`.** Use `docker compose -f compose.test.yaml down` with no
  other flags.
- **Never run `dotnet run --project src/Assistant.Worker` or any `send-test-message` command.**
  Both need real secrets and reach the owner's real phone; Decision 8's manual verification step is
  the owner's own to run, not an agent's.
- PR budget: 1000 changed lines per PR, excluding the plan. Decision 7 measures this slice at 205.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
  ```

---

## Verified facts this plan rests on

Every fact below states how it was checked: read from the working tree at `7291a64`, HEAD of this
branch, directly (`cat -n`, `grep -n`, `wc -l`); reflected off the installed `Telegram.Bot`
22.10.2.1 assembly; captured off a real `TelegramBotClient` pointed at a local `HttpListener`; or
measured by implementing this exact plan's Steps in a disposable `git worktree` off this branch's
HEAD, building it, running both suites to completion against it, and reading `git diff --numstat`
directly — the same discipline `docs/plans/2026-09-04-f6-2-action-catalogue.md`'s own "Verified
facts" section used. The worktree was removed immediately after; no file in the real working tree
was created or modified to produce this plan.

- **Fact A — a one-button keyboard on `sendMessage`.** `new InlineKeyboardMarkup(
  InlineKeyboardButton.WithCallbackData("Done", "v1:done:AAAA"))` passed as `replyMarkup` produces:
  ```json
  {"chat_id":100200300,"text":"call the bank","parse_mode":"Html","reply_markup":{"inline_keyboard":[[{"text":"Done","callback_data":"v1:done:AAAA"}]]}}
  ```
  `inline_keyboard` is an array of rows, each row an array of buttons; a button object carries
  exactly `text` and `callback_data`.
- **Fact B — omitting `replyMarkup` omits the key entirely.**
  ```json
  {"chat_id":100200300,"text":"plain","parse_mode":"Html"}
  ```
  There is no `"reply_markup":null`. This is why `SendMessagePayload`'s new field is nullable
  rather than required, and why the three existing `SendAsync` tests keep passing unmodified.
- **Fact C — the empty-keyboard trap.** Four constructions, four captured wire bodies:

  | Construction | `reply_markup` on the wire |
  | :--- | :--- |
  | `new InlineKeyboardMarkup()` | `{"inline_keyboard":[]}` |
  | `new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton>())` | `{"inline_keyboard":[[]]}` |
  | `new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>())` | `{"inline_keyboard":[]}` |
  | `new InlineKeyboardMarkup(new List<InlineKeyboardButton>())` | `{"inline_keyboard":[[]]}` |

  An empty *array of buttons* binds to the `InlineKeyboardButton[] inlineKeyboardRow` overload,
  which wraps it in a row — one empty row, `[[]]`, not an empty keyboard, `[]`. Telegram's Bot API
  treats `inline_keyboard: []` as "clear the keyboard"; `[[]]` is not that shape. The parameterless
  constructor is the correct one, and it is the one that reads *least* explicitly to a maintainer
  reaching for "no buttons" — the wrong one compiles silently and looks more deliberate.
- **Fact D — `InlineKeyboardMarkup.Empty` does not exist** in 22.10.2.1. Checked by reflection
  against the installed assembly; there is no such static property.
- **Fact E — the constructor and factory surface**, reflected directly off
  `~/.nuget/packages/telegram.bot/22.10.2.1/lib/net6.0/Telegram.Bot.dll`:
  ```
  Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup
    ctor(InlineKeyboardButton inlineKeyboardButton)
    ctor(InlineKeyboardButton[] inlineKeyboardRow)
    ctor(IEnumerable<InlineKeyboardButton> inlineKeyboardRow)
    ctor(List<InlineKeyboardButton> inlineKeyboardRow)
    ctor(IEnumerable<IEnumerable<InlineKeyboardButton>> inlineKeyboard)
    ctor()
  Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton
    static WithCallbackData(string textAndCallbackData)
    static WithCallbackData(string text, string callbackData)
    ... (other With* factories, not used here)
  ```
  `InlineKeyboardMarkup : ReplyMarkup` (confirmed by walking `.BaseType`), which is why passing an
  `InlineKeyboardMarkup` local to `SendMessage`'s `replyMarkup: ReplyMarkup? = null` parameter
  needs no cast.
- **Fact F — two buttons in one row.** `new InlineKeyboardMarkup(new[] { WithCallbackData("Done",
  ...), WithCallbackData("Snooze", ...) })` yields one row holding both — the same
  `IEnumerable<InlineKeyboardButton>` overload this slice's own `SendTaskAsync` uses via `.Select`
  over `TaskActions.All`. Relevant only as forward context for F11; F6-3 renders one button because
  `TaskActions.All` has exactly one entry today.
- **Fact G — `allowedUpdates` needs no work.** `TelegramListener.ExecuteAsync` computes
  `_allowedUpdates` from every registered `ITelegramUpdateHandler`'s `.Handles`.
  `CallbackRouter` (F6-2) already declares `UpdateType.CallbackQuery` and is already registered, so
  real Telegram already delivers callback queries on this branch. This slice adds nothing here.
- **Fact H — button text is not parsed.** `parse_mode` governs the message body, not a button's
  label: a button object is JSON-encoded as a plain string, not HTML-parsed, so `TaskActions.Done
  .Label` (`"Done"`) needs no escaping, and neither would a future label containing `&` or `<`.
- **The extension-method signatures** actually reflected off `Telegram.Bot.TelegramBotClientExtensions`:
  ```
  SendMessage(ITelegramBotClient, ChatId chatId, string text, ParseMode parseMode = None,
      ReplyParameters? replyParameters = null, ReplyMarkup? replyMarkup = null, ...,
      CancellationToken cancellationToken = null)
  EditMessageText(ITelegramBotClient, ChatId chatId, int messageId, string text,
      ParseMode parseMode = None, InlineKeyboardMarkup? replyMarkup = null, ...,
      CancellationToken cancellationToken = null)
  ```
  `replyMarkup` sits positionally right after `parseMode` on both, so
  `bot.EditMessageText(chatId, messageId, text, ParseMode.Html, NoButtons, cancellationToken: ct)`
  needs no named skip of `replyMarkup` itself, only of the optional parameters after it.
- **The three `SendAsync` call sites, verified directly and exhaustively (`grep -rn
  "SendAsync(" src/`):** `src/Assistant.Worker/Program.cs:17` (`await notifier.SendAsync("Assistant
  is configured and can reach you.", CancellationToken.None);`), `src/Assistant.Impl/Telegram/
  MessageHandler.cs:57` (`await notifier.SendAsync(reply, ct);`), and `src/Assistant.Impl/Services/
  Jobs/DueReminderJob.cs:33` (`await notifier.SendAsync(task.Title, ct);`, this slice's own target).
  No fourth call site exists anywhere in `src/`.
- **`ReminderTaskBuilder.BuildReminderTask`'s default title is `"call the bank"`**
  (`tests/Assistant.IntegrationTests/Infrastructure/ReminderTaskBuilder.cs:30`), reused by **27**
  call sites across the integration suite — verified directly: `grep -rn "BuildReminderTask(" tests/
  --include="*.cs"` filtered past `bin/` and `obj/` returns 28 matches, one of which is the method's
  own declaration. The unfiltered `grep -rn "BuildReminderTask(" tests/` returns 32, but four of
  those are generated XML doc files under `bin/` and `obj/` and are not call sites.
- **`Assistant.Impl.csproj` already grants `Assistant.IntegrationTests` internals access**
  (`src/Assistant.Impl/Assistant.Impl.csproj:23`, `<InternalsVisibleTo
  Include="Assistant.IntegrationTests" />`, added at F6-2) — confirmed by reading the file
  directly. This slice's new `DueReminderJobTests` fact calls the `internal` `CallbackCodec.Encode`
  without needing a `.csproj` change of its own.
- **The three `new SendMessagePayload(...)` sites**, verified directly (`grep -n "new
  SendMessagePayload(" tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`): lines
  **54**, **81**, **111** — exactly three, no others anywhere in the test suite.
- **The one `new EditMessageTextPayload(...)` site**, verified directly (`grep -n "new
  EditMessageTextPayload(" tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs`): line
  **101** — exactly one, no others anywhere in the test suite.
- **Baseline test counts, run directly against this branch's actual HEAD:** `dotnet test
  tests/Assistant.UnitTests` reports **51 passed**; `dotnet test tests/Assistant.IntegrationTests`
  (against `docker compose -f compose.test.yaml up -d`, no `--build` needed at baseline) reports
  **47 passed**. Matches the brief's stated figures exactly.
- **This plan's exact code was built and tested before being written down.** Every file change in
  the Steps below was applied inside a disposable `git worktree` off `7291a64`, built with `dotnet
  build -c Release` (zero warnings, zero errors, both after Commit 1's changes and after Commit 2's),
  and run. Result: `dotnet test tests/Assistant.UnitTests` **51 passed** throughout (this slice adds
  no unit test); `dotnet test tests/Assistant.IntegrationTests` **48 passed** after Commit 1, **49
  passed** after Commit 2. The two red-step failure messages quoted in the Steps below are the
  literal xUnit output captured from that same run, not reconstructed from memory.
- **Measured, not estimated, line cost** — the full table and its provenance are Decision 7.

---

## Inherited context: what this slice reads from earlier features

`TaskActions`/`TaskActionDefinition` (`src/Assistant.Contracts`, F6-2's action-catalogue addendum)
— `TelegramNotifier.SendTaskAsync` reads `TaskActions.All` and each entry's `Key`/`Label` to build
the keyboard; this slice adds no new catalogue entry. `CallbackCodec.Encode`
(`src/Assistant.Impl/Telegram/CallbackCodec.cs`, F6-2) — the exact encoder `SendTaskAsync` calls
per button, finally connected in a running message to the `CallbackCodec.TryDecode` call
`CallbackRouter.HandleAsync` already makes on a tap. `CallbackRouter` (F6-2) — unchanged by this
slice; its call to `notifier.MarkCompletedTaskAsync(messageId, messageText, ct)` needs no edit,
because the interface signature it calls against does not change, only `TelegramNotifier`'s
implementation of it. `MarkCompletedTaskAsync` itself (`INotifier`/`TelegramNotifier`, F6-2) — this
slice is the one both its own `<remarks>` blocks already named as the trigger to revisit it;
Decision 3 quotes both verbatim. `allowedUpdates` (`TelegramListener.ExecuteAsync`, F7/F6-2) —
already correct per Fact G; this slice adds nothing here. The `InternalsVisibleTo` grant to
`Assistant.IntegrationTests` (F6-2) — already in place; this slice's new integration fact calling
`CallbackCodec.Encode` relies on it without adding to it.

---

## Decisions

### 1. `INotifier` grows one method; `SendAsync` is not changed

**Decision:** `INotifier` gains `SendTaskAsync`. `SendAsync`'s own signature, implementation, and
every existing call site are untouched.

**Why not an optional parameter on `SendAsync`.** There are exactly three `SendAsync` call sites
(verified above): the startup probe (`Program.cs:17`), the AI reply (`MessageHandler.cs:57`), and
the reminder (`DueReminderJob.cs:33`, this slice's own target). **Only the third gets a button.**
An `IReadOnlyList<TaskActionDefinition>? actions = null` parameter added to `SendAsync` would
compile at the other two call sites unchanged, but it changes what `SendAsync` *is* — from
"delivers plain text" to "optionally delivers plain text with buttons" — for two callers that never
asked for that and have no test proving they still don't get one. A reader of
`MessageHandler.cs:57` six months from now, seeing `SendAsync` take an optional `actions`
parameter, has no way to tell from that call site alone whether omitting it is deliberate or an
oversight; a call to a method that simply does not exist for the AI reply leaves no such question.
Open/Closed says add a member over editing an existing one's contract; this is that choice applied
to a seam two other callers already share.

**Cost if this is wrong.** If a fourth caller ever needs buttons through `SendAsync` specifically —
unlikely, since every current and near-future caller either wants none (`Program.cs`,
`MessageHandler`) or wants the full catalogue (`DueReminderJob`, and F10's own capture-path reply
per the backlog's F10 entry) — the fix is a third `INotifier` method, not a change to `SendAsync`
or `SendTaskAsync`. Both stay exactly as they are.

**Alternative considered: a required `bool withButtons` parameter on `SendAsync`, reading
`TaskActions.All` internally when true.** Rejected for the same reason plus a new one: it forces
the two callers that want no buttons to say so explicitly at every call site, spreading a decision
that belongs in one place (which method to call) across three.

### 2. The new method's signature: `SendTaskAsync(Guid taskId, string text, CancellationToken ct)`

**Decision:** `Task SendTaskAsync(Guid taskId, string text, CancellationToken ct)`. The adapter
reads `TaskActions.All` internally and renders whatever its channel can; the caller supplies only
the task identifier and the plain-text body. Named for parallelism with the existing
`MarkCompletedTaskAsync` — both are `INotifier` methods about a specific task, one sending it, one
marking it done.

**The alternative to argue and reject: an explicit `IReadOnlyList<TaskActionDefinition> actions`
parameter, caller-selected.** Today there is exactly one caller (`DueReminderJob`) and it would
pass `TaskActions.All` every time — one caller, one value, which YAGNI forbids per the backlog's
own rule ("an abstraction with one implementation is a guess, not a seam"; the same reasoning
applies to a parameter that only ever carries one argument). The trigger that reopens this: the
first caller that needs a *subset* of the catalogue — plausibly F10's own capture-path reply, if
its keyboard ever needs to omit an action `DueReminderJob`'s does not. Until a second caller wants
a different set, there is nothing for the parameter to select between.

**The tension with `INotifier`'s own remarks, confronted rather than skated past.** Before this
slice, `INotifier`'s class-level `<remarks>` read: "The recipient is configuration, not a
parameter... Rendering is the caller's job — a notifier delivers text it is given and never sees a
database shape." A `Guid taskId` parameter is in real tension with that last sentence — it is
unambiguously something read from a database. The argument for why it is nonetheless right: that
sentence is about *text* rendering — the message body — not about every parameter an interface
might ever take. A task identifier is not text needing rendering; it is the channel-neutral handle
an adapter needs to build *any* affordance at all, the same way `MarkCompletedTaskAsync`'s existing
`int messageId` parameter is already a database-adjacent handle nobody objected to at F6-2, because
the alternative is strictly worse: a caller pre-encoding Telegram's own `v1:done:...` wire format
and passing that string in would leak one channel's callback protocol into an interface every
future channel must also implement, defeating the entire point of `INotifier` being channel-neutral.
**This slice does amend the `<remarks>`** — the old sentence no longer describes the interface
accurately once a `Guid` parameter sits three lines below it. The new text (Commit 2, below) keeps
"rendering is the caller's job" for the message *body* while stating plainly why a task identifier
is a different kind of thing.

**Naming alternatives considered and rejected.** `SendActionableTaskAsync` — accurate, but
"actionable" duplicates what the method's own `<remarks>` already say (every action in
`TaskActions.All` is attached); a name should not need to repeat its doc comment to be understood.
`SendTaskWithActionsAsync` — accurate and marginally more explicit, but longer than `SendTaskAsync`
for no reader benefit once the `<summary>`'s own first line says "with every action from the shared
catalogue attached as a button." `SendTaskAsync` wins on the same "shortest name that does not
mislead" standard `MarkCompletedTaskAsync` was chosen under at F6-2.

### 3. `MarkCompletedTaskAsync` sends `new InlineKeyboardMarkup()`; the parameterless constructor is load-bearing

**Decision:** `TelegramNotifier` gains a private static field,

```csharp
// new InlineKeyboardMarkup([]) is the wrong empty keyboard: an empty array of buttons binds
// to the constructor overload that wraps it in one row, producing {"inline_keyboard":[[]]}
// on the wire -- one empty row, not an empty keyboard. Only the parameterless constructor
// produces {"inline_keyboard":[]}, the shape Telegram treats as "no keyboard."
private static readonly InlineKeyboardMarkup NoButtons = new();
```

and `MarkCompletedTaskAsync` passes it as the `replyMarkup` argument on every edit,
unconditionally — not only when a keyboard is known to exist.

**Why the parameterless constructor, and why this is not obvious.** Fact C is the reason this gets
its own decision rather than a one-line implementation note. An empty *button array* binds to the
`InlineKeyboardButton[] inlineKeyboardRow` overload — the name says "row," singular — which wraps
whatever it is given in one row; an empty array is still one (empty) row, so the wire carries
`[[]]`, a keyboard with one empty row, not the absence of a keyboard. Telegram's Bot API documents
`inline_keyboard: []` as the shape that clears one; `[[]]` is not that shape and is not treated as
one. The parameterless constructor is the only reflected overload (Fact E) that produces `[]`
directly, with nothing fed to it that could be mistaken for "empty." This is worth spelling out
because the *wrong* construction — `new InlineKeyboardMarkup([])`, reaching for "pass an empty
collection" as the obvious way to say "no buttons" — compiles without a warning and reads *more*
explicitly to a maintainer who has not read this decision. `InlineKeyboardMarkup.Empty` does not
exist in this package version (Fact D); there is no static shortcut to reach for instead.

**Why unconditionally, not only when a keyboard might exist.** F6-2's own Decision 3 argued the
opposite for that slice's moment: sending no `reply_markup` instruction was correct *then* because
nothing in the codebase attached a keyboard to any message yet, so there was nothing to clear and
no test could exercise clearing it. That condition ends with this slice — every reminder
`DueReminderJob` sends now carries a Done button (Decision 2), so every message
`MarkCompletedTaskAsync` might edit could have one. Checking "does this message actually have a
keyboard" before deciding whether to send `NoButtons` would require `CallbackRouter` to track or
query state it has no way to reach, for no benefit: sending an empty keyboard to a message that
never had one is harmless, and the edit's text always changes regardless — `<s>...</s>` is added
every time — so Telegram's "message is not modified" 400 is not reachable from this call site
under any input this slice's tests construct.

**Both call sites' obligations, discharged in the same commit, not left promising future work.**
F6-2 wrote this exact obligation into two `<remarks>` blocks. `INotifier.MarkCompletedTaskAsync`
said, verbatim:

> Sends no keyboard instruction, so whatever inline keyboard the message already carries, if
> any, is left exactly as it is -- there is nothing to clear yet, because nothing in this
> codebase attaches a keyboard to a message before this method might edit it. F6-3, which
> attaches the first one, must revisit this call to pass an explicit empty keyboard, or a
> completed reminder keeps its dead Done button visible under a message that already shows
> the task as done.

and `TelegramNotifier.MarkCompletedTaskAsync` said, verbatim:

> Renders completion by wrapping the escaped text in an inline &lt;s&gt; element -- this
> adapter's own choice of how to show completion, not part of the interface's contract. F6-3
> must revisit this call once it attaches the first inline keyboard, or a completed reminder
> keeps a dead Done button visible under the struck-through title.

Both are rewritten in Commit 1, below, to state the actual behaviour rather than continuing to
promise it — leaving either unedited after this slice merges would mean a document keeps claiming
undone work that is now done, the same discipline `docs/tech-debt.md`'s own "Resolved at F6-2"
note already followed for a different obligation.

### 4. The WireMock payload records grow a nested `reply_markup` shape

**Decision:** two new records, matching the existing `AiRequestPayload` → `AiMessagePayload` →
`AiToolPayload` → `AiFunctionPayload` nesting precedent already in the same file:

```csharp
public sealed record ReplyMarkupPayload(
    [property: JsonPropertyName("inline_keyboard")] IReadOnlyList<IReadOnlyList<InlineButtonPayload>> InlineKeyboard)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed record InlineButtonPayload(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("callback_data")] string CallbackData)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
```

(full XML docs in Commit 1, below). `SendMessagePayload` and `EditMessageTextPayload` each gain a
trailing `ReplyMarkupPayload? ReplyMarkup` parameter — nullable, because Fact B shows the wire key
is *absent*, not `null`, when no keyboard is sent, and `System.Text.Json` deserialises a missing
key to a property's default, which for a nullable reference type is `null` — the same implicit
behaviour `SendMessagePayload`'s existing three fields already rely on.

**The exact churn, verified by actually implementing this change and rebuilding, not estimated:**
- `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs` — three `new
  SendMessagePayload(...)` sites, at lines 54, 81, 111, each gain a trailing `null`.
- `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs` — the one `new
  EditMessageTextPayload(...)` site, at line 101, gains the **empty-keyboard** expectation, `new
  ReplyMarkupPayload([])`, not `null` — Decision 3 makes the edit start carrying one.

**Rejected alternative: a raw `JsonElement?`.** Keeps the record churn at zero — `SendMessagePayload`
and `EditMessageTextPayload` would each gain one untyped field instead of a typed nested record —
but every assertion that needs to inspect a button becomes a hand-rolled `JsonElement` walk
(`.GetProperty("inline_keyboard")[0][0].GetProperty("text").GetString()`), and `Assert.Equivalent`
cannot compare two `JsonElement` trees the way it compares two records field by field — the entire
reason every other nested payload in this file (`AiRequestPayload` and its three nested types) is
already typed rather than left as `JsonElement`. Two more typed records, following a pattern
already used three levels deep in the same file, costs less than the first `JsonElement` walk
would.

### 5. Which tests prove this, and at what level

**Decision:** four business behaviours, each proven at the integration level, because every one of
them is observable only on the wire:

1. **A due reminder reaches the owner carrying a Done button whose callback data is the one the
   router will decode for that task.** `DueReminderJobTests` gains
   `RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask`, asserting
   `CallbackCodec.Encode(TaskActions.Done.Key, task.Id)` equals the sent button's `callback_data`
   and `TaskActions.Done.Label` equals its `text`. This is the one assertion in the whole slice
   that ties the encoder `SendTaskAsync` calls to the decoder `CallbackRouter` already calls —
   without it, the two could silently drift (an encoding bug that still produced *some* string
   would pass every other test in this slice, since nothing else round-trips it).
2. **Completing a task removes the button.** `CallbackRouterTests`'s existing
   `Listener_OwnerTapsDone_CompletesTheTaskAndStrikesThroughTheMessage` already asserts the edit's
   full body with `Assert.Equivalent(strict: true)`; its expectation grows the empty-keyboard field
   (Decision 4). No new test method — the existing one already exercises the exact code path this
   slice changes, and strict equivalence means it cannot silently ignore a wrong keyboard shape
   once the expectation names one.
3. **The startup message and the AI reply still carry no keyboard.** Already proven: the three
   existing `SendAsync` tests in `TelegramNotifierTests` use `strict: true` against a `null`
   `ReplyMarkup`, and `SendAsync` itself is untouched (Decision 1) — asserting this again would be
   the exact "two places to update, no extra confidence" duplication spec §7.2 forbids.
4. **`MarkCompletedTaskAsync`'s escaping** — Decision 6, below.

**Why integration, not unit, for all four.** Every one has an observable effect only a real wire
request can show — a specific JSON shape reaching the stub, or, for behaviour 3, the deliberate
*absence* of a field. None of spec §7.2's unit-test carve-outs (combinatorial tables, pure mapper
round-trips, rules with no observable side effect) applies: `SendTaskAsync` is not a pure function
(it calls `ITelegramBotClient`), and every rule it and `MarkCompletedTaskAsync` enforce is visible
only on the wire.

### 6. `MarkCompletedTaskAsync` gets its first direct escaping test, here

**Decision:** one new `[Fact]` in `TelegramNotifierTests`,
`MarkCompletedTaskAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder`, following
the existing `SendAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder` precedent
exactly, including its `<remarks>` discipline of hand-deriving the expected string rather than
running the same `Replace` chain the production code uses.

**The history, stated as history, not invented.** An external review of PR #24 found that
`INotifier.MarkCompletedTaskAsync` has no direct test in `TelegramNotifierTests`. Its only
exercise is through `CallbackRouterTests`, which always passes the shared builder's title, `"call
the bank"` — escape-invariant text, since none of `&`, `<`, `>` appear in it, so every
`CallbackRouterTests` assertion against an edited message proves nothing about whether `Escape`
runs correctly, only that *some* text arrives. `TelegramNotifierTests` covers `SendAsync`'s
escaping thoroughly (ampersand-before-angle-bracket ordering, Hebrew pass-through) and
`MarkCompletedTaskAsync` not at all. The finding was accepted and deferred to F6-3 on the ground
that F6-3 must reopen this exact method anyway (Decision 3); that deferral is now due.

**Why `ReminderTaskBuilder`'s own default title is not the fix.** Changing `Title = "call the
bank"` (line 30) to something escape-sensitive was considered and rejected: that literal feeds 27
`BuildReminderTask(` call sites across the integration suite, the overwhelming majority of which
have nothing to do with escaping and would gain an irrelevant, harder-to-read literal for no
reason. A shared test fixture's default should stay boring; a test that specifically needs
escape-sensitive text supplies its own, the same way
`SendAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder` already does with a
`const string text` local rather than touching the shared builder.

**Whether the empty keyboard is asserted in the same test or a separate one.** Same test. This
project's "one thing per test" rule is about the *behaviour* under test, and this test's behaviour
is escaping — but `Assert.Equivalent(strict: true)` necessarily compares the *whole*
`EditMessageTextPayload`, the same way `SendAsync_TextContainsAngleBracketsAndAmpersand_...`
already implicitly re-asserts `ChatId` and `ParseMode` alongside the escaping it is really about,
because that is what a strict wire-shape assertion is in this codebase. Splitting the empty
keyboard into a second test would mean a second call to `MarkCompletedTaskAsync` whose only reason
to exist is satisfying `strict: true`'s own comparison, proving nothing `CallbackRouterTests`'s
`Listener_OwnerTapsDone_...` test (Decision 5, behaviour 2) does not already prove about the empty
keyboard specifically, through a real tap rather than a direct call. The new test's own `<remarks>`
says this explicitly, so a reader does not conclude the empty-keyboard assertion here is this
test's real subject.

### 7. Does this fit one pull request?

**The arithmetic, measured, not estimated.** Every line below came from actually implementing this
plan's exact Steps in a disposable `git worktree` off this branch's HEAD (`7291a64`), building it
(`dotnet build -c Release` — zero warnings, zero errors after each commit's changes), running both
suites to green (51 unit, 49 integration final — matches Decision 5's own arithmetic and the
Validation section below), and reading `git diff --numstat` directly. The worktree was removed
immediately after; nothing in the real working tree was touched to produce this plan.

| File | Change | Added | Deleted |
| :--- | :--- | ---: | ---: |
| `src/Assistant.Interfaces/INotifier.cs` | modify | 31 | 8 |
| `src/Assistant.Impl/Telegram/TelegramNotifier.cs` | modify | 32 | 4 |
| `src/Assistant.Impl/Services/Jobs/DueReminderJob.cs` | modify | 1 | 1 |
| `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs` | modify | 41 | 3 |
| `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs` | modify | 27 | 0 |
| `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs` | modify | 2 | 1 |
| `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs` | modify | 32 | 3 |
| `docs/design/2026-08-22-slice-1-feature-backlog.md` | modify | 9 | 1 |
| `docs/e2e-local.md` | modify | 9 | 0 |
| **Total** | | **184** | **21** |

**205 changed lines**, against the 1000-line budget — 795 lines of headroom, well below the
orchestrator's own rough 250-300 estimate. The gap is not because the scope shrank; `git diff`'s
line-matching found smaller true edits than a naive before/after block count would, since most of
`INotifier.cs` and `TelegramNotifier.cs` (imports, unrelated methods, the `Escape` helper) is
untouched. This is smaller than every prior F6 sub-slice's own estimate (F6-1: 264; F6-2: 789
measured; F6-2's action-catalogue addendum: 135 measured against its own, much smaller, remaining
budget) and below every whole-feature estimate in this repository except F2's own roughly 150
lines (backlog §2). No split is proposed or needed.

### 8. F6 closes, and what "observable" costs

**Decision:** F6 is tagged **observable** in the backlog (line 274), and section 1's definition of
done says an observable feature "must also be demonstrable on a real phone." F6-1 and F6-2 could
not meet that — F6-1 shipped a column and a writer with no Telegram surface at all, and F6-2's own
"What this slice can and cannot show on a real phone" section says plainly, "Nothing — there is
still no button anywhere a phone could tap." F6-3 can, and this decision is what discharges it.

**`docs/e2e-local.md` is extended in this slice.** Recommended, and done: its existing "Walkthrough
against real Telegram" section (line 216) already walks the owner through seeding a due row against
a real bot and a real chat id and watching the reminder arrive; this slice appends the one
paragraph that follows naturally from where that section already ends — tap the button, watch the
title strike through and the button disappear. The addition is written in that section's own voice
(see the diff in Commit 2, below) as the walkthrough's own next and final step, not a separate
procedure bolted on.

**This step belongs to the owner. No agent runs it.** `dotnet run --project src/Assistant.Worker`
against real Telegram, and any `send-test-message` invocation, both need a real bot token and send
a real message to the owner's own phone — this plan's own Global Constraints repeat the same
restriction this plan itself was written under, and it applies exactly as much to whoever executes
these Steps as it did to writing this plan. Everything this plan's own test suite proves (Decision
5) is proof of wire shape; only a tap on a real phone proves Telegram's client actually renders
one, which no `WireMockFixture` assertion can stand in for.

**The backlog's F6 entry is marked closed, in the same commit that closes it.** `**F6 · Complete a
task from a button · observable** — spec §6.4` gains a trailing `· **done**`, matching the exact
convention F5b's own header already uses (`**F5b · The scheduler fires due reminders · observable**
— spec §6.1, §6.2 · **done**`, verified directly), and a new "*Settled at F6-3:*" block is appended
after the existing "*Settled at F6-2:*" one, stating the button now exists and naming
`docs/e2e-local.md`'s new paragraph as the owner's own proof. Per `AGENTS.md`'s own rule — "Read
the relevant section before any structural change, and update it in the same commit if the change
alters a documented decision" — this happens in Commit 2, the same commit that makes the claim
true.

---

## What this slice does NOT include

- **Snooze, Tomorrow, or Edit buttons.** `TaskActions.All` has exactly one entry, `Done`;
  `SnoozeAction`, `RescheduleAction`, `EditAction` and their catalogue entries all arrive at F11
  (`2026-08-22-slice-1-feature-backlog.md:596`). `SendTaskAsync` renders whatever `TaskActions.All`
  contains, so F11 adds buttons here by adding catalogue entries, not by touching this slice's
  code.
- **A keyboard on the AI reply.** `MessageHandler.cs:57`'s call to `SendAsync` is untouched
  (Decision 1); F10's own capture-path reply is a different feature, per the backlog's F10 entry.
- **A keyboard on the startup message.** `Program.cs:17`'s call to `SendAsync` is untouched
  (Decision 1).
- **The daily brief.** `DailyBriefJob` does not exist yet (spec §6.3, deferred at F5b) and is out
  of scope regardless.
- **Multi-row button layout.** `SendTaskAsync` builds one row from `TaskActions.All` (`.Select(...)`
  over an `IEnumerable<InlineKeyboardButton>`, Fact E's third constructor overload) — correct today
  because there is one entry, and still correct once F11 adds three more, since spec §6.4's own
  table gives no indication buttons should split across rows.
- **The `:<arg>` codec segment.** `CallbackCodec.TryDecode` still accepts exactly three
  colon-separated segments (F6-2's own Decision 4); this slice's one button, `Done`, needs no
  argument, so nothing here reopens that decision.
- **Any `.csproj` or DI registration change.** `TelegramNotifier` already implements `INotifier`
  and is already registered as its sole implementation; adding an interface method needs neither.

---

## File Structure

```
src/Assistant.Interfaces/
    INotifier.cs                              + SendTaskAsync, remarks rewritten   (Commit 2)
                                               MarkCompletedTaskAsync remarks rewritten (Commit 1)

src/Assistant.Impl/
    Telegram/TelegramNotifier.cs              + NoButtons field                    (Commit 1)
                                               MarkCompletedTaskAsync sends NoButtons (Commit 1)
                                               + SendTaskAsync                      (Commit 2)
    Services/Jobs/DueReminderJob.cs           calls SendTaskAsync, not SendAsync   (Commit 2)

tests/Assistant.IntegrationTests/
    Infrastructure/WireMockFixture.cs         + ReplyMarkupPayload, InlineButtonPayload,
                                               EditMessageTextPayload's new field   (Commit 1)
                                               + SendMessagePayload's new field     (Commit 2)
    Telegram/CallbackRouterTests.cs           edit expectation carries empty keyboard (Commit 1)
    Telegram/TelegramNotifierTests.cs         + MarkCompletedTaskAsync escaping test (Commit 1)
                                               3 call sites gain a trailing null    (Commit 2)
    Jobs/DueReminderJobTests.cs                + button-attachment test             (Commit 2)

docs/
    e2e-local.md                              + real-phone Done-button step        (Commit 2)
    design/2026-08-22-slice-1-feature-backlog.md
                                               F6 entry marked done                 (Commit 2)
```

`src/Assistant.Impl/Telegram/CallbackRouter.cs`, `src/Assistant.Interfaces/ITaskAction.cs`, and
`src/Assistant.Contracts/TaskActions.cs` are absent from this list, deliberately — this slice reads
all three without changing them (see "Inherited context").

---

## Validation

**Test count arithmetic.** Baseline, run directly against this branch's actual HEAD: **51 unit, 47
integration** (see "Verified facts").

- Commit 1 adds one `[Fact]` to `TelegramNotifierTests`
  (`MarkCompletedTaskAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder`) and
  touches no unit test file. Integration: 47 + 1 = **48** after Commit 1. Unit stays **51**.
- Commit 2 adds one `[Fact]` to `DueReminderJobTests`
  (`RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask`) and touches no unit test file.
  Integration: 48 + 1 = **49** after Commit 2. Unit stays **51**.

Expected final state: **51 unit, 49 integration.** Verified directly, not only projected: both
commits were implemented in the disposable worktree behind Decision 7, and both suites were
actually run to completion at each stage — `dotnet test tests/Assistant.UnitTests` reported **51
passed** throughout; `dotnet test tests/Assistant.IntegrationTests` reported **48 passed** after
Commit 1's changes and **49 passed** after Commit 2's, zero failures at either point.

**Split between `Assistant.UnitTests` and `Assistant.IntegrationTests`.** Neither new test is a
unit test, for the reasoning Decision 5 gives in full: every behaviour this slice adds is
observable only on the wire.

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests

docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

No `--build` is needed at any point: `tests/Assistant.WireMock/TelegramStubs.cs` is untouched by
this slice — its existing `/bot*/sendMessage` and `/bot*/editMessageText` mappings already answer
both wire calls this slice's code makes — and `compose.test.yaml`'s `wiremock` service only needs
rebuilding when that project's own source changes.

**What this slice can and cannot show without a phone.** Everything except the one thing spec
§6.4's own "observable" bar actually asks for: that Telegram's own client renders a tappable button
and reacts to a tap. The test suite above proves every wire shape byte for byte — the button's
label, its callback data, the empty keyboard on completion — but proving Telegram itself draws one
needs Decision 8's manual step, which is the owner's own to run.

---

## Steps

**Decisions this slice carries:** 1-8, given in full above.

**Consumes:** `ITaskAction`, `TaskActions`/`TaskActionDefinition`, `CallbackCodec.Encode`,
`CallbackRouter`, `INotifier.MarkCompletedTaskAsync`/`TelegramNotifier`'s implementation of it (all
F6-2), `WireMockFixture`/`PostgresFixture`/`IntegrationCollection`/`ReminderTaskBuilder` (F1-F7 test
infrastructure), `DueReminderJob` (F5b).
**Produces:** `INotifier.SendTaskAsync`, `TelegramNotifier`'s implementation of it, the explicit
empty keyboard on completion, and the WireMock/test growth needed to observe all of it.

Two commits. Commit 1 makes `MarkCompletedTaskAsync` clear the keyboard it can now assume might
exist — the half of this slice that touches an existing call path (`CallbackRouter`'s edit) and
gets its first direct escaping test in the same breath. Commit 2 makes a fresh reminder carry the
button in the first place, and closes F6.

### Commit 1: `MarkCompletedTaskAsync` clears the keyboard explicitly

**Files:**
- Modify: `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`
- Modify: `src/Assistant.Interfaces/INotifier.cs`
- Modify: `src/Assistant.Impl/Telegram/TelegramNotifier.cs`

- [ ] **Step 1: Grow `WireMockFixture.cs`'s payload vocabulary**

In `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`, give
`EditMessageTextPayload` a fifth field (its `Extra` property is unchanged):

Before:

```csharp
/// <param name="ParseMode">How Telegram is told to interpret the text.</param>
public sealed record EditMessageTextPayload(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("message_id")] int MessageId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parse_mode")] string ParseMode)
```

After:

```csharp
/// <param name="ParseMode">How Telegram is told to interpret the text.</param>
/// <param name="ReplyMarkup">The keyboard attached to the edit, or null when none was sent.</param>
public sealed record EditMessageTextPayload(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("message_id")] int MessageId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parse_mode")] string ParseMode,
    [property: JsonPropertyName("reply_markup")] ReplyMarkupPayload? ReplyMarkup)
```

Then append two brand-new records directly after `EditMessageTextPayload`'s closing brace:

```csharp

/// <summary>
/// The <c>reply_markup</c> object carried on a <c>sendMessage</c> or <c>editMessageText</c> request.
/// </summary>
/// <param name="InlineKeyboard">
/// Every row of buttons, outer list first -- empty when the message carries no buttons at all.
/// </param>
public sealed record ReplyMarkupPayload(
    [property: JsonPropertyName("inline_keyboard")] IReadOnlyList<IReadOnlyList<InlineButtonPayload>> InlineKeyboard)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>Null when the object carried exactly the named field.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// One inline button within a captured <c>reply_markup</c>.
/// </summary>
/// <param name="Text">The label shown on the button.</param>
/// <param name="CallbackData">The callback data carried on a tap.</param>
public sealed record InlineButtonPayload(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("callback_data")] string CallbackData)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>Null when the button carried exactly the two named fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
```

This step alone changes no behaviour and proves nothing by itself — it is scaffolding the next two
steps' failing tests need to exist at all.

- [ ] **Step 2: Update `CallbackRouterTests.cs`'s edit expectation**

In `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs`, line 101:

Before:

```csharp
        var expectedEdit = new EditMessageTextPayload(OwnerChatId, MessageId, $"<s>{task.Title}</s>", "Html");
```

After:

```csharp
        var expectedEdit = new EditMessageTextPayload(
            OwnerChatId, MessageId, $"<s>{task.Title}</s>", "Html", new ReplyMarkupPayload([]));
```

- [ ] **Step 3: Add `MarkCompletedTaskAsync`'s first direct escaping test**

In `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`, append after
`SendAsync_HebrewText_PassesThroughByteForByte`'s closing brace, before the class's own closing
brace:

```csharp

    /// <summary>
    /// When a previously sent message is marked complete
    /// And its title contains "&amp;", "&lt;" and "&gt;"
    /// Then the edit escapes all three in order inside the struck-through wrapper.
    /// </summary>
    /// <remarks>
    /// The expected string below was worked out by hand, the same discipline
    /// <see cref="SendAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder"/> already
    /// applies. The expected <see cref="ReplyMarkupPayload"/> is asserted here only because
    /// <c>strict: true</c> compares the whole payload -- this test's own subject is escaping, not
    /// the empty keyboard, which <c>CallbackRouterTests</c> already proves through a real tap.
    /// </remarks>
    [Fact]
    public async Task MarkCompletedTaskAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder()
    {
        // Arrange
        const int messageId = 42;
        const string text = "Meet R&D <at 5> & confirm";
        var expected = new EditMessageTextPayload(
            OwnerChatId, messageId, "<s>Meet R&amp;D &lt;at 5&gt; &amp; confirm</s>", "Html",
            new ReplyMarkupPayload([]));

        // Act
        await _sut.MarkCompletedTaskAsync(messageId, text, CancellationToken.None);

        // Assert
        Assert.Equivalent(expected, Assert.Single(await wireMock.EditedMessagesAsync()), strict: true);
    }
```

- [ ] **Step 4: Build, then run, and watch it fail with the specific expected message**

```bash
dotnet build --no-restore
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests --filter \
  "FullyQualifiedName~CallbackRouterTests|FullyQualifiedName~MarkCompletedTaskAsync"
```

The build succeeds — `TelegramNotifier.MarkCompletedTaskAsync` still compiles; it simply omits
`replyMarkup`, a legal, defaulted argument. The run does not: **2 failed, 7 passed.** Both
failures carry the identical message, since both exercise the same untouched production code:

```
Assert.Equivalent() Failure: Mismatched value on member 'ReplyMarkup'
Expected: ReplyMarkupPayload { InlineKeyboard = System.Collections.Generic.IReadOnlyList`1[Assistant.IntegrationTests.Infrastructure.InlineButtonPayload][], Extra =  }
Actual:   null
```

on both `CallbackRouterTests.Listener_OwnerTapsDone_CompletesTheTaskAndStrikesThroughTheMessage`
and `TelegramNotifierTests.MarkCompletedTaskAsync_TextContainsAngleBracketsAndAmpersand_
EscapesAllThreeInOrder`. Expected and specific: the wire body carries no `reply_markup` at all
yet, so `ReplyMarkup` deserialises to `null` against an expectation that now requires one.

- [ ] **Step 5: Make `TelegramNotifier.MarkCompletedTaskAsync` send the empty keyboard**

In `src/Assistant.Impl/Telegram/TelegramNotifier.cs`, add the missing `using` after
`Telegram.Bot.Types.Enums`:

```csharp
using Telegram.Bot.Types.ReplyMarkups;
```

Insert the new field directly above `SendAsync`:

```csharp
    // new InlineKeyboardMarkup([]) is the wrong empty keyboard: an empty array of buttons binds
    // to the constructor overload that wraps it in one row, producing {"inline_keyboard":[[]]}
    // on the wire -- one empty row, not an empty keyboard. Only the parameterless constructor
    // produces {"inline_keyboard":[]}, the shape Telegram treats as "no keyboard."
    private static readonly InlineKeyboardMarkup NoButtons = new();

```

Then replace `MarkCompletedTaskAsync`'s `<remarks>` and body:

Before:

```csharp
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

After:

```csharp
    /// <inheritdoc/>
    /// <remarks>
    /// Renders completion by wrapping the escaped text in an inline &lt;s&gt; element -- this
    /// adapter's own choice of how to show completion, not part of the interface's contract. The
    /// edit also sends <see cref="NoButtons"/>, an explicit empty keyboard, so a completed
    /// reminder does not keep a dead Done button visible under its struck-through title.
    /// </remarks>
    public async Task MarkCompletedTaskAsync(int messageId, string text, CancellationToken ct) =>
        await bot.EditMessageText(
            settings.OwnerChatId, messageId, $"<s>{Escape(text)}</s>", ParseMode.Html, NoButtons,
            cancellationToken: ct);
```

- [ ] **Step 6: Rewrite `INotifier.MarkCompletedTaskAsync`'s own `<remarks>`**

In `src/Assistant.Interfaces/INotifier.cs`:

Before:

```csharp
    /// <remarks>
    /// Sends no keyboard instruction, so whatever inline keyboard the message already carries, if
    /// any, is left exactly as it is -- there is nothing to clear yet, because nothing in this
    /// codebase attaches a keyboard to a message before this method might edit it. F6-3, which
    /// attaches the first one, must revisit this call to pass an explicit empty keyboard, or a
    /// completed reminder keeps its dead Done button visible under a message that already shows
    /// the task as done.
    /// </remarks>
```

After:

```csharp
    /// <remarks>
    /// Sends an explicit empty keyboard, clearing whatever inline keyboard the message already
    /// carries -- see <c>TelegramNotifier.MarkCompletedTaskAsync</c> for why an empty keyboard is
    /// not simply omitting the argument.
    /// </remarks>
```

- [ ] **Step 7: Build, run, watch it pass**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --filter \
  "FullyQualifiedName~CallbackRouterTests|FullyQualifiedName~MarkCompletedTaskAsync"
```

Expected: zero warnings; **9 passed, 0 failed** — the same nine cases Step 4 ran, now all green.

- [ ] **Step 8: Run the whole suite**

```bash
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down
```

Expected: unit tests **51 passed**, unchanged from baseline — this commit touches no unit test
file. Integration tests **48 passed** (47 + 1, see "Test count arithmetic").

- [ ] **Step 9: Commit**

```bash
git add src/Assistant.Interfaces/INotifier.cs \
        src/Assistant.Impl/Telegram/TelegramNotifier.cs \
        tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs \
        tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs \
        tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs
git commit
```

Message:

```
fix: MarkCompletedTaskAsync clears the keyboard explicitly

TelegramNotifier.MarkCompletedTaskAsync now sends an explicit empty
InlineKeyboardMarkup on every edit, via a private static NoButtons
field built with the parameterless constructor -- new
InlineKeyboardMarkup([]) is the wrong one: an empty array of buttons
binds to the row-wrapping overload and produces
{"inline_keyboard":[[]]} on the wire, one empty row, not the
{"inline_keyboard":[]} shape Telegram treats as "no keyboard."
Verified directly against Telegram.Bot 22.10.2.1 with a local HTTP
listener capturing request bodies for all four plausible
constructions.

Nothing in this codebase has attached a keyboard to a message before
this commit, so this edit is currently harmless -- but it discharges
the obligation both INotifier.MarkCompletedTaskAsync's and
TelegramNotifier.MarkCompletedTaskAsync's own <remarks> already
named: F6-3 must revisit this call once it attaches the first
keyboard, or a completed reminder keeps a dead Done button under its
struck-through title. Both remarks are rewritten here to state the
actual behaviour instead of still promising it.

MarkCompletedTaskAsync also gets its first direct escaping test in
TelegramNotifierTests -- previously exercised only through
CallbackRouterTests, whose shared "call the bank" title is
escape-invariant and proved nothing about the Escape call. Finding
from external review of PR #24, deferred to this slice on the
ground that F6-3 must reopen this exact method anyway.

The WireMock payload records grow a nested reply_markup shape
(ReplyMarkupPayload, InlineButtonPayload), matching the existing
AiRequestPayload nesting precedent, with JsonExtensionData at every
level so Assert.Equivalent(strict: true) keeps catching an unnamed
field.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

### Commit 2: `SendTaskAsync` attaches the Done button

**Files:**
- Modify: `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs`
- Modify: `src/Assistant.Interfaces/INotifier.cs`
- Modify: `src/Assistant.Impl/Telegram/TelegramNotifier.cs`
- Modify: `src/Assistant.Impl/Services/Jobs/DueReminderJob.cs`
- Modify: `docs/e2e-local.md`
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`

- [ ] **Step 1: Grow `SendMessagePayload`**

In `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`, give it a fourth field
(its `Extra` property's `<value>` text changes from "three" to "four"; the property itself does
not otherwise change):

Before:

```csharp
/// <param name="ParseMode">How Telegram is told to interpret the text.</param>
public sealed record SendMessagePayload(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parse_mode")] string ParseMode)
```

After:

```csharp
/// <param name="ParseMode">How Telegram is told to interpret the text.</param>
/// <param name="ReplyMarkup">The keyboard attached, or null when the request carried none.</param>
public sealed record SendMessagePayload(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parse_mode")] string ParseMode,
    [property: JsonPropertyName("reply_markup")] ReplyMarkupPayload? ReplyMarkup)
```

And inside the record body, change "three" to "four":

```csharp
    /// Null when the request carried exactly the four expected fields. Populated otherwise, which
```

This alone breaks the build: three existing `new SendMessagePayload(...)` calls in
`TelegramNotifierTests.cs` now miss a required constructor argument.

- [ ] **Step 2: Give the three existing sites their trailing `null`**

In `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`:

| Line | Before | After |
| :--- | :--- | :--- |
| 54, 111 (identical) | `new SendMessagePayload(OwnerChatId, text, "Html");` | `new SendMessagePayload(OwnerChatId, text, "Html", null);` |
| 81 | `new SendMessagePayload(OwnerChatId, "Meet R&amp;D &lt;at 5&gt; &amp; confirm", "Html");` | `new SendMessagePayload(OwnerChatId, "Meet R&amp;D &lt;at 5&gt; &amp; confirm", "Html", null);` |

- [ ] **Step 3: Add the button-attachment fact to `DueReminderJobTests.cs`**

In `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs`, the `using` block becomes:

```csharp
using Assistant.Contracts;
using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.Impl.Telegram;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using static Assistant.IntegrationTests.Infrastructure.ReminderTaskBuilder;
```

(two lines added: `Assistant.Contracts` and `Assistant.Impl.Telegram`, in alphabetical position).
Then append the new fact directly after `RunAsync_TaskIsDue_SendsItsTitle`'s closing brace:

```csharp

    /// <summary>
    /// When a task is due
    /// And the job runs
    /// Then the reminder carries exactly one button
    /// And that button's callback data decodes to the same task
    /// And its label is the catalogue's Done label.
    /// </summary>
    [Fact]
    public async Task RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        var sent = Assert.Single(await wireMock.SentMessagesAsync());
        var row = Assert.Single(sent.ReplyMarkup!.InlineKeyboard);
        var button = Assert.Single(row);
        Assert.Equal(TaskActions.Done.Label, button.Text);
        Assert.Equal(CallbackCodec.Encode(TaskActions.Done.Key, task.Id), button.CallbackData);
    }
```

- [ ] **Step 4: Build, then run, and watch it fail with the specific expected message**

```bash
dotnet build --no-restore
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests \
  --filter "FullyQualifiedName~RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask"
```

The build succeeds — `DueReminderJob` still calls `notifier.SendAsync(task.Title, ct)`, an
existing, still-valid call, and `sent.ReplyMarkup!` compiles under the null-forgiving operator. The
run does not: **1 failed.**

```
System.NullReferenceException : Object reference not set to an instance of an object.
```

thrown at the `Assert.Single(sent.ReplyMarkup!.InlineKeyboard)` line — `SendAsync` sent no
`reply_markup` at all (Fact B), so `ReplyMarkup` deserialises to `null`, and the null-forgiving
operator only silences the compiler, not the runtime.

- [ ] **Step 5: Add `SendTaskAsync` to `INotifier`, and amend the interface's own remarks**

In `src/Assistant.Interfaces/INotifier.cs`, replace the class-level `<remarks>`:

Before:

```csharp
/// <remarks>
/// The recipient is configuration, not a parameter: this is a single-user assistant, so every
/// call site would otherwise pass the same value. Rendering is the caller's job — a notifier
/// delivers text it is given and never sees a database shape.
/// </remarks>
```

After:

```csharp
/// <remarks>
/// The recipient is configuration, not a parameter: this is a single-user assistant, so every
/// call site would otherwise pass the same value. Rendering the message body is the caller's
/// job -- a notifier escapes and formats text it is given, never composing prose of its own. A
/// task identifier is different: it is the channel-neutral handle an adapter needs to build
/// whatever affordance its own channel supports (a callback button, a deep link, ...), not a
/// database shape. A caller passing a pre-built, channel-specific token instead -- Telegram's
/// own <c>v1:done:...</c> callback string, say -- would leak one channel's wire format into an
/// interface every future channel must also implement.
/// </remarks>
```

Then insert the new method directly after `SendAsync`'s closing `;`, before the existing
`<summary>` for `MarkCompletedTaskAsync`:

```csharp

    /// <summary>
    /// Sends a message announcing a task, with every action from the shared catalogue attached
    /// as a button.
    /// </summary>
    /// <param name="taskId">
    /// The task the message announces. The adapter needs this to build a channel-neutral handle
    /// for every action in the catalogue -- it never sees any other part of a database shape.
    /// </param>
    /// <param name="text">
    /// The message body, as plain text. The adapter escapes whatever its channel requires
    /// before sending, so callers must not pre-escape.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    /// <remarks>
    /// Every action in <c>TaskActions.All</c> is attached; there is no overload that accepts a
    /// subset, because every caller today wants the same one -- the day a caller needs fewer,
    /// that caller is the trigger for adding one.
    /// </remarks>
    Task SendTaskAsync(Guid taskId, string text, CancellationToken ct);
```

- [ ] **Step 6: Implement `SendTaskAsync` in `TelegramNotifier`**

In `src/Assistant.Impl/Telegram/TelegramNotifier.cs`, add the missing `using` after
`Assistant.Impl.Settings`:

```csharp
using Assistant.Contracts;
```

Insert the new method directly after `SendAsync`, before `MarkCompletedTaskAsync`:

```csharp

    /// <inheritdoc/>
    /// <remarks>
    /// Builds one button per entry in <c>TaskActions.All</c>, in one row -- there is exactly one
    /// entry, <c>Done</c>, until F11 adds more. Each button's callback data is
    /// <c>CallbackCodec.Encode</c> applied to that action's key and <paramref name="taskId"/>,
    /// the same encoding <c>CallbackRouter</c> decodes on a tap. A button's label is sent as-is:
    /// <c>parse_mode</c> governs the message body, not a button's text, which Telegram carries as
    /// a plain JSON string rather than parsed markup -- so a future label containing "&amp;" or
    /// "&lt;" would still need no escaping here.
    /// </remarks>
    public async Task SendTaskAsync(Guid taskId, string text, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(TaskActions.All
            .Select(a => InlineKeyboardButton.WithCallbackData(a.Label, CallbackCodec.Encode(a.Key, taskId))));

        await bot.SendMessage(
            settings.OwnerChatId, Escape(text), ParseMode.Html, replyMarkup: keyboard, cancellationToken: ct);
    }
```

- [ ] **Step 7: Make `DueReminderJob` call it**

In `src/Assistant.Impl/Services/Jobs/DueReminderJob.cs`:

Before:

```csharp
            await notifier.SendAsync(task.Title, ct);
```

After:

```csharp
            await notifier.SendTaskAsync(task.Id, task.Title, ct);
```

- [ ] **Step 8: Build, run, watch it pass**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests \
  --filter "FullyQualifiedName~RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask"
```

Expected: zero warnings; **1 passed, 0 failed.**

- [ ] **Step 9: Run the whole suite**

```bash
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
```

Expected: unit tests **51 passed**, unchanged — this commit touches no unit test file. Integration
tests **49 passed** (48 + 1, see "Test count arithmetic").

- [ ] **Step 10: Extend `docs/e2e-local.md`'s real-Telegram walkthrough**

In `docs/e2e-local.md`, append after the existing paragraph that ends "...worth doing once against
real Telegram, not only against the stub." and before `## Troubleshooting`:

```markdown

The reminder now carries a Done button. Tap it: the title gets struck through and the button
disappears -- Telegram's own UI removing it is the proof that `MarkCompletedTaskAsync`'s explicit
empty keyboard actually reached the app, not just the stub's request log in the walkthrough above.
This is the one step in this file a test suite cannot substitute for: `CallbackRouterTests` and
`TelegramNotifierTests` already prove every wire shape byte for byte, but only tapping a real
button on a real phone proves Telegram itself renders one. This step is the owner's own to run --
no agent may run the worker against real Telegram (it needs a real bot token and sends a real
message) -- and it is F6's own `observable` requirement (backlog §1).
```

- [ ] **Step 11: Mark the backlog's F6 entry done**

In `docs/design/2026-08-22-slice-1-feature-backlog.md`:

Before:

```markdown
**F6 · Complete a task from a button · observable** — spec §6.4
```

After:

```markdown
**F6 · Complete a task from a button · observable** — spec §6.4 · **done**
```

And, immediately after the existing "*Settled at F6-2:*" block's final bullet (the one ending
"...see F6-1's and F6-2's own plans for why the button ships last."), append:

```markdown
*Settled at F6-3:*
- **The button exists.** `INotifier.SendTaskAsync` attaches one button per `TaskActions` entry --
  today, only `Done` -- to the message `DueReminderJob` sends. `MarkCompletedTaskAsync` now sends
  an explicit empty keyboard on completion, so a struck-through reminder does not keep a dead
  button under it. This is the first inline keyboard anywhere in this repository's history.
- **F6 is closed.** All three pull requests (F6-1, F6-2, F6-3) have landed, and the `observable`
  tag is met: `docs/e2e-local.md`'s "Walkthrough against real Telegram" section carries the
  owner's own manual proof.
```

- [ ] **Step 12: Commit**

```bash
git add src/Assistant.Interfaces/INotifier.cs \
        src/Assistant.Impl/Telegram/TelegramNotifier.cs \
        src/Assistant.Impl/Services/Jobs/DueReminderJob.cs \
        tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs \
        tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs \
        tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs \
        docs/e2e-local.md \
        docs/design/2026-08-22-slice-1-feature-backlog.md
git commit
```

Message:

```
feat: INotifier.SendTaskAsync attaches the Done button

SendTaskAsync(Guid taskId, string text, CancellationToken ct) is a
new INotifier method: it sends a message the same way SendAsync
does, with one button per TaskActions.All entry attached -- today,
only Done -- each button's callback data built with the same
CallbackCodec.Encode CallbackRouter already decodes on a tap.
DueReminderJob now calls it instead of SendAsync; nothing else does.
SendAsync itself is unchanged, and its other two call sites (the
startup probe, the AI reply) still carry no button, per Decision 1:
an optional parameter on SendAsync would silently offer a button to
both, and Open/Closed says add a method rather than touch an
existing seam two other callers already share.

INotifier's own class remarks are amended: a Guid taskId is not the
"rendering is the caller's job" text originally ruled out, since it
names a channel-neutral handle an adapter needs to build any
affordance at all, not a database shape -- the alternative, a caller
passing a pre-encoded v1:done:... string, would leak Telegram's own
wire format into a channel-neutral interface, which is strictly
worse.

DueReminderJobTests gains a fact tying the encoder this method uses
to the decoder CallbackRouter uses, asserting the button's
callback_data against CallbackCodec.Encode directly rather than a
hand-rolled base64 string. SendMessagePayload grows the same
reply_markup shape EditMessageTextPayload gained last commit.

F6's own observable tag is met: this is the first inline keyboard
anywhere in this repository's history. docs/e2e-local.md's
"Walkthrough against real Telegram" section gains the manual step
that proves it on a real phone, and the backlog's F6 entry is
marked done.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

---
## Self-review

**Commit 1 (`MarkCompletedTaskAsync` clears the keyboard):**
- [ ] `TelegramNotifier.NoButtons` is built with the parameterless constructor, `new
      InlineKeyboardMarkup()`, not `new InlineKeyboardMarkup([])` — confirmed against Fact C's own
      table, and the field's own comment explains why the alternative is wrong, not just that it is
- [ ] `MarkCompletedTaskAsync` passes `NoButtons` unconditionally, on every edit, not only when a
      keyboard is known to exist — Decision 3's own argument for why conditional logic here would
      be unreachable-by-test speculation
- [ ] Both `INotifier.MarkCompletedTaskAsync`'s and `TelegramNotifier.MarkCompletedTaskAsync`'s
      `<remarks>` are rewritten to state current behaviour, with no lingering "F6-3 must revisit
      this" language — checked directly: neither new `<remarks>` block names F6-3 at all
- [ ] The new `TelegramNotifierTests` fact hand-derives its expected escaped string, the same
      discipline `SendAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder` already
      uses, not the production `Replace` chain
- [ ] `ReplyMarkupPayload` and `InlineButtonPayload` both carry `[JsonExtensionData] Extra`,
      matching every other payload record in the file, so `strict: true` catches an unnamed field
      at either nesting level
- [ ] `CallbackRouterTests.cs`'s one changed line now expects `new ReplyMarkupPayload([])`, not
      `null` — the edit carries a keyboard now, per Decision 3

**Commit 2 (`SendTaskAsync` attaches the button):**
- [ ] `SendTaskAsync`'s only production caller is `DueReminderJob`; `Program.cs:17` and
      `MessageHandler.cs:57` still call `SendAsync`, confirmed by re-reading both files after this
      commit's diff
- [ ] `SendTaskAsync` builds its keyboard as the single `Done` button, constructed directly
      rather than by iterating `TaskActions.All` — reading `Key` and `Label` off the catalogue's
      `Done` entry so neither is declared twice
- [ ] Every button's `callback_data` comes from `CallbackCodec.Encode`, the same method
      `CallbackRouter.HandleAsync` decodes with `CallbackCodec.TryDecode` — no separate encoding
      exists anywhere in this slice
- [ ] `INotifier`'s class-level `<remarks>` no longer say "a notifier delivers text it is given and
      never sees a database shape" unqualified — the amended text distinguishes the message body
      (still true) from a task identifier (a channel-neutral handle, not a database shape)
- [ ] The three `SendMessagePayload` construction sites in `TelegramNotifierTests.cs` (lines 54,
      81, 111) each carry a trailing `null`; no other file constructs `SendMessagePayload`
      positionally
- [ ] `DueReminderJobTests`'s new fact resolves the sent button through `sent.ReplyMarkup!
      .InlineKeyboard`, asserting exactly one row and exactly one button — not merely that a
      keyboard exists
- [ ] `docs/e2e-local.md`'s new paragraph sits inside the existing "Walkthrough against real
      Telegram" section, after its existing final paragraph and before `## Troubleshooting`, not as
      a new top-level section
- [ ] The backlog's F6 header gains `· **done**` in the exact position F5b's own header already
      uses it, and the new "Settled at F6-3" block sits after, not instead of, "Settled at F6-2"

**Whole feature, once both commits land:**
- [ ] Every new public member has a three-line `<summary>` (or a longer Gherkin block for a test);
      `CS1591` was clean in the verification worktree's build (zero warnings)
- [ ] Every class taking arguments still uses a primary constructor — neither `TelegramNotifier`
      nor `DueReminderJob`'s own constructor is touched, only their bodies
- [ ] No emoji anywhere, including both commit messages and the new `docs/e2e-local.md` paragraph
- [ ] **No plan-internal decision citation inside any C# code block, doc comment, or commit
      message** — re-checked directly: every fenced code block above was re-read for this before
      the plan was written down
- [ ] No `<see cref="...">` in any doc comment points at a type in a project that does not
      reference the type's own project — checked directly: `INotifier.cs`'s amended remarks name
      `TelegramNotifier` as plain `<c>` text, not `<see cref>`, because `Assistant.Interfaces` does
      not and must not reference `Assistant.Impl`
- [ ] Type and member names are spelled identically everywhere they appear: `SendTaskAsync`,
      `NoButtons`, `ReplyMarkupPayload`, `InlineButtonPayload`, `TaskActions.Done.Key`,
      `TaskActions.Done.Label` — each carries the identical spelling in every step, decision, and
      commit message that names it
- [ ] No placeholder text ships inside a source file — every code block above is the literal text
      that was built and run in the verification worktree, not a description of it
- [ ] Spec coverage: §6.4 (the button/action/effect table this slice's keyboard finally renders
      from), §7.2 (unit vs. integration split, argued in Decision 5), §7.3 (assertion standard —
      count, recipient, exact text, exact buttons, on every wire assertion this slice touches),
      §12.1/§12.5/§12.6 — every section this plan's brief named is addressed somewhere above
- [ ] This slice's diff stays comfortably under the 1000-line budget: Decision 7 measures 205
      changed lines by actually building the code, not estimating it
- [ ] Both commit messages end with the required trailer, after a blank line
