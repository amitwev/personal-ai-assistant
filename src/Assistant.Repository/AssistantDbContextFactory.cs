using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Assistant.Repository;

/// <summary>
/// Supplies a context to the Entity Framework command-line tools at design time.
/// </summary>
/// <remarks>
/// Used only by <c>dotnet ef</c> when generating or applying migrations. The connection string
/// here points at the local compose database and is never used at runtime.
/// </remarks>
internal sealed class AssistantDbContextFactory : IDesignTimeDbContextFactory<AssistantDbContext>
{
    /// <summary>
    /// Creates a context configured against the local compose database.
    /// </summary>
    /// <param name="args">Arguments passed by the tooling. Unused.</param>
    /// <returns>A context ready for migration tooling to operate on.</returns>
    public AssistantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AssistantDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=55432;Database=assistant_test;Username=assistant;Password=assistant")
            .Options;

        return new AssistantDbContext(options);
    }
}
