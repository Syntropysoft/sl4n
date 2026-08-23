namespace Sl4n;

/// <summary>
/// Thrown when sl4n's configuration cannot be interpreted unambiguously. Raised while building the
/// services — at host startup, never from the logging path, where the promise is that logging does
/// not throw. A configuration this class rejects has no safe default: guessing one would ship a
/// wrong answer quietly, which for a compliance window is worse than not starting.
/// </summary>
public sealed class Sl4nConfigurationException : Exception
{
    /// <param name="message">What is ambiguous, and what to declare instead.</param>
    public Sl4nConfigurationException(string message) : base(message) { }
}
