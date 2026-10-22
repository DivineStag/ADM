# M4 — implementation and offline verification

This records the M4 delivery. The subsequent pilot test and M5 implementation are recorded in [M5 report](m5-report.md).

Work completed 20–21 October 2026. **Simulator acceptance remains pending at the pilot's request.** The simulator was unavailable; competing Any2GSX service control has been disabled by the pilot. No M4 live service request or live workflow completion is claimed. M5 has not begun.

## Implemented architecture

- `ADM.Application` owns a serialized `LiveDispatchCoordinator`, live setup validation and persistence/cache ports. Immutable resolved plans retain airline, family/type and per-service source/revision. An operation never reloads edited policies in place.
- `ADM.Infrastructure` implements versioned JSON profiles, a validated optional aircraft capability catalogue, SQLite checkpoints/history, command reservations and a sanitized flight cache. Profile writes validate before atomic replacement, retain a last-valid backup and enforce revision conflicts. SQLite checkpoints and send reservations/receipts use transactions; a process ownership lock prevents two controllers sharing the database.
- `ADM.Integrations.SimBrief` performs explicit bounded HTTPS requests, parses the v2 response and reports redacted failures. Import and manual entry are validated before use. There is no polling, stored account ID, API key or hosted backend. The authorized one-time live SimBrief fetch verified the actual v2 field shape; the registration-style callsign correctly retained GENERIC rather than inventing an airline.
- `ADM.Integrations.Gsx` exposes service triggerability, waiting holds, context and connection generations alongside source-qualified observations. An engine restart or known context change invalidates the old operational generation. The installed official SDK remains the SimConnect source.
- `ADM.Desktop` provides explicit import/cache/manual flight entry, flight type and airline override, per-aircraft Any2GSX acknowledgements, connect/start, gate attestation, reconciliation and archive controls. Live chat/services use the real coordinator. The initial demonstration remains isolated. Settings has one Save handling profile action for policies and order.

## Handling and recovery

Default phases are deboarding, cleaning when configured, water/lavatory/fuel/catering, then boarding. First-departure applicability removes arrival-only work. Configured Automatic/Ask/Disabled policies, dependency validation, supported services, live triggerability and fresh stationary/engines-off/brake-on telemetry constrain the worker. Equal phases permit concurrency only when an explicit validated group covers the active set. **The shipped real-aircraft catalogue has no validated parallel group; real fallback is serial.**

ICAO detection uses the validated catalogue (initially RYR, BAW and UAL); unrecognized prefixes remain GENERIC. An explicit airline override is visible. Generic → airline → family → exact type → current-flight decisions determine policies. Family keys are explicit rather than guessed from simulator aircraft titles. The aircraft type and current flight require pilot confirmation.

Before any wire request, the coordinator revalidates safety and policy, then persists the checkpoint and unique send reservation. Lost receipts become Unknown and are never automatically resent. Restart restores paused, with gate readiness invalidated and uncertain active work requiring review. Reconnect alone does not resume dispatch. Definitively rejected requests can be replanned after explicit reconciliation. Current manual GSX service activity can be adopted; existing completion needs an explicit current-flight review. A GSX reset cannot reuse old fuel completion. Known idle-unavailable/bypassed services are never labelled complete or skipped. Real GSX waiting holds remain separate from simulated holds.

Archiving preserves SQLite history and does not cancel or complete GSX work. New flight import cannot replace an active operation. Automatic arrival/new-sector detection remains later work.

## Files created or changed

New implementation files:

- Application: `LiveContracts.cs`, `LiveDispatchCoordinator.cs`.
- Infrastructure: `JsonAirlineProfileRepository.cs`, `SqliteDispatchStore.cs`, `JsonAircraftCapabilityCatalog.cs`.
- Integrations.SimBrief: `SimBriefImporter.cs`.
- Desktop: `Presentation/AsyncRelayCommand.cs`, `Presentation/LiveOperationsViewModel.cs`, `Views/LiveChatView.xaml` and code-behind, `Views/LiveServicesView.xaml` and code-behind.
- Profiles: `aircraft-capabilities.json` (empty validated catalogue).
- Tests: `ADM.Integration.ContractTests/SimBriefTests.cs`, `ADM.EndToEnd.Tests/LiveWorkflowTests.cs`, `ADM.EndToEnd.Tests/PersistenceTests.cs`, `ADM.Desktop.Tests/SavedProfileEditorTests.cs`.
- Documentation: this report and `m4-live-test-guide.md`.

Updated implementation: Contracts integration DTOs; Domain operations/reducer/dispatch policy; Application checkpoint validation/profile codec/sample defaults; GSX protocol/gateway; Infrastructure and Desktop project references; Desktop composition root, main/profile viewmodels, main window, Flight/Settings views and WPF verification harness; generic profile JSON; domain sequence tests. README, architecture, compatibility and state-machine documents describe current boundaries. Microsoft.Data.Sqlite is patched to 10.0.13 following restore vulnerability auditing.

## Verification and acceptance

Final Release build passed with **0 warnings and 0 errors**. All **214 xUnit cases passed** (Domain 57, Application 32, Conversation 34, Desktop 26, Integration 34, EndToEnd 31), with no failures or skips. TRX evidence is in `artifacts/m4-final-tests`. All **31 WPF UI checks passed**, including manual flight entry/cache, profile revision saving, custom phases, both themes, compact/tablet layout, keyboard controls and no binding errors. UI evidence is in `artifacts/ui`.

The WPF startup/shutdown smoke check passed and `dotnet format ADM.sln --verify-no-changes --no-restore` passed. The local testhost required unrestricted local IPC after a restricted-runner timeout; the final complete suite passed. No simulator connection was used for these checks.

Automated integration fixtures are synthetic contract evidence, including fixtures carrying GSX/SimConnect source enums to exercise guards. They do not prove real-aircraft compatibility or substitute for the deferred simulator test.

The automated first-departure and turnaround paths reach AwaitingBoardingApproval without duplicate service sends. Tests cover uncertain delivery/restart, reserve-before-wire crash, rejection recovery, stale snapshots, external boarding, real holds, generation/aircraft changes, unavailable services, reset-after-completion, profile persistence and override preservation, validation and import failures.

## Remaining limitations

The real first-departure/turnaround exit gate must still be demonstrated in MSFS with GSX. No real-aircraft concurrency group is certified. Any2GSX coexistence and gate readiness are explicit pilot attestations, not automatic inspections. Real cancellation, boarding dispatch and closure/equipment release remain disabled; unsupported actions require manual handling. Pushback has no command path. The live workflow intentionally stops at boarding approval for M4. Disruptions, automatic multi-sector detection, installer/licensing review and broader aircraft qualification remain later work.

Use [the Windows setup and deferred live-test guide](m4-live-test-guide.md). Complete that test before advancing to M5.
