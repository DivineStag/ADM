using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Simulation;

/// <summary>Pure, session-seeded decision. No wall-clock reads, service I/O or repeated random sampling.</summary>
public sealed class SeededDisruptionEngine : IOperationalRealismEngine
{
    public OperationalRealismState Advance(OperationalState state, DateTimeOffset now)
    {
        var current = state.Realism ?? throw new ArgumentException("An operation realism snapshot is required.");
        current.Validate();
        var next = current with
        {
            ProjectionInitialized = true,
            GatePrepared = current.Options.Enabled && current.GateProfile.SimulatedReadiness &&
                (current.PreparationSkipped || now >= current.PreparedAt.AddSeconds(Math.Min(current.GateProfile.PreparationSeconds, current.Options.MaximumDelayMinutes * 60)))
        };
        bool realHold = state.Session.Holds.Any(hold => hold.IsActive && !hold.IsSimulated);
        if (!current.DecisionMade && state.ServicesHaveStarted)
        {
            next = next with { DecisionMade = true };
            bool eligible = current.Options.Enabled && current.Options.MaximumDelayMinutes > 0 && state.Session.Operation == FlightOperationType.Turnaround &&
                !realHold && state.Session.Services.Values.All(service => service.State is not (ServiceState.Unknown or ServiceState.Failed)) &&
                state.Connection == ConnectionState.Connected && state.Session.Services[ServiceKind.Boarding].State is ServiceState.Planned or ServiceState.Eligible;
            byte[] seed = SHA256.HashData(state.Session.Id.ToByteArray());
            uint chance = BinaryPrimitives.ReadUInt32LittleEndian(seed);
            if (eligible && chance % 100 < current.Options.ProbabilityPercent)
            {
                int[] weightedMinutes = [2, 3, 3, 4, 4, 4, 5, 5, 6, 7];
                int minutes = Math.Min(current.Options.MaximumDelayMinutes, weightedMinutes[seed[4] % weightedMinutes.Length]);
                Guid id = Stable(state.Session.Id, 1);
                var incident = new DisruptionIncident(id, state.Session.Id, (DisruptionKind)(seed[5] % 3), now, now.AddMinutes(minutes), DisruptionStatus.Active,
                    Enum.GetValues<DisruptionEffectKind>().Select(kind => new DisruptionEffect(Stable(state.Session.Id, 10 + (int)kind), id, kind, true)).ToImmutableArray());
                next = next with { Incident = incident, GateBeforeHold = state.Session.Gate };
            }
        }
        if (next.Incident is { Status: DisruptionStatus.Active } active && (realHold || now >= active.ExpectedResolution))
            next = next with
            {
                Incident = active with
                {
                    Status = realHold ? DisruptionStatus.SupersededByRealHold : DisruptionStatus.Resolved,
                    Effects = active.Effects.Select(effect => effect with { IsActive = false }).ToImmutableArray()
                }
            };
        next.Validate(); return next;
    }
    private static Guid Stable(Guid session, int discriminator) => new(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"ADM/realism/{session:D}/{discriminator}")).AsSpan(0, 16));
}
