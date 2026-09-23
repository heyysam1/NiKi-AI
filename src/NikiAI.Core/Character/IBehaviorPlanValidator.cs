namespace NikiAI.Core.Character;

/// <summary>
/// Contract for validating proposed behavior plans against the character's physical and anatomical capabilities.
/// Applies safe substitutions for unsupported primitives.
/// </summary>
public interface IBehaviorPlanValidator
{
    /// <summary>
    /// Validates all steps in the proposed behavior plan against character capabilities.
    /// Replaces unsupported primitives with compatible fallbacks or safe idles.
    /// </summary>
    BehaviorPlan ValidateAndAdapt(BehaviorPlan plan, CharacterIdentityProfile profile);

    /// <summary>
    /// Validates an individual motion primitive against character capabilities.
    /// Replaces unsupported primitives with compatible fallbacks.
    /// </summary>
    MotionPrimitive ValidatePrimitive(MotionPrimitive primitive, CharacterIdentityProfile profile);
}
