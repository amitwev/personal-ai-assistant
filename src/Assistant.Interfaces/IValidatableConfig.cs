namespace Assistant.Interfaces;

/// <summary>
/// Settings that must be checked while the application is starting.
/// </summary>
/// <remarks>
/// The configuration binder does not honour <c>required</c> — a missing value binds to null or
/// zero without complaint — so a settings type states its own rules and the host runs them before
/// anything can use a half-populated instance.
/// </remarks>
public interface IValidatableConfig
{
    /// <summary>
    /// Throws when a mandatory value is missing.
    /// </summary>
    void Validate();
}
