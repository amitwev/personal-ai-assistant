using System.Configuration;
using Assistant.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Assistant.Impl.Configuration;

/// <summary>
/// Reads configuration sections into validated settings objects.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Binds the section named after <typeparamref name="T"/> and validates it.
    /// </summary>
    /// <typeparam name="T">The settings type, which names its own section.</typeparam>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>A populated, validated instance.</returns>
    /// <exception cref="ConfigurationErrorsException">
    /// The section is absent, could not be bound, or a mandatory value is missing.
    /// </exception>
    /// <remarks>
    /// Each of the three checks catches a different failure. An absent section binds to null. A
    /// section holding only an unrelated key still reports <c>Exists()</c> as true, so binding
    /// succeeds and leaves the real values empty. And <c>required</c> is a compile-time contract
    /// the binder goes around, so only <see cref="IValidatableConfig.Validate"/> catches a value
    /// present in shape but missing in fact.
    /// </remarks>
    public static T Read<T>(this IConfiguration configuration)
        where T : IValidatableConfig
    {
        var sectionName = typeof(T).Name;
        var section = configuration.GetSection(sectionName);

        if (!section.Exists())
        {
            throw new ConfigurationErrorsException(
                $"Configuration section '{sectionName}' was not found.");
        }

        var settings = section.Get<T>()
            ?? throw new ConfigurationErrorsException(
                $"Configuration section '{sectionName}' could not be bound to {typeof(T).Name}.");

        settings.Validate();
        return settings;
    }
}
