# M5 — boarding, closure and handover

Implemented 21 October 2026. **Live acceptance is pending.** The pilot authorized M5 development and requested that simulator actions and questions be left until the end. No simulator connection, service request or change to the pilot's saved operational database was made during this work. M6 has not started.

## Fix for the pause after water

The existing local runtime log shows Water requested/4, performing/5, then available/1, with no completed/6 event and no logged socket failure. M4 paused this ambiguous transition as a possible reset, preventing the next service. This was an operational pause, rather than evidence of a transport disconnect.

The pilot clarified that routine service completion must not require confirmations. The implementation now:

- Retains an explicit GSX completed/6 observation through a subsequent available/1 state in the same generation, so the 500 ms desktop poll cannot lose a short completion event. A new service cycle, missing service, engine loss or context change clears the retained evidence.
- Resolves an uninterrupted **optional routine** service's observed active-to-idle cycle automatically as `CycleEnded`. It remains visibly distinct from verified `Completed`: Services says **Returned idle · GSX cycle ended**, and completion provenance stays Unknown. It is not a claim of successful work or confirmed cancellation. This pilot-requested workflow change permits subsequent compatible services without a routine confirmation or duplicate request.
- Never uses that rule for fuel, boarding or a profile-required service. Fuel/boarding returning idle without completion evidence produces a contextual manual check; a verified completed event needs no extra completion confirmation. A profile-required routine service with missing evidence remains unresolved, with no routine completion prompt.
- Retains real reset/disconnect/aircraft-change protections. A return to idle without ever observing active work does not establish an ended cycle. Historical Unknown deliveries are not silently converted into completion.

Regression coverage reproduces Water active → available, Lavatory skipped while still planned, then exactly one Refuelling request without a routine confirmation.

## M5 implementation

Live boarding now uses the already documented Remote API v2 `service.trigger` with canonical service ID `Boarding`. The existing durable journal, last-moment policy/telemetry checks and uncertain-delivery handling apply. The worker applies pending observations before interpreting approval, and requires an eligible current question. Repeated replies cannot produce repeated sends. Material changes invalidate unsent boarding clearance and return it to Planned for a fresh approval.

Verified or explicitly pilot-attested boarding completion produces a separate closure question. No door or equipment obligation becomes satisfied merely because closure was approved. No verified direction-specific automatic release action exists for the current aircraft/API manifest, so ADM asks separately for:

1. Required passenger/cargo doors closed.
2. Jetway disconnected or passenger stairs removed.
3. Remaining ground equipment clear, including chocks where appropriate.

Each confirmation records User evidence and PilotAttested provenance. Distinct quick replies carry the release item, so an old door-confirmation reply cannot clear the next item. Disconnect/restart invalidates closure approval and all partial release checks. A fresh approval and current confirmations are needed after reconciliation. Completion requires every release obligation satisfied; final wording distinguishes pilot confirmation from external verification.

Pushback remains entirely the pilot's responsibility. No Departure/pushback mapping or automatic equipment toggle was added. The worker stops issuing requests at ground completion while simulator monitoring continues. Automatic sector-arrival detection remains M7 work.

## Desktop and diagnostics

Live Chat has contextual quick replies using the same message/intent path as typed commands, important-message sounds through existing mute/DND/volume controls, and scrolling that follows new messages when already near the bottom. Services distinguishes pilot-attested completion, GSX verified completion, cycle-ended routine work and pending manual release checks. Headers expose boarding, closure and completed stages.

A stopped background reader now exposes a useful connection error, pauses handling and allows an explicit reconnect followed by reconciliation. Pause reasons and cycle-ended decisions are logged. The offline monitor's startup log now clearly identifies it as separate from live handling.

## Changed files

- Domain: `OperationalTypes.cs`, `Operations.cs`, `OperationalReducer.cs`, `OperationalCommandPolicy.cs` — explicit cycle-ended outcome, fuel/boarding checks, stale approval protection and release reset.
- Contracts: `OperationalContracts.cs` — contextual completion intent.
- Application: `LiveDispatchCoordinator.cs`, `LocalDispatchSession.cs`, `FoundationMonitor.cs` — M5 dispatch, durable evidence validation and useful diagnostics.
- GSX: `GsxRemoteGateway.cs` — completion-evidence retention, without changing raw protocol mapping or inventing commands.
- Conversation: `LocalIntentParser.cs`, `LocalConversationEngine.cs` — scoped confirmations and quick replies.
- Desktop: `App.xaml.cs`, `MainViewModel.cs`, `Presentation/LiveOperationsViewModel.cs`, `Views/LiveChatView.xaml` and code-behind, `Views/LiveServicesView.xaml`, `Views/FlightView.xaml`, `Verification/UiVerification.cs`.
- Tests: new `ADM.Domain.Tests/ServiceCycleTests.cs` and `ADM.EndToEnd.Tests/M5WorkflowTests.cs`; updated live workflow, GSX remote and conversation tests.
- Documentation: this report, `m5-live-test-guide.md`, README, current architecture/state/compatibility notes and milestone status.

## Verification

Final Release build: **0 warnings, 0 errors**. All **231 xUnit tests passed**, with no failures or skips: Domain 62, Application 32, Conversation 36, Desktop 26, Integration 36, EndToEnd 39. Final TRX evidence is under `artifacts/m5-verified-tests`.

All **34 WPF acceptance checks passed**, including the new live quick-reply binding, compact composer and release-provenance checks, with no binding errors. The desktop startup/shutdown smoke test and `dotnet format ADM.sln --verify-no-changes --no-restore` passed. Dark/light live-view screenshots in `artifacts/ui` were visually inspected. The added view fixture initially exposed repeated ContentRendered harness entry; the harness now starts once and the final full run passed.

Contract fixtures are synthetic; they do not prove live aircraft behavior. WPF live-view presentation fixtures are explicitly labelled offline and start no connection. The restricted formatter could not open its build-host pipe; the final formatter and test runs used unrestricted local IPC. No simulated result is presented as real acceptance.

## Remaining acceptance and limitations

The pilot's M4 test established a real water cycle, lavatory skip and profile saving, but did not reach refuelling/boarding readiness. The full M4 sequence and M5 real boarding-to-handover exit gate therefore remain pending. No real automatic door/equipment action or concurrency group is certified. Any2GSX configuration and gate readiness remain pilot attestations. Real cancellation remains unsupported and never falsely reported as successful. Required work with missing evidence, unknown deliveries and real service resets still require safe review rather than blind retry.

Use [the later live-test guide](m5-live-test-guide.md). Complete this acceptance before M6.
