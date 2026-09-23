using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Agent;
using NikiAI.Core.Voice;

namespace NikiAI.Voice;

/// <summary>
/// Core orchestrator for the Niki AI voice subsystem.
/// Manages speech capture, speech-to-text transcription, AI assistant dispatch,
/// text-to-speech feedback, and state synchronization.
/// </summary>
public class VoiceService : IVoiceService, IDisposable
{
    private readonly ISpeechToTextService _sttService;
    private readonly ITextToSpeechService _ttsService;
    private readonly IAgentProvider? _agentProvider;
    private readonly ILogger<VoiceService>? _logger;
    private VoiceSessionState _state = VoiceSessionState.Idle;
    private CancellationTokenSource? _activeCts;
    private bool _isDisposed;
    private readonly object _stateLock = new();

    public VoiceSessionState State
    {
        get => _state;
        private set
        {
            lock (_stateLock)
            {
                if (_state != value)
                {
                    _state = value;
                    _logger?.LogDebug("VoiceService state changed to: {State}", _state);
                    StateChanged?.Invoke(this, _state);
                }
            }
        }
    }

    public VoiceInputMode InputMode { get; set; } = VoiceInputMode.PushToTalk;
    public bool IsAudioOutputEnabled { get; set; } = true;

    public event EventHandler<VoiceSessionState>? StateChanged;
    public event EventHandler<string>? TranscriptReceived;
    public event EventHandler<VoiceCommandResult>? CommandCompleted;

    public VoiceService(
        ISpeechToTextService sttService,
        ITextToSpeechService ttsService,
        IAgentProvider? agentProvider = null,
        ILogger<VoiceService>? logger = null)
    {
        _sttService = sttService ?? throw new ArgumentNullException(nameof(sttService));
        _ttsService = ttsService ?? throw new ArgumentNullException(nameof(ttsService));
        _agentProvider = agentProvider;
        _logger = logger;
    }

    public async Task StartPushToTalkAsync()
    {
        if (State != VoiceSessionState.Idle && State != VoiceSessionState.Completed && State != VoiceSessionState.Error)
        {
            _logger?.LogWarning("Cannot start Push-to-Talk while state is {State}.", State);
            return;
        }

        _activeCts?.Cancel();
        _activeCts = new CancellationTokenSource();

        State = VoiceSessionState.Listening;

        try
        {
            await _sttService.StartListeningAsync(partialText =>
            {
                TranscriptReceived?.Invoke(this, partialText);
            }, _activeCts.Token);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start speech recognition.");
            State = VoiceSessionState.Error;
            State = VoiceSessionState.Idle;
            throw;
        }
    }

    public async Task<VoiceCommandResult> StopPushToTalkAsync()
    {
        if (State != VoiceSessionState.Listening)
        {
            return new VoiceCommandResult(
                Success: false,
                Prompt: string.Empty,
                ResponseText: string.Empty,
                Duration: TimeSpan.Zero,
                ErrorMessage: $"Cannot stop Push-to-Talk when in state {State}");
        }

        State = VoiceSessionState.Thinking;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var sttResult = await _sttService.StopListeningAsync(_activeCts?.Token ?? CancellationToken.None);
            if (!sttResult.Success || string.IsNullOrWhiteSpace(sttResult.TranscribedText))
            {
                _logger?.LogInformation("No speech recognized or recognition cancelled.");
                State = VoiceSessionState.Idle;
                return new VoiceCommandResult(
                    Success: false,
                    Prompt: string.Empty,
                    ResponseText: "No speech recognized.",
                    Duration: stopwatch.Elapsed,
                    ErrorMessage: sttResult.ErrorMessage ?? "No speech input detected.");
            }

            TranscriptReceived?.Invoke(this, sttResult.TranscribedText);
            return await ProcessTextCommandInternalAsync(sttResult.TranscribedText, stopwatch);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing Push-to-Talk speech recognition result.");
            State = VoiceSessionState.Error;
            State = VoiceSessionState.Idle;

            return new VoiceCommandResult(
                Success: false,
                Prompt: string.Empty,
                ResponseText: string.Empty,
                Duration: stopwatch.Elapsed,
                ErrorMessage: ex.Message);
        }
    }

    public async Task<VoiceCommandResult?> ToggleListeningAsync()
    {
        if (State == VoiceSessionState.Listening)
        {
            return await StopPushToTalkAsync();
        }
        else if (State == VoiceSessionState.Idle || State == VoiceSessionState.Completed || State == VoiceSessionState.Error)
        {
            await StartPushToTalkAsync();
            return null;
        }

        return null;
    }

    public async Task<VoiceCommandResult> ProcessTextCommandAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new VoiceCommandResult(false, string.Empty, string.Empty, TimeSpan.Zero, "Command text cannot be empty.");
        }

        CancelCurrentSession();
        State = VoiceSessionState.Thinking;
        var stopwatch = Stopwatch.StartNew();

        TranscriptReceived?.Invoke(this, text);
        return await ProcessTextCommandInternalAsync(text, stopwatch);
    }

    private async Task<VoiceCommandResult> ProcessTextCommandInternalAsync(string prompt, Stopwatch stopwatch)
    {
        string responseText = string.Empty;

        try
        {
            if (_agentProvider != null)
            {
                try
                {
                    var request = new ChatCompletionRequest(new[] { AgentMessage.User(prompt) });

                    var fullResponse = new System.Text.StringBuilder();
                    await foreach (var chunk in _agentProvider.StreamResponseAsync(request, CancellationToken.None))
                    {
                        fullResponse.Append(chunk);
                    }

                    responseText = fullResponse.ToString().Trim();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Agent provider streaming unavailable for voice command '{Prompt}'. Falling back to local offline response.", prompt);
                }
            }

            if (string.IsNullOrWhiteSpace(responseText))
            {
                responseText = $"I heard: \"{prompt}\". Ready for your next instruction!";
            }

            // Speak audio response if output is enabled
            if (IsAudioOutputEnabled && _ttsService.IsAvailable)
            {
                State = VoiceSessionState.Speaking;
                try
                {
                    await _ttsService.SpeakAsync(responseText, _activeCts?.Token ?? CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "TTS speech failed or was cancelled.");
                }
            }

            State = VoiceSessionState.Completed;
            var result = new VoiceCommandResult(
                Success: true,
                Prompt: prompt,
                ResponseText: responseText,
                Duration: stopwatch.Elapsed);

            CommandCompleted?.Invoke(this, result);
            State = VoiceSessionState.Idle;
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to process voice command '{Prompt}'.", prompt);
            State = VoiceSessionState.Error;
            State = VoiceSessionState.Idle;

            var failure = new VoiceCommandResult(
                Success: false,
                Prompt: prompt,
                ResponseText: string.Empty,
                Duration: stopwatch.Elapsed,
                ErrorMessage: ex.Message);

            CommandCompleted?.Invoke(this, failure);
            return failure;
        }
    }

    public async Task SpeakTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsAudioOutputEnabled || !_ttsService.IsAvailable)
        {
            return;
        }

        CancelCurrentSession();
        State = VoiceSessionState.Speaking;

        try
        {
            await _ttsService.SpeakAsync(text, CancellationToken.None);
        }
        finally
        {
            State = VoiceSessionState.Idle;
        }
    }

    public void CancelCurrentSession()
    {
        _activeCts?.Cancel();
        _activeCts = null;

        _sttService.CancelListeningAsync();
        _ttsService.StopSpeaking();

        State = VoiceSessionState.Idle;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        CancelCurrentSession();

        (_sttService as IDisposable)?.Dispose();
        (_ttsService as IDisposable)?.Dispose();
    }
}
