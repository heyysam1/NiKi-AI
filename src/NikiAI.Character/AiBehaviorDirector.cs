using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;
using NikiAI.Core.Lifecycle;
using NikiAI.Core.Voice;

namespace NikiAI.Character;

/// <summary>
/// AI Behavior Director coordinating context ingestion, behavior planning, and execution dispatching.
/// Mandatory Phase 15 runtime component operating 100% offline via LocalBehaviorEngine.
/// Reuses existing IPetContextProvider, ITaskLifecycleSignalHub, and IVoiceService.
/// </summary>
public class AiBehaviorDirector : IAiBehaviorDirector, IDisposable
{
    private readonly IPetContextProvider _contextProvider;
    private readonly ICharacterRegistry _registry;
    private readonly LocalBehaviorEngine _behaviorEngine;
    private readonly BehaviorExecutionCoordinator _coordinator;
    private readonly ITaskLifecycleSignalHub? _signalHub;
    private readonly IVoiceService? _voiceService;
    private readonly MoodEngine? _moodEngine;
    private readonly PersonalityAdaptationService? _personalityAdaptation;
    private readonly ILogger<AiBehaviorDirector>? _logger;

    private bool _reducedMotion;
    private bool _isDisposed;

    public event EventHandler<BehaviorPlan>? PlanGenerated;

    public bool ReducedMotion
    {
        get => _reducedMotion;
        set => _reducedMotion = value;
    }

    public AiBehaviorDirector(
        IPetContextProvider contextProvider,
        ICharacterRegistry registry,
        LocalBehaviorEngine behaviorEngine,
        BehaviorExecutionCoordinator coordinator,
        ITaskLifecycleSignalHub? signalHub = null,
        IVoiceService? voiceService = null,
        MoodEngine? moodEngine = null,
        PersonalityAdaptationService? personalityAdaptation = null,
        ILogger<AiBehaviorDirector>? logger = null)
    {
        _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _behaviorEngine = behaviorEngine ?? throw new ArgumentNullException(nameof(behaviorEngine));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _signalHub = signalHub;
        _voiceService = voiceService;
        _moodEngine = moodEngine;
        _personalityAdaptation = personalityAdaptation;
        _logger = logger;

        SubscribeToSignals();
    }

    private void SubscribeToSignals()
    {
        if (_signalHub != null)
        {
            _signalHub.SignalEmitted += OnTaskLifecycleSignal;
        }

        if (_voiceService != null)
        {
            _voiceService.StateChanged += OnVoiceStateChanged;
        }
    }

    private void OnTaskLifecycleSignal(TaskLifecycleSignal signal)
    {
        if (_isDisposed) return;

        var reason = signal.SignalType switch
        {
            TaskLifecycleSignalType.TaskStarted => "TaskStarted",
            TaskLifecycleSignalType.TaskCompleted => "TaskCompleted",
            TaskLifecycleSignalType.TaskFailed => "TaskFailed",
            TaskLifecycleSignalType.ApprovalRequired => "ApprovalRequired",
            _ => "TaskProgress"
        };

        var context = _contextProvider.GetContextSnapshot();
        var plan = GenerateImmediatePlan(context, reason);
        _coordinator.EnqueueOrExecute(plan, _reducedMotion);
    }

    private void OnVoiceStateChanged(object? sender, VoiceSessionState state)
    {
        if (_isDisposed) return;

        // When a voice session starts, cancel any active spontaneous behavior plan
        if (state == VoiceSessionState.Listening)
        {
            _coordinator.CancelActiveBehavior();
        }

        // Voice state transitions on CharacterStateMachine are authoritatively handled
        // by VoiceStateSynchronizer (Phase 12).
        // AiBehaviorDirector uses IVoiceService as a contextual signal to update pet mood.
        switch (state)
        {
            case VoiceSessionState.Listening:
                _moodEngine?.TransitionTo(PetMood.Curious, "VoiceListening");
                break;
            case VoiceSessionState.Thinking:
                _moodEngine?.TransitionTo(PetMood.Focused, "VoiceThinking");
                break;
            case VoiceSessionState.Speaking:
                _moodEngine?.TransitionTo(PetMood.Playful, "VoiceSpeaking");
                break;
            case VoiceSessionState.Idle:
                _moodEngine?.TransitionTo(PetMood.Calm, "VoiceIdle");
                break;
        }
    }

    public Task<BehaviorPlan> GeneratePlanAsync(PetContextSnapshot context, CancellationToken cancellationToken = default)
    {
        var plan = GenerateImmediatePlan(context, "Spontaneous");
        return Task.FromResult(plan);
    }

    public BehaviorPlan GenerateImmediatePlan(PetContextSnapshot context, string triggerReason)
    {
        var identity = _registry.ActiveIdentityProfile;

        // Record interaction if user clicked/interacted (PersonalityAdaptation handles strict OFF check)
        if (triggerReason == "UserInteraction")
        {
            _personalityAdaptation?.RecordInteraction(identity.CharacterId);
        }

        var plan = _behaviorEngine.GeneratePlan(context, identity, triggerReason);
        _logger?.LogDebug("BehaviorDirector generated plan {PlanId} ({Intent}, Priority: {Priority})",
            plan.PlanId, plan.Intent, plan.Priority);

        PlanGenerated?.Invoke(this, plan);
        return plan;
    }

    public void TriggerUserInteraction()
    {
        var context = _contextProvider.GetContextSnapshot();
        var plan = GenerateImmediatePlan(context, "UserInteraction");
        _coordinator.EnqueueOrExecute(plan, _reducedMotion);
    }

    public void TriggerSpontaneousEvaluation()
    {
        var context = _contextProvider.GetContextSnapshot();
        var plan = GenerateImmediatePlan(context, "Spontaneous");
        _coordinator.EnqueueOrExecute(plan, _reducedMotion);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_signalHub != null)
        {
            _signalHub.SignalEmitted -= OnTaskLifecycleSignal;
        }

        if (_voiceService != null)
        {
            _voiceService.StateChanged -= OnVoiceStateChanged;
        }
    }
}
