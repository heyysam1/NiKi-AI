namespace NikiAI.Core.Voice;

/// <summary>
/// Service abstraction for speech-to-text recognition.
/// Supports both native Windows speech recognition and seamless fallback.
/// </summary>
public interface ISpeechToTextService
{
    /// <summary>
    /// Indicates whether speech recognition hardware/services are available on the current machine.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Begins capturing and transcribing speech.
    /// </summary>
    /// <param name="onPartialResult">Optional callback invoked with partial/hypothesized text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartListeningAsync(Action<string>? onPartialResult = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops listening and returns the finalized transcribed text.
    /// </summary>
    Task<SpeechRecognitionResult> StopListeningAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels listening without producing a final result.
    /// </summary>
    Task CancelListeningAsync();
}
