using Assistant.Interfaces;
using Assistant.Models;
using NetArchTest.Rules;

namespace Assistant.UnitTests.Architecture;

public class DependencyRuleTests
{
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Npgsql")]
    public void Models_do_not_depend_on_persistence_libraries(string forbidden)
    {
        var result = Types.InAssembly(typeof(ReminderTask).Assembly)
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
        var result = Types.InAssembly(typeof(IClock).Assembly)
            .ShouldNot().HaveDependencyOn(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Assistant.Interfaces must stay free of {forbidden}. Offenders: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
