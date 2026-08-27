# Slice 1 — Feature Backlog

**Date:** 2026-08-22
**Status:** For review
**Derived from:** `docs/design/slice-1-reminders.md` (the approved design spec — binding)

This document decomposes slice 1 into features. It adds no requirements: every feature below is
already designed and agreed in the spec, and each entry cites the section it implements.

**One feature = one pull request.** Each is planned separately, with `superpowers:writing-plans`,
at the time it is built — never all at once.

---

## 1. Rules that govern every feature

**YAGNI.** A feature may only introduce an interface member, contract type, model, model
property, or table that **the same feature exercises with a test**. Everything else waits for
the feature that consumes it.

**Open/Closed, precisely bounded.** Behaviour seams stay extension-only — a new button, tool, or
job is a new class, never an edit to an existing one (spec §3.6). Data-access surfaces grow by
adding methods, because adding a repository method *is* a modification and no design avoids it.
An abstraction with one implementation is a guess, not a seam; interfaces appear when a second
implementation or a real extension point does.

**Definition of done.** A feature is done when: it builds with zero warnings, its tests pass,
every new public member has a three-line `<summary>`, and no code was added that nothing
exercises. Features marked **observable** must also be demonstrable on a real phone.

**Size budget.** No pull request exceeds **1000 changed lines**, and smaller is better. Where a
feature would breach that, it is split along a line that leaves both halves independently
testable — never split so that one half is untestable until the other lands.
Generated EF migration files count toward the diff but not toward review effort; PR bodies say
which files are generated so they can be skipped.

**No credentials to build or test.** Telegram and both model providers are stubbed with
WireMock; Postgres comes from Docker Compose. Spec §11.3.

---

## 2. Resolved: how F1 is split

Splitting "save a task" from "find the due ones" left the first feature holding only `AddAsync`,
which cannot be verified without a read. Resolved by splitting one level differently instead:

- **`AddAsync` and `FindAsync` ship together.** Separating them would produce one PR whose write
  is unverifiable and another whose read has nothing to read — each untestable alone, which is
  exactly the split the size budget forbids. `FindAsync` is ~15 lines and is consumed at F5a
  (`TaskService` reads a task before marking its reminder sent).
- **The schema and harness move to their own feature (F1).** That is where the bulk lives: the
  first migration, `compose.test.yaml`, and the Postgres fixture. It is independently testable
  without any repository method, because a malformed row can be rejected by the database using a
  raw `INSERT` in the test — deliberately inserting bad data duplicates no mapping.

Result: F1 ≈ 450 lines (≈300 of it generated migration scaffolding), F2 ≈ 150 lines. Both well
under budget, both meaningful alone.

## 3. The features

### Reminder path — no AI, no credentials

**F1 · Database schema and the integration test harness** — spec §4.3, §7.1
Adds `AssistantDbContext`, `ReminderTaskConfiguration`, the `reminder_tasks` table with its check
constraints, the first migration, `AddAssistantRepository`, `compose.test.yaml`, and
`PostgresFixture` (readiness polling + Respawn reset). No repository methods. The partial index
belongs to F3, which is the first feature to run a query that needs it.
*Tests:* each check constraint rejects a malformed row inserted by raw SQL. Raw SQL is correct
here — the point is to write data the mapping would never produce. That migrations apply cleanly
is proven by the fixture itself, which calls `MigrateAssistantDatabaseAsync()` during collection
setup: if it fails, every integration test fails in setup. A test asserting the column list was
written and then removed — it hardcoded the column names, so it failed when a column was added
and migrated correctly, which makes it a change-detector.
**Correction, measured at F2:** the removal note originally claimed that test would *pass* when a
property was added with no migration. It would not. EF Core raises
`PendingModelChangesWarning` on model drift and that warning throws by default, so
`MigrateAsync()` fails and every integration test fails in setup. The framework already refuses
the case, which makes the removed test redundant twice over rather than once.

**F2 · Save a task and read it back** — spec §4.1, §4.3 · **done**
`ITaskRepository.AddAsync` and `FindAsync`, plus `EfTaskRepository`. `GetDueRemindersAsync` was
removed from the interface here: nothing implemented or called it, and C# would have forced a
`NotImplementedException` body or F3's query written untested. F3 re-adds it.
*Tests:* four, one outcome each. A round-trip through two separate contexts preserves every
property; instants come back at a zero offset; a miss returns null; a task whose status was never
set is refused by `ck_reminder_tasks_status_known`.
*Citation corrected:* an earlier draft cited §4.4 here. §4.4's round-trip is model → response →
model, the hand-written `Contracts` mapper, which arrives at F10. F2's is model → Postgres →
model. Different defects: a mapper that forgets a field, versus a column mapping that forgets one.
*Settled at F2:*
- **`AddAsync` surfaces the database exception untranslated.** `Result` and `ErrorCode` live in
  `Contracts`, which F5a brings to life with `TaskService`; inventing them here would build types
  nothing consumes for three features. Translation belongs to the single writer, not the
  repository.
- **`FindAsync` reads with `AsNoTracking`**, and tests write through one provider and read
  through another. Sharing a context lets EF's identity map answer the read from memory, which
  would turn the round-trip assertion into a comparison of an object with itself.
- **Instants in tests are microsecond-aligned literals.** Postgres `timestamptz` holds
  microseconds, .NET ticks are 100ns, and `UtcNow` truncates on read depending on the host
  clock — passing on one machine and failing on another.
- **The F1 carry-over is discharged.** Dropping `ck_reminder_tasks_status_known` from the
  database failed both the raw-SQL test and the new `AddAsync` one, so the raw-SQL test was
  retired in favour of the application-level test.

**F3 · Find the tasks that are due** — spec §6.2 · **done**
`ITaskRepository.GetDueRemindersAsync(asOfUtc, limit, ct)` — re-added to the interface, which F2
removed as unconsumed — plus the `idx_tasks_due_pending` partial index the query needs. No lower bound on `due_at` — that absence is what makes restart
catch-up automatic.
*Tests:* eligible vs ineligible by equivalence class, with boundaries on `due_at <= now`;
ordering oldest-first; the limit.
*Settled at F3:*
- **Spec §6.2's `status = 0` was a renumbering leftover.** `Unknown` is 0 and
  `ck_reminder_tasks_status_known` forbids it from ever being persisted, so the query as
  originally written would have returned nothing, always. Corrected to `status = 1` — `Pending` —
  matching §4.3's index predicate, which the renumbering had already updated correctly.
- **Ordering is asserted with `Assert.Equal` on a sequence of ids, never `Assert.Equivalent`.**
  `Assert.Equivalent` compares collections order-insensitively and would pass on a reversed
  result; oldest-first delivery is a business requirement, not an incidental detail.
- **The partial index ships without a test.** It changes no behaviour, only performance, so a
  test asserting it exists would be a change-detector; whether Postgres uses it is an `EXPLAIN`
  question this project has no budget or volume to justify yet.

**F4a · Send a Telegram message** — spec §3.1, §3.3, §6.5, §12.3 · **done**
`INotifier` + `TelegramNotifier` over `Telegram.Bot`, HTML parse mode. Settings are validated
during host composition — `IValidatableConfig` plus `IConfiguration.Read<T>()` binds the section
named for the settings type and calls its `Validate()`, so a missing bot token or chat id stops
the host before it serves anything. This pulls fail-fast forward from F14; see the note against
F14, below. The recipient (`TelegramSettings.OwnerChatId`) is configuration, not a parameter —
this is a single-user assistant, so every call site would otherwise pass the same value. A
`send-test-message` switch on `Assistant.Worker` sends one fixed message and exits, so the feature
can be verified by hand.
*Tests:* four, on `ConfigurationExtensions.Read<T>` — the section missing; each of the two
mandatory values missing in turn; every value present returns the settings unchanged.
`TelegramNotifier` itself ships with no automated test. Deliberate and temporary: F4b owes it.
*Settled at F4a:*
- **F4 split in two.** F4a is the implementation, accepted by a human receiving a real message on
  a real phone. F4b adds a WireMock stub to Docker Compose and the automated delivery test spec
  §7.1 calls for — F4a cannot claim that level alone.
- **HTML parse mode, not MarkdownV2.** MarkdownV2 has eighteen escape-sensitive characters, so an
  underscore or asterisk in a task title would 400 on a live reminder. HTML has three, and none of
  them occur in ordinary task text.
- **The bot token goes in user secrets, never in `appsettings.Development.json`.** `.gitignore`
  covers `.env`, `*.env`, and `appsettings.*.local.json`, but not `appsettings.Development.json`,
  and this repository is public.

**F4b · Telegram integration test** — spec §7.1, §11.3 · **done**
A WireMock stub for the Telegram API in Docker Compose, and the automated test F4a deferred.
*Tests:* exact recipient and exact text; a title containing `_` and `*` still delivers.
*Settled at F4b:*
- **WireMock is a service, not an in-process library.** Requested in review. Gains: the stub is
  inspectable while a test is paused, it can serve a locally-run `Assistant.Worker` and not just
  the test process, and one service will host the Anthropic and OpenRouter stubs at F9 and F13
  rather than each test starting its own. Costs, stated because they are not obvious: these tests
  now need Docker; verification moved from reading `server.LogEntries` in-process to `GET
  /__admin/requests` over HTTP; and isolation became explicit — `DELETE /__admin/requests` is this
  fixture's Respawn, run before every test, or the second test sees the first one's message.
- **Named `Assistant.WireMock`, for the tool rather than the role.** The alternative,
  `Assistant.ApiStubs`, survives swapping the tool and reads better once F9 and F13 add stubs to
  the same service — but renaming it now, before anything depends on it, is cheap, and renaming it
  once three features depend on it would not be.
- **`WireMockCollection` was separate from `PostgresCollection` at first.** These tests needed
  the stub and no database. A test class can belong to only one xUnit collection, so F5b — the
  first feature needing a database and a stub together — merged the two definitions into
  `IntegrationCollection`.
- **The payload is asserted whole**, with `Assert.Equivalent(expected, actual, strict: true)`:
  count, recipient, exact text, and parse mode in one assertion. A deliberate consequence: when F6
  adds an inline keyboard, `reply_markup` appears in the body and `strict` fails this test until F6
  updates it.
- **The debt F4a took on is paid.** `TelegramNotifier` shipped with no automated test at F4a; F4b
  closes that gap.

**F5a · `TaskService`, the single writer** — spec §3.6, §4.2 · **done**
`IClock`/`SystemClock`, `Result`/`ErrorCode` in `Contracts`, `ITaskService.MarkReminderSentAsync`,
and `TaskService` — the only type permitted to change a task. `ITaskRepository` gains
`UpdateAsync`. Plan: `docs/plans/2026-08-25-f5a-task-service.md`.
*Tests:* three, all integration, all asserting business outcomes rather than fields — a due task
recorded as sent stops being due; a task with no due time is refused; a task that does not exist
is refused.
*Carried from F1:* `ck_reminder_tasks_sent_requires_due` is currently proven only by a raw-SQL
insert. `MarkReminderSentAsync` is the only code that ever sets `ReminderSentAt`, so it is the
one place the application can violate that rule. Note the path is unreachable through the
scheduler F5b adds — `GetDueRemindersAsync` filters on `due_at <= now`, so a task without a due
time never reaches the job. The test therefore calls the service directly with a task that has no
due time, covering the public surface rather than the scheduler's route through it.
*Settled at F5a:*
- **F5 is split.** F5a is the single writer; F5b is the scheduler and remains the observable
  milestone.
- **`IClock` landed in F5a, not F5b.** Spec §4.2 requires `UpdatedAt` stamped on every mutation,
  and doing that from `DateTimeOffset.UtcNow` inside `TaskService` would make the rule untestable
  in the one class §4.2 says must be directly testable. Replaced by the BCL's `TimeProvider` at
  F5b, below — `IClock` no longer exists in the codebase.
- **`Result` and `ErrorCode` were designed here because the spec never defines them.** §4.2 lists
  methods returning `Task<Result>` and stops. `Error` is nullable rather than an
  `Unknown`-means-success sentinel, since every enum in this project reserves its first member for
  "nobody set this". No message string — nothing renders one until F10.
- **`ITaskService` starts with one method.** §4.2 shows eight; the rest arrive with their
  callers. Adding a method to a data-access surface is a modification no design avoids.
- **The tests assert business outcomes.** The headline test asserts the task stops being due, not
  that a field changed — proven by a mutation that stamps `UpdatedAt` and leaves
  `ReminderSentAt`, which failed exactly that test while the two refusal tests passed.
- **The paired-write rule is proven in the `ReminderSentAt` direction only.** The reverse
  mutation — stamp `ReminderSentAt`, drop `UpdatedAt` — passes the whole suite unchanged, because
  nothing observable yet depends on `UpdatedAt`. Left honest rather than closed with an assertion
  invented to cover it.
- **Known sharp edge, carried to F10:** `AddAsync` leaves the entity tracked in its scope's
  `DbContext`. A caller that adds a task and then mutates it through `TaskService` inside one
  scope hits an EF identity conflict. No current call site does; F10 is the first that plausibly
  could.

**F5b · The scheduler fires due reminders · observable** — spec §6.1, §6.2 · **done**
`IScheduledJob`, `ScheduledJobBase` (re-entrancy guard + try/catch), `ReminderScheduler` on a 30s
`PeriodicTimer`, and `DueReminderJob`, calling `ITaskService.MarkReminderSentAsync` from F5a. Send
**then** mark — at-least-once is deliberate.
*Tests:* a due task produces exactly one message; a second tick produces none; a process restarted
after the due time still delivers; a delivery failure leaves the task still due.
*Settled at F5b:*
- **`TimeProvider` replaced `IClock`.** F5a had shipped a hand-rolled `IClock`/`SystemClock`
  three days earlier. F5b deleted both in favour of the BCL's `TimeProvider`, because
  `PeriodicTimer` accepts one directly and `FakeTimeProvider` drives a fake clock through the
  timer, which a custom interface cannot do. Worth modifying working code because the alternative
  was two clock abstractions in one codebase.
- **`ITaskService.GetDueRemindersAsync(limit, ct)` takes no instant.** The service decides what
  "now" means, so a job's notion of due time cannot drift from the rest of the assistant's. It has
  no test of its own: it is a branchless pass-through and F3's query tests already pin which rows
  come back.
- **Send, then mark — and it is now pinned by a test.** Reversing the two lines to mark-then-send
  passed all twenty-two integration tests, so the feature's most consequential decision was
  resting on source order alone. A fourth job test points the notifier at a dead port so delivery
  throws, then asserts the task is *still due*. Under mark-then-send it is not, and the reminder is
  gone.
- **Jobs are singletons and open their own scope.** `ReminderScheduler` is a `BackgroundService`,
  so it is a singleton, and `ITaskService` is scoped. Resolving jobs from a per-tick scope would
  fix the injection error but silently break the re-entrancy guard, because a per-instance flag on
  a per-tick object guards nothing. `DueReminderJob` takes `IServiceScopeFactory` instead.
- **The re-entrancy guard is tested on `ScheduledJobBase` directly.** The scheduler awaits jobs
  sequentially, so it cannot produce an overlapping call; a test driving the guard through the
  loop would pass whether or not the guard existed. It is tested by calling `RunAsync` twice
  concurrently on the base class, where the contract actually lives.
- **`AddAssistantScheduler` is the public seam.** `Assistant.IntegrationTests` reaches
  `Assistant.Impl` only transitively, so a test cannot name the internal `DueReminderJob`.
  Registering through the extension means the job test exercises the real DI wiring.
- **The two xUnit collections merged.** There is no `xunit.runner.json` and no
  `[assembly: CollectionBehavior]`, so xUnit's default parallelism runs distinct collections
  concurrently, and `PostgresFixture.ResetAsync` truncates every table — a second Postgres-touching
  collection running in parallel would truncate this one's rows mid-test, which presents as a
  flaky database, not as a test-isolation bug. F5b is the first feature needing a database and a
  stub together, so `PostgresCollection` and `WireMockCollection` became one
  `IntegrationCollection`.
- **The architecture rule barring jobs from repositories predates F5b, and only now guards
  anything.** `DependencyRuleTests.Only_TaskService_references_ITaskRepository_in_Impl` shipped
  before F5b existed, so F5b does not owe it. Until `DueReminderJob` was the first job type in
  `Assistant.Impl`, the test scanned zero job types and could not have failed no matter what a job
  did. It now has something to guard, and passes because `DueReminderJob` reaches `ITaskService`,
  never `ITaskRepository`.
- **The Worker applies migrations explicitly.** `MigrateAssistantDatabaseAsync` had existed since
  F2 with no caller anywhere in the repository, and `AGENTS.md` wrongly claimed
  `AddAssistantRepository` migrated automatically. F5b is the first feature where the Worker needs
  a database, so it became that method's first caller. A new `DatabaseSettings` reads the
  connection string through the project's existing `IConfiguration.Read<T>()` fail-fast path.
- **HTML-escaping debt, owed by F7.** `TelegramNotifier` sends with `ParseMode.Html`, so a title
  containing `<` or `&` will be rejected by Telegram with a 400. Unreachable at F5b — the only way
  a task exists is a hand-written SQL insert — but F7 is the first feature where a person can type
  a title, and F7 owes the escaping.
- **The reminder message lost its clock-emoji prefix on review.** `DueReminderJob` sent the title
  behind a pictogram; the repository's owner asked for it gone, and the delivered message is now
  the task title alone. The same review settled a repository-wide rule barring emoji outright
  (conventions §12.6): not in source, tests, documentation, commit messages, or bot message text.
**Milestone: the product works.** Verified against a local Postgres with Telegram pointed at the
WireMock stub: a row seeded two hours overdue, and the stub received exactly one `sendMessage`
carrying the task's title within the tick interval; the row was marked sent and no later tick
redelivered it. The same check against the real Telegram API is still owed by the repository's
owner, since it needs their bot token.

**F6 · Complete a task from a button · observable** — spec §6.4
`ITaskAction` + `DoneAction`, `ICallbackHandler` + `CallbackRouter`, the `v1:<action>:<id>`
callback codec, in-place message edit, and `ITaskService.CompleteAsync`. `ReminderTask` regains
`CompletedAt`, which also brings back the `ck_completed_consistency` check constraint. Depends on
F7's `TelegramListener`: a callback query arrives on the same `getUpdates` stream, so this feature
must add `UpdateType.CallbackQuery` to the listener's `allowedUpdates` array or its buttons will
silently never fire.
*Tests:* one tap completes; a second tap says "already done" rather than erroring; the callback
query is always answered.

### Capture path — the flow you described

**F7 · Consume inbound messages** — spec §5.1 · **done**
`TelegramListener` long-poll loop, owner whitelist, and an echo reply. No AI yet.
*Tests:* a whitelisted sender gets a reply; a non-whitelisted sender is ignored silently and
costs nothing; a message already answered is not answered again, even though the listener keeps
polling.
*Settled at F7:*
- **One class, not two.** The approved design paired `TelegramListener` (loop) with a
  `MessageHandler` (work), mirroring F5b's `ReminderScheduler`/`DueReminderJob` split. That split
  earns its keep at F5b because `ReminderScheduler` injects `IEnumerable<IScheduledJob>` — a real
  seam with a second implementation, `DailyBriefJob`. `TelegramListener` calls exactly one handler
  through no interface at all, so a second class would be ceremony: the backlog's own rule already
  says an abstraction with one implementation is a guess. `TelegramListener` owns the loop, the
  offset, the whitelist, and the reply in about 60 lines. **Cost if this is wrong:** F6 extracts a
  handler from a 60-line class it is already editing.
- **The whitelist compares `Message.Chat.Id`, not `Message.From.Id`.** Spec §5.1 says "reject any
  sender other than the configured owner ID"; "sender" reads as `From.Id`, a user id, while
  `TelegramSettings.OwnerChatId` is a chat id — the same number only in a private one-to-one chat.
  Comparing `Chat.Id` reuses the setting that already exists rather than adding an `OwnerUserId`
  that would, for this product, always hold an identical value — a second source of truth for one
  number. It is also the field that stays correct outside that one case: a stranger's message
  carries their own chat id and is dropped, and the check is still correct if the bot is ever added
  to a group, where `Chat.Id` is the group's, still not the owner's. `From` is additionally
  nullable on `Message` and absent from the stub's canned bodies, so `Chat.Id` is the field
  reliably present to compare.
- **The reply goes through `INotifier`, unchanged — and this is now proven, not just argued.** No
  new interface, no `SendToAsync(chatId, ...)` overload: `INotifier`'s recipient comes from
  configuration, so it is structurally incapable of addressing anyone but the owner. Deliberately
  breaking the whitelist to prove the whitelist test could fail confirmed the shape of the defect
  rather than just its existence — both replies still landed in the owner's chat; the stranger's
  *text* was echoed back, but the destination never moved. A bypassed whitelist makes the owner
  read a stranger's words, not the stranger receive anything.
- **Advance the offset before handling — the opposite of F5b's ordering.** F5b sends and then
  marks, so a crash re-delivers rather than loses a reminder, because a lost reminder is this
  product's core failure. F7 marks first: `_offset = update.Id + 1` runs before the update is
  handled, because the failure that matters here is different — a lost echo costs one retype,
  while a handler that always throws and is re-polled forever wedges the bot and hammers Telegram
  at full speed. Advancing first bounds a poison message's cost to exactly one dropped reply.
  Handling is wrapped in its own try/catch inside the loop over the batch, not around it, so one
  throwing update does not stop the next update in the same batch from running.
- **The stub answers `getUpdates` by matching the offset in the request body, not a WireMock
  scenario.** A scenario-based stub was tried first: with an entry mapping and a
  `WhenStateIs: "drained"` mapping, the third call served the seeded update again — scenarios
  cycle. Pinning the drained state fixes the cycling but then needs `POST /__admin/scenarios/reset`
  between tests, and, fatally, it drains by call count, so a listener that never advances its
  offset still passes. Two priority-ordered mappings instead — any `getUpdates` POST served at
  priority 10, a body match on the advanced offset served at priority 1 — drain by the same signal
  real Telegram uses: once the listener sends the advanced offset it gets, and keeps getting, the
  empty result. No scenario reset, no call counting. This is also what makes the offset testable
  through a business outcome rather than a mock verification: a listener that never advances its
  offset keeps matching the *pending* mapping and echoes the same message forever. Deliberately
  breaking the offset advance to check the no-repeat test could fail measured 3704 replies in the
  3-second settle window — ten to thirty times the plan's own estimate of "hundreds". The direction
  was right; the magnitude was not close. It cannot fail marginally either way.
- **The drained response carries a one-second delay.** Real Telegram holds a `getUpdates` call
  open for the requested `timeout`; WireMock answers instantly, so a correct listener would spin at
  full speed against the stub — thousands of requests during a test, a pegged core during a local
  `dotnet run`. A `"Delay": 1000` on the drained mapping throttles the idle loop to roughly one poll
  a second without slowing any test, because every test's reply comes from the first poll, which
  matches the undelayed pending mapping.
- **The integration tests drive the real hosted service, because they cannot name
  `TelegramListener`.** `Assistant.IntegrationTests` references only `Assistant.Worker`, reaching
  `Assistant.Impl` transitively, and `Assistant.Impl`'s `InternalsVisibleTo` names only
  `Assistant.UnitTests`. That is not worked around — it is the constraint that keeps the tests
  honest. They resolve `IHostedService` from the container `AddAssistantListener()` builds, start
  it, and assert on what reaches the stub, exactly as F5b resolved `IScheduledJob` through
  `AddAssistantScheduler()`. No `InternalsVisibleTo` was added for the integration project, and
  `TelegramListener` stays internal.
- **A deliberate-break instruction can itself be wrong.** The plan's own recipe for proving the
  whitelist test could fail was to delete the whitelist clause outright. Doing that leaves the
  `settings` primary-constructor parameter unreferenced, which trips `CS9113` under
  warnings-as-errors — it does not compile, so it cannot even reach the test it was meant to fail.
  The break that actually exercises the test is making the comparison false at runtime instead of
  removing it. Worth recording on its own: the failure mode was in the break instruction, not in
  the code it was checking.
- **The HTML-escaping debt F5b flagged as owed by F7 is discharged.** `TelegramNotifier` now
  escapes `&`, `<` and `>` before every send. Of the two consequences the debt carried, the
  reminder path was the more severe and had been live on `main` since F5b: `DueReminderJob` sends
  `task.Title` and then calls `MarkReminderSentAsync`, so a title containing `<` or `&` made the
  send throw, the mark never ran, and the `foreach` over the batch aborted — that task retried
  every 30 seconds forever, and every reminder behind it in the same batch was blocked with it.
  F7's own echo carried the milder version: typing `5 < 6` to the bot produced a 400 and silence.
  Escaping lives in `TelegramNotifier`, not at its call sites, because it is the only type that
  knows it is sending HTML — today one hundred percent of what it sends is plain text and nothing
  sends markup, so a text-versus-markup distinction would be an abstraction with one case, which
  the backlog's own YAGNI rule forbids. F10 is the first feature that renders markup, and it earns
  that distinction then. The three replacements are hand-rolled (`Replace("&","&amp;")` before
  `Replace("<","&lt;")` before `Replace(">","&gt;")`) rather than delegated to a general-purpose
  encoder: `&` must run first because reversing it makes `<` become `&lt;` and then the `&` that
  replacement just introduced gets re-escaped into `&amp;lt;`, rendering as literal text instead
  of a bracket. `WebUtility.HtmlEncode` was tried and rejected on suspicion it would numeric-encode
  non-ASCII text; verified directly on .NET 10, it turns out to reach only the Latin-1 Supplement
  range and characters outside the Basic Multilingual Plane — it leaves Hebrew alone but still
  turns "café" into "caf&amp;#233;" — while `System.Text.Encodings.Web.HtmlEncoder.Default`, an
  equally reachable general-purpose choice, does numeric-encode this bot's Hebrew text
  wholesale. Either is a trap a maintainer could reach for without noticing; hand-rolling three
  fixed replacements is immune by construction, because it can only ever touch `&`, `<` and `>`.

**F8 · Resolve local time** — spec §5.4
`ILocalTimeResolver` + `LocalTimeResolver` over the configured IANA zone, and the guard clauses:
more than a minute in the past, more than two years ahead, DST spring-forward gap, fall-back
ambiguity.
*Tests:* a table over the DST boundaries and each guard.

**F9 · Send to the model and get a tool call** — spec §5.2, §5.3, §12.3
`IChatClient`, `IAnthropicApi` via Refit, the system prompt carrying current local time, and
`CreateTaskTool` as the first `IAssistantTool`. Adds `CreateTaskRequest` to `Contracts`
(`Result` and `ErrorCode` arrived at F5a).
*Tests:* free text produces the expected tool call against a WireMock'd provider.

**F10 · Store the parsed task and reply · observable** — spec §5.1
`ITaskService.CreateAsync`, the mapping extension methods, and the reply rendered with its
inline keyboard. `ReminderTask` regains `Notes`, which the capture path is first to write.
*Tests:* "call the bank tomorrow at 10" ends as a row with the right UTC instant and a reply
carrying the right buttons.
**Milestone: the full loop.** Talk to it, get reminded, tap Done.

### Completing the product

**F11 · Snooze and reschedule** — spec §6.4, §4.2
`SnoozeAction`, `RescheduleAction`, `EditAction`. Both clear `ReminderSentAt` and reset
`DeliveryAttempts` so the task fires again — the pairing that makes `TaskService` the mandatory
single writer. `ReminderTask` regains `DeliveryAttempts`; the retry cap enters the due query here.
*Tests:* snooze 1h fires at exactly +1h, not immediately; the sent marker is cleared.

**F12 · Daily brief · observable** — spec §6.3
`DailyBriefLog` + `daily_brief_log` (its primary key is the once-per-day check),
`IDailyBriefRepository.TryClaimAsync`, `DailyBriefJob` at 07:00 local, no cutoff.
`ReminderTask` regains `Priority` — the brief is the first thing that orders by it (spec §3.3).
*Tests:* one brief per day across a restart; a late brief still sends.

**F13 · Never lose a capture** — spec §5.5, §5.6
`IOpenRouterApi`, `FallbackChatClient` with Polly, the per-minute call cap, and the raw-capture
safety net: if every provider fails the text is still saved as an undated task and the user is
told. `ChatMessage` + `chat_messages` arrive here for the conversation window.
*Tests:* both providers failing still produces a task and a reply.

**F14 · Operations** — spec §6.5, §8, §11.3
`/status`, Serilog, the heartbeat file and container healthcheck, `Dockerfile`, `compose.yaml`,
`.github/workflows/ci.yml` with gitleaks, and `appsettings.{Environment}.json`.
*Tests:* the host refuses to start on invalid configuration.
**Reduced by F4a:** the fail-fast mechanism — `IValidatableConfig` and `IConfiguration.Read<T>()`
— already exists and is already exercised by `TelegramSettings`. F14 inherits only
`appsettings.{Environment}.json` and validation for the settings types slice 1 still needs, not
the mechanism itself.

**Container packaging for the worker** — spec §8, §11.6 · **unscheduled**
There is no `compose.yaml` and no worker `Dockerfile` in this repository, and there never has
been — only `compose.test.yaml`, which serves the test suite. F5b needed the Worker to run
against a real database for the first time, and in doing so found `AGENTS.md` documenting a
`docker compose up -d` workflow that had never worked; F5b corrected that text to describe what
actually exists today. The work itself remains undone: an image build, secret delivery, and a
restart policy. F14 already lists `Dockerfile` and `compose.yaml` among its contents, so this is
not a competing feature number — it is a flag that the gap F5b found is real and directly
observed, not merely anticipated, and worth tracking on its own until F14 is planned.

**Continuous integration** — spec §9 step 1, §11.2, §11.3 · **unscheduled**
There is no `.github/workflows` directory in this repository, and there never has been — no pull
request has ever been checked by a machine. Spec §9 step 1 lists "GitHub Actions workflow running
them" as part of the very first implementation step, before any code was written; it was skipped.
§11.2 states that gitleaks "runs in CI on every push and pull request" — it does not run anywhere,
and during F5b a live Postgres password reached the tracked, public `appsettings.json` and was
caught only by a human reading a diff, not by any machine. §11.3 already specifies the stages this
needs: restore, build with warnings as errors, architecture tests, unit tests, integration tests,
gitleaks. F14 already lists `.github/workflows/ci.yml` among its contents, so this is not a
competing feature number — it is a flag that a promise the documents already make is unbacked,
which is worse than not having made it.

---

## 4. Deferred model properties

The YAGNI reset stripped `ReminderTask` to seven properties. Each returns with the feature that
first needs it, at the cost of one additive migration:

| Property | Returns at |
| :--- | :--- |
| `CompletedAt` | F6 |
| `DeliveryAttempts` | F11 |
| `Priority` | F12 |
| `Notes` | F10 |

## 5. Not in slice 1

Notes with embeddings and semantic search, recurring tasks, separate due date vs reminder time,
an HTTP API, calendar integration, voice transcription, multi-user. Spec §1.3 and §10.

## 6. Order and dependencies

F1 → F2 → F3 → F4a → F5a → F5b is a chain; nothing in it can be reordered. F4b depends only on
F4a and does not block F5a. F6 needs F5b **and F7**: a button tap arrives as a `callback_query`
on the same `getUpdates` stream `TelegramListener` polls, so without the listener there is
nothing to route the tap to. This backlog previously called F7 "independent of F1-F6" and free to
move earlier only if you wanted to talk to the bot sooner — both halves understated the truth; F7
is not optional-but-early, F6 cannot be built without it. The binding spec had this right from the
start: §9's implementation order puts the Telegram round-trip at step 5 and buttons at step 7,
after it. The backlog's own numbering, not the spec, was wrong.
F8 → F9 → F10 is a chain. F11-F14 each depend only on F10.

Three milestones are worth pausing on: **F5b** (a reminder actually fires), **F10** (the whole
loop works), **F14** (it runs on a VPS).
