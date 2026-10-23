namespace ADM.Domain;

/// <summary>Revalidate journaled intent immediately before any future adapter dispatch.</summary>
public static class OperationalCommandPolicy
{
    public static bool CanDispatch(OperationalState state, OperationalCommand command)
    {
        if (state.Connection != ConnectionState.Connected || command.SessionId != state.Session.Id ||
            state.RequiresServiceStartApproval && (state.ServiceStartApproval is not { Source: EvidenceSource.User } || !OperationalReducer.EvidenceValid(state.ServiceStartApproval)) ||
            state.Session.Services.Values.Any(service => service.State is ServiceState.Unknown or ServiceState.Failed) ||
            command.PlanSnapshotId != state.Session.Plan.SnapshotId || state.AcknowledgedCommands.Contains(command.Id) ||
            !state.Commands.Contains(command) || !Enum.IsDefined(command.Kind) ||
            state.Commands.Last(known => known.Kind == command.Kind && known.Service == command.Service && known.Release == command.Release).Id != command.Id) return false;
        bool approval(ApprovalKind kind) => command.PrerequisiteRevision == state.Session.PrerequisiteRevision &&
            state.Session.Approvals.Any(item => ApprovalValidity.IsValid(item, state.Session.Id, kind, state.Session.PrerequisiteRevision));
        if (command.Kind == OperationalCommandKind.ReleaseEquipment)
            return false; // Physical doors/equipment remain pilot/GSX owned, including restored intents.
        if (command.Service is not { } kind || !state.Session.Services.TryGetValue(kind, out var service)) return false;
        if (command.Kind == OperationalCommandKind.CancelService)
            return service.State == ServiceState.CancelPending && state.CancellableServices.Contains(kind);
        if (state.SkipToTarget is { } target && ServiceSequence.Ordered(state.Session.Plan).TakeWhile(item => item != target).Any(item => !Resolved(state, item))) return false;
        if (!state.SupportedServices.Contains(kind) || service.State != ServiceState.RequestPending) return false;
        if (state.TriggerableServices is { } available && !available.Contains(kind)) return false;
        // Recheck dependencies at the last possible moment; planned concurrency never grants readiness.
        if (!ServicePlanRules.Dependencies(state.Session.Plan, kind).All(dependency => Resolved(state, dependency))) return false;
        if (kind == ServiceKind.Boarding)
            return approval(ApprovalKind.Boarding) && !state.Session.Holds.Any(hold => hold.IsActive) &&
                state.Session.Gate.State == GateReadinessState.Ready && GateRules.HasEvidence(state.Session.Gate);
        return !state.Session.Holds.Any(hold => hold.IsActive && !hold.IsSimulated && hold.Scope == HoldScope.AllOperations) &&
            ServiceSequence.CanOverlap(state, kind) &&
            (state.Session.Plan.Services[kind].Rule.Policy != ServicePolicy.Ask || state.ServicePermissionEvidence.ContainsKey(kind));
    }

    private static bool Resolved(OperationalState state, ServiceKind kind)
    {
        var service = state.Session.Services[kind];
        if (service.State is ServiceState.Completed or ServiceState.NotRequired) return true;
        if (service.State == ServiceState.CycleEnded) return kind is not (ServiceKind.Refuelling or ServiceKind.Boarding) && !state.Session.Plan.Services[kind].Rule.Required;
        if (service.State == ServiceState.Cancelled) return !state.Session.Plan.Services[kind].Rule.Required;
        return service.State == ServiceState.Skipped && kind != ServiceKind.Boarding &&
            (kind != ServiceKind.Refuelling || !state.Session.Plan.Services[kind].Rule.Required ||
                state.FuelAttested && state.FuelAttestation is { Source: EvidenceSource.User } && OperationalReducer.EvidenceValid(state.FuelAttestation));
    }
}
