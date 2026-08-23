# F4 — Send a Telegram message

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** `INotifier` and `TelegramNotifier`, sending HTML-parse-mode messages through
`Telegram.Bot`, proven against WireMock standing in for the real API.

**Architecture:** `Telegram.Bot` is an SDK, not a hand-rolled HTTP client, so spec §12.3's Refit
rule does not apply to it. Its base address is a constructor option, which is what makes it
WireMock-testable with no production seam. `TelegramNotifier` lives in `Impl/Telegram` and is the
only type that names an SDK type.

**Tech Stack:** Telegram.Bot 22.10.2.1, WireMock.Net 2.15.0, xUnit 2.9.3.

**Spec:** `docs/design/slice-1-reminders.md` §3.1, §6.5, §7.1, §7.3, §12.3.
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F4.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error in `src/` only.
- **Every class with arguments uses a primary constructor** (§12.5). No separate constructors.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first.
- Every `<summary>` is three lines. Primary constructor parameters are documented on the class.
- Central package management: versions go in `Directory.Packages.props`, never inline (NU1008).
- **YAGNI:** this feature introduces nothing it does not test.
- PR budget: 1000 lines. Estimated ~180 of code and tests, plus this plan.

---

## Verified before writing this plan

Every claim below was measured against the real packages, not recalled.

**The SDK's surface (v22 renamed things):**

```
TelegramBotClientOptions(String token, String baseUrl = null, Boolean useTestEnvironment = False)
TelegramBotClient(TelegramBotClientOptions options, HttpClient httpClient = null, ...)
TelegramBotClientExtensions.SendMessage(ITelegramBotClient, ChatId, String text, ParseMode parseMode, ...)
ParseMode members: None, Markdown, Html, MarkdownV2
```

It is `SendMessage`, **not** `SendTextMessageAsync` — that name belongs to older major versions
and does not exist here.

**The wire format**, captured from a live `WireMockServer` with the client pointed at it:

```
PATH   /bot123456:TESTTOKEN/sendMessage
METHOD POST
CTYPE  application/json; charset=utf-8
BODY   {"chat_id":472619570,"text":"Reminder: call the bank_now *urgent*","parse_mode":"Html"}
```

Two things follow. The stub must match path `/bot*/sendMessage`, and `parse_mode` serialises as
`"Html"` — capital H, lowercase tml — which is what the assertion must expect.

**The response the SDK requires.** It deserialises the envelope, so a bare `{}` will not do:

```json
{"ok":true,"result":{"message_id":42,"date":1756000000,
 "chat":{"id":472619570,"type":"private"},"text":"hi"}}
```

**`_` and `*` survive untouched** under `ParseMode.Html` — confirmed in the captured body above.
That is the whole reason spec §3.3 chose HTML over MarkdownV2, which has 18 escape-sensitive
characters and would 400 on a live reminder.

---

## Decisions this plan makes — review these first

### A. `INotifier.SendAsync(string text, CancellationToken ct)` — the recipient is configuration

The notifier owns the owner chat id; callers do not pass it. This is a single-user product
(spec §1), so a recipient parameter would be a parameter with exactly one possible value at every
call site, and every caller would have to fetch it from somewhere.

Rendering stays out too. F5 composes the text; F4 delivers it. That keeps `Contracts` empty until
F5 needs it, per the backlog.

### B. The test asserts the whole request payload, not "a message was sent"

Extending the review pattern from F3 to the wire format: the captured JSON body is parsed into a
record and compared with `Assert.Equivalent(expected, actual, strict: true)` against a hardcoded
expected value. Spec §7.3 demands a delivery assertion pin count, recipient, and exact text; this
pins those plus `parse_mode`, in one assertion.

**A deliberate consequence:** when F6 adds an inline keyboard, `reply_markup` appears in the body
and `strict: true` fails this test. That is correct — the delivery payload changing is exactly
the thing a reviewer should be made to look at, not something that should slip through.

### C. WireMock tests live in `Assistant.IntegrationTests`, but need no Postgres

Spec §7.1 puts WireMock at the integration level. These tests do **not** join
`PostgresCollection`, so the Postgres fixture never initialises for them and they run with no
Docker. They own a WireMock server per class instead.

### D. Registration mirrors `AddAssistantRepository`

`AddAssistantTelegram(services, botToken, ownerChatId, baseUrl)` — four explicit arguments, no
options class. Fail-fast options validation with `appsettings.{Environment}.json` is F14's, per
the backlog, and inventing an options type here would build something nothing validates yet.

`baseUrl` is nullable and defaults to null, which is the SDK's own default for "the real API".
Tests pass the WireMock URL; production passes nothing.

### E. The architecture test forbidding `Impl.Services` → `Telegram.Bot` waits for F5

Spec §7.5 lists it, and it is the guard that keeps the SDK out of service code. There is no
`Impl/Services` namespace yet — `Assistant.Impl` is empty until this feature — so the test would
pass vacuously over zero types. It arrives with F5, which creates the first service.

---

## What F4 does NOT include, and why

| Excluded | Returns at |
| :--- | :--- |
| Rendering a reminder into text, `ReminderNotification` | F5 / F10 |
| Inline keyboards and `reply_markup` | F6 |
| Polly retry on 429 honouring `retry_after` (spec §6.5) | F14, with the resilience handler |
| Receiving messages, `TelegramListener` | F7 |
| Options validation, `appsettings.{Environment}.json` | F14 |
| `HostApplicationFactory` booting the real host (spec §7.1) | F5, which is the first feature with a host worth booting |

---

## File Structure

| Path | Responsibility |
| :--- | :--- |
| `Directory.Packages.props` | **Modify.** Add Telegram.Bot and WireMock.Net versions. |
| `src/Assistant.Interfaces/INotifier.cs` | **Create.** One method. |
| `src/Assistant.Impl/Assistant.Impl.csproj` | **Modify.** Reference Telegram.Bot and DI abstractions. |
| `src/Assistant.Impl/Telegram/TelegramNotifier.cs` | **Create.** The only type naming an SDK type. |
| `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` | **Create.** `AddAssistantTelegram`. |
| `tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj` | **Modify.** Add WireMock.Net. |
| `tests/Assistant.IntegrationTests/Infrastructure/TelegramApiServer.cs` | **Create.** WireMock stub + payload capture. |
| `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs` | **Create.** One test, two cases. |

**Interfaces produced:**
- `INotifier.SendAsync(string text, CancellationToken ct)` → `Task`
- `ImplServiceCollectionExtensions.AddAssistantTelegram(IServiceCollection, string botToken, long ownerChatId, string? baseUrl = null)`
- `TelegramApiServer` with `string Url`, `IReadOnlyList<SendMessagePayload> SentMessages`, `IAsyncDisposable`

---

## Test design

One test method, two cases, in a class that owns a WireMock server.

| Test | Kind | What it documents |
| :--- | :--- | :--- |
| `SendAsync_Text_PostsOneMessageToTheOwner` | `[Theory]` ×2 | Exactly one request, to the owner, with the exact text and HTML parse mode |

An earlier draft of this plan added a second test asserting that nothing is sent when nothing is
called. It was cut on review: "not calling a method has no effect" is not a business requirement,
and `Assert.Single` in the test above already pins the count at exactly one.

**Equivalence classes for the text.** Plain text, and text containing the MarkdownV2-sensitive
characters `_` and `*`. Those are the two classes that matter, because the choice of HTML parse
mode exists precisely so the second class does not 400 (spec §3.3). One representative each — the
`[Theory]` cases are `"Call the bank"` and `"Call the bank_now *urgent*"`.

**The assertion**, per Decision B and spec §7.3:

```csharp
var expected = new SendMessagePayload(OwnerChatId, text, "Html");
Assert.Equivalent(expected, Assert.Single(telegram.SentMessages), strict: true);
```

`Assert.Single` pins the count at exactly one and yields the item, so count and content are one
assertion rather than two. Measured at F3: `strict: true` is required — without it an extra
member in the actual payload goes undetected.

**Deliberately not tested, and why:**

| Not tested | Reason |
| :--- | :--- |
| That Telegram accepts the message | That is Telegram's behaviour, and there is no credential to test it with. |
| A 429 or 400 response | No retry policy exists until F14. A test would assert the absence of behaviour. |
| Every `ParseMode` value | Only `Html` is ever passed. The others are the SDK's. |
| The SDK's own serialisation | Framework behaviour. We assert the payload our call produces, once. |

---

## Task 1: `INotifier`, `TelegramNotifier`, and the WireMock harness

**Files:** as listed in File Structure.

**Interfaces:**
- Consumes: nothing from earlier features. This is the first vertical that does not touch Postgres.
- Produces: `INotifier.SendAsync`, `AddAssistantTelegram`, `TelegramApiServer`.

- [ ] **Step 1: Add the packages**

In `Directory.Packages.props`, add in alphabetical position:

```xml
    <PackageVersion Include="Telegram.Bot" Version="22.10.2.1" />
    <PackageVersion Include="WireMock.Net" Version="2.15.0" />
```

Then add `<PackageReference Include="Telegram.Bot" />` and
`<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />` to
`src/Assistant.Impl/Assistant.Impl.csproj`, and `<PackageReference Include="WireMock.Net" />` to
`tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj`.

Never write an inline `Version=` on a `PackageReference` — that is NU1008 and the build fails.

- [ ] **Step 2: Create `INotifier`**

`src/Assistant.Interfaces/INotifier.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>
/// Delivers a message to the person the assistant works for.
/// </summary>
/// <remarks>
/// The recipient is configuration, not a parameter: this is a single-user assistant, so every
/// call site would otherwise pass the same value. Rendering is the caller's job — a notifier
/// delivers text it is given and never sees a database shape.
/// </remarks>
public interface INotifier
{
    /// <summary>
    /// Sends a message to the owner.
    /// </summary>
    /// <param name="text">The message body, already rendered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    Task SendAsync(string text, CancellationToken ct);
}
```

- [ ] **Step 3: Write the WireMock harness**

`tests/Assistant.IntegrationTests/Infrastructure/TelegramApiServer.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>
/// The Telegram Bot API, stubbed in process, capturing what was sent to it.
/// </summary>
/// <remarks>
/// The stub matches <c>/bot*/sendMessage</c> because the SDK puts the token in the path. The
/// response is the real envelope shape: the client deserialises it, so a bare object is rejected.
/// </remarks>
public sealed class TelegramApiServer : IAsyncDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    /// <summary>
    /// Initialises the stub and starts listening.
    /// </summary>
    public TelegramApiServer() =>
        _server
            .Given(Request.Create().WithPath("/bot*/sendMessage").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {"ok":true,"result":{"message_id":1,"date":1756000000,
                     "chat":{"id":1,"type":"private"},"text":"stubbed"}}
                    """));

    /// <summary>
    /// The base address to hand the bot client.
    /// </summary>
    public string Url => _server.Url!;

    /// <summary>
    /// Every send-message request the stub received, in order.
    /// </summary>
    public IReadOnlyList<SendMessagePayload> SentMessages =>
        _server.LogEntries
            .Select(entry => JsonSerializer.Deserialize<SendMessagePayload>(
                entry.RequestMessage.Body ?? "{}")!)
            .ToList();

    /// <summary>
    /// Stops the server and releases its port.
    /// </summary>
    /// <returns>A completed task; stopping is synchronous.</returns>
    public ValueTask DisposeAsync()
    {
        _server.Stop();
        _server.Dispose();
        return ValueTask.CompletedTask;
    }
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
    [property: JsonPropertyName("parse_mode")] string ParseMode);
```

- [ ] **Step 4: Write the failing tests**

`tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`:

```csharp
using Assistant.Impl;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.IntegrationTests.Telegram;

/// <summary>
/// Test class for <see cref="INotifier"/>.
/// </summary>
/// <remarks>
/// This class deliberately does not join the Postgres collection. Nothing here touches a
/// database, so it runs with no Docker: WireMock stands in for the Telegram API in process.
/// </remarks>
public sealed class TelegramNotifierTests : IAsyncLifetime
{
    private const string BotToken = "123456:TESTTOKEN";
    private const long OwnerChatId = 472619570L;

    private readonly TelegramApiServer _telegram = new();
    private ServiceProvider _provider = null!;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddAssistantTelegram(BotToken, OwnerChatId, _telegram.Url);
        _provider = services.BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _telegram.DisposeAsync();
    }

    /// <summary>
    /// When a message is sent
    /// And the text contains characters MarkdownV2 would treat as formatting
    /// Then exactly one request reaches Telegram, addressed to the owner, with the text unchanged.
    /// </summary>
    [Theory]
    [InlineData("Call the bank")]
    [InlineData("Call the bank_now *urgent*")]
    public async Task SendAsync_Text_PostsOneMessageToTheOwner(string text)
    {
        // Arrange
        var expected = new SendMessagePayload(OwnerChatId, text, "Html");
        var sut = _provider.GetRequiredService<INotifier>();

        // Act
        await sut.SendAsync(text, CancellationToken.None);

        // Assert
        Assert.Equivalent(expected, Assert.Single(_telegram.SentMessages), strict: true);
    }
}
```

`Assert.Single` is doing real work: it pins the count at exactly one, which spec §7.3 requires
over "at least one", and hands back the item for the content assertion.

- [ ] **Step 5: Run and watch them fail**

```bash
dotnet test tests/Assistant.IntegrationTests
```

Expected: compilation fails — neither `AddAssistantTelegram` nor any `INotifier` implementation
exists. That is the red state.

- [ ] **Step 6: Write `TelegramNotifier`**

`src/Assistant.Impl/Telegram/TelegramNotifier.cs`:

```csharp
using Assistant.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Delivers messages through the Telegram Bot API.
/// </summary>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="ownerChatId">The chat this assistant reports to.</param>
/// <remarks>
/// HTML parse mode is deliberate. MarkdownV2 has eighteen escape-sensitive characters, so an
/// underscore in a task title would produce a 400 on a live reminder — a formatting defect that
/// costs a delivery. HTML has three, and none of them occur in ordinary task text.
/// </remarks>
internal sealed class TelegramNotifier(ITelegramBotClient bot, long ownerChatId) : INotifier
{
    /// <inheritdoc/>
    public async Task SendAsync(string text, CancellationToken ct) =>
        await bot.SendMessage(ownerChatId, text, ParseMode.Html, cancellationToken: ct);
}
```

`SendMessage` is the v22 name. `SendTextMessageAsync` belongs to older majors and will not
compile — this was verified by reflection over the package, not recalled.

- [ ] **Step 7: Register it**

`src/Assistant.Impl/ImplServiceCollectionExtensions.cs`:

```csharp
using Assistant.Impl.Telegram;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

namespace Assistant.Impl;

/// <summary>
/// Registers the assistant's outbound channels.
/// </summary>
public static class ImplServiceCollectionExtensions
{
    /// <summary>
    /// Registers Telegram as the assistant's notifier.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <param name="botToken">The bot token issued by BotFather.</param>
    /// <param name="ownerChatId">The chat the assistant reports to.</param>
    /// <param name="baseUrl">
    /// The API base address, or <see langword="null"/> for the real Telegram API. Tests pass a
    /// WireMock address here, which is why no seam is needed in the notifier itself.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAssistantTelegram(
        this IServiceCollection services, string botToken, long ownerChatId, string? baseUrl = null)
    {
        services.AddSingleton<ITelegramBotClient>(
            _ => new TelegramBotClient(new TelegramBotClientOptions(botToken, baseUrl)));
        services.AddSingleton<INotifier>(
            provider => new TelegramNotifier(
                provider.GetRequiredService<ITelegramBotClient>(), ownerChatId));
        return services;
    }
}
```

- [ ] **Step 8: Run and watch them pass**

```bash
docker compose -f compose.test.yaml up -d
dotnet build
dotnet test tests/Assistant.IntegrationTests
```

Expected: `0 Warning(s)`, `0 Error(s)`, **16** integration tests — 14 from F1 to F3 plus the 2
cases added here.

- [ ] **Step 9: Prove the payload assertion can fail**

Temporarily change `ParseMode.Html` to `ParseMode.MarkdownV2` in `TelegramNotifier` and run.

Expected: both `SendAsync_Text_PostsOneMessageToTheOwner` cases fail with an
`EquivalentException` naming member `ParseMode`. If they pass, the assertion is not reading the
payload and Task 1 has not delivered.

Revert and confirm green.

- [ ] **Step 10: Commit**

```bash
git add src/ tests/ Directory.Packages.props
git commit -m "feat: send a message through Telegram"
```

---

## Task 2: Record what F4 settled

**Files:**
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`

- [ ] **Step 1: Update the F4 entry**

Mark it done. Record: `INotifier` takes text and the recipient is configuration; the payload is
asserted whole, so F6's inline keyboard will fail this test until it is updated deliberately; and
that the architecture test forbidding `Impl.Services` from naming `Telegram.Bot` waits for F5,
when the first service exists for it to constrain.

- [ ] **Step 2: Full verification**

```bash
dotnet build
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
```

Expected: `0 Warning(s)`, `0 Error(s)`, 12 unit tests, 16 integration tests.

- [ ] **Step 3: Commit and push**

```bash
git add docs/
git commit -m "docs: record the decisions F4 settled"
git push -u origin feature/f4-send-telegram-message
```

Open the PR against `main`. Do not merge.

---

## Self-review

**Spec coverage.** §3.1 — `TelegramNotifier` lands in `Impl/Telegram` as the structure requires.
§3.3 and §7.4 — HTML parse mode, with the `_` and `*` case tested. §7.1 — WireMock at the
integration level. §7.3 — the delivery assertion pins count, recipient, and exact text; buttons
are F6's and the spec's example includes them because it is written from the finished system.
§12.3 — the SDK exception is honoured, and its base address is a registration concern, which is
why no seam exists in the notifier. §6.5's retry table is excluded and assigned to F14.

**Placeholder scan.** No TBDs. Step 9 states one expected outcome and what it means if it does
not happen.

**Type consistency.** `INotifier.SendAsync(string, CancellationToken)` → `Task` is identical in
the interface, the implementation, and both tests. `SendMessagePayload(long, string, string)` is
constructed in the test exactly as it is declared in the harness.

**Known risk.** `SentMessages` deserialises every log entry each time it is read. With one stub
and at most a handful of requests that is irrelevant, but if a later feature drives hundreds of
sends through this harness it should be memoised. Noted rather than solved, because solving it
now would be a guess.

**A spec inconsistency noticed, not fixed.** §7.3's example asserts with Shouldly
(`sent.Should().HaveCount(1)`), which this project banned in favour of plain xUnit `Assert`. The
snippet is illustrative rather than executable, and rewriting it touches buttons and a clock that
do not exist yet, so it is left for the feature that makes it real. Flagging so it is not read as
permission to add Shouldly.
