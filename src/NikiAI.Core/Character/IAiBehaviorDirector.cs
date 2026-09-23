namespace NikiAI.Core.Character;

/// <summary>
/// High-level AI behavior director coordinating context, mood, and capability-aware behavior plans.
/// Operates 100% offline via local heuristics in Phase 15.
/// </summary>
public interface IAiBehaviorDirector
{
    /// <summary>
    /// Evaluates non-sensitive pet context and generates a structured, validated BehaviorPlan.
    /// </summary>
    Task<BehaviorPlan> GeneratePlanAsync(PetContextSnapshot context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an immediate reactive behavior plan for a specific event trigger (e.g. task signal, click).
    /// </summary>
    BehaviorPlan GenerateImmediatePlan(PetContextSnapshot context, string triggerReason);

    /// <summary>
    /// Fired whenever a new behavior plan is generated.
    /// </summary>
    event EventHandler<BehaviorPlan>? PlanGenerated;
}
