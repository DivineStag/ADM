# M0 architecture

The M0–M4 sections record those historical milestones. The current live implementation is described in the M5 section at the end.

`ADM.Domain` has no package or project dependencies. It contains immutable records/enums, explicit evidence and foundational service transitions. `ADM.Contracts` depends on Domain and contains stable DTOs and ports. `ADM.Application` depends on both and owns the sample factory, pure foundation policy/identity resolution, and hosted inbound monitor.

`ADM.Infrastructure` provides the operational clock and local diagnostics logger. `ADM.Integrations.Gsx` and `ADM.Integrations.Sim` implement the ports with in-memory fakes. The Desktop composition root registers them explicitly; no runtime switch can accidentally select a real provider. A Generic Host starts/stops the monitor. A bounded channel feeds one observer; integration streams are consumed outside the UI thread. No outbound scheduler is registered in M0. The future serialized command processor must journal before dispatch and reconcile before replay.

`ADM.Desktop` uses a viewmodel as its XAML DataContext. The M0 viewmodel is read-only and needs no change notification or command framework. The minimal shell proves WPF startup and DI; chat/navigation/themes and responsive behavior belong to M1. The desktop is `net10.0-windows` with `PlatformTarget=x64`; the reusable libraries and tests target `net10.0`.

`ADM.Integrations.SimBrief`, `ADM.Conversation`, `ADM.Simulation`, `ADM.Operations` and `ADM.Compatibility` are intentional project scaffolds with contract dependencies and no implementation. They reserve module boundaries without introducing premature M1–M7 behavior.

## Domain boundaries

Duty identity outlives each sector. Every sector has its own ground session, dispatcher assignment, immutable applied plan and scoped approvals. New fixtures generate independent identities and never copy service outcomes. Actual sector detection and persistence are deferred.

Gate readiness starts Unknown. Provenance distinguishes simulation, pilot attestation and verified external evidence. Holds retain their origin so a future simulated-delay skip cannot remove a real blockage. Completion provenance remains separate from command acceptance.

Profiles use immutable maps, typed policy enums, required flags, applicability rules and dependencies. The foundation resolver overlays generic, airline, family, exact type and current-flight choices, recording source/revision for each service. It validates the baseline/schema version only; full schema/dependency validation, capability guards, mandatory skip confirmation and safe replanning belong to M2/M4. A resolved Automatic policy never sends anything in M0.

Airline identity resolution is a pure offline function over explicitly supplied validated codes. It ignores unverified simulator callsigns, commercial flight numbers, registrations and unknown prefixes. Callsign suffix grammar is 1–7 alphanumeric characters beginning with a digit. It exposes conflicting credible sources and records precedence/provenance. It cannot mutate an active session. Alias storage/catalogue loading and import validation are deferred.

## Test boundaries

Domain tests cover legal/illegal edges, pending cancellation, evidence requirements, approval revision/session invalidation and hold origin. Application tests cover ICAO syntax, source precedence/conflict, immutable snapshots, profile layering and JSON fixture compatibility. Contract tests cover fake capability rejection, cancellation tokens, explicit simulation evidence and concurrent duplicate intents. Conversation tests lock down initial intent/action contracts only. Host composition tests verify offline startup/shutdown and independent fixture sessions.

Fakes do not validate GSX wire behavior and use memory-only deduplication. Durable idempotence, recovery checkpoints and SQLite migrations remain required before real dispatch.

## M1 desktop presentation

`MainViewModel` owns navigation, appearance/notification preferences and child `DemoSessionViewModel` / `ProfileEditorViewModel` instances. Four separate WPF UserControls bind to those objects. `ObservableObject` supplies change notification; guarded `RelayCommand` instances drive typed input, quick replies, service confirmation and navigation. View code-behind is limited to window chrome, viewport measurement, scrolling and keyboard input. The real integration interfaces and domain transition guards were not changed for M1.

The scripted `DemoSessionViewModel` is a presentation fixture and references no gateway, simulator, repository or conversation engine. It keeps sample approvals local to its current demo ID and rejects stale service-row references after Reset. Separate demo buttons inject sample completion, cancellation and equipment-release observations. A connection-loss preview invalidates sample approvals and freezes the sample in NeedsReview; it implements no automatic recovery. M2 must introduce the operational reducer separately.

The profile editor holds per-airline/per-operation edits in memory and uses M0's pure foundation resolver to show the family/type precedence. It has no route for applying changes to the active demo or saving airline profiles. Current-flight commands, safety validation and schema/atomic profile storage remain later milestones. Flight, history and airline detection/source displays are explicit sample data.

Dark/light dictionaries provide semantic brushes, with shared keyboard-focus and control templates. At 840 logical pixels the Chat view can expose a collapsible service overview. A custom resizable WPF chrome retains minimize/maximize/close controls and keyboard-accessible navigation. Minimum size is 420 × 640 logical pixels.

`INotificationPlayer` wraps local WPF MediaPlayer playback of the original checked-in WAV. Sound policy is checked before playback; failures appear in Settings. `IUiPreferencesStore` saves only appearance, notifications, rail and future-realism preferences through a temporary JSON file. Missing/corrupt settings fall back with visible guidance. SimBrief ID, chat and profile edits are never saved or sent. Test modes use a transient store and cannot overwrite the user's preferences.

`ADM.Desktop.Tests` covers meaningful presentation behavior and settings fallback. `--ui-test` runs opt-in, in-process WPF acceptance checks against the live window: UI Automation invocation, accessible control names, focus traversal, shared composer key handling, real editor/theme bindings, phone/tablet layout, the scripted happy path and interruption preview. It captures screenshots for visual inspection. No OS-level keyboard injection, simulator connection or external service is involved.

## M2 domain workflows and local conversation

`OperationalReducer` in Domain is a pure immutable function from state + typed event to state + notices + command intent. It does no I/O and obtains every timestamp/identity from its input. It deduplicates event IDs, rejects cross-session input, maintains separate service/workflow/connection state and journals policy-sourced, versioned intent. Built-in dependencies augment profile dependencies and are cycle-checked. All automatic work is serialized conservatively; no unverified parallel-service assumption exists.

The reducer distinguishes requested/accepted work from observed completion and cancellation. Required skips require confirmation; a skipped required refuel additionally needs a separately recorded fuel attestation. Ask services require recorded permission. Boarding and closure approvals are scoped to session/revision and invalidated on material changes. Gate readiness is source-qualified through GateRules/IGateReadinessCoordinator. Poll freshness alone is not a semantic change. Simulated holds cannot clear real holds, and resuming boarding cannot clear unrelated pilot holds.

`OperationalCommandPolicy` revalidates journaled intent immediately before any future dispatch. A queued boarding request cannot survive new holds or a stale approval revision. No real command worker is connected in M2; M3/M4 must apply this policy with authoritative provider snapshots before introducing outbound I/O.

`LocalDispatchSession` in Application serializes input, routes parsed pilot intents to the reducer and stores immutable conversation/checkpoint state. It publishes essential notices once by semantic key; suppressed messages also suppress notification requests. Conversation message IDs are derived separately from event IDs, so pilot replies and multiple notices from one event retain distinct identities. Versioned checkpoint codecs retain persona, immutable plan, journal, approvals, messages and deduplication state. Restoring a checkpoint requires reconciliation by default and does not resend commands. Automatic SQLite storage and recovery remain M4 work.

`ADM.Conversation` implements local normalization, anchored service-command/synonym recognition, contextual yes/no and unambiguous pronouns. Its embedded validated JSON catalogue provides stable persona selection, weighted phrase variants and recent-variant avoidance. Personas affect wording only. Operational clarification and failure messages cannot be overridden through template keys. No LLM, HTTP request or API key is involved.

ProfileJsonCodec enforces the shipped JSON schema shape and additional domain validation, preserves last-valid/generic fallback with diagnostics, and never changes an active session. Canonical aircraft identity and explicit airline/type aliases avoid guesses from commercial flight numbers or marketing titles. Pilot overrides cannot erase existing required flags, dependencies or applicability. A resolved plan is an immutable, revisioned value captured before dispatch.

The desktop's previous M1 script has been replaced by a projection over LocalDispatchSession. Its advance/observation buttons inject labelled Simulation events; they do not bypass reducer approvals or call GSX. The sample starts partway through a documented synthetic turnaround, with prior service observations and command intent retained. Settings profile edits remain isolated previews. Decorative page copy and redundant service indicators were removed in the same increment.

## M3 integration boundaries and configurable service phases

OperationPolicy optionally carries a complete service-phase map. Each layer inherits or replaces that map using the existing generic → airline → family → exact-type precedence. AppliedServicePlan captures the immutable result. Older profile JSON inherits the default map. Runtime and JSON validation reject missing services, invalid phases, boarding before/together with fuel or other ground work, and dependency cycles.

Default phases are deboarding 1, cleaning 2, water/lavatory/refuelling/catering 3, boarding 7. The boarding phase is reserved after ground work; gaps permit custom sequencing. Earlier phases become dependencies, while hard arrival and boarding dependencies remain. Within a phase the stable default request order is water, lavatory, fuel, catering. Parallel work requires the entire active set plus candidate to fit an explicitly validated concurrent group. No data means serial behavior. Journaled intents are revalidated against phase dependencies before dispatch.

The desktop preview provides phase editing and explicit Save service order. Saved orders apply only when a new sample starts and persist with UI preferences; policy previews remain in memory. The full versioned JSON profile repository remains M4. The synthetic sample supports four concurrent services; it does not claim compatibility for real aircraft.

GsxRemoteGateway implements documented Couatl Remote API v2/wire-1 through ClientWebSocket, with a small socket seam for contract tests. It validates the hello and capability tokens, replaces whole service arrays, qualifies observed evidence, correlates receipts, serializes sends and reconnects without replay. The local port is rediscovered per connection. Engine loss clears states; an engine return requires a fresh snapshot. Cancellation and equipment release are explicitly unsupported.

SimConnectTelemetry owns a dedicated long-running thread. Each SDK session is constructed, opened, dispatched and disposed there. An event handle drives read-only SDK reception; a channel carries normalized snapshots. Disconnects remove stale values. The managed implementation compiles conditionally against the installed official SDK, with a clear absent-SDK fallback.

ADM.IntegrationProof composes the real adapters for an explicit observation or single preboarding-service test. It emits local JSONL diagnostics and requires actual telemetry/GSX availability before a request. The normal WPF app retains fake DI adapters and labelled simulation; connecting its scheduler to live control belongs to M4. After the pilot prepared a test flight, live SimConnect telemetry and a single GSX water request progressing to requested/performing satisfied M3's exit gate. The retained evidence does not claim service completion or universal compatibility.

## M4 live operations and persistence

The desktop now composes a separate explicit live path beside its isolated demonstration. LiveOperationsViewModel owns connection lifetime and projects LiveDispatchCoordinator state. Connecting, importing and starting are separate pilot actions. Restored operations remain paused without external connections. The worker serializes events and sends outside the UI thread; cancellation-aware disposal stops it before the persistence owner is released.

LiveDispatchCoordinator applies the same immutable domain reducer and dispatch policy, revalidating telemetry, live availability and generation immediately before sending. SQLite stores the checkpoint and unique send reservation before wire I/O, then commits the receipt with the resulting checkpoint. Unknown delivery never retries automatically. Reconciliation freezes scheduling until the complete current snapshot has been reviewed. GSX engine/context changes invalidate the generation, manual activity is source-qualified, and completion contradicted by reset becomes Unknown. Idle-unavailable services are not fabricated as complete.

SqliteDispatchStore retains session/checkpoint history, command outcomes and normalized flight cache, with transactions, WAL and an exclusive process ownership file. JsonAirlineProfileRepository validates complete revisioned profiles before atomic replacement and retains a last-valid backup. Settings saves both policies and service phases for future operations. AircraftServiceCapabilities adds explicit evidence-backed concurrency groups; the shipped empty catalogue provides serial fallback. Family keys require explicit selection.

SimBriefImporter makes a bounded explicit HTTPS fetch. Application validates freshness, route and aircraft before caching or starting. Raw OFPs and account identity are not retained. Airline source/override and profile revision are visible; unknown identities use GENERIC. Any2GSX configuration and gate readiness are pilot attestations.

M4's live worker stops at AwaitingBoardingApproval. No real boarding, cancellation, closure, equipment-release or pushback command is dispatched. Automated first/turnaround fixtures pass; real M4 simulator acceptance is deferred at the user's request. See [M4 report](m4-report.md) and [live-test guide](m4-live-test-guide.md).


## M5 boarding, release and service-cycle evidence

LiveDispatchCoordinator now permits the documented Boarding service request through the existing reserve-before-wire path. It observes pending provider changes before interpreting a pilot approval, rechecks policy and telemetry at dispatch, and invalidates unsent boarding intent when prerequisites materially change. Accepted delivery is never completion.

The gateway retains a completed/6 observation through a subsequent idle snapshot until a new service cycle or invalidation. For optional routine work that instead returns from observed Active to Available in one uninterrupted operational generation, the coordinator records CycleEnded, not Completed. This pilot-requested policy resolves subsequent sequencing without claiming work success. Fuel, boarding and profile-required work cannot use CycleEnded. Missing fuel/boarding completion has a scoped attestation question; no routine completion questions are introduced.

After observed or attested boarding completion the reducer asks separately for closure. Current automatic release capabilities are empty, so it sequences Doors → JetwayOrStairs → GroundEquipment as manual obligations. Distinct confirmations persist User evidence. Connection loss clears both approval and partial release checks. All release criteria must be satisfied before handover; no pushback mapping exists. Completed ground handling stops dispatch but leaves telemetry monitoring alive.

Live Chat exposes contextual quick replies, essential notification sounds, live stages and separate release provenance. Worker errors pause and report their reason; reconnect is explicit and restores the operation for reconciliation. See [M5 report](m5-report.md) and [later live-test guide](m5-live-test-guide.md). Real M4/M5 exit acceptance remains pending.

## Service-start conversation and pilot-requested pairing (21 October)

New live operations are prepared after explicit connect plus the completed setup checks and safe telemetry. Preparation is not service approval. A persisted User acknowledgement of the ServicesStart question gates scheduling and last-moment dispatch. The first accepted preboarding request emits one services.starting notice. Existing checkpoints retain their active plan and decisions; restart/reconcile cannot manufacture an unapproved start.

The pilot requested fuel/catering calls together. RequestFuelAndCateringTogether permits only that pair, still subject to common-phase dependencies, independent capability/availability/Ask permissions, durable receipts and unknown-delivery stops. It is a request-order policy, not an aircraft concurrency certification. Other service overlap remains capability-driven.

Live routine waiting holds use ChangeHold with Notify=false. They remain operational blockers and visible in Services/explicit delay answers without repeated Chat notices or pings. Detailed diagnostic logging is unchanged. See [service-start update](service-start-update.md).

## M6 operational variability and polish

A pure session-seeded engine in ADM.Simulation advances a typed incident/effect graph through Domain events. LiveDispatchCoordinator serializes these changes with real observations and receipt checkpoints. SQLite's existing transactional checkpoint/history payload retains the full incident graph, seed identity, decision, timing and persona; M6 uses that single recovery source rather than introducing independently updated relational projections. Service evidence remains separate from simulation-owned gate and boarding holds.

Defaults are one incident on 25% of eligible turnarounds, weighted 2–7 minutes and a hard configurable 0–10 minute cap. Real or pilot holds supersede simulated incidents. Expiry/skip clears all linked effects without clearing real holds, and restart cannot reroll a decision. Simulated gate changes preserve independent preboarding Ask permissions while invalidating boarding clearance.

Global preferences and per-airline tuning apply to future immutable session snapshots. Exact airport → GENERIC → manual fallback is validated locally. Profile import stages a validated future profile; export uses atomic replacement. Neutral .resx strings cover new settings/navigation; language-qualified persona catalogues cover new essential events. See [M6 report](m6-report.md) and its 50-session evidence. M7 is not started.

## M7 duty, chat lifecycle and service restart

DutyTracking is a pure domain state machine separate from ground handling. Its persisted DutyProgress owns immutable DutySector records, original plan/dispatcher identities and arrival candidates. LiveDispatchCoordinator advances it under the same serialized operational lock. Completed operation checkpoints and their released duty state are saved atomically and archived; airborne confirmation retires the live runtime while observation continues. SQLite adds additive duty_sessions and sector_legs tables and read-only conversation retrieval. Existing command journals remain scoped to operation/plan IDs.

Startup restores unfinished handling paused but never automatically opens a cached OFP. A persisted chat visibility offset hides messages without removing audit facts, delivered semantic keys or pending approvals. Desktop retains an empty live workspace while the duty watches flight/arrival state. A next OFP/manual plan is staged separately, then explicitly confirmed against the actual origin before the session-bound gateway is rebuilt. Profile lookup and dispatcher selection run again; no approvals or service completion states transfer. Prior provider completion/active states without new-sector send evidence remain uncertain until a fresh available baseline.

Pilot-requested deboarding removal keeps the enum identity for legacy checkpoints only. New resolved plans force it disabled; controls, previews, exported profiles, parser, protocol mapping and CLI exclude it. Untouched legacy steps migrate to Skipped without changing the original applied plan. Explicit restart of uncertain work needs a reconciled connection and fresh triggerable GSX state, creates a new versioned intent and clears old fuel/boarding achievement assumptions. See [M7 implementation and verification](m7-report.md).

## Current automatic startup and simpler UX (21 October)

This supersedes earlier explicit-connect/direct-SimBrief startup descriptions. Desktop opens the live waiting workspace, automatically starts its read-only connections and remembers a per-aircraft integration acknowledgement in atomic JSON. It consumes capability-advertised GSX handlerData rather than a Desktop HTTP OFP dependency. AutomaticPreparation checks current safe telemetry, route/aircraft/airport agreement, setup and GSX input state. Preparing creates only a persisted Chat question; pilot approval still gates dispatch. New plans are staged separately, while restore requires explicit operational resume. Detailed recovery and profile editors remain available behind collapsed sections.

The coordinator requires observed fuel Requested/Active/Completed before same-phase catering. An accepted but unobserved service pauses after 20 seconds; GSX input waits suspend the timer and dispatch. Recovery stays explicit and journaled. Handler data supplies planned load figures, never final-loadsheet evidence. See [current UX report](simple-ux-report.md).

## M7 follow-up: current Desktop launch and presentation

Normal startup now calls FreshLaunch.ArchivePreviousAsync before creating the live coordinator: prior sessions/duty are archived/ended without changing service facts or erasing send reservations. The Desktop begins with no restored current flight/chat/approvals. Explicit backend recovery remains available to tests and running-session recovery. Dynamic profile discovery and a user-maintained designator registry make created/imported airlines available offline; scoped profile editing preserves other operations/types. LocalDispatchSession records deduplicated IsSystem progress entries separately from conversation notices. Notification eligibility explicitly excludes these entries. See [M7 UX follow-up](m7-ux-followup.md), which supersedes older startup-restoration UI descriptions.

## M8 presentation and distribution amendment

Desktop now uses Chat / Profiles / Flight / Settings. RecoveryGuide is a pure presentation policy over typed current facts, with actions executed through the existing serialized coordinator. It distinguishes reconnect, explicit available-service restart, review/resume and GSX intervention; it adds no protocol verbs. SupportReport exports only an explicit status allowlist after a local Save dialog. InstallationRepair locates a package-validated installer and App invokes it only after host disposal. Pilot deployment is self-contained, including the matched client runtime pair; the SDK remains developer-only. See [M8 report](m8-report.md) for verification and unresolved public-release gates.

## M8 conventional deployment follow-up

GitHubUpdates reads fixed-origin public release metadata, validates version/asset provenance and downloads bounded, digest-checked installers. UpdatesViewModel owns quiet status and explicit install availability, checking handling state before and after download. App disposes connections before opening setup; Inno Setup manages application files and its cached repair copy. No pilot PowerShell/SDK prerequisite remains. Repository identity is build metadata, not a pilot setting. JsonAirlineProfileRepository now reads personal/rollback profiles before validated bundled defaults. FlightProfileChanged selects the preview/editor context once per flight without rewriting an active plan. Windows supplies the native window frame; transient dropdowns close on deactivation. [Details](m8-distribution-followup.md).
