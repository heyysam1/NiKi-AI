using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;

namespace NikiAI.Character;

/// <summary>
/// Controller for varied casual idle behaviors.
/// Evaluates idle duration and dispatches purposeful micro-fidgets so the companion
/// feels alive and responsive without repeating an identical static pose continuously.
/// Fully honors throttling and reduced motion settings.
/// </summary>
public class CasualIdleController : IDisposable
{
    private readonly ICharacterStateMachine _stateMachine;
    private readonly CharacterAnimationController _animationController;
    private readonly CasualIdleBehavior _behavior;
    private readonly IClock _clock;
    private readonly ILogger<CasualIdleController>? _logger;

    private DispatcherTimer? _evaluationTimer;
    private bool _isThrottled;
    private bool _reducedMotion;
    private bool _isDisposed;

    public CasualIdleBehavior Behavior => _behavior;
    public bool IsThrottled => _isThrottled;
    public bool ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            _reducedMotion = value;
            if (_reducedMotion)
            {
                _behavior.RecordActivity(_clock.UtcNow);
            }
        }
    }

    public CasualIdleController(
        ICharacterStateMachine stateMachine,
        CharacterAnimationController animationController,
        IClock? clock = null,
        IRandomSource? random = null,
        ILogger<CasualIdleController>? logger = null)
    {
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _animationController = animationController ?? throw new ArgumentNullException(nameof(animationController));
        _clock = clock ?? new SystemClock();
        _logger = logger;

        _behavior = new CasualIdleBehavior(_clock, random);

        _stateMachine.StateChanged += OnStateMachineStateChanged;

        InitializeTimer();
    }

    private void InitializeTimer()
    {
        _evaluationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _evaluationTimer.Tick += OnEvaluationTick;
        _evaluationTimer.Start();
    }

    private void OnEvaluationTick(object? sender, EventArgs e)
    {
        if (_isThrottled || _reducedMotion || _isDisposed) return;

        EvaluateAndDispatch();
    }

    /// <summary>
    /// Evaluates current idle state against behavior rules.
    /// Public to allow deterministic, non-flaky test execution.
    /// </summary>
    public CasualFidgetAction? EvaluateAndDispatch()
    {
        var now = _clock.UtcNow;
        var action = _behavior.Evaluate(now, _stateMachine.CurrentState, _isThrottled, _reducedMotion);

        if (action != null)
        {
            if (action.State != CharacterState.Idle)
            {
                _logger?.LogDebug("Casual idle fidget triggered: {State} for {Duration}s ({Reason})",
                    action.State, action.Duration.TotalSeconds, action.Reason);

                _stateMachine.TriggerReaction(action.State, action.Duration);
            }
        }

        return action;
    }

    /// <summary>
    /// Notifies the controller that user interaction occurred, resetting the casual idle timer.
    /// </summary>
    public void RecordUserInteraction()
    {
        _behavior.RecordActivity(_clock.UtcNow);
    }

    public void SetThrottled(bool throttled)
    {
        if (_isThrottled == throttled) return;

        _isThrottled = throttled;
        if (_isThrottled)
        {
            _evaluationTimer?.Stop();
            _logger?.LogDebug("Casual idle controller throttled.");
        }
        else
        {
            if (_evaluationTimer != null && !_evaluationTimer.IsEnabled && !_isDisposed)
            {
                _behavior.RecordActivity(_clock.UtcNow);
                _evaluationTimer.Start();
                _logger?.LogDebug("Casual idle controller resumed.");
            }
        }
    }

    private void OnStateMachineStateChanged(object? sender, CharacterState newState)
    {
        if (newState != CharacterState.Idle)
        {
            _behavior.RecordActivity(_clock.UtcNow);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_evaluationTimer != null)
        {
            _evaluationTimer.Stop();
            _evaluationTimer.Tick -= OnEvaluationTick;
            _evaluationTimer = null;
        }

        _stateMachine.StateChanged -= OnStateMachineStateChanged;
    }
}
