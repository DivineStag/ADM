# M2 — Domain workflows and messaging

Date: 20 October 2026. M2 is complete for local simulated operations. M3 has not been started. No real GSX, SimConnect or SimBrief calls were added.

## UI cleanup

Decorative headings were replaced by Services, Flight and Settings. “A little order on the ground”, “A smooth departure”, “Make yourself at home”, “A quieter dispatch, your way” and “Your next departure” were removed. Completed progress bars, repeated completed-service descriptions, the extra dispatcher label and duplicate header Reset control were removed. Reset remains in Settings. Demo/source evidence remains visible so simulated outcomes cannot be mistaken for real observations.

The old canned three-message conversation is now a single welcome from the assigned dispatcher. Names and persona previews bind to the session rather than hard-coded Sarah labels. Status/delay replies use operational facts rather than internal enum names or profile identifiers.

## M2 acceptance

| Requirement | Implementation and evidence |
|---|---|
| Immutable event reducer and guards | OperationalReducer returns new state, notices and journaled intent; it does no I/O. Duplicate/cross-session input is rejected. |
| Service dependencies and three operation types | Mandatory default sequencing augments profile dependencies; cycles are rejected. First departure does not deboard; configured final arrival completes without boarding/closure/pushback. |
| ICAO/profile inheritance and validation | Validated prefixes and explicit aliases, explicit GENERIC override, canonical aircraft identity/aliases, schema-shaped JSON validation, required constraints and immutable resolved snapshots. |
| Skip/cancel/resume and approvals | Distinct state paths; mandatory skip confirmation; separate fuel attestation after required fuel skip; pending cancellation until observation; new approval after holds/cancellation/material changes. |
| Local contextual conversation | Anchored synonym grammar, contextual yes/no, unambiguous pronouns, typed intents and shared handling for chat/quick replies. No LLM/backend. |
| Persona catalogue and identity persistence | Three validated embedded JSON personas with weighted variants and deterministic selection. Checkpoint file round-trip retains dispatcher assignment, plan, messages, approvals, journal and deduplication keys. |
| Source-qualified gate readiness and holds | Simulation, pilot attestation and external evidence remain distinct. Gate holds invalidate readiness/approval; repeated poll freshness does not. Simulation skips cannot remove real or unrelated pilot holds. |
| Deterministic simulated handover | End-to-end tests exercise the real parser, reducer and templates through boarding approval, observed boarding, closure approval and separate release evidence. |

## Architecture

Domain owns OperationalState, typed OperationEvent records, OperationalReducer, GateRules, ServicePlanRules and OperationalCommandPolicy. Input IDs/times are supplied by events; deterministic effect IDs make replay repeatable. Command intent records retain session, policy source, snapshot ID, intent version and prerequisite revision. Late acknowledgements cannot modify a newer intent. Queued work is revalidated before simulated dispatch; boarding/release cannot use stale approvals.

Application owns LocalDispatchSession, which serializes event/text handling and records immutable conversation/checkpoint state. Only newly delivered essential notices request notification audio. Pilot and dispatcher messages have distinct IDs even when they arise from the same event. DispatchCheckpointCodec supplies versioned serialization/validation. Restoring a checkpoint requires reconciliation by default, invalidates old permissions, and does not resend work.

Conversation owns LocalIntentParser, LocalConversationEngine and the embedded PersonaCatalogue. Personas vary wording only. Template configuration is restricted to approved essential keys; it cannot override operational failure/cancellation clarifications. Recent variants are avoided and essential messages are deduplicated by session/semantic meaning.

Operations implements the gate-coordinator port. Application's profile codec validates required fields, enum types, duplicate properties/dependencies, canonical keys, generic coverage, graph cycles and incompatible required/disabled choices. The declarative JSON schema is included for tooling. Malformed profiles retain a supplied last-valid or generic profile with a diagnostic. Current-flight overrides cannot erase existing required flags, dependencies or applicability.

Desktop now projects LocalDispatchSession instead of maintaining the M1 script. Its demo controls inject explicit Simulation receipts/observations and never access a gateway. The sample begins partway through a synthetic turnaround, retaining the earlier simulated observations and command history. Settings profile edits remain isolated previews.

## Verification

Verified on x64 Windows with SDK 10.0.402:

- Release solution build: **passed, 0 warnings, 0 errors**.
- Final Release test run: **149 passed, 0 failed, 0 skipped** across six projects: Domain 45, Application 29, Conversation 34, Integration contracts 7, End-to-end 12, Desktop 22.
- Formatting verification: passed.
- Desktop startup smoke check: passed, WPF ContentRendered and clean exit.
- Live WPF acceptance: **24 checks passed**, including all views, live profile/theme bindings, minimum 420 × 640, tablet rail, accessibility names, focus order, composer key handling, the domain-driven happy path and interruption preview. No binding errors occurred in the exercised flow.
- Screenshots of the cleaned-up phone/tablet and light/dark views were inspected. Hardware audio, Narrator and other DPI scales have not been manually tested.

Regression coverage includes 100 repeated service observations, 100 repeated gate checks with fresh evidence timestamps, ambiguous/unprompted permission, required fuel skips, supported/unsupported/rejected cancellation, old receipts after resume, unknown receipts, disconnect without replay, manual release fallback, closure holds, hold ownership, real-vs-simulated holds, current-flight override provenance, profile isolation and disk checkpoint round-trips.

A late-cycle regression exposed that required cancelled work must remain unresolved. That guard was corrected and the final complete test pass succeeded. The baseline fake gateway's unsupported real-action manifest remains unchanged; the simulation capability set does not advertise actual SDK support.

TRX outputs: `artifacts/m2-final-test-results`. Live UI report/screenshots: `artifacts/ui`. These are ignored local artifacts. Existing Windows CI automatically includes the new tests and UI checks, but no remote CI run has occurred because no remote repository was created.

## Windows setup and run

Use x64 Windows and .NET SDK 10.0.402 (or a compatible 10.0.4xx patch). Initial NuGet restore needs Internet access; simulator software, vendor binaries, API keys and a hosted backend are not required.

```powershell
Set-Location 'C:\Projects\ADM'
dotnet restore ADM.sln
dotnet build ADM.sln -c Release
dotnet test ADM.sln -c Release --no-build
dotnet run --project .\src\ADM.Desktop\ADM.Desktop.csproj -c Release --no-build
```

Open ADM.sln in a Visual Studio installation supporting .NET 10 with the .NET desktop development workload, or run the executable at `src\ADM.Desktop\bin\Release\net10.0-windows\ADM.Desktop.exe`. A runtime-only machine needs the x64 .NET 10 Windows Desktop runtime.

Use Finish sample services and the contextual approval replies to run the happy path. Typed commands and quick replies use the same engine. Cancelled mandatory work must be resumed and completed; skipping required fuel requires a separate fuel confirmation and never changes its state to Completed. Resuming a boarding hold asks again before boarding. Reset is in Settings.

## Limits and next milestone

This remains a local simulation. Domain intent is not a verified GSX command. No real API protocol, aircraft compatibility, live telemetry or service automation is claimed. Durable SQLite storage, automatic application restart recovery, authoritative reconnect reconciliation, real imports, disruption generation and multi-sector detection remain later work. Checkpoints can preserve identity/history through serialization, but the desktop does not automatically save/restore ground sessions yet. Profile storage/editing/import/export is still a preview rather than the M4 repository.

The next milestone is **M3 — Integration proof: GSX and MSFS**, starting with the installed official GSX Remote API v2 manual and official SimConnect documentation. M3 has not been started. Pushback remains outside all ADM control paths.

## Files created

- `src/ADM.Domain/Operations.cs`
- `src/ADM.Domain/OperationalReducer.cs`
- `src/ADM.Domain/OperationalCommandPolicy.cs`
- `src/ADM.Domain/GateRules.cs`
- `src/ADM.Application/LocalDispatchSession.cs`
- `src/ADM.Application/ProfileJsonCodec.cs`
- `src/ADM.Application/CanonicalAircraftIdentity.cs`
- `src/ADM.Conversation/LocalIntentParser.cs`
- `src/ADM.Conversation/LocalConversationEngine.cs`
- `src/ADM.Conversation/PersonaCatalogue.cs`
- `src/ADM.Operations/GateReadinessCoordinator.cs`
- `profiles/airline-profile.schema.json`
- `tests/Shared/OperationalScenario.cs`
- `tests/ADM.Domain.Tests/OperationalReducerTests.cs`
- `tests/ADM.Application.Tests/ProfileValidationTests.cs`
- `tests/ADM.Application.Tests/CanonicalAircraftTests.cs`
- `tests/ADM.Conversation.Tests/LocalConversationTests.cs`
- `tests/ADM.EndToEnd.Tests/LocalTurnaroundTests.cs`
- `docs/m2-report.md`

## Files modified

- `src/ADM.Domain/OperationalTypes.cs`
- `src/ADM.Domain/Profiles.cs`
- `src/ADM.Contracts/OperationalContracts.cs`
- `src/ADM.Application/AirlineIdentityResolver.cs`
- `src/ADM.Application/FoundationServicePlanResolver.cs`
- `src/ADM.Conversation/ADM.Conversation.csproj`
- `src/ADM.Desktop/ADM.Desktop.csproj`
- `src/ADM.Desktop/App.xaml.cs`
- `src/ADM.Desktop/MainViewModel.cs`
- `src/ADM.Desktop/MainWindow.xaml`
- `src/ADM.Desktop/Presentation/DemoModels.cs`
- `src/ADM.Desktop/Presentation/DemoSessionViewModel.cs`
- `src/ADM.Desktop/Views/ChatView.xaml`
- `src/ADM.Desktop/Views/ServicesView.xaml`
- `src/ADM.Desktop/Views/FlightView.xaml`
- `src/ADM.Desktop/Views/SettingsView.xaml`
- `src/ADM.Desktop/Verification/UiVerification.cs`
- `profiles/dispatchers-default.json`
- `profiles/README.md`
- `tests/ADM.Domain.Tests/ADM.Domain.Tests.csproj`
- `tests/ADM.Conversation.Tests/ADM.Conversation.Tests.csproj`
- `tests/ADM.EndToEnd.Tests/ADM.EndToEnd.Tests.csproj`
- `tests/ADM.Desktop.Tests/DemoPresentationTests.cs`
- `tests/ADM.Desktop.Tests/ShellAndProfileTests.cs`
- `README.md`
- `docs/architecture.md`
- `docs/state-machine.md`
- `docs/gsx-api-capabilities.md`
