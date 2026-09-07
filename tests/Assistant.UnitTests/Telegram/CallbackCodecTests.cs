using Assistant.Impl.Telegram;

namespace Assistant.UnitTests.Telegram;

/// <summary>
/// Test class for <see cref="CallbackCodec"/>.
/// </summary>
public sealed class CallbackCodecTests
{
    /// <summary>
    /// When a known task id is encoded with no argument
    /// Then the exact three-segment wire string is produced.
    /// </summary>
    [Fact]
    public void Encode_KnownTaskId_ProducesTheExpectedString()
    {
        // Act
        var data = CallbackCodec.Encode("done", Guid.Empty);

        // Assert
        Assert.Equal("v1:done:AAAAAAAAAAAAAAAAAAAAAA==", data);
    }

    /// <summary>
    /// When a known task id and argument are encoded
    /// Then the exact four-segment wire string is produced.
    /// </summary>
    [Fact]
    public void Encode_KnownTaskIdAndArgument_ProducesTheExpectedString()
    {
        // Act
        var data = CallbackCodec.Encode("schedule", Guid.Empty, "+1h");

        // Assert
        Assert.Equal("v1:schedule:AAAAAAAAAAAAAAAAAAAAAA==:+1h", data);
    }

    /// <summary>
    /// When a string is encoded for a task with no argument
    /// And that same string is decoded
    /// Then the original action and task id are recovered
    /// And the argument is empty.
    /// </summary>
    [Fact]
    public void TryDecode_WellFormedThreeSegmentString_RecoversTheActionAndTaskIdWithAnEmptyArgument()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var data = CallbackCodec.Encode("done", taskId);

        // Act
        var decoded = CallbackCodec.TryDecode(data, out var action, out var recoveredId, out var argument);

        // Assert
        Assert.True(decoded);
        Assert.Equal("done", action);
        Assert.Equal(taskId, recoveredId);
        Assert.Equal(string.Empty, argument);
    }

    /// <summary>
    /// When a string is encoded for a task with an argument
    /// And that same string is decoded
    /// Then the original action, task id and argument are all recovered.
    /// </summary>
    [Fact]
    public void TryDecode_WellFormedFourSegmentString_RecoversTheActionTaskIdAndArgument()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var data = CallbackCodec.Encode("schedule", taskId, "+1h");

        // Act
        var decoded = CallbackCodec.TryDecode(data, out var action, out var recoveredId, out var argument);

        // Assert
        Assert.True(decoded);
        Assert.Equal("schedule", action);
        Assert.Equal(taskId, recoveredId);
        Assert.Equal("+1h", argument);
    }

    /// <summary>
    /// When a string does not match the v1:&lt;action&gt;:&lt;base64-id&gt;[:&lt;arg&gt;] shape
    /// Then it is not decoded.
    /// </summary>
    [Theory]
    [InlineData("garbage")]
    [InlineData("v1:done")]
    [InlineData("v2:done:AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("v1:done:not-valid-base64!!")]
    [InlineData("v1:done:AAAA")]
    [InlineData("v1:done:AAAAAAAAAAAAAAAAAAAAAA==:1h:extra")]
    public void TryDecode_MalformedOrUnsupportedStrings_Fails(string data)
    {
        // Act
        var decoded = CallbackCodec.TryDecode(data, out var action, out var taskId, out var argument);

        // Assert
        Assert.False(decoded);
        Assert.Equal(string.Empty, action);
        Assert.Equal(Guid.Empty, taskId);
        Assert.Equal(string.Empty, argument);
    }
}
