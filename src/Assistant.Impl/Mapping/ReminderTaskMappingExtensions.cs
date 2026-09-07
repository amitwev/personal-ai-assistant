using System.Globalization;
using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.Models;

namespace Assistant.Impl.Mapping;

/// <summary>
/// Builds and projects <see cref="ReminderTask"/> instances at the two genuinely external
/// boundaries a task crosses: a captured request coming in, and a rendered reply going out.
/// </summary>
public static class ReminderTaskMappingExtensions
{
    private const string DueTimeFormat = "dddd d MMMM yyyy, HH:mm";

    /// <summary>
    /// Builds a new, unsaved task from a captured creation request.
    /// </summary>
    /// <param name="request">The captured request, carrying the task's title.</param>
    /// <param name="dueAtUtc">
    /// The task's due instant in UTC, already resolved from the request's local-time text, or
    /// <see langword="null"/> when the request gave no time.
    /// </param>
    /// <param name="now">
    /// The instant to stamp as both <see cref="ReminderTask.CreatedAt"/> and
    /// <see cref="ReminderTask.UpdatedAt"/>.
    /// </param>
    /// <returns>
    /// A new <see cref="ReminderTask"/> with a freshly generated identifier and
    /// <see cref="ReminderStatus.Pending"/> status, not yet persisted.
    /// </returns>
    public static ReminderTask ToModel(
        this CreateTaskRequest request, DateTimeOffset? dueAtUtc, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Title = request.Title,
        Status = ReminderStatus.Pending,
        DueAt = dueAtUtc,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>
    /// Renders the message text a task's own reminder message should carry.
    /// </summary>
    /// <param name="task">The task to render.</param>
    /// <param name="clock">Converts a stored due instant back to the configured local zone.</param>
    /// <returns>
    /// "<c>{title} -- due {local due time}.</c>" when <see cref="ReminderTask.DueAt"/> is set, or
    /// "<c>{title} -- saved with no reminder.</c>" otherwise.
    /// </returns>
    /// <remarks>
    /// The shared shape both <c>MessageHandler</c> and <c>CallbackRouter</c> render, so the two
    /// can never silently drift onto two different sentences for the same task state.
    /// </remarks>
    public static string ToMessageText(this ReminderTask task, ILocalTimeResolver clock) =>
        task.DueAt is { } dueAt
            ? $"{task.Title} -- due {clock.ToLocal(dueAt).ToString(DueTimeFormat, CultureInfo.InvariantCulture)}."
            : $"{task.Title} -- saved with no reminder.";
}
