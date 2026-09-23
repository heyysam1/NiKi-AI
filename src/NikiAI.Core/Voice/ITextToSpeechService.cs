namespace NikiAI.Core.Voice;

/// <summary>
/// Service abstraction for text-to-speech audio synthesis.
/// Supports both native Windows speech synthesis and graceful fallback.
/// </summary>
public interface ITextToSpeechService
{
    /// <summary>
    /// Indicates whether speech synthesis hardware/services are available on the current machine.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Whether audio is currently actively speaking.
    /// </summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Speaks the given text asynchronously.
    /// </summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Immediately interrupts and cancels any active spoken output.
    /// </summary>
    void StopSpeaking();
}
