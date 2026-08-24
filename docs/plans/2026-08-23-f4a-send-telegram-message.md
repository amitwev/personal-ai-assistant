# F4a — Send a Telegram message

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** validated Telegram settings, `INotifier`, and `TelegramNotifier` — enough to send a real
message to a real phone. **Verification for this PR is manual and human:** configure the app, run
it, receive the message.

**Why split:** F4 grew past a reviewable size. F4a is the implementation, confirmed by a live
message. F4b then adds the WireMock service and the automated integration test, now knowing the
adapter genuinely works. See `docs/plans/2026-08-23-f4b-telegram-integration-tests.md`.

**Tech Stack:** Telegram.Bot 22.10.2.1, `Microsoft.Extensions.Configuration`, xUnit 2.9.3.

**Spec:** `docs/design/slice-1-reminders.md` §3.1, §3.3, §12.3.
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F4.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error in `src/` only.
- **Every class with arguments uses a primary constructor** (§12.5). No separate constructors.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first.
- Every `<summary>` is three lines. Primary constructor parameters are documented on the class.
- Central package management: versions in `Directory.Packages.props`, never inline (NU1008).
- PR budget: 1000 lines. Estimated ~260.

---

## The YAGNI tension, stated rather than hidden

Backlog §1 says a feature may only introduce code the same feature exercises with a test.
**`TelegramNotifier` ships here with no automated test.** That is a deliberate, temporary
exception, and it is worth being honest about:

- The settings and their validation *are* tested here — four unit tests, no Docker.
- `TelegramNotifier` itself is verified by a human receiving a message, which for an outbound
  adapter is stronger evidence than a stub: WireMock proves we send *something we believe is
  right*, a phone proves Telegram accepted it.
- The automated test is **owed by F4b** and is the first thing it does.

If F4b never lands, this exception becomes debt. That is the risk of the split, and it is the
reason F4b is next rather than "later".

---

## Verified before writing this plan

**The SDK renamed things in v22.** Reflected over the package:

```
TelegramBotClientOptions(String token, String baseUrl = null, Boolean useTestEnvironment = False)
TelegramBotClientExtensions.SendMessage(ITelegramBotClient, ChatId, String text, ParseMode parseMode, ...)
ParseMode members: None, Markdown, Html, MarkdownV2
```

It is `SendMessage`, **not** `SendTextMessageAsync`.

**`required` does not protect a settings class.** Binding a section missing its values:

```
PROBE section.Exists() (only unrelated key) = True
PROBE bound with missing required          = token='<null>' chat=0
PROBE absent section Exists()              = False
PROBE absent section Get<T>()              = NULL
```

Nothing throws. `required` is a compile-time contract for object initialisers and the binder goes
around it, so a `string` the nullable analysis swears is non-null holds `null` at runtime. That is
the whole reason `Validate()` exists. Note also that `Exists()` is true when *any* key sits under
the section, so a misspelled key makes an empty section look present.

**`ConfigurationErrorsException` needs a package** — it does not resolve from the shared
framework. `Trading.Ibkr.Common.csproj` references `System.Configuration.ConfigurationManager` for
the same reason.

**User secrets load only in Development.** Measured against a host with a `UserSecretsId`:

```
default environment      = Production   -> secret visible = '<null>'
DOTNET_ENVIRONMENT=Development          -> secret visible = 'SECRET-FROM-USER-SECRETS'
```

This matters for Task 4: a bot token set with `dotnet user-secrets` is invisible unless the
environment is Development, and the failure looks exactly like a missing token.

---

## Decisions this plan makes — review these first

### A. Settings are a bound, validated model, checked while the host composes

Modelled on `Trading.Ibkr`'s `IValidatableConfig` / `ConfigurationExtensions` pattern, as
requested. This pulls fail-fast forward from F14, where the backlog had parked it — F4 is the
first feature with real configuration, so it is the first with anything to validate.

`Program.cs` calls `Read<TelegramSettings>()` while composing the container, so a missing token
stops the host before `Run()` rather than surfacing as a 401 on the first reminder.

### B. Where the types live is forced by tests this repo already has

`TelegramSettings` cannot live in `Assistant.Models`:
`ConventionTests.Models_declare_no_methods_beyond_property_accessors` fails on `Validate()`. It
cannot live in `Assistant.Interfaces` either, which `Interfaces_declares_no_concrete_public_classes`
reserves for abstractions.

So `IValidatableConfig` → `Interfaces` (spec §3.1: every interface in the system), and
`TelegramSettings` + `ConfigurationExtensions` → `Impl`, beside the adapter that consumes them.

### C. `INotifier.SendAsync(string text, CancellationToken ct)` — the recipient is configuration

Single-user product, so a recipient parameter would carry one possible value at every call site.
Rendering stays out: F5 composes text, F4 delivers it, and `Contracts` stays empty until F5.

### D. A `send-test-message` switch, which is the one thing here with no automated test

The Worker is a bare host that does nothing. Without something to call `SendAsync`, "run it and
check your phone" is not possible, so this PR cannot be verified at all.

```
dotnet run --project src/Assistant.Worker -- send-test-message
```

sends one message and exits. It is five lines, it is the acceptance criterion for this PR, and it
stays useful afterwards as the fastest way to confirm credentials still work. F14's `/status`
command (spec §6.5) is the grown-up version and may replace it.

**Reject this if you would rather verify another way** — a scratch console project would also
work, and would leave no untested code in `src/`.

### E. The bot token goes in user secrets, never in `appsettings.Development.json`

`.gitignore` covers `.env`, `*.env`, and `appsettings.*.local.json` — it does **not** cover
`appsettings.Development.json`. A token placed there is one `git add` from being public, and this
repository is public.

`src/Assistant.Worker/Assistant.Worker.csproj` already carries a `UserSecretsId`, so the machinery
exists. Task 4 uses it.

---

## What F4a does NOT include

| Excluded | Where it goes |
| :--- | :--- |
| `Assistant.WireMock`, its Dockerfile, the compose service | F4b |
| `WireMockFixture`, `WireMockCollection`, the notifier integration test | F4b |
| The spec §7.1 correction about in-process WireMock | F4b, which makes it true |
| Rendering a reminder into text | F5 |
| Inline keyboards | F6 |
| Polly retry on 429 (spec §6.5) | F14 |
| `appsettings.{Environment}.json` files | F14 |

---

## File Structure

| Path | Responsibility |
| :--- | :--- |
| `Directory.Packages.props` | **Modify.** Telegram.Bot, Configuration, ConfigurationManager. |
| `src/Assistant.Interfaces/IValidatableConfig.cs` | **Create.** `void Validate()`. |
| `src/Assistant.Interfaces/INotifier.cs` | **Create.** One method. |
| `src/Assistant.Impl/Settings/TelegramSettings.cs` | **Create.** Bound settings + validation. |
| `src/Assistant.Impl/Configuration/ConfigurationExtensions.cs` | **Create.** `Read<T>()`. |
| `src/Assistant.Impl/Telegram/TelegramNotifier.cs` | **Create.** The only type naming an SDK type. |
| `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` | **Create.** `AddAssistantTelegram`. |
| `src/Assistant.Impl/Assistant.Impl.csproj` | **Modify.** Package references. |
| `src/Assistant.Worker/Program.cs` | **Modify.** Compose, validate, and the test switch. |
| `tests/Assistant.UnitTests/Configuration/ConfigurationExtensionsTests.cs` | **Create.** Four cases. |

---

## Test design

Four cases, all unit, no Docker. They cover the settings contract; the notifier is covered by the
human test in Task 4 and by F4b's automated one.

| Test | Kind | What it documents |
| :--- | :--- | :--- |
| `Read_SectionMissing_Throws` | `[Fact]` | An absent section stops startup |
| `Read_MandatoryValueMissing_Throws` | `[Theory]` ×2 | A missing token or chat id stops startup |
| `Read_EveryMandatoryValuePresent_ReturnsSettings` | `[Fact]` | The bound result, asserted whole |

**Equivalence classes.** For each mandatory field: present, or not. Null, empty, and whitespace
are one class — "not supplied" — so one representative each, which is what the `[Theory]` does by
supplying only the *other* field.

**Deliberately not tested:** that `Telegram.Bot` sends correctly (F4b), that Telegram accepts the
message (Task 4, by a human), and `BaseUrl` being absent (it is optional by design).

---

## Task 1: Settings and validation

**Files:** `Directory.Packages.props`, `src/Assistant.Interfaces/IValidatableConfig.cs`,
`src/Assistant.Impl/Settings/`, `src/Assistant.Impl/Configuration/`,
`tests/Assistant.UnitTests/Configuration/`.

- [ ] **Step 1: Package versions**

In `Directory.Packages.props`, alphabetically:

```xml
    <PackageVersion Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.4" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.4" />
    <PackageVersion Include="System.Configuration.ConfigurationManager" Version="10.0.11" />
    <PackageVersion Include="Telegram.Bot" Version="22.10.2.1" />
```

Add matching `PackageReference` entries — no inline versions, that is NU1008 — to
`src/Assistant.Impl/Assistant.Impl.csproj`, together with
`Microsoft.Extensions.DependencyInjection.Abstractions`.

- [ ] **Step 2: `IValidatableConfig`**

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

- [ ] **Step 3: `TelegramSettings`**

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
    /// F4b's tests point this at a stub container. Optional, so it is not validated: absent
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

`OwnerChatId` is checked against `0`, not null: it is a `long`, so an absent key binds to the
default. Measured, see above.

- [ ] **Step 4: `ConfigurationExtensions`**

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
    /// Each of the three checks catches a different failure. An absent section binds to null. A
    /// section holding only an unrelated key still reports <c>Exists()</c> as true, so binding
    /// succeeds and leaves the real values empty. And <c>required</c> is a compile-time contract
    /// the binder goes around, so only <see cref="IValidatableConfig.Validate"/> catches a value
    /// present in shape but missing in fact.
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

- [ ] **Step 5: Write the failing tests**

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
missing — both fields covered with no conditional in the test body.

- [ ] **Step 6: Red, then green**

```bash
dotnet test tests/Assistant.UnitTests
```

Red first: `Read` does not exist. After Steps 2–4, expect **16** unit tests — the 12 existing plus
the 4 here.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props src/ tests/
git commit -m "feat: read and validate settings while the host composes"
```

---

## Task 2: `INotifier` and `TelegramNotifier`

- [ ] **Step 1: `INotifier`**

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

- [ ] **Step 2: `TelegramNotifier`**

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

`SendMessage` is the v22 name, verified by reflection. `SendTextMessageAsync` will not compile.

- [ ] **Step 3: Registration**

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

- [ ] **Step 4: Build**

```bash
dotnet build
```

Expected `0 Warning(s)`, `0 Error(s)`. No new tests here — that is Decision D's tension, and
Task 4 plus F4b close it.

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "feat: deliver messages through the Telegram Bot API"
```

---

## Task 3: Compose the host and add the test switch

- [ ] **Step 1: `Program.cs`**

Add a project reference from `Assistant.Worker` to `Assistant.Impl` if absent, then:

```csharp
using Assistant.Impl;
using Assistant.Impl.Configuration;
using Assistant.Impl.Settings;
using Assistant.Interfaces;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAssistantTelegram(builder.Configuration.Read<TelegramSettings>());

var host = builder.Build();

if (args.Contains("send-test-message"))
{
    var notifier = host.Services.GetRequiredService<INotifier>();
    await notifier.SendAsync("Assistant is configured and can reach you.", CancellationToken.None);
    return;
}

host.Run();
```

`Read` runs during composition, so a missing token stops the process before `Build()` returns.
That is the fail-fast behaviour this task exists for.

- [ ] **Step 2: Confirm `dotnet ef` still works**

This step exists because the risk is real. `Program.cs` used to register nothing and now throws
without configuration, and EF's tooling probes the startup project's host builder.

```bash
dotnet ef migrations list --project src/Assistant.Repository --startup-project src/Assistant.Worker
```

Expected: both migrations listed. **If it fails, report it — do not weaken the validation.** The
fix would be an `appsettings.Development.json` with placeholder values, and that is a decision to
make deliberately rather than by accident.

- [ ] **Step 3: Full verification and commit**

```bash
dotnet build
dotnet test tests/Assistant.UnitTests
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests
```

Expected: `0 Warning(s)`, `0 Error(s)`, 16 unit tests, 14 integration tests (unchanged — F4a adds
none).

```bash
git add src/
git commit -m "feat: compose the host and add a test-message switch"
```

---

## Task 4: The human test — a real message to a real phone

This is F4a's acceptance criterion. **It is run by the maintainer, not by an agent**, and it needs
a real bot token.

- [ ] **Step 1: Create a bot and find the chat id**

Message `@BotFather` on Telegram, `/newbot`, and keep the token. Then message `@userinfobot` to
get the numeric chat id, or send the new bot a message and read `chat.id` from
`https://api.telegram.org/bot<TOKEN>/getUpdates`.

- [ ] **Step 2: Store the token in user secrets — never in a file**

`.gitignore` covers `.env`, `*.env`, and `appsettings.*.local.json`. It does **not** cover
`appsettings.Development.json`, and this repository is public, so a token placed there is one
`git add` away from being exposed.

```bash
cd src/Assistant.Worker
dotnet user-secrets set "TelegramSettings:BotToken" "<token from BotFather>"
dotnet user-secrets set "TelegramSettings:OwnerChatId" "<your numeric chat id>"
```

The `UserSecretsId` already exists in the csproj.

- [ ] **Step 3: Run it**

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project src/Assistant.Worker -- send-test-message
```

**`DOTNET_ENVIRONMENT=Development` is required.** Measured: the default environment is
`Production`, and user secrets do not load there — the token reads as `<null>` and the run fails
with a missing-token error that looks exactly like the secret was never set.

```
default environment      -> secret visible = '<null>'
DOTNET_ENVIRONMENT=Development -> secret visible = 'SECRET-FROM-USER-SECRETS'
```

Expected: the message arrives on your phone.

- [ ] **Step 4: Confirm fail-fast, which is the other half of what was asked**

```bash
cd src/Assistant.Worker && dotnet user-secrets remove "TelegramSettings:BotToken"
DOTNET_ENVIRONMENT=Development dotnet run --project src/Assistant.Worker -- send-test-message
```

Expected: `ConfigurationErrorsException: TelegramSettings.BotToken is missing or empty.` — thrown
during composition, before anything tries to reach Telegram. Then set the token back.

---

## Task 5: Record what F4a settled

- [ ] **Step 1: Backlog**

Split the F4 entry into F4a and F4b. Record: settings validated during composition (which pulls
fail-fast forward from F14 — note against F14 that it inherits only
`appsettings.{Environment}.json` and the remaining settings types, not the mechanism); the
recipient as configuration; and that `TelegramNotifier`'s automated test is owed by F4b.

- [ ] **Step 2: `AGENTS.md`**

Add one line under running locally: the token goes in user secrets, and `DOTNET_ENVIRONMENT=Development`
is required for them to load.

- [ ] **Step 3: Commit and push, then mark PR #6 ready**

```bash
git add docs/ AGENTS.md
git commit -m "docs: record the decisions F4a settled"
git push
```

Do not merge.

---

## Self-review

**Spec coverage.** §3.1 — `TelegramNotifier` in `Impl/Telegram`. §3.3 — HTML parse mode, with the
reasoning in the type's own remarks. §12.3 — the SDK exception honoured; base address is a
registration concern, which is what lets F4b point it at a stub with no production seam. §7's
testing levels are F4b's to satisfy.

**Placeholder scan.** No TBDs. Task 3 Step 2 states one outcome and what to do if it does not
happen.

**Type consistency.** `INotifier.SendAsync(string, CancellationToken)` → `Task` is identical in
the interface, the implementation, and `Program.cs`. `TelegramSettings` is constructed in the test
exactly as declared.

**Known risk, and it is the reason F4b is next.** `TelegramNotifier` ships with no automated test.
Task 4 is a human check that cannot run in CI and will not catch a regression six weeks from now.
The split is worth it because a live message proves more than a stub — but only if F4b follows.
