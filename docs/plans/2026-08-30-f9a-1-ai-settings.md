# F9a-1 — AI settings

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F9a makes the assistant able to reach a chat model and return its answer to the owner
over Telegram — no tools yet; parsing a `create_task` call out of the answer is F9b. This
document is F9a's **first of four** independently reviewable PRs. It ships no AI behaviour: it
introduces `AiSettings`, the validated configuration every later slice reads, and wires it into
the composition root so a fresh clone fails fast, naming exactly what is missing, rather than
failing silently three slices from now.

**Tech Stack:** .NET 10 (`net10.0`), nullable enabled, warnings are errors — the existing stack,
unchanged. This slice adds no new NuGet package, no new project reference, and touches no test
project; Refit, WireMock.Net, and `Microsoft.Extensions.TimeProvider.Testing` all arrive in
slice 3.

**Spec:** `docs/design/slice-1-reminders.md` — the approved specification for the whole
assistant. This slice implements none of its AI-facing sections directly (§5.1 flow, §5.2 system
prompt, §5.5 provider routing are slices 2–4's concern); it only lays the configuration
groundwork spec §3.4 names (`AiSettings` in `Impl/Settings/`, decision 1 below).
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F9, split into F9a (this
slice and its three siblings) and F9b.

---

## Where this sits

F9a ships as four independently reviewable PRs rather than one. Precedent: F8 shipped its plan
and its code together in one PR and broke this repository's 1000-line budget (1243 plan + 598
code = 1841 lines); F9a's plan is split by PR instead, each slice getting its own document.

1. **Slice 1 — AI settings (this document).** `AiSettings`, `appsettings.json`, `.env.example`,
   a minimal `AddAssistantAi`, and the `Program.cs` chain link.
2. **Slice 2 — the clock and the system prompt.** `ILocalTimeResolver` gains `CurrentLocalTime`
   and `ZoneId`; `SystemPrompt` builds the text sent to the model.
3. **Slice 3 — reach the model.** Refit, the wire types, `IChatClient`, `ChatCompletionsClient`,
   failure handling, and the WireMock stub.
4. **Slice 4 — the owner gets the model's answer.** `MessageHandler` replaces F7's echo with a
   real call to the model, plus the design-doc corrections that follow from it.

Slices 2–4 each get their own plan document, written after the slice before it has merged. This
document covers slice 1 only — it is not a guide to implementing slices 2–4.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error everywhere.
- **Every class taking arguments uses a primary constructor.** No separate constructors.
- **CS9113 is an error**: a primary-constructor parameter nothing references fails the build.
  Never declare a parameter one step ahead of the step that uses it.
- Every enum's first member is `Unknown`, with no explicit numeric values. New members are
  **appended**, never inserted.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- Central package management; no inline `Version=` (NU1008).
- No emoji anywhere: source, tests, docs, or commit messages.
- **Never run `docker compose down -v`.** Use `docker compose -f compose.test.yaml down` with no
  flags.
- Integration tests need `docker compose -f compose.test.yaml up -d --build` first — and
  `--build`, because this feature changes the WireMock stub image.
- PR budget: 1000 changed lines per PR, excluding this plan (which merges on its own, docs-only).
  This plan is split into four PRs — PR 1 ~90, PR 2 ~120, PR 3 ~260, PR 4 ~150 code lines — every
  one comfortably under budget by itself.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
  ```

---

## Verified facts this plan rests on

- OpenRouter's `GET https://openrouter.ai/api/v1/models` answered with 396 models on
  2026-08-29. Slug format is `vendor/model`; `anthropic/claude-sonnet-5` and
  `anthropic/claude-haiku-4.5` are both present. Both slugs are verified; this plan invents no
  others. (Decision 4, below, ships the first as `AiSettings`'s default model;
  `.env.example`'s comment names the second as the cheaper alternative.)
- `ConfigurationExtensions.Read<T>` binds the section **named after the type**, so `AiSettings`
  reads the `AiSettings` section and throws `ConfigurationErrorsException` when the section is
  absent — so whatever defaults a fresh clone should have must ship in `appsettings.json`.
- `TelegramSettings.BaseUrl` is the existing precedent for a nullable, optional base URL —
  decision 2, below, explains why `AiSettings.BaseUrl` is not modelled the same way.

---

## Inherited context: provider routing (governs all four F9a slices)

The repository owner ruled Anthropic out of slice 1 entirely as a direct dependency. The
endpoint F9a reaches speaks the **OpenAI chat-completions** format, which OpenRouter, OpenAI,
Groq and a local Ollama all serve — so every type F9a introduces is named for the wire format,
not the vendor (`IChatCompletionsApi`, `ChatCompletionsClient`, arriving in slice 3, not this
one). Moving providers becomes a change to `AiSettings.BaseUrl` and `AiSettings.Model` alone.

This slice does not implement that routing — there is no client yet — but `AiSettings`'s
vendor-neutral field names (`BaseUrl`, `Model`, not `OpenRouterUrl` or `OpenRouterModel`) and its
default value pointing at OpenRouter both follow directly from this ruling. It is recorded here
as inherited context, not as a decision this document makes.

---

## Decisions this slice makes

Numbered 1–5 here. The plan these four PRs were split from numbered its full, four-slice
decision set A–O; these five carried letters C, D, M, N and O there. Renumbered for this
standalone document, since it carries only these five.

### 1. `AiSettings` lives in `Impl/Settings/`, with the other settings

`TelegramSettings`, `TimeSettings` and `DatabaseSettings` are all there; configuration is not an
`Ai/` concern, even though spec §3.4 places the Refit interfaces themselves in `Impl/Ai/`. The
name is `AiSettings` because this repository's convention is `<Subsystem>Settings`, and spec §3.4
already names the subsystem folder `Ai`.

### 2. `BaseUrl` is required here, unlike `TelegramSettings.BaseUrl`

Telegram's `BaseUrl` is nullable because absent means "the real Telegram" — there is exactly one
real Telegram API, so a missing value has an unambiguous meaning. There is no single "the"
chat-completions provider; that is the entire point of the provider-routing context above. So
`appsettings.json` ships `https://openrouter.ai/api/v1` as a changeable default, and validation
requires an absolute URI rather than treating absence as meaningful.

### 3. `.env.example` loses two lines that nothing reads

`LLM__ANTHROPIC__APIKEY` and `LLM__OPENROUTER__APIKEY` predate every naming convention in this
repository, and no code anywhere binds them — grep confirms zero references outside the file
itself. They are replaced by `AiSettings__ApiKey`, `AiSettings__Model` and `AiSettings__BaseUrl`.

### 4. The default model ships as `anthropic/claude-sonnet-5`

Verified present in OpenRouter's live model list on 2026-08-29 (see "Verified facts," above).
This is a model **slug**, not a code identifier — a slug naming a vendor is unavoidable under
the provider-routing context above, since OpenRouter's own catalogue is vendor-prefixed; that
context is about type and file names, not about the string values `appsettings.json` and
`.env.example` carry. `.env.example`'s comment names `anthropic/claude-haiku-4.5` as the cheaper
alternative — also verified present. Neither slug is invented.

### 5. `AiSettings` gets no dedicated unit test file

`tests/Assistant.UnitTests/Configuration/ConfigurationExtensionsTests.cs` already proves
`IConfiguration.Read<T>`'s entire mechanism, generically, using `TelegramSettings` as its
vehicle: an absent section throws, a mandatory value missing from an otherwise-present section
throws through `IValidatableConfig.Validate`, and a fully-populated section binds and returns
unchanged. `AiSettings` binds through that same `Read<T>`, so a settings-specific test proving
the same three facts a second time would be the duplication spec §7.2 forbids, and four tests
over `AiSettings.Validate`'s four guard clauses — one per `if` — is implementation testing,
which this repository's owner rules out on principle.

`TimeSettingsTests` is not a precedent for keeping such a file. Read closely, it asserts nothing
about a null or empty `IanaTimeZone` — that ground is exactly what `ConfigurationExtensionsTests`
already covers, generically, for every settings type. What `TimeSettingsTests` earns its place by
testing is the one thing `ConfigurationExtensionsTests` cannot reach: a value that is present,
non-empty, and still wrong — an identifier this machine's tzdata does not know, rejected by name.
`AiSettings` has no rule of that shape. Every one of its checks — an empty key, a relative URL, an
empty model, a non-positive token limit — is a guard clause, and the mechanism that runs a guard
clause and turns its failure into `ConfigurationErrorsException` is exactly what
`Read_MandatoryValueMissing_Throws` already exercises, generically, today.

`AiSettings.Validate()` itself is unchanged, and it still runs at every startup through `Read<T>`.
Dropping the automated test is not dropping the code or its verification: this slice's own
manual `dotnet run` check (Step 6, below) — a missing `AiSettings__ApiKey` surfacing a
`ConfigurationErrorsException` naming it — is what proves `Validate()` fires in practice, and it
proves it the moment `AiSettings` exists, immediately, rather than waiting for a later slice's
test suite.

---

## What this slice does NOT include

- **Any AI behaviour.** `AddAssistantAi` registers `AiSettings` and nothing else. The chat
  client, the clock (`CurrentLocalTime`, `ZoneId`), the system prompt, and `MessageHandler`'s
  change away from F7's echo all arrive in slices 2–4 (see "Where this sits," above), each with
  its own plan document.
- **A dedicated unit test file for `AiSettings`.** Decision 5, above, explains why.
- **Any new NuGet package, project reference, or Docker/WireMock change.** This slice touches
  five files, all of them already inside `Assistant.Impl` or `Assistant.Worker`.

---

## File Structure

```
src/Assistant.Impl/
    Settings/AiSettings.cs                 new
    ImplServiceCollectionExtensions.cs     + AddAssistantAi, settings only

src/Assistant.Worker/
    Program.cs                             + AddAssistantAi(...) link in the chain
    appsettings.json                       + AiSettings section

.env.example                               - LLM__*, + AiSettings__*
```

---

## Validation

This slice has no automated test of its own (decision 5) and no caller of `AiSettings` besides
the composition root — so it is validated by running the app, in Step 5 and Step 6 below:

1. `dotnet build` and `dotnet test tests/Assistant.UnitTests` still pass, unchanged at 37 tests
   — proof nothing broke.
2. `dotnet run --project src/Assistant.Worker`, with `AiSettings__ApiKey` set and a deliberately
   unreachable database, fails on the database, not on configuration — proof the shipped
   `AiSettings` defaults (`BaseUrl`, `Model`, `MaxTokens`) plus a supplied key are enough to
   compose and pass `Validate()`.
3. The same command with `AiSettings__ApiKey` omitted fails immediately with
   `ConfigurationErrorsException: AiSettings.ApiKey is missing or empty.`, before the process
   ever reaches `builder.Build()` — proof the fail-fast check works and names the right value.
4. `DOTNET_ENVIRONMENT=Development dotnet run --project src/Assistant.Worker -- send-test-message`
   still succeeds with **no** `AiSettings` configuration at all — proof the diagnostic path,
   which builds and returns before this slice's chain link ever runs, is unaffected.

---

## Steps

**Decisions this slice carries:** 1–5, given in full above.

**Files:**
- Create: `src/Assistant.Impl/Settings/AiSettings.cs`
- Modify: `src/Assistant.Worker/appsettings.json`
- Modify: `.env.example`
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`
- Modify: `src/Assistant.Worker/Program.cs`

**Produces:** `AiSettings`, and a minimal `AddAssistantAi` that registers it — extended by
slice 3, never touched in `Program.cs` again after this slice.

**The open question this slice has to resolve:** `AiSettings` is read at startup, but
`AddAssistantAi`'s eventual body — the Refit client, `SystemPrompt` — doesn't exist until slice 3
has something to hand it to. The rejected option was reading `AiSettings` in `Program.cs` and
discarding the result until slice 3 had something to hand it to: that line would exist only to be
deleted once `AddAssistantAi` existed, which is a modification wearing an extension's clothes,
and it breaks a standing owner preference for code that is open for extension, closed for
modification — write it once, then only ever add to it. So this slice creates the real
`AddAssistantAi` now, with a body that does only what this slice needs (register the validated
settings). `Program.cs` gets exactly one line, added to the existing chain, matching
`TelegramSettings`'s and `TimeSettings`'s own precedent exactly: `builder.Configuration.Read<T>()`
threaded straight in as the argument to the registration method that consumes it. `Read<T>`
validates as a *side effect* of binding — it calls `settings.Validate()` before returning — so
this one line is what makes this slice's fail-fast check (Step 6, below) work, with no discard
and no bespoke mechanism. Slice 3 only ever appends to `AddAssistantAi`'s body; slice 4 needs no
`Program.cs` change at all — a claim slice 4's own plan document will confirm, not this one.

- [ ] **Step 1: Write `AiSettings`**

No failing test precedes this step. Decision 5, above, explains why:
`ConfigurationExtensionsTests` already proves `Read<T>`'s mechanism generically, using
`TelegramSettings` as its vehicle, and `AiSettings`'s own checks are guard clauses of exactly the
shape that test already exercises — a settings-specific copy of the same three facts would be the
duplication spec §7.2 forbids. `Validate()` itself runs unchanged; this slice's own manual
`dotnet run` check (Step 6, below) is what proves it fires in practice, not a dedicated unit test.

Create `src/Assistant.Impl/Settings/AiSettings.cs`:

```csharp
using System.Configuration;
using Assistant.Interfaces;

namespace Assistant.Impl.Settings;

/// <summary>
/// Configuration for the chat-completions endpoint the assistant reaches for an answer.
/// </summary>
/// <remarks>
/// One provider serves the whole assistant. Unlike <see cref="TelegramSettings.BaseUrl"/>,
/// <see cref="BaseUrl"/> here is required: there is no single "the" chat-completions provider
/// the way there is a single real Telegram API, so a value must always be supplied, and
/// <c>appsettings.json</c> ships OpenRouter's address as a changeable default (decision 2).
/// </remarks>
public sealed class AiSettings : IValidatableConfig
{
    /// <summary>
    /// The API key sent as a bearer token on every request.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// The chat-completions endpoint's base address, such as
    /// <c>https://openrouter.ai/api/v1</c>.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// The model slug to request, such as <c>anthropic/claude-sonnet-5</c>.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// The maximum number of tokens the model may return.
    /// </summary>
    public required int MaxTokens { get; init; }

    /// <inheritdoc/>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(ApiKey)} is missing or empty.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(BaseUrl)} is '{BaseUrl}', which is not an "
                + "absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(Model)} is missing or empty.");
        }

        if (MaxTokens <= 0)
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(MaxTokens)} is {MaxTokens}, which is not "
                + "positive.");
        }
    }
}
```

- [ ] **Step 2: Ship the default and update `.env.example`**

`src/Assistant.Worker/appsettings.json` — add a sibling to `TimeSettings`:

```json
  "AiSettings": {
    "BaseUrl": "https://openrouter.ai/api/v1",
    "Model": "anthropic/claude-sonnet-5",
    "MaxTokens": 1024
  }
```

`ApiKey` is deliberately absent — like `TelegramSettings.BotToken`, it is a secret and never
ships a default; a fresh clone without `AiSettings__ApiKey` set fails `Validate()` naming exactly
that (Step 6, below).

`.env.example` — replace the two lines decision 3 retires:

```
# Anthropic API key (primary LLM provider)
LLM__ANTHROPIC__APIKEY=

# OpenRouter API key (fallback provider; optional)
LLM__OPENROUTER__APIKEY=
```

with:

```
# API key for the chat-completions endpoint AiSettings__BaseUrl points at.
AiSettings__ApiKey=

# Model slug to request. Defaults to anthropic/claude-sonnet-5; the cheaper
# anthropic/claude-haiku-4.5 is a good alternative.
AiSettings__Model=

# Chat-completions endpoint base address. Defaults to OpenRouter; point this
# at any OpenAI-compatible endpoint instead (OpenAI, Groq, a local Ollama).
AiSettings__BaseUrl=
```

- [ ] **Step 3: Create `AddAssistantAi`, registering only the settings**

Add to `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`, after `AddAssistantTime`:

```csharp
    /// <summary>
    /// Registers the chat-completions endpoint's settings.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <param name="settings">
    /// Validated chat-model configuration. Read it with <c>IConfiguration.Read</c> so a missing
    /// key or an unusable base address stops the host here, while it is composing, rather than at
    /// the first message the owner sends.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Registers only <see cref="AiSettings"/> for now; the chat-completions client itself is
    /// added to this method's body once there is something to build it from
    /// (<c>IChatCompletionsApi</c>, <c>SystemPrompt</c>). This method's signature, and the
    /// settings registration above, do not change when that happens — only the body grows.
    /// </remarks>
    public static IServiceCollection AddAssistantAi(
        this IServiceCollection services, AiSettings settings)
    {
        services.AddSingleton(settings);
        return services;
    }
```

No new `using` is needed — `Assistant.Impl.Settings` is already imported in this file, for
`TelegramSettings` and `TimeSettings`.

- [ ] **Step 4: Wire `AddAssistantAi` into `Program.cs`'s existing chain**

Add one link, matching `AddAssistantTime`'s own call shape exactly — `Read<AiSettings>()` threaded
straight in as the argument, never stored, never discarded:

```csharp
builder.Services.AddAssistantRepository(builder.Configuration.Read<DatabaseSettings>().ConnectionString)
                .AddAssistantServices()
                .AddAssistantTime(builder.Configuration.Read<TimeSettings>())
                .AddAssistantAi(builder.Configuration.Read<AiSettings>())
                .AddAssistantScheduler()
                .AddAssistantListener();
```

**Placement, chosen deliberately:** after `AddAssistantTime`, before `AddAssistantScheduler` and
`AddAssistantListener`. `Program.cs` is not touched again after this slice — slice 3 only ever
appends to `AddAssistantAi`'s body, and slice 4 needs no `Program.cs` change at all — so this
position has to be right the first time. From slice 3 onward, `AddAssistantAi`'s own `<remarks>`
document that it requires `AddAssistantTime` for the `ILocalTimeResolver` `SystemPrompt` reads
from; placing the call after `AddAssistantTime` keeps the chain reading top to bottom as a
dependency order, the same convention `AddAssistantListener`'s own `<remarks>` already follow
("Requires `AddAssistantTelegram` … and `AddAssistantServices`…"). .NET's DI does not actually
require this — a singleton's dependencies resolve lazily at construction, not at registration —
so it is a readability convention this file already has, not a correctness requirement.

**The `send-test-message` diagnostic is unaffected.** That branch (`Program.cs`, before this
chain) builds a minimal host and returns early, before this chain ever runs — so a missing
`AiSettings__ApiKey` cannot break it:
`DOTNET_ENVIRONMENT=Development dotnet run --project src/Assistant.Worker -- send-test-message`
still works with no `AiSettings` configured at all, exactly as it did before this slice.

- [ ] **Step 5: Confirm nothing broke**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
```

Expected: zero warnings, 37 passed — the F8 baseline, unchanged (decision 5: no dedicated
`AiSettings` test file).

- [ ] **Step 6: Prove the host boots with the key set, and fails fast without it**

Assumes a local `.env`/user secrets already carry `TelegramSettings__BotToken` and
`TelegramSettings__OwnerChatId`, per `AGENTS.md`'s "Run locally" section — the same assumption
F8's own Task 3 Step 7 made.

```bash
DatabaseSettings__ConnectionString="Host=localhost;Port=1;Database=x;Username=x;Password=x" \
AiSettings__ApiKey="test-key" \
DOTNET_ENVIRONMENT=Development \
  dotnet run --project src/Assistant.Worker
```

Expected: it fails trying to reach Postgres on port 1, **not** on configuration — proving the
shipped `AiSettings` defaults (`BaseUrl`, `Model`, `MaxTokens`) plus a supplied `ApiKey` are
enough to compose and pass `Validate()`. Stop the process once you have seen the error.

Then, omitting `AiSettings__ApiKey`:

```bash
DatabaseSettings__ConnectionString="Host=localhost;Port=1;Database=x;Username=x;Password=x" \
DOTNET_ENVIRONMENT=Development \
  dotnet run --project src/Assistant.Worker
```

Expected: `System.Configuration.ConfigurationErrorsException: AiSettings.ApiKey is missing or
empty.`, thrown before anything touches Postgres — the process never reaches `builder.Build()`.
Record both outputs in the PR report.

- [ ] **Step 7: Commit**

```bash
git add src/Assistant.Impl/Settings/AiSettings.cs src/Assistant.Worker/appsettings.json \
        .env.example src/Assistant.Impl/ImplServiceCollectionExtensions.cs \
        src/Assistant.Worker/Program.cs
git commit
```

Message:

```
feat: add AiSettings and a minimal AddAssistantAi

AiSettings joins TelegramSettings, TimeSettings and DatabaseSettings in
Impl/Settings, validated the same way: an absolute BaseUrl, a non-empty
Model, a non-empty ApiKey and a positive MaxTokens fail fast at startup
rather than on the first message the owner sends. AddAssistantAi ships now,
registering only the settings, rather than Program.cs threading a
throwaway read until the client exists -- its body grows in a later
change, but its signature and this first registration do not.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

---

## Self-review

- [ ] `dotnet build --no-restore` — zero warnings, zero errors
- [ ] `dotnet test tests/Assistant.UnitTests` — 37 passed, unchanged from the F8 baseline
      (decision 5: no dedicated `AiSettings` test file)
- [ ] No new package reference and no new project reference anywhere
- [ ] `AiSettings.Validate()` has exactly four guard clauses — empty `ApiKey`, non-absolute
      `BaseUrl`, empty `Model`, non-positive `MaxTokens` — each throwing
      `ConfigurationErrorsException` naming the offending property
- [ ] `appsettings.json` ships `BaseUrl`, `Model` and `MaxTokens` defaults but no `ApiKey` —
      like `TelegramSettings.BotToken`, a secret never ships a default
- [ ] `.env.example` no longer contains `LLM__ANTHROPIC__APIKEY` or `LLM__OPENROUTER__APIKEY`;
      it contains `AiSettings__ApiKey`, `AiSettings__Model` and `AiSettings__BaseUrl` instead
- [ ] `AddAssistantAi`'s body registers only `AiSettings` — no client, no `SystemPrompt`, nothing
      slice 3 has not shipped yet
- [ ] `Program.cs`'s `AddAssistantAi(...)` link sits between `AddAssistantTime` and
      `AddAssistantScheduler`, matching the chain's top-to-bottom dependency-order convention
- [ ] The `send-test-message` diagnostic path is unaffected — it builds and returns before this
      slice's chain link ever runs, so it still works with zero `AiSettings` configuration
- [ ] Every new public member (`AiSettings` and its four properties, `Validate`,
      `AddAssistantAi`) carries a three-line `<summary>`
- [ ] Both `dotnet run` checks in Step 6 were actually performed and both outputs recorded, not
      merely predicted
- [ ] No emoji in any changed file, including the commit message
- [ ] Diff comfortably under the 1000-line PR budget (~90 lines estimated)
