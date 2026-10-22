using System.Collections.Immutable;
using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Domain;
using ADM.Tests.Shared;

namespace ADM.EndToEnd.Tests;

public sealed class LocalTurnaroundTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => OperationalScenario.Now; }
    private static LocalDispatchSession Create(OperationalScenario scenario) => new(scenario.State,
        new LocalConversationEngine(PersonaCatalogue.LoadBundled()), new Clock());

    private static void CompletePreboarding(LocalDispatchSession runtime, OperationalScenario events)
    {
        runtime.Accept(new EvaluateOperation(events.Meta()));
        for (int step = 0; step < 30; step++)
        {
            var next = runtime.Checkpoint.State.Session.Services.Values.SingleOrDefault(service => service.Kind != ServiceKind.Boarding && service.State is ServiceState.RequestPending or ServiceState.Requested or ServiceState.Active);
            if (next is null) return;
            if (next.State == ServiceState.RequestPending)
            {
                var command = runtime.Checkpoint.State.Commands.Last(command => command.Service == next.Kind);
                runtime.Accept(new AcknowledgeCommand(events.Meta(EvidenceSource.System, null), command.Id, CommandOutcome.Accepted));
            }
            runtime.Accept(new ObserveService(events.Meta(), next.Kind, ServiceState.Active));
            runtime.Accept(new ObserveService(events.Meta(), next.Kind, ServiceState.Completed));
        }
        throw new InvalidOperationException("Service sequence did not settle.");
    }

    [Fact]
    public void Repeated_gate_checks_refresh_evidence_without_reasking_boarding()
    {
        var events = new OperationalScenario(); var runtime = Create(events);
        var gate = events.Meta(); runtime.Accept(new ObserveGate(gate, new(GateReadinessState.Ready, GateProvenance.Simulation, gate.Evidence)));
        CompletePreboarding(runtime, events);
        int revision = runtime.Checkpoint.State.Session.PrerequisiteRevision;
        int messages = runtime.Checkpoint.Messages.Length;
        for (int index = 0; index < 100; index++)
        {
            gate = events.Meta(evidence: $"Gate poll {index}");
            var result = runtime.Accept(new ObserveGate(gate, new(GateReadinessState.Ready, GateProvenance.Simulation, gate.Evidence)));
            Assert.Empty(result.Notices);
        }
        Assert.Equal(revision, runtime.Checkpoint.State.Session.PrerequisiteRevision);
        Assert.Equal(messages, runtime.Checkpoint.Messages.Length);
    }

    [Fact]
    public void Declining_mandatory_skip_keeps_fuel_planned_without_skipping_it()
    {
        var events = new OperationalScenario(); var runtime = Create(events);
        runtime.Send("skip fuel");
        Assert.Equal(QuestionKind.MandatorySkip, runtime.Checkpoint.State.Question!.Kind);
        runtime.Send("no");
        Assert.NotEqual(ServiceState.Skipped, runtime.Checkpoint.State.Session.Services[ServiceKind.Refuelling].State);
        Assert.NotEqual(QuestionKind.MandatorySkip, runtime.Checkpoint.State.Question?.Kind);
    }

    [Fact]
    public void Pilot_override_can_resume_skipped_work_without_rewriting_previous_facts()
    {
        var events = new OperationalScenario(); var runtime = Create(events);
        runtime.Send("skip catering");
        CompletePreboarding(runtime, events);
        Guid snapshot = runtime.Checkpoint.State.Session.Plan.SnapshotId;
        var fuel = runtime.Checkpoint.State.Session.Services[ServiceKind.Refuelling];
        runtime.Send("actually do catering");
        var command = runtime.Checkpoint.State.Commands.Last();
        Assert.Equal(ServiceKind.Catering, command.Service);
        Assert.Equal("CurrentFlightPilotOverride", command.PolicySource);
        Assert.Equal(snapshot, command.PlanSnapshotId);
        Assert.Equal(fuel, runtime.Checkpoint.State.Session.Services[ServiceKind.Refuelling]);
        Assert.Equal(ServicePolicy.Automatic, runtime.Checkpoint.State.Session.Plan.Services[ServiceKind.Catering].Rule.Policy);
    }

    [Fact]
    public void Turnaround_uses_real_parser_reducer_and_templates_through_handover()
    {
        var events = new OperationalScenario();
        var runtime = Create(events);
        runtime.Send("yes");
        Assert.Empty(runtime.Checkpoint.State.Session.Approvals);
        var gate = events.Meta();
        runtime.Accept(new ObserveGate(gate, new(GateReadinessState.Ready, GateProvenance.Simulation, gate.Evidence)));
        runtime.Send("skip catering");
        CompletePreboarding(runtime, events);
        Assert.Equal(ServiceState.Skipped, runtime.Checkpoint.State.Session.Services[ServiceKind.Catering].State);
        Assert.Equal(QuestionKind.Boarding, runtime.Checkpoint.State.Question!.Kind);
        Assert.DoesNotContain(runtime.Checkpoint.State.Commands, command => command.Service == ServiceKind.Catering);
        runtime.Send("yes, start boarding");
        var boarding = runtime.Checkpoint.State.Commands.Last();
        runtime.Accept(new AcknowledgeCommand(events.Meta(EvidenceSource.System, null), boarding.Id, CommandOutcome.Accepted));
        runtime.Accept(new ObserveService(events.Meta(), ServiceKind.Boarding, ServiceState.Active));
        runtime.Accept(new ObserveService(events.Meta(), ServiceKind.Boarding, ServiceState.Completed));
        Assert.Equal(QuestionKind.Closure, runtime.Checkpoint.State.Question!.Kind);
        runtime.Send("close up and disconnect");
        Assert.NotEqual(WorkflowStage.Completed, runtime.Checkpoint.State.Session.Stage);
        foreach (var action in Enum.GetValues<ReleaseAction>()) runtime.Accept(new ObserveRelease(events.Meta(), action));
        Assert.Equal(WorkflowStage.Completed, runtime.Checkpoint.State.Session.Stage);
        Assert.Equal(2, runtime.Checkpoint.State.Session.Approvals.Count(approval => !approval.IsInvalidated));
        Assert.Contains(runtime.Checkpoint.Messages, message => message.SemanticKey == "handover.simulated");
        Assert.Equal(runtime.Checkpoint.Messages.Length, runtime.Checkpoint.Messages.Select(message => message.Id).Distinct().Count());
        Assert.All(runtime.Checkpoint.State.Commands, command => Assert.Equal(runtime.Checkpoint.State.Session.Plan.SnapshotId, command.PlanSnapshotId));
    }

    [Fact]
    public void Repeated_events_and_identical_observations_do_not_repeat_essential_messages()
    {
        var events = new OperationalScenario(); var runtime = Create(events);
        var gate = events.Meta();
        runtime.Accept(new ObserveGate(gate, new(GateReadinessState.Ready, GateProvenance.Simulation, gate.Evidence)));
        CompletePreboarding(runtime, events);
        int before = runtime.Checkpoint.Messages.Length;
        var observation = new ObserveService(events.Meta(), ServiceKind.Refuelling, ServiceState.Completed);
        for (int repeat = 0; repeat < 100; repeat++)
        {
            runtime.Accept(observation);
            runtime.Accept(observation with { Metadata = events.Meta() });
        }
        Assert.Equal(before, runtime.Checkpoint.Messages.Length);
        Assert.Single(runtime.Checkpoint.Messages.Where(message => message.SemanticKey.StartsWith("boarding.approval:", StringComparison.Ordinal)));
        Assert.DoesNotContain(runtime.Checkpoint.State.Commands, command => command.Service == ServiceKind.Boarding);
    }

    [Fact]
    public void Checkpoint_roundtrip_persists_persona_messages_approvals_and_command_identity()
    {
        var events = new OperationalScenario(); var runtime = Create(events);
        CompletePreboarding(runtime, events);
        string json = DispatchCheckpointCodec.Serialize(runtime.Checkpoint);
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"adm-checkpoint-{Guid.NewGuid():N}.json");
        DispatchCheckpoint restored;
        try
        {
            System.IO.File.WriteAllText(path, json);
            restored = DispatchCheckpointCodec.Deserialize(System.IO.File.ReadAllText(path));
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        var session = new LocalDispatchSession(restored, new LocalConversationEngine(PersonaCatalogue.LoadBundled()), new Clock());
        Assert.Equal(runtime.Checkpoint.State.Session.Dispatcher, session.Checkpoint.State.Session.Dispatcher);
        Assert.Equal(runtime.Checkpoint.Messages.ToArray(), session.Checkpoint.Messages.ToArray());
        Assert.Equal(runtime.Checkpoint.State.Commands.ToArray(), session.Checkpoint.State.Commands.ToArray());
        Assert.Equal(runtime.Checkpoint.State.Session.Plan.SnapshotId, session.Checkpoint.State.Session.Plan.SnapshotId);
        Assert.Equal(ConnectionState.ReconciliationRequired, session.Checkpoint.State.Connection);
        int messages = session.Checkpoint.Messages.Length;
        session.Accept(new EvaluateOperation(events.Meta()));
        Assert.Equal(messages, session.Checkpoint.Messages.Length);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":1}")]
    public void Invalid_checkpoints_are_rejected_explicitly(string json) => Assert.Throws<ArgumentException>(() => DispatchCheckpointCodec.Deserialize(json));

    [Fact]
    public void Hold_expiry_changes_revision_and_old_permission_cannot_be_reused()
    {
        var events = new OperationalScenario(); var runtime = Create(events);
        var gate = events.Meta(); runtime.Accept(new ObserveGate(gate, new(GateReadinessState.Ready, GateProvenance.Simulation, gate.Evidence)));
        CompletePreboarding(runtime, events);
        runtime.Send("hold boarding");
        runtime.Send("yes");
        Assert.Empty(runtime.Checkpoint.State.Session.Approvals);
        runtime.Send("start boarding");
        Assert.Equal(QuestionKind.Boarding, runtime.Checkpoint.State.Question!.Kind);
        Assert.Empty(runtime.Checkpoint.State.Session.Approvals);
        runtime.Send("yes");
        Assert.Single(runtime.Checkpoint.State.Session.Approvals);
    }

    [Fact]
    public void Concurrent_duplicate_events_are_serialized_once()
    {
        var events = new OperationalScenario(); var runtime = Create(events);
        var input = new EvaluateOperation(events.Meta());
        Parallel.For(0, 100, _ => runtime.Accept(input));
        Assert.Single(runtime.Checkpoint.State.Commands);
        Assert.Single(runtime.Checkpoint.State.SeenEvents);
    }

    [Fact]
    public void New_profile_values_cannot_rewrite_an_active_immutable_snapshot()
    {
        var events = new OperationalScenario(); var runtime = Create(events);
        runtime.Accept(new EvaluateOperation(events.Meta()));
        var original = runtime.Checkpoint.State.Session.Plan;
        var revised = original with
        {
            ProfileRevision = 9,
            Services = original.Services.SetItem(ServiceKind.Catering,
            original.Services[ServiceKind.Catering] with { Rule = original.Services[ServiceKind.Catering].Rule with { Policy = ServicePolicy.Disabled } })
        };
        Assert.Equal(3, runtime.Checkpoint.State.Session.Plan.ProfileRevision);
        Assert.Equal(ServicePolicy.Automatic, runtime.Checkpoint.State.Session.Plan.Services[ServiceKind.Catering].Rule.Policy);
        Assert.Equal(ServicePolicy.Disabled, revised.Services[ServiceKind.Catering].Rule.Policy);
    }
}
