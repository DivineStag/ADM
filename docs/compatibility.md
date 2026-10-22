# Compatibility and integration limits

The WPF demonstration runs without MSFS, GSX, SimBrief or Any2GSX and uses explicitly simulated observations. M4 adds an isolated opt-in live desktop path; import, connect and start require explicit actions. The separate M3 integration tool remains available for diagnostic reads and a single explicitly requested preboarding service.

The official installed MSFS 2024 SDK is referenced conditionally at build time. Default path: `C:\MSFS 2024 SDK`; override with `-p:MsfsSdkPath="D:\MSFS 2024 SDK"`. With no SDK, the solution builds with an unavailable adapter diagnostic. Local development output may contain vendor DLL copies; no vendor binaries are committed and redistribution has not been cleared.

Telemetry is nullable. Missing engine/position data, airport, stand and callsign are not guessed. No gate readiness is inferred from coordinates. Engine count 1–4 is supported; other counts leave engine state unknown.

ADM is intended to own GSX service automation. Any2GSX aircraft integration can remain enabled while its competing service automation is disabled per aircraft. ADM does not inspect or modify those settings yet. The checklist is a manual acknowledgement, not verified ownership. See the installed Any2GSX version's documentation.

Real GSX cancellation, door closure and equipment release are disabled. Remote API v2 documents service requests, but not a cancellation verb or universal direction-specific closure commands. Manually resolve unsupported actions and reconcile their state. Never interpret menu toggles as verified close/disconnect operations.

Default water/lavatory/fuel/catering share a requested phase. The demo can run these concurrently. No real-aircraft compatible concurrent group is validated yet; the real scheduler fallback is serial. User order preferences cannot override fuel/boarding dependencies, approvals or live GSX availability.

M3 connection monitoring/reconnect, source-qualified state mapping and in-process duplicate protection are implemented behind the integration ports. A real water request progressed to requested and performing with captured GSX evidence, alongside live SimConnect telemetry. M4 implements the desktop preboarding worker, durable SQLite journal/checkpoints, optional aircraft capability catalogue and paused restart/reconciliation. Its real simulator exit test is deferred. Automatic multi-sector detection remains later work. No universal aircraft support is claimed from the single M3 setup or synthetic fixtures.

See [capability verification](gsx-api-capabilities.md), [M3 report](m3-report.md), [M4 report](m4-report.md) and [deferred live-test guide](m4-live-test-guide.md).


## M5 current behavior

M5 enables pilot-approved live Boarding using the documented canonical service request. Closure is a separate approval after boarding completion. Current aircraft manifests still expose no verified automatic door/equipment release; individual manual confirmations are required. Distinct replies identify the exact item and record pilot-attested evidence. Interruptions reset partial release checks. No pushback action exists.

A short completed/6 GSX event is retained across subsequent available snapshots so polling cannot lose it. The pilot-requested optional routine-cycle policy records Active → Available in the same generation as CycleEnded, with unknown completion provenance and a clear Returned idle label. It permits future sequencing without claiming successful completion. Fuel, boarding and required profile work never use this fallback. Only fuel/boarding may ask for a completion attestation when GSX returns idle without evidence; routine work has no completion-confirmation prompt. Uncertain requests, context changes and mandatory work remain guarded.

The pilot's M4 run reached water and lavatory skip, then paused on the water reset-style transition. M5 covers this regression offline, but real end-to-end acceptance remains pending. See [M5 report](m5-report.md).

## Pilot-requested fuel/catering calls

The current live scheduler can request fuel and catering back-to-back when both are eligible in the same saved phase. Each receives a separate service.trigger call and receipt; each must be supported and triggerable in the current GSX snapshot. GSX may still queue execution. This explicit pilot-requested request pairing is separate from validated aircraft concurrency groups; none is shipped for unrestricted overlap. Different phases retain serial sequencing. See [service-start update](service-start-update.md).

## M6 simulation and profiles

Optional operational realism adds explicitly simulated gate/passenger/team holds; it does not add any GSX command or claim new aircraft compatibility. Current release/cancellation limits and pilot-owned pushback remain. Fuel/catering still use separate eligible back-to-back requests with GSX controlling actual overlap.

Airport rules are local, validated and fall back to manual readiness. Simulation evidence is labelled and never accepted as live service completion. Airline tuning and imported profiles apply only to future operations. The current editor/catalogue accepts GENERIC, BAW, RYR and UAL; arbitrary new airline catalogue entries and additional languages are not shipped. The 50-session M6 acceptance uses synthetic adapters and proves workflow invariants, not real aircraft completion. See [M6 report](m6-report.md); full M4/M5 live acceptance remains pending.

## M7 duty and catalogue changes

Multi-sector tracking uses already verified simulator and GSX context fields; no new SDK variable or API command is introduced. Arrival remains a candidate requiring explicit location/next-sector confirmation, including diversions, return-to-origin, teleports and recovery gaps. Unknown airport/stand falls back to manual confirmation. New sectors need safe parked telemetry and fresh GSX availability; prior completions are not reused. Synthetic two-sector evidence covers continuous use and SQLite restart, not real-aircraft qualification.

Deboarding is removed from the public service catalogue and protocol request mapping. Existing saved enum values remain readable; old profile choices cannot re-enable it. GSX cancellation remains unsupported. Use GSX manually, then an explicit Restart fuel / Restart boarding / Restart catering instruction when available. Fuel/boarding returning idle is never falsely recorded as completed. Existing Any2GSX ownership and door/equipment manual-confirmation limits remain. See [M7 report and later live checks](m7-report.md).

## Current UX amendment: GSX cached plan and fuel sequencing

This supersedes the earlier back-to-back-on-receipt policy. Separate fuel/catering requests remain permitted in the same phase, but catering now waits for observed GSX fuel Requested/Active/Completed. Actual execution remains controlled by GSX; no new general aircraft concurrency guarantee is made. Accepted requests without observed start pause for explicit recovery. Answer or close GSX menus/questions in the simulator before further dispatch.

GSX hello must advertise handlerData for automatic cached-plan ingestion. Without a valid recent matching plan, use manual entry. The upstream plan may be SimBrief-derived, but Desktop no longer fetches it directly. Setup acknowledgement is remembered per aircraft and remains pilot-attested. Planned load figures do not establish a final loadsheet. New live qualification is pending because GSX was unavailable during development; offline tests do not prove actual fuel/catering overlap. See [current UX report](simple-ux-report.md).

## Current M7 follow-up

Custom saved/imported airlines are now discovered from validated JSON and become explicit local designator mappings. Unrecognised codes remain generic; this is user configuration, not a carrier database claim. Each normal launch starts a new visible session and archives old handling without cancelling physical work or forgetting journal facts. Open GSX menus are treated separately from validated text prompts and never generate repeated dispatcher questions. Actual live cached-plan data was read successfully; aircraft matching now tolerates the observed GSX lowercase title. [Current behaviour and qualification](m7-ux-followup.md).

## M8 deployment and current UI amendment

The current normal app connects automatically and uses Chat / Profiles / Flight / Settings; earlier demo/explicit-connect descriptions above are historical. The self-contained local beta includes the matching SimConnect runtime pair and .NET/WPF. Pilots do not need the SDK or a separate .NET install. Only developers build against the SDK. Public runtime redistribution clearance and clean-PC/live qualification remain open; no unrestricted GSX/aircraft support is claimed. See [M8 report](m8-report.md), [matrix](beta-test-matrix.md) and [Windows setup](windows-beta-setup.md).

## M8 conventional installer and Fenix readiness follow-up

The conventional Windows setup supersedes the script ZIP. The 25 supplied profiles are defaults, not aircraft capability claims; existing personal edits take precedence. The current Fenix A319 live probe verifies a read-only cockpit brake mapping while the standard SimVar reads released. Other Fenix variants still need qualification; other aircraft retain the standard brake reading. Imported validated ICAO context focuses Profiles. GitHub update checks are implemented but unconfigured in the local build. See [follow-up evidence and remaining gates](m8-distribution-followup.md).
