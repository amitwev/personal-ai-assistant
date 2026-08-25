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
    private static Assembly ImplAssembly => LoadProject("Assistant.Impl");

    private const string TaskRepositoryFullName = "Assistant.Interfaces.ITaskRepository";

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

    /// <summary>
    /// When a type in Assistant.Impl other than TaskService references ITaskRepository
    /// Then the build fails.
    /// </summary>
    /// <remarks>
    /// Models are anemic by design (§4.1), so <c>TaskService</c> is the only place the invariants
    /// that govern a task's lifecycle can be enforced (§4.2). A second writer could set one field
    /// of a paired mutation — for example stamping <c>ReminderSentAt</c> without <c>UpdatedAt</c>
    /// — without setting its partner, silently breaking the rule the single writer exists to
    /// protect. NetArchTest's <c>HaveDependencyOn</c> works on namespaces, so this rule is checked
    /// with reflection instead, over constructor parameters and fields, in the style of
    /// <see cref="ConventionTests"/>.
    /// </remarks>
    [Fact]
    public void Only_TaskService_references_ITaskRepository_in_Impl()
    {
        var offenders = ImplAssembly.GetTypes()
            .Where(t => t.IsClass && !BelongsToTaskService(t))
            .Where(ReferencesTaskRepository)
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "TaskService is the only type in Assistant.Impl permitted to reference ITaskRepository. "
            + "Offenders: " + string.Join(", ", offenders));
    }

    private static bool BelongsToTaskService(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current.Name == "TaskService")
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReferencesTaskRepository(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType.FullName == TaskRepositoryFullName)
        || type.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static)
            .Any(f => f.FieldType.FullName == TaskRepositoryFullName);
}
