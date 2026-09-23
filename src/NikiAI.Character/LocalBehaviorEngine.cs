using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;

namespace NikiAI.Character;

/// <summary>
/// Deterministic, 100% offline local behavior heuristic engine.
/// Produces structured BehaviorPlan instances from PetContextSnapshot and triggers without network or cloud AI dependencies.
/// </summary>
public class LocalBehaviorEngine
{
    private readonly ILogger<LocalBehaviorEngine>? _logger;

    public LocalBehaviorEngine(ILogger<LocalBehaviorEngine>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates a structured BehaviorPlan for a given trigger reason and pet context.
    /// </summary>
    public BehaviorPlan GeneratePlan(PetContextSnapshot context, CharacterIdentityProfile profile, string triggerReason)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        var planId = Guid.NewGuid().ToString("N");

        return triggerReason switch
        {
            "TaskStarted" => CreateTaskStartedPlan(planId, profile),
            "TaskCompleted" => CreateTaskCompletedPlan(planId, profile),
            "TaskFailed" => CreateTaskFailedPlan(planId, profile),
            "ApprovalRequired" => CreateApprovalRequiredPlan(planId, profile),
            "UserInteraction" => CreateUserInteractionPlan(planId, profile),
            "VoiceListening" => CreateVoiceListeningPlan(planId, profile),
            "VoiceThinking" => CreateVoiceThinkingPlan(planId, profile),
            "VoiceSpeaking" => CreateVoiceSpeakingPlan(planId, profile),
            _ => CreateSpontaneousPlan(planId, profile, context)
        };
    }

    private static BehaviorPlan CreateTaskStartedPlan(string planId, CharacterIdentityProfile profile)
    {
        var steps = new List<MotionPrimitiveStep>
        {
            new(MotionPrimitive.Nod, TimeSpan.FromMilliseconds(400)),
            new(MotionPrimitive.LookRight, TimeSpan.FromMilliseconds(500)),
            new(MotionPrimitive.Idle, TimeSpan.FromMilliseconds(300))
        };

        var expression = new ExpressionIntent(profile.ExpressionTheme, ExpressionSymbol.FocusSpark, ExpressionIntensity.Medium, TimeSpan.FromSeconds(1.2));

        return new BehaviorPlan(
            planId,
            "TaskFocus",
            PetMood.Focused,
            new MotionSequence("TaskFocusSequence", steps),
            expression,
            BehaviorPriority.TaskAttention,
            TimeSpan.FromSeconds(5));
    }

    private static BehaviorPlan CreateTaskCompletedPlan(string planId, CharacterIdentityProfile profile)
    {
        var steps = new List<MotionPrimitiveStep>
        {
            new(profile.SignatureReactionPrimitive, TimeSpan.FromMilliseconds(600)),
            new(MotionPrimitive.Smile, TimeSpan.FromMilliseconds(400)),
            new(MotionPrimitive.Idle, TimeSpan.FromMilliseconds(300))
        };

        var expression = new ExpressionIntent(profile.ExpressionTheme, ExpressionSymbol.StarBurst, ExpressionIntensity.High, TimeSpan.FromSeconds(1.5));

        return new BehaviorPlan(
            planId,
            "Celebration",
            PetMood.Happy,
            new MotionSequence("TaskCompletedSequence", steps),
            expression,
            BehaviorPriority.ContextReaction,
            TimeSpan.FromSeconds(10));
    }

    private static BehaviorPlan CreateTaskFailedPlan(string planId, CharacterIdentityProfile profile)
    {
        var steps = new List<MotionPrimitiveStep>
        {
            new(MotionPrimitive.HeadTiltLeft, TimeSpan.FromMilliseconds(500)),
            new(MotionPrimitive.SlowBlink, TimeSpan.FromMilliseconds(600)),
            new(MotionPrimitive.Idle, TimeSpan.FromMilliseconds(400))
        };

        var expression = new ExpressionIntent(profile.ExpressionTheme, ExpressionSymbol.SweatDrop, ExpressionIntensity.Medium, TimeSpan.FromSeconds(1.5));

        return new BehaviorPlan(
            planId,
            "Disappointment",
            PetMood.Disappointed,
            new MotionSequence("TaskFailedSequence", steps),
            expression,
            BehaviorPriority.TaskAttention,
            TimeSpan.FromSeconds(10));
    }

    private static BehaviorPlan CreateApprovalRequiredPlan(string planId, CharacterIdentityProfile profile)
    {
        var steps = new List<MotionPrimitiveStep>
        {
            new(MotionPrimitive.LeanForward, TimeSpan.FromMilliseconds(500)),
            new(MotionPrimitive.Blink, TimeSpan.FromMilliseconds(400)),
            new(MotionPrimitive.Idle, TimeSpan.FromMilliseconds(300))
        };

        var expression = new ExpressionIntent(profile.ExpressionTheme, ExpressionSymbol.Question, ExpressionIntensity.High, TimeSpan.FromSeconds(2.0));

        return new BehaviorPlan(
            planId,
            "WaitingApproval",
            PetMood.Surprised,
            new MotionSequence("ApprovalRequiredSequence", steps),
            expression,
            BehaviorPriority.TaskAttention,
            TimeSpan.FromSeconds(5));
    }

    private static BehaviorPlan CreateUserInteractionPlan(string planId, CharacterIdentityProfile profile)
    {
        var steps = new List<MotionPrimitiveStep>
        {
            new(profile.SignatureReactionPrimitive, TimeSpan.FromMilliseconds(500)),
            new(MotionPrimitive.Smile, TimeSpan.FromMilliseconds(400)),
            new(MotionPrimitive.Idle, TimeSpan.FromMilliseconds(300))
        };

        var expression = new ExpressionIntent(profile.ExpressionTheme, ExpressionSymbol.Heart, ExpressionIntensity.High, TimeSpan.FromSeconds(1.5));

        return new BehaviorPlan(
            planId,
            "FriendlyInteraction",
            PetMood.Playful,
            new MotionSequence("UserInteractionSequence", steps),
            expression,
            BehaviorPriority.UserInteraction,
            TimeSpan.FromSeconds(2));
    }

    private static BehaviorPlan CreateVoiceListeningPlan(string planId, CharacterIdentityProfile profile)
    {
        var steps = new List<MotionPrimitiveStep>
        {
            new(MotionPrimitive.HeadTiltRight, TimeSpan.FromMilliseconds(400)),
            new(MotionPrimitive.Idle, TimeSpan.FromMilliseconds(400))
        };

        return new BehaviorPlan(
            planId,
            "VoiceListening",
            PetMood.Curious,
            new MotionSequence("VoiceListeningSequence", steps),
            null,
            BehaviorPriority.Command,
            TimeSpan.FromSeconds(2));
    }

    private static BehaviorPlan CreateVoiceThinkingPlan(string planId, CharacterIdentityProfile profile)
    {
        var steps = new List<MotionPrimitiveStep>
        {
            new(MotionPrimitive.LookLeft, TimeSpan.FromMilliseconds(400)),
            new(MotionPrimitive.Blink, TimeSpan.FromMilliseconds(400)),
            new(MotionPrimitive.Idle, TimeSpan.FromMilliseconds(400))
        };

        return new BehaviorPlan(
            planId,
            "VoiceThinking",
            PetMood.Focused,
            new MotionSequence("VoiceThinkingSequence", steps),
            null,
            BehaviorPriority.Command,
            TimeSpan.FromSeconds(2));
    }

    private static BehaviorPlan CreateVoiceSpeakingPlan(string planId, CharacterIdentityProfile profile)
    {
        var steps = new List<MotionPrimitiveStep>
        {
            new(MotionPrimitive.Nod, TimeSpan.FromMilliseconds(400)),
            new(MotionPrimitive.Smile, TimeSpan.FromMilliseconds(400)),
            new(MotionPrimitive.Idle, TimeSpan.FromMilliseconds(300))
        };

        return new BehaviorPlan(
            planId,
            "VoiceSpeaking",
            PetMood.Happy,
            new MotionSequence("VoiceSpeakingSequence", steps),
            null,
            BehaviorPriority.Command,
            TimeSpan.FromSeconds(2));
    }

    private static BehaviorPlan CreateSpontaneousPlan(string planId, CharacterIdentityProfile profile, PetContextSnapshot? context)
    {
        // If session is long (>45m) and inactive, suggest gentle stretch
        if (context != null && context.SessionDuration > TimeSpan.FromMinutes(45) && !context.IsUserActive)
        {
            var tiredSteps = new List<MotionPrimitiveStep>
            {
                new(MotionPrimitive.Yawn, TimeSpan.FromMilliseconds(600)),
                new(MotionPrimitive.Stretch, TimeSpan.FromMilliseconds(800)),
                new(MotionPrimitive.Idle, TimeSpan.FromMilliseconds(500))
            };

            return new BehaviorPlan(
                planId,
                "RestingObservation",
                PetMood.Tired,
                new MotionSequence("LongSessionSequence", tiredSteps),
                null,
                BehaviorPriority.Spontaneous,
                TimeSpan.FromSeconds(60));
        }

        // Standard spontaneous observation
        var steps = new List<MotionPrimitiveStep>
        {
            new(MotionPrimitive.LookLeft, TimeSpan.FromMilliseconds(400)),
            new(MotionPrimitive.LookRight, TimeSpan.FromMilliseconds(400)),
            new(MotionPrimitive.ShiftWeight, TimeSpan.FromMilliseconds(600)),
            new(MotionPrimitive.Idle, TimeSpan.FromMilliseconds(400))
        };

        return new BehaviorPlan(
            planId,
            "SpontaneousObservation",
            PetMood.Curious,
            new MotionSequence("SpontaneousSequence", steps),
            null,
            BehaviorPriority.Spontaneous,
            TimeSpan.FromSeconds(45)); // Minimum 45s cooldown per spec
    }

    /// <summary>
    /// Generates a spontaneous observation plan without requiring an active context snapshot.
    /// </summary>
    public BehaviorPlan GenerateSpontaneousPlan(CharacterIdentityProfile? profile = null)
    {
        var identity = profile ?? new CharacterIdentityProfile(
            "niki",
            "Niki",
            "Human Anime Companion",
            "Primary AI operator.",
            CharacterCapability.HeadTilt | CharacterCapability.Nod | CharacterCapability.WeightShift,
            ExpressionTheme.StandardAnime,
            MotionPrimitive.Nod,
            "Thoughtful nod.",
            new[] { PetMood.Calm, PetMood.Focused, PetMood.Happy });
        var planId = Guid.NewGuid().ToString("N");
        return CreateSpontaneousPlan(planId, identity, null);
    }
}
