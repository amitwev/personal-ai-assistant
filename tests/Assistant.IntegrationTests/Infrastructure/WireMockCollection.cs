namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>
/// Groups every test class that shares the stub API.
/// </summary>
/// <remarks>
/// Separate from <see cref="PostgresCollection"/> because these tests need no database. A class
/// can belong to only one collection, so the feature that first needs both a database and a stub
/// merges these two definitions into one.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class WireMockCollection : ICollectionFixture<WireMockFixture>
{
    /// <summary>
    /// The collection name to put on test classes that use <see cref="WireMockFixture"/>.
    /// </summary>
    public const string Name = "wiremock";
}
