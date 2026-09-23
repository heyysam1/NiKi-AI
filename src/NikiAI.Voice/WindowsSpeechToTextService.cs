using System.Speech.Recognition;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Voice;

namespace NikiAI.Voice;

/// <summary>
/// Windows SAPI speech-to-text recognition service.
/// Gracefully falls back to unavailable state if no audio input device is present.
/// </summary>
public class WindowsSpeechToTextService : ISpeechToTextService, IDisposable
{
    private readonly ILogger<WindowsSpeechToTextService>? _logger;
    private SpeechRecognitionEngine? _recognizer;
    private Action<string>? _onPartialResult;
    private string _lastRecognizedText = string.Empty;
    private float _lastConfidence = 0f;
    private bool _isListening;
    private bool _isDisposed;
    private readonly object _lock = new();

    public bool IsAvailable { get; private set; }

    public WindowsSpeechToTextService(ILogger<WindowsSpeechToTextService>? logger = null)
    {
        _logger = logger;
        InitializeRecognizer();
    }

    private void InitializeRecognizer()
    {
        try
        {
            var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            if (recognizers.Count == 0)
            {
                _logger?.LogWarning("No installed speech recognizers found on this system.");
                IsAvailable = false;
                return;
            }

            _recognizer = new SpeechRecognitionEngine();
            _recognizer.LoadGrammar(new DictationGrammar());

            _recognizer.SpeechHypothesized += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Result?.Text))
                {
                    _onPartialResult?.Invoke(e.Result.Text);
                }
            };

            _recognizer.SpeechRecognized += (s, e) =>
            {
                if (e.Result != null)
                {
                    _lastRecognizedText = e.Result.Text;
                    _lastConfidence = e.Result.Confidence;
                    _logger?.LogDebug("Speech recognized: '{Text}' (Confidence: {Confidence})", _lastRecognizedText, _lastConfidence);
                }
            };

            _recognizer.SpeechRecognitionRejected += (s, e) =>
            {
                _logger?.LogDebug("Speech recognition rejected.");
            };

            IsAvailable = true;
            _logger?.LogInformation("Windows Speech Recognition initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initialize Windows Speech Recognition. STT will fall back to text mode.");
            IsAvailable = false;
            _recognizer?.Dispose();
            _recognizer = null;
        }
    }

    public Task StartListeningAsync(Action<string>? onPartialResult = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!IsAvailable || _recognizer == null)
            {
                throw new InvalidOperationException("Speech recognition is not available on this system.");
            }

            if (_isListening) return Task.CompletedTask;

            _onPartialResult = onPartialResult;
            _lastRecognizedText = string.Empty;
            _lastConfidence = 0f;

            try
            {
                _recognizer.SetInputToDefaultAudioDevice();
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                _isListening = true;
                _logger?.LogInformation("Windows speech recognition started.");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to start listening on default audio device.");
                IsAvailable = false;
                throw new InvalidOperationException("Failed to bind audio input device: " + ex.Message, ex);
            }
        }

        return Task.CompletedTask;
    }

    public Task<SpeechRecognitionResult> StopListeningAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_isListening || _recognizer == null)
            {
                return Task.FromResult(new SpeechRecognitionResult(
                    Success: !string.IsNullOrWhiteSpace(_lastRecognizedText),
                    TranscribedText: _lastRecognizedText,
                    Confidence: _lastConfidence));
            }

            try
            {
                _recognizer.RecognizeAsyncStop();
                _isListening = false;
                _logger?.LogInformation("Windows speech recognition stopped.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error while stopping speech recognizer.");
                _isListening = false;
            }

            return Task.FromResult(new SpeechRecognitionResult(
                Success: !string.IsNullOrWhiteSpace(_lastRecognizedText),
                TranscribedText: _lastRecognizedText,
                Confidence: _lastConfidence > 0 ? _lastConfidence : 1.0f));
        }
    }

    public Task CancelListeningAsync()
    {
        lock (_lock)
        {
            if (_isListening && _recognizer != null)
            {
                try
                {
                    _recognizer.RecognizeAsyncCancel();
                }
                catch { }
                _isListening = false;
            }
            _lastRecognizedText = string.Empty;
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_recognizer != null)
        {
            try
            {
                if (_isListening)
                {
                    _recognizer.RecognizeAsyncCancel();
                }
                _recognizer.Dispose();
            }
            catch { }
            _recognizer = null;
        }
    }
}
