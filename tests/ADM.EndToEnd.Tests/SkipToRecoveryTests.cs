using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Domain;
using ADM.Tests.Shared;

namespace ADM.EndToEnd.Tests;

public sealed class SkipToRecoveryTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => OperationalScenario.Now; }

    [Fact]
    public void Typed_shortcut_survives_checkpoint_without_dispatching_until_observed_cancellation()
    {
        var run = new OperationalScenario(); run.Pulse(); run.Start(ServiceKind.Deboarding);
        var conversation = new LocalConversationEngine(PersonaCatalogue.LoadBundled());
        var runtime = new LocalDispatchSession(run.State, conversation, new Clock());
        runtime.Send("Could you skip to refuelling, please?");
        Assert.Equal(ServiceKind.Refuelling, runtime.Checkpoint.State.SkipToTarget);
        var cancel = Assert.Single(runtime.Checkpoint.State.Commands, command => command.Kind == OperationalCommandKind.CancelService);
        var saved = DispatchCheckpointCodec.Deserialize(DispatchCheckpointCodec.Serialize(runtime.Checkpoint));
        var restored = new LocalDispatchSession(saved, conversation, new Clock(), false);
        restored.Accept(new EvaluateOperation(run.Meta()));
        Assert.DoesNotContain(restored.Checkpoint.State.Commands, command => command.Service == ServiceKind.Refuelling);
        restored.Accept(new AcknowledgeCommand(run.Meta(EvidenceSource.System, null), cancel.Id, CommandOutcome.Accepted));
        Assert.DoesNotContain(restored.Checkpoint.State.Commands, command => command.Service == ServiceKind.Refuelling);
        restored.Accept(new ObserveService(run.Meta(), ServiceKind.Deboarding, ServiceState.Cancelled));
        var fuel = Assert.Single(restored.Checkpoint.State.Commands, command => command.Service == ServiceKind.Refuelling);
        Assert.True(OperationalCommandPolicy.CanDispatch(restored.Checkpoint.State, fuel));
    }
}
