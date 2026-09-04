using Assistant.Contracts;

namespace Assistant.UnitTests.Contracts;

/// <summary>
/// Test class for <see cref="TaskActions"/>.
/// </summary>
public sealed class TaskActionsTests
{
    /// <summary>
    /// When every declared action's key is compared against the others
    /// Then no two keys are equal.
    /// </summary>
    [Fact]
    public void All_EveryDeclaredKey_IsUnique()
    {
        // Act
        var keys = TaskActions.All.Select(d => d.Key).ToList();

        // Assert
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    /// <summary>
    /// When every declared action's key is inspected
    /// Then none contains a colon.
    /// </summary>
    [Fact]
    public void All_EveryDeclaredKey_ContainsNoColon()
    {
        // Assert
        Assert.All(TaskActions.All, d => Assert.DoesNotContain(":", d.Key));
    }
}
