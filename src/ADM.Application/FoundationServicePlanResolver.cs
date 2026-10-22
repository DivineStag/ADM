using System.Collections.Immutable;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Application;

/// <summary>Pure snapshot layering. Does not schedule, authorize, or dispatch services.</summary>
public sealed class FoundationServicePlanResolver(IOperationalClock clock) : IServicePlanResolver
{
    public AppliedServicePlan Resolve(AirlineProfile genericProfile, AirlineProfile? airlineProfile,
        AircraftIdentity aircraft, FlightOperationType operation, PilotServiceOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(genericProfile);
        ArgumentNullException.ThrowIfNull(aircraft);
        ArgumentNullException.ThrowIfNull(overrides);
        aircraft = CanonicalAircraftIdentity.Create(aircraft.IcaoType, aircraft.Family);
        Validate(genericProfile);
        if (genericProfile.Icao != "GENERIC") throw new ArgumentException("A GENERIC baseline is required.", nameof(genericProfile));
        if (airlineProfile is not null) Validate(airlineProfile);
        var rules = ImmutableDictionary.CreateBuilder<ServiceKind, ResolvedServiceRule>();
        var phases = ServiceSequence.DefaultPhases;
        Apply(genericProfile.Operations, PolicyLayer.GenericDefaults, genericProfile);
        rules[ServiceKind.Deboarding] = new(ServiceKind.Deboarding, new(ServicePolicy.Disabled, false, [], "Removed from ADM handling"), PolicyLayer.GenericDefaults, "GENERIC", genericProfile.Revision);
        if (rules.Count != Enum.GetValues<ServiceKind>().Length)
            throw new ArgumentException("Generic policy must explicitly define every service for the selected operation.", nameof(genericProfile));
        if (airlineProfile is not null) Apply(airlineProfile.Operations, PolicyLayer.Airline, airlineProfile);
        ApplyFamily(genericProfile);
        if (airlineProfile is not null) ApplyFamily(airlineProfile);
        ApplyType(genericProfile);
        if (airlineProfile is not null) ApplyType(airlineProfile);
        AirlineProfile selected = airlineProfile ?? genericProfile;
        foreach (var (kind, rule) in overrides.Services)
        {
            if (!rules.TryGetValue(kind, out var previous)) throw new ArgumentException("Unknown service in pilot overrides.", nameof(overrides));
            rules[kind] = new(kind, rule with
            {
                Required = previous.Rule.Required || rule.Required,
                Dependencies = previous.Rule.Dependencies.Union(rule.Dependencies),
                Applicability = previous.Rule.Applicability
            },
                PolicyLayer.CurrentFlight, "CURRENT_FLIGHT", selected.Revision);
        }
        // Legacy enum value remains readable in old checkpoints; no new profile can enable it.
        rules[ServiceKind.Deboarding] = new(ServiceKind.Deboarding, new(ServicePolicy.Disabled, false, [], "Removed from ADM handling"), PolicyLayer.GenericDefaults, "GENERIC", genericProfile.Revision);
        foreach (var kind in rules.Keys.ToArray()) rules[kind] = rules[kind] with { Rule = rules[kind].Rule with { Dependencies = rules[kind].Rule.Dependencies.Remove(ServiceKind.Deboarding) } };
        var plan = new AppliedServicePlan(Guid.NewGuid(), selected.Icao, selected.Revision, aircraft, operation, rules.ToImmutable(), clock.UtcNow)
        { ServicePhases = phases };
        ServicePlanRules.Validate(plan);
        return plan;

        void Apply(ImmutableDictionary<FlightOperationType, OperationPolicy> operations, PolicyLayer source, AirlineProfile profile)
        {
            if (!operations.TryGetValue(operation, out var policy)) return;
            if (policy.ServicePhases is not null) { var restoredPhases = policy.ServicePhases.SetItem(ServiceKind.Deboarding, 1); ServiceSequence.Validate(restoredPhases); phases = restoredPhases; }
            foreach (var (kind, rule) in policy.Services)
                rules[kind] = new(kind, rule, source, profile.Icao, profile.Revision);
        }

        void ApplyFamily(AirlineProfile profile)
        {
            if (aircraft.Family is not null && profile.FamilyOverrides.TryGetValue(aircraft.Family, out var family))
                Apply(family.Operations, PolicyLayer.AircraftFamily, profile);
        }

        void ApplyType(AirlineProfile profile)
        {
            if (aircraft.IcaoType is not null && profile.TypeOverrides.TryGetValue(aircraft.IcaoType, out var type))
                Apply(type.Operations, PolicyLayer.AircraftType, profile);
        }
    }

    private static void Validate(AirlineProfile profile)
    {
        if (profile.SchemaVersion != 1 || profile.Revision < 1)
            throw new ArgumentException("Unsupported profile schema or revision.", nameof(profile));
    }
}
