# Running the assistant end to end, locally

This is a walkthrough for running the whole system yourself — worker, database, and a
scheduler tick that actually delivers a reminder — outside the test suite. It proves the
same path the unit and integration tests cover, but end to end and against a real running
process, so you can watch a reminder go from a database row to a Telegram message with your
own eyes.

There are two versions of the walkthrough. The stub version talks to the WireMock container
that the test suite already uses, so nothing leaves your machine and no real bot token is
required. The real-Telegram version is a short delta at the end: same steps, a real token,
and the message lands on your phone instead of in a stub's request log.

## Prerequisites

- .NET 10 SDK
- Docker, running

## How configuration resolves

This trips people up, so read it before you start typing commands.

`dotnet run --project src/Assistant.Worker` reads
`src/Assistant.Worker/Properties/launchSettings.json`, which sets
`DOTNET_ENVIRONMENT=Development` for you. That means **user secrets load automatically on a
plain `dotnet run`** — you do not need to export `DOTNET_ENVIRONMENT` yourself for this to
work. (Passing `--no-launch-profile` turns this off, and with it the automatic
`DOTNET_ENVIRONMENT=Development`.)

Environment variables win over user secrets. That is what lets you point a run at the stub
without disturbing whatever real token you may already have stored: export
`TelegramSettings__BotToken` on the command line and it overrides the secret store for that
run only.

Settings bind by section name, double underscore for nesting:

- `TelegramSettings__BotToken`
- `TelegramSettings__OwnerChatId`
- `TelegramSettings__BaseUrl`
- `DatabaseSettings__ConnectionString`

## Walkthrough against the stub

### 1. Start the containers

```bash
docker compose -f compose.test.yaml up -d
```

This brings up two containers: `postgres-test` (Postgres 16, host port 55432, database
`assistant_test`, user `assistant`, password `assistant`) and `wiremock` (the Telegram stub,
host port 58080). The database and credentials above belong to the test suite; the walkthrough
below points the worker at a different, throwaway database on the same server instead of
touching `assistant_test`.

### 2. Give the worker a connection string

`DatabaseSettings__ConnectionString` has to come from somewhere — the worker will not start
without one. The standing local setup is `src/Assistant.Worker/appsettings.Development.json`,
which is gitignored so it never reaches the public repository:

```json
{
  "DatabaseSettings": {
    "ConnectionString": "Host=localhost;Port=55432;Database=assistant;Username=assistant;Password=assistant"
  }
}
```

A fresh clone has no connection string at all — `DatabaseSettings.Validate()` throws
`ConfigurationErrorsException` when it is missing — so creating this file is a required
first-run step, not an optional convenience.

The stub run below still passes an inline `DatabaseSettings__ConnectionString=...` override on
the command line, and that is what redirects it to the throwaway `assistant_e2e` database
instead of the `assistant` database named above. Environment variables beat the settings file,
so both mechanisms coexist here: the file supplies a working default, and the override wins for
this run.

### 3. Run the worker against the stub

```bash
DatabaseSettings__ConnectionString="Host=localhost;Port=55432;Database=assistant_e2e;Username=assistant;Password=assistant" \
TelegramSettings__BotToken="111111:AAFakeTokenForLocalStubRunsOnly_xxxxx" \
TelegramSettings__OwnerChatId="<your-chat-id>" \
TelegramSettings__BaseUrl="http://localhost:58080" \
dotnet run --project src/Assistant.Worker
```

Two things about that command are easy to get wrong:

**The bot token has to be well-formed, not just a placeholder.** `Telegram.Bot` validates the
token's shape inside `AddAssistantTelegram`, before the host even starts. A token that is not
`<digits>:<rest>` throws immediately:

```
Unhandled exception. System.ArgumentException: Bot token invalid (Parameter 'token')
   at Telegram.Bot.TelegramBotClientOptions..ctor(...)
   at Assistant.Impl.ImplServiceCollectionExtensions.AddAssistantTelegram(...)
```

`not-a-real-token` is the obvious thing to try here, and it does not work. Use
`111111:AAFakeTokenForLocalStubRunsOnly_xxxxx` — it satisfies the shape check without being
anyone's real credential.

**The database does not need to exist first.** `assistant_e2e` is not a database
`compose.test.yaml` creates — point the connection string at it anyway and the worker creates
it for you. Watch the log; in order, you should see `CREATE DATABASE assistant_e2e`, then
`CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"`, then both migrations applying
(`20260822103957_InitialCreate`, `20260822202918_AddDueReminderIndex`), then
`Application started. Press Ctrl+C to shut down.` This is `Program.cs` calling
`MigrateAssistantDatabaseAsync` explicitly at startup — `AddAssistantRepository` itself never
migrates.

Leave the worker running in this terminal for the rest of the walkthrough.

### 4. Seed a due reminder

In another terminal, insert a row directly. Reaching `psql` through the container avoids
installing anything locally:

```bash
docker compose -f compose.test.yaml exec -T postgres-test \
  psql -U assistant -d assistant_e2e -c \
  "INSERT INTO reminder_tasks (id, title, status, due_at, reminder_sent_at, created_at, updated_at)
   VALUES (gen_random_uuid(), 'Call the bank', 1, now() - interval '1 hour', NULL, now(), now());"
```

`status = 1` is `ReminderStatus.Pending`; `0` is `Unknown` and is rejected by the
`ck_reminder_tasks_status_known` check constraint. `due_at` an hour in the past means the row
is already due the moment the scheduler looks at it.

The notifier sends with `ParseMode.Html` and escapes `&`, `<` and `>` before sending, so a
hand-seeded title containing those characters is delivered as literal text rather than
rejected — no need to keep hand-seeded titles plain.

### 5. Watch the stub

The scheduler ticks every 30 seconds. Give it up to about 30 seconds before concluding
anything is wrong — in the verified run, the message reached the stub 19 seconds after the
row was inserted.

Since F7, the worker also runs `TelegramListener`
(`src/Assistant.Impl/Telegram/TelegramListener.cs`), a `BackgroundService` that long-polls
Telegram's `getUpdates` for as long as the worker is running. Against real Telegram that poll
blocks for up to 30 seconds between requests, but the stub's default `getUpdates` mapping
answers immediately after a fixed one-second delay, so a running worker adds a `getUpdates`
entry to the stub's request log roughly once per second. The raw `__admin/requests` log is
therefore mostly `getUpdates` polls, not the reminder — filter to `sendMessage`:

```bash
curl -s http://localhost:58080/__admin/requests \
  | python3 -c "import json,sys; [print(e['Request']['Body']) for e in json.load(sys.stdin) if e['Request']['Path'].endswith('/sendMessage')]"
```

You should see the request body printed on its own:

```
{"chat_id":<your-chat-id>,"text":"Call the bank","parse_mode":"Html"}
```

Note that `text` is the bare task title, with no prefix — that is the behaviour conventions
§12.6 settled: the message arrives from the assistant, in a chat only the assistant writes to,
so there is nothing for a prefix to disambiguate.

### 6. Check the row in the database

```bash
docker compose -f compose.test.yaml exec -T postgres-test \
  psql -U assistant -d assistant_e2e -c \
  "SELECT title, status, due_at, reminder_sent_at FROM reminder_tasks;"
```

`reminder_sent_at` should now be populated — it is set after delivery, not at seed time.

### 7. Confirm the reminder does not fire twice

This is the single most valuable check in the walkthrough. Wait another 45 seconds — one and
a half tick intervals — and count the stub's `sendMessage` requests again, filtering out the
`getUpdates` polling the same way as step 5:

```bash
curl -s http://localhost:58080/__admin/requests \
  | python3 -c "import json,sys; print(sum(1 for e in json.load(sys.stdin) if e['Request']['Path'].endswith('/sendMessage')))"
```

The count should stay at exactly one. That proves the row was marked sent rather than
redelivered on every tick.

### 8. Clean up

Stop the worker with Ctrl+C in its terminal, then drop the throwaway database and reset the
stub:

```bash
docker compose -f compose.test.yaml exec -T postgres-test psql -U assistant -d postgres \
  -c 'DROP DATABASE assistant_e2e;'
curl -s -X POST http://localhost:58080/__admin/requests/reset
```

Leave the containers running if you plan to repeat the walkthrough; otherwise take them down
entirely:

```bash
docker compose -f compose.test.yaml down -v
```

## Walkthrough against real Telegram

Two things differ from the stub run: you supply a real bot token, and you omit
`TelegramSettings__BaseUrl` so the client talks to `api.telegram.org` instead of the stub.

Store the token and your chat id in user secrets, never in `appsettings.Development.json` —
this repository is public:

```bash
dotnet user-secrets set "TelegramSettings:BotToken" "<token from BotFather>" \
  --project src/Assistant.Worker
dotnet user-secrets set "TelegramSettings:OwnerChatId" "<your-chat-id>" \
  --project src/Assistant.Worker
```

Before running the full worker, use the token diagnostic. It needs no database at all —
`Program.cs` handles `send-test-message` before the database is wired, specifically so this
works with no connection string configured:

```bash
dotnet run --project src/Assistant.Worker -- send-test-message
```

It sends "Assistant is configured and can reach you." to the owner chat. Run this first: it
separates "my token is wrong" from "my scheduler is wrong" before you invest in seeding a row.

From there, the full run is a plain `dotnet run --project src/Assistant.Worker` with no
environment variables at all — which is exactly what the owner ran. `appsettings.Development.json`
supplies the database and user secrets supply the token, so nothing needs exporting:

```bash
dotnet run --project src/Assistant.Worker
```

Seed a due row the same way as step 4 above, wait up to 30 seconds, and the reminder arrives
on your phone instead of in the stub's request log. The at-most-once check in step 7 applies
here too — it is worth doing once against real Telegram, not only against the stub.

## Troubleshooting

**`System.ArgumentException: Bot token invalid` at startup.** The token is not shaped like
`<digits>:<rest>`. For the stub walkthrough, use
`111111:AAFakeTokenForLocalStubRunsOnly_xxxxx` exactly. For the real walkthrough, check what
you set with `dotnet user-secrets list --project src/Assistant.Worker`.

**Nothing arrives within 30 seconds.** Confirm the worker log reached
`Application started. Press Ctrl+C to shut down.` — if it stopped earlier, the database step
failed and the scheduler never started. Confirm the seeded row actually has `status = 1` and
a past `due_at`; a row with `status = 0` is rejected outright by the check constraint and
never gets inserted. If the worker is running and the row looks right, give it the rest of
the 30-second tick window before assuming something is broken.

**A seeded title with `<` or `&` gets a 400 from Telegram (or from the stub).** Should not
happen — `TelegramNotifier` escapes `&`, `<` and `>` before every send (F7). If you see this,
the escaping regressed; it is not expected behaviour in the scheduler.

**`ConfigurationErrorsException: TelegramSettings.OwnerChatId is missing or zero.`** The owner
chat id is unset or was left as a placeholder. Set it with
`dotnet user-secrets set "TelegramSettings:OwnerChatId" "<your-chat-id>" --project src/Assistant.Worker`.

**`ConfigurationErrorsException: DatabaseSettings.ConnectionString is missing or empty.`** No
connection string is configured — see "Give the worker a connection string" above and create
`src/Assistant.Worker/appsettings.Development.json`.
