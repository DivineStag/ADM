# GSX owns aircraft service-readiness checks

22 October 2026, pilot-requested policy amendment, beta 0.8.0-beta.3.

ADM no longer independently requires engines off, parking brake set, on-ground or stationary readings before preparing, requesting or continuing handling. Missing or conflicting cockpit readings cannot pause an otherwise valid ADM operation. The Fenix-specific parking-brake subscription and generic brake/engine SimVars are removed from live telemetry.

GSX makes the physical service decision through its configured aircraft handling. The installed official Remote API v2 guide, §10 p279, states that service.trigger scripts the live menu and follows GSX's existing state machine. ADM still requires a verified connected adapter, a currently triggerable advertised service, no provider waiting block or open input/menu, the right operation context and a durable unreplayed intent. The guide's §8.1 pp271–272 describes canTrigger as service availability; it is not a standalone promise that the aircraft is parked or that the service will start. GSX can still reject or defer a request. Acceptance is never completion, and an accepted request with no observed start retains the existing explicit recovery path. No new GSX verb or workaround is introduced.

SimConnect remains read-only for aircraft identity, position and passive flight-phase tracking (airborne chat clearing, sector history and arrival candidates). It does not enforce cockpit service-readiness policy. Arrival candidates use stationary/on-ground flight observations plus current GSX stand context, followed by existing explicit location/next-sector confirmation; no brake or engine reading is needed. This is not an automatic declaration that the gate or aircraft is ready.

Preserved: current simulator/GSX connection and aircraft binding, airport/flight consistency, generation reconciliation, per-service capabilities/availability, fuel-before-boarding and mandatory service dependencies, pilot approval for services/boarding/closure, simulated/real holds, reliable completion evidence and duplicate-request prevention. Pushback stays pilot-owned.

Legacy nullable brake/engine fields remain readable in contracts/history and synthetic fixtures, but production SimConnect does not request them and no handling guard consumes them. Earlier Fenix brake-mapping evidence is historical and superseded by this policy.

Regression evidence and the current installer location are recorded in the M8 distribution follow-up after verification. Actual GSX refusal/defer behaviour for each aircraft/version still belongs to the live qualification matrix; automated tests do not claim universal compatibility.

## Verification and build

Release build: zero warnings/errors. All **363 automated tests** pass (Application 48, Conversation 39, Domain 73, EndToEnd 85, Integration 55, Desktop 63), including unknown/disagreeing cockpit readings, provider availability, unchanged aircraft/airport/connection guards and two-sector arrival with no brake/engine readings. Packaged startup/shutdown and all **33 WPF checks** pass. Formatting verification passes. Evidence: artifacts/gsx-owned-readiness-tests and artifacts/ui/ui-results.txt.

Current installer: `C:\Projects\ADM\artifacts\releases\ADM-0.8.0-beta.3-local-setup\ADM-Setup.exe`

SHA-256: `7B597A2242B72E562ADBF06CFECF3FCABB5A4616680BEC42BA0AD0544C445F1F`

The read-only live probe on 22 October found MSFS/GSX unavailable. No service was requested, and no new live acceptance is claimed. The native installer mechanism was qualified on beta.2; beta.3's updated self-contained payload passes startup/UI checks. Public release gates remain unchanged.
