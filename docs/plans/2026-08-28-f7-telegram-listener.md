# F7 — Consume inbound Telegram messages

**Spec:** `docs/design/slice-1-reminders.md` §5.1, §3.6, §7.5
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md`, F7
**Depends on:** F4a/F4b (`INotifier`, `TelegramSettings`, the WireMock stub service)

> **For agentic workers:** execute this plan task by task with
> `superpowers:subagent-driven-development`. One task, one review, one commit.

Until now the assistant has only ever spoken. This is the feature that lets it
listen: a long-poll loop over `getUpdates`, a whitelist that drops anyone who is
not the owner, and an echo reply. No AI — F9 adds that. No database write — F10
adds that. The echo exists so the transport can be proven on its own, and it is
deliberately temporary.

**It also unblocks F6.** A button tap arrives as a `callback_query` on this same
`getUpdates` stream. There is no polling loop anywhere in `src/` today, so F6 —
which the backlog listed as needing only F5b — cannot be built until this lands.

## Global Constraints

- **YAGNI.** Build only what F7's stated behaviours need. A type arrives with its
  consumer, never before it.
- **Open/closed**, as the backlog bounds it: new behaviour is a new class where a
  seam already exists. An abstraction with one implementation is a guess.
- **`TaskService` is the single writer.** F7 touches no repository and no task.
- **Test business use cases, not implementation.** Assert that a reply arrived,
  not that a field changed.
- **Primary constructors** on every class that takes arguments. Never a separate
  constructor declaration.
- **`<summary>` XML tags span three lines** — open tag, text, close tag.
- **Gherkin summaries** on tests, one clause per line, `When` / `And` / `Then`.
- **Plain xUnit `Assert`.** No Shouldly, no FluentAssertions.
- **No emoji anywhere** — source, tests, docs, commit messages, or bot message
  text (conventions §12.6).
- Warnings are errors. `dotnet clean` before any build you intend to trust.
- No `Version` attribute on a `PackageReference`; versions live in
  `Directory.Packages.props`. **F7 adds no package**, with the one contingency
  named in Task 1 Step 4.

## Verified facts this plan rests on

Each of these was checked against the installed package or the running stub
container before this plan was written. Do not re-derive them; do notice if one
turns out to be false, because the plan is wrong if so.

| Fact | How it was checked |
| :--- | :--- |
| `Telegram.Bot` sends **every** API call as HTTP `POST`, `getUpdates` included | `RequestBase<TResponse>`'s constructor assigns `HttpMethod.Post` (IL) |
| `GetUpdates(this ITelegramBotClient, int? offset, int? limit, int? timeout, IEnumerable<UpdateType>?, CancellationToken)` returns `Task<Update[]>` | IL call site in `Telegram.Bot.dll` 22.10.2.1 |
| `Update.Id`, `Update.Message`, `Update.CallbackQuery`; `Message.Text`, `Message.Chat`, `Message.From` | XML doc members |
| `UpdateType.Message` and `UpdateType.CallbackQuery` exist | XML doc members |
| WireMock.Net matches on request **body**, and **lower `Priority` wins** | Seeded two competing mappings on the live stub; the priority-1 body matcher beat the priority-10 catch-all |
| A WireMock response `Delay` is milliseconds and applies per mapping | Seeded `"Delay": 1000`; the matching call took 1.03s, the non-matching one 0.03s |
| `DELETE /__admin/mappings/{guid}` removes one mapping and leaves the others | Deleted seeded mappings; the `sendMessage` mapping survived and still answered |

**A scenario-based stub was tried first and rejected.** WireMock scenarios cycle:
with an entry mapping and a `WhenStateIs: "drained"` mapping, call 3 served the
update *again*. Pinning the drained state with `SetStateTo: "drained"` fixes the
cycling but then requires `POST /__admin/scenarios/reset` between tests, and —
fatally — it drains by **call count**, so a listener that never advances its
offset still passes. Body matching on the offset drains by the same signal real
Telegram uses, which is why Decision E chooses it.

## Decisions this plan makes — review these first

### A. One class, not two

The approved design said `TelegramListener` (loop) plus a `MessageHandler` (work),
mirroring F5b's `ReminderScheduler`/`DueReminderJob` split. **On writing it out,
that split does not earn its keep here.**

F5b's split is load-bearing because `ReminderScheduler` injects
`IEnumerable<IScheduledJob>` — a real seam with a designed second implementation
(`DailyBriefJob`). F7's listener would call exactly one handler through no
interface at all. Two classes with no seam between them is ceremony, and the
backlog's own rule settles it: *"an abstraction with one implementation is a
guess, not a seam."*

So `TelegramListener` owns the loop, the offset, the whitelist, and the reply. It
is about 60 lines. F6 splits it if and when two update kinds make the split real.

**Cost if this is wrong:** F6 extracts a handler from a 60-line class it is
already editing.

### B. The whitelist compares `Message.Chat.Id`, not `Message.From.Id`

Spec §5.1 says *"reject any sender other than the configured owner ID"*.
"Sender" is `From.Id`, a **user** id; `TelegramSettings.OwnerChatId` is a **chat**
id. In a private one-to-one bot chat they are the same number.

Comparing `Chat.Id` reuses the setting that already exists. Comparing `From.Id`
would require a new `OwnerUserId` setting that, for this product, always holds the
identical value — a second source of truth for one number.

It is also correct for the case that matters: a stranger who messages the bot gets
their own chat id, which does not match, and is dropped. It stays correct if the
bot is ever added to a group, where `Chat.Id` is the group's — also not the owner,
also dropped.

`From` is additionally **nullable** on `Message` and absent from the canned update
bodies the stub serves, so `Chat.Id` is the field that is reliably present.

### C. The reply goes through `INotifier`, unchanged

No new interface, no new method. `INotifier`'s recipient is configuration, so it
is structurally **incapable** of replying to a stranger even if the whitelist were
bypassed — the defect would be a missing reply to the owner, not a leak to
someone else. That property is worth keeping and is the reason not to add a
`SendToAsync(chatId, ...)` overload here.

### D. Advance the offset **before** handling — the opposite of F5b's ordering

F5b sends and then marks, so a crash re-delivers rather than loses. F7 does the
reverse: `_offset = update.Id + 1` runs *before* the update is handled.

They differ because the failure modes differ. A lost reminder is this product's
core failure — the whole premise is that nothing gets dropped. A lost echo costs
the user one retype. Against that, marking after handling means a message that
always throws is re-polled forever at full speed: one poison message wedges the
bot and hammers Telegram. Advancing first makes a poison message cost exactly one
dropped reply.

Handling is additionally wrapped in its own try/catch **inside** the loop over the
batch, not around it, so update three still runs when update two throws.

### E. The stub answers `getUpdates` by matching the offset in the request body

Two mappings per test, seeded through the admin API:

| Mapping | Matches | Serves | Priority |
| :--- | :--- | :--- | :--- |
| pending | any `getUpdates` POST | the seeded updates | 10 |
| drained | `getUpdates` POST whose body contains `"offset":<max id + 1>` | `{"ok":true,"result":[]}` | 1 |

Lower priority wins, so once the listener sends the advanced offset it gets the
empty result and keeps getting it. That is exactly Telegram's own semantics, and
it is stateless — no scenario reset, no call counting.

**This is what makes the offset testable through a business outcome.** A listener
that never advances its offset keeps matching the *pending* mapping, so it echoes
the same message over and over. Task 3 asserts one reply and gets hundreds. The
test cannot fail marginally.

### F. The drained response carries a one-second delay

Real Telegram holds a `getUpdates` call open for the `timeout` it is given. WireMock
answers instantly, so a correct listener would spin at full speed against the stub
— thousands of requests during a test, and a pegged core during a local
`dotnet run` against the stub.

A `"Delay": 1000` on the drained mapping throttles the idle loop to roughly one
poll per second. It does not slow any test: the reply comes from the *first* poll,
which matches the pending mapping and has no delay.

### G. The integration tests drive the real hosted service

`Assistant.IntegrationTests` references only `Assistant.Worker`, reaching
`Assistant.Impl` transitively, and `Assistant.Impl`'s `InternalsVisibleTo` names
only `Assistant.UnitTests`. **An integration test cannot name `TelegramListener`.**

That is not a problem to work around — it is the constraint that keeps these tests
honest. They resolve `IHostedService` from the container built by
`AddAssistantListener()`, start it, and assert on what reaches the stub, exactly
as F5b resolved `IScheduledJob` through `AddAssistantScheduler()`. Do **not** add
an `InternalsVisibleTo` for the integration project, and do **not** promote
`TelegramListener` to public.

### H. `allowedUpdates` is `[UpdateType.Message]`, and F6 must widen it

Passing an explicit allow-list keeps callback queries out of the stream until
something handles them. **F6 must add `UpdateType.CallbackQuery` to this array**
or its buttons will silently never fire — the single most likely way to lose a
day on F6. It is called out here, in the code comment, and in the backlog.

### I. No unit tests

All three behaviours are business outcomes observable through the stub, and
`AGENTS.md` forbids a unit test for behaviour an integration test already covers.
`Assistant.UnitTests` is untouched by this feature.

## What F7 does NOT include

Say so in the PR body rather than letting a reviewer wonder.

- **No AI.** The reply is the user's own text. F9 brings `IChatClient`.
- **No `chat_messages` row.** Spec §5.1 persists inbound messages; the backlog
  puts `ChatMessage` and its table at F13. Nothing here writes to the database.
- **No typing indicator.** Spec §5.1 calls for one, refreshed every 4s while a
  reply is composed. There is nothing to compose — the echo is instant. It
  arrives with the first slow reply, at F9.
- **No callback queries.** F6, per Decision H.
- **No inline keyboard on the reply.** Spec §5.1's flow ends with a reply carrying
  one; the backlog puts that at F10. This matters beyond scope: F4b's
  `TelegramNotifierTests` asserts the whole `sendMessage` payload with
  `Assert.Equivalent(strict: true)`, so the first feature that attaches
  `reply_markup` breaks that test. F7 attaches none, so it stays green — if it goes
  red, something added a keyboard that should not have.
- **No exactly-once delivery.** A restart re-polls from Telegram's last
  unconfirmed update, so an update handled but not yet confirmed can be handled
  twice. That is the same at-least-once trade F5b made deliberately. Stated
  rather than tested, because a test asserting exactly-once would be asserting
  something false.
- **No rate limiting or 429 handling.** Spec §6.5 owes Polly retry honouring
  `retry_after`; it is unscheduled and F7 does not pull it forward.

## File Structure

```
src/Assistant.Impl/
    Telegram/TelegramListener.cs          new
    ImplServiceCollectionExtensions.cs    + AddAssistantListener

src/Assistant.Worker/Program.cs           + AddAssistantListener()

tests/Assistant.WireMock/
    TelegramStubs.cs                      + default getUpdates mapping (empty, delayed)

tests/Assistant.IntegrationTests/
    Infrastructure/WireMockFixture.cs     + SeedUpdatesAsync, WaitForSentMessagesAsync,
                                            seeded-mapping cleanup in ResetAsync
    Telegram/TelegramListenerTests.cs     new

docs/design/slice-1-reminders.md          §5.1 deferred notes
docs/design/2026-08-22-slice-1-feature-backlog.md
                                          F7 settled, F6 dependency corrected,
                                          CI gap recorded
AGENTS.md                                 correct the false CI claim
```

---

## Task 1: The owner's message comes back

**Files:**
- Create: `src/Assistant.Impl/Telegram/TelegramListener.cs`
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`
- Modify: `src/Assistant.Worker/Program.cs`
- Modify: `tests/Assistant.WireMock/TelegramStubs.cs`
- Modify: `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`
- Create: `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`

**Produces:** `AddAssistantListener()`, and a `WireMockFixture` that can seed
inbound updates and wait for outbound messages. Tasks 2 and 3 add tests only.

- [ ] **Step 1: Give the stub a default `getUpdates` answer**

Without this, a locally-run worker polling the stub gets "No matching mapping
found", fails to deserialise it, and logs an error every five seconds forever.

In `tests/Assistant.WireMock/TelegramStubs.cs`, add alongside the existing
`sendMessage` mapping:

```csharp
private const string NoUpdatesResponse = """{"ok":true,"result":[]}""";
```

```csharp
server
    .Given(Request.Create().WithPath("/bot*/getUpdates").UsingPost())
    .AtPriority(100)
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", "application/json")
        .WithBody(NoUpdatesResponse)
        .WithDelay(TimeSpan.FromSeconds(1)));
```

Priority 100 is deliberately the weakest: every mapping a test seeds must win over
it. The one-second delay is Decision F — it stops an idle listener spinning.

Extend the method's `<remarks>` to say why the default exists and why it is
delayed. Keep the three-line `<summary>` convention.

- [ ] **Step 2: Rebuild the stub container**

The stub runs from an image, so a source change does nothing until it is rebuilt.
Forgetting this produces a test failure that looks like a code bug.

```bash
docker compose -f compose.test.yaml up -d --build wiremock
curl -s -X POST -H 'Content-Type: application/json' -d '{}' \
  http://localhost:58080/bot123:ABC/getUpdates
```

Expected: `{"ok":true,"result":[]}` after about one second.

- [ ] **Step 3: Teach the fixture to seed updates and wait for replies**

In `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`. The two
mapping ids are constants so `ResetAsync` can delete exactly what a test seeded
and leave the stub's own mappings alone.

```csharp
private static readonly Guid PendingUpdatesMapping =
    new("f7000000-0000-0000-0000-000000000001");

private static readonly Guid DrainedUpdatesMapping =
    new("f7000000-0000-0000-0000-000000000002");
```

```csharp
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

    await PutMappingAsync(PendingUpdatesMapping, priority: 10, bodyPattern: null,
        result: pending, delayMs: null);

    await PutMappingAsync(DrainedUpdatesMapping, priority: 1,
        bodyPattern: $"*\"offset\":{nextOffset}*", result: new JsonArray(), delayMs: 1000);
}

private async Task PutMappingAsync(
    Guid id, int priority, string? bodyPattern, JsonNode result, int? delayMs)
{
    var request = new JsonObject
    {
        ["Path"] = new JsonObject
        {
            ["Matchers"] = new JsonArray(new JsonObject
            {
                ["Name"] = "WildcardMatcher",
                ["Pattern"] = "/bot*/getUpdates",
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
        ["StatusCode"] = 200,
        ["Headers"] = new JsonObject { ["Content-Type"] = "application/json" },
        ["Body"] = new JsonObject { ["ok"] = true, ["result"] = result }.ToJsonString(),
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
```

**Build the mapping with `JsonObject`, not string interpolation.** WireMock's
`Response.Body` is a *string* holding JSON, so the payload is JSON nested inside
JSON. Written as a raw interpolated string, the escaping is hand-rolled and the
closing `}}}}` sits ambiguously against the `{{...}}` interpolation delimiter.
`JsonObject` removes both problems, and it is what makes the optional `Body`
matcher and `Delay` fields readable as conditionals.

Then the wait helper the tests synchronise on, on the same class:

```csharp
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
```

Add the record beside `SendMessagePayload`, with a three-line `<summary>` and
`<param>` docs:

```csharp
public sealed record InboundUpdate(int UpdateId, long ChatId, string Text);
```

Extend `ResetAsync` so a seeded mapping never survives into the next test. `404`
is expected whenever the previous test seeded nothing, so it is not an error:

```csharp
public async Task ResetAsync()
{
    foreach (var id in new[] { PendingUpdatesMapping, DrainedUpdatesMapping })
    {
        (await _http.DeleteAsync($"{Url}/__admin/mappings/{id}")).Dispose();
    }

    (await _http.DeleteAsync($"{Url}/__admin/requests")).EnsureSuccessStatusCode();
}
```

Add `using System.Text;` for `Encoding` and `using System.Text.Json.Nodes;` for
`JsonObject` and `JsonArray`. `System.Text.Json` is already imported.

- [ ] **Step 4: Write the failing test**

Create `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`.

`DisposeAsync` must stop the listener before the next test seeds anything, or a
still-running loop from the previous test pollutes it.

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
        services.AddAssistantListener();
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetServices<IHostedService>().Single();

        await wireMock.ResetAsync();
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
    /// Then a reply comes back carrying what they sent.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerSendsAMessage_RepliesWithTheirText()
    {
        // Arrange
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal("call the bank", sent[0].Text);
    }
}
```

`AddLogging()` is needed because `TelegramListener` takes an `ILogger<T>`; no
existing integration test calls it, so it is the one line here that might not
compile. It lives in the `Microsoft.Extensions.Logging` package, which this project
reaches transitively through `Assistant.Worker`'s `Microsoft.Extensions.Hosting`.
**If it does not resolve**, add `Microsoft.Extensions.Logging` to
`Directory.Packages.props` at `10.0.11` — matching every other
`Microsoft.Extensions.*` entry there — and reference it from
`Assistant.IntegrationTests.csproj`. That is the one package this plan permits, and
only if the build demands it. Say so in the commit message if it happens.

- [ ] **Step 5: Run it and watch it fail for the right reason**

```bash
dotnet test tests/Assistant.IntegrationTests --filter TelegramListenerTests
```

Expected: a compile error — `AddAssistantListener` does not exist. That is the
correct first failure. If it compiles, something already registers a listener and
this plan's assumptions are wrong.

- [ ] **Step 6: Write the listener**

Create `src/Assistant.Impl/Telegram/TelegramListener.cs`. The structure mirrors
`ReminderScheduler` deliberately, including the outer `OperationCanceledException`
catch and its comment.

```csharp
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Polls Telegram for inbound updates and answers the ones the owner sent.
/// </summary>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="notifier">Where a reply is delivered.</param>
/// <param name="timeProvider">Supplies the delay applied after a failed poll.</param>
/// <param name="logger">Where a failure is recorded.</param>
/// <remarks>
/// The offset is advanced before an update is handled, not after. Handling first would
/// re-poll an update whose handler always throws, forever and at full speed, so one
/// malformed message would wedge the assistant and hammer Telegram. Advancing first
/// costs at most one dropped reply instead. This is the opposite of the reminder path's
/// send-then-mark ordering, because there a lost message is the product's core failure
/// while here it costs the owner one retype.
/// </remarks>
internal sealed class TelegramListener(
    ITelegramBotClient bot,
    TelegramSettings settings,
    INotifier notifier,
    TimeProvider timeProvider,
    ILogger<TelegramListener> logger) : BackgroundService
{
    private const int LongPollSeconds = 30;

    private static readonly TimeSpan PollFailureBackoff = TimeSpan.FromSeconds(5);

    private int? _offset;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
        Update[] updates;

        try
        {
            updates = await bot.GetUpdates(
                offset: _offset,
                limit: null,
                timeout: LongPollSeconds,

                // F6 must add UpdateType.CallbackQuery here, or its buttons never fire.
                allowedUpdates: [UpdateType.Message],
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Polling Telegram for updates failed; the loop continues.");
            await Task.Delay(PollFailureBackoff, timeProvider, ct);
            return;
        }

        foreach (var update in updates)
        {
            _offset = update.Id + 1;

            try
            {
                await HandleAsync(update, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex, "Handling update {UpdateId} failed; the loop continues.", update.Id);
            }
        }
    }

    private async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (update.Message is not { Text: { } text } message
            || message.Chat.Id != settings.OwnerChatId)
        {
            return;
        }

        await notifier.SendAsync(text, ct);
    }
}
```

The whitelist is written here rather than in Task 2 because the same `if` carries
the "is this a text message at all" guard, and splitting one condition across two
commits would leave Task 1 with a listener that replies to strangers. **Task 2 is
the test that pins it** — without that test the whitelist rests on nothing, which
is exactly the gap F5b found in its send-then-mark ordering.

- [ ] **Step 7: Register it**

In `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`, beside
`AddAssistantScheduler`:

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
    services.AddHostedService<TelegramListener>();
    return services;
}
```

In `src/Assistant.Worker/Program.cs`, after `builder.Services.AddAssistantScheduler();`:

```csharp
builder.Services.AddAssistantListener();
```

- [ ] **Step 8: Run the test**

```bash
dotnet test tests/Assistant.IntegrationTests --filter TelegramListenerTests
```

Expected: PASS.

- [ ] **Step 9: Confirm the offset actually reaches the wire**

The whole test design in Decision E depends on the SDK serialising the offset as
`"offset":11`. Verify it rather than trust it:

```bash
curl -s http://localhost:58080/__admin/requests \
  | python3 -c "import json,sys; [print(e['Request']['Body']) for e in json.load(sys.stdin) if e['Request']['Path'].endswith('/getUpdates')]"
```

Expected: at least one body containing `"offset":11`. **If the shape differs — a
space after the colon, a different casing — the drained mapping in
`SeedUpdatesAsync` never matches, and Task 3 will fail with hundreds of replies.
Fix the pattern in `SeedUpdatesAsync` to match what is actually sent, and say so
in the commit message.**

- [ ] **Step 10: Commit**

```bash
git add src/Assistant.Impl src/Assistant.Worker tests/Assistant.WireMock tests/Assistant.IntegrationTests
git commit -m "feat: poll Telegram for inbound messages and echo the owner's"
```

---

## Task 2: A stranger gets nothing

**Files:**
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`

**Consumes:** `SeedUpdatesAsync`, `WaitForSentMessagesAsync`, and the whitelist
written in Task 1 Step 6.

- [ ] **Step 1: Write the failing test**

Add to `TelegramListenerTests`:

```csharp
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
    Assert.Equal("call the bank", Assert.Single(sent).Text);
}
```

- [ ] **Step 2: Run it**

```bash
dotnet test tests/Assistant.IntegrationTests --filter TelegramListenerTests
```

Expected: PASS, because Task 1 Step 6 wrote the whitelist.

- [ ] **Step 3: Prove the test can fail**

A test that has never failed proves nothing. Temporarily delete
`|| message.Chat.Id != settings.OwnerChatId` from `HandleAsync` and re-run.

Expected: FAIL — two messages reach the stub, `Assert.Single` throws. Restore the
condition and confirm the suite is green again. Do not commit the broken version.

- [ ] **Step 4: Commit**

```bash
git add tests/Assistant.IntegrationTests
git commit -m "test: only the owner gets an answer"
```

---

## Task 3: The same message is not answered twice

**Files:**
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`

**Consumes:** the offset advance written in Task 1 Step 6, and the
offset-matched drained mapping from Decision E.

- [ ] **Step 1: Write the test**

```csharp
/// <summary>
/// When a message has been answered
/// And the listener keeps polling
/// Then it is not answered again.
/// </summary>
/// <remarks>
/// The only test in this suite that waits on wall-clock time, and it is worth the
/// cost: a listener that fails to advance its offset is served the same update on
/// every poll, and the stub answers an unadvanced poll with no delay at all. The
/// failure is therefore hundreds of replies inside the settle window, not two, so
/// this cannot fail marginally. A false pass would need the machine to make no
/// progress for the whole window.
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
```

- [ ] **Step 2: Run it**

```bash
dotnet test tests/Assistant.IntegrationTests --filter TelegramListenerTests
```

Expected: PASS.

- [ ] **Step 3: Prove the test can fail**

Temporarily change `_offset = update.Id + 1;` to `_offset = update.Id;` — an
off-by-one that leaves the update unconfirmed, which is the realistic version of
this bug rather than deleting the line outright.

Expected: FAIL, with a large message count. Restore the line, re-run, confirm
green. Do not commit the broken version.

- [ ] **Step 4: Run the whole suite three times**

Anything driven by a background loop is intermittent by nature, so one green run
proves less than it appears to.

```bash
for i in 1 2 3; do dotnet test tests/Assistant.IntegrationTests || break; done
```

Expected: 26 integration tests pass, three times.

- [ ] **Step 5: Commit**

```bash
git add tests/Assistant.IntegrationTests
git commit -m "test: an answered message is not answered again"
```

---

## Task 4: Record what F7 settled, and correct what it disproved

**Files:**
- Modify: `docs/design/slice-1-reminders.md`
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`
- Modify: `AGENTS.md`

Do **not** edit the dated documents under `docs/plans/` or `docs/2026-08-16-*`.
Those are point-in-time records; editing them rewrites history rather than
correcting a live claim. Same call F5b made.

- [ ] **Step 1: Mark the spec's deferrals**

In `docs/design/slice-1-reminders.md` §5.1, after the flow block, add deferral
notes in the style §6.1 and §6.2 already use:

- The `persist inbound message to chat_messages` step: **Deferred** — `ChatMessage`
  and its table arrive at F13. F7 writes nothing to the database.
- The `start "typing…" indicator` step: **Deferred** — there is nothing to compose
  until F9 makes a model call. F7's reply is instant.

Do not renumber or restructure §5.1.

- [ ] **Step 2: Correct the backlog's dependency graph**

In `docs/design/2026-08-22-slice-1-feature-backlog.md`:

§6 currently reads *"F6 needs F5b. F7 is independent of F1-F6 and could move
earlier if you want to talk to the bot sooner."* Both halves understate the truth.
Replace with a statement that **F6 needs F7**: a button tap arrives as a
`callback_query` on the same `getUpdates` stream, so without the listener there is
nothing to route. Note that the binding spec's §9 already had this right — the
Telegram round-trip is step 5 and buttons are step 7 — and that the backlog's
numbering, not the spec, was wrong.

Add to the **F6** entry that it depends on F7's listener, and that it must add
`UpdateType.CallbackQuery` to the `allowedUpdates` array in `TelegramListener`.

- [ ] **Step 3: Record F7 as done**

Mark the F7 entry **done** and add a `*Settled at F7:*` list covering: one class
rather than the two the design proposed, and why (Decision A); the whitelist on
`Chat.Id` rather than `From.Id`, and why (Decision B); offset-before-handling and
how it differs from F5b's send-then-mark (Decision D); the offset-matched stub
mapping and the scenario approach it replaced (Decision E); the one-second drained
delay (Decision F); and that the integration project cannot name internal `Impl`
types, which is why the tests drive the hosted service (Decision G).

- [ ] **Step 4: Record the missing CI**

Add an **unscheduled** entry beside "Container packaging for the worker", in the
same shape. The facts:

- There is no `.github/workflows` directory in this repository and there never has
  been. No pull request has ever been checked by a machine.
- Spec §9 step 1 lists "GitHub Actions workflow running them" as part of the very
  first step, before any code. It was skipped.
- Spec §11.2 states gitleaks "runs in CI on every push and pull request". It does
  not run anywhere. During F5b a live Postgres password reached the tracked, public
  `appsettings.json` and was caught by a human reading a diff.
- §11.3 already specifies the stages: restore, build with warnings as errors,
  architecture tests, unit tests, integration tests, gitleaks.
- F14 lists `.github/workflows/ci.yml` among its contents, so this is not a
  competing feature number — it flags that a promise the documents already make is
  unbacked, which is worse than not having made it.

- [ ] **Step 5: Stop AGENTS.md claiming a CI that does not exist**

`AGENTS.md:9` opens the Commands section with *"Every command below is run by CI,
so if one fails here it fails there too."* That is false. Replace it with a
sentence saying these are the commands to run before opening a pull request, and
that nothing runs them automatically yet — pointing at the backlog entry from
Step 4.

- [ ] **Step 6: Commit**

```bash
git add docs AGENTS.md
git commit -m "docs: record what F7 settled and correct the dependency graph"
```

---

## Self-review

- [ ] `dotnet clean && dotnet build` — zero warnings, zero errors
- [ ] Unit tests green (20, unchanged — F7 adds none, per Decision I)
- [ ] Integration tests green (26), run three times
- [ ] The stub container was rebuilt (`up -d --build wiremock`), not just restarted
- [ ] Both deliberate-break checks in Tasks 2 and 3 were actually performed, and
      neither broken version was committed
- [ ] The `getUpdates` request body was inspected and the offset really is
      `"offset":11` on the wire (Task 1 Step 9)
- [ ] `TelegramListener` is `internal`, and no `InternalsVisibleTo` was added for
      `Assistant.IntegrationTests`
- [ ] `TelegramListener` names no repository type —
      `DependencyRuleTests.Only_TaskService_references_ITaskRepository_in_Impl`
      scans it automatically and must stay green
- [ ] `TelegramNotifierTests` still passes untouched: F7 attaches no
      `reply_markup`, so its `strict: true` payload assertion is undisturbed
- [ ] No new package reference, unless the `AddLogging` contingency in Task 1
      Step 4 fired — in which case exactly one was added and the commit says so
- [ ] No emoji in any changed file, including commit messages
- [ ] `allowedUpdates` carries the comment naming what F6 must change
- [ ] `docs/e2e-local.md` still describes reality — a worker run against the stub
      now also polls `getUpdates`; if the runbook's output samples are now
      misleading, correct them
- [ ] Diff under 1000 lines excluding this plan
