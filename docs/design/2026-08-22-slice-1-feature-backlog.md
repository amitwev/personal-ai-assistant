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
- **Reversed: one class was wrong, and the cost predicted above arrived on schedule.**
  `allowedUpdates: [UpdateType.Message]` was hardcoded with a comment telling F6 to remember to add
  `UpdateType.CallbackQuery`, and the owner whitelist lived in the private `HandleAsync`, so F6's
  callback handling would have had to remember to apply it again. A comment warning the next
  feature about a trap is evidence of a trap, not a fix for one — "an abstraction with one
  implementation is a guess" is a good rule, but it argues against a seam justified by a
  hypothetical second implementation, not against one justified by a documented, already-written
  trap. `TelegramListener` now injects `IEnumerable<ITelegramUpdateHandler>`, derives
  `allowedUpdates` from `handlers.Select(h => h.Handles).Distinct()`, and dispatches each update to
  every handler that claims it, each in its own try/catch — `MessageHandler` is the sole handler
  today, and it applies the owner check itself. `ITelegramUpdateHandler` stays internal to
  `Assistant.Impl` rather than moving to `Assistant.Interfaces` with every other interface, because
  it names `Telegram.Bot.Types.Update` and
  `DependencyRuleTests.Interfaces_do_not_depend_on_infrastructure_libraries` fails the build if
  `Assistant.Interfaces` references `Telegram.Bot`. This was a behaviour-preserving refactor:
  `TelegramListenerTests.cs` — owner gets a reply, stranger does not, an answered message is not
  answered twice — passed unchanged before and after, which is what proves behaviour was
  preserved rather than merely reshuffled.
- **Reversed again: the extracted base class was also wrong.** The same refactor introduced an
  abstract `OwnerOnlyUpdateHandler` holding the owner check, and that class was dropped before this
  PR merged. One subclass made it a single-case abstraction, which this backlog's own rule and
  `TelegramNotifier`'s remarks both forbid. The `ScheduledJobBase` parallel drawn above doesn't
  hold either: `ScheduledJobBase` hides mechanism a subclass never touches, while
  `OwnerOnlyUpdateHandler` held a policy the subclass had to feed back in through `ChatIdOf`. And
  the protection was opt-in anyway — the listener dispatches on `ITelegramUpdateHandler`, so a
  handler that implements the interface directly skips the check entirely, base class or not. The
  owner check now lives inline in `MessageHandler`. **Cost if this is wrong:** F6 writes the owner
  check by hand in its callback handler, and if it forgets, anyone who finds the bot can press
  buttons that complete the owner's tasks. The base is worth re-extracting once two real handlers
  exist, when it will be clear whether it should be generic over the payload to avoid the
  double-unwrap.
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

**F8 · Resolve local time** — spec §5.4 · **done**
`ILocalTimeResolver` + `LocalTimeResolver` over the configured IANA zone, and the guard clauses:
more than a minute in the past, more than two years ahead, DST spring-forward gap, fall-back
ambiguity.
*Tests:* a table over the DST boundaries and each guard.
*Settled at F8:*
- **The guards live in the resolver, not a service.** `LocalTimeResolver.Resolve` checks the past
  and future bounds itself and returns the failure on a `Result<DateTimeOffset>`. Spec §5.4 said
  a service would apply them, but F10 is the first feature with a service — `ITaskService` — that
  could even hold them.
- **`Result<T>` joined the non-generic `Result` in `Contracts`.** Both stay: most operations in
  this project succeed without producing a value, and giving them a meaningless type argument
  would read worse than having two types.
- **`Resolve` takes a `DateTime`, not the model's ISO string.** The parse from
  `2026-08-17T10:00:00` happens where F9's `CreateTaskRequest` lands it — free, from
  `System.Text.Json`, which already parses that format into a `DateTime` without any code of
  this project's own.
- **The spring-forward gap needs no branch.** `GetUtcOffset` returns the pre-transition offset
  `o` for a reading `L` inside the gap, and shifting the reading past the gap by the gap's width
  `D` names the same instant: `(L + D) - (o + D) == L - o` for any `D`, because the two shifts
  cancel. This was probed in two zones before the code was written, and
  `LocalTimeResolverTests` holds it in both — Jerusalem's hour-wide gap and Lord Howe's
  half-hour one. Recorded here so the next reader does not add a branch the algebra already
  makes unnecessary.
- **Both `ConvertTimeToUtc` and `GetUtcOffset` resolve an ambiguous time to the second
  occurrence.** Spec §5.4 requires the first, so it is selected by hand:
  `GetAmbiguousTimeOffsets(wall).Max()` is always the larger of the two offsets, and so the
  first occurrence, because falling back only ever lowers the offset.
- **Tests run against `Australia/Lord_Howe` as well as the configured zone.** Jerusalem's
  spring-forward gap and its offset change are both exactly one hour, so a resolver that
  hardcodes a one-hour shift instead of reading the actual transition would still pass every
  Jerusalem case. Lord Howe's half-hour gap is the only fixture that can catch it.
- **The zone is configuration, not code.** `TimeSettings.IanaTimeZone` binds
  `TimeSettings:IanaTimeZone`, defaults to `Asia/Jerusalem` in `appsettings.json`, and is
  validated with `TimeZoneInfo.FindSystemTimeZoneById` at startup, so a typo in the identifier
  fails fast instead of surfacing the first time a reminder is due.
- **A three-way spec contradiction was found and ruled.** §2 and §12.7 called the zone fixed for
  slice 1; §5.4 and §11.4 said it is bound from configuration and never a literal. Ruled for
  §5.4 and §11.4: two sections, agreeing in detail, and §11.4 explicitly governs every mention
  of Jerusalem in the document. §2 and §12.7 are corrected to match; per-user zones stay
  deferred.
- **No current-local-time member on the interface.** `ILocalTimeResolver` only resolves a given
  reading; F9 adds a member for the current local time when the system prompt needs one to
  state "now" in the user's zone.

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

**F9b · Parse a tool call out of the answer** — spec §5.2, §5.3, §12.3 · **done**
`IAssistantTool`, `CreateTaskTool` as its first implementation, tool definitions added to the
chat request, `tool_calls` parsed out of the response, `CreateTaskRequest` in `Contracts`, and
`IAiClient.AskAsync` changed to return `Result<ToolCall>` in place of `Result<string>`.
*Tests:* free text produces the expected tool call against a WireMock'd provider.
*Settled at F9b:*
- **`IAssistantTool` carries no execution member.** `Name`, `Description` and
  `ParametersJsonSchema` are enough to put a tool definition on the wire request; `ExecuteAsync`
  arrives at F10 as a deliberate modification to this interface, once `ITaskService` has a create
  method for it to call. Considered and rejected: defining it now against nothing, the shape an
  earlier, abandoned full-slice draft took.
- **`ToolCall` and `CreateTaskRequest` both live in `Contracts`**, next to `ErrorCode` and
  `Result<T>` — the first two request/response-style types that project has shipped. `ToolCall`
  carries the model's arguments as a raw JSON string, not a bound value: `AiClient` knows a tool
  was named, not which typed shape to bind its arguments against, so binding stays the calling
  tool's job, arriving with dispatch at F10.
- **`CreateTaskRequest.DueAtLocal` stays a string.** Resolving it against the configured zone is
  `ILocalTimeResolver.Resolve`'s job, called from F10's `CreateTaskTool.ExecuteAsync` once that
  exists — not this slice's, and not a job a parsed `DateTime` could do without a zone to resolve
  against yet. `Notes` and `Priority` are absent from the schema entirely, deferred to F10 and
  F12, the features that give `ReminderTask` somewhere to put them.
- **A model's prose reply is a named failure**, `ErrorCode.ModelReturnedNoToolCall`, appended
  after `ModelReturnedNoAnswer` — distinct from a transport failure or an empty response, both
  already named at F9a. `MessageHandler` gives it its own sentence, telling the owner their
  message was not read as a task rather than folding it into "could not reach the model."
- **Shipped in two commits, in one PR, on purpose**, mirroring F9a-3's own shape: the happy path
  first, with the parsed tool call dereferenced by the null-forgiving operator rather than
  guarded, so a prose-only reply crashed for real before `ModelReturnedNoToolCall` gave that
  failure a name. The crash surfaced two ways — an immediate `NullReferenceException` at the
  `AiClientTests` level, and a `TimeoutException` at the `TelegramListenerTests` level, because
  `TelegramListener` already catches and logs a handler's exception per update rather than
  propagating it.
- **`IAiApi`'s tool-definition JSON Schema travels as a `System.Text.Json.Nodes.JsonNode`, not a
  `JsonElement`.** A `JsonNode` carries no disposal lifetime to trip over; a `JsonElement` parsed
  from a `JsonDocument` stops being readable once that document is disposed.
  `WireMockFixture`'s own seeded payloads already use the same `JsonNode` family.
- **No new NuGet package.** The wire and tool-definition types this slice adds are all `System.Text.Json`.

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
**Reduced by CI:** `.github/workflows/ci.yml` already exists and runs gitleaks, the build, and
both test suites. F14 inherits only the eval workflow and the image-publishing job (spec §11.6).

**Container packaging for the worker** — spec §8, §11.6 · **unscheduled**
There is no `compose.yaml` and no worker `Dockerfile` in this repository, and there never has
been — only `compose.test.yaml`, which serves the test suite. F5b needed the Worker to run
against a real database for the first time, and in doing so found `AGENTS.md` documenting a
`docker compose up -d` workflow that had never worked; F5b corrected that text to describe what
actually exists today. The work itself remains undone: an image build, secret delivery, and a
restart policy. F14 already lists `Dockerfile` and `compose.yaml` among its contents, so this is
not a competing feature number — it is a flag that the gap F5b found is real and directly
observed, not merely anticipated, and worth tracking on its own until F14 is planned.

**Continuous integration** — spec §9 step 1, §11.2, §11.3 · **done**
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
*Settled at CI:*
- **The architecture tests do not get their own stage**, unlike the four stages §11.3 lists
  around them. They live inside `tests/Assistant.UnitTests/Architecture/`, in the same assembly
  as every other unit test, so a separate stage would mean two filtered runs of one assembly —
  and `dotnet test --filter` exits `0` when a filter matches nothing, so a typo in an inverse
  filter would turn an entire stage into a silent no-op. A single unfiltered
  `dotnet test tests/Assistant.UnitTests/Assistant.UnitTests.csproj` cannot skip anything that
  way. §11.3's stage list predates the test projects; the architecture tests run in the stage
  they physically live in.
- **gitleaks runs first in `ci.yml`, not last as §11.3 lists it.** A last-place gitleaks step
  never runs at all if the build fails first, so a pull request that both leaks a secret and
  fails to compile would raise no secret warning. And a leaked credential is already leaked the
  moment it reaches a public repository — ordering only controls how fast the owner is told to
  revoke it, roughly thirty seconds first-in-job against several minutes last-in-job.
- **`ci.yml` narrows spec §11.2's "every push and pull request" to every pull request plus every
  push to `main`, not every push.** A feature-branch push almost always already has an open pull
  request, which the `pull_request` trigger already scans, so covering every push as well would
  run the whole suite twice for the same commit. This sits in tension with the bullet above:
  gitleaks was moved first because time-to-notification matters enough to reorder the whole file,
  yet this narrowing accepts an unbounded delay for a push to a branch with no pull request open
  yet. It is still the right trade — a leaked credential is public the moment it is pushed either
  way, and the pull request scan catches it before merge.
- **The official gitleaks Docker image, not `gitleaks/gitleaks-action`.** The action gates on a
  `GITLEAKS_LICENSE` for organisations; free for a personal repository today, but that is a third
  party's licensing decision sitting inside this build, and the image carries no such gate.
- **gitleaks was pinned to `v8.30.1`, invoked with the `git` subcommand** — `detect` was
  confirmed absent from this line of releases by running `--help` against the pinned image.
  Running it against the repository's full history found no findings, so no `.gitleaks.toml` was
  created.
- **Whether the container hit "dubious ownership" against this repository's bind mount:
  no — the local run showed no such error, so no `--user` flag was added.** A clean result on a
  non-Linux development machine does not settle this — Docker Desktop's bind mount ownership does
  not reproduce what a Linux CI runner's checkout looks like to a container running as root, so
  the real test was the workflow's first run rather than a local one. That run has since
  happened: gitleaks passed on `ubuntu-latest` with no dubious-ownership error, confirming the
  `--user` flag was correctly left out.
- **The workflow's first run caught a race in `ReminderSchedulerTests` that had passed
  locally every time.** The test's `ArmSignallingTimeProvider` signalled `Armed` before its
  inner `FakeTimeProvider.CreateTimer` call had registered the timer, so the fake clock could
  be advanced before anything was listening and that advance was silently lost; an idle
  development machine almost never loses that race, a busier CI runner does. The fix was to
  the test's own harness — reordering the signal to fire after registration — not to
  `ReminderScheduler`.
- **Each of the three failure-detecting stages was proven to go red on its own throwaway
  branch, and none of the three branches was merged.** `ci-break-gitleaks` tripped
  `Scan every commit for secrets` — not with the AWS example key the task brief named, since
  gitleaks's own default ruleset allowlists `AKIAIOSFODNN7EXAMPLE`, AWS's own published
  documentation example, so the branch was repeated with a random value that tripped the
  `generic-api-key` rule instead. `ci-break-warnings` added an unused field,
  not a constructor parameter, to `TaskService` in `Assistant.Impl` and tripped
  `Build with warnings as errors` on `CS0169`, with `Restore` staying green ahead of it — the
  F7 `CS9113` mistake was not repeated. `ci-break-test` flipped one assertion in
  `ScheduledJobBaseTests` and tripped `Unit and architecture tests`, naming the one test that
  failed, with the build staying green ahead of it.
- **`AKIAIOSFODNN7EXAMPLE`, quoted above, is now permanent tracked history — the plan document
  describing the `ci-break-gitleaks` branch quotes it too, so deleting that branch does not
  remove it.** This repository is green today only because gitleaks v8.30.1's default ruleset
  allowlists that exact key; whoever bumps the pinned version should re-run the scan before
  assuming it still passes — a version whose allowlist differs would turn every pull request red
  until a `.gitleaks.toml` is added.

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
