namespace Assistant.Models;

/// <summary>
/// A task the assistant is holding, and the time at which it should remind the user about it.
/// </summary>
/// <remarks>
/// This is a persistence model with no behaviour: every mutation goes through the task service,
/// which is the single writer and the only place the invariants are enforced. All instants are
/// UTC with a zero offset.
/// </remarks>
public sealed class ReminderTask
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Short description of what needs doing.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Current lifecycle state.
    /// </summary>
    public ReminderStatus Status { get; set; }

    /// <summary>
    /// When the task is due, in UTC. Also the instant at which its reminder is delivered.
    /// </summary>
    /// <value><see langword="null"/> for a task with no deadline, which never triggers a reminder.</value>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>
    /// When the reminder for the current <see cref="DueAt"/> was delivered, in UTC.
    /// </summary>
    /// <value>
    /// <see langword="null"/> when delivery is still owed. Snoozing or rescheduling resets this
    /// to <see langword="null"/> so the task fires again.
    /// </value>
    public DateTimeOffset? ReminderSentAt { get; set; }

    /// <summary>
    /// When the task was created, in UTC.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the task was last modified, in UTC.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When the task was completed, in UTC.
    /// </summary>
    /// <value>
    /// <see langword="null"/> until the task is completed. Set together with
    /// <see cref="Status"/> becoming <see cref="ReminderStatus.Completed"/> -- the database
    /// enforces that the two always agree.
    /// </value>
    public DateTimeOffset? CompletedAt { get; set; }
}
