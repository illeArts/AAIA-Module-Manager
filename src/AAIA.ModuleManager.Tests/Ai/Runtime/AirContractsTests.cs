using AAIA.Air.Contracts;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class AirContractsTests
{
    [Fact]
    public void AiMessage_ProvidesStableDefaults()
    {
        var message = new AiMessage
        {
            Sender = "architect-session",
            Receiver = "reviewer-session",
            Subject = "Review",
            CorrelationId = "task-42"
        };

        Assert.Equal(12, message.Id.Length);
        Assert.Equal(AiMessagePriority.Normal, message.Priority);
        Assert.Equal("task-42", message.CorrelationId);
        Assert.True(message.TimestampUtc <= DateTime.UtcNow);
    }
}
