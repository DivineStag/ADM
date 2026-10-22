using System.Collections.Immutable;

namespace ADM.Domain;

/// <summary>Requested workflow order, independent of aircraft-specific concurrency permission.</summary>
public static class ServiceSequence
{
    public static ImmutableArray<ServiceKind> DefaultOrder { get; } =
        [ServiceKind.Deboarding, ServiceKind.Cleaning, ServiceKind.Water, ServiceKind.Lavatory,
            ServiceKind.Refuelling, ServiceKind.Catering, ServiceKind.Boarding];
    public static ImmutableDictionary<ServiceKind, int> DefaultPhases { get; } = DefaultOrder.ToImmutableDictionary(
        kind => kind, kind => kind switch { ServiceKind.Deboarding => 1, ServiceKind.Cleaning => 2, ServiceKind.Boarding => 7, _ => 3 });

    public static IEnumerable<ServiceKind> Ordered(AppliedServicePlan plan) =>
        DefaultOrder.OrderBy(kind => plan.ServicePhases[kind]);

    public static void Validate(ImmutableDictionary<ServiceKind, int>? phases)
    {
        if (phases is null || phases.Count != 7 || DefaultOrder.Any(kind => !phases.TryGetValue(kind, out int phase) || phase is < 1 or > 7))
            throw new ArgumentException("Service phases must contain every service once, with phase numbers from 1 to 7.");
        if (DefaultOrder.Where(kind => kind != ServiceKind.Boarding).Any(kind => phases[kind] >= phases[ServiceKind.Boarding]))
            throw new ArgumentException("Boarding must have its own phase after all ground services, including refuelling.");
    }

    public static bool IsInFlight(ServiceState state) => state is ServiceState.RequestPending or ServiceState.Requested or ServiceState.Active or ServiceState.CancelPending;

    public static bool CanOverlap(OperationalState state, ServiceKind candidate)
    {
        var active = state.Session.Services.Values.Where(service => IsInFlight(service.State) && service.Kind != candidate).Select(service => service.Kind).ToArray();
        return active.Length == 0 ||
            state.RequestFuelAndCateringTogether && (candidate is ServiceKind.Refuelling or ServiceKind.Catering) && active.All(kind => kind is ServiceKind.Refuelling or ServiceKind.Catering) ||
            state.ValidatedConcurrentGroups.Any(group => group.Contains(candidate) && active.All(group.Contains));
    }
}
