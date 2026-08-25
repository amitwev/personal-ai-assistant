using Assistant.Impl;
using Assistant.Impl.Configuration;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Assistant.Repository;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAssistantTelegram(builder.Configuration.Read<TelegramSettings>());

// Diagnose the Telegram token before the database is wired, so this command still works
// with no connection string configured at all.
if (args.Contains("send-test-message"))
{
    using var probe = builder.Build();
    var notifier = probe.Services.GetRequiredService<INotifier>();
    await notifier.SendAsync("Assistant is configured and can reach you.", CancellationToken.None);
    return;
}

builder.Services.AddAssistantRepository(
    builder.Configuration.Read<DatabaseSettings>().ConnectionString);
builder.Services.AddAssistantServices();
builder.Services.AddAssistantScheduler();

var host = builder.Build();
await host.Services.MigrateAssistantDatabaseAsync();
host.Run();
