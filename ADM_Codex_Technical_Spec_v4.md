# ADM — Aircraft Dispatch Manager
## Codex Technical Architecture & Implementation Backlog v4.0

**Project:** brand-new freeware Windows companion application for MSFS 2024.
**Version:** 4.0 (20 October 2026). Fresh-start project, supersedes v3.0.
**Retained v3 additions:** gate readiness, dispatcher personalities, linked operational disruptions, multi-sector operations, smart recovery and compatibility.
**New v4 capability:** automatic airline profile lookup by validated ICAO ATC callsign designator, first-departure/turnaround/final-arrival per-service policy, aircraft-type override, live-flight commands with explicit precedence.
**Fixed choices:** ADM owns GSX automation; Any2GSX integration-only; balanced realism; essential messages; pilot owns pushback. Do not reuse the older ADM project.

**Pilot amendment (21 October 2026, M7):** Remove deboarding from every user option and all new handling plans/GSX requests. Retain only an internal legacy identifier for saved-record compatibility. Clear the live chat manually, after confirmed airborne telemetry, or on reopening after completed handling; preserve operational audit/history. Restart cancelled services through fresh, explicit intents after GSX availability/reconciliation, without falsely confirming completion. These instructions supersede older deboarding references below.

**Pilot amendment (21 October 2026, simpler everyday UX):** Normal startup automatically connects to MSFS/GSX, remembers the pilot's aircraft integration acknowledgement and reads the feature-detected cached flight plan/planned load figures from the documented GSX handlerData proxy. Remove the separate Desktop SimBrief account/import flow; GSX may still obtain its plan upstream from SimBrief. Missing/unusable data supports manual entry. Never call planned figures a final loadsheet. Prepare automatically only after matching plan and safe fresh telemetry, then ask in Chat before services. Preserve boarding/closure approvals and uncertain-delivery recovery. Keep profile/recovery/history controls available but collapsed. Fuel and catering remain separate same-phase calls; require observed fuel Requested/Active/Completed before catering, rather than transport acceptance alone. A stalled accepted request pauses for explicit recovery; no blind resend. GSX operator questions are answered in the simulator. Existing saved policies remain unchanged. This amendment supersedes conflicting explicit-connect/direct-import/back-to-back timing requirements below. See `docs/simple-ux-report.md` for checks and pending live qualification.

**Pilot amendment (21 October 2026, M7 follow-up):** On every normal Desktop launch, show a clean Chat/current-flight/service state with fresh approvals; archive previous handling and end its duty without cancelling GSX work, falsifying completion or erasing command audit. Preserve profiles, preferences and aircraft setup. This supersedes normal-startup restoration above; within-process connection recovery and explicit backend recovery retain reconciliation safeguards. Simplify profile editing to one selected scope and service choice; support creation/import and discovery of user-defined ICAO airline profiles and aircraft overrides. Add quiet, unattributed observed-service status lines to Chat, with no notification sound, and use the supplied MP3 for ADM notifications. An open GSX menu is not proof of a question; prompt status must be validated and must not generate repeated ADM messages. Aircraft-title comparison ignores letter case; same-plan load updates refresh presentation without mutating active service plans. See `docs/m7-ux-followup.md`.

## 1. Outcomes and boundaries

Build a stylish standalone phone/tablet-like Windows app in C#/.NET 10 WPF. It imports the active SimBrief OFP on user request, monitors MSFS 2024 via SimConnect, controls GSX through the documented Couatl Remote API v2, and simulates an ADM dispatcher with local template-based conversation and pilot-intent recognition. No LLM, keys, subscription, server or internet dependency after OFP import. Multi-sector duty tracking remains active after each ground-handling handover and requires no ADM pushback control. User owns/installs MSFS and GSX separately; Any2GSX is supported but not mandatory for every compatible aircraft.

**Non-goals v1:** GSX pushback initiation/control; paid or local LLM; voice recognition; distributed mobile companion app; general AI chat; injecting automatic commands into undocumented aircraft interfaces; claiming universal door/equipment support.

**Contract:** Never say a service completed without verified status or an explicitly labelled manual acknowledgement. All automatic commands are serialized, validated, journaled and reconciled after reconnect. Unknown status is never assumed complete.

## 2. Integration research and gate

Vendor GSX Manual: local installed `GSX_manual_MSFS.pdf`, Couatl Remote API v2 Developer Guide & SDK Reference. Candidate WebSocket endpoint: `ws://127.0.0.1:8744/`; verify endpoint, handshake, schema, command names, subscription messages, service states, cancellation and door/jetway handling against locally installed manual. No guessed messages. The API adapter should have a capability manifest per version.

MSFS 2024 SDK: official SimConnect managed-client docs. Treat SimConnect dispatch as single-thread-affine, push normalized events to an application channel. Track on-ground, position, ground speed, parking brake, engine state, aircraft title and other only-verified variables. Do not infer gate from coordinates alone without a resolved airport/stand method.

Any2GSX README documents that **automation and aircraft integration are separate**: keep aircraft plugin integration for fuel/payload/equipment sync, but disable its GSX service automation in the applicable aircraft profile. During setup show a checklist, manual acknowledgement, and warning if ownership cannot be verified. Do not pretend ADM can globally toggle Any2GSX settings unless a documented API is found.

SimBrief latest OFP: `https://www.simbrief.com/api/xml.fetcher.php?userid={id}` or username variant; prefer supported `&json=v2` if appropriate after schema validation. Only fetch on explicit user action. Validate OFP date, destination, aircraft and airport against simulator before applying.

### Capability matrix to populate from real SDK

| Feature | Requested | Observed | Evidence | Fallback |
|---|---|---|---|---|
| Refuel request | unknown until validated | unknown | GSX service status | manual instruction |
| Boarding request | unknown until validated | unknown | GSX service status | manual instruction |
| Cancellation | unknown until validated | unknown | cancellation state | operator guidance |
| Door close | aircraft-dependent | aircraft-dependent | SimConnect/GSX if available | ask pilot to close and confirm |
| Jetway/stairs release | to verify | to verify | GSX/equipment status | manual completion |
| GPU/chocks release | aircraft-dependent | aircraft-dependent | plugin/verified telemetry | pilot handles; no false verification |

## 3. Solution structure

```
ADM.sln
src/
  ADM.Desktop/             # WPF shell, views, viewmodels, UI styling, resources
  ADM.Application/         # orchestration use cases, commands, handlers
  ADM.Domain/              # pure entities, state machines, policies, events
  ADM.Contracts/           # stable interfaces, DTOs, enums
  ADM.Infrastructure/      # SQLite, JSON, clocks, random, notification audio
  ADM.Integrations.Gsx/    # WebSocket protocol adapter and capability probing
  ADM.Integrations.Sim/    # SimConnect adapter and normalizer
  ADM.Integrations.SimBrief/ # HTTP import and validation
  ADM.Conversation/        # context, intent parsing, templates, suppression
  ADM.Simulation/          # holds, linked seeded disruptions and resolution
  ADM.Operations/          # gate readiness, sector coordinator, readiness projections
  ADM.Compatibility/       # capability policies, reconciliation and setup audit
 tests/
  ADM.Domain.Tests/
  ADM.Application.Tests/
  ADM.Conversation.Tests/
  ADM.Integration.ContractTests/
  ADM.EndToEnd.Tests/
 docs/
  architecture.md
  gsx-api-capabilities.md
  state-machine.md
  compatibility.md
  release-checklist.md
 profiles/
  airline-default.json
  airport-default.json
  dispatchers-default.json
```

Dependency direction: Desktop -> Application -> Domain/Contracts; Integration projects implement Contracts, and register through DI; Domain does not reference GSX, WPF or SimConnect. Use `Host.CreateApplicationBuilder` / Generic Host, async message pipeline, `Channel<T>` for events, one serialized command processor, cancellation tokens, structured logs, and a clock abstraction. Avoid event handling on the UI thread except through Dispatcher invocation.

Core interfaces (illustrative signatures only, not SDK wire protocol):

```csharp
public interface ISimulatorTelemetry {
    IAsyncEnumerable<SimulatorSnapshot> WatchAsync(CancellationToken ct);
}
public interface IGsxGateway {
    Task<GsxCapabilities> ProbeAsync(CancellationToken ct);
    IAsyncEnumerable<GsxEvent> WatchAsync(CancellationToken ct);
    Task<CommandReceipt> RequestServiceAsync(ServiceKind kind, CancellationToken ct);
    Task<CommandReceipt> CancelServiceAsync(ServiceKind kind, CancellationToken ct);
    Task<CommandReceipt> ExecuteVerifiedActionAsync(GsxAction action, CancellationToken ct);
}
public interface IFlightPlanImporter {
    Task<FlightPlanResult> ImportLatestAsync(string pilotId, CancellationToken ct);
}
public interface IConversationEngine {
    ConversationDecision Evaluate(OperationalContext context, DomainEvent evt);
    PilotIntent Parse(string message, OperationalContext context);
}
public interface IOperationalClock { DateTimeOffset UtcNow { get; } }
public interface IRandomSource { double NextDouble(); int Next(int min, int max); }
```

`CommandReceipt` means accepted/rejected/unknown, **not** completed. Confirm completion from subsequent status observation.

## 4. Domain model and state separation

Entities: `DutySession`, `SectorLeg`, `GateReadiness`, `DisruptionIncident`, `DisruptionEffect`, `RecoveryCheckpoint`, `CompatibilityReport`, `DispatcherPersona`, `FlightSession`, `DispatchAssignment`, `AirlineProfile`, `ServiceInstance`, `OperationalHold`, `PilotApproval`, `ConversationMessage`, `EquipmentRelease`, `CommandJournalEntry`.

`ServiceKind`: Deboarding, Cleaning, Catering, Refuelling, Water, Lavatory, Boarding. Do not include Pushback as an ADM-controlled service.

`ServiceState`: NotRequired, Planned, Eligible, RequestPending, Requested, Active, Completed, Skipped, CancelPending, Cancelled, Failed, Unknown.

`WorkflowStage`: Uninitialized, Planning, ArrivalHandling, PreBoardServices, AwaitingBoardingApproval, Boarding, AwaitingClosureApproval, ClosingAndReleasing, Completed, Suspended, NeedsAttention.

`HoldKind`: SimulatedGate, SimulatedPassenger, SimulatedStaff, RealServiceBlocked, ConnectionIssue, ManualPilotHold.

`GateReadinessState`: Unknown, NotApplicable, Preparing, Held, Ready, Invalidated. `Ready` requires explicit evidence or a labelled **simulated** gate readiness rule; it is never inferred from GPS coordinates. Unknown must not silently become ready.

`DutySession`: persistent parent for multiple `SectorLeg` records, each referencing an independent `FlightSession` and dispatcher assignment. `FlightSession.Completed` means this ground handling cycle ended; it does **not** terminate the duty tracker.

Every domain event has unique ID, session ID, timestamps, event source (`GSX`, `SimConnect`, `User`, `Simulation`, `System`), correlation ID and optional evidence. The command processor journals intent, send attempts and observed outcomes; a reconnect performs state reconciliation and never blindly resends a non-idempotent action.

### Session lifecycle

A new turnaround ID is allocated only on a confirmed new operation or explicit user action. Dispatcher name/persona is sampled once and persisted. SimBrief import does not automatically discard existing service history. First departure / turnaround / final arrival: infer only from actual observed prior arrival or explicit user selection; otherwise ask once. For a multi-sector duty, preserve the completed previous sector and never carry service completions or approvals into a new sector. Arrival at the next destination is a candidate, not proof: detect on-ground and standstill plus parking cues, confirm airport/leg match, and require one-tap confirmation if ambiguous.

### Dependencies

Default sequencing:
- Start of turnaround: deboarding -> cleaning if required -> preboarding services.
- User-requested service-order extension (20 October 2026): water, lavatory, refuelling and catering share the default preboarding phase, listed in that order. Allow configurable phases per operation/profile, preserving mandatory dependencies. Boarding has its own later phase and cannot be configured before or alongside refuelling. Equal phases express desired concurrency; actual overlap still requires validated aircraft/API compatibility. Apply saved ordering changes only to a new operation, preserving the active immutable plan.
- Catering/water/lavatory/refuelling only if required under profile, aircraft capabilities and actual GSX conditions.
- Parallelize only if validated; make compatibility constraint data-driven.
- If refuel required, verified Completed (or intentional authorized Skipped) is required before proposing boarding. Distinguish a skipped service from confirmed refuel achievement.
- Boarding approval prompt requires all hard safety/operations prerequisites satisfied, a `Ready` gate state (or a user-approved gate-check fallback clearly marked as unverified), and **no active simulated boarding hold**. A simulated delay is not called a 'display'.
- If boarding approved and eligibility still valid, request GSX boarding once. If eligibility changes, defer and notify only if material.
- On verified boarding completion, ask permission to close/disconnect.
- With approval, execute supported actions in safe order, monitor confirmation, prompt for unsupported manual door/equipment actions.
- Mark ground handling completed only once relevant work is confirmed or explicitly manually attested. End ADM session and yield pushback entirely to pilot.

## 5. Pilot chat commands, skip vs cancel

Free text and tappable quick replies route to **the same intent handlers**.

Supported intents and example wording:
- `GetStatus` (how's boarding / are we ready / what's left)
- `GetDelay` (what's holding us up)
- `ApproveBoarding` (yes / start boarding / go ahead) **only if context is boarding approval**
- `HoldBoarding` (not yet / hold boarding)
- `ApproveClosure` (yes close up / disconnect) **only if context is closure approval**
- `HoldClosure` (not yet / keep jetway connected)
- `SkipService(kind)` (skip catering / no cleaning needed)
- `CancelService(kind)` (cancel refuelling / stop boarding)
- `ResumeService(kind)` (actually do catering / resume boarding)
- `GetHelp`, `Acknowledgement`, `Unknown`.

**Skip** means never request a not-yet-started service; transition Planned/Eligible -> Skipped with audit and reason, then re-plan. Skip active service must produce a clarification: it requires cancellation, not silent skipping. **Cancel** means stop a requested or active service using a verified supported GSX action; move to CancelPending until cancellation is confirmed. If cancellation unsupported, explain that and show manual GSX fallback. Never claim success based only on command acceptance.

Some services are mandatory by profile or current state. Skipping/refuelling, boarding, or release obligations might make the flight not ready. Warn and require explicit confirmation for mandatory skipping; do not bypass real safety/precondition checks. Cancelled boarding invalidates closure readiness until separately resolved. A new approval is required after an impactful state change; old approvals expire on session switch, disconnect, service failure, or relevant hold.

Parser rule: normalize whitespace/case, support natural synonyms, resolve 'it' only from an unambiguous current active intent, prefer the pending question for yes/no, ask a one-line clarification for ambiguous service/action. No keyword-only `yes` command without context. No invented actions. Confirmation in chat must have a corresponding record.

## 6. Human-feeling essential messages

Dispatcher selected from a configurable roster; name and persona stable for the operation. Personalities have a stable `personaId`, name, vocabulary/tone, contraction/formality settings, and pre-approved message template variants by intent. Personas **do not** affect safety gates, service sequencing, delay probability, telemetry interpretation, or command permissions. Profiles specify tone, formality, light emoji frequency and region-appropriate phrasing. Templates must preserve the same operational meaning regardless of persona. Default essential messaging:

1. Welcome when a *new* operation begins.
2. Meaningful exceptional hold or service problem, with updates only when resolved/materially changed.
3. Boarding approval prompt when fuel and all other mandatory preboarding conditions are satisfied.
4. Closure/disconnection approval prompt when boarding is verified complete.
5. Ground handling completion and pilot pushback handover.
6. Answers to explicit pilot questions.

Routine catering/cleaning/refuel progress is visible in the Status view but not automatically sent unless it affects pilot action or delay. Deduplicate by semantic message key, session and stage; no repeated push notifications from repeated GSX status events. Message templates have multiple variants, weighted choice and recent-used avoidance. Show messages without typing delays that obstruct actions. Notification ping only for important incoming messages; settings for mute, volume and do-not-disturb.

Example happy-path conversation:

ADM: "Hi! I'm Sarah, your ADM for today's flight to Amsterdam. I'll take care of the ground services and keep you posted."
ADM: "Fuel's complete and we're ready for boarding. Happy for me to get everyone on?"
Pilot: "Yes, go ahead."
ADM: "Perfect, I'll get boarding started."
ADM: "Everyone's on board. Shall we close up and disconnect?"
Pilot: "Yes please."
ADM: "All finished this end, doors and ground equipment confirmed clear. Pushback's over to you. Have a good flight!"

Only use the final verified wording when evidence exists. If no door/equipment telemetry, write "I've finished the GSX services. Please confirm the doors and equipment are clear before pushback." and retain a pending manual-confirmation state.

## 7. Balanced realism

Default: ~25% of eligible turnarounds receive **at most one originating simulated disruption incident** (user-adjustable; not a promise of real-world airline statistics). Candidate durations 2–7 minutes with weighted samples, and a hard configurable cap at 10 minutes; hold only if safe, service compatible and no unresolved real delays. Use deterministic seeded random per session for test repeatability; no invented maintenance failures, security incidents or flight-critical failures. A real GSX block supersedes a fictional delay in priority. Each originating `DisruptionIncident` may create dependent `DisruptionEffect` holds, such as gate staff issue -> postponed gate ready -> later boarding approval -> revised estimated ready time. This is **one causal incident**, not three separate random disruptions. Track parent ID, affected prerequisites, start/end time, expected resolution and effective stop conditions. Effects must clear when their parent clears unless separately blocked by a verified real event. Do not stack unrelated delays by default. Holds can be turned off or skipped via chat: "skip the gate delay"; clearing the incident also clears its simulated effects only. Real GSX failures persist. While a simulated hold exists, do not ask permission to board until cleared; if pilot asks why, give a concise explanation.

## 8. UI experience and layout

Modern narrow rounded phone/tablet form in a resizable Windows window (minimum ~420x640 logical pixels), responsive tablet layout at wider widths, accessible keyboard navigation and large tap targets. No native phone simulation or web backend needed. Use WPF styling/resources and custom vector icons only where appropriate.

Add a tiny Gate status chip (`Gate ready`, `Gate hold`, `Gate unknown`) next to the stage indicator, a persistent sector breadcrumb/history in Flight view, a connection-recovery banner when status is unknown, and dispatcher profile preview in Settings. None should create essential-message noise.

Primary surface: Conversation (60-70% visual prominence). Header: dispatcher avatar/name, flight number and route, compact real connection status. Chat bubbles, sender/time, message input, quick-reply chips shown only when helpful. Bottom navigation: Chat, Services, Flight, Settings. Tablet width shows collapsible service/status rail beside conversation. Services view exposes clean progress timeline and context actions `Skip`, `Cancel`, `Resume`, with confirmations and status indicators. Flight view: imported plan, aircraft and service profile. Settings: SimBrief, notifications, realism, aircraft/airline profiles, Any2GSX checklist, diagnostics. Always distinguish Verified / Pending / Needs Pilot Action / Unknown. Graceful offline mode.

Colour system: deep navy/charcoal, restrained teal accent, neutral surfaces, adaptive light/dark theme; do not use amber/red for decoration. UI must be polished yet compact, no screen-wide popups for routine updates. Keyboard Enter sends and Shift+Enter adds newline. Notification audio must be licensed or original for redistribution.

## 9. Persistence, logs and privacy

SQLite tables: duty_sessions, sector_legs, gate_readiness_history, disruption_incidents, disruption_effects, recovery_checkpoints, compatibility_reports, dispatcher_personas, sessions, flight plans, service instances, approvals, holds, conversation messages, events, command journal, airline profile selections, airline_profiles, airline_aliases, aircraft_profile_overrides, applied_profile_snapshots, profile_audit_log. Store settings JSON in per-user app data and never require elevation. SQLite migrations and restore on restart. Local log rotation with opt-in diagnostic export, redact unnecessary SimBrief account data. Don't send chat text to any server. Flight plans are cached locally. Make no automated SimBrief polling.

## 10. New v3 subsystems: normative contracts and decision rules

### 10.1 Gate readiness coordinator

**Purpose:** prevent boarding release simply because fuel is done. Gate readiness is a separately evaluated requirement.

Inputs: flight/airport/stand if available, configured airline/airport handling rules, current simulated gate incident, explicit pilot or dispatcher acknowledgement, and any genuine provider observations. GSX boarding eligibility is an additional constraint; GSX does not automatically prove real gate readiness. Offline/default operation uses a clearly identified simulated gate state, not invented external telemetry.

Rules:
1. At each sector start initialize gate to `Unknown` or `Preparing`; airport profiles can set a minimum preparation period and applicability, but may not bypass safety prerequisites.
2. Simulated readiness transitions `Preparing -> Ready` after configured gate checks/hold resolution; mark provenance `Simulation`. If a real external source is later supported, provenance `VerifiedExternal` is permitted only with documented evidence.
3. `Held` blocks requesting boarding and blocks issuing the pilot boarding prompt. An approved pilot request during the hold is remembered only as a request, **not** as a durable authorization; request new approval when ready.
4. `Unknown` triggers one concise check or a clear user acknowledgement path. Record `PilotAttested` provenance; do not mislabel it as externally verified.
5. Any new qualifying gate hold invalidates a prior `Ready` state and boarding approval unless boarding is already confirmed active, in which case apply a safe, documented disruption policy and report a condition change.
6. Show gate state silently in the Services view; text only for significant hold, release after hold, or explicit pilot question.

Suggested interface:

```csharp
public interface IGateReadinessCoordinator {
    GateReadiness Evaluate(SectorContext sector, OperationalSnapshot snapshot);
    GateTransition Apply(GateEvent evt, GateReadiness current);
}
```

### 10.2 Dispatcher personality catalogue

Store JSON `DispatcherPersona` entries with stable IDs, display name, tone enum (`FriendlyConcise`, `FormalConcise`, `WarmProfessional`), template set, regional spelling and supported language. Select once per **ground operation**, seeded for reproducible testing, and persist on creation. Keep the same persona during recovery; a newly arrived sector may choose a different dispatcher. Add weighted variants per *semantic* message key and prevent repeats within a sliding window. Persona changes cannot alter a service command, an approval requirement, an evidence qualification, or the event suppression policy. Avoid unverifiable personal backstories or excessive chatter.

Sample templating:

```json
{
  "id": "warm_professional_01",
  "name": "Sarah",
  "tone": "WarmProfessional",
  "templates": {
    "boarding.approval": [
      "Everything's ready at the gate. Happy for me to start boarding?",
      "We're ready for boarding this end. Shall I get them on?"
    ],
    "closure.approval": [
      "Everyone's on board. Happy for us to close up and disconnect?"
    ]
  }
}
```

Template variables must come from known session data; no generative API or internet dependency.

### 10.3 Linked disruption engine

Model `DisruptionIncident` as the root and dependent `DisruptionEffect` records referencing it. One seeded selection per eligible turnaround, default probability 25%, duration 2-7 minutes, cap 10 minutes, with 0 or 1 *originating* simulated incident. Effects describe downstream consequences, **not** new randomly sampled delays. Eligible examples:
- Gate-document query -> gate Held -> boarding approval withheld -> ready-time forecast moves.
- Gate staff late -> gate Preparing extended -> boarding deferred; cleaning proceeds if permitted.
- Passenger processing issue -> temporary boarding readiness hold; if boarding is active, only pause if a supported and safe real action exists; otherwise communicate limitation.

Forecast delays are estimates labelled as simulated; no invented live gate staff/real-world operational data. Coalesce effects into at most one first essential message plus a resolution message when material and not redundant with boarding permission. Allow `skip gate delay` or `skip simulated disruption` to clear only simulation-owned holds. A real GSX blockage remains independently active. Re-evaluate all dependent conditions in a single transactional domain event.

### 10.4 Multi-sector duty lifecycle

Concepts: `DutySession (1) -> SectorLeg (N) -> GroundHandlingSession (1 per sector)` with immutable historical status, SimBrief plan snapshot and dispatcher identity per sector.

States for duty tracker: `Idle`, `GroundHandling`, `ReleasedForFlight`, `EnrouteObserved`, `ArrivalCandidate`, `AwaitingNextPlan`, `Paused`, `Ended`. These are **not** ground service states. When ground session completes, persist it, enter `ReleasedForFlight`, and do **not** command pushback, taxi or departure. SimConnect later observes takeoff/enroute/arrival; avoid assuming a new sector based only on time at a stand or simulator reload. On destination arrival, match airport/position and stationary/parking evidence, dedupe repeated events, then create an `ArrivalCandidate`. Offer `Start turnaround` or `End duty`. Next flight SimBrief import is explicitly user-triggered; allow manual flight entry offline. Create a fresh sector ID, dispatcher persona, service graph, gates, seeded disruption, and empty approvals. Never copy service Completed/Skipped/Cancelled states between sectors. Keep a simple read-only flight history in the UI.

Exceptional paths: flight diversion, go-around, return to origin, teleport, sim reload, changed OFP and startup parked at destination should require pilot confirmation of sector transition. User can manually start a new sector or end duty. If last location matches new leg departure but no arrival telemetry exists, request confirmation rather than fabricating deboarding.

### 10.5 Smart recovery and compatibility

Compatibility tiers by aircraft and setup: `Validated`, `PartiallySupported`, `ManualAssist`, `Unknown`, based on verifiable GSX capabilities, SimConnect status, configuration attestation, and optional Any2GSX setup. Onboarding asks the pilot to turn **off** Any2GSX GSX service automation for the active aircraft profile while retaining needed aircraft integration; show an explicit acknowledgment and offer instructions, but never assert the setting was verified absent a supported introspection API. Do not modify Any2GSX configuration files without a documented, user-authorized mechanism.

Recovery sequence:
1. Pause **new** GSX outbound commands on connectivity loss or ambiguous acknowledgment.
2. Persist the event journal, previous command correlation, session ID and most recent known verified service states.
3. Reconnect with exponential backoff and jitter; show `Reconnecting` without repetitive chat/notification alerts.
4. Probe compatible remote API version/capabilities and reacquire authoritative GSX service/equipment snapshots.
5. Reconcile pending requests/cancels from observed facts. If evidence is insufficient, mark `Unknown` and ask for manual review; never automatically repeat a potentially non-idempotent service request.
6. Re-evaluate eligibility and invalidate approvals whose prerequisites or sector identity changed.
7. Resume only safe future scheduling after an explicit reconciliation checkpoint. Journal reason and outcome.

Prevent duplicate command dispatch via persistent `(sectorId, serviceKind, commandIntentVersion)` deduplication and one serialized outbound worker. Other GSX control surfaces can change states externally; detect and respect verified changes, do not fight them. Provide diagnostics export with remote API version, adapter capability matrix, current service snapshot, last disconnect/reconnect, and redacted journal; omit identifiers not required for troubleshooting.

Suggested interfaces:

```csharp
public interface IDutyCoordinator {
    DutyTransition Apply(DutyEvent evt, DutySnapshot current);
}
public interface IDisruptionEngine {
    DisruptionDecision Evaluate(SectorContext context, OperationalSnapshot snapshot);
}
public interface IRecoveryCoordinator {
    Task<RecoveryOutcome> ReconcileAsync(RecoveryCheckpoint checkpoint, CancellationToken ct);
}
public interface ICompatibilityAdvisor {
    CompatibilityReport Assess(IntegrationCapabilities capabilities, UserSetupAttestation setup);
}
```

### 10.6 Mandatory cross-feature invariants

- No gate-ready state, dispatcher persona, delay estimate or multi-sector state can override verified service eligibility or pilot approval.
- Simulated delays remain visibly distinguishable in the internal model from real GSX events, even when the conversational wording is natural.
- Skip/cancel/resume commands apply to **the active sector only**. Require explicit clarification if sector context is ambiguous.
- Approval to board or close is scoped to a single session and expires after material prerequisite changes, disconnect or sector switch.
- Never replay commands merely because the app restarted, an aircraft changed or a new SimBrief OFP arrived.
- All essential messages are driven by changed semantic meaning, not repeated telemetry.
- ADM does not request, automate or manage pushback at any point.

### 10.7 Airline ICAO profile resolution and inheritance (new v4 normative subsystem)

**Objective:** automatically bind airline-specific GSX service choices to a validated three-letter ICAO airline/operator designator such as `RYR`, `BAW` or `UAL`, persist once and reuse across flights. These are configurable **user preferences**, not representations of verified airline procedures. The system must work offline using shipped/user-created profiles and an optional locally bundled designator catalogue with clear source/version.

**Callsign source precedence:** (1) explicit per-flight airline override; (2) SimBrief *ATC callsign* from an explicitly imported current OFP; (3) simulator ATC callsign when verified readable; (4) manual airline selection; (5) `GENERIC`. The displayed commercial flight number (`BA123`) is **not** equivalent to the ICAO ATC callsign (`BAW123`). Normalize whitespace/case, parse a leading exactly three-letter `[A-Z]{3}` designator followed by a plausible suffix, and validate the designator against an offline allowlist or a user-defined alias; never automatically infer from an aircraft registration (`G-ABCD`, `N123AB`), airline display name, arbitrary three leading letters, or an unknown abbreviation. Callsigns such as `BAW7AB` should be accepted if suffix grammar allows alphanumeric characters. When two credible sources disagree, show the chosen source in the Flight view and permit user override; do not silently switch an active plan. Store parsed designator, source and confidence/provenance in `AirlineResolution`. A callsign matching a designator does not prove the operating carrier (e.g., wet lease); support a manual override.

**Layered resolution and precedence, lowest to highest:** `GenericDefaults` -> `ICAO AirlineProfile` -> `AircraftFamily/AircraftTypeOverride` -> `CurrentFlightPilotOverrides`. Aircraft matching must use a canonical type identity (prefer verified ICAO type designator from OFP, e.g. `A20N`, `B738`, `B77W`) and optionally a configurable aircraft family (`A320_FAMILY`, `B737NG`); do not blindly use simulator marketing titles. Exact type overrides supersede family overrides. Add aliases only with explicit user mappings. Save a **resolved immutable snapshot** of profile revision + source per `GroundHandlingSession` before dispatching its first GSX command. New SimBrief import or changed callsign while services are underway must never silently mutate current service requirements; show a non-blocking change notice and offer a user-confirmed replan of only untouched services. Existing GSX requests and verified completions remain immutable historical facts.

**Policy per flight type:** `FirstDeparture`, `Turnaround`, `FinalArrival`. Each service has `Automatic`, `Ask`, or `Disabled`, plus dependencies, required/optional flag and applicability rule. `Automatic` on **boarding means prepare and offer a boarding approval prompt**, not start boarding without pilot consent. `Ask` for a non-boarding service requires an explicit pilot approval before request. `Disabled` means not scheduled, not forcibly cancelled if already active. Mandatory physical/safety constraints, GSX capability rules and real observed state **always override** profile/user preferences; manual skip/cancel requests may not falsify achievements or bypass hard prerequisites. When profile disables a service, record `NotRequired` with policy evidence rather than `Completed`. If skipping refuelling, do not call it complete or assume required fuel is aboard.

**Persistence and editor:** Store profiles as versioned, human-readable JSON under per-user app data (with safe atomic writes, backups and schema validation). `GENERIC` ships in-app, editable clones save by canonical ICAO key (`RYR.json`, `BAW.json`, `UAL.json`). Keep a JSON catalogue of optional ICAO-to-display-name mappings independent of service preferences. Provide UI to view detected callsign/source, select/override airline, edit services per flight type, select aircraft family/type overrides, preview the resolved plan, duplicate/reset/import/export profile and verify incompatible/unsafe choices. For an unknown ICAO designator, use `GENERIC` until the user creates or maps a profile. No online lookup or additional account is needed.

**Example illustrative (not actual airline SOP):**

```json
{
  "schemaVersion": 1,
  "icao": "RYR",
  "displayName": "Ryanair",
  "revision": 1,
  "operations": {
    "FirstDeparture": {
      "Catering": "Ask",
      "Cleaning": "Ask",
      "Refuelling": "Automatic",
      "Boarding": "Automatic"
    },
    "Turnaround": {
      "Catering": "Disabled",
      "Cleaning": "Automatic",
      "Refuelling": "Automatic",
      "Boarding": "Automatic"
    },
    "FinalArrival": {
      "Deboarding": "Automatic",
      "Boarding": "Disabled"
    }
  },
  "aircraftOverrides": {
    "B738": {
      "Turnaround": { "Catering": "Ask" }
    }
  }
}
```

In the code use strongly typed, schema-validated policy objects instead of stringly typed decisions. Example contracts:

```csharp
public interface IAirlineIdentityResolver {
    AirlineResolution Resolve(FlightIdentity identity, UserAirlineOverride? manualOverride);
}
public interface IAirlineProfileRepository {
    Task<AirlineProfile?> FindAsync(string icao, CancellationToken cancellationToken);
    Task SaveAsync(AirlineProfile profile, CancellationToken cancellationToken);
}
public interface IServicePlanResolver {
    ResolvedServicePlan Resolve(AirlineProfile genericProfile, AirlineProfile? airlineProfile,
        AircraftIdentity aircraft, FlightOperationType operation, PilotServiceOverrides overrides);
}
```

**Failure handling:** malformed JSON -> preserve last valid revision and fallback to generic with visible diagnostic; unknown prefix -> generic without guessing; conflicting imported callsign -> ask user; aircraft type unknown -> use airline-level profile; policy edits during active service -> deferred to next session or pilot-approved safe replan of unstarted work; profile import -> validate schema/ICAO code/unsafe dependencies before saving. Every generated command must record its source policy and session snapshot revision for recovery/replay auditing.

### 10.8 Profile-aware multi-sector behaviour

On confirmed next-sector creation, resolve the newly imported ATC callsign, ICAO airline profile and aircraft override again, even within the same duty. Different ICAO designators can appear in different sectors; do not automatically carry a prior airline preference across them. An unchanged airline uses the latest saved profile **for the new sector only**. The new `GroundHandlingSession` persists its own immutable resolved plan, and the previous sector remains fully auditable. A pilot can review and edit the next sector's planned services before command dispatch. No additional SimBrief polling.

## 11. Milestone backlog and exit gates

### M0 — Repository and foundation
- [ ] Create fresh repository/solution and projects as described (do not import old sources).
- [ ] Set .NET 10 WPF x64, CI build, analyzers, formatting, logging, DI and test projects.
- [ ] Define immutable DTOs, interfaces, clocks, events and enums.
- [ ] Define separate DutySession / SectorLeg / GroundHandlingSession and gate readiness contracts.
- [ ] Add `AirlineResolution`, ICAO designator validation, layered profile-resolution and immutable applied-plan contracts.
- [ ] Add fake SimConnect/GSX adapters and sample session fixtures.
- [ ] `dotnet build ADM.sln` and `dotnet test ADM.sln` pass.
**Exit:** fresh app opens, tests work, no simulator dependency to run.

### M1 — Premium desktop experience
- [ ] Implement shell, dark/light styling, compact/chat/tablet responsive layouts.
- [ ] Build message cards, input, timestamp, contextual quick replies, flight header.
- [ ] Services progress timeline, flight/settings pages, status indicators.
- [ ] Gate state chip, sector history panel, recovery banner and persona preview.
- [ ] Add airline detection badge/source, per-operation service editor, aircraft override editor and plan preview using mock data.
- [ ] Local incoming sound, settings and test preview.
- [ ] Keyboard/accessibility UI testing.
**Exit:** polished mock end-to-end happy-path demo, no real external calls.

### M2 — Domain workflows and messaging
- [ ] Implement state machine and guard checks with immutable event reducer.
- [ ] Service dependency graph, planning rules, first/turnaround/final profile rules.
- [ ] Offline ICAO parsing/validation, generic->airline->family->type->flight override resolution, profile JSON schema validation and snapshot tests.
- [ ] `Skip`, `Cancel`, `Resume`, approvals and intent parser with clear responses.
- [ ] Template variants, contextual de-duplication and persisted dispatcher identity.
- [ ] Create validated dispatcher persona JSON catalogue with stable tone and reproducible selection.
- [ ] Gate readiness reducer and source-qualified gate holds (simulated, pilot-attested, externally verified).
- [ ] Tests for repeated events, ambiguous yes/no, mandatory skip, pending cancel.
**Exit:** complete simulated turnaround through handover in deterministic tests.

### M3 — Integration proof: GSX and MSFS
- [x] Locate and review installed vendor GSX Remote API v2 guide; record supported schema.
- [x] Build WebSocket protocol adapter with capability probing and diagnostics.
- [x] Read actual GSX service states; manually request safe service in test environment.
- [x] Verify any cancellation support before enabling cancel control (no documented verb; real cancellation disabled).
- [x] Build SimConnect adapter and normalized telemetry, disconnect/reconnect tests.
- [x] Map observed API evidence into domain, never fabricate success.
**Exit:** real GSX request and authoritative state change demonstrated, with log capture; all unsupported actions clearly disabled.

### M4 — SimBrief, Any2GSX, and automated service control
Implementation status (21 October 2026): code and offline checks are available. The pilot exercised water, lavatory skip and profile saving; the subsequent water-to-idle pause is corrected in M5. Full simulator acceptance is still pending. Checkboxes remain open until that gate is reviewed. See `docs/m4-report.md` and `docs/m5-report.md`.
- [ ] Explicit SimBrief import with validation and local cache.
- [ ] Implement versioned airline profile JSON repository, editing/validation, generic default, aircraft capability matrix and atomic save/rollback.
- [ ] Import validated ATC callsign from SimBrief, apply ICAO profile using documented precedence, and show provenance/override.
- [ ] Apply per-flight-type `Automatic`/`Ask`/`Disabled` policies with aircraft-type inheritance and immutable current-session snapshot.
- [ ] Any2GSX coexistence onboarding: per-aircraft GSX automation off, integration on.
- [ ] Connect scheduler to real adapters with command journal and retry discipline.
- [ ] First-departure and turnaround planning, allowed parallel-service rules.
- [ ] Recovery from reload, disconnect, service failures, and external manual GSX changes.
- [ ] Capability/compatibility report with manual checklist and safe fallback.
- [ ] Implement durable command-reconciliation checkpoints and Unknown state fallback.
**Exit:** first/turnaround workflow reaches AwaitingBoardingApproval without duplicate commands.

### M5 — Boarding, closure, and handover
Implementation status (21 October 2026): boarding approvals/dispatch, observed or explicitly attested completion, manual closure/release and pilot handover are implemented and covered offline. No automatic release command has been verified for the current manifest. Live exit acceptance remains pending; see `docs/m5-report.md` and `docs/m5-live-test-guide.md`. Pilot clarification: optional routine active-to-idle service cycles advance automatically as CycleEnded without claiming verified completion; fuel, boarding and required work cannot use that shortcut.
- [ ] Ask pilot permission to board only after verified prerequisites, gate readiness, and hold clearance.
- [ ] Initiate boarding on approval and observe real completion.
- [ ] Ask permission to close/disconnect.
- [ ] Implement verified GSX/aircraft release actions where supported.
- [ ] Request pilot manual closure/disconnect where not supported and record confirmation.
- [ ] Mark completed only on verified/attested satisfaction of configured release criteria.
- [ ] Verify no ADM pushback initiation or control under any path.
**Exit:** real flight goes from arrival/services to safe pilot handover, without premature closure.

### M6 — Operational variability and polish
Implementation status (21 October 2026): implemented and verified against the offline exit gate: 50 completed simulated sessions, 13 linked incidents, six service-start phrase variants, no duplicate requests or polling chatter. See `docs/m6-report.md` for results and limits. Full M4/M5 live acceptance remains pending; M7 has not started.
- [x] Balanced simulated gate/passenger/staff holds with deterministic seeds.
- [x] Causal linked incident graph with dependent holds and ready-time impacts, max one incident by default.
- [x] Hold skip, delay status and conflict handling.
- [x] Essential-message suppression and multiple varied phrasing templates.
- [x] Per-airline tuning, flight-type policy editor, aircraft family/type override previews and profile import/export.
- [x] Personality-consistent templates across all essential messages.
- [x] Airport-specific gate readiness profiles with generic default and fallback.
- [x] Notification audio, accessibility, translations-ready resource strings.
**Exit:** 50 simulated sessions show variety without duplicate/false messages, unnecessary delays or chat flooding.

### M7 — Multi-sector duty operation
Implementation status (21 October 2026): implemented and verified offline through two consecutive completed handling sectors, both continuous and across a SQLite restart. BAW and RYR use independent profile snapshots, dispatchers, commands and approvals. All arrival candidates require explicit next-sector/location confirmation. Pilot-requested chat lifecycle, service restart and deboarding removal are integrated. See `docs/m7-report.md` and retained synthetic evidence. Real simulator qualification remains pending; M8 has not started.
- [x] Parent duty tracking with archived ground operations and independent sector IDs.
- [x] Confirmed arrival candidate identification with telemetric airport/stand checks and user confirmation when ambiguous.
- [x] New dispatcher, fresh service states and approvals for every new sector; previous sector remains read-only.
- [x] Re-resolve new sector ICAO airline and aircraft overrides; prevent mid-session imported callsign changes from rewriting active service state.
- [x] Manual next-leg entry and user-triggered SimBrief import; handle changes/diversions/return-to-origin/teleport.
- [x] Resume duty after restart using recovery coordinator; verify no cross-sector command replay.
- [x] Multi-sector regression: first flight -> release -> sim departure -> arrival -> turnaround -> release -> next leg.
**Exit:** at least two consecutive sectors complete without duplicated handling, unintended auto-pushback or inherited approvals.

### M8 — Community beta and distribution
- [ ] Real-device testing matrix: GSX versions, multiple aircraft, with/without Any2GSX.
- [ ] Verify SDK redistribution rights for SimConnect binaries before bundling.
- [ ] User-friendly install and clean uninstall; offline/first-run validation.
- [ ] Privacy notice, license review, third-party attributions and diagnostic export.
- [ ] Produce self-contained Windows release installer after validating binary licensing.
- [ ] Release notes, README, troubleshooting and test evidence.
**Exit:** clean Windows PC install; GSX compatibility caveats clear; all regression tests pass.

Implementation status (22 October 2026): M8 presentation cleanup, contextual recovery, privacy/support export, notices and self-contained local qualification packaging are implemented. Release build, 341 xUnit tests, 28 WPF checks and 13 isolated installer checks pass. Pilots do not need the SDK or a separate .NET installation. Public acceptance is not complete: clean-PC/live matrix and exact runtime/asset redistribution decisions remain open. No public upload has occurred. See `docs/m8-report.md` and `docs/release-checklist.md`.

Pilot-requested UI amendment: replace Services with Profiles, simplify Settings/Flight, remove Final arrival from public handling/profile choices (legacy data remains readable), and offer specific recovery actions in Chat. This supersedes older UI descriptions; operational fuel/boarding safeguards and pilot-owned pushback remain binding.

## 12. High-value acceptance tests

1. No SimBrief or Internet: manually selected flight supports offline mock/service operation when local simulator works.
2. Refuel active: no boarding clearance prompt until confirmed completed (unless legitimately not required and safely bypassed).
3. Simulated boarding hold: no boarding prompt until it ends or pilot skips the *simulated* hold.
4. User says "skip catering" before request: service never dispatched.
5. User says "cancel catering" while active: UI enters CancelPending; only becomes Cancelled on observed confirmation.
6. User says "yes" with no pending question: ADM asks for clarification, does not issue GSX command.
7. During boarding "cancel boarding": don't ask for closure; reacquire eligibility if resumed.
8. Boarding completed but closure approval unanswered: no auto door/jetway release.
9. User approves closure but door cannot be controlled: request manual action and confirmation, no fabricated completed status.
10. Disconnect GSX during cancellation: remain Unknown/NeedsAttention; do not replay blindly after reconnect.
11. Any2GSX running in integration-only setup: zero competing automatic service calls from it in tested scenario.
12. At Completed, zero GSX pushback commands from ADM; responsibility clearly handed to pilot.
13. Reopen ADM mid-turnaround: restore service state and dispatcher identity, reconcile before new actions.
14. 100 repeated identical GSX updates: zero duplicated pilot messages.
15. Mixed airline settings and flight types: no needless refuel/catering required for a true turnaround profile.

16. ATC callsign `BAW482` maps to validated `BAW` profile; commercial flight number `BA482` by itself does not infer `BAW`.
17. Unknown prefix `XYZ123` absent validated catalogue/alias -> `GENERIC` profile, not a guessed airline.
18. Registration `G-ABCD` or `N123AB` cannot be parsed as an airline designator.
19. Generic->airline->family->exact-aircraft->current-flight override precedence is deterministic; e.g., BAW `A20N` uses type-level policy while BAW `B77W` uses airline/default policy.
20. `RYR` turnaround `Catering:Disabled` generates no catering request; first departure may have a different policy.
21. `Boarding:Automatic` never dispatches before gate ready, mandatory prerequisites, simulated hold clearance and explicit pilot approval.
22. Profile edited after GSX catering request does not cancel or rewrite active service; only unstarted steps can be replanned with confirmation.
23. Multi-sector `RYR` then `BAW`: each sector has distinct profile snapshot, independent service history and immutable completed history.
24. Damaged profile JSON falls back safely to last valid/generic and displays a diagnostic; no fabricated service completion.
25. Manual pilot `skip`/`cancel` takes precedence over future scheduling, but no override bypasses hard safety requirements or verifiable service evidence.

## 13. Exact Codex instructions

> Create a **new** `ADM` repository and implement only milestone M0, then M1, then M2 in successive verifiable increments. Do not reuse any files from my previous ADM project. Read `ADM_Codex_Technical_Spec_v4.md` as the binding functional and architectural reference. Never invent external API commands. Stub real integrations behind interfaces until M3 when you can check vendor documentation. For every milestone, run `dotnet build` and `dotnet test`, fix all failures, and report changed files, test output, unresolved API dependencies and concrete next steps. Do not advance to the next milestone without the current milestone's exit gate passing. Keep pushback entirely under pilot control and keep simulated delay/state separate from real GSX evidence. Implement new v4 ICAO airline identification and deterministic profile inheritance as specified in §10.7–10.8; add its contracts and fixtures in M0 but do not jump milestones.

## References (verification starting points)

- Official MSFS 2024 SimConnect SDK: https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/simconnect-api-reference/
- Any2GSX README: https://github.com/Fragtality/Any2GSX
- SimBrief latest OFP: https://beta.docs.navigraph.com/simbrief/fetching-ofp-data
- SimBrief polling and JSON v2 notes: https://forum.navigraph.com/t/fetching-a-users-latest-ofp-data/5297
- GSX Remote API developer reference is in the installed GSX manual; validate protocol details locally.

## M8 delivery follow-up — 22 October 2026

Pilot delivery now uses conventional ADM-Setup.exe with the supplied icon and 25 default profiles. Personal edits take precedence. Configured GitHub builds check stable releases automatically; verified installation is explicit and waits for inactive handling. This checkout is not linked/uploaded yet. Native Windows framing/dropdown cleanup addresses input interception; imported flight context selects Profiles automatically. The current Fenix A319 parked brake mapping is verified read-only. Release build, 357 regression tests, 33 UI checks and 12 native installer checks pass. Public M8 acceptance still awaits licensing/signing, clean-PC and live matrix gates. See docs/m8-distribution-followup.md.

## Pilot amendment — GSX-owned cockpit readiness, 22 October 2026

Remove ADM's duplicate brake, engine, on-ground and stationary prerequisites from handling preparation/continuation/request dispatch; rely on GSX's normal service state machine and configured aircraft checks. Remove brake/engine polling and the aircraft-specific brake adapter. Retain SimConnect identity/passive flight-phase tracking, current connection/flight binding, GSX availability/waiting/input guards, approval/dependency/idempotency and evidence requirements. This supersedes earlier parked-state acceptance requirements and the temporary Fenix mapping. See docs/gsx-readiness-policy.md.
