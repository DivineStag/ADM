# M3 — Integration proof and configurable service order

Status: implementation and automated checks pass. The live test on 20 October 2026 demonstrated the documented request and authoritative GSX state transition; its captured summary is recorded below. No M4 automatic control was introduced.

## Implemented architecture

- Profiles carry configurable service phases, inherited with the existing generic → airline → family → exact-aircraft precedence and frozen in each applied plan.
- Default arrival handling remains deboarding/cleaning when required. Water, lavatory, fuel and catering share the next phase; boarding follows explicit approval, and closure follows a separate approval.
- Earlier phases form dependency barriers. Boarding cannot be configured before/together with fuel or other ground services. Missing services, invalid phases and dependency cycles are rejected.
- Equal phases allow overlap only with explicitly validated compatibility data. The sample supports the four ground services concurrently. Real aircraft concurrency remains unverified and defaults to serial handling.
- Settings saves per-airline/per-operation order for the next operation. Normal exit persists it in UI preferences. Active service history, approvals and the applied plan are not rewritten. Full versioned operational-profile storage remains M4.
- GSX Remote API v2/wire-1 adapter: verified hello/capabilities, INI port discovery, service snapshots/patches, request correlation, serialized sends, reconnect/resubscribe, source-qualified evidence, no blind retry and explicit unsupported results.
- Official SDK SimConnect adapter: thread-affine construction/subscription/receive/disposal, normalized nullable telemetry and stale-data clearing on disconnect.
- A separate opt-in integration tool composes the real adapters for observation or a single explicitly requested preboarding service. WPF retains its labelled offline simulation.

The service-order extension was added to the authoritative specification to reflect the user's new requirement. It brings the configurable planning portion forward without enabling M4's live automatic scheduler.

## Documentation and capability verification

The installed GSX manual's Remote API developer guide (pp.265–291) was reviewed for all implemented protocol surfaces. Its SHA-256, message shapes, canonical service IDs, semantic/raw mappings, command evidence and unsupported surfaces are recorded in [GSX capabilities](gsx-api-capabilities.md).

The installed official MSFS 2024 SDK reports version 1.5.7. The adapter built against its actual managed assembly and loaded its native library. The SDK-absent fallback was also built successfully. No vendor manual or DLL was added to source control. Development output may contain local SDK DLL copies; redistribution/licensing remains a release gate.

## Build and automated verification

Release solution build: **passed, 0 warnings, 0 errors**.

| Test project | Passed | Failed/skipped |
|---|---:|---:|
| Domain | 55 | 0 |
| Application | 32 | 0 |
| Conversation | 34 | 0 |
| Integration contracts | 29 | 0 |
| End-to-end | 12 | 0 |
| Desktop | 24 | 0 |
| **Total** | **186** | **0** |

Tests cover ordering barriers, validated overlap, fuel/boarding guards, profile inheritance and immutable snapshots, JSON compatibility/fallback, saved next-operation ordering, cancellation/resume, real-adapter protocol mapping, timeout/no-retry, reconnect/resubscription, disabled actions and SimConnect normalization/thread ownership. Contract fixtures are explicitly synthetic.

Additional checks:

- Formatting verification passed.
- WPF startup rendered, resolved DI and exited cleanly.
- **28 WPF acceptance checks passed**, including invalid fuel ordering, phase bindings, explicit save, new-operation application, phone/tablet layouts, keyboard handling and both approvals.
- UI screenshots were inspected. No WPF binding errors were reported.
- The SDK-absent SimConnect project build passed with 0 warnings/errors.
- Before the simulator started, a read-only probe correctly returned exit 2 with no fabricated observations or service request.

Evidence: `artifacts/m3-final-test-results/*.trx`, `artifacts/ui/ui-results.txt`, screenshots under `artifacts/ui`.

Restricted-shell compiler-server named-pipe permissions caused intermittent diagnostic-free build failures. Building with one MSBuild node and shared compilation disabled succeeded. These were environment failures; no warning/error was suppressed.

## Live integration proof

A private live probe verified SimConnect telemetry and the GSX handshake. An explicitly approved Water request was accepted, followed by independent requested/active observations. Acceptance and activity do not establish completion. No boarding, cancellation, closure or pushback was sent. Detailed captures remain local and are excluded from the public repository.

Tool shutdown deliberately invalidates its cached observations; trailing Unknown cleanup records are not a service cancellation or a simulated live reconnect test.

## Created files

- `src/ADM.Domain/ServiceSequence.cs`
- `src/ADM.Integrations.Gsx/GsxProtocol.cs`
- `src/ADM.Integrations.Gsx/GsxSocket.cs`
- `src/ADM.Integrations.Gsx/GsxRemoteGateway.cs`
- `src/ADM.Integrations.Sim/ManagedSimConnectSession.cs`
- `src/ADM.Integrations.Sim/SimConnectTelemetry.cs`
- `src/ADM.IntegrationProof/ADM.IntegrationProof.csproj`
- `src/ADM.IntegrationProof/Program.cs`
- `tests/ADM.Domain.Tests/ServiceSequenceTests.cs`
- `tests/ADM.Application.Tests/ServicePhaseProfileTests.cs`
- `tests/ADM.Integration.ContractTests/GsxRemoteTests.cs`
- `tests/ADM.Integration.ContractTests/SimConnectTelemetryTests.cs`
- This report and the compact live evidence capture.

## Modified files

- `ADM.sln`, `ADM_Codex_Technical_Spec_v4.md`, `README.md`.
- Domain: `Profiles.cs`, `Operations.cs`, `OperationalReducer.cs`, `OperationalCommandPolicy.cs`.
- Application: `FoundationServicePlanResolver.cs`, `ProfileJsonCodec.cs`.
- Contracts: `IntegrationContracts.cs`.
- Sim integration: `ADM.Integrations.Sim.csproj`.
- Desktop: `MainViewModel.cs`; Presentation `ProfileEditorViewModel.cs`, `DemoSessionViewModel.cs`, `DemoModels.cs`; Services `UiPreferences.cs`; Views `SettingsView.xaml`; Verification `UiVerification.cs`.
- Profiles: `airline-default.json`, `airline-profile.schema.json`.
- Tests: `Shared/OperationalScenario.cs`, desktop `DemoPresentationTests.cs`, `ShellAndProfileTests.cs`.
- Documentation: `architecture.md`, `state-machine.md`, `gsx-api-capabilities.md`, `compatibility.md`, `release-checklist.md`.

Paths within these groups are relative to their named project directories. M0–M2 historical reports are retained.

## Exact Windows setup and execution

Windows x64, .NET SDK 10.0.402 (or accepted later patch), optional Visual Studio with .NET desktop development workload. First restore requires NuGet access.

```powershell
Set-Location 'C:\Projects\ADM'
dotnet restore ADM.sln
dotnet build ADM.sln -c Release
dotnet test ADM.sln -c Release --no-build
dotnet run --project .\src\ADM.Desktop\ADM.Desktop.csproj -c Release --no-build
```

If a restricted shell cannot access the compiler server:

```powershell
dotnet build ADM.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
```

To use the real adapters, install MSFS 2024 and GSX separately, enable GSX's local remote server, and install the official SDK at `C:\MSFS 2024 SDK` or rebuild with `-p:MsfsSdkPath="D:\MSFS 2024 SDK"`. Keep required Any2GSX aircraft integration but disable competing GSX service automation; ADM does not verify/change those settings yet.

```powershell
# Observe only:
dotnet run --project .\src\ADM.IntegrationProof -c Release --no-build -- --seconds 30
# Explicitly request one available water service in a prepared test flight:
dotnet run --project .\src\ADM.IntegrationProof -c Release --no-build -- --seconds 120 --request-service Water
```

The tool waits for fresh stationary/on-ground/parking-brake/engines-off telemetry and GSX availability. It cannot select a gate or answer GSX operator questions. Handle those manually if presented. Exit 0 means the chosen observation criteria passed; exit 2 means missing live evidence; exit 1 means usage/runtime error. Never repeat an uncertain request without reviewing actual GSX state.

## Remaining limitations and next milestone

M3 proves the core local integration path for this test setup, not universal aircraft support or a full live turnaround. Real cancellation, door/equipment release and concurrent-aircraft capability groups remain disabled. Their simulated examples do not advertise real support. The normal desktop is still the offline demo.

Persistent command reconciliation/SQLite recovery, automatic service dispatch, a full profile repository, SimBrief import/cache and Any2GSX ownership verification remain M4. Live disconnect/restart and multiple-aircraft compatibility require further integration testing. No previous command is automatically replayed; the M3 adapter's duplicate protection is process-local.

**Next recommended milestone: M4 — SimBrief, Any2GSX and automated service control**, on user instruction. No M4 work has been started.

