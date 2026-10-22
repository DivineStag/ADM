using ADM.Domain;

namespace ADM.Domain.Tests;

public sealed class StateTransitionTests
{
    [Theory]
    [InlineData(ServiceState.Active, ServiceState.Skipped)]
    [InlineData(ServiceState.Requested, ServiceState.Skipped)]
    [InlineData(ServiceState.Planned, ServiceState.Completed)]
    [InlineData(ServiceState.RequestPending, ServiceState.Completed)]
    [InlineData(ServiceState.Completed, ServiceState.Planned)]
    [InlineData(ServiceState.Unknown, ServiceState.Completed)]
    public void Illegal_or_unreconciled_transitions_preserve_state(ServiceState current, ServiceState next)
    {
        var result = ServiceTransitions.Apply(current, next);
        Assert.False(result.Allowed);
        Assert.Equal(current, result.State);
        Assert.NotNull(result.Reason);
    }

    [Theory]
    [InlineData(ServiceState.Active, ServiceState.Completed)]
    [InlineData(ServiceState.CancelPending, ServiceState.Cancelled)]
    public void Terminal_outcomes_require_evidence(ServiceState current, ServiceState next)
    {
        Assert.False(ServiceTransitions.Apply(current, next).Allowed);
        Assert.False(ServiceTransitions.Apply(current, next, new(EvidenceSource.System, "receipt accepted", DateTimeOffset.UtcNow)).Allowed);
        Assert.True(ServiceTransitions.Apply(current, next, new(EvidenceSource.Gsx, "observation-123", DateTimeOffset.UtcNow)).Allowed);
        Assert.True(ServiceTransitions.Apply(current, next, new(EvidenceSource.User, "pilot manual acknowledgement", DateTimeOffset.UtcNow)).Allowed);
    }

    [Fact]
    public void Cancellation_receipt_is_pending_until_observed()
    {
        var result = ServiceTransitions.Apply(ServiceState.Active, ServiceState.CancelPending);
        Assert.True(result.Allowed);
        Assert.Equal(ServiceState.CancelPending, result.State);
        Assert.False(ServiceTransitions.Apply(result.State, ServiceState.Cancelled).Allowed);
    }

    [Fact]
    public void Repeated_state_is_idempotent()
    {
        Assert.Equal(new TransitionResult(true, ServiceState.Active, null),
            ServiceTransitions.Apply(ServiceState.Active, ServiceState.Active));
    }

    [Fact]
    public void Approval_is_scoped_to_session_kind_and_prerequisite_revision()
    {
        Guid session = Guid.NewGuid();
        var approval = new PilotApproval(Guid.NewGuid(), session, ApprovalKind.Boarding, 3, DateTimeOffset.UtcNow, false);
        Assert.True(ApprovalValidity.IsValid(approval, session, ApprovalKind.Boarding, 3));
        Assert.False(ApprovalValidity.IsValid(approval, Guid.NewGuid(), ApprovalKind.Boarding, 3));
        Assert.False(ApprovalValidity.IsValid(approval, session, ApprovalKind.Closure, 3));
        Assert.False(ApprovalValidity.IsValid(approval, session, ApprovalKind.Boarding, 4));
        Assert.False(ApprovalValidity.IsValid(approval with { IsInvalidated = true }, session, ApprovalKind.Boarding, 3));
    }

    [Theory]
    [InlineData(HoldKind.SimulatedGate, true)]
    [InlineData(HoldKind.SimulatedPassenger, true)]
    [InlineData(HoldKind.SimulatedStaff, true)]
    [InlineData(HoldKind.RealServiceBlocked, false)]
    [InlineData(HoldKind.ManualPilotHold, false)]
    [InlineData(HoldKind.ConnectionIssue, false)]
    public void Real_and_simulated_holds_are_distinct(HoldKind kind, bool expected)
    {
        Assert.Equal(expected, new OperationalHold(Guid.NewGuid(), kind, "test", true, null, null).IsSimulated);
    }
}
