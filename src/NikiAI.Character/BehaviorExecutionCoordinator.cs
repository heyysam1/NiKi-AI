using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;

namespace NikiAI.Character;

/// <summary>
/// Orchestrates behavior execution, priority scheduling, cooldown enforcement, and preemption.
/// Resides in the application/service layer and feeds the existing Character Runtime (CharacterStateMachine).
/// Generates temporary visual physics offsets strictly during active behavior steps.
/// </summary>
public class BehaviorExecutionCoordinator : IDisposable
{
    private readonly ICharacterStateMachine _stateMachine;
    private readonly ICharacterRegistry _registry;
    private readonly IBehaviorPlanValidator _validator;
    private readonly IExpressionComposer _expressionComposer;
    private readonly MotionPhysicsSimulator _physicsSimulator;
    private readonly MoodEngine _moodEngine;
    private readonly ILogger<BehaviorExecutionCoordinator>? _logger;

    private readonly object _lock = new();
    private CancellationTokenSource? _activeCts;
    private BehaviorPlan? _activePlan;
    private DateTime _lastSpontaneousTime = DateTime.MinValue;
    private DateTime _lastExpressionTime = DateTime.MinValue;
    private bool _isDisposed;

    public event EventHandler<PhysicsOffset>? VisualOffsetChanged;
    public event EventHandler<RenderableExpression>? ExpressionTriggered;
    public event EventHandler<BehaviorPlan>? PlanStarted;
    public event EventHandler<BehaviorPlan>? PlanCompleted;

    public BehaviorPlan? ActivePlan
    {
        get { lock (_lock) return _activePlan; }
    }

    public bool IsExecuting => ActivePlan != null;

    public bool SubmitPlan(BehaviorPlan plan, bool reducedMotion = false) => EnqueueOrExecute(plan, reducedMotion);

    public BehaviorExecutionCoordinator(
        ICharacterStateMachine stateMachine,
        ICharacterRegistry registry,
        IBehaviorPlanValidator validator,
        IExpressionComposer expressionComposer,
        MotionPhysicsSimulator physicsSimulator,
        MoodEngine moodEngine,
        ILogger<BehaviorExecutionCoordinator>? logger = null)
    {
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _expressionComposer = expressionComposer ?? throw new ArgumentNullException(nameof(expressionComposer));
        _physicsSimulator = physicsSimulator ?? throw new ArgumentNullException(nameof(physicsSimulator));
        _moodEngine = moodEngine ?? throw new ArgumentNullException(nameof(moodEngine));
        _logger = logger;
    }

    /// <summary>
    /// Evaluates priority and cooldown constraints. If accepted, executes the behavior plan.
    /// Higher priority plans preempt interruptible lower priority plans.
    /// </summary>
    public bool EnqueueOrExecute(BehaviorPlan proposedPlan, bool reducedMotion)
    {
        if (proposedPlan == null) return false;

        lock (_lock)
        {
            if (_isDisposed) return false;

            var now = DateTime.UtcNow;

            // Enforce Spontaneous Cooldown (minimum 45 seconds per spec)
            if (proposedPlan.Priority == BehaviorPriority.Spontaneous)
            {
                if (now - _lastSpontaneousTime < proposedPlan.Cooldown && now - _lastSpontaneousTime < TimeSpan.FromSeconds(45))
                {
                    _logger?.LogDebug("Spontaneous behavior rejected due to cooldown ({Remaining:F1}s remaining).",
                        (TimeSpan.FromSeconds(45) - (now - _lastSpontaneousTime)).TotalSeconds);
                    return false;
                }
            }

            // Priority check against currently active plan
            if (_activePlan != null)
            {
                // Lower integer means higher priority (Priority 1 > Priority 6)
                if (proposedPlan.Priority >= _activePlan.Priority)
                {
                    _logger?.LogDebug("Proposed plan {Proposed} rejected: active plan {Active} has equal or higher priority.",
                        proposedPlan.Intent, _activePlan.Intent);
                    return false;
                }

                if (!_activePlan.IsInterruptible)
                {
                    _logger?.LogDebug("Proposed plan {Proposed} rejected: active plan {Active} is non-interruptible.",
                        proposedPlan.Intent, _activePlan.Intent);
                    return false;
                }

                // Preempt active plan
                _logger?.LogInformation("Preempting active plan {Active} for higher priority plan {Proposed}.",
                    _activePlan.Intent, proposedPlan.Intent);
                CancelActivePlanInternal();
            }

            // Do not interrupt active voice session unless plan is UserInteraction / Command
            if (_stateMachine.CurrentState is CharacterState.Listening or CharacterState.Talking)
            {
                if (proposedPlan.Priority > BehaviorPriority.Command)
                {
                    _logger?.LogDebug("Proposed plan {Proposed} rejected: Voice session actively driving character.", proposedPlan.Intent);
                    return false;
                }
            }

            // Validate and adapt plan to active character capabilities
            var identityProfile = _registry.ActiveIdentityProfile;
            var adaptedPlan = _validator.ValidateAndAdapt(proposedPlan, identityProfile);

            if (adaptedPlan.Priority == BehaviorPriority.Spontaneous)
            {
                _lastSpontaneousTime = now;
            }

            _activePlan = adaptedPlan;
            _activeCts = new CancellationTokenSource();
            var token = _activeCts.Token;

            // Update mood
            _moodEngine.TransitionTo(adaptedPlan.TargetMood, adaptedPlan.Intent);

            // Execute asynchronous plan step runner
            _ = RunPlanAsync(adaptedPlan, identityProfile, reducedMotion, token);
            return true;
        }
    }

    /// <summary>
    /// Cancels any currently executing behavior plan and returns to idle.
    /// </summary>
    public void CancelActiveBehavior()
    {
        lock (_lock)
        {
            CancelActivePlanInternal();
            ResetToIdle();
        }
    }

    private void CancelActivePlanInternal()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;
        _activePlan = null;
    }

    private async Task RunPlanAsync(
        BehaviorPlan plan,
        CharacterIdentityProfile profile,
        bool reducedMotion,
        CancellationToken token)
    {
        PlanStarted?.Invoke(this, plan);

        try
        {
            // Trigger character expression if available and cooled down
            if (plan.Expression != null)
            {
                var now = DateTime.UtcNow;
                if (now - _lastExpressionTime >= TimeSpan.FromSeconds(15) || plan.Priority <= BehaviorPriority.TaskAttention)
                {
                    _lastExpressionTime = now;
                    var renderable = _expressionComposer.ComposeExpression(plan.Expression, profile, reducedMotion);
                    if (renderable != null)
                    {
                        ExpressionTriggered?.Invoke(this, renderable);
                    }
                }
            }

            // Step through each primitive in sequence
            foreach (var step in plan.Sequence.Steps)
            {
                token.ThrowIfCancellationRequested();

                if (_stateMachine.CurrentState is not (CharacterState.Listening or CharacterState.Talking))
                {
                    var mappedState = MapPrimitiveToState(step.Primitive);
                    _stateMachine.SetState(mappedState);
                }

                // Run step physics loop strictly over the duration of this step
                var stepDuration = step.Duration;
                var startTime = DateTime.UtcNow;

                while (DateTime.UtcNow - startTime < stepDuration)
                {
                    token.ThrowIfCancellationRequested();

                    var elapsed = DateTime.UtcNow - startTime;
                    var offset = _physicsSimulator.CalculateOffset(step.Primitive, elapsed, stepDuration, reducedMotion);
                    VisualOffsetChanged?.Invoke(this, offset);

                    await Task.Delay(25, token).ConfigureAwait(false);
                }

                // Reset visual offset at step boundary
                VisualOffsetChanged?.Invoke(this, PhysicsOffset.Identity);
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Behavior plan {PlanId} ({Intent}) was cancelled.", plan.PlanId, plan.Intent);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error executing behavior plan {PlanId} ({Intent})", plan.PlanId, plan.Intent);
        }
        finally
        {
            lock (_lock)
            {
                if (_activePlan?.PlanId == plan.PlanId)
                {
                    _activePlan = null;
                    _activeCts?.Dispose();
                    _activeCts = null;
                }
            }

            ResetToIdle();
            PlanCompleted?.Invoke(this, plan);
        }
    }

    private void ResetToIdle()
    {
        // Visual transform strictly resets to identity
        VisualOffsetChanged?.Invoke(this, PhysicsOffset.Identity);

        // CharacterStateMachine cleanly returns to Idle unless voice session is active
        if (_stateMachine.CurrentState != CharacterState.Idle &&
            _stateMachine.CurrentState is not (CharacterState.Listening or CharacterState.Talking))
        {
            _stateMachine.SetState(CharacterState.Idle);
        }
    }

    private static CharacterState MapPrimitiveToState(MotionPrimitive primitive)
    {
        return primitive switch
        {
            MotionPrimitive.Idle => CharacterState.Idle,
            MotionPrimitive.HeadTiltLeft or MotionPrimitive.HeadTiltRight or MotionPrimitive.LookLeft or MotionPrimitive.LookRight or MotionPrimitive.Blink or MotionPrimitive.SlowBlink => CharacterState.Thinking,
            MotionPrimitive.Nod or MotionPrimitive.Smile or MotionPrimitive.Bounce or MotionPrimitive.FistPump or MotionPrimitive.TailWag => CharacterState.Happy,
            MotionPrimitive.Yawn or MotionPrimitive.LieDown => CharacterState.Sleep,
            MotionPrimitive.Sit => CharacterState.Idle,
            _ => CharacterState.Working
        };
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            CancelActivePlanInternal();
            VisualOffsetChanged?.Invoke(this, PhysicsOffset.Identity);
        }
    }
}
