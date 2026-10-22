using System.Collections.Immutable;
using ADM.Domain;
using ADM.Tests.Shared;

namespace ADM.Domain.Tests;

public sealed class ServiceSequenceTests
{
    [Fact]
    public void Boarding_eligibility_loss_removes_the_prompt_and_blocks_approval_until_GSX_allows_it_again()
    {
        var run = new OperationalScenario(); run.GateReady(); run.CompletePreboarding();
        Assert.Equal(QuestionKind.Boarding, run.State.Question!.Kind);
        run.Apply(new UpdateServiceAvailability(run.Meta(EvidenceSource.System, null), []));
        Assert.Null(run.State.Question);
        run.User(PilotDirective.ApproveBoarding);
        Assert.DoesNotContain(run.State.Commands, command => command.Service == ServiceKind.Boarding);
        run.Apply(new UpdateServiceAvailability(run.Meta(EvidenceSource.System, null), [ServiceKind.Boarding]));
        Assert.Equal(QuestionKind.Boarding, run.State.Question!.Kind);
    }
    [Fact]
    public void Previously_queued_concurrent_commands_are_blocked_when_one_service_becomes_unknown()
    {
        var run = new OperationalScenario(concurrent: true); run.GateReady();
        foreach (var kind in new[] { ServiceKind.Deboarding, ServiceKind.Cleaning })
        { run.Start(kind); run.Apply(new ObserveService(run.Meta(), kind, ServiceState.Completed)); }
        var queued = run.State.Commands.TakeLast(4).ToArray();
        run.Apply(new ObserveService(run.Meta(), ServiceKind.Water, ServiceState.Unknown));
        Assert.All(queued, command => Assert.False(OperationalCommandPolicy.CanDispatch(run.State, command)));
    }
    [Fact]
    public void Default_order_has_four_ground_services_in_one_phase_and_boarding_last()
    {
        Assert.Equal(new[] { ServiceKind.Water, ServiceKind.Lavatory, ServiceKind.Refuelling, ServiceKind.Catering },
            ServiceSequence.DefaultOrder.Where(kind => ServiceSequence.DefaultPhases[kind] == 3));
        ServiceSequence.Validate(ServiceSequence.DefaultPhases);
        Assert.Equal(ServiceKind.Boarding, ServiceSequence.DefaultOrder.Last());
    }

    [Fact]
    public void Without_validated_overlap_default_phase_executes_in_water_lavatory_fuel_catering_order()
    {
        var run = new OperationalScenario(); run.GateReady(); run.CompletePreboarding();
        Assert.Equal(ServiceSequence.DefaultOrder.Where(kind => kind != ServiceKind.Boarding), run.State.Commands.Select(command => command.Service!.Value));
        Assert.Equal(QuestionKind.Boarding, run.State.Question!.Kind);
    }

    [Fact]
    public void Validated_overlap_queues_four_services_after_arrival_handling_but_never_boarding()
    {
        var run = new OperationalScenario(concurrent: true); run.GateReady();
        foreach (var kind in new[] { ServiceKind.Deboarding, ServiceKind.Cleaning })
        { run.Start(kind); run.Apply(new ObserveService(run.Meta(), kind, ServiceState.Completed)); }
        var pending = run.State.Session.Services.Values.Where(service => service.State == ServiceState.RequestPending).ToArray();
        Assert.Equal(4, pending.Length);
        Assert.All(pending, service => Assert.Equal(3, run.State.Session.Plan.ServicePhases[service.Kind]));
        Assert.All(run.State.Commands.TakeLast(4), command => Assert.True(OperationalCommandPolicy.CanDispatch(run.State, command)));
        foreach (var kind in new[] { ServiceKind.Water, ServiceKind.Lavatory, ServiceKind.Catering })
        { run.Start(kind); run.Apply(new ObserveService(run.Meta(), kind, ServiceState.Completed)); }
        run.Start(ServiceKind.Refuelling);
        run.User(PilotDirective.ApproveBoarding);
        Assert.Null(run.State.Question);
        Assert.DoesNotContain(run.State.Commands, command => command.Service == ServiceKind.Boarding);
        run.Apply(new ObserveService(run.Meta(), ServiceKind.Refuelling, ServiceState.Completed));
        Assert.Equal(QuestionKind.Boarding, run.State.Question!.Kind);
        Assert.DoesNotContain(run.State.Commands, command => command.Service == ServiceKind.Boarding);
        run.User(PilotDirective.ApproveBoarding);
        Assert.Single(run.State.Commands, command => command.Service == ServiceKind.Boarding);
    }

    [Fact]
    public void Custom_phase_barrier_waits_for_every_earlier_service()
    {
        var phases = ServiceSequence.DefaultPhases.SetItem(ServiceKind.Water, 5).SetItem(ServiceKind.Lavatory, 4);
        var run = new OperationalScenario(phases: phases, concurrent: true); run.GateReady(); run.CompletePreboarding();
        Assert.Equal(new[] { ServiceKind.Deboarding, ServiceKind.Cleaning, ServiceKind.Refuelling, ServiceKind.Catering, ServiceKind.Lavatory, ServiceKind.Water },
            run.State.Commands.Select(command => command.Service!.Value));
    }

    [Theory]
    [InlineData(ServiceKind.Refuelling, 7)]
    [InlineData(ServiceKind.Boarding, 3)]
    [InlineData(ServiceKind.Water, 0)]
    [InlineData(ServiceKind.Catering, 8)]
    public void Unsafe_or_out_of_range_phases_are_rejected(ServiceKind kind, int phase) =>
        Assert.Throws<ArgumentException>(() => new OperationalScenario(phases: ServiceSequence.DefaultPhases.SetItem(kind, phase)));

    [Fact]
    public void Missing_service_and_arrival_dependency_cycle_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ServiceSequence.Validate(ServiceSequence.DefaultPhases.Remove(ServiceKind.Lavatory)));
        Assert.Throws<ArgumentException>(() => new OperationalScenario(phases: ServiceSequence.DefaultPhases.SetItem(ServiceKind.Water, 1)));
    }

    [Fact]
    public void Concurrent_repeated_evaluations_never_duplicate_intents()
    {
        var run = new OperationalScenario(concurrent: true); run.GateReady(); run.CompletePreboarding();
        for (int index = 0; index < 100; index++) run.Pulse();
        Assert.Equal(6, run.State.Commands.Length);
        Assert.Single(run.Notices, notice => notice.Key.StartsWith("boarding.approval", StringComparison.Ordinal));
    }
}
