using NikiAI.Core.Tools;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Tools.Tests;

public class CreateReminderToolTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_WithDelaySeconds_CalculatesCorrectDueTime()
    {
        var tool = new CreateReminderTool(() => FixedNow);
        var call = new ToolCall("r1", "create_reminder", """
        {
          "message": "Drink water",
          "delay_seconds": 300,
          "title": "Health"
        }
        """, FixedNow);

        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.Contains("Scheduled", result.OutputJson);
        Assert.Contains("2026-09-20T12:05:00", result.OutputJson);
        Assert.Contains("Drink water", result.OutputJson);
    }

    [Fact]
    public async Task ExecuteAsync_WithFutureIsoDueTime_Succeeds()
    {
        var tool = new CreateReminderTool(() => FixedNow);
        var futureTime = FixedNow.AddHours(2).ToString("o");
        var call = new ToolCall("r2", "create_reminder", $$"""
        {
          "message": "Review pull request",
          "due_time": "{{futureTime}}"
        }
        """, FixedNow);

        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.Contains("Review pull request", result.OutputJson);
    }

    [Fact]
    public async Task ExecuteAsync_PastDueTime_ReturnsFailure()
    {
        var tool = new CreateReminderTool(() => FixedNow);
        var pastTime = FixedNow.AddHours(-1).ToString("o");
        var call = new ToolCall("r3", "create_reminder", $$"""
        {
          "message": "Missed event",
          "due_time": "{{pastTime}}"
        }
        """, FixedNow);

        var result = await tool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("must be in the future", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyMessage_ReturnsFailure()
    {
        var tool = new CreateReminderTool(() => FixedNow);
        var call = new ToolCall("r4", "create_reminder", """
        {
          "message": "   "
        }
        """, FixedNow);

        var result = await tool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("cannot be empty", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_ReturnsCancelledFailure()
    {
        var tool = new CreateReminderTool(() => FixedNow);
        var call = new ToolCall("r5", "create_reminder", """{ "message": "Test" }""", FixedNow);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await tool.ExecuteAsync(call, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
