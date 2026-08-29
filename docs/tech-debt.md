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
