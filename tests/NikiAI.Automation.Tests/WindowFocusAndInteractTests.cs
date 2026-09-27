using System.Text.Json;
using NikiAI.Automation;
using NikiAI.Core.Automation;
using NikiAI.Core.Tools;
using NikiAI.Tools;

namespace NikiAI.Automation.Tests;

public class WindowFocusAndInteractTests : IClassFixture<TestWindowFixture>
{
    private readonly IWindowsAutomationService _automationService = new WindowsAutomationService();
    private readonly TestWindowFixture _fixture;

    public WindowFocusAndInteractTests(TestWindowFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WindowFocusTool_InvalidHwnd_ReturnsFailure()
    {
        var tool = new WindowFocusTool(_automationService);
        var call = new ToolCall("c_foc_inv", "window_focus", JsonSerializer.Serialize(new { hwnd = "0x0" }), DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);
        Assert.False(result.IsSuccess);
        Assert.Contains("valid window handle", result.ErrorMessage);
    }

    [Fact]
    public async Task WindowFocusTool_ValidRunningApp_FocusesSuccessfully()
    {
        var tool = new WindowFocusTool(_automationService);
        var call = new ToolCall("c_foc_val", "window_focus", JsonSerializer.Serialize(new { hwnd = $"0x{_fixture.Hwnd:X}" }), DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);
        Assert.True(result.IsSuccess);

        using var doc = JsonDocument.Parse(result.OutputJson!);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task WindowUiInteractTool_InspectRunningApp_ReturnsElements()
    {
        var tool = new WindowUiInteractTool(_automationService);
        var call = new ToolCall("c_ui_insp", "window_ui_interact", JsonSerializer.Serialize(new
        {
            hwnd = $"0x{_fixture.Hwnd:X}",
            action = "inspect",
            max_depth = 1
        }), DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);
        Assert.True(result.IsSuccess);

        using var doc = JsonDocument.Parse(result.OutputJson!);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("elements", out _));
    }

    [Fact]
    public async Task WindowUiInteractTool_InvokeNonExistentElement_ReturnsFailureGracefully()
    {
        var tool = new WindowUiInteractTool(_automationService);
        var call = new ToolCall("c_ui_inv_fail", "window_ui_interact", JsonSerializer.Serialize(new
        {
            hwnd = $"0x{_fixture.Hwnd:X}",
            action = "invoke",
            element_id_or_name = "DefinitelyNonExistentButton_12345"
        }), DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);
        Assert.False(result.IsSuccess);
        Assert.Contains("Could not find or invoke", result.ErrorMessage);
    }
}
