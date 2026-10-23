using ADM.Domain;
using ADM.Tests.Shared;

namespace ADM.Domain.Tests;

public sealed class SkipToTests
{
    [Fact]
    public void Planned_optional_services_are_skipped_and_fuel_is_requested()
    {
        var run = new OperationalScenario();
        run.User(PilotDirective.SkipTo, ServiceKind.Refuelling);
        foreach (var kind in new[] { ServiceKind.Deboarding, ServiceKind.Cleaning, ServiceKind.Water, ServiceKind.Lavatory })
            Assert.Equal(ServiceState.Skipped, run.State.Session.Services[kind].State);
        Assert.Equal(ServiceState.RequestPending, run.State.Session.Services[ServiceKind.Refuelling].State);
        Assert.Equal(ServiceState.Planned, run.State.Session.Services[ServiceKind.Catering].State);
    }

    [Fact]
    public void Jump_to_boarding_cannot_skip_required_fuel_or_partially_skip_other_work()
    {
        var run = new OperationalScenario();
        run.User(PilotDirective.SkipTo, ServiceKind.Boarding);
        Assert.DoesNotContain(run.State.Session.Services.Values, item => item.State == ServiceState.Skipped);
        Assert.DoesNotContain(run.State.Commands, command => command.Service == ServiceKind.Boarding);
        Assert.Contains(run.Notices, notice => notice.Text?.Contains("required work", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Cancellation_acceptance_does_not_unlock_the_target_and_repeats_do_not_duplicate_it()
    {
        var run = new OperationalScenario(concurrent: true); run.Pulse(); run.Start(ServiceKind.Deboarding);
        run.User(PilotDirective.SkipTo, ServiceKind.Refuelling);
        var cancel = Assert.Single(run.State.Commands, command => command.Kind == OperationalCommandKind.CancelService);
        Assert.True(OperationalCommandPolicy.CanDispatch(run.State, cancel));
        run.Apply(new AcknowledgeCommand(run.Meta(EvidenceSource.System, null), cancel.Id, CommandOutcome.Accepted));
        run.User(PilotDirective.SkipTo, ServiceKind.Refuelling);
        Assert.Single(run.State.Commands, command => command.Kind == OperationalCommandKind.CancelService);
        Assert.DoesNotContain(run.State.Commands, command => command.Service == ServiceKind.Refuelling);
        run.Apply(new ObserveService(run.Meta(), ServiceKind.Deboarding, ServiceState.Cancelled));
        Assert.Single(run.State.Commands, command => command.Service == ServiceKind.Refuelling);
        Assert.Null(run.State.SkipToTarget);
    }

    [Fact]
    public void Unsupported_cancellation_waits_for_completion_instead_of_fabricating_it()
    {
        var run = new OperationalScenario(cancellation: false); run.Pulse(); run.Start(ServiceKind.Deboarding);
        run.User(PilotDirective.SkipTo, ServiceKind.Refuelling);
        Assert.Equal(ServiceState.Active, run.State.Session.Services[ServiceKind.Deboarding].State);
        Assert.DoesNotContain(run.State.Commands, command => command.Kind == OperationalCommandKind.CancelService || command.Service == ServiceKind.Refuelling);
        run.Apply(new ObserveService(run.Meta(), ServiceKind.Deboarding, ServiceState.Completed));
        Assert.Single(run.State.Commands, command => command.Service == ServiceKind.Refuelling);
    }

    [Fact]
    public void An_unacknowledged_request_cannot_be_skipped_or_cancelled_blindly()
    {
        var run = new OperationalScenario(); run.Pulse();
        run.User(PilotDirective.SkipTo, ServiceKind.Refuelling);
        Assert.Equal(ServiceState.RequestPending, run.State.Session.Services[ServiceKind.Deboarding].State);
        Assert.DoesNotContain(run.State.Session.Services.Values, item => item.State == ServiceState.Skipped);
        Assert.Single(run.State.Commands);
    }

    [Fact]
    public void Shortcut_follows_the_custom_profile_order()
    {
        var phases = ServiceSequence.DefaultPhases.SetItem(ServiceKind.Refuelling, 4);
        var run = new OperationalScenario(phases: phases);
        run.User(PilotDirective.SkipTo, ServiceKind.Refuelling);
        Assert.Equal(ServiceState.Skipped, run.State.Session.Services[ServiceKind.Catering].State);
        Assert.Equal(ServiceState.RequestPending, run.State.Session.Services[ServiceKind.Refuelling].State);
    }

    [Fact]
    public void Jump_to_boarding_still_requires_separate_pilot_approval()
    {
        var run = new OperationalScenario();
        var state = run.State;
        foreach (var kind in ServiceSequence.Ordered(state.Session.Plan).TakeWhile(kind => kind != ServiceKind.Catering))
            state = state with { Session = state.Session with { Services = state.Session.Services.SetItem(kind, state.Session.Services[kind] with { State = ServiceState.Completed }) } };
        var reduction = OperationalReducer.Apply(state, new PilotInstruction(run.Meta(EvidenceSource.User), PilotDirective.SkipTo, ServiceKind.Boarding));
        Assert.Equal(ServiceState.Skipped, reduction.State.Session.Services[ServiceKind.Catering].State);
        Assert.DoesNotContain(reduction.State.Commands, command => command.Service == ServiceKind.Boarding);
        Assert.Equal(QuestionKind.Boarding, reduction.State.Question?.Kind);
    }

    [Theory]
    [InlineData(ConnectionState.Offline)]
    [InlineData(ConnectionState.ReconciliationRequired)]
    public void Disconnected_shortcuts_do_not_change_services(ConnectionState connection)
    {
        var run = new OperationalScenario();
        var result = OperationalReducer.Apply(run.State with { Connection = connection },
            new PilotInstruction(run.Meta(EvidenceSource.User), PilotDirective.SkipTo, ServiceKind.Refuelling));
        Assert.Empty(result.NewCommands);
        Assert.DoesNotContain(result.State.Session.Services.Values, service => service.State == ServiceState.Skipped);
    }

    [Fact]
    public void A_shortcut_does_not_grant_initial_service_start_permission()
    {
        var run = new OperationalScenario();
        var result = OperationalReducer.Apply(run.State with { RequiresServiceStartApproval = true },
            new PilotInstruction(run.Meta(EvidenceSource.User), PilotDirective.SkipTo, ServiceKind.Refuelling));
        Assert.Empty(result.NewCommands);
        Assert.Null(result.State.ServiceStartApproval);
        Assert.Equal(QuestionKind.ServicesStart, result.State.Question?.Kind);
    }
}
