using Assistant.Impl.Telegram;

namespace Assistant.UnitTests.Telegram;

/// <summary>
/// Test class for <see cref="CallbackCodec"/>.
/// </summary>
public sealed class CallbackCodecTests
{
    /// <summary>
    /// When a known task id is encoded
    /// Then the exact wire string is produced.
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
    /// When a string is encoded for a task
    /// And that same string is decoded
    /// Then the original action and task id are recovered.
    /// </summary>
    [Fact]
    public void TryDecode_WellFormedString_RecoversTheActionAndTaskId()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var data = CallbackCodec.Encode("done", taskId);

        // Act
        var decoded = CallbackCodec.TryDecode(data, out var action, out var recoveredId);

        // Assert
        Assert.True(decoded);
        Assert.Equal("done", action);
        Assert.Equal(taskId, recoveredId);
    }

    /// <summary>
    /// When a string does not match the v1:&lt;action&gt;:&lt;base64-id&gt; shape
    /// Then it is not decoded.
    /// </summary>
    [Theory]
    [InlineData("garbage")]
    [InlineData("v1:done")]
    [InlineData("v2:done:AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("v1:done:not-valid-base64!!")]
    [InlineData("v1:done:AAAA")]
    [InlineData("v1:done:AAAAAAAAAAAAAAAAAAAAAA==:1h")]
    public void TryDecode_MalformedOrUnsupportedStrings_Fails(string data)
    {
        // Act
        var decoded = CallbackCodec.TryDecode(data, out var action, out var taskId);

        // Assert
        Assert.False(decoded);
        Assert.Equal(string.Empty, action);
        Assert.Equal(Guid.Empty, taskId);
    }
}
