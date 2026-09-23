using NikiAI.Core.Voice;

namespace NikiAI.Voice;

/// <summary>
/// Text-fallback speech-to-text service that enables text-mode voice command processing
/// without requiring audio hardware. Also used for automated unit testing.
/// </summary>
public class TextFallbackSpeechToTextService : ISpeechToTextService
{
    private string _preparedText = string.Empty;
    private bool _isListening;

    public bool IsAvailable => true;
    public bool IsListening => _isListening;

    public void SetInputText(string text)
    {
        _preparedText = text;
    }

    public Task StartListeningAsync(Action<string>? onPartialResult = null, CancellationToken cancellationToken = default)
    {
        _isListening = true;
        if (!string.IsNullOrWhiteSpace(_preparedText))
        {
            onPartialResult?.Invoke(_preparedText);
        }
        return Task.CompletedTask;
    }

    public Task<SpeechRecognitionResult> StopListeningAsync(CancellationToken cancellationToken = default)
    {
        _isListening = false;
        var text = _preparedText;
        _preparedText = string.Empty;

        return Task.FromResult(new SpeechRecognitionResult(
            Success: !string.IsNullOrWhiteSpace(text),
            TranscribedText: text,
            Confidence: 1.0f));
    }

    public Task CancelListeningAsync()
    {
        _isListening = false;
        _preparedText = string.Empty;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Text-fallback text-to-speech service that records spoken text in memory
/// without requiring audio hardware.
/// </summary>
public class TextFallbackTextToSpeechService : ITextToSpeechService
{
    public bool IsAvailable => true;
    public bool IsSpeaking { get; private set; }
    public List<string> SpokenUtterances { get; } = new();

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;

        IsSpeaking = true;
        SpokenUtterances.Add(text);
        IsSpeaking = false;
        return Task.CompletedTask;
    }

    public void StopSpeaking()
    {
        IsSpeaking = false;
    }
}
