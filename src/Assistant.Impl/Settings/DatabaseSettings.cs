using System.Configuration;
using Assistant.Interfaces;

namespace Assistant.Impl.Settings;

/// <summary>
/// Configuration for the assistant's PostgreSQL database.
/// </summary>
public sealed class DatabaseSettings : IValidatableConfig
{
    /// <summary>
    /// A Npgsql connection string for the assistant database.
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <inheritdoc/>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(DatabaseSettings)}.{nameof(ConnectionString)} is missing or empty.");
        }
    }
}
