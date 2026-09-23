using Microsoft.Extensions.Logging;
using NikiAI.Core.Voice;

namespace NikiAI.Voice;

/// <summary>
/// Controller for managing Push-to-Talk input gestures (key press / release)
/// and click-to-toggle voice activation.
/// </summary>
public class PushToTalkController
{
    private readonly IVoiceService _voiceService;
    private readonly ILogger<PushToTalkController>? _logger;
    private bool _isPressed;
    private readonly object _lock = new();

    public bool IsPressed => _isPressed;

    public PushToTalkController(IVoiceService voiceService, ILogger<PushToTalkController>? logger = null)
    {
        _voiceService = voiceService ?? throw new ArgumentNullException(nameof(voiceService));
        _logger = logger;
    }

    /// <summary>
    /// Invoked when the Push-to-Talk key or button is pressed down.
    /// </summary>
    public async Task OnKeyDownAsync()
    {
        lock (_lock)
        {
            if (_isPressed) return; // Prevent key-repeat
            _isPressed = true;
        }

        _logger?.LogDebug("Push-to-Talk key pressed down.");
        if (_voiceService.InputMode == VoiceInputMode.PushToTalk)
        {
            await _voiceService.StartPushToTalkAsync();
        }
        else
        {
            await _voiceService.ToggleListeningAsync();
        }
    }

    /// <summary>
    /// Invoked when the Push-to-Talk key or button is released.
    /// </summary>
    public async Task<VoiceCommandResult?> OnKeyUpAsync()
    {
        lock (_lock)
        {
            if (!_isPressed) return null;
            _isPressed = false;
        }

        _logger?.LogDebug("Push-to-Talk key released.");
        if (_voiceService.InputMode == VoiceInputMode.PushToTalk)
        {
            return await _voiceService.StopPushToTalkAsync();
        }

        return null;
    }

    /// <summary>
    /// Invoked when clicking the microphone button to toggle listening.
    /// </summary>
    public async Task<VoiceCommandResult?> OnButtonClickedAsync()
    {
        return await _voiceService.ToggleListeningAsync();
    }
}
