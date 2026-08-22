using Assistant.Interfaces;
using Assistant.Repository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Repository;

/// <summary>
/// Registers the assistant's persistence layer.
/// </summary>
/// <remarks>
/// This is the only public surface of this assembly. Nothing outside it names an Entity
/// Framework type, which keeps the persistence technology replaceable without touching a service.
/// </remarks>
public static class RepositoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database context and the repositories against the given connection string.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <param name="connectionString">A Npgsql connection string for the assistant database.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAssistantRepository(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AssistantDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ITaskRepository, EfTaskRepository>();
        return services;
    }

    /// <summary>
    /// Applies any outstanding database migrations.
    /// </summary>
    /// <param name="provider">A provider from which a scoped context can be resolved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the schema is up to date.</returns>
    /// <remarks>
    /// Called explicitly rather than from <see cref="AddAssistantRepository"/> so a caller
    /// controls when the schema mutation happens instead of it being hidden inside DI setup.
    /// Safe to call when the schema is already current.
    /// </remarks>
    public static async Task MigrateAssistantDatabaseAsync(
        this IServiceProvider provider, CancellationToken ct = default)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssistantDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
