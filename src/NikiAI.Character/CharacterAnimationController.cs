using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;

namespace NikiAI.Character;

/// <summary>
/// Controls animation playback, frame advancement, and lifecycle throttling.
/// When companion window is hidden or minimized, animation timer is completely halted to ensure zero CPU/GPU overhead.
/// </summary>
public class CharacterAnimationController : IDisposable
{
    private readonly ICharacterStateMachine _stateMachine;
    private readonly ICharacterRegistry _registry;
    private readonly ILogger<CharacterAnimationController>? _logger;

    private DispatcherTimer? _timer;
    private CharacterAnimation _currentAnimation;
    private int _currentFrameIndex;
    private bool _isThrottled;
    private bool _reducedMotion;
    private bool _isDisposed;

    public event EventHandler<SpriteFrame>? FrameChanged;
    public event EventHandler<CharacterState>? AnimationStateChanged;

    public CharacterProfile ActiveCharacter => _registry.ActiveCharacter;
    public CharacterState CurrentState => _stateMachine.CurrentState;
    public CharacterAnimation CurrentAnimation => _currentAnimation;
    public int CurrentFrameIndex => _currentFrameIndex;
    public bool IsThrottled => _isThrottled;

    public bool ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            _reducedMotion = value;
            if (_reducedMotion)
            {
                _currentFrameIndex = 0;
                NotifyCurrentFrame();
            }
        }
    }

    public CharacterAnimationController(
        ICharacterStateMachine stateMachine,
        ICharacterRegistry registry,
        ILogger<CharacterAnimationController>? logger = null)
    {
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger;

        _currentAnimation = _registry.ActiveCharacter.GetAnimation(_stateMachine.CurrentState);

        _stateMachine.StateChanged += OnStateMachineStateChanged;
        _registry.ActiveCharacterChanged += OnActiveCharacterChanged;

        InitializeTimer();
    }

    private void InitializeTimer()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render);
        _timer.Tick += OnTimerTick;
        UpdateTimerInterval();
        _timer.Start();
    }

    public void SetThrottled(bool throttled)
    {
        if (_isThrottled == throttled) return;

        _isThrottled = throttled;
        if (_isThrottled)
        {
            _timer?.Stop();
            _logger?.LogDebug("Animation timer throttled/halted (window hidden or minimized).");
        }
        else
        {
            if (_timer != null && !_timer.IsEnabled && !_isDisposed)
            {
                UpdateTimerInterval();
                _timer.Start();
                NotifyCurrentFrame();
                _logger?.LogDebug("Animation timer resumed.");
            }
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isThrottled || _reducedMotion || _isDisposed) return;

        _currentFrameIndex++;
        NotifyCurrentFrame();
        UpdateTimerInterval();
    }

    private void NotifyCurrentFrame()
    {
        if (_currentAnimation.Frames.Count == 0) return;
        var frame = _currentAnimation.GetFrame(_currentFrameIndex);
        FrameChanged?.Invoke(this, frame);
    }

    private void UpdateTimerInterval()
    {
        if (_timer == null || _currentAnimation.Frames.Count == 0) return;

        var currentFrame = _currentAnimation.GetFrame(_currentFrameIndex);
        var duration = Math.Max(50, currentFrame.DurationMs);
        _timer.Interval = TimeSpan.FromMilliseconds(duration);
    }

    private void OnStateMachineStateChanged(object? sender, CharacterState newState)
    {
        _currentAnimation = _registry.ActiveCharacter.GetAnimation(newState);
        _currentFrameIndex = 0;
        AnimationStateChanged?.Invoke(this, newState);
        NotifyCurrentFrame();
        UpdateTimerInterval();
    }

    private void OnActiveCharacterChanged(object? sender, CharacterProfile newProfile)
    {
        _currentAnimation = newProfile.GetAnimation(_stateMachine.CurrentState);
        _currentFrameIndex = 0;
        NotifyCurrentFrame();
        UpdateTimerInterval();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        _stateMachine.StateChanged -= OnStateMachineStateChanged;
        _registry.ActiveCharacterChanged -= OnActiveCharacterChanged;
    }
}
