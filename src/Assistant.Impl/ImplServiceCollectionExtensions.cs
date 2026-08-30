using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Impl.Ai;
using Assistant.Impl.Scheduling;
using Assistant.Impl.Services;
using Assistant.Impl.Services.Jobs;
using Assistant.Impl.Settings;
using Assistant.Impl.Telegram;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Refit;
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

    /// <summary>
    /// Registers the resolver that turns local wall-clock times into instants.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <param name="settings">
    /// Validated time configuration. Read it with <c>IConfiguration.Read</c> so an unknown zone
    /// stops the host here, while it is composing, rather than at the first captured task.
    /// </param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Requires <c>AddAssistantServices</c> for the <see cref="TimeProvider"/> the past and
    /// future guards read.
    /// </remarks>
    public static IServiceCollection AddAssistantTime(
        this IServiceCollection services, TimeSettings settings)
    {
        services.AddSingleton(TimeZoneInfo.FindSystemTimeZoneById(settings.IanaTimeZone));
        services.AddSingleton<ILocalTimeResolver, LocalTimeResolver>();
        return services;
    }

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
        services.AddRefitGeneratedClient<IAiApi>(new RefitSettings
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
}
