using System.Runtime.InteropServices;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Music Control Widget: Controls Windows desktop media playback.
/// Uses standard Win32 virtual media key events without introducing external audio/streaming services.
/// Correctly represents valid "No media active" / "Idle" empty state when no media is playing.
/// </summary>
public class MusicControlWidget : BaseWidget
{
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xCD;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private bool _isPlaying;

    public override string Id => "music_control";
    public override string Title => "Music Control";
    public override WidgetCategory Category => WidgetCategory.Media;
    public override string IconGlyph => "🎵";

    public MusicControlWidget()
    {
        ActionLabel = "Play/Pause";
        PrimaryDisplayValue = "No media active";
        SecondaryDisplayValue = "System Audio";
        PresentationState = WidgetPresentationState.Empty;
    }

    public override Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isPlaying)
        {
            PrimaryDisplayValue = "No media active";
            SecondaryDisplayValue = "System Audio";
            PresentationState = WidgetPresentationState.Empty;
        }
        else
        {
            PrimaryDisplayValue = "Playing";
            SecondaryDisplayValue = "Active Media Session";
            PresentationState = WidgetPresentationState.Active;
        }
        return Task.CompletedTask;
    }

    public override Task ExecuteActionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Dispatch standard Windows Play/Pause virtual key
        try
        {
            keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            _isPlaying = !_isPlaying;
            if (_isPlaying)
            {
                PrimaryDisplayValue = "Playing";
                SecondaryDisplayValue = "Active Media Session";
                PresentationState = WidgetPresentationState.Active;
            }
            else
            {
                PrimaryDisplayValue = "Paused";
                SecondaryDisplayValue = "System Audio";
                PresentationState = WidgetPresentationState.Empty;
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            PresentationState = WidgetPresentationState.Error;
        }

        return Task.CompletedTask;
    }
}
