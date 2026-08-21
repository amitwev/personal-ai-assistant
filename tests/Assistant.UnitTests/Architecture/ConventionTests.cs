using System.Reflection;
using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.Models;

namespace Assistant.UnitTests.Architecture;

public class ConventionTests
{
    private static Assembly ModelsAssembly => typeof(ReminderTask).Assembly;
    private static Assembly InterfacesAssembly => typeof(IClock).Assembly;
    private static Assembly ContractsAssembly => typeof(Result).Assembly;

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
