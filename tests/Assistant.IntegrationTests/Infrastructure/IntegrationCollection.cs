namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>
/// Groups every integration test class, so they share one Postgres instance and one stub API.
/// </summary>
/// <remarks>
/// One collection rather than two. A class can belong to only one, and a job test needs a
/// database and the stub together. xUnit also runs distinct collections in parallel while
/// PostgresFixture.ResetAsync truncates every table, so a second Postgres-touching collection
/// would truncate this one's rows mid-test.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection
    : ICollectionFixture<PostgresFixture>, ICollectionFixture<WireMockFixture>
{
    /// <summary>
    /// The collection name to put on every integration test class.
    /// </summary>
    public const string Name = "integration";
}
