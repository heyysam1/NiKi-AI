using NikiAI.Core.Character;

namespace NikiAI.Character;

/// <summary>
/// State machine managing character animations and states independently from agent execution.
/// Supports state transitions, transient reactions with automatic return, and state change notifications.
/// </summary>
public class CharacterStateMachine : ICharacterStateMachine, IDisposable
{
    private CancellationTokenSource? _reactionCts;
    private readonly object _lock = new();
    private bool _isDisposed;

    private CharacterState _baseState = CharacterState.Idle;

    public CharacterState CurrentState { get; private set; } = CharacterState.Idle;

    public event EventHandler<CharacterState>? StateChanged;

    public void SetState(CharacterState newState)
    {
        lock (_lock)
        {
            if (_isDisposed) return;

            // Cancel any active transient reaction when an explicit state is set
            _reactionCts?.Cancel();
            _reactionCts?.Dispose();
            _reactionCts = null;

            _baseState = newState;

            if (CurrentState != newState)
            {
                CurrentState = newState;
                StateChanged?.Invoke(this, newState);
            }
        }
    }

    public void TriggerReaction(CharacterState reactionState, TimeSpan duration, CharacterState? returnState = null)
    {
        lock (_lock)
        {
            if (_isDisposed) return;

            var targetReturnState = returnState ?? _baseState;

            _reactionCts?.Cancel();
            _reactionCts?.Dispose();
            _reactionCts = new CancellationTokenSource();
            var token = _reactionCts.Token;

            CurrentState = reactionState;
            StateChanged?.Invoke(this, reactionState);

            Task.Delay(duration, token).ContinueWith(t =>
            {
                if (!t.IsCanceled && !_isDisposed)
                {
                    lock (_lock)
                    {
                        if (!_reactionCts?.IsCancellationRequested ?? false)
                        {
                            CurrentState = targetReturnState;
                            StateChanged?.Invoke(this, targetReturnState);
                        }
                    }
                }
            }, TaskScheduler.Default);
        }
    }

    public void TransitionTo(CharacterState newState) => SetState(newState);

    public void Reset() => SetState(CharacterState.Idle);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _reactionCts?.Cancel();
            _reactionCts?.Dispose();
            _reactionCts = null;
        }
    }
}
