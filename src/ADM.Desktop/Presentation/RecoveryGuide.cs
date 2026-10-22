using ADM.Domain;

namespace ADM.Desktop.Presentation;

public enum RecoveryAction { None, Reconnect, EnterFlight, OpenGsx, Resume, RestartService, Prepare, RepairInstall }
public sealed record RecoveryStep(RecoveryAction Action, string Title, string Detail, string Button, ServiceKind? Service = null)
{
    public static RecoveryStep None { get; } = new(RecoveryAction.None, "", "", "");
}
public sealed record RecoveryFacts(bool Connected, bool HasOperation, bool HasFlight, bool Armed, bool HasError,
    bool PromptOpen, bool MenuOpen, ServiceKind? Restartable, bool NeedsSetup, bool Completed, bool SimulatorRuntimeAvailable = true);
public static class RecoveryGuide
{
    public static RecoveryStep Choose(RecoveryFacts facts)
    {
        if (facts.NeedsSetup) return RecoveryStep.None;
        if (!facts.SimulatorRuntimeAvailable) return new(RecoveryAction.RepairInstall, "ADM installation needs repair", "A required connection file is missing. I'll run ADM setup again and keep your profiles and history.", "Repair installation");
        if (!facts.Connected) return new(RecoveryAction.Reconnect, "Let's reconnect", "Start MSFS and let GSX load. I'll check the connection again.", "Retry connection");
        if (facts.PromptOpen || facts.MenuOpen) return new(RecoveryAction.OpenGsx, "GSX is open", "Finish or close the GSX screen so service requests can continue.", "Open GSX");
        if (!facts.HasFlight && !facts.HasOperation) return new(RecoveryAction.EnterFlight, "No flight plan yet", "Load your plan in GSX, or enter the flight here.", "Enter flight details");
        if (facts.Restartable is { } service) return new(RecoveryAction.RestartService, "Review this service", "GSX reports it available. Restart requests it again; it does not confirm completion.",
            "Restart " + (service == ServiceKind.Refuelling ? "fuel" : service.ToString().ToLowerInvariant()), service);
        if (facts.HasOperation && !facts.Armed && !facts.Completed) return new(RecoveryAction.Resume, "Handling is paused", "Review the aircraft and current GSX services, then resume safely.", "Review and resume");
        if (facts.HasError && facts.HasOperation && !facts.Completed) return new(RecoveryAction.OpenGsx, "Let's check the service", "Open GSX to review its current state. ADM won't repeat an uncertain request automatically.", "Open GSX");
        if (facts.HasError && !facts.HasOperation) return new(RecoveryAction.Prepare, "Let's try that again", "I'll recheck the current flight and service availability before continuing.", "Try again");
        return RecoveryStep.None;
    }
}
