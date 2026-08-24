# F4b — The WireMock stub service and the notifier's integration test

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** a WireMock service running in Docker Compose that stands in for every external API, and
the automated test F4a owed for `TelegramNotifier`.

**Prerequisite:** F4a is merged, and its human test passed — a real message arrived on a real
phone. This plan assumes the adapter works and asks only whether it keeps working.

**Tech Stack:** WireMock.Net 2.15.0, Docker Compose, xUnit 2.9.3.

**Spec:** `docs/design/slice-1-reminders.md` §7.1, §7.3.
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F4.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors.
- Every class with arguments uses a primary constructor (§12.5).
- Plain xUnit `Assert`; `Assert.Equal(expected, actual)` — expected first.
- Central package management; no inline versions (NU1008).
- PR budget: 1000 lines. Estimated ~280.

---

## This supersedes spec §7.1

§7.1 says *"WireMock runs in-process (1–5ms per call), so once Postgres is up a test lands around
20–60ms."* That stops being true here, and Task 4 corrects it rather than leaving the spec quietly
wrong.

---

## Verified before writing this plan

**The wire format**, captured from a live server with the real client pointed at it:

```
PATH   /bot123456:TESTTOKEN/sendMessage
METHOD POST
BODY   {"chat_id":472619570,"text":"Reminder: call the bank_now *urgent*","parse_mode":"Html"}
```

So the stub matches `/bot*/sendMessage` — the token is in the path — and `parse_mode` serialises
as `"Html"`, capital H only. `_` and `*` pass through untouched, which is the acceptance case
spec §7.4 names and the reason §3.3 chose HTML over MarkdownV2.

**The SDK deserialises the envelope,** so a stub returning a bare `{}` makes the client throw:

```json
{"ok":true,"result":{"message_id":1,"date":1756000000,
 "chat":{"id":1,"type":"private"},"text":"stubbed"}}
```

**The admin API does everything an out-of-process stub needs.** Measured over HTTP:

```
GET    /__admin/requests   -> 1 entry; Request.Body is the exact payload above
DELETE /__admin/requests   -> 200; a follow-up GET returns 0 entries
POST   /__admin/mappings   -> 201; the pushed stub answers immediately
```

Admin calls are not themselves recorded, so reading does not pollute the log.

**WireMock.Net needs the ASP.NET Core shared framework.** From the built `runtimeconfig.json`:

```json
"frameworks": [{"name": "Microsoft.NETCore.App", ...},
               {"name": "Microsoft.AspNetCore.App", "version": "10.0.0"}]
```

So the base image is `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`, not `dotnet/runtime`. The
plain runtime image builds and then dies at startup with a missing framework.

---

## Decisions this plan makes — review these first

### A. WireMock is a service, and here is what that costs

Requested in review. Gains: the stub is inspectable while a test is paused, it can serve a
locally-run `Assistant.Worker` and not just the test process, and one service will host the
Anthropic and OpenRouter stubs at F9 and F13 rather than each test starting its own.

Three costs, stated because they are not obvious:

1. **The notifier test needs Docker.** In-process it would not have. Every other integration test
   already does, so the loop is unchanged — but a Docker-free notifier test is no longer possible.
2. **Verification moves to HTTP.** `server.LogEntries` is unreachable from another process, so the
   fixture reads `GET /__admin/requests` and parses `Request.Body`.
3. **Isolation becomes explicit.** A shared container accumulates requests exactly as a shared
   Postgres accumulates rows. `DELETE /__admin/requests` is the Respawn of this fixture and runs
   before every test, or the second test sees the first one's message. Step 9 proves it.

### B. The stub service owns its mappings

Defined in C# at startup, not pushed by tests: a test asks what was sent, it does not first teach
the server how to answer. The admin API can push at runtime (measured: `201`), which is the escape
hatch when F14 needs a 429 with `retry_after`.

### C. `Assistant.WireMock`, named for the tool

Your name, and unambiguous about what the container is. The alternative — naming it for the role,
`Assistant.ApiStubs` — survives swapping the tool and reads better once F9 and F13 add stubs to
the same service. Cheap to rename now, annoying once three features depend on it.

### D. A separate xUnit collection now; F5 merges them

These tests need the stub and not Postgres, so they get `WireMockCollection`. **A test class can
belong to exactly one xUnit collection**, so when F5's scheduler needs a database *and* a stub,
the two definitions merge into one holding both fixtures. F5's work, flagged here.

### E. The test asserts the whole request payload

Extending the F3 review pattern to the wire format: the captured body is parsed into a record and
compared with `Assert.Equivalent(expected, actual, strict: true)`, so count, recipient, exact
text, and parse mode are one assertion.

**A deliberate consequence:** when F6 adds an inline keyboard, `reply_markup` appears in the body
and `strict` fails this test. That is intended — a change to what we put on the wire should force
a reviewer to look — but it is a cost.

---

## File Structure

| Path | Responsibility |
| :--- | :--- |
| `Directory.Packages.props` | **Modify.** WireMock.Net. |
| `PersonalAssistant.slnx` | **Modify.** Add the new project. |
| `tests/Assistant.WireMock/Assistant.WireMock.csproj` | **Create.** Console app. |
| `tests/Assistant.WireMock/Program.cs` | **Create.** Start, install stubs, block. |
| `tests/Assistant.WireMock/TelegramStubs.cs` | **Create.** The Telegram mappings. |
| `tests/Assistant.WireMock/Dockerfile` | **Create.** SDK build → aspnet:10.0-alpine. |
| `compose.test.yaml` | **Modify.** The `wiremock` service on 58080. |
| `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs` | **Create.** Readiness, reset, reads. |
| `tests/Assistant.IntegrationTests/Infrastructure/WireMockCollection.cs` | **Create.** Collection definition. |
| `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs` | **Create.** One test, two cases. |
| `AGENTS.md`, `docs/design/slice-1-reminders.md` | **Modify.** Task 4. |

---

## Test design

| Test | Kind | What it documents |
| :--- | :--- | :--- |
| `SendAsync_Text_PostsOneMessageToTheOwner` | `[Theory]` ×2 | Exactly one request, to the owner, with the exact text and HTML parse mode |

**Equivalence classes for the text.** Plain, and containing the MarkdownV2-sensitive `_` and `*`.
Those are the two that matter, because HTML parse mode exists so the second does not 400 on a live
reminder. Representatives: `"Call the bank"` and `"Call the bank_now *urgent*"`.

`Assert.Single` pins the count at exactly one — spec §7.3 requires that over "at least one" — and
yields the item, so count and content are one assertion.

**Deliberately not tested:** that Telegram accepts the message (F4a's human test did that, and
there is no credential in CI); a 429 or 400 (no retry policy until F14); that the container starts
(every test in the collection fails in fixture setup if it does not).

---

## Task 1: The stub service and its container

- [ ] **Step 1: Package version**

`Directory.Packages.props`: `<PackageVersion Include="WireMock.Net" Version="2.15.0" />`.

- [ ] **Step 2: The project**

`tests/Assistant.WireMock/Assistant.WireMock.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="WireMock.Net" />
  </ItemGroup>

</Project>
```

Not a test project — no xUnit reference. Nothing discovers it, because `AGENTS.md` runs
`dotnet test` against named project directories rather than the solution. The
inherited-looking properties match the other projects under `tests/`, which repeat them even
though `Directory.Build.props` supplies them; follow the surrounding style. Note that
`tests/Directory.Build.props` puts `CS1591` in `NoWarn`, so doc comments are not build-enforced
here — write them anyway.

`tests/Assistant.WireMock/TelegramStubs.cs`:

```csharp
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Assistant.WireMock;

/// <summary>
/// The Telegram Bot API endpoints this stub answers.
/// </summary>
internal static class TelegramStubs
{
    private const string SendMessageResponse = """
        {"ok":true,"result":{"message_id":1,"date":1756000000,
         "chat":{"id":1,"type":"private"},"text":"stubbed"}}
        """;

    /// <summary>
    /// Installs the Telegram mappings on the given server.
    /// </summary>
    /// <param name="server">The running stub server.</param>
    /// <remarks>
    /// The path is <c>/bot*/sendMessage</c> because the SDK puts the bot token in the path, so
    /// whichever token a test uses must not affect matching. The response is the real envelope
    /// shape: the client deserialises it, and a bare object makes it throw.
    /// </remarks>
    public static void Install(WireMockServer server) =>
        server
            .Given(Request.Create().WithPath("/bot*/sendMessage").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(SendMessageResponse));
}
```

`tests/Assistant.WireMock/Program.cs`:

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

Console.WriteLine("Stub API listening on http://0.0.0.0:8080");

var stopping = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopping.TrySetResult();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => stopping.TrySetResult();

await stopping.Task;
```

`Urls` binds `0.0.0.0`, not `localhost` — a container binding loopback is unreachable from the
host, and the failure looks like a hung readiness poll. `StartAdminInterface` is what makes
`/__admin/requests` available; without it the fixture can verify nothing.

- [ ] **Step 3: The Dockerfile**

`tests/Assistant.WireMock/Dockerfile`, built with the **repository root** as context:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY Directory.Packages.props Directory.Build.props ./
COPY tests/Assistant.WireMock/Assistant.WireMock.csproj tests/Assistant.WireMock/
RUN dotnet restore tests/Assistant.WireMock/Assistant.WireMock.csproj

COPY tests/Assistant.WireMock/ tests/Assistant.WireMock/
RUN dotnet publish tests/Assistant.WireMock/Assistant.WireMock.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Assistant.WireMock.dll"]
```

The runtime image is **aspnet**, not **runtime** — verified above. Both `Directory.*.props` files
are copied before `restore` because central package management resolves versions from them; both
exist at the repository root.

- [ ] **Step 4: Compose**

In `compose.test.yaml`, alongside `postgres-test`:

```yaml
  wiremock:
    build:
      context: .
      dockerfile: tests/Assistant.WireMock/Dockerfile
    ports:
      - "58080:8080"
    healthcheck:
      test: ["CMD-SHELL", "wget -q -O - http://localhost:8080/__admin/mappings || exit 1"]
      interval: 2s
      timeout: 3s
      retries: 30
```

`58080` mirrors the `55432` convention. The healthcheck uses busybox `wget`, which Alpine
provides; the fixture polls independently, so the healthcheck is a convenience rather than the
safety net.

- [ ] **Step 5: Solution**

```bash
dotnet sln PersonalAssistant.slnx add tests/Assistant.WireMock/Assistant.WireMock.csproj
```

- [ ] **Step 6: Prove the container works before any test depends on it**

```bash
docker compose -f compose.test.yaml build wiremock
docker compose -f compose.test.yaml up -d
curl -s http://localhost:58080/__admin/mappings | head -c 200
curl -s -X POST http://localhost:58080/bot123:ABC/sendMessage \
     -H 'Content-Type: application/json' \
     -d '{"chat_id":1,"text":"hi","parse_mode":"Html"}'
curl -s http://localhost:58080/__admin/requests | head -c 400
```

Expected: mappings non-empty, the POST returns the `{"ok":true,...}` envelope, and the requests
list contains the body just posted. If any of those fail, stop — nothing downstream can work and
the cause is here.

- [ ] **Step 7: Commit**

```bash
git add tests/Assistant.WireMock Directory.Packages.props compose.test.yaml PersonalAssistant.slnx
git commit -m "test: add a WireMock stub service that runs in compose"
```

---

## Task 2: The fixture and the test

- [ ] **Step 1: `WireMockFixture`**

`tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
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
    /// <returns>A task that completes once the request log is empty.</returns>
    public async Task ResetAsync() =>
        (await _http.DeleteAsync($"{Url}/__admin/requests")).EnsureSuccessStatusCode();

    /// <summary>
    /// Returns the send-message requests the stub received, in order.
    /// </summary>
    /// <returns>One payload per captured request.</returns>
    public async Task<IReadOnlyList<SendMessagePayload>> SentMessagesAsync()
    {
        var entries = await _http.GetFromJsonAsync<List<AdminLogEntry>>($"{Url}/__admin/requests")
                      ?? [];

        return entries
            .Where(entry => entry.Request.Path.EndsWith("/sendMessage", StringComparison.Ordinal))
            .Select(entry => JsonSerializer.Deserialize<SendMessagePayload>(entry.Request.Body)!)
            .ToList();
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

    private sealed record AdminLogEntry(
        [property: JsonPropertyName("Request")] AdminRequest Request);

    private sealed record AdminRequest(
        [property: JsonPropertyName("Path")] string Path,
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
    [property: JsonPropertyName("parse_mode")] string ParseMode);
```

The `/sendMessage` filter is load-bearing later: when F9 adds the Anthropic stub to the same
service, it is what stops one API's traffic being read as another's.

`tests/Assistant.IntegrationTests/Infrastructure/WireMockCollection.cs`:

```csharp
namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>
/// Groups every test class that shares the stub API.
/// </summary>
/// <remarks>
/// Separate from <see cref="PostgresCollection"/> because these tests need no database. A class
/// can belong to only one collection, so the feature that first needs both a database and a stub
/// merges these two definitions into one.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class WireMockCollection : ICollectionFixture<WireMockFixture>
{
    /// <summary>
    /// The collection name to put on test classes that use <see cref="WireMockFixture"/>.
    /// </summary>
    public const string Name = "wiremock";
}
```

- [ ] **Step 2: The test**

`tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`:

```csharp
using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.IntegrationTests.Telegram;

/// <summary>
/// Test class for <see cref="INotifier"/>.
/// </summary>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(WireMockCollection.Name)]
public sealed class TelegramNotifierTests(WireMockFixture wireMock) : IAsyncLifetime
{
    private const string BotToken = "123456:TESTTOKEN";
    private const long OwnerChatId = 472619570L;

    private ServiceProvider _provider = null!;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddAssistantTelegram(new TelegramSettings
        {
            BotToken = BotToken,
            OwnerChatId = OwnerChatId,
            BaseUrl = wireMock.Url,
        });
        _provider = services.BuildServiceProvider();
        return wireMock.ResetAsync();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

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
        Assert.Equivalent(expected, Assert.Single(await wireMock.SentMessagesAsync()), strict: true);
    }
}
```

`ResetAsync` runs in `InitializeAsync`, before every test — that is what stops the second
`[InlineData]` case seeing the first one's message.

- [ ] **Step 3: Run**

```bash
docker compose -f compose.test.yaml up -d
dotnet build
dotnet test tests/Assistant.IntegrationTests
```

Expected: `0 Warning(s)`, `0 Error(s)`, **16** integration tests — 14 from F1 to F3 plus the 2
here.

- [ ] **Step 4: Prove the payload assertion can fail**

Temporarily change `ParseMode.Html` to `ParseMode.MarkdownV2` in `TelegramNotifier`. Expect both
cases to fail with an `EquivalentException` naming member `ParseMode`. Revert and confirm green.

- [ ] **Step 5: Prove the reset is working**

Temporarily make `InitializeAsync` return `Task.CompletedTask` instead of calling `ResetAsync`.
Expect the second `[InlineData]` case to fail, because `Assert.Single` sees two messages. If both
still pass, the tests are not sharing the container and the collection is misconfigured. Revert
and confirm green.

- [ ] **Step 6: Commit**

```bash
git add tests/
git commit -m "test: prove the notifier's payload against a stubbed Telegram API"
```

---

## Task 3: Documentation and the spec correction

- [ ] **Step 1: Spec §7.1**

Replace *"WireMock runs in-process (1–5ms per call), so once Postgres is up a test lands around
20–60ms."* with a statement that WireMock runs as its own container defined in
`compose.test.yaml`, that tests verify through its admin API, and that the request log is cleared
between tests the way Respawn clears tables. Keep the surrounding paragraphs.

- [ ] **Step 2: `AGENTS.md`**

The compose command now brings up two services. Say so where the build and test commands are
listed, and add `Assistant.WireMock` to the project map with one line on what it is.

- [ ] **Step 3: Backlog**

Mark F4b done. Record Decisions A, C, D, and E — and that the debt F4a took on, an untested
`TelegramNotifier`, is now paid.

- [ ] **Step 4: Full verification from nothing**

```bash
docker compose -f compose.test.yaml down -v
docker compose -f compose.test.yaml up -d --build
dotnet build
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
```

`down -v` then `up -d --build` proves it works from a clean slate, which is what a fresh clone
does. Expected: `0 Warning(s)`, `0 Error(s)`, 16 unit tests, 16 integration tests.

- [ ] **Step 5: Commit, push, open the PR**

```bash
git add docs/ AGENTS.md
git commit -m "docs: record the decisions F4b settled"
git push -u origin feature/f4b-telegram-integration-tests
```

Open the PR against `main`. Do not merge.

---

## Self-review

**Spec coverage.** §7.1 corrected rather than quietly contradicted. §7.3 — count, recipient, and
exact text pinned in one assertion; buttons are F6's. §7.4's `_` and `*` scenario is one of the
two `[InlineData]` cases.

**Placeholder scan.** No TBDs. Steps 6, 4, and 5 each state one expected outcome and what it means
if it does not happen.

**Type consistency.** `SendMessagePayload(long, string, string)` is constructed in the test exactly
as declared in the fixture file. `TelegramSettings` is used exactly as F4a declares it.

**Known risk.** Building the container adds a step CI does not have — there is no CI until F14.
Until then `docker compose up -d --build` is a developer responsibility, which Task 3 Step 2
writes into `AGENTS.md` so it does not stay folklore.

**Second risk.** `SentMessagesAsync` deserialises every log entry on each read. With a handful of
requests that is irrelevant; if a later feature drives hundreds of sends through this harness it
should be memoised. Noted rather than solved, because solving it now would be a guess.
