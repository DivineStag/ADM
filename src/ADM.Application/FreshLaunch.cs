using ADM.Domain;

namespace ADM.Application;

/// <summary>A new desktop launch archives prior work without cancelling, completing or replaying it.</summary>
public static class FreshLaunch
{
    public static async Task ArchivePreviousAsync(IDispatchStore store, CancellationToken cancellationToken)
    {
        var duties = store as IDutyStore;
        var duty = duties is null ? null : await duties.LoadDutyAsync(cancellationToken).ConfigureAwait(false);
        while (await store.LoadActiveAsync(cancellationToken).ConfigureAwait(false) is { } saved)
        {
            var session = saved.Checkpoint.State.Session;
            var archiveDuty = duty?.Sectors.Any(sector => sector.SessionId == session.Id) == true ? duty :
                DutyTracking.Attach(null, session, saved.Flight, saved.AircraftBinding, true);
            archiveDuty = DutyTracking.Complete(archiveDuty!, session.Id, session.Stage == WorkflowStage.Completed, true) with { State = DutyState.Ended };
            await store.SaveAsync(saved with { Archived = true, Duty = archiveDuty }, cancellationToken).ConfigureAwait(false);
            if (archiveDuty.Id == duty?.Id) duty = archiveDuty;
        }
        if (duties is not null && duty is { State: not DutyState.Ended })
            await duties.SaveDutyAsync(duty with { State = DutyState.Ended }, cancellationToken).ConfigureAwait(false);
    }
}
