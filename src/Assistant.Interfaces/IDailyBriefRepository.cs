namespace Assistant.Interfaces;

/// <summary>
/// Persistence for the record of which days the brief has been sent.
/// </summary>
public interface IDailyBriefRepository
{
    /// <summary>
    /// Attempts to claim a date for the daily brief.
    /// </summary>
    /// <param name="briefDate">The local date to claim.</param>
    /// <param name="nowUtc">The current instant, recorded against the claim.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the caller has exclusively claimed the date and should send
    /// the brief; <see langword="false"/> when it was already claimed. The claim is atomic, so
    /// two concurrent callers cannot both receive <see langword="true"/>.
    /// </returns>
    Task<bool> TryClaimAsync(DateOnly briefDate, DateTimeOffset nowUtc, CancellationToken ct);
}
