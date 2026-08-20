# personal-ai-assistant

Tasks get captured and then forgotten. Nothing resurfaces them at the right
moment. This is a Telegram bot that fixes the second half of that problem.

You send it a message:

> call the bank tomorrow at 10

It replies with the task and three buttons, then messages you at 10:00 the
next morning. Tap **Done** and it is gone — no app, no tokens spent, one tap.
Every morning it sends a brief of what is due and what is overdue.

Self-hosted on a €5 VPS. Your data never leaves your machine.

## Quickstart

1. Create a bot with [@BotFather](https://t.me/BotFather) and copy the token
2. Get your numeric user ID from [@userinfobot](https://t.me/userinfobot)
3. `cp .env.example .env` and fill in the values
4. `docker compose up -d`

## Deliberate limitations

- **Single user.** There are no accounts and no tenancy: one whitelisted
  Telegram ID, and every other sender is ignored. This is the point, not a
  gap — nobody operates a server that can read your tasks.
- **One timezone.** Currently fixed to `Asia/Jerusalem`. Making this
  configurable is a small change and a welcome pull request.

## Contributing

The whole test suite runs with **no credentials at all** — Telegram and the
LLM APIs are stubbed with WireMock and Postgres comes from Docker Compose.
Fork, `dotnet test`, done. See [AGENTS.md](./AGENTS.md) for every command and
[docs/design/](./docs/design/) for why the system is shaped the way it is.

## Licence

MIT.
