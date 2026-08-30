# F9a-3 — reach the model

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F9a makes the assistant able to reach a chat model and return its answer to the owner
over Telegram — no tools yet; parsing a `create_task` call out of the answer is F9b. This
document is F9a's **third of four** independently reviewable PRs. It ships the actual network
call: Refit, the wire types for the OpenAI-compatible chat API, `IAiClient`, `AiClient`, failure
handling, the WireMock stub, and the integration tests that prove all of it against a stub —
still with no caller wiring it into `MessageHandler`.

**Tech Stack:** .NET 10 (`net10.0`), nullable enabled, warnings are errors — the existing stack.
This slice adds two new NuGet packages, both pinned to their current stable release: Refit
15.2.0 and Refit.HttpClientFactory 15.2.0. WireMock.Net gains a second stub endpoint. No other
new package.

**Spec:** `docs/design/slice-1-reminders.md` §3.2 (reference rules), §3.6 (extension seams —
its client-interface row is superseded by decision 1's naming, below, though the document itself
is not touched here), §5.1 (flow), §5.2 (system prompt — implemented by slice 2), §5.5 (provider
routing — decision 1 below supersedes its naming), §7.1–§7.3 (testing strategy), §12.3 (Refit
for HTTP clients), §12.6 (no emoji).
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F9, split into F9a (of which
this is slice 3 of four) and F9b.

---

## Where this sits

F9a ships as four independently reviewable PRs rather than one. Precedent: F8 shipped its plan
and its code together in one PR and broke this repository's 1000-line budget (1243 plan + 598
code = 1841 lines); F9a's plan is split by PR instead, each slice getting its own document.

1. **Slice 1 — AI settings.** `AiSettings`, `appsettings.json`, `.env.example`, a minimal
   `AddAssistantAi`, and the `Program.cs` chain link. Merged as `987ad21`.
2. **Slice 2 — the clock and the system prompt.** `ILocalTimeResolver` gains `CurrentLocalTime`
   and `ZoneId`; `SystemPrompt` builds the text sent to the model. Merged as `3b136fe`.
3. **Slice 3 — reach the model (this document).** Refit, the wire types, `IAiClient`,
   `AiClient`, failure handling, and the WireMock stub.
4. **Slice 4 — the owner gets the model's answer.** `MessageHandler` replaces F7's echo with a
   real call to the model, plus the design-doc corrections that follow from it.

Slice 4 gets its own plan document, written after this slice has merged. This document covers
slice 3 only — it is not a guide to implementing slice 4.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error everywhere.
- **Every class taking arguments uses a primary constructor.** No separate constructors.
- **CS9113 is an error**: a primary-constructor parameter nothing references fails the build.
  Never declare a parameter one step ahead of the step that uses it. Commit 1's
  `AiClient` takes no `ILogger` for exactly this reason.
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
  `--build`, because this slice changes the WireMock stub image (`AiStubs.cs`, new
  inside `tests/Assistant.WireMock/`).
- PR budget: 1000 changed lines per PR, excluding the plan (which merges on its own, docs-only).
  The rejected monolith this plan was split from estimated this slice at ~260 code lines, the
  largest of the four — comfortably under budget by itself.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
  ```

---

## Verified facts this plan rests on

- Refit and Refit.HttpClientFactory's current stable version is **15.2.0** (nuget.org, confirmed
  before this document was written).
- `JsonNamingPolicy.SnakeCaseLower` exists in .NET 8 and later, so `max_tokens` needs no
  per-property `[JsonPropertyName]` attribute anywhere this slice's production code writes the
  wire types (`AiRequest`, `AiMessage`, `AiResponse`, `AiChoice`) — the naming policy set on
  `AddAssistantAi`'s `RefitSettings` handles every property.
- `Directory.Packages.props` already carries
  `<PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.9.0" />`, used
  today only by `Assistant.UnitTests` (`SystemPromptTests.cs`, `LocalTimeResolverTests.cs`).
  `tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj` does not reference it —
  confirmed by reading both files before this document was written. Decision 4, below, is why
  this slice does not add that reference.
- `Directory.Packages.props`'s existing entries run alphabetically, with
  `Npgsql.EntityFrameworkCore.PostgreSQL` immediately followed by `Respawn` — confirmed by
  reading the file; that is where `Refit` and `Refit.HttpClientFactory` are inserted (Commit 1,
  Step 1).
- `src/Assistant.Impl/Assistant.Impl.csproj`'s `PackageReference` entries run alphabetically too,
  with `Microsoft.Extensions.Logging.Abstractions` immediately followed by
  `System.Configuration.ConfigurationManager` — confirmed by reading the file; that is where the
  same two references are inserted.
- `DependencyRuleTests.Interfaces_do_not_depend_on_infrastructure_libraries` in
  `tests/Assistant.UnitTests/Architecture/DependencyRuleTests.cs` already carries
  `[InlineData("Refit")]` — confirmed by reading the file before this document was written. Its
  own remarks say it was written ahead of this feature, so it needs no change here; this slice
  gives it the first thing it has ever had to catch. No architecture test in that file names any
  of `Assistant.Impl`'s subfolders directly, so nothing there needs a new rule either — the wire
  types and the Refit interface live in `Impl/Ai/`, never in `Impl/Services/`, keeping that
  folder free of `Telegram.Bot`, the wire types for the OpenAI-compatible chat API, and `Refit`
  by placement, the same way `Impl/Telegram/` already keeps `Telegram.Bot` out of
  `Impl/Services/`.
- `tests/Assistant.WireMock/Dockerfile` copies the whole `tests/Assistant.WireMock/` directory
  before publishing — confirmed by reading it — so `AiStubs.cs` needs no Dockerfile
  change, only the `--build` the Global Constraints already call for.
- `TelegramListenerTests.cs` already builds its own `ServiceCollection` and calls
  `services.AddAssistantServices()`, `services.AddAssistantTelegram(...)` and
  `services.AddAssistantListener()` directly, even though
  `Assistant.IntegrationTests.csproj` names only `Assistant.Worker` as a `ProjectReference` —
  MSBuild's default `ProjectReference` propagation makes `Assistant.Impl`'s public surface
  reachable transitively through `Assistant.Worker`. `AiClientTests` (Commit 1, below) uses the
  identical pattern for `AddAssistantServices`, `AddAssistantTime` and `AddAssistantAi`.
- **Unverified, flagged rather than assumed:** Refit 15's source generator is expected to accept
  an `internal` interface for `IAiApi`. **Step instruction, Commit 1, Step 6:** if
  the build fails on the generated client, make `IAiApi` `public` instead. It stays
  inside `Assistant.Impl`, which `Assistant.Worker` already references, so nothing leaks past the
  composition root. Record in the report which of the two happened.

---

## Inherited context: what this slice reads from slices 1 and 2

`AiSettings` (slice 1, merged `987ad21`) ships four validated properties this slice's client
reads directly: `ApiKey`, `BaseUrl`, `Model`, `MaxTokens`. `AddAssistantAi` (also slice 1)
already registers `AiSettings` as a singleton and is wired into `Program.cs`'s composition
chain, after `AddAssistantTime`. This slice extends that method's body and touches `Program.cs`
not at all, matching slice 1's own claim that `Program.cs` would not be touched again after it.

`SystemPrompt` (slice 2, merged `3b136fe`) is `internal sealed class SystemPrompt(ILocalTimeResolver clock)`
in `Assistant.Impl.Ai`, with one public member: `string Build()`. It has had no caller since it
shipped — this slice gives it its first one. `AiClient` calls `prompt.Build()` once per request
and sends the result as the conversation's first message; it never reads `ILocalTimeResolver`
directly. Slice 2's own inherited-context section already flagged that this is where the built
string finally gets wrapped with a role (`{"role": "system", "content": ...}`), because the
OpenAI-compatible chat API carries the system prompt inside the same message array as everything
else, tagged by role, rather than in a separate top-level field the way Anthropic's Messages API
shapes it.

---

## Decisions this slice makes

Numbered 1–4 here. The plan these four PRs were split from numbered its full, four-slice
decision set A–O; these four carried letters A, B, E and K there. Renumbered for this standalone
document, since it carries only these four.

### 1. OpenRouter, not Anthropic — and nothing is named after a vendor

Slice 1's own inherited-context section already flagged this ruling ("every type F9a introduces
is named for the wire format, not the vendor ... arriving in slice 3, not this one") without
applying it — there was no client yet to name. This slice is where it is applied: `IAiApi`,
`AiClient`, and `tests/Assistant.WireMock/AiStubs.cs` are all named for the OpenAI-compatible
chat API, which OpenRouter, OpenAI, Groq and a local Ollama all serve, not for OpenRouter
specifically. Moving providers becomes a change to `AiSettings.BaseUrl` and `AiSettings.Model`
and nothing else.

The naming scheme carries through the transport abstraction as well as the wire adapter: a
generic `IAiClient` interface, implemented by the equally generic `AiClient`. Neither name
mentions a provider, because switching provider is a change to `AiSettings.BaseUrl`, not a new
class. This is unlike `INotifier` and its implementation `TelegramNotifier`, where the
implementation genuinely is Telegram-specific: it is built on the `Telegram.Bot` SDK, and a
second notification channel would be a second class, not a settings change. The `Ai` prefix
matches `AiSettings`, the vocabulary slice 1 already established, rather than inventing a second
name for the same concept.

`docs/design/slice-1-reminders.md` §5.5 and §3.6 name `IAnthropicApi`/`IOpenRouterApi` and list
three vendor-specific client implementations, one of them a fallback decorator, against the
transport interface. That naming is superseded by this decision. A later slice corrects the spec
document itself; this one does not touch it (see "What this slice does NOT include," below).

### 2. The OpenAI-compatible chat API carries the system prompt inside `messages[0]`

Slice 2's own inherited-context section already described this shape and said it "governs
slices 2 and 3" — it is why `SystemPrompt.Build()` returns a plain `string` with no notion of
role baked in. This slice is where that string is finally wrapped: `AiMessage("system",
prompt.Build())` as the conversation's first entry, `AiMessage("user", userText)` as its second —
rather than a separate top-level `system` field the way Anthropic's Messages API shapes it. No
record property in this feature is named `System`, so the namespace-shadowing trap a `System`
property would set up next to `System.*` never arises.

One consequence for a later feature, recorded here because the wire types are being designed
now: at F9b, `tool_calls[].function.arguments` arrives as a JSON **string** in this format, not a
nested object. `Assistant.Contracts` will never need a `JsonElement` to represent it — a plain
`string` deserialises and later re-parses like any other. This slice's wire types carry no
`tool_calls` property at all; F9b adds it.

### 3. `IAiClient` returns `Result<string>` at this slice and `Result<ToolCall>` at F9b

That is a modification to an existing interface, not an extension by a new class — acceptable
here because `IAiClient` is a transport abstraction, not one of spec §3.6's behaviour seams.
Those seams grow by adding a class (`ITaskAction`'s implementations, `IScheduledJob`'s
implementations, per §3.6's own table); `IAiClient` has exactly one production implementation
today and will still have exactly one at F9b. `IAssistantTool` is the seam F9b actually extends.

Recorded honestly as a cost, not hidden: a caller of `IAiClient.AskAsync` written against
this slice's shape will not compile unchanged after F9b.

### 4. Integration tests do not pin the clock

The rejected monolith this plan was split from added `Microsoft.Extensions.TimeProvider.Testing`
to `Assistant.IntegrationTests.csproj` so `AiClientTests` could pin the clock with
`FakeTimeProvider`, on the reasoning that `AiClient` is the first `Impl` type whose
happy path reads wall-clock time on every call — applied as a precaution, by its own admission,
even while conceding that "this plan's integration tests make no assertion on the prompt's time
content at all."

That concession turns out to be the whole answer. Slice 2's own decision keeps every assertion
on the prompt's *content* in `SystemPromptTests.cs`, at the unit level, pinned with
`FakeTimeProvider` there — which means `AiClientTests` never reads the prompt's text at all.
Its two happy-path tests (`AskAsync_ProviderAnswers_ReturnsItsText`,
`AskAsync_AnyText_PlacesThePromptAndTheModelCorrectlyOnTheWire`) assert the model's answer,
the request's role placement, and the configured model and token limit — never the system
message's `Content` string — and Commit 2's two failure tests assert only an `ErrorCode`. No
test this slice writes reads `clock.CurrentLocalTime`, `ZoneId`, or anything derived from either.

`AddAssistantServices()`'s default `TimeProvider.System` is therefore enough, and
`Microsoft.Extensions.TimeProvider.Testing` is **not** added to
`tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj` in this slice — despite the
package's version already sitting in `Directory.Packages.props` for `Assistant.UnitTests`'s use.
Adding a `<PackageReference>` with no assertion in this document that needs it would be exactly
the kind of speculative dependency this repository's owner rules out on principle — the same
principle slice 1's own decision 5 already applied to a speculative `AiSettings` test file.

---

## What this slice does NOT include

- **`MessageHandler`'s change away from F7's echo.** Nothing calls `IAiClient` outside this
  slice's own tests. A later slice wires it in.
- **A fallback decorator, Polly, retry, circuit breaking, the per-minute call cap** (spec §5.5,
  §5.6). A later slice is expected to flag the question spec §5.5 now owes an answer to before a
  much later feature builds any of this, and does not answer it here.
- **The spec-document corrections.** Spec §5.5's `IAnthropicApi`/`IOpenRouterApi` naming and
  §3.6's client-implementations row both need updating to match decision 1, above. A later slice
  makes that change; this document does not touch `docs/design/slice-1-reminders.md`.
- **A dedicated unit test file for `AiClient` or the wire types.**
  `AiClient` is an adapter against a real wire format; spec §7.2 already assigns
  that ground to the integration level ("The integration level covers adapters against real wire
  formats, so there is no fake to drift") — the same reasoning that has kept `TelegramNotifier`
  out of `Assistant.UnitTests`.
- **`Microsoft.Extensions.TimeProvider.Testing` in `Assistant.IntegrationTests`.** Decision 4,
  above, explains why.
- **Persisting inbound messages, storing anything in `chat_messages`, the "typing…" indicator.**
  All deferred beyond F9a entirely (see the feature backlog).

---

## File Structure

```
src/Assistant.Contracts/
    ErrorCode.cs                           + ModelUnavailable, ModelReturnedNoAnswer   (Commit 2)

src/Assistant.Interfaces/
    IAiClient.cs                           new                                         (Commit 1)

src/Assistant.Impl/
    Ai/AiWire.cs                           new                                         (Commit 1)
    Ai/IAiApi.cs                           new                                         (Commit 1)
    Ai/AiClient.cs                         new, happy path (Commit 1); hardened (Commit 2)
    ImplServiceCollectionExtensions.cs     AddAssistantAi extended with the client      (Commit 1)
    Assistant.Impl.csproj                  + Refit, Refit.HttpClientFactory            (Commit 1)

Directory.Packages.props                   + Refit, Refit.HttpClientFactory            (Commit 1)

tests/Assistant.WireMock/
    AiStubs.cs                             new                                         (Commit 1)
    Program.cs                             + AiStubs.Install(server)                   (Commit 1)

tests/Assistant.IntegrationTests/
    Infrastructure/WireMockFixture.cs      + AI-answer seeding and read-back            (Commit 1)
    Ai/AiClientTests.cs                    new, happy path (Commit 1); + 2 tests (Commit 2)
```

`tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj` is deliberately absent from
this list — decision 4, above.

---

## Validation

`dotnet test` against the WireMock stub, after both commits:

```bash
docker compose -f compose.test.yaml up -d --build
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~AiClientTests"
docker compose -f compose.test.yaml down
```

`--build` is required: this slice changes the WireMock stub image, and while
`tests/Assistant.WireMock/Dockerfile` needs no edit of its own to pick up the new
`AiStubs.cs` (verified fact, above), the image still has to be rebuilt to contain it.

This slice cannot be validated by running the app: nothing calls `IAiClient` until a later
slice wires it into `MessageHandler`. The owner has explicitly accepted this — this slice's job
is proving the client is correct against a stub, not proving the whole pipeline works.

**Test count arithmetic.** The unit suite stays at **41**, unchanged from slice 2 — this slice
adds no unit test file (see "What this slice does NOT include," above).

The integration suite stands at **28** today, counted directly from the test files: 4
(`TaskRepositoryTests`) + 9 (`DueReminderQueryTests`: a 2-case `[Theory]` + 1 `[Fact]` + a
2-case `[Theory]` + 4 `[Fact]`) + 1 (`ReminderTaskSchemaTests`) + 4 (`TelegramNotifierTests`: a
2-case `[Theory]` + 2 `[Fact]`) + 3 (`TelegramListenerTests`) + 4 (`DueReminderJobTests`) + 3
(`TaskServiceTests`) = 28. Commit 1 adds 2 (`AskAsync_ProviderAnswers_ReturnsItsText`,
`AskAsync_AnyText_PlacesThePromptAndTheModelCorrectlyOnTheWire`) — 30. Commit 2 adds 2 more
(`AskAsync_ProviderReturnsAServerError_IsRefusedAsUnavailable`,
`AskAsync_ProviderReturnsNoChoices_IsRefusedAsNoAnswer`) — **32** expected total after this
slice.

---

## Steps

**Decisions this slice carries:** 1–4, given in full above.

**Consumes:** `AiSettings` and the minimal `AddAssistantAi` (slice 1), `SystemPrompt` (slice 2).
**Produces:** `IAiClient`, `AiClient`, the extended `AddAssistantAi`,
`ErrorCode.ModelUnavailable`, `ErrorCode.ModelReturnedNoAnswer`.

`AiSettings` already has a consumer, from slice 1 — this slice **extends** `AddAssistantAi`'s
existing body, it does not create the method. `SystemPrompt` gets its first real caller here
(`AiClient`). The wire types, the Refit interface, `IAiClient`, and the
WireMock/integration-test infrastructure all ship in this one document too — but as **two
commits**, not one; **do not merge them into one commit.**

**Why two commits:** Commit 1 ships a happy-path-only `AiClient` with **no
`ILogger` parameter** (unreferenced, it would trip CS9113) and **no reference to `ErrorCode`** —
a provider failure or an empty `choices` array is left to crash, same as any unhandled
exception. That is deliberate: this repository's own "never reference a symbol ahead of the step
that needs it" rule, applied across commits instead of across steps. Commit 2 appends
`ModelUnavailable` and `ModelReturnedNoAnswer` to `ErrorCode`, hardens the client, and its own
tests fail first on a genuine `Refit.ApiException` (500) and an `ArgumentOutOfRangeException`
(empty `choices`) — proof the crash Commit 1 left in place was real, not merely asserted. F8's
own Task 1/Task 2 split used the identical shape: Task 1 deliberately left a bug for Task 2 to
fix.

### Commit 1: the happy path

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Assistant.Impl/Assistant.Impl.csproj`
- Create: `tests/Assistant.WireMock/AiStubs.cs`
- Modify: `tests/Assistant.WireMock/Program.cs`
- Modify: `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`
- Create: `tests/Assistant.IntegrationTests/Ai/AiClientTests.cs`
- Create: `src/Assistant.Impl/Ai/AiWire.cs`
- Create: `src/Assistant.Impl/Ai/IAiApi.cs`
- Create: `src/Assistant.Interfaces/IAiClient.cs`
- Create: `src/Assistant.Impl/Ai/AiClient.cs` (happy path only)
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`

- [ ] **Step 1: Add the two packages**

`Directory.Packages.props` — insert alphabetically, between `Npgsql.EntityFrameworkCore.PostgreSQL`
and `Respawn`:

```xml
    <PackageVersion Include="Refit" Version="15.2.0" />
    <PackageVersion Include="Refit.HttpClientFactory" Version="15.2.0" />
```

`src/Assistant.Impl/Assistant.Impl.csproj` — insert alphabetically, between
`Microsoft.Extensions.Logging.Abstractions` and `System.Configuration.ConfigurationManager`:

```xml
    <PackageReference Include="Refit" />
    <PackageReference Include="Refit.HttpClientFactory" />
```

```bash
dotnet restore
```

Expected: restores clean. Nothing references the new packages yet, so nothing else changes.

- [ ] **Step 2: Add the AI-answer stub to the WireMock image**

Create `tests/Assistant.WireMock/AiStubs.cs`:

```csharp
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Assistant.WireMock;

/// <summary>
/// The OpenAI-compatible chat endpoint this stub answers.
/// </summary>
/// <remarks>
/// The path is <c>/chat/completions</c> with no prefix: tests point <c>AiSettings.BaseUrl</c> at
/// this fixture's own address directly, while production points it at
/// <c>https://openrouter.ai/api/v1</c>, which already carries the version segment. The default
/// mapping answers at weak priority (100) so a locally-run worker never logs "No matching mapping
/// found"; tests install a stronger-priority mapping of their own.
/// </remarks>
internal static class AiStubs
{
    private const string DefaultAnswerResponse = """
        {"choices":[{"message":{"role":"assistant","content":"Stubbed answer."}}]}
        """;

    /// <summary>
    /// Installs the chat-endpoint mapping on the given server.
    /// </summary>
    /// <param name="server">The running stub server.</param>
    public static void Install(WireMockServer server)
    {
        server
            .Given(Request.Create().WithPath("/chat/completions").UsingPost())
            .AtPriority(100)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(DefaultAnswerResponse));
    }
}
```

Modify `tests/Assistant.WireMock/Program.cs` — add one line after the existing
`TelegramStubs.Install(server);` call:

```csharp
TelegramStubs.Install(server);
AiStubs.Install(server);
```

- [ ] **Step 3: Extend `WireMockFixture` with AI-answer seeding and read-back**

`PutMappingAsync` is generalised to take the path and status code, so both the existing Telegram
seeder and the new AI-answer seeders share it — the Telegram-specific `{"ok":true,"result":...}`
envelope moves to `SeedUpdatesAsync`'s own call site, since a response from the chat API carries
no such envelope. Replace the full contents of
`tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs` with:

```csharp
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    private static readonly Guid PendingUpdatesMapping =
        new("f7000000-0000-0000-0000-000000000001");

    private static readonly Guid DrainedUpdatesMapping =
        new("f7000000-0000-0000-0000-000000000002");

    private static readonly Guid AiMapping =
        new("f9a00000-0000-0000-0000-000000000001");

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
    /// <returns>A task that completes once the request log is empty and any seeded mapping is gone.</returns>
    public async Task ResetAsync()
    {
        foreach (var id in new[] { PendingUpdatesMapping, DrainedUpdatesMapping, AiMapping })
        {
            (await _http.DeleteAsync($"{Url}/__admin/mappings/{id}")).Dispose();
        }

        (await _http.DeleteAsync($"{Url}/__admin/requests")).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Returns the send-message requests the stub received, in order.
    /// </summary>
    /// <returns>One payload per captured request.</returns>
    public async Task<IReadOnlyList<SendMessagePayload>> SentMessagesAsync()
    {
        var entries = await _http.GetFromJsonAsync<List<AdminLogEntry>>($"{Url}/__admin/requests")
                      ?? [];

        return entries
            .Where(entry => entry.Request.Path.EndsWith("/sendMessage", StringComparison.Ordinal)
                            && entry.Request.Method == "POST")
            .Select(entry => JsonSerializer.Deserialize<SendMessagePayload>(entry.Request.Body)!)
            .ToList();
    }

    /// <summary>
    /// Returns the chat-endpoint requests the stub received, in order.
    /// </summary>
    /// <returns>One payload per captured request.</returns>
    public async Task<IReadOnlyList<AiRequestPayload>> AiRequestsAsync()
    {
        var entries = await _http.GetFromJsonAsync<List<AdminLogEntry>>($"{Url}/__admin/requests")
                      ?? [];

        return entries
            .Where(entry => entry.Request.Path.EndsWith("/chat/completions", StringComparison.Ordinal)
                            && entry.Request.Method == "POST")
            .Select(entry => JsonSerializer.Deserialize<AiRequestPayload>(entry.Request.Body)!)
            .ToList();
    }

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

        await PutMappingAsync(PendingUpdatesMapping, "/bot*/getUpdates", priority: 10,
            bodyPattern: null, statusCode: 200,
            responseBody: new JsonObject { ["ok"] = true, ["result"] = pending }, delayMs: null);

        await PutMappingAsync(DrainedUpdatesMapping, "/bot*/getUpdates", priority: 1,
            bodyPattern: $"*\"offset\":{nextOffset}*", statusCode: 200,
            responseBody: new JsonObject { ["ok"] = true, ["result"] = new JsonArray() },
            delayMs: 1000);
    }

    /// <summary>
    /// Makes the stub answer the next chat request with the given answer text.
    /// </summary>
    /// <param name="answer">The model's answer.</param>
    /// <returns>A task that completes once the mapping is installed.</returns>
    public Task SeedAiAnswerAsync(string answer) =>
        PutMappingAsync(AiMapping, "/chat/completions", priority: 1,
            bodyPattern: null, statusCode: 200,
            responseBody: new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = answer },
                }),
            },
            delayMs: null);

    /// <summary>
    /// Makes the stub answer the next chat request with a server error.
    /// </summary>
    /// <returns>A task that completes once the mapping is installed.</returns>
    public Task SeedAiFailureAsync() =>
        PutMappingAsync(AiMapping, "/chat/completions", priority: 1,
            bodyPattern: null, statusCode: 500,
            responseBody: new JsonObject { ["error"] = "stubbed provider failure" },
            delayMs: null);

    /// <summary>
    /// Makes the stub answer the next chat request with no candidate answers.
    /// </summary>
    /// <returns>A task that completes once the mapping is installed.</returns>
    public Task SeedAiNoAnswerAsync() =>
        PutMappingAsync(AiMapping, "/chat/completions", priority: 1,
            bodyPattern: null, statusCode: 200,
            responseBody: new JsonObject { ["choices"] = new JsonArray() },
            delayMs: null);

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

    /// <summary>
    /// Releases the HTTP client.
    /// </summary>
    /// <returns>A completed task.</returns>
    public Task DisposeAsync()
    {
        _http.Dispose();
        return Task.CompletedTask;
    }

    private async Task PutMappingAsync(
        Guid id, string path, int priority, string? bodyPattern, int statusCode,
        JsonObject responseBody, int? delayMs)
    {
        var request = new JsonObject
        {
            ["Path"] = new JsonObject
            {
                ["Matchers"] = new JsonArray(new JsonObject
                {
                    ["Name"] = "WildcardMatcher",
                    ["Pattern"] = path,
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
            ["StatusCode"] = statusCode,
            ["Headers"] = new JsonObject { ["Content-Type"] = "application/json" },
            ["Body"] = responseBody.ToJsonString(),
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

    private sealed record AdminLogEntry(
        [property: JsonPropertyName("Request")] AdminRequest Request);

    private sealed record AdminRequest(
        [property: JsonPropertyName("Path")] string Path,
        [property: JsonPropertyName("Method")] string Method,
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
    [property: JsonPropertyName("parse_mode")] string ParseMode)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>
    /// Null when the request carried exactly the three expected fields. Populated otherwise, which
    /// makes <c>Assert.Equivalent(strict: true)</c> fail — without this, extra fields are silently
    /// discarded during deserialisation and the assertion cannot see them.
    /// </value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// An inbound Telegram update, as <see cref="WireMockFixture.SeedUpdatesAsync"/> serves it.
/// </summary>
/// <param name="UpdateId">Telegram's identifier for the update.</param>
/// <param name="ChatId">The chat the message appears to come from.</param>
/// <param name="Text">The message body.</param>
public sealed record InboundUpdate(int UpdateId, long ChatId, string Text);

/// <summary>
/// The body of a chat request, as the assistant sends it.
/// </summary>
/// <param name="Model">The requested model slug.</param>
/// <param name="Messages">The conversation sent, system prompt first.</param>
/// <param name="MaxTokens">The token limit sent with the request.</param>
public sealed record AiRequestPayload(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<AiMessagePayload> Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>Null when the request carried exactly the three expected fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// One message within a captured chat request.
/// </summary>
/// <param name="Role">Who is speaking: <c>system</c> or <c>user</c>.</param>
/// <param name="Content">What was said.</param>
public sealed record AiMessagePayload(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content)
{
    /// <summary>
    /// Any field on the wire that this message does not name.
    /// </summary>
    /// <value>Null when the message carried exactly the two expected fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
```

- [ ] **Step 4: Write the failing happy-path tests**

Create `tests/Assistant.IntegrationTests/Ai/AiClientTests.cs`:

```csharp
using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.IntegrationTests.Ai;

/// <summary>
/// Test class for <see cref="IAiClient"/>.
/// </summary>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class AiClientTests(WireMockFixture wireMock) : IAsyncLifetime
{
    private const string Model = "test-model";
    private const int MaxTokens = 100;

    private ServiceProvider _provider = null!;

    private IAiClient _sut = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddAssistantServices();
        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
        services.AddAssistantAi(new AiSettings
        {
            ApiKey = "test-key", BaseUrl = wireMock.Url, Model = Model, MaxTokens = MaxTokens,
        });
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<IAiClient>();

        await wireMock.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When the provider answers with a candidate message
    /// And the model is asked
    /// Then its text comes back as the result's value.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderAnswers_ReturnsItsText()
    {
        // Arrange
        await wireMock.SeedAiAnswerAsync("Noted -- I will remind you.");

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Noted -- I will remind you.", result.Value);
    }

    /// <summary>
    /// When the model is asked
    /// Then the system prompt is sent as the first message with role system
    /// And the owner's text is sent as the second message with role user
    /// And the configured model and token limit go on the wire.
    /// </summary>
    [Fact]
    public async Task AskAsync_AnyText_PlacesThePromptAndTheModelCorrectlyOnTheWire()
    {
        // Arrange
        await wireMock.SeedAiAnswerAsync("Noted.");

        // Act
        await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        var request = Assert.Single(await wireMock.AiRequestsAsync());
        Assert.Equal(Model, request.Model);
        Assert.Equal(MaxTokens, request.MaxTokens);
        Assert.Equal(2, request.Messages.Count);
        Assert.Equal("system", request.Messages[0].Role);
        Assert.Equal("user", request.Messages[1].Role);
        Assert.Equal("call the bank tomorrow at 10", request.Messages[1].Content);
    }
}
```

This test asserts placement and wire values only, never the prompt's text — that stays owned by
`SystemPromptTests.cs` (decision 4, above). No clock is pinned here: nothing in this file reads
`clock.CurrentLocalTime` or `ZoneId`, so the container is built with `AddAssistantServices()`'s
default `TimeProvider.System`, unmodified.

- [ ] **Step 5: Bring up the stub with the new image, and watch the tests fail**

```bash
docker compose -f compose.test.yaml up -d --build
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~AiClientTests"
```

Expected: does not compile. `Assistant.Interfaces.IAiClient` does not exist yet.

- [ ] **Step 6: Write the wire types, the Refit interface, `IAiClient` and its happy-path client**

Create `src/Assistant.Impl/Ai/AiWire.cs`:

```csharp
namespace Assistant.Impl.Ai;

/// <summary>
/// A request to the OpenAI-compatible chat API, which OpenRouter, OpenAI, Groq and a local
/// Ollama all serve.
/// </summary>
/// <param name="Model">The model slug to request, such as <c>anthropic/claude-sonnet-5</c>.</param>
/// <param name="Messages">The conversation so far, system prompt first.</param>
/// <param name="MaxTokens">The maximum number of tokens the model may return.</param>
internal sealed record AiRequest(
    string Model, IReadOnlyList<AiMessage> Messages, int MaxTokens);

/// <summary>
/// One turn in the chat API's conversation, on either side of the wire.
/// </summary>
/// <param name="Role">
/// Who is speaking: <c>system</c>, <c>user</c>, or <c>assistant</c>.
/// </param>
/// <param name="Content">
/// What was said, or <see langword="null"/> on a response that carries only tool calls (F9b) —
/// harmless now, since F9a never sends or reads a null one.
/// </param>
internal sealed record AiMessage(string Role, string? Content);

/// <summary>
/// A response from the chat API, carrying every answer the model offered.
/// </summary>
/// <param name="Choices">
/// The model's candidate answers. Empty when the provider accepted the request but produced
/// nothing.
/// </param>
internal sealed record AiResponse(IReadOnlyList<AiChoice> Choices);

/// <summary>
/// One candidate answer within a response from the chat API.
/// </summary>
/// <param name="Message">The answer itself, in the same shape a request message takes.</param>
internal sealed record AiChoice(AiMessage Message);
```

One file rather than four, because these four types are one wire contract and meaningless apart —
`Assistant.Contracts/Result.cs` is this repository's own precedent for two related types sharing a
file, and this is the same reasoning stretched to four.

Create `src/Assistant.Impl/Ai/IAiApi.cs`:

```csharp
using Refit;

namespace Assistant.Impl.Ai;

/// <summary>
/// The OpenAI-compatible chat endpoint, reachable at any provider that speaks it.
/// </summary>
/// <remarks>
/// Named for the wire format, not a vendor: OpenRouter, OpenAI, Groq and a local Ollama all
/// serve this same shape, so moving providers is a change to
/// <see cref="Assistant.Impl.Settings.AiSettings.BaseUrl"/> and nothing in this interface.
/// </remarks>
internal interface IAiApi
{
    /// <summary>
    /// Asks the chat endpoint for its response to the given request.
    /// </summary>
    /// <param name="request">The model, conversation and token limit to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The provider's response, including every candidate answer it offered.</returns>
    [Post("/chat/completions")]
    Task<AiResponse> AskAsync([Body] AiRequest request, CancellationToken ct);
}
```

Create `src/Assistant.Interfaces/IAiClient.cs`:

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// Reaches a chat model with the owner's text and returns its answer.
/// </summary>
/// <remarks>
/// A transport abstraction, not one of spec §3.6's behaviour seams: this interface changes
/// shape at F9b, when <c>AskAsync</c> starts returning <c>Result&lt;ToolCall&gt;</c> so a
/// tool invocation can be parsed out of the answer. F9b's growing seam is
/// <c>IAssistantTool</c>, not this one.
/// </remarks>
public interface IAiClient
{
    /// <summary>
    /// Sends the owner's text to the configured model and returns its answer.
    /// </summary>
    /// <param name="userText">What the owner said.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The model's answer, or the reason it could not be reached.
    /// </returns>
    Task<Result<string>> AskAsync(string userText, CancellationToken ct);
}
```

Create `src/Assistant.Impl/Ai/AiClient.cs` — happy path only, per this commit's own scope,
described above: no `ILogger`, no `ErrorCode`.

```csharp
using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;

namespace Assistant.Impl.Ai;

/// <summary>
/// Reaches the configured chat endpoint with the owner's text and the system prompt, and
/// returns the model's answer.
/// </summary>
/// <param name="api">The Refit client for the chat endpoint.</param>
/// <param name="prompt">Builds the system prompt sent as the first message.</param>
/// <param name="settings">The configured model and token limit.</param>
internal sealed class AiClient(
    IAiApi api, SystemPrompt prompt, AiSettings settings) : IAiClient
{
    /// <inheritdoc/>
    public async Task<Result<string>> AskAsync(string userText, CancellationToken ct)
    {
        var response = await api.AskAsync(
            new AiRequest(
                settings.Model,
                [new AiMessage("system", prompt.Build()),
                 new AiMessage("user", userText)],
                settings.MaxTokens),
            ct);

        return Result<string>.Success(response.Choices[0].Message.Content!);
    }
}
```

**Extend `AddAssistantAi` in `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` — do not
create it, it already exists from slice 1.** Add these usings at the top of the file, alongside
the existing ones:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Impl.Ai;
using Refit;
```

Before, as slice 1 shipped it — settings only:

```csharp
    public static IServiceCollection AddAssistantAi(
        this IServiceCollection services, AiSettings settings)
    {
        services.AddSingleton(settings);
        return services;
    }
```

After — replace the `<summary>` and `<remarks>`, and append to the body. The
`services.AddSingleton(settings);` line and the method's signature do not change:

```csharp
    /// <summary>
    /// Registers the AI client the assistant reaches for an answer.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <param name="settings">
    /// Validated chat-model configuration. Read it with <c>IConfiguration.Read</c> so a missing
    /// key or an unusable base address stops the host here, while it is composing, rather than at
    /// the first message the owner sends.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddAssistantTime</c> for the <see cref="ILocalTimeResolver"/> the system
    /// prompt reads the current time from — the reason this method sits after
    /// <c>AddAssistantTime</c> in <c>Program.cs</c>'s chain.
    /// </remarks>
    public static IServiceCollection AddAssistantAi(
        this IServiceCollection services, AiSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<SystemPrompt>();
        services.AddRefitClient<IAiApi>(new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }),
        })
        .ConfigureHttpClient(http =>
        {
            http.BaseAddress = new Uri(settings.BaseUrl);
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });
        services.AddScoped<IAiClient, AiClient>();
        return services;
    }
```

`Program.cs` needs no change: slice 1 already threads `builder.Configuration.Read<AiSettings>()`
into `.AddAssistantAi(...)` in the right chain position, and that call's shape does not depend on
what the method's body does.

- [ ] **Step 7: Run them and watch them pass**

```bash
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~AiClientTests"
```

Expected: 2 passed. If the source generator rejects `internal interface IAiApi`,
apply the fallback from "Verified facts" above — make it `public` — and record which path was
taken.

- [ ] **Step 8: Run the whole suite**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
```

Expected: zero warnings; unit tests unchanged at 41; every previously green integration test
still green (28), plus these 2 (`AiClientTests`) — 30 total. Commit 2, below, adds the
remaining 2.

- [ ] **Step 9: Commit 1**

```bash
git add Directory.Packages.props src/Assistant.Impl/Assistant.Impl.csproj \
        src/Assistant.Impl/Ai/AiWire.cs src/Assistant.Impl/Ai/IAiApi.cs \
        src/Assistant.Interfaces/IAiClient.cs src/Assistant.Impl/Ai/AiClient.cs \
        src/Assistant.Impl/ImplServiceCollectionExtensions.cs \
        tests/Assistant.WireMock/AiStubs.cs tests/Assistant.WireMock/Program.cs \
        tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs \
        tests/Assistant.IntegrationTests/Ai/AiClientTests.cs
git commit
```

Message:

```
feat: reach a chat endpoint and get an answer back

IAiClient, AiClient and IAiApi speak the OpenAI-compatible chat API that
OpenRouter, OpenAI, Groq and a local Ollama all serve, so a provider change
is AiSettings.BaseUrl and AiSettings.Model and nothing else. This ships the
happy path only: a provider failure still propagates as an exception, and
AiClient takes no ILogger yet. The next commit turns a provider failure into
an answer instead of a crash.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

### Commit 2: a provider failure becomes an answer, not a crash

**Files:**
- Modify: `src/Assistant.Contracts/ErrorCode.cs`
- Modify: `src/Assistant.Impl/Ai/AiClient.cs`
- Modify: `tests/Assistant.IntegrationTests/Ai/AiClientTests.cs`

**Consumes:** `AiClient`'s happy path (Commit 1). **Produces:**
`ErrorCode.ModelUnavailable`, `ErrorCode.ModelReturnedNoAnswer`, the hardened
`AiClient`.

- [ ] **Step 1: Write the failing tests**

Add `services.AddLogging();` as the first line of `AiClientTests.InitializeAsync` — needed once
`AiClient` takes an `ILogger<AiClient>` in this commit's Step 3:

```csharp
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantServices();
        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
        services.AddAssistantAi(new AiSettings
        {
            ApiKey = "test-key", BaseUrl = wireMock.Url, Model = Model, MaxTokens = MaxTokens,
        });
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<IAiClient>();

        await wireMock.ResetAsync();
    }
```

Append to `AiClientTests`:

```csharp
    /// <summary>
    /// When the provider answers with a server error
    /// And the model is asked
    /// Then the call is refused as unavailable, not thrown.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderReturnsAServerError_IsRefusedAsUnavailable()
    {
        // Arrange
        await wireMock.SeedAiFailureAsync();

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ModelUnavailable, result.Error);
    }

    /// <summary>
    /// When the provider answers with no candidate messages
    /// And the model is asked
    /// Then the call is refused as having returned nothing.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderReturnsNoChoices_IsRefusedAsNoAnswer()
    {
        // Arrange
        await wireMock.SeedAiNoAnswerAsync();

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ModelReturnedNoAnswer, result.Error);
    }
```

Add `using Assistant.Contracts;` to the file's usings for `ErrorCode`.

- [ ] **Step 2: Run them and watch them fail for the right reason**

```bash
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~AiClientTests"
```

Expected: both new tests fail with an **unhandled exception**, not a failed assertion —
`AskAsync_ProviderReturnsAServerError_IsRefusedAsUnavailable` surfaces a `Refit.ApiException`
for the 500; `AskAsync_ProviderReturnsNoChoices_IsRefusedAsNoAnswer` surfaces an
`ArgumentOutOfRangeException` off `response.Choices[0]` on an empty array. Both fail for the
title's own reason: a provider failure currently crashes. The two happy-path tests from Commit 1
still pass unchanged.

- [ ] **Step 3: Append the two error codes**

At the **end** of the `ErrorCode` enum in `src/Assistant.Contracts/ErrorCode.cs`, after
`DueTimeTooFarAhead`:

```csharp
    /// <summary>
    /// The chat model could not be reached, or it responded with an error.
    /// </summary>
    ModelUnavailable,

    /// <summary>
    /// The chat model was reached but returned no usable answer.
    /// </summary>
    ModelReturnedNoAnswer,
```

- [ ] **Step 4: Harden `AiClient`**

Replace the full contents of `src/Assistant.Impl/Ai/AiClient.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;

namespace Assistant.Impl.Ai;

/// <summary>
/// Reaches the configured chat endpoint with the owner's text and the system prompt, and
/// returns the model's answer.
/// </summary>
/// <param name="api">The Refit client for the chat endpoint.</param>
/// <param name="prompt">Builds the system prompt sent as the first message.</param>
/// <param name="settings">The configured model and token limit.</param>
/// <param name="logger">Where a provider failure is recorded.</param>
internal sealed class AiClient(
    IAiApi api, SystemPrompt prompt, AiSettings settings,
    ILogger<AiClient> logger) : IAiClient
{
    /// <inheritdoc/>
    public async Task<Result<string>> AskAsync(string userText, CancellationToken ct)
    {
        AiResponse response;
        try
        {
            response = await api.AskAsync(
                new AiRequest(
                    settings.Model,
                    [new AiMessage("system", prompt.Build()),
                     new AiMessage("user", userText)],
                    settings.MaxTokens),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reaching the chat model failed.");
            return Result<string>.Failure(ErrorCode.ModelUnavailable);
        }

        var answer = response.Choices.FirstOrDefault()?.Message.Content;

        if (string.IsNullOrWhiteSpace(answer))
        {
            logger.LogError("The chat model returned no answer.");
            return Result<string>.Failure(ErrorCode.ModelReturnedNoAnswer);
        }

        return Result<string>.Success(answer);
    }
}
```

`AddAssistantAi` needs no change: `services.AddScoped<IAiClient, AiClient>()` already resolves
whatever constructor `AiClient` currently has, and `ILogger<AiClient>` resolves from the
`AddLogging()` this commit's Step 1 added to the test.

- [ ] **Step 5: Run them and watch them pass**

```bash
dotnet test tests/Assistant.IntegrationTests --filter "FullyQualifiedName~AiClientTests"
```

Expected: 4 passed.

- [ ] **Step 6: Run the whole suite**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
```

Expected: zero warnings; unit tests unchanged at 41 (`ConventionTests` inspects `ErrorCode` by
reflection and needs no change of its own, the same way F8's own two `ErrorCode` additions
needed none); integration tests all green, 32 total (see "Test count arithmetic," above).

- [ ] **Step 7: Commit 2**

```bash
git add src/Assistant.Contracts/ErrorCode.cs src/Assistant.Impl/Ai/AiClient.cs \
        tests/Assistant.IntegrationTests/Ai/AiClientTests.cs
git commit
```

Message:

```
feat: turn a provider failure into an answer, not a crash

AiClient now catches a failed request and an empty answer and returns
Result<string>.Failure with a new ErrorCode instead of letting the
exception (Refit.ApiException, or an index out of range on an empty choices
array) propagate. Both new codes are appended -- no existing member's value
moved.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01EKjh2GD3CzkKt9aPxibkVF
```

---

## Self-review

- [ ] `dotnet build --no-restore` — zero warnings, zero errors
- [ ] `dotnet test tests/Assistant.UnitTests` — 41 passed, unchanged (no unit test file added
      this slice)
- [ ] `dotnet test tests/Assistant.IntegrationTests` — 32 passed (28 baseline + 4 `AiClientTests`)
- [ ] Commit 1 and Commit 2 are separate commits, in that order, never squashed
- [ ] Commit 1's `AiClient` takes no `ILogger` parameter and references no `ErrorCode` member
- [ ] Commit 2's two new tests were watched failing for a genuine `Refit.ApiException` (500) and
      a genuine `ArgumentOutOfRangeException` (empty `choices`), not a failed assertion, before
      that commit's Step 3 ran
- [ ] `ErrorCode.ModelUnavailable` and `ErrorCode.ModelReturnedNoAnswer` are appended at the end
      of the enum; no existing member's implicit value moved
- [ ] `AddAssistantAi`'s signature and its `services.AddSingleton(settings);` line are unchanged
      from slice 1; only the body grows
- [ ] `tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj` is untouched —
      decision 4
- [ ] No `FakeTimeProvider`, no `TimeProvider` override, anywhere in `AiClientTests`
- [ ] `IAiApi` compiled as `internal`; if not, it was made `public` and that is recorded in the
      report (see "Verified facts," above)
- [ ] Every new public member carries a three-line `<summary>`; every new internal member does
      too
- [ ] No emoji in any changed file, including both commit messages
- [ ] Both diffs comfortably under the 1000-line PR budget combined (~260 lines estimated by the
      rejected monolith this plan was split from)
