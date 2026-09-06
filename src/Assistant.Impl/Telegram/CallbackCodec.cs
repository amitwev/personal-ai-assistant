namespace Assistant.Impl.Telegram;

/// <summary>
/// Encodes and decodes the <c>callback_data</c> string carried on an inline button.
/// </summary>
/// <remarks>
/// The wire format is <c>v1:&lt;action&gt;:&lt;base64-id&gt;[:&lt;arg&gt;]</c>, per spec 6.4. The
/// four-argument <see cref="TryDecode(string,out string,out System.Guid,out string)"/> overload
/// reads the trailing <c>:&lt;arg&gt;</c> segment -- nothing consumes it yet (F11-2's and F11-3's
/// job). The original three-out-parameter overload keeps its exact parameter list, so
/// <c>CallbackRouter</c>'s existing call needs no change, and delegates to the four-argument one
/// so a given wire string decodes the same way regardless of which overload is called.
/// </remarks>
internal static class CallbackCodec
{
    private const string Prefix = "v1";

    /// <summary>
    /// Builds the callback data string for a button with no argument.
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
    /// Builds the callback data string for a button carrying an argument.
    /// </summary>
    /// <param name="action">The action's key, matching <c>ITaskAction.Key</c>.</param>
    /// <param name="taskId">The task the button refers to.</param>
    /// <param name="argument">The argument <see cref="TryDecode(string,out string,out Guid,out string)"/> reads back.</param>
    /// <returns>
    /// A string of the form <c>v1:&lt;action&gt;:&lt;base64-id&gt;:&lt;arg&gt;</c> -- 37 characters
    /// for the key <c>schedule</c> and the argument <c>+1h</c>, comfortably inside Telegram's
    /// 64-byte callback data limit.
    /// </returns>
    public static string Encode(string action, Guid taskId, string argument) =>
        $"{Prefix}:{action}:{Convert.ToBase64String(taskId.ToByteArray())}:{argument}";

    /// <summary>
    /// Attempts to decode a callback data string, without reading its optional argument.
    /// </summary>
    /// <param name="data">The raw string from <c>CallbackQuery.Data</c>.</param>
    /// <param name="action">The decoded action key, or empty when decoding fails.</param>
    /// <param name="taskId">The decoded task identifier, or <see cref="Guid.Empty"/> when decoding fails.</param>
    /// <returns>
    /// <see langword="true"/> for a well-formed three- or four-segment string; delegates to the
    /// four-argument overload, discarding its argument.
    /// </returns>
    public static bool TryDecode(string data, out string action, out Guid taskId) =>
        TryDecode(data, out action, out taskId, out _);

    /// <summary>
    /// Attempts to decode a callback data string, including its optional argument.
    /// </summary>
    /// <param name="data">The raw string from <c>CallbackQuery.Data</c>.</param>
    /// <param name="action">The decoded action key, or empty when decoding fails.</param>
    /// <param name="taskId">The decoded task identifier, or <see cref="Guid.Empty"/> when decoding fails.</param>
    /// <param name="argument">
    /// The decoded fourth segment, or an empty string when <paramref name="data"/> carried only
    /// three segments or decoding failed altogether.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for a well-formed three- or four-segment string with the right
    /// version prefix and a 16-byte base64 id segment; <see langword="false"/> otherwise.
    /// </returns>
    public static bool TryDecode(string data, out string action, out Guid taskId, out string argument)
    {
        action = string.Empty;
        taskId = Guid.Empty;
        argument = string.Empty;

        var parts = data.Split(':');

        if ((parts.Length != 3 && parts.Length != 4) || parts[0] != Prefix)
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
        argument = parts.Length == 4 ? parts[3] : string.Empty;
        return true;
    }
}
