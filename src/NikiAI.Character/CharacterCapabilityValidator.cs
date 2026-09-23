using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;

namespace NikiAI.Character;

/// <summary>
/// Validates proposed behavior plans against the active character's anatomical capabilities and personality.
/// Replaces unsupported motion primitives with safe, identity-compatible substitutions.
/// </summary>
public class CharacterCapabilityValidator : IBehaviorPlanValidator
{
    private readonly ILogger<CharacterCapabilityValidator>? _logger;

    public CharacterCapabilityValidator(ILogger<CharacterCapabilityValidator>? logger = null)
    {
        _logger = logger;
    }

    public BehaviorPlan ValidateAndAdapt(BehaviorPlan plan, CharacterIdentityProfile profile)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        var validatedSteps = new List<MotionPrimitiveStep>();

        foreach (var step in plan.Sequence.Steps)
        {
            var adaptedPrimitive = ValidatePrimitive(step.Primitive, profile);
            validatedSteps.Add(step with { Primitive = adaptedPrimitive });
        }

        // If all steps were invalid or empty, guarantee at least an Idle step
        if (validatedSteps.Count == 0)
        {
            validatedSteps.Add(new MotionPrimitiveStep(MotionPrimitive.Idle, TimeSpan.FromSeconds(1)));
        }

        var adaptedSequence = new MotionSequence(plan.Sequence.Name, validatedSteps);

        // Validate target mood is within natural emotional range (or fallback to Calm)
        var targetMood = plan.TargetMood;
        if (profile.NaturalEmotionalRange.Count > 0 && !profile.NaturalEmotionalRange.Contains(targetMood))
        {
            _logger?.LogDebug("Mood {Mood} outside emotional range of character {Id}; falling back to Calm.", targetMood, profile.CharacterId);
            targetMood = profile.NaturalEmotionalRange.Contains(PetMood.Calm) ? PetMood.Calm : profile.NaturalEmotionalRange.First();
        }

        return plan with
        {
            Sequence = adaptedSequence,
            TargetMood = targetMood
        };
    }

    public MotionPrimitive ValidatePrimitive(MotionPrimitive primitive, CharacterIdentityProfile profile)
    {
        return primitive switch
        {
            MotionPrimitive.TailWag or MotionPrimitive.TailCurl =>
                profile.HasCapability(CharacterCapability.Tail) ? primitive : MotionPrimitive.ShiftWeight,

            MotionPrimitive.EarTwitch =>
                profile.HasCapability(CharacterCapability.FloppyEars) || profile.HasCapability(CharacterCapability.CatEars)
                    ? primitive
                    : MotionPrimitive.HeadTiltRight,

            MotionPrimitive.PawStep =>
                profile.HasCapability(CharacterCapability.Forepaws) ? primitive : MotionPrimitive.ShiftWeight,

            MotionPrimitive.GearCheck or MotionPrimitive.GuardStance =>
                profile.HasCapability(CharacterCapability.Armor) || profile.HasCapability(CharacterCapability.Visor) || profile.HasCapability(CharacterCapability.GuardStance)
                    ? primitive
                    : MotionPrimitive.Nod,

            MotionPrimitive.HairAdjust =>
                profile.HasCapability(CharacterCapability.HairGroups) ? primitive : MotionPrimitive.HeadTiltLeft,

            MotionPrimitive.JacketAdjust =>
                profile.HasCapability(CharacterCapability.Jacket) ? primitive : MotionPrimitive.ShiftWeight,

            MotionPrimitive.CapAdjust =>
                profile.HasCapability(CharacterCapability.Cap) ? primitive : MotionPrimitive.Nod,

            MotionPrimitive.FistPump =>
                profile.HasCapability(CharacterCapability.FistPump) || profile.HasCapability(CharacterCapability.Hands)
                    ? primitive
                    : MotionPrimitive.Nod,

            MotionPrimitive.Bounce =>
                profile.HasCapability(CharacterCapability.Bounce) || profile.HasCapability(CharacterCapability.Legs)
                    ? primitive
                    : MotionPrimitive.Nod,

            MotionPrimitive.Stretch =>
                profile.HasCapability(CharacterCapability.Stretch) || profile.HasCapability(CharacterCapability.Torso)
                    ? primitive
                    : MotionPrimitive.ShiftWeight,

            _ => primitive
        };
    }
}
