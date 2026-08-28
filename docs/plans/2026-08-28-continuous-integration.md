# Continuous Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every pull request and every push to `main` a machine check — build, both test
suites, and a secret scan — so a broken or leaking change is caught before a human has to notice
it in a diff.

**Architecture:** One GitHub Actions workflow, `ci.yml`, runs on `ubuntu-latest`: check out full
history, scan every commit with gitleaks, restore and build the solution with warnings as errors,
bring up Postgres and the WireMock stub with Docker Compose, then run the unit and integration
test projects. No source or test code changes — the suites and the compose file this workflow
drives already exist and already pass locally.

**Tech Stack:** GitHub Actions, Docker Compose, gitleaks, .NET 10 SDK

**Spec:** `docs/design/slice-1-reminders.md` sections 9 step 1, 11.2, 11.3

## Global Constraints

- No emoji anywhere in the repository: not source, tests, documentation, commit messages, or bot
  message text (conventions section 12.6).
- Max 1000 changed lines in a pull request. This one should land under 200.
- YAGNI: write only what this feature needs. No matrix builds, no code coverage, no README badge,
  no caching beyond what `actions/setup-dotnet` already does, no image-publishing job. Publishing
  to GHCR is F14's work (spec 11.6).
- `main` is protected by the GitHub ruleset `main-protection` (id 21162263). Never push to `main`
  directly. Work on a branch and open a pull request.
- No `.editorconfig`, no `global.json`, no `.config/dotnet-tools.json`.
- `TreatWarningsAsErrors` is already set in `Directory.Build.props`. The workflow must not pass a
  redundant flag for it.
- Never run `docker compose down -v`. It destroys the owner's local database. Use
  `docker compose -f compose.test.yaml down` with no `-v` if a task needs to stop containers.
- No secret may ever be committed. `.env` is gitignored.
- The repository is public. Anything printed into a build log is public.

---

## Background

Seven pull requests have merged into this repository and none was ever checked by a machine.
`.github/workflows` has never existed. Spec section 9 lists a GitHub Actions workflow as part of
implementation step 1 — before any code was written — and it was skipped. Spec section 11.2
states that gitleaks "runs in CI on every push and pull request"; it runs nowhere. During F5b a
live Postgres password reached the tracked, public `appsettings.json` and was caught only because
a human happened to read the diff, not by anything automated.

This is cheap to fix right now, for reasons already true of the repository rather than reasons
this plan has to build:

- `compose.test.yaml` already declares healthchecks on both `postgres-test` and `wiremock`, so
  `docker compose -f compose.test.yaml up -d --wait` blocks until both are ready with no extra
  scripting. Confirmed by running it: both containers reported `Healthy`.
- `PostgresFixture` reads `ASSISTANT_TEST_DB` and `WireMockFixture` reads `ASSISTANT_TEST_STUB`,
  each falling back to the fixed compose port, and each already polls for readiness with a
  deadline. Neither needs changing.
- Tracked history contains no `Password=` string in any `.json` file, ever. Confirmed with
  `git rev-list --all`, `git grep`-ing every reachable commit for `.json` files: zero hits. The
  only credential-shaped string anywhere in history is the documented `assistant` / `assistant`
  local test password in `docs/e2e-local.md`.
- `tests/Assistant.WireMock/Dockerfile` is a multi-stage build that layers restore separately, so
  it builds on a runner with no changes.

**Consequence: no file under `src/` or `tests/` changes in this plan.**

## Two deviations from spec 11.3

**1. The architecture tests do not get their own stage.** Spec 11.3 lists the stages as "restore,
build with warnings as errors, architecture tests, unit tests, integration tests, gitleaks". The
architecture tests live inside `tests/Assistant.UnitTests/Architecture/`, in the same assembly as
every other unit test, so a separate stage would mean two filtered runs of one assembly. The
reason to refuse that is not the few seconds it costs — it is that `dotnet test --filter` exits
`0` when the filter matches nothing. A typo in an inverse filter would turn an entire test stage
into a green no-op that nobody would notice, silently. A single unfiltered
`dotnet test tests/Assistant.UnitTests/Assistant.UnitTests.csproj` cannot skip anything without
the run itself failing to find the project. Spec 11.3's stage list was written before the test
projects existed; the architecture tests still run, in the stage they physically live in, which
this plan's workflow calls "Unit and architecture tests" rather than pretending it is two stages.

**2. gitleaks runs first, not last.** Spec 11.3 lists it last. Two reasons to move it earlier
instead. First, if the build fails, a last-place gitleaks step never runs at all — a pull request
that both leaks a secret and fails to compile would produce no secret warning, because the job
stops before reaching it. Second, a leaked credential is already leaked the moment it is pushed to
a public repository; the only thing CI ordering controls from that point on is how fast the owner
is told to revoke it. First-in-job gitleaks costs roughly thirty seconds to report a leak; a
gitleaks step at the end of a six-minute build costs six minutes, purely because of where it sits
in the file.

---

## Verified facts this plan rests on

Each of these was checked against the real repository or a real container before this plan was
written, at commit `ed45ec4` on 2026-08-28. The last two rows are explicitly not binding — Task 1
re-measures them at execution time rather than trusting this table, because a gitleaks release
between now and then can change either answer.

| Fact | How it was checked |
| :--- | :--- |
| `dotnet restore PersonalAssistant.slnx` succeeds | Ran it — `All projects are up-to-date for restore.` |
| `dotnet build PersonalAssistant.slnx --configuration Release --no-restore` succeeds with zero warnings | Ran it — `Build succeeded. 0 Warning(s) 0 Error(s)`, ~4.5s |
| `docker compose -f compose.test.yaml up -d --wait` blocks until both services are healthy, with no extra scripting | Ran it — both `postgres-test` and `wiremock` reported `Healthy` |
| `dotnet test tests/Assistant.UnitTests/Assistant.UnitTests.csproj --configuration Release --no-build` passes | Ran it — 20 passed, 0 failed |
| `dotnet test tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj --configuration Release --no-build` passes | Ran it — 28 passed, 0 failed |
| No `Password=` string exists in any `.json` file anywhere in tracked history | `git rev-list --all` piped through `git grep -l "Password=" <rev> -- '*.json'` for every commit — zero hits |
| Ruleset `21162263` (`main-protection`) currently has no `required_status_checks` rule | `gh api repos/amitwev/personal-ai-assistant/rulesets/21162263` — `rules` is exactly `pull_request`, `non_fast_forward`, `deletion` |
| The current stable gitleaks release is `v8.30.1`, and `--help` against that tag lists `git` and `dir` subcommands with no `detect` | `gh api repos/gitleaks/gitleaks/releases/latest`, then `docker run --rm ghcr.io/gitleaks/gitleaks:v8.30.1 --help` |
| Scanning this repository's full history with `v8.30.1` finds nothing — including no finding for the `assistant`/`assistant` password in `docs/e2e-local.md` — and does not hit "dubious ownership" | `docker run --rm --volume "$PWD:/repo" ghcr.io/gitleaks/gitleaks:v8.30.1 git /repo --no-banner --redact --verbose` → `no leaks found`, exit `0` |

The "dubious ownership" result needs one more sentence than the table has room for: on this
machine (macOS, Docker Desktop) the bind mount is presented inside the container as owned by
`root:root`, and the gitleaks container also runs as root, so there is no uid mismatch to trigger
the check. That says nothing about GitHub's `ubuntu-latest` runner, where `actions/checkout`
writes the repository as the runner's own non-root user and a root-inside-container gitleaks
process would see a genuine mismatch. Task 1 Step 3 treats the local result as uninformative for
that reason and names the real test as the workflow's first run.

---

## Task 1: Pin gitleaks and learn what it reports here

**Files:** none. This task produces a value (`GITLEAKS_VERSION`) and a decision (whether
`.gitleaks.toml` is needed) that Tasks 2 and 3 consume. No file changes happen in this task unless
Step 4 finds the documented password, in which case `.gitleaks.toml` is created here.

Three things about gitleaks cannot be assumed and must be measured on this repository before the
workflow is written: the version to pin, the subcommand that version supports, and whether the
container hits a "dubious ownership" error against this repository's bind mount. A fourth check
confirms the one documented password already in this repository does not fail every future pull
request.

- [ ] **Step 1: Determine the current stable version**

  ```bash
  gh api repos/gitleaks/gitleaks/releases/latest --jq .tag_name
  ```

  Record the result as `GITLEAKS_VERSION`. Every `GITLEAKS_VERSION` reference in Task 2's YAML and
  Task 3's documentation edits becomes this value. Never pin `latest` in the workflow itself: a
  floating tag means a gitleaks release can turn this repository red overnight with no change from
  the owner. At the time this plan was written that command returned `v8.30.1` — use whatever it
  returns when this step actually runs, not that recorded value, since gitleaks ships releases
  regularly and the tag must be current when Task 2 is written, not when this plan was.

- [ ] **Step 2: Determine the correct subcommand**

  ```bash
  docker run --rm ghcr.io/gitleaks/gitleaks:GITLEAKS_VERSION --help
  ```

  `gitleaks detect` was deprecated in favour of the `git` and `dir` subcommands around v8.19, so
  confirm which this pinned version supports rather than assuming. Expected: the `Available
  Commands` list shows `dir` and `git` and does not show `detect`. If it does show `detect`
  instead, use `detect --source /repo` in place of `git /repo` throughout Task 2's YAML and say so
  in the commit message. (Checked against `v8.30.1` while writing this plan: `git` and `dir` are
  present, `detect` is not — expect the same result if Step 1 pins the same or a later version.)

- [ ] **Step 3: Determine whether the container hits "dubious ownership" against this repository**

  Run, from the repository root, the exact command Task 2's workflow will run:

  ```bash
  docker run --rm --volume "$PWD:/repo" ghcr.io/gitleaks/gitleaks:GITLEAKS_VERSION git /repo --no-banner --redact --verbose
  ```

  The gitleaks image runs as root over a bind-mounted repository owned by another uid, which is
  the classic trigger for git's "detected dubious ownership" error. **A clean result on macOS does
  not settle this for the GitHub Actions runner.** Checked while writing this plan: on macOS,
  Docker Desktop presents the bind mount as owned by `root:root` and the container also runs as
  root, so there is no uid mismatch to trigger the check there — that result says nothing about
  `ubuntu-latest`, where `actions/checkout` writes the repository as the runner's own non-root
  user. Treat a clean local run as inconclusive and rely on the first real run of Task 2's workflow
  as the actual test.

  If the workflow's "Scan every commit for secrets" step fails with a message containing `detected
  dubious ownership`, fix it by adding `--user "$(id -u):$(id -g)"` to that step's `docker run`
  line in `ci.yml` — GitHub Actions runners execute every step of a job as the same user, so
  `$(id -u):$(id -g)` on the runner matches whatever `actions/checkout` wrote. Prefer this over
  setting `safe.directory`, which needs a `git config` call run inside the container before
  gitleaks starts and fixes the identical problem more roundaboutly.

- [ ] **Step 4: Determine whether the documented test password produces a finding**

  Read the output of the same command run in Step 3.

  - **Clean** — the log ends with `no leaks found` and the process exits `0`. Checked while writing
    this plan against `v8.30.1`: this is what happened, including for the `assistant` /
    `assistant` password documented in `docs/e2e-local.md`. If Step 1 pins the same version, expect
    the same result; if a newer version pins a different one, re-run and read the actual output
    rather than assuming it still holds.
  - **One finding, and it names `docs/e2e-local.md`** — proceed to the conditional deliverable
    below.
  - **A finding anywhere else** — stop. Do not allowlist it. Report the finding to the repository
    owner instead. It is either a real secret that must be revoked, or a false positive that needs
    a human judgement call this plan does not make on the owner's behalf.

  **The conditional deliverable — create `.gitleaks.toml` only if Step 4 found the
  `docs/e2e-local.md` password.** If gitleaks is already quiet, as it was when this plan was
  written, do not create the file: an allowlist for a finding that does not exist is configuration
  nobody can verify. If Step 4 does find it, create `.gitleaks.toml` at the repository root:

  ```toml
  [allowlist]
  paths = [
      '''docs/e2e-local\.md''',
  ]
  ```

  Note in the commit message which rule matched, so a future reader knows why the allowlist exists
  without re-running gitleaks themselves.

- [ ] **Step 5: Record the measured values for Tasks 2 and 3**

  Write down: the value of `GITLEAKS_VERSION`, the subcommand used (`git` unless Step 2 found
  otherwise), whether `--user "$(id -u):$(id -g)"` was needed (Step 3), and whether
  `.gitleaks.toml` was created (Step 4). Task 2 and Task 3 both consume these.

---

## Task 2: Write the workflow

**File:** create `.github/workflows/ci.yml`

```yaml
name: ci

on:
  pull_request:
  push:
    branches: [main]

jobs:
  ci:
    runs-on: ubuntu-latest

    steps:
      - name: Check out the full history
        uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Scan every commit for secrets
        run: >
          docker run --rm --volume "$PWD:/repo"
          ghcr.io/gitleaks/gitleaks:GITLEAKS_VERSION
          git /repo --no-banner --redact --verbose

      - name: Install the .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore PersonalAssistant.slnx

      - name: Build with warnings as errors
        run: dotnet build PersonalAssistant.slnx --configuration Release --no-restore

      - name: Start Postgres and the stub API
        run: docker compose -f compose.test.yaml up -d --wait

      - name: Unit and architecture tests
        run: dotnet test tests/Assistant.UnitTests/Assistant.UnitTests.csproj --configuration Release --no-build

      - name: Integration tests
        run: dotnet test tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj --configuration Release --no-build
```

`GITLEAKS_VERSION` on the `ghcr.io/gitleaks/gitleaks:GITLEAKS_VERSION` line is a named
substitution, not a literal string to commit: replace it with the value Task 1 Step 1 measured
(for example `v8.30.1`, if that is still current when this task runs). If Task 1 Step 3 found
"dubious ownership", add `--user "$(id -u):$(id -g)"` to the `docker run` invocation in the "Scan
every commit for secrets" step. If Task 1 Step 2 found `detect` instead of `git`/`dir`, replace
`git /repo` with `detect --source /repo`.

Why each choice a reader would otherwise question:

- `fetch-depth: 0` because the default shallow clone gives gitleaks one commit to scan, not the
  repository's history.
- `--redact` because a finding would otherwise print the secret itself into a public build log,
  turning the leak detector into a second leak.
- `--no-banner` and `--verbose` for a log that names the finding without ASCII art.
- The gitleaks Docker image rather than `gitleaks/gitleaks-action`: the action gates on a
  `GITLEAKS_LICENSE` for organisations. It is free for a personal repository today, but that is a
  third party's licensing decision sitting inside this build, and the official image has no such
  gate.
- `push: branches: [main]` rather than every push: spec 11.2 asks for "every push and pull
  request", and this pair covers it without running the whole suite twice on every feature-branch
  push that already has an open pull request.
- `--no-restore` and `--no-build` so the later steps cannot silently rebuild with different
  settings than the step that already enforced warnings as errors.
- No `-warnaserror` flag, because `Directory.Build.props` already sets `TreatWarningsAsErrors`; a
  redundant flag here would be a second place to keep in sync with the first.

- [ ] **Step 1: Write `.github/workflows/ci.yml`** with `GITLEAKS_VERSION` and, if applicable, the
  Task 1 Step 2 subcommand and Step 3 `--user` flag substituted in.

- [ ] **Step 2: Verify locally before pushing**

  ```bash
  docker compose -f compose.test.yaml up -d --wait
  dotnet restore PersonalAssistant.slnx
  dotnet build PersonalAssistant.slnx --configuration Release --no-restore
  dotnet test tests/Assistant.UnitTests/Assistant.UnitTests.csproj --configuration Release --no-build
  dotnet test tests/Assistant.IntegrationTests/Assistant.IntegrationTests.csproj --configuration Release --no-build
  ```

  Run these exactly as written in the YAML above — not `dotnet test` at the solution root — since
  the workflow names each project explicitly and a solution-level command could mask a
  project-specific failure the workflow would actually hit. Expected: restore succeeds; build
  succeeds with zero warnings and zero errors; **20 unit tests pass**; **28 integration tests
  pass**. These counts were measured while writing this plan (2026-08-28, commit `ed45ec4`) —
  re-confirm them here rather than trusting this plan, since a branch further along may have added
  or removed tests.

- [ ] **Step 3: Commit**

  ```bash
  git add .github/workflows/ci.yml
  git commit -m "ci: add build, test, and gitleaks workflow"
  ```

  If Task 1 Step 4 created `.gitleaks.toml`, add and commit it in this same commit, and say in the
  message which rule it allowlists and why.

---

## Task 3: Make the documentation true again

**Files:**
- `AGENTS.md`
- `docs/design/2026-08-22-slice-1-feature-backlog.md`
- `README.md`

- [ ] **Step 1: Correct `AGENTS.md`**

  The "Commands" section currently opens with:

  > These are the commands to run before opening a pull request. Nothing runs them automatically
  > yet — there is no CI in this repository (see "Continuous integration" in
  > `docs/design/2026-08-22-slice-1-feature-backlog.md`), so a failing command here will not be
  > caught by anything except a human reading the diff.

  Replace it with:

  > These are the commands to run before opening a pull request. `.github/workflows/ci.yml` runs
  > the restore, build, and both `dotnet test` commands below — the "Build and test" section — on
  > every push and pull request, so a failing one there is caught by a machine, not only by a human
  > reading the diff. Nothing under "Run locally" or "Database migrations" runs automatically.

  The replacement is deliberately scoped to the "Build and test" commands rather than claiming
  every command in the section runs in CI: the workflow never runs `docker compose ... down -v`,
  and it never runs anything under "Run locally" or "Database migrations", so a blanket claim would
  be exactly the kind of false statement this correction exists to remove.

- [ ] **Step 2: Mark the backlog entry done and record what was settled**

  In `docs/design/2026-08-22-slice-1-feature-backlog.md`, the entry currently reads:

  > **Continuous integration** — spec §9 step 1, §11.2, §11.3 · **unscheduled**
  > There is no `.github/workflows` directory in this repository, and there never has been — no
  > pull request has ever been checked by a machine. Spec §9 step 1 lists "GitHub Actions workflow
  > running them" as part of the very first implementation step, before any code was written; it
  > was skipped. §11.2 states that gitleaks "runs in CI on every push and pull request" — it does
  > not run anywhere, and during F5b a live Postgres password reached the tracked, public
  > `appsettings.json` and was caught only by a human reading a diff, not by any machine. §11.3
  > already specifies the stages this needs: restore, build with warnings as errors, architecture
  > tests, unit tests, integration tests, gitleaks. F14 already lists `.github/workflows/ci.yml`
  > among its contents, so this is not a competing feature number — it is a flag that a promise the
  > documents already make is unbacked, which is worse than not having made it.

  Change `· **unscheduled**` to `· **done**`, keep the paragraph above unchanged as the historical
  record of why the work existed, and append the following list directly beneath it, in the same
  shape the F7 entry above it uses:

  ```markdown
  *Settled at CI:*
  - **The architecture tests do not get their own stage**, unlike the four stages §11.3 lists
    around them. They live inside `tests/Assistant.UnitTests/Architecture/`, in the same assembly
    as every other unit test, so a separate stage would mean two filtered runs of one assembly —
    and `dotnet test --filter` exits `0` when a filter matches nothing, so a typo in an inverse
    filter would turn an entire stage into a silent no-op. A single unfiltered
    `dotnet test tests/Assistant.UnitTests/Assistant.UnitTests.csproj` cannot skip anything that
    way. §11.3's stage list predates the test projects; the architecture tests run in the stage
    they physically live in.
  - **gitleaks runs first in `ci.yml`, not last as §11.3 lists it.** A last-place gitleaks step
    never runs at all if the build fails first, so a pull request that both leaks a secret and
    fails to compile would raise no secret warning. And a leaked credential is already leaked the
    moment it reaches a public repository — ordering only controls how fast the owner is told to
    revoke it, roughly thirty seconds first-in-job against several minutes last-in-job.
  - **The official gitleaks Docker image, not `gitleaks/gitleaks-action`.** The action gates on a
    `GITLEAKS_LICENSE` for organisations; free for a personal repository today, but that is a third
    party's licensing decision sitting inside this build, and the image carries no such gate.
  - **gitleaks was pinned to `GITLEAKS_VERSION`, invoked with the `git` subcommand** — `detect` was
    confirmed absent from this line of releases by running `--help` against the pinned image.
    Running it against the repository's full history found `GITLEAKS_STEP_4_RESULT`.
  - **Whether the container hit "dubious ownership" against this repository's bind mount:
    `GITLEAKS_STEP_3_RESULT`.** A clean result on a non-Linux development machine does not settle
    this — Docker Desktop's bind mount ownership does not reproduce what a Linux CI runner's
    checkout looks like to a container running as root, so the real test was the workflow's first
    run rather than a local one.
  ```

  `GITLEAKS_VERSION`, `GITLEAKS_STEP_4_RESULT`, and `GITLEAKS_STEP_3_RESULT` are the values Task 1
  Step 5 recorded — write the actual measured sentence in each place (for example
  `GITLEAKS_STEP_4_RESULT` becomes "no findings, so no `.gitleaks.toml` was created" or "one
  finding, in `docs/e2e-local.md`, allowlisted in `.gitleaks.toml`", whichever Task 1 actually
  measured), not the token itself.

- [ ] **Step 3: Add the README line spec 11.3 asks for**

  In `README.md`, the "Contributing" section currently ends:

  > The whole test suite runs with **no credentials at all** — Telegram and the
  > LLM APIs are stubbed with WireMock and Postgres comes from Docker Compose.
  > Fork, `dotnet test`, done. See [AGENTS.md](./AGENTS.md) for every command and
  > [docs/design/](./docs/design/) for why the system is shaped the way it is.

  Insert one sentence after "Fork, `dotnet test`, done." so the section reads:

  ```markdown
  The whole test suite runs with **no credentials at all** — Telegram and the
  LLM APIs are stubbed with WireMock and Postgres comes from Docker Compose.
  Fork, `dotnet test`, done. The same suite runs automatically on a fork's pull
  request, with zero credentials configured, via `.github/workflows/ci.yml`.
  See [AGENTS.md](./AGENTS.md) for every command and
  [docs/design/](./docs/design/) for why the system is shaped the way it is.
  ```

  Spec 11.3 calls this out explicitly as worth stating as a feature rather than leaving implicit:
  a contributor can validate a change end to end without an Anthropic account or a Telegram bot,
  and now a machine proves that on every fork PR rather than the claim resting on trust.

- [ ] **Step 4: Commit**

  ```bash
  git add AGENTS.md docs/design/2026-08-22-slice-1-feature-backlog.md README.md
  git commit -m "docs: record that CI now exists"
  ```

---

## Task 4: Prove each stage can fail

This project has a standing practice, used at F5b and F7: a test that has never been seen to fail
is not known to work. A green CI run on a correct branch proves only that the YAML parses and the
commands happen to succeed today — it does not prove any of the three failure-detecting stages
would actually turn red if the thing they check for happened.

Record F7's lesson before doing this: a deliberate-break instruction can itself be wrong. At F7 the
planned break left a primary-constructor parameter unreferenced and tripped `CS9113`, so the code
did not compile and never reached the test it was meant to fail — the break was checked to be red,
but not checked to be red *for the intended reason*. Each break below must be confirmed to fail at
the intended step and for the intended reason, not merely to be red somewhere.

Each of the three breaks below is: made on its own throwaway branch, pushed to open a pull request
against this repository, confirmed red at the specific step named, and then the branch is deleted
without merging. **These branches must never be merged, and must be deleted immediately after the
observation.** Pushing the AWS example key means it sits in a public branch on GitHub until that
branch is deleted — acceptable only because `AKIAIOSFODNN7EXAMPLE` is AWS's own published
documentation example, not a real credential, and gitleaks flags it as a finding regardless.

- [ ] **Step 1: gitleaks**

  ```bash
  git checkout -b ci-break-gitleaks
  echo 'AKIAIOSFODNN7EXAMPLE' > ci-break-secret.txt
  git add ci-break-secret.txt
  git commit -m "ci: deliberately trip the gitleaks stage (throwaway, do not merge)"
  git push -u origin ci-break-gitleaks
  ```

  Open a pull request from this branch. Confirm the "Scan every commit for secrets" step goes red,
  and that the log names the AWS example key as the finding — not a different step, and not a red
  result for an unrelated reason. Then:

  ```bash
  git push origin --delete ci-break-gitleaks
  git checkout main
  git branch -D ci-break-gitleaks
  ```

- [ ] **Step 2: warnings as errors**

  ```bash
  git checkout -b ci-break-warnings
  ```

  Add `private readonly int _unused;` to any class in `src/Assistant.Impl`. That field is never
  read, which raises `CS0169`, and `TreatWarningsAsErrors` turns that warning into a build failure.

  ```bash
  git add -A
  git commit -m "ci: deliberately trip the warnings-as-errors stage (throwaway, do not merge)"
  git push -u origin ci-break-warnings
  ```

  Open a pull request. Confirm the "Build with warnings as errors" step goes red and the log names
  `CS0169`, and that the two steps before it (gitleaks, restore) stayed green. Then:

  ```bash
  git push origin --delete ci-break-warnings
  git checkout main
  git branch -D ci-break-warnings
  ```

- [ ] **Step 3: a failing test**

  ```bash
  git checkout -b ci-break-test
  ```

  Change one assertion in `tests/Assistant.UnitTests` so it fails — for example, flip an
  `Assert.True` to `Assert.False` on a condition that is actually true, so the change is obviously
  temporary to any reviewer who happens to see the diff.

  ```bash
  git add -A
  git commit -m "ci: deliberately trip the unit test stage (throwaway, do not merge)"
  git push -u origin ci-break-test
  ```

  Open a pull request. Confirm the "Unit and architecture tests" step goes red, that the log names
  the specific test that failed, and that the build step before it stayed green. Then:

  ```bash
  git push origin --delete ci-break-test
  git checkout main
  git branch -D ci-break-test
  ```

- [ ] **Step 4: Record the outcome**

  For each of the three breaks, note in this plan (or the pull request that lands Task 2) which
  step went red, whether the failure reason matched what the break was meant to cause, and whether
  any earlier step masked it by failing first for an unrelated reason. If any break did not fail
  the way intended — compare to F7's `CS9113` lesson above — fix the break and repeat it before
  concluding this task, rather than accepting a red result that proves the wrong thing.

---

## Task 5: Require the check before merge

**Do not run this task until Task 2's workflow has gone green on its own pull request.** Requiring
a check that has never reported would block the very pull request that introduces it, since GitHub
has no successful run of `ci` to satisfy the requirement with.

**This task requires the repository owner's confirmation before running**, because it changes
repository settings rather than files in the working tree. Show the owner the exact commands below
before running any of them.

The GitHub rulesets API replaces the whole ruleset on update, so the recipe is read, modify,
write — never a blind `PUT`. The current ruleset (`21162263`, `main-protection`) has exactly three
rules today — `pull_request`, `non_fast_forward`, `deletion` — and no `required_status_checks`
rule, confirmed by reading it while writing this plan.

- [ ] **Step 1: Read the current ruleset**

  ```bash
  gh api repos/amitwev/personal-ai-assistant/rulesets/21162263 > /tmp/ruleset.json
  ```

- [ ] **Step 2: Add the required check to its `rules` array**

  Add this object to the `rules` array in `/tmp/ruleset.json`, alongside the three that are already
  there — do not remove or edit the existing three:

  ```json
  {
    "type": "required_status_checks",
    "parameters": {
      "strict_required_status_checks_policy": false,
      "required_status_checks": [
        { "context": "ci" }
      ]
    }
  }
  ```

- [ ] **Step 3: Write it back**

  ```bash
  gh api --method PUT repos/amitwev/personal-ai-assistant/rulesets/21162263 --input /tmp/ruleset.json
  ```

  If this is rejected because the GET response in Step 1 included fields the API does not accept
  back on write (`id`, `node_id`, `created_at`, `updated_at`, `_links`, `current_user_can_bypass`,
  `source_type`, `source`), strip those keys from `/tmp/ruleset.json` with `jq` and retry — GitHub's
  read shape and write shape are not guaranteed identical.

- [ ] **Step 4: Confirm**

  ```bash
  gh api repos/amitwev/personal-ai-assistant/rulesets/21162263 --jq '.rules[] | select(.type == "required_status_checks")'
  ```

  Expected: the object from Step 2, present in the ruleset.

**`"context": "ci"` matches the job id `ci` in `ci.yml`** (`jobs: ci:`), not the workflow's `name:`
field. Renaming the job in the YAML without updating this ruleset breaks the requirement silently —
GitHub would report a required check that never arrives, and every future pull request would be
permanently blocked until someone notices and fixes one side or the other.

**`strict_required_status_checks_policy` is `false` deliberately.** `true` would force every branch
to be up to date with `main` before it can merge, re-running CI after every merge to `main` even
when the branch's own diff never changed. On a single-maintainer repository with no concurrent
pull requests competing for the same files, that is friction with no corresponding benefit.

---

## File Structure

```
.github/workflows/ci.yml                                    new
.gitleaks.toml                                               new, only if Task 1 Step 4 found a finding
AGENTS.md                                                     modified — Commands section intro
docs/design/2026-08-22-slice-1-feature-backlog.md             modified — CI entry marked done, settled-decisions added
README.md                                                      modified — one line under Contributing
```

No file under `src/` or `tests/` changes. Task 4's throwaway branches are never merged and touch
no file that lands on `main`.

---

## Self-review

- [ ] `.github/workflows/ci.yml` pins an exact gitleaks tag — never `latest`
- [ ] The `docker run` line uses whichever subcommand Task 1 Step 2 actually found (`git` unless
      recorded otherwise), and carries `--user "$(id -u):$(id -g)"` only if Task 1 Step 3 found it
      necessary
- [ ] `.gitleaks.toml` exists if and only if Task 1 Step 4 found a finding — not created
      unconditionally, not skipped if one was found
- [ ] `dotnet restore PersonalAssistant.slnx` and
      `dotnet build PersonalAssistant.slnx --configuration Release --no-restore` were run locally
      and both succeeded before pushing
- [ ] Both `dotnet test` commands were run locally exactly as written in the YAML, and their real
      pass counts were recorded in this plan, not assumed from an earlier feature's numbers
- [ ] No `-warnaserror` flag anywhere in `ci.yml` — `Directory.Build.props` already sets
      `TreatWarningsAsErrors`
- [ ] No `.editorconfig`, `global.json`, or `.config/dotnet-tools.json` was added
- [ ] All three Task 4 breaks were actually pushed, confirmed red at the intended step for the
      intended reason, and their branches deleted — not merely asserted to have been done
- [ ] Task 5 was not run before Task 2's workflow had a green run on its own pull request
- [ ] Task 5's exact commands were shown to the repository owner before anything was run, and the
      owner's confirmation was received
- [ ] `AGENTS.md`, the backlog entry, and `README.md` all read true against the finished state —
      no leftover claim that CI does not exist
- [ ] No emoji in any changed file, including commit messages
- [ ] Diff for Tasks 2 and 3 combined is under 200 lines, excluding this plan and excluding Task
      4's throwaway branches, which never land

## Expected size

Files touched: `.github/workflows/ci.yml` (new, ~50 lines), optionally `.gitleaks.toml` (new, ~4
lines, conditional on Task 1), `AGENTS.md` (~4 lines changed),
`docs/design/2026-08-22-slice-1-feature-backlog.md` (~20 lines added), `README.md` (~2 lines
added). Approximately 80-100 changed lines in the pull request that lands Tasks 2 and 3, well under
the 200-line target and far under the repository's 1000-line hard limit. Task 4 adds no lines to
that count — its branches are never merged. Task 5 changes no file at all.
