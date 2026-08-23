using System.Reflection;
using NetArchTest.Rules;

namespace Assistant.UnitTests.Architecture;

public class DependencyRuleTests
{
    private static Assembly LoadProject(string name) =>
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, $"{name}.dll"));

    private static Assembly ModelsAssembly => LoadProject("Assistant.Models");
    private static Assembly InterfacesAssembly => LoadProject("Assistant.Interfaces");

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
