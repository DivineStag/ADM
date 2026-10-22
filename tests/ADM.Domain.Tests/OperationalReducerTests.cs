using ADM.Domain;
using ADM.Tests.Shared;

namespace ADM.Domain.Tests;

public sealed class OperationalReducerTests
{
    [Fact]
    public void Late_receipt_from_an_earlier_intent_cannot_change_a_resumed_service()
    {
        var run = new OperationalScenario(additionalRequired: ServiceKind.Deboarding); run.Pulse(); run.Start(ServiceKind.Deboarding);
        var oldCancel = Assert.Single(run.User(PilotDirective.Cancel, ServiceKind.Deboarding).NewCommands);
        run.Apply(new ObserveService(run.Meta(), ServiceKind.Deboarding, ServiceState.Cancelled));
        Assert.DoesNotContain(run.State.Commands, command => command.Service == ServiceKind.Cleaning);
        run.User(PilotDirective.Resume, ServiceKind.Deboarding);
        run.Start(ServiceKind.Deboarding);
        var freshCancel = Assert.Single(run.User(PilotDirective.Cancel, ServiceKind.Deboarding).NewCommands);
        Assert.False(OperationalCommandPolicy.CanDispatch(run.State, oldCancel));
        Assert.True(OperationalCommandPolicy.CanDispatch(run.State, freshCancel));
        run.Apply(new AcknowledgeCommand(run.Meta(EvidenceSource.System, null), oldCancel.Id, CommandOutcome.Rejected));
        Assert.Equal(ServiceState.CancelPending, run.State.Session.Services[ServiceKind.Deboarding].State);
    }

    [Fact]
    public void Queued_boarding_is_revalidated_against_new_holds_before_dispatch()
    {
        var run = new OperationalScenario(); run.GateReady(); run.CompletePreboarding(); run.User(PilotDirective.ApproveBoarding);
        var command = run.State.Commands.Last();
        Assert.True(OperationalCommandPolicy.CanDispatch(run.State, command));
        run.Apply(new ChangeHold(run.Meta(), new(Guid.NewGuid(), HoldKind.SimulatedPassenger, "Passenger hold", true, null, null)));
        Assert.False(OperationalCommandPolicy.CanDispatch(run.State, command));
    }

    [Fact]
    public void Resume_boarding_does_not_clear_unrelated_manual_holds()
    {
        var run = new OperationalScenario(); run.GateReady(); run.CompletePreboarding();
        var unrelated = new OperationalHold(Guid.NewGuid(), HoldKind.ManualPilotHold, "Pilot requested inspection", true, null, null);
        run.Apply(new ChangeHold(run.Meta(EvidenceSource.User), unrelated));
        run.User(PilotDirective.HoldBoarding);
        run.User(PilotDirective.Resume, ServiceKind.Boarding);
        Assert.True(run.State.Session.Holds.Single(hold => hold.Id == unrelated.Id).IsActive);
        Assert.Null(run.State.Question);
        Assert.DoesNotContain(run.State.Commands, command => command.Service == ServiceKind.Boarding);
    }

    [Fact]
    public void Lost_receipt_marks_unknown_and_cannot_be_retried_by_a_pulse()
    {
        var run = new OperationalScenario(); run.Pulse();
        var command = Assert.Single(run.State.Commands);
        run.Apply(new AcknowledgeCommand(run.Meta(EvidenceSource.System, null), command.Id, CommandOutcome.Unknown));
        run.Pulse();
        Assert.Equal(ConnectionState.ReconciliationRequired, run.State.Connection);
        Assert.Equal(ServiceState.Unknown, run.State.Session.Services[ServiceKind.Deboarding].State);
        Assert.Single(run.State.Commands);
    }

    [Fact]
    public void System_receipt_is_not_authoritative_service_completion_evidence()
    {
        var run = new OperationalScenario(); run.Pulse(); run.Start(ServiceKind.Deboarding);
        run.Apply(new ObserveService(run.Meta(EvidenceSource.System), ServiceKind.Deboarding, ServiceState.Completed));
        Assert.Equal(ServiceState.Active, run.State.Session.Services[ServiceKind.Deboarding].State);
        Assert.Single(run.State.Commands);
    }

    [Fact]
    public void Rejected_equipment_action_requires_new_approval_and_manual_fallback()
    {
        var run = new OperationalScenario(); run.BoardAndComplete(); run.User(PilotDirective.ApproveClosure);
        var release = run.State.Commands.Last();
        run.Apply(new AcknowledgeCommand(run.Meta(EvidenceSource.System, null), release.Id, CommandOutcome.Rejected));
        Assert.Equal(QuestionKind.Closure, run.State.Question!.Kind);
        Assert.False(run.State.Releases[ReleaseAction.Doors].IsSatisfied);
        run.User(PilotDirective.ApproveClosure);
        Assert.Equal(QuestionKind.ManualRelease, run.State.Question!.Kind);
        Assert.Equal(ReleaseAction.Doors, run.State.Question.Release);
    }

    [Fact]
    public void Closure_hold_requires_a_new_question_and_a_separate_new_yes()
    {
        var run = new OperationalScenario(); run.BoardAndComplete(); run.User(PilotDirective.HoldClosure);
        Assert.Null(run.State.Question);
        run.User(PilotDirective.ApproveClosure);
        Assert.Equal(QuestionKind.Closure, run.State.Question!.Kind);
        Assert.DoesNotContain(run.State.Commands, command => command.Kind == OperationalCommandKind.ReleaseEquipment);
        run.User(PilotDirective.ApproveClosure);
        Assert.Contains(run.State.Commands, command => command.Kind == OperationalCommandKind.ReleaseEquipment);
    }

    [Fact]
    public void Gate_hold_invalidates_ready_and_clearing_it_requires_new_gate_evidence()
    {
        var run = new OperationalScenario(); run.GateReady(); run.CompletePreboarding();
        var hold = new OperationalHold(Guid.NewGuid(), HoldKind.SimulatedGate, "Gate hold", true, null, null);
        run.Apply(new ChangeHold(run.Meta(), hold));
        Assert.Equal(GateReadinessState.Held, run.State.Session.Gate.State);
        Assert.Null(run.State.Question);
        run.User(PilotDirective.SkipSimulatedDelay);
        Assert.Equal(GateReadinessState.Unknown, run.State.Session.Gate.State);
        Assert.Equal(QuestionKind.GateCheck, run.State.Question!.Kind);
        run.User(PilotDirective.ConfirmGate);
        Assert.Equal(QuestionKind.Boarding, run.State.Question!.Kind);
        Assert.Empty(run.State.Session.Approvals);
    }

    [Fact]
    public void Applicable_required_services_cannot_be_disabled_and_applicability_is_typed()
    {
        var run = new OperationalScenario(FlightOperationType.FirstDeparture);
        var plan = run.State.Session.Plan;
        var fuel = plan.Services[ServiceKind.Refuelling];
        Assert.Throws<ArgumentException>(() => ServicePlanRules.Validate(plan with
        {
            Services = plan.Services.SetItem(ServiceKind.Refuelling,
            fuel with { Rule = fuel.Rule with { Policy = ServicePolicy.Disabled } })
        }));
        var lavatory = plan.Services[ServiceKind.Lavatory];
        Assert.False(ServicePlanRules.Applicable(ServiceKind.Lavatory, FlightOperationType.FirstDeparture,
            lavatory.Rule with { Applicability = ServiceApplicability.ArrivalOnly }));
    }

    [Fact]
    public void Requests_are_serial_and_acceptance_is_not_completion()
    {
        var run = new OperationalScenario();
        run.Pulse();
        var command = Assert.Single(run.State.Commands);
        Assert.Equal(ServiceKind.Deboarding, command.Service);
        run.Apply(new AcknowledgeCommand(run.Meta(EvidenceSource.System, null), command.Id, CommandOutcome.Accepted));
        Assert.Equal(ServiceState.Requested, run.State.Session.Services[ServiceKind.Deboarding].State);
        Assert.Single(run.State.Commands);
        Assert.Null(run.State.Question);
        run.Start(ServiceKind.Deboarding);
        run.Apply(new ObserveService(run.Meta(), ServiceKind.Deboarding, ServiceState.Completed));
        Assert.Equal(ServiceKind.Cleaning, run.State.Commands.Last().Service);
        Assert.DoesNotContain(run.State.Commands, item => item.Service == ServiceKind.Boarding);
    }

    [Fact]
    public void Reducer_is_pure_and_repeated_or_cross_session_events_have_no_effects()
    {
        var run = new OperationalScenario();
        var initial = run.State;
        var input = new EvaluateOperation(run.Meta());
        var first = OperationalReducer.Apply(initial, input);
        var same = OperationalReducer.Apply(initial, input);
        Assert.Empty(initial.Commands);
        Assert.Equal(first.NewCommands.ToArray(), same.NewCommands.ToArray());
        Assert.Equal(first.State.Session.Stage, same.State.Session.Stage);
        var repeated = OperationalReducer.Apply(first.State, input);
        Assert.Same(first.State, repeated.State);
        Assert.Empty(repeated.NewCommands);
        var other = OperationalReducer.Apply(first.State, input with { Metadata = input.Metadata with { SessionId = Guid.NewGuid() } });
        Assert.Same(first.State, other.State);
    }

    [Fact]
    public void Automatic_boarding_waits_for_ready_gate_services_and_explicit_approval()
    {
        var run = new OperationalScenario();
        run.User(PilotDirective.ApproveBoarding);
        Assert.Empty(run.State.Session.Approvals);
        run.CompletePreboarding();
        Assert.Equal(QuestionKind.GateCheck, run.State.Question!.Kind);
        Assert.DoesNotContain(run.State.Commands, command => command.Service == ServiceKind.Boarding);
        run.User(PilotDirective.ConfirmGate);
        Assert.Equal(GateProvenance.PilotAttested, run.State.Session.Gate.Provenance);
        Assert.Equal(QuestionKind.Boarding, run.State.Question!.Kind);
        run.User(PilotDirective.ApproveBoarding);
        Assert.Equal(ServiceState.RequestPending, run.State.Session.Services[ServiceKind.Boarding].State);
        Assert.Single(run.State.Session.Approvals);
    }

    [Fact]
    public void Required_skip_needs_confirmation_and_does_not_claim_fuel_complete()
    {
        var run = new OperationalScenario();
        run.GateReady();
        run.User(PilotDirective.Skip, ServiceKind.Refuelling);
        Assert.Equal(QuestionKind.MandatorySkip, run.State.Question!.Kind);
        Assert.Equal(ServiceState.Planned, run.State.Session.Services[ServiceKind.Refuelling].State);
        run.User(PilotDirective.ConfirmSkip, ServiceKind.Refuelling);
        run.CompletePreboarding();
        Assert.Equal(ServiceState.Skipped, run.State.Session.Services[ServiceKind.Refuelling].State);
        Assert.Equal(QuestionKind.FuelCheck, run.State.Question!.Kind);
        Assert.DoesNotContain(run.State.Commands, command => command.Service == ServiceKind.Boarding);
        run.User(PilotDirective.ConfirmFuel);
        Assert.Equal(QuestionKind.Boarding, run.State.Question!.Kind);
        Assert.Contains(run.Notices, notice => notice.Key.StartsWith("boarding.approval.no-fuel:", StringComparison.Ordinal));
        Assert.Equal(ServiceState.Skipped, run.State.Session.Services[ServiceKind.Refuelling].State);
    }

    [Fact]
    public void Skip_active_service_clarifies_and_does_not_cancel()
    {
        var run = new OperationalScenario(); run.Pulse(); run.Start(ServiceKind.Deboarding);
        var result = run.User(PilotDirective.Skip, ServiceKind.Deboarding);
        Assert.Equal(ServiceState.Active, run.State.Session.Services[ServiceKind.Deboarding].State);
        Assert.Empty(result.NewCommands);
        Assert.Contains(result.Notices, notice => notice.Text!.Contains("cancellation", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cancellation_is_capability_gated_and_never_complete_on_receipt(bool supported)
    {
        var run = new OperationalScenario(cancellation: supported); run.Pulse(); run.Start(ServiceKind.Deboarding);
        var result = run.User(PilotDirective.Cancel, ServiceKind.Deboarding);
        Assert.Equal(supported ? ServiceState.CancelPending : ServiceState.Active, run.State.Session.Services[ServiceKind.Deboarding].State);
        if (!supported) { Assert.Empty(result.NewCommands); Assert.Contains(result.Notices, notice => notice.Text!.Contains("unsupported", StringComparison.Ordinal)); return; }
        var cancel = Assert.Single(result.NewCommands);
        run.Apply(new AcknowledgeCommand(run.Meta(EvidenceSource.System, null), cancel.Id, CommandOutcome.Accepted));
        Assert.Equal(ServiceState.CancelPending, run.State.Session.Services[ServiceKind.Deboarding].State);
        run.Apply(new ObserveService(run.Meta(), ServiceKind.Deboarding, ServiceState.Cancelled));
        Assert.Equal(ServiceState.Cancelled, run.State.Session.Services[ServiceKind.Deboarding].State);
    }

    [Fact]
    public void Rejected_cancellation_can_be_explicitly_retried_with_new_intent_version()
    {
        var run = new OperationalScenario(); run.Pulse(); run.Start(ServiceKind.Deboarding);
        var first = Assert.Single(run.User(PilotDirective.Cancel, ServiceKind.Deboarding).NewCommands);
        run.Apply(new AcknowledgeCommand(run.Meta(EvidenceSource.System, null), first.Id, CommandOutcome.Rejected));
        Assert.Equal(ServiceState.Active, run.State.Session.Services[ServiceKind.Deboarding].State);
        var second = Assert.Single(run.User(PilotDirective.Cancel, ServiceKind.Deboarding).NewCommands);
        Assert.True(second.IntentVersion > first.IntentVersion);
    }

    [Fact]
    public void Lost_connection_during_cancellation_never_replays_after_reconnect()
    {
        var run = new OperationalScenario(); run.Pulse(); run.Start(ServiceKind.Deboarding); run.User(PilotDirective.Cancel, ServiceKind.Deboarding);
        int sent = run.State.Commands.Length;
        run.Apply(new ChangeConnection(run.Meta(EvidenceSource.System, null), ConnectionState.Offline));
        Assert.Equal(ServiceState.Unknown, run.State.Session.Services[ServiceKind.Deboarding].State);
        run.Apply(new ChangeConnection(run.Meta(EvidenceSource.System, null), ConnectionState.Connected));
        run.Pulse();
        Assert.Equal(sent, run.State.Commands.Length);
        Assert.Equal(WorkflowStage.NeedsAttention, run.State.Session.Stage);
    }

    [Fact]
    public void Simulated_skip_cannot_clear_real_holds_and_new_holds_invalidate_approval()
    {
        var run = new OperationalScenario(); run.GateReady(); run.CompletePreboarding();
        var simulated = new OperationalHold(Guid.NewGuid(), HoldKind.SimulatedGate, "Simulated gate delay", true, null, null);
        var real = new OperationalHold(Guid.NewGuid(), HoldKind.RealServiceBlocked, "GSX blocked", true, null, null);
        run.Apply(new ChangeHold(run.Meta(), simulated));
        run.Apply(new ChangeHold(run.Meta(EvidenceSource.Gsx), real));
        run.User(PilotDirective.SkipSimulatedDelay);
        Assert.False(run.State.Session.Holds.Single(hold => hold.Id == simulated.Id).IsActive);
        Assert.True(run.State.Session.Holds.Single(hold => hold.Id == real.Id).IsActive);
        Assert.Null(run.State.Question);
        run.User(PilotDirective.ApproveBoarding);
        Assert.Empty(run.State.Session.Approvals);
        run.Apply(new ChangeHold(run.Meta(), real with { IsActive = false }));
        Assert.True(run.State.Session.Holds.Single(hold => hold.Id == real.Id).IsActive);
        run.Apply(new ChangeHold(run.Meta(EvidenceSource.User), real with { Kind = HoldKind.ManualPilotHold, IsActive = false }));
        Assert.Equal(HoldKind.RealServiceBlocked, run.State.Session.Holds.Single(hold => hold.Id == real.Id).Kind);
        Assert.True(run.State.Session.Holds.Single(hold => hold.Id == real.Id).IsActive);
    }

    [Fact]
    public void Cancelled_boarding_never_triggers_closure_and_requires_new_approval()
    {
        var run = new OperationalScenario(); run.GateReady(); run.CompletePreboarding(); run.User(PilotDirective.ApproveBoarding); run.Start(ServiceKind.Boarding);
        run.User(PilotDirective.Cancel, ServiceKind.Boarding);
        run.Apply(new ObserveService(run.Meta(), ServiceKind.Boarding, ServiceState.Cancelled));
        Assert.NotEqual(QuestionKind.Closure, run.State.Question?.Kind);
        Assert.All(run.State.Session.Approvals, approval => Assert.True(approval.IsInvalidated));
        run.User(PilotDirective.Resume, ServiceKind.Boarding);
        Assert.Equal(QuestionKind.Boarding, run.State.Question!.Kind);
        Assert.Equal(ServiceState.Planned, run.State.Session.Services[ServiceKind.Boarding].State);
    }

    [Fact]
    public void Closure_and_release_need_distinct_pilot_approval_and_observations()
    {
        var run = new OperationalScenario(); run.BoardAndComplete();
        Assert.Equal(QuestionKind.Closure, run.State.Question!.Kind);
        Assert.DoesNotContain(run.State.Commands, command => command.Kind == OperationalCommandKind.ReleaseEquipment);
        run.User(PilotDirective.ApproveClosure);
        foreach (var action in Enum.GetValues<ReleaseAction>())
        {
            Assert.NotEqual(WorkflowStage.Completed, run.State.Session.Stage);
            var command = run.State.Commands.Last();
            run.Apply(new AcknowledgeCommand(run.Meta(EvidenceSource.System, null), command.Id, CommandOutcome.Accepted));
            Assert.False(run.State.Releases[action].IsSatisfied);
            run.Apply(new ObserveRelease(run.Meta(), action));
        }
        Assert.Equal(WorkflowStage.Completed, run.State.Session.Stage);
        Assert.Contains(run.Notices, notice => notice.Key == "handover.simulated");
        Assert.DoesNotContain(run.State.Commands, command => command.ToString().Contains("Pushback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unsupported_release_requires_labelled_manual_confirmation_for_each_obligation()
    {
        var run = new OperationalScenario(manualRelease: true); run.BoardAndComplete(); run.User(PilotDirective.ApproveClosure);
        foreach (var action in Enum.GetValues<ReleaseAction>())
        {
            Assert.Equal(QuestionKind.ManualRelease, run.State.Question!.Kind);
            Assert.Equal(action, run.State.Question.Release);
            Assert.False(run.State.Releases[action].IsSatisfied);
            run.User(PilotDirective.ConfirmRelease);
            Assert.Equal(CompletionProvenance.PilotAttested, run.State.Releases[action].Provenance);
        }
        Assert.Equal(WorkflowStage.Completed, run.State.Session.Stage);
        Assert.Contains(run.Notices, notice => notice.Key == "handover.attested");
        Assert.DoesNotContain(run.State.Commands, command => command.Kind == OperationalCommandKind.ReleaseEquipment);
    }

    [Fact]
    public void Ask_policy_requires_explicit_service_permission()
    {
        var run = new OperationalScenario(policy: ServicePolicy.Ask); run.Pulse();
        Assert.Empty(run.State.Commands);
        Assert.Equal(QuestionKind.Service, run.State.Question!.Kind);
        run.User(PilotDirective.ApproveService, ServiceKind.Catering);
        Assert.Empty(run.State.Commands);
        run.User(PilotDirective.ApproveService, ServiceKind.Deboarding);
        Assert.Single(run.State.Commands);
    }

    [Theory]
    [InlineData(FlightOperationType.FirstDeparture)]
    [InlineData(FlightOperationType.FinalArrival)]
    public void Operation_applicability_never_fabricates_completion(FlightOperationType operation)
    {
        var run = new OperationalScenario(operation);
        ServiceKind irrelevant = operation == FlightOperationType.FirstDeparture ? ServiceKind.Deboarding : ServiceKind.Boarding;
        Assert.Equal(ServiceState.NotRequired, run.State.Session.Services[irrelevant].State);
        Assert.Equal(CompletionProvenance.Unknown, run.State.Session.Services[irrelevant].CompletionProvenance);
        Assert.Equal(EvidenceSource.System, run.State.Session.Services[irrelevant].LastEvidence!.Source);
        run.CompletePreboarding();
        if (operation == FlightOperationType.FinalArrival) Assert.Equal(WorkflowStage.Completed, run.State.Session.Stage);
        Assert.DoesNotContain(run.State.Commands, command => command.Service == irrelevant);
    }

    [Theory]
    [InlineData(GateProvenance.Simulation, EvidenceSource.Simulation)]
    [InlineData(GateProvenance.PilotAttested, EvidenceSource.User)]
    [InlineData(GateProvenance.VerifiedExternal, EvidenceSource.SimConnect)]
    public void Gate_ready_is_source_qualified(GateProvenance provenance, EvidenceSource source)
    {
        Assert.Throws<ArgumentException>(() => GateRules.Apply(new(GateReadinessState.Ready, provenance, null)));
        var gate = new GateReadiness(GateReadinessState.Ready, provenance, new(source, "Explicit gate check", OperationalScenario.Now));
        Assert.Equal(gate, GateRules.Apply(gate));
        Assert.Throws<ArgumentException>(() => GateRules.Apply(gate with { Evidence = new(EvidenceSource.System, "Coordinates", OperationalScenario.Now) }));
    }
}
