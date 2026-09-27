using NikiAI.Automation;

namespace NikiAI.Automation.Tests;

public class WindowsAutomationServiceTests
{
    [Fact]
    public void WindowsAutomationService_IsAvailable_ReturnsTrueOnWindows()
    {
        var service = new WindowsAutomationService();
        Assert.Equal(OperatingSystem.IsWindows(), service.IsAvailable);
    }
}
