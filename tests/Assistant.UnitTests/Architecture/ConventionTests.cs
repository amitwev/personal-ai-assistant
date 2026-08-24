using System.Reflection;

namespace Assistant.UnitTests.Architecture;

/// <summary>
/// Test class for the project's structural conventions.
/// </summary>
public class ConventionTests
{
    private static Assembly LoadProject(string name) =>
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, $"{name}.dll"));

    private static Assembly ModelsAssembly => LoadProject("Assistant.Models");
    private static Assembly InterfacesAssembly => LoadProject("Assistant.Interfaces");
    private static Assembly ContractsAssembly => LoadProject("Assistant.Contracts");

    /// <summary>
    /// When a public class in Assistant.Models declares a method other than a property accessor
    /// Then the build fails.
    /// </summary>
    /// <remarks>
    /// Models are POCOs. TaskService is the single writer and the only place invariants live, so a
    /// method on a model is behaviour in the one place the design says cannot hold any.
    /// </remarks>
    [Fact]
    public void Models_declare_no_methods_beyond_property_accessors()
    {
        var offenders = ModelsAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsPublic: true })
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Models are POCOs; behaviour belongs in TaskService. Offenders: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// When a repository interface declares a method returning IQueryable
    /// Then the build fails.
    /// </summary>
    /// <remarks>
    /// IQueryable leaks EF Core through the abstraction. Repository methods are named by intent
    /// instead, so each one can be backed by an index built for it.
    /// </remarks>
    [Fact]
    public void No_repository_method_returns_IQueryable()
    {
        var offenders = InterfacesAssembly.GetTypes()
            .Where(t => t.IsInterface && t.Name.EndsWith("Repository", StringComparison.Ordinal))
            .SelectMany(t => t.GetMethods())
            .Where(m => m.ReturnType.Name.StartsWith("IQueryable", StringComparison.Ordinal))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "IQueryable leaks EF Core through the interface; return IReadOnlyList. Offenders: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// When Assistant.Contracts declares a public interface
    /// Then the build fails.
    /// </summary>
    /// <remarks>
    /// Contracts holds request and response types; every interface in the system lives in
    /// Assistant.Interfaces.
    /// </remarks>
    [Fact]
    public void Contracts_declares_no_interfaces()
    {
        var offenders = ContractsAssembly.GetTypes()
            .Where(t => t is { IsInterface: true, IsPublic: true })
            .Select(t => t.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Contracts holds request/response types; interfaces belong in Assistant.Interfaces. Offenders: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// When Assistant.Interfaces declares a concrete public class
    /// Then the build fails.
    /// </summary>
    /// <remarks>
    /// That assembly holds abstractions only, so an implementation cannot arrive through the
    /// dependency every other project takes on it.
    /// </remarks>
    [Fact]
    public void Interfaces_declares_no_concrete_public_classes()
    {
        var offenders = InterfacesAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsPublic: true, IsAbstract: false })
            .Select(t => t.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Assistant.Interfaces holds abstractions only. Offenders: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// When a type in Models, Contracts, or Interfaces is named Task or TaskStatus
    /// Then the build fails.
    /// </summary>
    /// <remarks>
    /// Both collide with System.Threading.Tasks and produce ambiguous references in every async
    /// file, which is why the model is ReminderTask and the enum is ReminderStatus.
    /// </remarks>
    [Fact]
    public void No_type_is_named_Task_and_no_enum_is_named_TaskStatus()
    {
        var offenders = new[] { ModelsAssembly, ContractsAssembly, InterfacesAssembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name is "Task" or "TaskStatus")
            .Select(t => $"{t.Assembly.GetName().Name}.{t.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "These collide with System.Threading.Tasks. Use ReminderTask and ReminderStatus. Offenders: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// When a public enum in Models or Contracts does not declare Unknown first, with value zero
    /// Then the build fails.
    /// </summary>
    /// <remarks>
    /// Reserving zero for Unknown means default(T) is never a meaningful value, so a field nobody
    /// set cannot be mistaken for a deliberate choice.
    /// </remarks>
    [Fact]
    public void Public_enums_start_with_Unknown_equal_to_zero()
    {
        var offenders = new[] { ModelsAssembly, ContractsAssembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsEnum: true, IsPublic: true })
            .Where(t =>
            {
                var namesByValue = t.GetEnumNames()
                    .Zip(t.GetEnumValues().Cast<object>(), (name, value) => (name, value: Convert.ToInt64(value)))
                    .OrderBy(pair => pair.value)
                    .ToList();

                return namesByValue.Count == 0
                    || namesByValue[0].name != "Unknown"
                    || namesByValue[0].value != 0;
            })
            .Select(t => t.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Every public enum must declare Unknown = 0 as its first member, so default(T) is "
            + "never a meaningful value. Offenders: " + string.Join(", ", offenders));
    }
}
