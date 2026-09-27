using NikiAI.Core.Tasks;

namespace NikiAI.Core.Tests;

public class TaskStateMachineTests
{
    [Theory]
    [InlineData(AgentTaskStatus.Draft, AgentTaskStatus.Pending)]
    [InlineData(AgentTaskStatus.Draft, AgentTaskStatus.Cancelled)]
    [InlineData(AgentTaskStatus.Pending, AgentTaskStatus.Running)]
    [InlineData(AgentTaskStatus.Pending, AgentTaskStatus.Cancelled)]
    [InlineData(AgentTaskStatus.Running, AgentTaskStatus.Waiting)]
    [InlineData(AgentTaskStatus.Running, AgentTaskStatus.NeedsApproval)]
    [InlineData(AgentTaskStatus.Running, AgentTaskStatus.Completed)]
    [InlineData(AgentTaskStatus.Running, AgentTaskStatus.Failed)]
    [InlineData(AgentTaskStatus.Running, AgentTaskStatus.Cancelled)]
    [InlineData(AgentTaskStatus.Waiting, AgentTaskStatus.Running)]
    [InlineData(AgentTaskStatus.Waiting, AgentTaskStatus.Cancelled)]
    [InlineData(AgentTaskStatus.Waiting, AgentTaskStatus.Failed)]
    [InlineData(AgentTaskStatus.NeedsApproval, AgentTaskStatus.Running)]
    [InlineData(AgentTaskStatus.NeedsApproval, AgentTaskStatus.Cancelled)]
    [InlineData(AgentTaskStatus.NeedsApproval, AgentTaskStatus.Failed)]
    public void ValidTransitions_ShouldBeAllowed(AgentTaskStatus from, AgentTaskStatus to)
    {
        Assert.True(TaskStateMachine.CanTransition(from, to));
        TaskStateMachine.ValidateTransition(from, to); // Should not throw
    }

    [Theory]
    [InlineData(AgentTaskStatus.Draft, AgentTaskStatus.Running)]
    [InlineData(AgentTaskStatus.Draft, AgentTaskStatus.Completed)]
    [InlineData(AgentTaskStatus.Completed, AgentTaskStatus.Running)]
    [InlineData(AgentTaskStatus.Completed, AgentTaskStatus.Pending)]
    [InlineData(AgentTaskStatus.Failed, AgentTaskStatus.Running)]
    [InlineData(AgentTaskStatus.Cancelled, AgentTaskStatus.Running)]
    [InlineData(AgentTaskStatus.Waiting, AgentTaskStatus.Completed)]
    public void InvalidTransitions_ShouldThrowInvalidOperationException(AgentTaskStatus from, AgentTaskStatus to)
    {
        Assert.False(TaskStateMachine.CanTransition(from, to));
        Assert.Throws<InvalidOperationException>(() => TaskStateMachine.ValidateTransition(from, to));
    }

    [Fact]
    public void AgentTask_TransitionTo_UpdatesStatusAndTimestampsCorrectly()
    {
        var task = new AgentTask
        {
            Title = "Test Task",
            NaturalLanguageRequest = "Perform action"
        };

        Assert.Equal(AgentTaskStatus.Draft, task.Status);
        Assert.Null(task.StartedAt);
        Assert.Null(task.CompletedAt);

        task.TransitionTo(AgentTaskStatus.Pending);
        Assert.Equal(AgentTaskStatus.Pending, task.Status);

        task.TransitionTo(AgentTaskStatus.Running);
        Assert.Equal(AgentTaskStatus.Running, task.Status);
        Assert.NotNull(task.StartedAt);
        Assert.Null(task.CompletedAt);

        task.TransitionTo(AgentTaskStatus.Completed);
        Assert.Equal(AgentTaskStatus.Completed, task.Status);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public void ProgressPercentage_ShouldBeTruthfulAndClamped()
    {
        var task = new AgentTask { ProgressPercentage = null };
        Assert.Null(task.ProgressPercentage);

        task.ProgressPercentage = 50;
        Assert.Equal(50, task.ProgressPercentage);

        task.ProgressPercentage = -10;
        Assert.Equal(0, task.ProgressPercentage);

        task.ProgressPercentage = 150;
        Assert.Equal(100, task.ProgressPercentage);

        task.ProgressPercentage = null;
        Assert.Null(task.ProgressPercentage);
    }
}
