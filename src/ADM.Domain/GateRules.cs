namespace ADM.Domain;

public static class GateRules
{
    public static bool HasEvidence(GateReadiness gate) => gate.Evidence is { Reference.Length: > 0 } evidence &&
        gate.Provenance switch
        {
            GateProvenance.Simulation => evidence.Source == EvidenceSource.Simulation,
            GateProvenance.PilotAttested => evidence.Source == EvidenceSource.User,
            GateProvenance.VerifiedExternal => evidence.Source is EvidenceSource.Gsx or EvidenceSource.SimConnect,
            _ => false
        };

    public static GateReadiness Apply(GateReadiness next)
    {
        if (!Enum.IsDefined(next.State) || !Enum.IsDefined(next.Provenance)) throw new ArgumentException("Unknown gate state.");
        if (next.State == GateReadinessState.Ready && !HasEvidence(next))
            throw new ArgumentException("Gate ready requires source-qualified evidence; coordinates alone are insufficient.");
        return next;
    }
}
