# M7 — multi-sector duty and lifecycle fixes

M7 adds a persistent duty tracker around independent ground-handling operations. It also implements the pilot's chat reset, restart and deboarding changes. Simulator acceptance remains a later manual check; all automated tests use isolated synthetic data and never connect to MSFS or GSX.

## Chat and flight boundaries

- Startup does not load the last cached flight automatically. Use cached flight remains an explicit action. An unfinished handling session still restores paused for reconciliation.
- Clear chat hides the current conversation and persists that visibility boundary. Pending questions, approvals, command reservations and audit messages remain intact. It does not cancel or restart work.
- Confirmed airborne telemetry (two airborne samples at least five seconds apart, with ground speed at least 50 knots) archives the current operation, clears live chat/flight data and continues arrival monitoring. No aircraft motion is commanded. A single transient on-ground flag cannot trigger this.
- Completed handling is archived in the same SQLite transaction as its final checkpoint. Closing/reopening ADM shows a waiting duty workspace, not the old flight/chat. Read-only sector and conversation history remains available in Flight.

## New sectors

A duty records independent sector, operation, applied-plan and dispatcher IDs. Completion hands flight operations to the pilot; connected telemetry continues. A parked arrival with airport/stand context is a candidate only. Destination, diversion, return-to-origin, unknown airport, location jumps, changed aircraft and interrupted telemetry all require pilot confirmation before another operation. A restart cannot manufacture a completed flight from a parked snapshot.

Import latest OFP or enter a new flight manually to stage the next leg. This leaves active handling's plan and history unchanged. Confirm the aircraft's actual airport matches the next plan origin, then select Prepare next turnaround. Current handling must be completed or explicitly archived. ADM recreates the session-bound gateway, resolves the newly selected airline and latest saved aircraft/profile preferences and asks separately before services start. Check the route and aircraft again. The dispatcher differs from the preceding sector when the catalogue has a choice. Boarding and closure approvals never carry over.

Old GSX completed/active states cannot be inherited into a new sector merely because they remain in the provider snapshot. The new operation waits for a fresh available service state and normal reconciliation. Each new service intent has the new session/plan IDs and durable reservation. No SimBrief polling, pushback, taxi or flight control was added.

## Cancellation and restart

The verified GSX API still has no supported cancellation command. Cancel active work in GSX; ADM never claims that its unsupported cancellation succeeded. If fuel or boarding returns idle without completed evidence, Chat offers both the existing explicit completion attestation and Restart fuel / Restart boarding. Restart is a new instruction, not a completion attestation. Unknown work can restart only after reconciliation and fresh GSX availability; old intent/receipt records remain immutable. Resume/restart also supports skipped, cancelled, failed and returned-idle work. All dependencies, service-start approval and boarding approval still apply.

## Deboarding removal and compatibility

Deboarding is absent from service timelines, airline/family/type options, profile previews and exported JSON, command parsing, GSX protocol mapping and the integration-proof CLI. New resolved plans force its internal legacy slot disabled even if an old profile or override enables it. Existing profile files remain readable and are normalized on import/load. Untouched deboarding in an older live checkpoint is recorded as skipped by the pilot-requested migration, preserving the original applied plan and journal. An already active/uncertain legacy cycle is kept for review rather than falsely marked stopped; explicitly archive that old operation before a new sector if necessary. The enum's numeric identity remains solely for saved-record compatibility; it is not a user-selectable service. Cleaning remains optional. Fuel and catering pairing and configurable phases are unchanged.

## Architecture and changed files

Domain: new DutyTracking.cs contains immutable duty/sector/candidate models and pure telemetry transitions. Application: LiveDispatchCoordinator coordinates duty tracking with handling persistence and no outbound commands after departure; LiveContracts adds the duty-store port and explicit transition confirmation; LocalDispatchSession adds a persisted chat visibility boundary. Infrastructure: SqliteDispatchStore adds additive duty_sessions and sector_legs tables and read-only archived history, retaining schema-1 compatibility and command reservations. Archived checkpoint and parent duty updates share a transaction.

Desktop: LiveOperationsViewModel stages plans, manages sector transition/reconnection, exposes clear chat and duty/history controls; MainViewModel keeps the waiting live workspace visible; Flight/Chat/Services views expose the relevant controls. Profile codec/resolver/editor/defaults, parser, persona selection, GSX mapping and CLI implement deboarding removal and restart wording. New domain, profile-migration, workspace and end-to-end tests cover these paths; existing fixtures follow the revised service catalogue. UI verification covers the new controls.

Added files: `src/ADM.Domain/DutyTracking.cs`, `tests/ADM.Domain.Tests/DutyTrackingTests.cs`, `tests/ADM.Application.Tests/M7ProfileMigrationTests.cs`, `tests/ADM.Desktop.Tests/M7WorkspaceTests.cs`, `tests/ADM.EndToEnd.Tests/M7DutyTests.cs`, this report and the two `docs/evidence/m7-two-sector-*-evidence.json` files.

Modified files: Application `LiveContracts.cs`, `LiveDispatchCoordinator.cs`, `LocalDispatchSession.cs`, `FoundationServicePlanResolver.cs`, `SampleSessionFactory.cs`, `ProfileJsonCodec.cs`; Domain `OperationalReducer.cs`; Infrastructure `SqliteDispatchStore.cs`; Conversation `LocalIntentParser.cs`, `LocalConversationEngine.cs`, `PersonaCatalogue.cs`; GSX `GsxProtocol.cs`; integration proof `Program.cs`; Desktop `MainViewModel.cs`, live/profile/demo viewmodels, `FlightView.xaml`, `LiveChatView.xaml`, `LiveServicesView.xaml`, `UiVerification.cs`; airline JSON/schema; existing profile-contract, foundation-composition and M6-realism fixtures; README, specification, architecture/state/compatibility and release checklist.

## Validation

Release build passed with 0 warnings and 0 errors. All 283 xUnit tests passed: Domain 72, Application 33, Conversation 39, Desktop 32, Integration contracts 36, EndToEnd 71. All 43 WPF UI checks and the startup/render/clean-exit smoke test passed. Formatting verification passed. The M6 50-session regression also passes with the revised service catalogue.

The multi-sector acceptance fixture completes two full handling workflows around simulated departure/arrival in both continuous and SQLite-restart cases, uses BAW revision 2 then RYR revision 4 profiles, asserts fresh IDs/persona/approvals, protects previous history and verifies 10 unique requests in each run, two completed archived operations, zero inherited approvals and zero deboarding/pushback requests. Evidence: [continuous](evidence/m7-two-sector-continuous-evidence.json) and [restart](evidence/m7-two-sector-restart-evidence.json). It provides synthetic workflow evidence, not real aircraft qualification. Tests additionally cover chat visibility persistence, no automatic cached-plan loading, next-plan staging, unavailable restart rejection, fresh boarding permission and legacy deboarding migration.

## Windows setup and later live checks

Use Windows x64, .NET SDK 10.0.402 and the official installed MSFS 2024 SDK for live telemetry. From C:\Projects\ADM:

```powershell
dotnet restore ADM.sln
dotnet build ADM.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet test ADM.sln -c Release --no-build --no-restore
dotnet run --project .\src\ADM.Desktop\ADM.Desktop.csproj -c Release --no-build
```

Executable: src\ADM.Desktop\bin\Release\net10.0-windows\ADM.Desktop.exe. A runtime-only machine needs the x64 .NET 10 Windows Desktop runtime. Alternate SDK: -p:MsfsSdkPath="D:\MSFS 2024 SDK". No paid AI, API key or backend is needed.

When the simulator is available: verify Clear chat during a pending approval; manually cancel fuel in GSX, wait for available, select Restart fuel and complete the new cycle; finish boarding/closure and close/reopen ADM; then fly a departure/arrival and stage a second leg. Confirm the observed airport and reset GSX for the stand before the new operation. Check the new dispatcher/profile, empty approvals, one start question, quiet Chat and read-only previous sector. Complete outstanding M4/M5 live acceptance as part of this test. No live actions were attempted during development.

M8 release packaging/qualification is next on request. Public distribution still requires the existing licence/SDK redistribution review and clean installer/device testing.

## Connection recovery fix — 21 October 2026

A pilot reconnect exposed an M7 defect: recovery reset LastObservedAt but retained saved coordinates. The first real SimConnect position sample entered the teleport check and dereferenced the missing timestamp, stopping the live worker after both adapters had connected successfully. Initial fixtures had not combined restored coordinates with a cleared timestamp.

DutyTracking now compares positions only when a prior timestamp exists. Restore also invalidates both coordinates alongside that timestamp, so the first fresh observation establishes a coherent baseline. Location review, paused recovery, service-start approval and durable request deduplication remain intact. No saved flight/service history was deleted to work around the issue.

Changed: DutyTracking.cs, LiveDispatchCoordinator.cs, DutyTrackingTests.cs and M7DutyTests.cs. Two regression tests cover the timestamp-free position baseline and SQLite reconnect with saved coordinates, including explicit reconciliation and zero replayed service requests.

Release build: 0 warnings/errors. All 285 tests passed (Domain 73, Application 33, Conversation 39, Desktop 32, Integration contracts 36, EndToEnd 72); formatting verification passed. A five-second live observation-only integration check at 10:46 UTC confirmed both MSFS telemetry and the GSX handshake/service snapshot, with attempted=false and no service request. Evidence: [private local integration capture]. This is connection proof, not end-to-end handling acceptance.

Reopen the normal Release executable and select Connect MSFS / GSX. If an unfinished operation is restored, review its states and use Reconcile current states before continuing. No simulator or GSX restart is required by this fix.

## Later pilot-directed profile, Chat and startup changes

The [M7 UX follow-up](m7-ux-followup.md) supersedes this report's normal Desktop restore-on-reopen instructions. Normal launch now archives old handling and opens a clean visible workspace, while preserving the operational audit and saved configuration. It also adds custom profile creation/discovery, quiet progress lines, the supplied MP3, silent GSX menu/input handling and robust current-flight ingestion. Multi-sector tracking while the app stays open and explicit backend recovery remain covered. No M8 work has begun.
