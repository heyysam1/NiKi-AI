namespace NikiAI.Core.Voice;

/// <summary>
/// Current state of the voice session.
/// Synchronized directly with character state and UI indicators.
/// </summary>
public enum VoiceSessionState
{
    /// <summary>
    /// No voice interaction active.
    /// </summary>
    Idle,

    /// <summary>
    /// Capturing microphone audio input from user.
    /// </summary>
    Listening,

    /// <summary>
    /// Transcribing speech or awaiting AI response generation.
    /// </summary>
    Thinking,

    /// <summary>
    /// Synthesizing and playing spoken audio response.
    /// </summary>
    Speaking,

    /// <summary>
    /// Waiting for user follow-up or permission approval.
    /// </summary>
    Waiting,

    /// <summary>
    /// Voice session encountered an error.
    /// </summary>
    Error,

    /// <summary>
    /// Voice interaction completed successfully.
    /// </summary>
    Completed
}

/// <summary>
/// Interaction mode for voice capture.
/// </summary>
public enum VoiceInputMode
{
    /// <summary>
    /// Hold hotkey or button while speaking, release to transcribe.
    /// </summary>
    PushToTalk,

    /// <summary>
    /// Click/hotkey once to start listening, click/hotkey again to stop.
    /// </summary>
    Toggle
}

/// <summary>
/// Result from speech-to-text recognition.
/// </summary>
public record SpeechRecognitionResult(
    bool Success,
    string TranscribedText,
    float Confidence = 1.0f,
    string? ErrorMessage = null,
    bool IsFinal = true);

/// <summary>
/// Result from a voice command execution through the assistant pipeline.
/// </summary>
public record VoiceCommandResult(
    bool Success,
    string Prompt,
    string ResponseText,
    TimeSpan Duration,
    string? ErrorMessage = null);
