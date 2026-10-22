using ADM.Domain;
using ADM.Tests.Shared;

namespace ADM.Domain.Tests;

public sealed class ServiceCycleTests
{
    [Fact]
    public void Real_hold_replans_an_unsent_ask_service_with_fresh_permission_instead_of_stranding_it()
    {
        var run = new OperationalScenario(policy: ServicePolicy.Ask); run.Pulse(); run.User(PilotDirective.ApproveService, ServiceKind.Deboarding);
        var oldCommand = run.State.Commands.Last(); var meta = run.Meta(EvidenceSource.Gsx);
        var hold = new OperationalHold(meta.EventId, HoldKind.RealServiceBlocked, "Real vehicle wait", true, null, null);
        run.Apply(new ChangeHold(meta, hold, Notify: false));
        Assert.Equal(ServiceState.Planned, run.State.Session.Services[ServiceKind.Deboarding].State);
        Assert.False(OperationalCommandPolicy.CanDispatch(run.State, oldCommand));
        run.Apply(new ChangeHold(run.Meta(EvidenceSource.Gsx), hold with { IsActive = false }, Notify: false));
        Assert.Equal(QuestionKind.Service, run.State.Question?.Kind);
        run.User(PilotDirective.ApproveService, ServiceKind.Deboarding);
        Assert.True(OperationalCommandPolicy.CanDispatch(run.State, run.State.Commands.Last()));
        Assert.False(OperationalCommandPolicy.CanDispatch(run.State, oldCommand));
    }
    [Theory]
    [InlineData(ServiceKind.Refuelling)]
    [InlineData(ServiceKind.Boarding)]
    public void Routine_cycle_resolution_cannot_bypass_fuel_or_boarding(ServiceKind kind)
    {
        var run = new OperationalScenario(); run.GateReady();
        if (kind == ServiceKind.Boarding) { run.CompletePreboarding(); run.User(PilotDirective.ApproveBoarding); }
        else
        {
            run.Pulse();
            foreach (var previous in new[] { ServiceKind.Deboarding, ServiceKind.Cleaning, ServiceKind.Water, ServiceKind.Lavatory })
            { run.Start(previous); run.Apply(new ObserveService(run.Meta(), previous, ServiceState.Completed)); }
        }
        run.Start(kind);
        run.Apply(new ObserveServiceCycleEnd(run.Meta(EvidenceSource.Gsx), kind));
        Assert.Equal(ServiceState.Active, run.State.Session.Services[kind].State);
        run.Apply(new RequestServiceCompletionCheck(run.Meta(EvidenceSource.Gsx), kind));
        Assert.Equal(QuestionKind.ServiceCompletion, run.State.Question?.Kind);
        run.User(PilotDirective.ConfirmServiceCompletion, kind == ServiceKind.Refuelling ? ServiceKind.Boarding : ServiceKind.Refuelling);
        Assert.Equal(ServiceState.Unknown, run.State.Session.Services[kind].State);
        run.User(PilotDirective.ConfirmServiceCompletion, kind);
        Assert.Equal(CompletionProvenance.PilotAttested, run.State.Session.Services[kind].CompletionProvenance);
    }
    [Fact]
    public void Required_routine_work_cannot_be_satisfied_by_idle_return()
    {
        var run = new OperationalScenario(additionalRequired: ServiceKind.Deboarding); run.Pulse(); run.Start(ServiceKind.Deboarding);
        run.Apply(new ObserveServiceCycleEnd(run.Meta(EvidenceSource.Gsx), ServiceKind.Deboarding));
        Assert.Equal(ServiceState.Active, run.State.Session.Services[ServiceKind.Deboarding].State);
        Assert.DoesNotContain(run.State.Commands, command => command.Service == ServiceKind.Cleaning);
    }
    [Fact]
    public void Losing_boarding_availability_after_approval_invalidates_unsent_clearance()
    {
        var run = new OperationalScenario(); run.GateReady(); run.CompletePreboarding(); run.User(PilotDirective.ApproveBoarding);
        var oldCommand = run.State.Commands.Last();
        run.Apply(new UpdateServiceAvailability(run.Meta(EvidenceSource.System, null), []));
        Assert.Equal(ServiceState.Planned, run.State.Session.Services[ServiceKind.Boarding].State);
        Assert.False(OperationalCommandPolicy.CanDispatch(run.State, oldCommand));
        run.Apply(new UpdateServiceAvailability(run.Meta(EvidenceSource.System, null), [ServiceKind.Boarding]));
        Assert.Equal(QuestionKind.Boarding, run.State.Question?.Kind);
        Assert.False(OperationalCommandPolicy.CanDispatch(run.State, oldCommand));
    }
    [Fact]
    public void New_hold_clears_unsent_boarding_intent_and_requires_a_new_approval()
    {
        var run = new OperationalScenario(); run.GateReady(); run.CompletePreboarding(); run.User(PilotDirective.ApproveBoarding);
        var oldCommand = run.State.Commands.Last();
        var meta = run.Meta(EvidenceSource.Gsx);
        run.Apply(new ChangeHold(meta, new(meta.EventId, HoldKind.RealServiceBlocked, "Vehicle waiting", true, null, null)));
        Assert.Equal(ServiceState.Planned, run.State.Session.Services[ServiceKind.Boarding].State);
        Assert.False(OperationalCommandPolicy.CanDispatch(run.State, oldCommand));
        run.Apply(new ChangeHold(run.Meta(EvidenceSource.Gsx), new(meta.EventId, HoldKind.RealServiceBlocked, "Vehicle waiting", false, null, null)));
        Assert.Equal(QuestionKind.Boarding, run.State.Question?.Kind);
        run.User(PilotDirective.ApproveBoarding);
        Assert.True(OperationalCommandPolicy.CanDispatch(run.State, run.State.Commands.Last()));
        Assert.False(OperationalCommandPolicy.CanDispatch(run.State, oldCommand));
    }
}
