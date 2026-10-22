using ADM.Contracts;
using ADM.Domain;

namespace ADM.Conversation.Tests;

public sealed class ConversationContractTests
{
    [Fact]
    public void Skip_and_cancel_are_separate_intents()
    {
        var skip = new PilotIntent(PilotIntentKind.SkipService, ServiceKind.Catering);
        var cancel = new PilotIntent(PilotIntentKind.CancelService, ServiceKind.Catering);
        Assert.NotEqual(skip, cancel);
    }

    [Fact]
    public void Contracts_expose_no_pushback_control()
    {
        Assert.DoesNotContain(Enum.GetNames<ServiceKind>(), name => name.Contains("Pushback", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Enum.GetNames<GsxAction>(), name => name.Contains("Pushback", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Enum.GetNames<PilotIntentKind>(), name => name.Contains("Pushback", StringComparison.OrdinalIgnoreCase));
    }
}
