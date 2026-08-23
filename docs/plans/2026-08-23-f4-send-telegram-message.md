# F4 — Send a Telegram message

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** `INotifier` and `TelegramNotifier`, sending HTML-parse-mode messages through
`Telegram.Bot`, proven against a WireMock **service running in Docker Compose** — not in process.

**Architecture:** `Telegram.Bot` is an SDK, not a hand-rolled HTTP client, so spec §12.3's Refit
rule does not apply to it. Its base address is a constructor option, which is what makes it
stub-testable with no production seam. A new project, `tests/Assistant.WireMock`, owns every
external API stub and ships as a container alongside Postgres. Tests verify what was sent by
querying WireMock's admin API over HTTP, the same way they reset Postgres with Respawn.

**Tech Stack:** Telegram.Bot 22.10.2.1, WireMock.Net 2.15.0, xUnit 2.9.3, Docker Compose.

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
- PR budget: 1000 lines. Estimated ~430 of code, tests, settings, and container plumbing,
  plus this plan.

---

## This supersedes spec §7.1

§7.1 currently reads: *"WireMock runs in-process (1–5ms per call), so once Postgres is up a test
lands around 20–60ms."* That is no longer true, and Task 4 Step 1 updates it.

The change was requested in review: WireMock becomes its own project and its own container rather
than a library the test process starts. What that buys, and what it costs, are both real and are
set out in Decision A.

---

## Verified before writing this plan

Every claim below was measured, not recalled.

**The SDK's surface (v22 renamed things):**

```
TelegramBotClientOptions(String token, String baseUrl = null, Boolean useTestEnvironment = False)
TelegramBotClient(TelegramBotClientOptions options, HttpClient httpClient = null, ...)
TelegramBotClientExtensions.SendMessage(ITelegramBotClient, ChatId, String text, ParseMode parseMode, ...)
ParseMode members: None, Markdown, Html, MarkdownV2
```

It is `SendMessage`, **not** `SendTextMessageAsync` — that belongs to older majors and does not
exist here.

**The wire format**, captured from a live WireMock server with the client pointed at it:

```
PATH   /bot123456:TESTTOKEN/sendMessage
METHOD POST
BODY   {"chat_id":472619570,"text":"Reminder: call the bank_now *urgent*","parse_mode":"Html"}
```

So the stub matches `/bot*/sendMessage` — the token is in the path — and `parse_mode` serialises
as `"Html"`, capital H only.

**The SDK deserialises the envelope**, so a stub returning a bare `{}` makes the client throw:

```json
{"ok":true,"result":{"message_id":1,"date":1756000000,
 "chat":{"id":1,"type":"private"},"text":"stubbed"}}
```

**`_` and `*` survive untouched** under `ParseMode.Html` — visible in the captured body above.
That is why spec §3.3 chose HTML over MarkdownV2 and its eighteen escape-sensitive characters.

**The admin API does everything an out-of-process stub needs.** Measured over HTTP against a
running server:

```
GET    /__admin/requests   -> 1 entry; Request.Body is the exact payload above
DELETE /__admin/requests   -> 200; a follow-up GET returns 0 entries
POST   /__admin/mappings   -> 201; the pushed stub answers immediately
```

Admin calls are not themselves recorded in the request log, so reading does not pollute it.

**WireMock.Net needs the ASP.NET Core shared framework.** A console app referencing it produces:

```json
"frameworks": [{"name": "Microsoft.NETCore.App", ...},
               {"name": "Microsoft.AspNetCore.App", "version": "10.0.0"}]
```

So the container base image is `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`, not
`dotnet/runtime`. Getting that wrong produces a container that builds and then fails at startup.

**`required` does not protect a settings class, and this is the whole reason `Validate()` exists.**
Binding a section that is missing values to a class whose properties are `required`:

```
PROBE section.Exists() (only unrelated key) = True
PROBE bound with missing required          = token='<null>' chat=0
PROBE absent section Exists()              = False
PROBE absent section Get<T>()              = NULL
PROBE populated                            = token='123:ABC' chat=472619570
```

Three things follow, and each one is load-bearing:

- `required` is a compile-time contract for object initialisers. The configuration binder goes
  around it, so a missing token becomes `null` in a `string` property that the nullable analysis
  swears is not null. Nothing throws. The failure surfaces later as a confusing 401 from Telegram.
- `section.Exists()` is true when *any* key sits under the section, so an unrelated or misspelled
  key makes an otherwise-empty section look present.
- A section that is entirely absent binds to `null`, which is why the null check in `Read<T>` is
  not defensive padding.

**`ConfigurationErrorsException` needs a package.** It does not resolve from the shared framework:

```
PROBE ConfigurationErrorsException resolvable = False
```

`Trading.Ibkr.Common.csproj` references `System.Configuration.ConfigurationManager` for exactly
this reason, so this project does the same rather than substituting a different exception type.

---

## Decisions this plan makes — review these first

### A. WireMock is a service, and here is what that costs

Requested in review. The gains are real: the stub is inspectable while a test is paused, it can
serve a locally-run `Assistant.Worker` and not only the test process, and one service will host
the Anthropic and OpenRouter stubs at F9 and F13 rather than each test project starting its own.

Three costs, stated plainly because they are not obvious:

1. **F4's tests now require Docker.** In-process they would not have. Every integration test in
   this repo already needs Docker for Postgres, so the developer loop is unchanged — but the
   option of a Docker-free notifier test is gone.
2. **Verification moves to HTTP.** `server.LogEntries` is not reachable from another process, so
   the fixture reads `GET /__admin/requests` and parses `Request.Body`. Measured above; it works,
   and it is more code than a property access.
3. **Isolation becomes explicit.** A shared container accumulates requests across tests, exactly
   as a shared Postgres accumulates rows. `DELETE /__admin/requests` is the Respawn of this
   fixture and must run before every test, or test two sees test one's message.

### B. The stub service owns its mappings

"A separate service that does it all" — so the mappings live in the service, defined in C# at
startup, not pushed by tests. A test that wants to know what was sent asks the admin API; it does
not first have to teach the server how to answer.

The admin API can push mappings at runtime (measured: `201`), which is the escape hatch when F14
needs a 429 with `retry_after`. Not used yet, and not built yet.

### C. `Assistant.WireMock`, named for the tool

It is the name you used, and it is unambiguous about what the container is. The alternative,
naming it for the role (`Assistant.ApiStubs`), survives swapping the tool but reads as vaguer
today. Easy to rename before F9 adds the second stub if you prefer.

### D. A separate xUnit collection now; F5 merges them

F4's tests need WireMock and not Postgres, so they get `WireMockCollection`. **A test class can
belong to exactly one xUnit collection**, so when F5's scheduler test needs a database *and* a
stub, the two collection definitions merge into one that holds both fixtures. That is F5's work,
noted here so it is not a surprise.

### E. `INotifier.SendAsync(string text, CancellationToken ct)` — the recipient is configuration

Single-user product (spec §1), so a recipient parameter would carry exactly one possible value at
every call site. Rendering stays out too: F5 composes the text, F4 delivers it, and `Contracts`
stays empty until F5 needs it.

### F. The test asserts the whole request payload

Extending the pattern from the F3 review to the wire format: the captured body is parsed into a
record and compared with `Assert.Equivalent(expected, actual, strict: true)` against a hardcoded
value, so count, recipient, exact text, and parse mode are one assertion.

**A deliberate consequence:** when F6 adds an inline keyboard, `reply_markup` appears in the body
and `strict: true` fails this test. That is intended — a change to what we put on the wire should
force a reviewer to look — but it is a cost.

### G. The architecture test forbidding `Impl.Services` → `Telegram.Bot` waits for F5

Spec §7.5 lists it. There is no `Impl/Services` namespace yet, so it would pass over zero types.

### H. Settings are a bound, validated model — checked at startup, not at first use

Requested in review, modelled on `Trading.Ibkr`'s `IValidatableConfig` / `ConfigurationExtensions`
pattern. This pulls fail-fast validation forward from F14, where the backlog had parked it.

The shape is the same as the reference project:

- `IValidatableConfig` — one method, `void Validate()`.
- `TelegramSettings : IValidatableConfig` — `required` init properties plus a `Validate()` that
  throws per missing field.
- `IConfiguration.Read<T>()` — section name is `typeof(T).Name`, checks `Exists()`, binds,
  null-checks, calls `Validate()`, returns.
- `Program.cs` calls `Read<TelegramSettings>()` while composing the container, so a missing token
  stops the host before it starts rather than surfacing as a 401 on the first reminder.

**Where the types live is constrained by tests this repo already has.** `TelegramSettings` cannot
live in `Assistant.Models`: `ConventionTests.Models_declare_no_methods_beyond_property_accessors`
would fail on `Validate()`. It cannot live in `Assistant.Interfaces` either, which
`Interfaces_declares_no_concrete_public_classes` reserves for abstractions. So `IValidatableConfig`
goes to `Interfaces` (spec §3.1: every interface in the system) and the settings class and the
binder extension go to `Impl`, next to the adapter that consumes them.

**`baseUrl` becomes a settings property** rather than a parameter, which is what lets a test point
the client at the stub container without a production seam.

---

## What F4 does NOT include, and why

| Excluded | Returns at |
| :--- | :--- |
| Rendering a reminder into text, `ReminderNotification` | F5 / F10 |
| Inline keyboards and `reply_markup` | F6 |
| Anthropic and OpenRouter stubs in the same service | F9, F13 |
| Polly retry on 429 honouring `retry_after` (spec §6.5) | F14 |
| Receiving messages, `TelegramListener` | F7 |
| Options validation, `appsettings.{Environment}.json` | F14 |
| Merging the Postgres and WireMock collections | F5 (Decision D) |

---

## File Structure

| Path | Responsibility |
| :--- | :--- |
| `Directory.Packages.props` | **Modify.** Telegram.Bot, WireMock.Net. |
| `PersonalAssistant.slnx` | **Modify.** Add the new project. |
| `tests/Assistant.WireMock/Assistant.WireMock.csproj` | **Create.** Console app, WireMock.Net. |
| `tests/Assistant.WireMock/Program.cs` | **Create.** Starts the server, installs stubs, blocks. |
| `tests/Assistant.WireMock/TelegramStubs.cs` | **Create.** The Telegram mappings. |
| `tests/Assistant.WireMock/Dockerfile` | **Create.** SDK build → aspnet:10.0-alpine runtime. |
| `compose.test.yaml` | **Modify.** Add the `wiremock` service. |
| `src/Assistant.Interfaces/INotifier.cs` | **Create.** One method. |
| `src/Assistant.Interfaces/IValidatableConfig.cs` | **Create.** `void Validate()`. |
| `src/Assistant.Impl/Settings/TelegramSettings.cs` | **Create.** Bound settings + validation. |
| `src/Assistant.Impl/Configuration/ConfigurationExtensions.cs` | **Create.** `Read<T>()`. |
| `src/Assistant.Worker/Program.cs` | **Modify.** Read and validate settings while composing. |
| `tests/Assistant.UnitTests/Configuration/ConfigurationExtensionsTests.cs` | **Create.** Four cases. |
| `src/Assistant.Impl/Assistant.Impl.csproj` | **Modify.** Telegram.Bot + DI abstractions. |
| `src/Assistant.Impl/Telegram/TelegramNotifier.cs` | **Create.** The only type naming an SDK type. |
| `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` | **Create.** `AddAssistantTelegram`. |
| `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs` | **Create.** Readiness, reset, request reads. |
| `tests/Assistant.IntegrationTests/Infrastructure/WireMockCollection.cs` | **Create.** Collection definition. |
| `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs` | **Create.** One test, two cases. |
| `AGENTS.md`, `docs/design/slice-1-reminders.md` | **Modify.** Task 4 — correct §7.1, document the second container. |

**Interfaces produced:**
- `INotifier.SendAsync(string text, CancellationToken ct)` → `Task`
- `AddAssistantTelegram(IServiceCollection, TelegramSettings settings)`
- `IValidatableConfig.Validate()`; `TelegramSettings`; `IConfiguration.Read<T>()`
- `WireMockFixture` with `string Url`, `Task ResetAsync()`, `Task<IReadOnlyList<SendMessagePayload>> SentMessagesAsync()`
- `WireMockCollection.Name` (`"wiremock"`)

---

## Test design

One test method, two cases.

| Test | Kind | What it documents |
| :--- | :--- | :--- |
| `SendAsync_Text_PostsOneMessageToTheOwner` | `[Theory]` ×2 | Exactly one request, to the owner, with the exact text and HTML parse mode |

An earlier draft added a second test asserting nothing is sent when nothing is called. Cut on
review: "not calling a method has no effect" is not a business requirement, and `Assert.Single`
already pins the count at exactly one.

**Equivalence classes for the text.** Plain text, and text containing the MarkdownV2-sensitive
characters `_` and `*`. Those are the two classes that matter, because HTML parse mode exists
precisely so the second does not 400 on a live reminder (spec §3.3, §7.4). Representatives:
`"Call the bank"` and `"Call the bank_now *urgent*"`.

**The assertion**, per Decision F and spec §7.3:

```csharp
var expected = new SendMessagePayload(OwnerChatId, text, "Html");
Assert.Equivalent(expected, Assert.Single(await _wireMock.SentMessagesAsync()), strict: true);
```

`Assert.Single` pins the count at exactly one — spec §7.3 requires that over "at least one" — and
yields the item, so count and content are one assertion. Measured at F3: `strict: true` is
required, because without it an extra member in the actual payload goes undetected.

**Deliberately not tested, and why:**

| Not tested | Reason |
| :--- | :--- |
| That Telegram accepts the message | Telegram's behaviour, and there is no credential to test it with. |
| A 429 or 400 response | No retry policy exists until F14. The test would assert absent behaviour. |
| That the stub container starts | Every test in the collection fails in fixture setup if it does not. |
| The SDK's serialisation | Framework behaviour. We assert the payload our call produces, once. |

---

## Task 1: The WireMock service and its container

**Files:** the `tests/Assistant.WireMock/` project, `compose.test.yaml`, `Directory.Packages.props`,
`PersonalAssistant.slnx`.

**Interfaces:**
- Consumes: nothing.
- Produces: a container listening on `58080` that stubs `POST /bot*/sendMessage`.

- [ ] **Step 1: Add the package versions**

In `Directory.Packages.props`, in alphabetical position:

```xml
    <PackageVersion Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.4" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.4" />
    <PackageVersion Include="System.Configuration.ConfigurationManager" Version="10.0.11" />
    <PackageVersion Include="Telegram.Bot" Version="22.10.2.1" />
    <PackageVersion Include="WireMock.Net" Version="2.15.0" />
```

`System.Configuration.ConfigurationManager` is there only for `ConfigurationErrorsException`,
which does not resolve from the shared framework — measured above. `Trading.Ibkr.Common` carries
the same reference for the same reason.

- [ ] **Step 2: Create the project**

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

This project is **not** a test project — it has no xUnit reference. Nothing discovers it either,
because `AGENTS.md` runs `dotnet test` against a named project directory rather than the solution.

The redundant-looking properties match the other projects under `tests/`, which repeat them even
though `Directory.Build.props` supplies them. Follow the surrounding style rather than trimming.
Note also that `tests/Directory.Build.props` puts `CS1591` in `NoWarn`, so the doc comments below
are not build-enforced here — write them anyway; the reasoning they carry is the point.

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
    /// the token a test happens to use must not affect matching. The response is the real
    /// envelope shape: the client deserialises it, and a bare object makes it throw.
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

`Urls` binds `0.0.0.0`, not `localhost` — a container that binds loopback is unreachable from the
host and the failure looks like a hung readiness poll. `StartAdminInterface` is what makes
`/__admin/requests` available; without it the fixture cannot verify anything.

- [ ] **Step 3: Write the Dockerfile**

`tests/Assistant.WireMock/Dockerfile`, built from the **repository root** as context:

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

The runtime image is **aspnet**, not **runtime**. WireMock.Net declares a framework reference to
`Microsoft.AspNetCore.App` — verified by reading the built `runtimeconfig.json` — so the plain
runtime image starts and then dies with a missing-framework error.

`Directory.Build.props` and `Directory.Packages.props` are both copied before `restore` because
central package management resolves versions from them — verified present at the repository root.
Without them the restore fails with NU1008 inside the image, which is a confusing place to
discover it.

- [ ] **Step 4: Add the service to compose**

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

Port `58080` mirrors the `55432` convention: high, fixed, unlikely to collide. The healthcheck
uses `wget`, which Alpine provides through busybox; the fixture polls independently anyway, so a
healthcheck failure is a convenience signal rather than the safety net.

- [ ] **Step 5: Add the project to the solution**

```bash
dotnet sln PersonalAssistant.slnx add tests/Assistant.WireMock/Assistant.WireMock.csproj
```

- [ ] **Step 6: Prove the container works before writing a test against it**

```bash
docker compose -f compose.test.yaml build wiremock
docker compose -f compose.test.yaml up -d
curl -s http://localhost:58080/__admin/mappings | head -c 200
curl -s -X POST http://localhost:58080/bot123:ABC/sendMessage \
     -H 'Content-Type: application/json' \
     -d '{"chat_id":1,"text":"hi","parse_mode":"Html"}'
curl -s http://localhost:58080/__admin/requests | head -c 400
```

Expected: the mappings list is non-empty, the `sendMessage` POST returns the `{"ok":true,...}`
envelope, and the requests list contains the body just posted. If any of those fail, stop —
nothing downstream can work and the cause is here, not in the tests.

- [ ] **Step 7: Commit**

```bash
git add tests/Assistant.WireMock Directory.Packages.props compose.test.yaml PersonalAssistant.slnx
git commit -m "test: add a WireMock stub service that runs in compose"
```

---

## Task 2: Settings, validated at startup

**Files:**
- Create: `src/Assistant.Interfaces/IValidatableConfig.cs`
- Create: `src/Assistant.Impl/Settings/TelegramSettings.cs`
- Create: `src/Assistant.Impl/Configuration/ConfigurationExtensions.cs`
- Modify: `src/Assistant.Worker/Program.cs`
- Test: `tests/Assistant.UnitTests/Configuration/ConfigurationExtensionsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `IValidatableConfig`, `TelegramSettings`, `IConfiguration.Read<T>()`.

- [ ] **Step 1: The contract**

`src/Assistant.Interfaces/IValidatableConfig.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>
/// Settings that must be checked while the application is starting.
/// </summary>
/// <remarks>
/// The configuration binder does not honour <c>required</c> — a missing value binds to null or
/// zero without complaint — so a settings type states its own rules and the host runs them before
/// anything can use a half-populated instance.
/// </remarks>
public interface IValidatableConfig
{
    /// <summary>
    /// Throws when a mandatory value is missing.
    /// </summary>
    void Validate();
}
```

- [ ] **Step 2: The settings**

`src/Assistant.Impl/Settings/TelegramSettings.cs`:

```csharp
using System.Configuration;
using Assistant.Interfaces;

namespace Assistant.Impl.Settings;

/// <summary>
/// Configuration for the Telegram notifier.
/// </summary>
public sealed class TelegramSettings : IValidatableConfig
{
    /// <summary>
    /// The bot token issued by BotFather.
    /// </summary>
    public required string BotToken { get; init; }

    /// <summary>
    /// The chat the assistant reports to.
    /// </summary>
    public required long OwnerChatId { get; init; }

    /// <summary>
    /// The API base address, or null for the real Telegram API.
    /// </summary>
    /// <value>
    /// Tests point this at the stub container. It is optional, so it is not validated: absent
    /// means production.
    /// </value>
    public string? BaseUrl { get; init; }

    /// <inheritdoc/>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BotToken))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(TelegramSettings)}.{nameof(BotToken)} is missing or empty.");
        }

        if (OwnerChatId == 0)
        {
            throw new ConfigurationErrorsException(
                $"{nameof(TelegramSettings)}.{nameof(OwnerChatId)} is missing or zero.");
        }
    }
}
```

`OwnerChatId` is checked against `0` rather than for null, because it is a `long`: an absent key
binds to the default, not to null. Measured — see the probe output above.

- [ ] **Step 3: The binder extension**

Add `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.Configuration.Binder`
and `System.Configuration.ConfigurationManager` as `PackageReference` entries (no inline versions)
to `src/Assistant.Impl/Assistant.Impl.csproj`.

`src/Assistant.Impl/Configuration/ConfigurationExtensions.cs`:

```csharp
using System.Configuration;
using Assistant.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Assistant.Impl.Configuration;

/// <summary>
/// Reads configuration sections into validated settings objects.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Binds the section named after <typeparamref name="T"/> and validates it.
    /// </summary>
    /// <typeparam name="T">The settings type, which names its own section.</typeparam>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>A populated, validated instance.</returns>
    /// <exception cref="ConfigurationErrorsException">
    /// The section is absent, could not be bound, or a mandatory value is missing.
    /// </exception>
    /// <remarks>
    /// Each of the three checks catches a different failure. An absent section binds to null.
    /// A section holding only an unrelated key still reports <c>Exists()</c> as true, so binding
    /// succeeds and leaves the real values empty. And <c>required</c> is a compile-time contract
    /// the binder goes around, so only <see cref="IValidatableConfig.Validate"/> catches a value
    /// that is present in shape but missing in fact.
    /// </remarks>
    public static T Read<T>(this IConfiguration configuration)
        where T : IValidatableConfig
    {
        var sectionName = typeof(T).Name;
        var section = configuration.GetSection(sectionName);

        if (!section.Exists())
        {
            throw new ConfigurationErrorsException(
                $"Configuration section '{sectionName}' was not found.");
        }

        var settings = section.Get<T>()
            ?? throw new ConfigurationErrorsException(
                $"Configuration section '{sectionName}' could not be bound to {typeof(T).Name}.");

        settings.Validate();
        return settings;
    }
}
```

- [ ] **Step 4: Write the failing tests**

`tests/Assistant.UnitTests/Configuration/ConfigurationExtensionsTests.cs`:

```csharp
using System.Configuration;
using Assistant.Impl.Configuration;
using Assistant.Impl.Settings;
using Microsoft.Extensions.Configuration;

namespace Assistant.UnitTests.Configuration;

/// <summary>
/// Test class for <see cref="ConfigurationExtensions.Read{T}"/>.
/// </summary>
public sealed class ConfigurationExtensionsTests
{
    private const string BotToken = "123456:TESTTOKEN";
    private const string OwnerChatId = "472619570";

    /// <summary>
    /// When the settings section is absent altogether
    /// And configuration is read
    /// Then startup fails rather than continuing with defaults.
    /// </summary>
    [Fact]
    public void Read_SectionMissing_Throws()
    {
        // Arrange
        var configuration = BuildConfiguration([]);

        // Act
        var exception = Record.Exception(() => configuration.Read<TelegramSettings>());

        // Assert
        Assert.IsType<ConfigurationErrorsException>(exception);
    }

    /// <summary>
    /// When a mandatory value is absent from an otherwise present section
    /// And configuration is read
    /// Then startup fails rather than binding it to null or zero.
    /// </summary>
    [Theory]
    [InlineData("TelegramSettings:OwnerChatId", OwnerChatId)]
    [InlineData("TelegramSettings:BotToken", BotToken)]
    public void Read_MandatoryValueMissing_Throws(string presentKey, string presentValue)
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [presentKey] = presentValue,
        });

        // Act
        var exception = Record.Exception(() => configuration.Read<TelegramSettings>());

        // Assert
        Assert.IsType<ConfigurationErrorsException>(exception);
    }

    /// <summary>
    /// When every mandatory value is present
    /// And configuration is read
    /// Then the settings are returned exactly as configured.
    /// </summary>
    [Fact]
    public void Read_EveryMandatoryValuePresent_ReturnsSettings()
    {
        // Arrange
        var expected = new TelegramSettings
        {
            BotToken = BotToken,
            OwnerChatId = 472619570L,
            BaseUrl = "http://localhost:58080",
        };
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["TelegramSettings:BotToken"] = BotToken,
            ["TelegramSettings:OwnerChatId"] = OwnerChatId,
            ["TelegramSettings:BaseUrl"] = "http://localhost:58080",
        });

        // Act
        var result = configuration.Read<TelegramSettings>();

        // Assert
        Assert.Equivalent(expected, result, strict: true);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
```

Each `[InlineData]` supplies the key that *is* present, so the other mandatory value is the one
missing. That covers both fields without a conditional in the test body.

`Assistant.UnitTests` already references `Assistant.Impl`. Add the two configuration packages to
its `csproj` if `IConfiguration` does not resolve.

- [ ] **Step 5: Run and watch them fail, then pass**

```bash
dotnet test tests/Assistant.UnitTests
```

Red first — `Read` does not exist. After Steps 1 to 3, expect **16** unit tests: the 12 existing
plus the 4 cases here.

- [ ] **Step 6: Wire it into startup**

`src/Assistant.Worker/Program.cs`:

```csharp
using Assistant.Impl;
using Assistant.Impl.Configuration;
using Assistant.Impl.Settings;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAssistantTelegram(builder.Configuration.Read<TelegramSettings>());

var host = builder.Build();
host.Run();
```

Add a project reference from `Assistant.Worker` to `Assistant.Impl` if one is not already there.

This is the point of the task: a missing token now stops the host during composition, before
`Run()`, instead of surfacing as a 401 the first time a reminder fires.

- [ ] **Step 7: Confirm `dotnet ef` still works**

This step exists because the risk is real, not hypothetical. `Program.cs` previously registered
nothing; it now throws without configuration. EF's tooling resolves
`AssistantDbContextFactory` first, so migrations should be unaffected — but EF also probes the
startup project's host builder, and that probe running `Main` would now throw.

```bash
dotnet ef migrations list --project src/Assistant.Repository --startup-project src/Assistant.Worker
```

Expected: the two migrations are listed. **If this fails**, do not work around it by weakening the
validation — report it. The fix would be to give the Worker an `appsettings.Development.json` with
placeholder values, which is a decision worth making deliberately rather than by accident.

- [ ] **Step 8: Commit**

```bash
git add src/ tests/
git commit -m "feat: validate Telegram settings while the host is composing"
```

---

## Task 3: `INotifier`, `TelegramNotifier`, and the fixture

**Files:** `src/Assistant.Interfaces/INotifier.cs`, `src/Assistant.Impl/**`,
`tests/Assistant.IntegrationTests/**`.

**Interfaces:**
- Consumes: the stub container from Task 1.
- Produces: `INotifier.SendAsync`, `AddAssistantTelegram`, `WireMockFixture`, `WireMockCollection`.

- [ ] **Step 1: Create `INotifier`**

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

- [ ] **Step 2: Write the fixture**

Add `<PackageReference Include="Telegram.Bot" />` to `src/Assistant.Impl/Assistant.Impl.csproj`
together with `Microsoft.Extensions.DependencyInjection.Abstractions`, and add a project
reference from `Assistant.IntegrationTests` to `Assistant.Impl` if one is not already there.

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

`tests/Assistant.IntegrationTests/Infrastructure/WireMockCollection.cs`:

```csharp
namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>
/// Groups every test class that shares the stub API.
/// </summary>
/// <remarks>
/// Separate from <see cref="PostgresCollection"/> because F4's tests need no database. A class
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

- [ ] **Step 3: Write the failing test**

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

- [ ] **Step 4: Run and watch it fail**

```bash
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests
```

Expected: compilation fails — neither `AddAssistantTelegram` nor any `INotifier` implementation
exists. That is the red state.

- [ ] **Step 5: Write `TelegramNotifier`**

`src/Assistant.Impl/Telegram/TelegramNotifier.cs`:

```csharp
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Delivers messages through the Telegram Bot API.
/// </summary>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="settings">Validated Telegram configuration.</param>
/// <remarks>
/// HTML parse mode is deliberate. MarkdownV2 has eighteen escape-sensitive characters, so an
/// underscore in a task title would produce a 400 on a live reminder — a formatting defect that
/// costs a delivery. HTML has three, and none occur in ordinary task text.
/// </remarks>
internal sealed class TelegramNotifier(ITelegramBotClient bot, TelegramSettings settings) : INotifier
{
    /// <inheritdoc/>
    public async Task SendAsync(string text, CancellationToken ct) =>
        await bot.SendMessage(settings.OwnerChatId, text, ParseMode.Html, cancellationToken: ct);
}
```

`SendMessage` is the v22 name, verified by reflection over the package. `SendTextMessageAsync`
will not compile.

- [ ] **Step 6: Register it**

`src/Assistant.Impl/ImplServiceCollectionExtensions.cs`:

```csharp
using Assistant.Impl.Settings;
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
    /// <param name="settings">
    /// Validated Telegram configuration. Read it with <c>IConfiguration.Read</c> so a missing
    /// value stops the host here, while it is composing, rather than at first delivery.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAssistantTelegram(
        this IServiceCollection services, TelegramSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<ITelegramBotClient>(
            _ => new TelegramBotClient(
                new TelegramBotClientOptions(settings.BotToken, settings.BaseUrl)));
        services.AddSingleton<INotifier>(
            provider => new TelegramNotifier(
                provider.GetRequiredService<ITelegramBotClient>(),
                provider.GetRequiredService<TelegramSettings>()));
        return services;
    }
}
```

- [ ] **Step 7: Run and watch it pass**

```bash
dotnet build
dotnet test tests/Assistant.IntegrationTests
```

Expected: `0 Warning(s)`, `0 Error(s)`, **16** integration tests — 14 from F1 to F3 plus the 2
cases added here.

- [ ] **Step 8: Prove the payload assertion can fail**

Temporarily change `ParseMode.Html` to `ParseMode.MarkdownV2` and run.

Expected: both cases fail with an `EquivalentException` naming member `ParseMode`. If they pass,
the assertion is not reading the payload and this task has not delivered.

Revert and confirm green.

- [ ] **Step 9: Prove the reset is working**

Temporarily remove `wireMock.ResetAsync()` from `InitializeAsync` (return `Task.CompletedTask`)
and run.

Expected: the second `[InlineData]` case fails, because `Assert.Single` sees two messages. If
both still pass, tests are not sharing the container and the collection is misconfigured.

Revert and confirm green.

- [ ] **Step 10: Commit**

```bash
git add src/ tests/
git commit -m "feat: send a message through Telegram"
```

---

## Task 4: Record what F4 settled, and correct the spec

**Files:** `docs/design/slice-1-reminders.md`, `docs/design/2026-08-22-slice-1-feature-backlog.md`,
`AGENTS.md`.

- [ ] **Step 1: Correct spec §7.1**

Replace the sentence *"WireMock runs in-process (1–5ms per call), so once Postgres is up a test
lands around 20–60ms."* with a statement that WireMock runs as its own container defined in
`compose.test.yaml`, that tests verify through its admin API, and that the request log is cleared
between tests the way Respawn clears tables. Keep the surrounding paragraphs intact.

- [ ] **Step 2: Update `AGENTS.md`**

The compose command now brings up two services, and integration tests need both. Say so where the
build and test commands are listed, and add `Assistant.WireMock` to the project map with one line
on what it is.

- [ ] **Step 3: Update the F4 backlog entry**

Mark it done. Record Decision A (the service, and its three costs), Decision D (the collection
merge owed at F5), Decision F (the payload assertion, and that F6's keyboard will fail it), and
Decision H — settings validated during composition, which pulls fail-fast forward from F14. Note
against F14 that the pattern now exists and it inherits `appsettings.{Environment}.json` and the
remaining settings types rather than the whole mechanism.

- [ ] **Step 4: Full verification**

```bash
docker compose -f compose.test.yaml down -v
docker compose -f compose.test.yaml up -d --build
dotnet build
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
```

`down -v` then `up -d --build` proves the whole thing works from nothing, which is what a fresh
clone or CI will do. Expected: `0 Warning(s)`, `0 Error(s)`, 16 unit tests, 16 integration tests.

- [ ] **Step 5: Commit and push**

```bash
git add docs/ AGENTS.md
git commit -m "docs: record the decisions F4 settled"
git push
```

Mark PR #6 ready for review. Do not merge.

---

## Self-review

**Spec coverage.** §3.1 — `TelegramNotifier` lands in `Impl/Telegram`. §3.3 and §7.4 — HTML parse
mode with the `_` and `*` case tested. §7.1 — corrected rather than quietly contradicted. §7.3 —
count, recipient, and exact text pinned in one assertion; buttons are F6's. §12.3 — the SDK
exception honoured, base address as a registration concern. §6.5's retry table is F14's.

**Placeholder scan.** No TBDs. Steps 6, 8, and 9 each state one expected outcome and what it
means if it does not happen. Step 3's `Directory.Build.props` line has a stated fallback.

**Type consistency.** `INotifier.SendAsync(string, CancellationToken)` → `Task` is identical in
the interface, the implementation, and the test. `SendMessagePayload(long, string, string)` is
constructed in the test exactly as declared in the fixture file.

**Known risk — the one I would watch.** `SentMessagesAsync` filters admin log entries by a path
ending in `/sendMessage`. When F9 adds the Anthropic stub to the same service, that filter is what
stops one API's traffic being read as another's. It is correct today and it is load-bearing
later, so the filter is not an incidental detail to tidy away.

**Second risk.** Building the container adds a step CI does not yet have — there is no CI until
F14. Until then `docker compose up -d --build` is a developer responsibility, which Task 4 Step 2
writes into `AGENTS.md` so it is not folklore.
