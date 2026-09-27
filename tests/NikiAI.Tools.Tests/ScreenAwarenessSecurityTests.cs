using System.Drawing;
using System.Text.Json;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Vision;
using NikiAI.Security;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Tools.Tests;

public class ScreenAwarenessSecurityTests
{
    private class TestScreenCaptureService : IScreenCaptureService
    {
        public bool IsAvailable => true;
        public bool ScreenAwarenessEnabled { get; set; } = true;
        public int CaptureCount { get; private set; }
        public byte[] DummyBytes { get; set; } = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // Minimal PNG magic bytes

        public Task<ScreenCaptureResult> CapturePrimaryScreenAsync(CancellationToken cancellationToken = default)
        {
            if (!ScreenAwarenessEnabled)
            {
                return Task.FromResult(ScreenCaptureResult.Failed("Screen capture is disabled by user privacy policy.", "Primary Screen"));
            }
            CaptureCount++;
            return Task.FromResult(ScreenCaptureResult.Succeeded(DummyBytes, 1920, 1080, "Primary Screen"));
        }

        public Task<ScreenCaptureResult> CaptureActiveWindowAsync(CancellationToken cancellationToken = default)
        {
            if (!ScreenAwarenessEnabled)
            {
                return Task.FromResult(ScreenCaptureResult.Failed("Screen capture is disabled by user privacy policy.", "Active Window"));
            }
            CaptureCount++;
            return Task.FromResult(ScreenCaptureResult.Succeeded(DummyBytes, 800, 600, "Active Window"));
        }

        public Task<ScreenCaptureResult> CaptureWindowAsync(nint hWnd, CancellationToken cancellationToken = default)
        {
            if (!ScreenAwarenessEnabled)
            {
                return Task.FromResult(ScreenCaptureResult.Failed("Screen capture is disabled by user privacy policy.", $"Window 0x{hWnd:X}"));
            }
            CaptureCount++;
            return Task.FromResult(ScreenCaptureResult.Succeeded(DummyBytes, 800, 600, $"Window 0x{hWnd:X}"));
        }

        public Task<ScreenCaptureResult> CaptureRegionAsync(Rectangle bounds, CancellationToken cancellationToken = default)
        {
            if (!ScreenAwarenessEnabled)
            {
                return Task.FromResult(ScreenCaptureResult.Failed("Screen capture is disabled by user privacy policy.", "Custom Region"));
            }
            CaptureCount++;
            return Task.FromResult(ScreenCaptureResult.Succeeded(DummyBytes, bounds.Width, bounds.Height, "Custom Region"));
        }
    }

    private class TestVisionProvider : IVisionProvider
    {
        public string ProviderId => "mock-vision";
        public string DisplayName => "Mock Vision";
        public bool IsAvailable => true;
        public VisionAnalysisRequest? LastRequest { get; private set; }

        public Task<VisionAnalysisResult> AnalyzeImageAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(VisionAnalysisResult.Succeeded(
                summary: $"Summary for prompt: {request.Prompt}",
                extractedText: "Extracted Sample Text",
                detectedElements: new[] { "Button Submit", "Text Box" }
            ));
        }
    }

    [Fact]
    public void ToolRiskLevels_MustBeSensitive_RiskLevel2()
    {
        var captureService = new TestScreenCaptureService();
        var visionProvider = new TestVisionProvider();

        var captureTool = new CaptureScreenTool(captureService);
        var analyzeTool = new AnalyzeScreenTool(captureService, visionProvider);

        Assert.Equal(ToolRiskLevel.Sensitive, captureTool.RiskLevel);
        Assert.Equal(ToolRiskLevel.Sensitive, analyzeTool.RiskLevel);
    }

    [Fact]
    public async Task PermissionEngine_WithoutRule_RequiresUserApproval()
    {
        var captureService = new TestScreenCaptureService();
        var captureTool = new CaptureScreenTool(captureService);
        var permEngine = new PermissionEngine();

        var call = new ToolCall("call-1", "capture_screen", "{\"target\":\"primary_screen\"}", DateTimeOffset.UtcNow);
        var eval = await permEngine.EvaluateToolExecutionAsync("task-100", captureTool, call);

        Assert.False(eval.IsAllowed);
        Assert.True(eval.RequiresUserPrompt);
        Assert.NotNull(eval.PromptRequest);
        Assert.Equal("capture_screen", eval.PromptRequest.ToolId);
        Assert.Equal(ToolRiskLevel.Sensitive, eval.PromptRequest.RiskLevel);
        Assert.Equal(0, captureService.CaptureCount);
    }

    [Fact]
    public async Task PermissionEngine_WithAlwaysAllowRule_AutoAllowsSensitiveTool()
    {
        var captureService = new TestScreenCaptureService();
        var captureTool = new CaptureScreenTool(captureService);
        var permEngine = new PermissionEngine();

        var scopeKey = "capture_screen:capture:primary_screen";
        await permEngine.RecordDecisionAsync(scopeKey, "capture_screen", ApprovalDecision.AlwaysAllow, ToolRiskLevel.Sensitive);

        var call = new ToolCall("call-2", "capture_screen", "{\"target\":\"primary_screen\"}", DateTimeOffset.UtcNow);
        var eval = await permEngine.EvaluateToolExecutionAsync("task-101", captureTool, call);

        Assert.True(eval.IsAllowed);
        Assert.False(eval.RequiresUserPrompt);
    }

    [Fact]
    public async Task PrivacyGate_WhenDisabled_CaptureToolFailsClosed()
    {
        var captureService = new TestScreenCaptureService { ScreenAwarenessEnabled = false };
        var captureTool = new CaptureScreenTool(captureService);

        var call = new ToolCall("call-3", "capture_screen", "{\"target\":\"active_window\"}", DateTimeOffset.UtcNow);
        var result = await captureTool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("disabled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, captureService.CaptureCount);
    }

    [Fact]
    public async Task CaptureTool_ReturnsMetadata_DoesNotReturnRawBytes()
    {
        var captureService = new TestScreenCaptureService { ScreenAwarenessEnabled = true };
        var captureTool = new CaptureScreenTool(captureService);

        var call = new ToolCall("call-4", "capture_screen", "{\"target\":\"primary_screen\"}", DateTimeOffset.UtcNow);
        var result = await captureTool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputJson);
        Assert.Equal(1, captureService.CaptureCount);

        using var doc = JsonDocument.Parse(result.OutputJson);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("captured").GetBoolean());
        Assert.Equal(1920, root.GetProperty("width").GetInt32());
        Assert.Equal(1080, root.GetProperty("height").GetInt32());
        Assert.Equal(captureService.DummyBytes.Length, root.GetProperty("byte_length").GetInt32());
        // Verify raw bytes are NOT serialized in JSON
        Assert.False(root.TryGetProperty("image_bytes", out _));
        Assert.False(root.TryGetProperty("bytes", out _));
    }

    [Fact]
    public async Task AnalyzeTool_QuarantinesOutput_InUntrustedContentBlock()
    {
        var captureService = new TestScreenCaptureService { ScreenAwarenessEnabled = true };
        var visionProvider = new TestVisionProvider();
        var analyzeTool = new AnalyzeScreenTool(captureService, visionProvider);

        var call = new ToolCall("call-5", "analyze_screen", "{\"prompt\":\"Find the save button\",\"target\":\"active_window\"}", DateTimeOffset.UtcNow);
        var result = await analyzeTool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputJson);
        Assert.StartsWith("=== UNTRUSTED SCREEN CONTENT START ===", result.OutputJson.Trim());
        Assert.EndsWith("=== UNTRUSTED SCREEN CONTENT END ===", result.OutputJson.Trim());
        Assert.Contains("Summary for prompt: Find the save button", result.OutputJson);
        Assert.Contains("Button Submit", result.OutputJson);
        Assert.Equal(1, captureService.CaptureCount);
    }

    [Fact]
    public async Task AnalyzeTool_WithoutPrompt_FailsValidation()
    {
        var captureService = new TestScreenCaptureService();
        var visionProvider = new TestVisionProvider();
        var analyzeTool = new AnalyzeScreenTool(captureService, visionProvider);

        var call = new ToolCall("call-6", "analyze_screen", "{\"target\":\"active_window\"}", DateTimeOffset.UtcNow);
        var result = await analyzeTool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("prompt", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, captureService.CaptureCount);
    }
}
