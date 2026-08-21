using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// Delivers messages to the user.
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Sends a reminder for a single task, with its action buttons attached.
    /// </summary>
    /// <param name="notification">What to remind the user about.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    /// <exception cref="Exception">
    /// Thrown when delivery fails. Callers must treat a thrown exception as "not delivered" and
    /// must not mark the reminder sent.
    /// </exception>
    Task SendReminderAsync(ReminderNotification notification, CancellationToken ct);

    /// <summary>
    /// Sends a single message covering several overdue tasks at once.
    /// </summary>
    /// <param name="notifications">The overdue tasks to summarise.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    Task SendOverdueSummaryAsync(IReadOnlyList<ReminderNotification> notifications, CancellationToken ct);

    /// <summary>
    /// Sends the daily brief.
    /// </summary>
    /// <param name="brief">What to include.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    Task SendDailyBriefAsync(DailyBriefNotification brief, CancellationToken ct);

    /// <summary>
    /// Sends a plain text message with no buttons.
    /// </summary>
    /// <param name="text">The message body. May contain the supported HTML subset.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    Task SendTextAsync(string text, CancellationToken ct);
}
