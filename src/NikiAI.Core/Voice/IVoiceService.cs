namespace NikiAI.Core.Voice;

/// <summary>
/// High-level voice subsystem orchestrator coordinating Push-to-Talk, Speech-to-Text,
/// assistant interaction, Text-to-Speech output, and character state synchronization.
/// </summary>
public interface IVoiceService
{
    /// <summary>
    /// Current state of the voice session.
    /// </summary>
    VoiceSessionState State { get; }

    /// <summary>
    /// Active voice input mode (Push-to-Talk or Toggle).
    /// </summary>
    VoiceInputMode InputMode { get; set; }

    /// <summary>
    /// Whether spoken audio response (TTS) is enabled.
    /// </summary>
    bool IsAudioOutputEnabled { get; set; }

    /// <summary>
    /// Begins a Push-to-Talk listening window.
    /// </summary>
    Task StartPushToTalkAsync();

    /// <summary>
    /// Releases the Push-to-Talk listening window and processes the captured speech.
    /// </summary>
    Task<VoiceCommandResult> StopPushToTalkAsync();

    /// <summary>
    /// Toggles listening on or off. If off, starts listening. If on, stops listening and processes.
    /// </summary>
    Task<VoiceCommandResult?> ToggleListeningAsync();

    /// <summary>
    /// Executes a voice command directly via text fallback (bypassing microphone input).
    /// </summary>
    Task<VoiceCommandResult> ProcessTextCommandAsync(string text);

    /// <summary>
    /// Synthesizes and plays the provided text if audio output is enabled.
    /// </summary>
    Task SpeakTextAsync(string text);

    /// <summary>
    /// Cancels any active listening or speaking turn immediately.
    /// </summary>
    void CancelCurrentSession();

    /// <summary>
    /// Fired when the voice session state changes.
    /// </summary>
    event EventHandler<VoiceSessionState>? StateChanged;

    /// <summary>
    /// Fired when partial or final speech transcript is received.
    /// </summary>
    event EventHandler<string>? TranscriptReceived;

    /// <summary>
    /// Fired when a voice command execution completes.
    /// </summary>
    event EventHandler<VoiceCommandResult>? CommandCompleted;
}
