using System.Reflection;
using NetArchTest.Rules;

namespace Assistant.UnitTests.Architecture;

/// <summary>
/// Test class for the project's dependency-direction rules.
/// </summary>
public class DependencyRuleTests
{
    private static Assembly LoadProject(string name) =>
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, $"{name}.dll"));

    private static Assembly ModelsAssembly => LoadProject("Assistant.Models");
    private static Assembly InterfacesAssembly => LoadProject("Assistant.Interfaces");

    /// <summary>
    /// When a type in Assistant.Models depends on the given infrastructure library
    /// Then the build fails.
    /// </summary>
    /// <param name="forbidden">The assembly name a model type must not reference.</param>
    /// <remarks>
    /// Models are mapped to tables and carried across every boundary, so a dependency here would
    /// pull persistence or transport concerns into the one assembly everything else references.
    /// </remarks>
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Npgsql")]
    [InlineData("Telegram.Bot")]
    public void Models_do_not_depend_on_persistence_libraries(string forbidden)
    {
        var result = Types.InAssembly(ModelsAssembly)
            .ShouldNot().HaveDependencyOn(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Models must not depend on {forbidden}. Offenders: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// When a type in Assistant.Interfaces depends on the given infrastructure library
    /// Then the build fails.
    /// </summary>
    /// <param name="forbidden">The assembly name an abstraction must not reference.</param>
    /// <remarks>
    /// An abstraction that names its implementation's library is not an abstraction. Refit appears
    /// in this list before the project uses it, so the rule is in place when the first HTTP client
    /// arrives.
    /// </remarks>
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Npgsql")]
    [InlineData("Telegram.Bot")]
    [InlineData("Refit")]
    public void Interfaces_do_not_depend_on_infrastructure_libraries(string forbidden)
    {
        var result = Types.InAssembly(InterfacesAssembly)
            .ShouldNot().HaveDependencyOn(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Assistant.Interfaces must stay free of {forbidden}. Offenders: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
