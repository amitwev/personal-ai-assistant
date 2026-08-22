# YAGNI Reset — delete everything not needed yet

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> This plan is a deletion, not a feature. Nothing new is written.

**Goal:** Reduce the codebase to exactly what the next deliverable exercises, and adopt a
standing rule that keeps it that way.

> Arithmetic correction: an earlier revision of this document said "24 files" and "→ 5". The
> named lists in §4 and §10 were always right — 23 deletions, 4 survivors, from 27 starting
> files in `src/`. The counts have been corrected to match the lists.

**Spec:** `docs/design/slice-1-reminders.md` (unchanged — it describes the target design,
not the build order).

---

## 1. The standing rule

> A task may only introduce an interface member, contract type, model, model property, or
> table that **the same task exercises with a test**. Everything else waits for the task that
> consumes it.

Corollary: **an abstraction with one implementation is not an extension seam — it is a guess.**
Interfaces earn their place when a second implementation or a test double arrives.

## 2. How this squares with Open/Closed

These two goals pull in opposite directions unless the boundary is drawn precisely, so:

**OCP applies to behaviour seams.** Spec §3.6 already names them: `ITaskAction`,
`IAssistantTool`, `IScheduledJob`, `INotifier`, `IChatClient`. Each is a point where a new
capability is a *new class*, and no existing class is edited. Adding a fourth button must never
mean editing `CallbackRouter`. That property is worth protecting and this plan does not weaken it.

**OCP does not apply to data-access surfaces.** Adding `QueryAsync` to `ITaskRepository` is a
modification, and no amount of design makes it otherwise — every implementer must change. With
exactly one implementation (EF), that cost is one class, and paying it when the need is real
beats guessing eight methods up front.

So: **grow repository and service interfaces method-by-method; keep the behaviour seams
extension-only.** The seams get deleted now not because they are wrong, but because none of them
has a second implementation yet. Each returns with the task that introduces its first real
extension point — at which moment it is a seam rather than a guess.

## 3. What survives

| File | Why |
| :--- | :--- |
| `src/Assistant.Models/ReminderTask.cs` | trimmed — see §5 |
| `src/Assistant.Models/ReminderStatus.cs` | the due-query filters on it |
| `src/Assistant.Interfaces/ITaskRepository.cs` | trimmed to 2 methods — see §5 |
| `src/Assistant.Worker/Program.cs` | bare host, already minimal |
| both `Architecture/*.cs` | re-anchored — see §6 |

All eight projects stay. The project skeleton *is* the architecture (spec §3.2), it is enforced
by tests, and empty projects cost nothing. We delete types, not the frame.

## 4. What gets deleted — 23 files

**`src/Assistant.Contracts/` — all 9 files.** The project is left empty.
`CreateTaskRequest` `DailyBriefNotification` `ErrorCode` `ListTasksRequest`
`ReminderNotification` `Result` `TaskFilter` `TaskResponse` `UpdateTaskRequest`
→ first consumers T5–T12.

**`src/Assistant.Interfaces/` — 11 of 12 files.**
`IAgent` `IAssistantTool` `ICallbackHandler` `IChatMessageRepository` `IClock`
`IDailyBriefRepository` `IMessageHandler` `INotifier` `IScheduledJob` `ITaskAction`
`ITaskService`
→ first consumers T4–T12.

**`src/Assistant.Models/` — 3 of 5 files.**
`ChatMessage` (T11) `DailyBriefLog` (T12) `Priority` (T5)

## 5. Trims

**`ITaskRepository` → 2 members.**

```csharp
Task AddAsync(ReminderTask task, CancellationToken ct);
Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(DateTimeOffset asOfUtc, int limit, CancellationToken ct);
```

Removed: `FindAsync`, `UpdateAsync`, `QueryAsync`, `CountOpenWithoutDueDateAsync`.

Two, not one. `GetDueRemindersAsync` cannot be tested without a write path, and seeding through
raw SQL would duplicate schema knowledge into the tests and rot the first time a column moves.
`AddAsync` is production code that T6 and T11 both call — merely consumed later than it is built.

**`ReminderTask` → 7 properties.**

Kept: `Id` `Title` `Status` `DueAt` `ReminderSentAt` `CreatedAt` `UpdatedAt`

| Removed | Returns at | Because |
| :--- | :--- | :--- |
| `Notes` | T11 | only the capture path writes it |
| `Priority` | T5 | nothing reads it until listings are rendered |
| `CompletedAt` | T9 | completion arrives with the Done button |
| `DeliveryAttempts` | T8 | the retry cap is the scheduler's rule, not the query's |

Dropping `DeliveryAttempts` also removes `delivery_attempts < 3` from the due predicate. At T3 the
predicate is exactly: `status = Pending AND due_at <= now AND reminder_sent_at IS NULL`.

**Cost, stated plainly:** four future `ALTER TABLE` migrations instead of one `CREATE TABLE`.
That is the price of the rule and it is being paid deliberately.

## 6. Architecture test re-anchoring

`ConventionTests` anchors on `typeof(Result)` and `typeof(IClock)`; both are deleted, and
`Assistant.Contracts` will contain zero types, so no `typeof(...)` anchor exists for it at all.

Fix: resolve assemblies by file, which works for an empty assembly.

```csharp
private static Assembly LoadProject(string name) =>
    Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, $"{name}.dll"));

private static Assembly ModelsAssembly => LoadProject("Assistant.Models");
private static Assembly InterfacesAssembly => LoadProject("Assistant.Interfaces");
private static Assembly ContractsAssembly => LoadProject("Assistant.Contracts");
```

`DependencyRuleTests` re-anchors the same way. Every existing convention test then keeps
passing — several vacuously, which is correct: the rule binds before the code it constrains.

## 7. Consequences accepted

1. **`Assistant.Contracts` is an empty project until T5.** Its convention tests pass vacuously.
2. **The spec is not rewritten.** §3.3 and §4.3 describe the target design, which has not
   changed — only the order of construction has. One line is added to §9 recording that types
   are introduced by the task that first uses them.
3. **The Task 3 plan is superseded**, not edited: `docs/plans/2026-08-21-task-3-repository-plan.md`
   loses its chat-message and daily-brief sections and most of `QueryAsync`. It is rewritten
   after this reset lands, against the trimmed surface.
4. **Interfaces churn across tasks.** Intended: each diff shows exactly what capability its task
   bought.

## 8. Pull-request workflow

From here, every task is a branch and a PR, so the code can be read before it lands.

- [ ] Enable a ruleset on `main`: require a pull request before merging, block force pushes,
      block deletion.
- [ ] One branch per task, named `task-<n>-<slug>`.
- [ ] The PR body states what the task added and which tests cover it.

Note: this makes `main` non-pushable directly — including for us. That is the point.

## 9. Steps

- [ ] **Step 1: Branch.** `git checkout -b yagni-reset` off `main`.
- [ ] **Step 2: Delete the 24 files** listed in §4. Use `git rm`.
- [ ] **Step 3: Trim `ITaskRepository`** to the two members in §5.
- [ ] **Step 4: Trim `ReminderTask`** to the seven properties in §5.
- [ ] **Step 5: Re-anchor the architecture tests** per §6.
- [ ] **Step 6: Build.** `dotnet build` — 0 warnings, 0 errors.
- [ ] **Step 7: Test.** `dotnet test tests/Assistant.UnitTests` — all green. Record the count;
      it should be 12, since no convention rule was removed.
- [ ] **Step 8: Add the §9 note to the spec** — one line, recording that types are introduced by
      the task that first uses them.
- [ ] **Step 9: Commit** — `refactor: delete everything not yet exercised by a test`.
- [ ] **Step 10: Open a PR** into `main` and hand over the link for review.

## 10. Verification

1. `dotnet build` — 0 warnings.
2. `dotnet test tests/Assistant.UnitTests` — 12/12.
3. `find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' | wc -l` → **4**
   (`ReminderTask`, `ReminderStatus`, `ITaskRepository`, `Program.cs`, and nothing else).
4. `ls src/Assistant.Contracts/*.cs` → no matches.
5. `grep -c Repository src/Assistant.Impl/Assistant.Impl.csproj` → 0.
6. Every remaining public member still carries a three-line `<summary>`.
