# Tech debt

This file is a register of things left in a known-imperfect shape on purpose. Each entry
records the reasoning behind leaving it that way, and a concrete trigger for revisiting the
decision.

It complements, and does not duplicate, §12.7 of `docs/design/slice-1-reminders.md`. That
section defers **product** decisions — a per-user timezone, message localisation — and its
triggers are user-facing: "a second user," a trip that makes a hardcoded zone personally
annoying. This file is for **code shape**: internal duplication, awkward signatures, structural
compromises with no user-visible consequence, kept because the cost of fixing them now is
higher than the cost of living with them a while longer.

An entry here is not a licence to fix the thing opportunistically inside an unrelated feature
branch. Each entry states its own scope, and that scope is the boundary the fix stays inside,
not a suggestion.

## `Result` and `Result<T>` are two types

**What the duplication is.** `Result` and `Result<T>`, both declared in
`src/Assistant.Contracts/Result.cs`, are separate `readonly record struct` types. Both carry
`IsSuccess => Error is null`, and both carry a `Failure(ErrorCode)` factory of the same shape —
`Result.Failure` returns `new(error)`, `Result<T>.Failure` returns `new(default, error)`. That
duplication is roughly six lines. `Success()` and `Success(T value)` genuinely differ in what
they take and are not part of it.

**How much surface it has today.** `Result` has exactly one use in `src/`:
`ITaskService.MarkReminderSentAsync(Guid id, CancellationToken ct)` returns `Task<Result>`, and
`TaskService.MarkReminderSentAsync` is its only implementation, called once per due task from
`DueReminderJob.ExecuteAsync`. `Result<T>` also has exactly one use:
`ILocalTimeResolver.Resolve(DateTime local)` returns `Result<DateTimeOffset>`, and
`LocalTimeResolver.Resolve` is its only implementation. Two interface methods, two
implementations, one type argument instantiated — that is the whole surface today.

**Why both stay, route by route.**

1. *One generic type, `Result<Unit>`, dropping the non-generic.* Every operation that succeeds
   without producing a value gets a meaningless type argument: `MarkReminderSentAsync` would
   return `Task<Result<Unit>>`, and its call site would read `Result<Unit>.Success(Unit.Value)`
   instead of `Result.Success()`. It also requires adding a `Unit` type to `Contracts`, which
   does not exist there today. Six lines of duplication would be traded for noise at every
   void-result signature, permanently, and this project expects more such operations than
   value-returning ones.

2. *Inheritance, `Result<T> : Result`.* Record structs cannot inherit from another struct type
   in C#, so this route would force both to become classes. That allocates on every call —
   `MarkReminderSentAsync` is invoked once per due task by the scheduler — and gives up the
   value equality a `record struct` provides for free. A larger change than the thing it fixes.

3. *A shared interface carrying a default `IsSuccess`.* The cleanest route in principle, and
   blocked by the project's own architecture test:
   `ConventionTests.Contracts_declares_no_interfaces`, in
   `tests/Assistant.UnitTests/Architecture/ConventionTests.cs`, fails the build the moment
   `Contracts` declares a public interface. Placing the interface in `Assistant.Interfaces`
   instead does not avoid the problem: `Interfaces` already references `Contracts` (see
   `src/Assistant.Interfaces/Assistant.Interfaces.csproj`), so `Result` and `Result<T>`
   implementing an interface declared in `Interfaces` would need the reference in the other
   direction too — a cycle. And every `IsSuccess` read through an interface reference boxes the
   struct.

**The trigger for revisiting.** Two, and neither has happened: a third result shape appearing,
which would turn six duplicated lines into twelve and change the arithmetic above; or C#
gaining discriminated unions, which would let one type express both cases at no duplication
cost.

**Scope when it is picked up.** `Result` is part of `ITaskService`, shipped at F5a. Changing it
is its own pull request, not something folded into a feature branch that happens to touch a
result type.

## Each handler opens its own scope, rather than the dispatcher opening one

**What the compromise is.** `MessageHandler` (registered singleton,
`ImplServiceCollectionExtensions.cs:78`) cannot constructor-inject `IAiClient` (registered
scoped, same file line 138), so it takes `IServiceScopeFactory` and opens a scope per update
inside `HandleAsync`. `DueReminderJob.cs:27` already does the same for `ITaskService`. The scope
handling therefore lives in each consumer rather than in one place.

**Why it is this way, route by route.**

1. *Constructor-inject `IAiClient` into `MessageHandler`.* This is a captive dependency — a
   scoped service pinned for the process lifetime by a singleton holder. `ValidateScopes` is
   enabled by default in the Development environment, so the host throws while composing:
   "Cannot consume scoped service 'Assistant.Interfaces.IAiClient' from singleton". In
   Production it does not throw; it silently produces one instance for the life of the process,
   which is worse than the exception.

2. *Register `IAiClient` as a singleton so the injection is legal.* This compiles and starts,
   and defeats the reason the lifetime is scoped. `AiClient` depends on `IAiApi`, a Refit
   interface backed by a typed `HttpClient` (`ImplServiceCollectionExtensions.cs:124`).
   `IHttpClientFactory` hands out clients over a rotating pool of `HttpMessageHandler`
   instances — the default handler lifetime is two minutes — so DNS changes are picked up. A
   singleton holder pins one handler, and the addresses it resolved, for as long as the process
   runs. That is the stale-DNS failure `IHttpClientFactory` exists to prevent, and OpenRouter
   sits behind a CDN, so it is a live concern rather than a theoretical one.

3. *Register `MessageHandler` as scoped.* This does not work on its own, because the thing
   holding the handler is itself a singleton. `TelegramListener` is a `BackgroundService` that
   takes `IEnumerable<ITelegramUpdateHandler>` and resolves the whole collection once, in the
   field initializer at `TelegramListener.cs:39`, to compute `_allowedUpdates` for the long-poll
   call. Making handlers scoped forces the listener to open a scope per update and resolve
   handlers inside it — which relocates the same `IServiceScopeFactory` one level up, and
   breaks that field initializer.

**The fix, when it is worth doing.** Route 3 taken deliberately rather than as a side effect:
`TelegramListener.DispatchAsync` opens one scope per update and resolves the matching handlers
from it, and handlers go back to plain constructor injection of what they need.

```csharp
using var scope = scopeFactory.CreateScope();

foreach (var handler in scope.ServiceProvider
    .GetServices<ITelegramUpdateHandler>()
    .Where(h => h.Handles == update.Type))
```

The cost is plain: `_allowedUpdates` can no longer be a field initializer over an injected
collection, because the handlers are no longer resolvable at construction. The kinds have to be
computed once at startup from a scope the listener opens for that purpose. That is the whole
cost, and it is small — but it is a change to code shipped at F7, which is why it is not worth
paying for a single handler.

**The trigger for revisiting.** The second update handler. F6 ("Complete a task from a button",
in `docs/design/2026-08-22-slice-1-feature-backlog.md`) introduces `ICallbackHandler` and
`CallbackRouter`, a second `ITelegramUpdateHandler` that will need a scope for the same reason
`MessageHandler` does. At one handler the current shape is one `CreateScope()` call in one
place; at two it is the same three lines copied, and the dispatcher becomes the obviously right
owner.

**Scope when it is picked up.** This is a deliberate exception to the preamble's rule above, and
it is named as one on purpose: an entry here is not a licence to fix the thing opportunistically
inside an unrelated feature branch. F6 is not that — F6 is the branch that creates the trigger,
the second handler, in the first place, so the pull request that adds `ICallbackHandler` is
also the right place to move the scope into `TelegramListener` and simplify `MessageHandler`
to match.

**Resolved at F6-2.** `TelegramListener.DispatchAsync` opens one scope per update and resolves
`ITelegramUpdateHandler` from it; `MessageHandler` is back to plain constructor injection of
`IAiClient`; both are registered scoped. One correction to this entry's own text, made in the same
commit that resolves it: the second handler this entry predicted, `CallbackRouter`, shipped without
a paired `ICallbackHandler` interface: nothing in this design routes a callback query a second,
different way, so a router-level interface with one implementation would be exactly the guess this
project has already twice reversed the cost of writing. `CallbackRouter` implements
`ITelegramUpdateHandler` directly, the same as `MessageHandler` does.
