# Simpler everyday workflow — 21 October 2026

The normal desktop now opens a waiting live Chat workspace, connects automatically, remembers aircraft setup and reads the plan already held by GSX. The separate ADM SimBrief import/account interface and Desktop project dependency have been removed. No GSX service starts until the pilot approves the dispatcher's start question. This is a focused UX/reliability update; M8 is not started and outstanding live acceptance is not declared complete.

## Pilot flow

Open MSFS/GSX at a stand, then open ADM. On the first use of an aircraft, acknowledge one setup card: competing GSX service automation disabled, needed aircraft integration retained. The saved acknowledgement is scoped to the GSX aircraft handler/type, with the simulator aircraft title as a fallback identity. It records the pilot's review, not automatic inspection of Any2GSX. Review it again from Settings after changing integration settings.

Once current telemetry is fresh, on ground, stationary, engines off and parking brake set, the loaded plan must match the current aircraft and GSX airport. ADM prepares the operation and asks in Chat: **Flight plan and planned load figures received from GSX. Happy for me to start services?** Missing planned passenger/ZFW data produces the flight-plan-only wording. This never claims receipt of a final loadsheet. Preparing or reconnecting sends no service requests.

Chat is the everyday surface. Services keeps progress and detailed waits; Flight shows the route/planned figures with manual entry, history and recovery collapsed; Settings keeps profile, realism and integration editors collapsed. The composer waits until there is an operation or staged next sector. The regular app no longer starts with an active synthetic sample flight. Dark/light themes, keyboard navigation and compact layouts remain.

New generic policies use Automatic for water/lavatory/fuel/catering, with cleaning disabled. Existing saved airline policies are retained, including explicit Ask choices. Saved service phases still apply only to future sessions. Boarding and closure approvals remain separate. Required fuel/boarding completion and unsupported manual release checks retain truthful evidence requirements. No pushback command exists.

## Verified data contract and fallback

The installed GSX manual, `C:\Program Files (x86)\Addon Manager\couatl\GSX\GSX_manual_MSFS.pdf`, was checked directly: Remote API §8.13 (pp275–276), handler proxies (pp210–211), and menu/prompt state in §8.2/§8.10. The adapter consumes `handlerData` only when advertised by the hello capabilities. It reads documented aircraft identity, airport/gate and SimBrief proxy fields, plus coarse state updates. Handler Weight values are kilograms regardless of display units.

GSX exposes its cached upstream SimBrief plan; it does not create an independent OFP. ADM makes no direct HTTP request and needs no pilot ID. The reader validates callsign, route, aircraft and scheduled departure freshness; malformed/unsupported or absent data falls back to manual flight entry. Planned passengers, ramp fuel and ZFW are optional, bounded fields. Current code accepts scheduled departure as six UTC date components or an ISO timestamp. Live data-shape qualification is still required.

A next plan is staged without changing active handling. Its scheduled departure participates in identity so repeated snapshots/reconnects do not create duplicate sectors. Start-next-sector confirmation remains in Chat. An unfinished restored operation requires explicit review/resume, rather than automatic service replay.

## Refuelling fix and evidence

The pilot log around 10:51:54 UTC shows fuel accepted followed roughly 40 ms later by catering accepted. Catering then reported requested; fuel never reported requested/performing. This supports a missing fuel start despite transport acceptance. It does not prove exactly how GSX handled the two calls.

The scheduler now waits for GSX fuel **Requested / Active / Completed** before the separate same-phase catering call. It does not wait for fuel to finish once GSX has acknowledged its operational state. Both services still require independent current availability, policy permission and durable receipt handling. Different phases remain sequential.

An accepted service with no observed start after 20 seconds suspends handling and records Unknown. No automatic replay occurs. A GSX menu/question creates one actionable real hold, pauses requests and excludes that waiting time from the watchdog. The pilot answers in GSX. Recovery uses fresh availability, reconciliation and an explicit restart intent. This avoids continuing silently when only catering starts, while preserving the safeguards against duplicate fuel requests.

## Architecture and file inventory

The existing clean dependency direction remains. Domain stores source-qualified planned figures and the service-start facts; Application owns readiness, request sequencing, menu holds and recovery. GSX integration parses feature-detected read-only fields. Desktop owns persisted first-use setup and presentation. SQLite operational recovery remains separate from JSON setup preferences. No credentials or raw OFP are added to setup storage.

- Domain: `Sessions.cs`, `Operations.cs`, `OperationalReducer.cs`, `SampleSessionFactory.cs`; generic JSON and profile-editor defaults.
- Contracts: `IntegrationContracts.cs` enriches the live GSX snapshot.
- Application: new `AutomaticPreparation.cs`; `LiveDispatchCoordinator.cs` adds observed fuel gating, stalled-request handling and GSX-input holds.
- GSX integration: new `GsxFlightReader.cs`; `GsxProtocol.cs` and `GsxRemoteGateway.cs` read capability-gated handler data, menu/prompt state and handler location fallback.
- Desktop: new `Services/AssistantSetup.cs`; `App.xaml.cs`, `ADM.Desktop.csproj`, `Presentation/LiveOperationsViewModel.cs`, `Views/LiveChatView.xaml`, `Views/FlightView.xaml`, `Views/SettingsView.xaml`, `Themes/Controls.xaml`, profile defaults and `Verification/UiVerification.cs` implement automatic startup and simpler screens.
- Tests: new `AutomaticPreparationTests.cs`, `GsxFlightReaderTests.cs`, `SimpleAssistantWorkflowTests.cs`, `SimpleStartupTests.cs`; gateway, pairing, profile-validation and shell/profile tests updated.
- Documentation: README, this report, specification amendment, architecture, state-machine, compatibility, GSX capabilities and service-start update.

The historical SimBrief integration source/project and its contract coverage remain in the solution for compatibility; the Desktop no longer references or registers its HTTP implementation. Its unused importer port is satisfied by a local GSX-only explanation.

## Validation

Release build passed with **0 warnings and 0 errors**. All **315 xUnit tests** passed, none failed or skipped: Domain73, Application46, Conversation39, Desktop34, Integration47, EndToEnd76. Results: `artifacts/simple-ux-final-tests/*.trx`. Coverage includes plan parsing, bad/stale/mismatched data, saved setup, no-account startup, no start before approval, fuel observation gating, a stalled accepted request, menu waits without repeated messages, and explicit restart.

All **48 WPF checks** passed with no binding errors. Startup/shutdown and `dotnet format --verify-no-changes --no-restore` passed. Dark/light startup, first-use setup, Flight and Settings screenshots were inspected under `artifacts/ui`; all screens use isolated synthetic fixtures, and the startup capture reuses the harness's manually entered fixture plan. No pilot database/profile was reset and no real GSX service was requested during this development.

## Windows launch and remaining live check

Build/run commands and SDK/runtime requirements are in the README. The rebuilt executable is:

`C:\Projects\ADM\src\ADM.Desktop\bin\Release\net10.0-windows\ADM.Desktop.exe`

Enable the GSX local Remote API server and load its flight plan. The read-only localhost probe was refused because GSX was unavailable, so live automatic plan ingestion and revised fuel/catering sequencing remain unqualified. At the next simulator session:

1. Open ADM at a safe stand; expect automatic connection, one first-use setup acknowledgement and a correct GSX route/type/planned-load display. Confirm that no service starts before the Chat approval.
2. Approve services. Let water/lavatory resolve; verify fuel is observed requested before catering is sent. Answer any GSX operator/fuel choice in the simulator. Both calls should have separate receipts; no routine waiting chatter should appear.
3. Verify boarding is withheld until required fuel and preboarding work resolve, then test boarding and closure approvals/manual release. Reopen once to confirm setup persists and unfinished work restores paused.

If the installed GSX version lacks a usable handler plan, manual entry remains available. Final signed loadsheets, automatic fuel-menu answers, real cancellation and automatic unsupported equipment release are not implemented. Full M4/M5 live qualification and later M8 distribution work remain separate.
