using NikiAI.Core.Agent;

namespace NikiAI.Agent.Tests;

public class AgentContractsTests
{
    [Fact]
    public void AgentMessage_Contract_StoresRoleAndContent()
    {
        var msg = new AgentMessage("user", "Hello Niki");
        Assert.Equal("user", msg.Role);
        Assert.Equal("Hello Niki", msg.Content);
    }
}
