using ADM.Contracts;
using ADM.Domain;

namespace ADM.Application;

public sealed record PreparationReadiness(bool Ready, string Message);
public static class AutomaticPreparation
{
    public static PreparationReadiness Check(FlightPlanSnapshot? flight, SimulatorSnapshot? sim, GsxLiveSnapshot? gsx, bool configured, DateTimeOffset now)
    {
        if (sim is not { Source: EvidenceSource.SimConnect, Connection: ConnectionState.Connected } || gsx?.Connection != ConnectionState.Connected ||
            now - sim.ObservedAt > TimeSpan.FromSeconds(5) || sim.ObservedAt > now.AddSeconds(2) || string.IsNullOrWhiteSpace(sim.AircraftTitle)) return new(false, "Waiting for MSFS and GSX. I'll connect when they're ready.");
        if (!configured) return new(false, "One-time aircraft setup is needed below.");
        if (gsx.AwaitingInput) return new(false, "GSX has a question open. Answer or close it in the simulator; I'll continue when it's ready.");
        if (flight is null) return new(false, "Waiting for the flight plan in GSX. Load it there, or enter a flight in Flight.");
        try { FlightPlanValidation.Validate(flight, now); }
        catch (ArgumentException) { return new(false, "The flight details need updating. Check Flight."); }
        if (gsx.AirportIcao != flight.Origin || flight.Source == FlightPlanSource.Gsx &&
            (!string.Equals(flight.SourceAircraftTitle?.Trim(), sim.AircraftTitle?.Trim(), StringComparison.OrdinalIgnoreCase) || gsx.AircraftIcaoType != flight.Aircraft.IcaoType))
            return new(false, "The loaded plan doesn't match this aircraft or airport. Check the flight plan in GSX.");
        return new(true, "Flight ready. I'll ask before starting services.");
    }
}
