using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Domain;
using ADM.Tests.Shared;

namespace ADM.EndToEnd.Tests;

public sealed class QuietServiceUpdateTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => OperationalScenario.Now; }
    [Fact]
    public void System_updates_need_observed_progress_are_quiet_and_survive_restore_without_duplicates()
    {
        var events = new OperationalScenario(); var conversation = new LocalConversationEngine(PersonaCatalogue.LoadBundled());
        var runtime = new LocalDispatchSession(events.State, conversation, new Clock());
        runtime.Accept(new EvaluateOperation(events.Meta()));
        var legacy = runtime.Checkpoint.State.Commands.FirstOrDefault(command => command.Service == ServiceKind.Deboarding);
        if (legacy is not null)
        {
            runtime.Accept(new AcknowledgeCommand(events.Meta(EvidenceSource.System, null), legacy.Id, CommandOutcome.Accepted));
            runtime.Accept(new ObserveService(events.Meta(), ServiceKind.Deboarding, ServiceState.Active));
            runtime.Accept(new ObserveService(events.Meta(), ServiceKind.Deboarding, ServiceState.Completed));
        }
        var command = runtime.Checkpoint.State.Commands.First(command => command.Kind == OperationalCommandKind.RequestService && command.Service != ServiceKind.Deboarding);
        var kind = command.Service!.Value;
        runtime.Accept(new AcknowledgeCommand(events.Meta(EvidenceSource.System, null), command.Id, CommandOutcome.Accepted));
        Assert.Empty(runtime.Checkpoint.Messages.Where(message => message.IsSystem));
        var active = runtime.Accept(new ObserveService(events.Meta(), kind, ServiceState.Active));
        var started = Assert.Single(runtime.Checkpoint.Messages, message => message.IsSystem);
        Assert.Equal(kind + " started", started.Text); Assert.Equal("", started.Sender);
        Assert.DoesNotContain(active.Notices, notice => notice.Key.StartsWith("system.service:", StringComparison.Ordinal));
        for (int index = 0; index < 100; index++) runtime.Accept(new ObserveService(events.Meta(), kind, ServiceState.Active));
        Assert.Single(runtime.Checkpoint.Messages.Where(message => message.IsSystem));
        var restored = new LocalDispatchSession(DispatchCheckpointCodec.Deserialize(DispatchCheckpointCodec.Serialize(runtime.Checkpoint)), conversation, new Clock(), false);
        restored.Accept(new ObserveService(events.Meta(), kind, ServiceState.Active));
        Assert.Single(restored.Checkpoint.Messages.Where(message => message.IsSystem));
        restored.Accept(new ObserveService(events.Meta(), kind, ServiceState.Completed));
        Assert.Contains(restored.Checkpoint.Messages, message => message.IsSystem && message.Text == kind + " completed");
        restored.ClearChat(); int offset = restored.Checkpoint.VisibleMessageOffset;
        restored.Accept(new ObserveService(events.Meta(), kind, ServiceState.Completed));
        Assert.Equal(offset, restored.Checkpoint.Messages.Length);
        // A fresh explicit request gets a new identity; a repeated observation of an old cycle does not.
        var checkpoint = restored.Checkpoint;
        var next = command with { Id = Guid.NewGuid(), IntentVersion = command.IntentVersion + 1 };
        checkpoint = checkpoint with
        {
            State = checkpoint.State with
            {
                Commands = checkpoint.State.Commands.Add(next),
                Session = checkpoint.State.Session with
                {
                    Services = checkpoint.State.Session.Services.SetItem(kind, checkpoint.State.Session.Services[kind] with { State = ServiceState.Requested })
                }
            }
        };
        var restarted = new LocalDispatchSession(checkpoint, conversation, new Clock(), false);
        restarted.Accept(new ObserveService(events.Meta(), kind, ServiceState.Active));
        Assert.Equal(2, restarted.Checkpoint.Messages.Count(message => message.IsSystem && message.Text == kind + " started"));
        Assert.Single(restarted.Checkpoint.Messages.Skip(offset));
    }
}
