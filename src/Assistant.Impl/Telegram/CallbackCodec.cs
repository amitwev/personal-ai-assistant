namespace Assistant.Impl.Telegram;

/// <summary>
/// Encodes and decodes the <c>callback_data</c> string carried on an inline button.
/// </summary>
/// <remarks>
/// The wire format is <c>v1:&lt;action&gt;:&lt;base64-id&gt;</c>, per spec 6.4. The version
/// prefix means a button left in chat history from a build that no longer understands its exact
/// format degrades to a polite reply instead of throwing. <see cref="TryDecode"/> only ever reads
/// this exact three-segment shape -- it has no notion yet of the optional trailing
/// <c>:&lt;arg&gt;</c> segment spec 6.4 also describes; nothing produces or consumes one until an
/// argument-taking action arrives.
/// </remarks>
internal static class CallbackCodec
{
    private const string Prefix = "v1";

    /// <summary>
    /// Builds the callback data string for one button.
    /// </summary>
    /// <param name="action">The action's key, matching <c>ITaskAction.Key</c>.</param>
    /// <param name="taskId">The task the button refers to.</param>
    /// <returns>
    /// A string of the form <c>v1:&lt;action&gt;:&lt;base64-id&gt;</c> -- 32 characters for the
    /// four-letter key <c>done</c>, comfortably inside Telegram's 64-byte callback data limit.
    /// </returns>
    public static string Encode(string action, Guid taskId) =>
        $"{Prefix}:{action}:{Convert.ToBase64String(taskId.ToByteArray())}";

    /// <summary>
    /// Attempts to decode a callback data string.
    /// </summary>
    /// <param name="data">The raw string from <c>CallbackQuery.Data</c>.</param>
    /// <param name="action">The decoded action key, or empty when decoding fails.</param>
    /// <param name="taskId">The decoded task identifier, or <see cref="Guid.Empty"/> when decoding fails.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="data"/> is a well-formed
    /// <c>v1:&lt;action&gt;:&lt;base64-id&gt;</c> string; <see langword="false"/> for anything
    /// else, including a different version prefix, a wrong number of segments, or an id segment
    /// that is not valid base64 encoding exactly 16 bytes.
    /// </returns>
    public static bool TryDecode(string data, out string action, out Guid taskId)
    {
        action = string.Empty;
        taskId = Guid.Empty;

        var parts = data.Split(':');

        if (parts.Length != 3 || parts[0] != Prefix)
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length != 16)
        {
            return false;
        }

        action = parts[1];
        taskId = new Guid(bytes);
        return true;
    }
}
