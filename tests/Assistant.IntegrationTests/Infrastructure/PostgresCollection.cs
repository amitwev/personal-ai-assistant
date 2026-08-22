namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>
/// Groups every test class that shares the Postgres instance.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>
    /// The collection name to put on test classes that use <see cref="PostgresFixture"/>.
    /// </summary>
    public const string Name = "postgres";
}
