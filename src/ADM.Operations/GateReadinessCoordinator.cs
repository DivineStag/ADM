using ADM.Contracts;
using ADM.Domain;

namespace ADM.Operations;

public sealed class GateReadinessCoordinator : IGateReadinessCoordinator
{
    public GateReadiness Evaluate(OperationalContext context) => context.Session.Holds.Any(hold => hold.IsActive && hold.Kind is HoldKind.SimulatedGate or HoldKind.SimulatedStaff)
        ? context.Session.Gate with { State = GateReadinessState.Held } : context.Session.Gate;
    public GateTransition Apply(GateReadiness next, GateReadiness current)
    {
        bool changed = next.State != current.State || next.Provenance != current.Provenance;
        return new(GateRules.Apply(next), changed, changed ? "Gate prerequisites changed; previous approval must be invalidated." : "No semantic change");
    }
}
