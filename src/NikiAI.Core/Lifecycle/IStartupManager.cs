namespace NikiAI.Core.Lifecycle;

/// <summary>
/// Abstraction for managing Windows Startup registry/shortcut registration.
/// Enables testability and real Windows autorun management.
/// </summary>
public interface IStartupManager
{
    /// <summary>
    /// Checks whether Niki AI is currently configured to start automatically with Windows.
    /// </summary>
    bool IsStartupEnabled();

    /// <summary>
    /// Enables or disables automatic startup with Windows.
    /// </summary>
    /// <param name="enabled">True to enable auto-start, false to remove.</param>
    void SetStartupEnabled(bool enabled);
}
