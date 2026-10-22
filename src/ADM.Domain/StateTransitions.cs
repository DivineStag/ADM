namespace ADM.Domain;

public sealed record TransitionResult(bool Allowed, ServiceState State, string? Reason);

/// <summary>Foundation only; scheduling and full operational guards belong to M2.</summary>
public static class ServiceTransitions
{
    public static TransitionResult Apply(ServiceState current, ServiceState next, Evidence? evidence = null)
    {
        if (current == next)
            return new(true, current, null);

        bool allowed = (current, next) switch
        {
            (ServiceState.Planned, ServiceState.Eligible or ServiceState.Skipped or ServiceState.NotRequired) => true,
            (ServiceState.Eligible, ServiceState.RequestPending or ServiceState.Skipped) => true,
            (ServiceState.RequestPending, ServiceState.Requested or ServiceState.Failed or ServiceState.Unknown) => true,
            (ServiceState.Requested, ServiceState.Active or ServiceState.CancelPending or ServiceState.Failed or ServiceState.Unknown) => true,
            (ServiceState.Active, ServiceState.Completed or ServiceState.CancelPending or ServiceState.Failed or ServiceState.Unknown) => true,
            (ServiceState.CancelPending, ServiceState.Cancelled or ServiceState.Completed or ServiceState.Failed or ServiceState.Unknown) => true,
            _ => false
        };
        if (!allowed)
            return new(false, current, $"Transition from {current} to {next} is not permitted.");
        if (next is ServiceState.Completed or ServiceState.Cancelled &&
            (evidence is null || string.IsNullOrWhiteSpace(evidence.Reference) ||
             evidence.Source is not (EvidenceSource.Gsx or EvidenceSource.SimConnect or EvidenceSource.User or EvidenceSource.Simulation)))
            return new(false, current, "Completion or cancellation requires observed or explicitly attested evidence.");

        return new(true, next, null);
    }
}

public static class ApprovalValidity
{
    public static bool IsValid(PilotApproval approval, Guid sessionId, ApprovalKind kind, int revision) =>
        !approval.IsInvalidated && approval.SessionId == sessionId && approval.Kind == kind &&
        approval.PrerequisiteRevision == revision;
}
