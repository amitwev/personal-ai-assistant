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
