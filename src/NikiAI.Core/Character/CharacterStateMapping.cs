using NikiAI.Core.Tasks;

namespace NikiAI.Core.Character;

/// <summary>
/// Architectural decoupling mapper between agent/task lifecycle and visual character states.
/// Implements mappings specified in 04_ARCHITECTURE.md.
/// </summary>
public static class CharacterStateMapping
{
    /// <summary>
    /// Maps an AgentTaskStatus to the appropriate visual CharacterState.
    /// </summary>
    public static CharacterState FromTaskStatus(AgentTaskStatus status) => status switch
    {
        AgentTaskStatus.Running => CharacterState.Working,
        AgentTaskStatus.Waiting => CharacterState.Listening,
        AgentTaskStatus.NeedsApproval => CharacterState.WaitingForApproval,
        AgentTaskStatus.Completed => CharacterState.Happy,
        AgentTaskStatus.Failed => CharacterState.Error,
        AgentTaskStatus.Cancelled => CharacterState.Idle,
        AgentTaskStatus.Pending => CharacterState.Thinking,
        AgentTaskStatus.Draft => CharacterState.Idle,
        _ => CharacterState.Idle
    };
}
