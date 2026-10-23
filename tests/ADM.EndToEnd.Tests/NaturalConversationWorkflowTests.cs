using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Domain;
using ADM.Tests.Shared;

namespace ADM.EndToEnd.Tests;

public sealed class NaturalConversationWorkflowTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => OperationalScenario.Now; }
    private static LocalDispatchSession Create(OperationalState state) => new(state, new LocalConversationEngine(PersonaCatalogue.LoadBundled()), new Clock());

    [Fact]
    public void Smalltalk_unknown_messages_and_thanks_leave_the_pending_approval_and_state_untouched()
    {
        var scenario = new OperationalScenario(); scenario.CompletePreboarding();
        var runtime = Create(scenario.State);
        var before = runtime.Checkpoint.State;
        foreach (string text in new[] { "How are you?", "Been busy?", "I'm good thanks", "Thanks", "What is the weather on Mars?", "Please don't start boarding" })
        {
            var result = runtime.Send(text);
            Assert.Same(before, result.State);
            Assert.Empty(result.NewCommands); Assert.Empty(result.Notices);
            Assert.StartsWith("chat.", runtime.Checkpoint.Messages[^1].SemanticKey);
            Assert.DoesNotContain("Please clarify the service", runtime.Checkpoint.Messages[^1].Text);
        }
        Assert.Equal(QuestionKind.Boarding, runtime.Context.Question?.Kind);
        runtime.Send("Could you begin boarding, please?");
        Assert.Equal(GateProvenance.PilotAttested, runtime.Checkpoint.State.Session.Gate.Provenance);
        Assert.Contains(runtime.Checkpoint.State.Commands, command => command.Service == ServiceKind.Boarding);
        Assert.DoesNotContain(runtime.Checkpoint.Messages, message => message.SemanticKey.StartsWith("gate.check", StringComparison.Ordinal));
    }

    [Fact]
    public void Gate_refreshes_do_not_repeat_the_start_invitation_and_polite_approval_still_works()
    {
        var scenario = new OperationalScenario();
        var runtime = Create(scenario.State with { RequiresServiceStartApproval = true });
        runtime.Accept(new EvaluateOperation(scenario.Meta()));
        var gate = scenario.Meta();
        runtime.Accept(new ObserveGate(gate, new(GateReadinessState.Ready, GateProvenance.Simulation, gate.Evidence)));
        Assert.Single(runtime.Checkpoint.Messages, message => message.SemanticKey.StartsWith("services.start.", StringComparison.Ordinal));
        Assert.Null(runtime.Checkpoint.State.ServiceStartApproval);
        runtime.Send("Please can you start the services?");
        Assert.NotNull(runtime.Checkpoint.State.ServiceStartApproval);
        Assert.Single(runtime.Checkpoint.State.Commands);
    }

    [Theory]
    [InlineData(GateReadinessState.Preparing)]
    [InlineData(GateReadinessState.Held)]
    public void Boarding_approval_never_overrides_an_observed_gate_restriction(GateReadinessState gateState)
    {
        var scenario = new OperationalScenario(); scenario.CompletePreboarding();
        var runtime = Create(scenario.State);
        var gate = scenario.Meta();
        runtime.Accept(new ObserveGate(gate, new(gateState, GateProvenance.Simulation, gate.Evidence)));
        runtime.Send("Please start boarding");
        Assert.Empty(runtime.Checkpoint.State.Session.Approvals);
        Assert.DoesNotContain(runtime.Checkpoint.State.Commands, command => command.Service == ServiceKind.Boarding);
        Assert.Equal(gateState, runtime.Checkpoint.State.Session.Gate.State);
    }

    [Theory]
    [InlineData(HoldKind.ManualPilotHold, EvidenceSource.User)]
    [InlineData(HoldKind.RealServiceBlocked, EvidenceSource.Gsx)]
    public void A_hold_arriving_after_the_boarding_prompt_prevents_gate_attestation_and_boarding(HoldKind kind, EvidenceSource source)
    {
        var scenario = new OperationalScenario(); scenario.CompletePreboarding();
        var runtime = Create(scenario.State);
        Assert.Equal(QuestionKind.Boarding, runtime.Context.Question?.Kind);
        runtime.Accept(new ChangeHold(scenario.Meta(source), new(Guid.NewGuid(), kind, "Fixture hold", true, null, null, HoldScope.Boarding)));
        runtime.Send("Please start boarding");
        Assert.Empty(runtime.Checkpoint.State.Session.Approvals);
        Assert.DoesNotContain(runtime.Checkpoint.State.Commands, command => command.Service == ServiceKind.Boarding);
        Assert.NotEqual(GateProvenance.PilotAttested, runtime.Checkpoint.State.Session.Gate.Provenance);
    }
}
