using Assistant.Contracts;

namespace Assistant.UnitTests.Contracts;

/// <summary>
/// Test class for <see cref="TaskNavigations"/>.
/// </summary>
public sealed class TaskNavigationsTests
{
    /// <summary>
    /// When every declared navigation's key is compared against the others
    /// Then no two keys are equal.
    /// </summary>
    [Fact]
    public void All_EveryDeclaredKey_IsUnique()
    {
        // Act
        var keys = TaskNavigations.All.Select(d => d.Key).ToList();

        // Assert
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    /// <summary>
    /// When every declared navigation's key is inspected
    /// Then none contains a colon.
    /// </summary>
    [Fact]
    public void All_EveryDeclaredKey_ContainsNoColon()
    {
        // Assert
        Assert.All(TaskNavigations.All, d => Assert.DoesNotContain(":", d.Key));
    }

    /// <summary>
    /// When every declared navigation's key is compared against every declared action's key
    /// Then no key is shared between the two catalogues.
    /// </summary>
    /// <remarks>
    /// <c>CallbackRouter</c> tries an action's key before a navigation's, so a collision would
    /// silently make a navigation unreachable rather than fail loudly -- this is the test that
    /// would catch it.
    /// </remarks>
    [Fact]
    public void All_NoKeyIsSharedWithTaskActions()
    {
        // Act
        var navigationKeys = TaskNavigations.All.Select(d => d.Key).ToHashSet();
        var actionKeys = TaskActions.All.Select(d => d.Key).ToHashSet();

        // Assert
        Assert.Empty(navigationKeys.Intersect(actionKeys));
    }
}
