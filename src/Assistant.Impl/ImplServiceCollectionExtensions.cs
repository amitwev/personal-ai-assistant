using Assistant.Impl.Scheduling;
using Assistant.Impl.Services;
using Assistant.Impl.Services.Jobs;
using Assistant.Impl.Settings;
using Assistant.Impl.Telegram;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

namespace Assistant.Impl;

/// <summary>
/// Registers the assistant's outbound channels and domain services.
/// </summary>
public static class ImplServiceCollectionExtensions
{
    /// <summary>
    /// Registers Telegram as the assistant's notifier.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <param name="settings">
    /// Validated Telegram configuration. Read it with <c>IConfiguration.Read</c> so a missing
    /// value stops the host here, while it is composing, rather than at first delivery.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAssistantTelegram(
        this IServiceCollection services, TelegramSettings settings)
    {
        var client = new TelegramBotClient(
            new TelegramBotClientOptions(settings.BotToken, settings.BaseUrl));

        services.AddSingleton(settings);
        services.AddSingleton<ITelegramBotClient>(client);
        services.AddSingleton<INotifier, TelegramNotifier>();
        return services;
    }

    /// <summary>
    /// Registers the assistant's domain services.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAssistantServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ITaskService, TaskService>();
        return services;
    }

    /// <summary>
    /// Registers the scheduler loop and every job it runs.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAssistantScheduler(this IServiceCollection services)
    {
        services.AddSingleton<IScheduledJob, DueReminderJob>();
        services.AddHostedService<ReminderScheduler>();
        return services;
    }

    /// <summary>
    /// Registers the inbound Telegram listener.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddAssistantTelegram</c> for the client and the owner's chat id, and
    /// <c>AddAssistantServices</c> for the <see cref="TimeProvider"/> the failure backoff uses.
    /// </remarks>
    public static IServiceCollection AddAssistantListener(this IServiceCollection services)
    {
        services.AddSingleton<ITelegramUpdateHandler, MessageHandler>();
        services.AddHostedService<TelegramListener>();
        return services;
    }
}
