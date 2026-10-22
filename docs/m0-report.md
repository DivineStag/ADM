# M0 implementation and verification

Date: 20 October 2026. The repository originally contained only `ADM_Codex_Technical_Spec_v4.md`. It was read in full and remains unchanged. Git was initialized locally; no commits, remote, pull request or publication were created. All application files are new.

## M0 acceptance

| Exit requirement | Evidence |
|---|---|
| Fresh solution and prescribed module boundaries | `ADM.sln`, twelve source projects, five test projects |
| .NET 10 WPF x64, DI, logging | Desktop project, Generic Host startup, file diagnostics |
| Formatting/analyzers/CI | `.editorconfig`, shared build properties, Windows workflow |
| Immutable DTOs, events, clocks and interfaces | Domain/Contracts; system clock in Infrastructure |
| Independent duty/sector/ground sessions and gate contracts | Domain session records, shared fixture factory |
| ICAO validation and profile contracts | Offline resolver, layered immutable snapshots, JSON fixture test |
| Fake GSX/SimConnect adapters | Explicitly labelled fake capabilities and simulation streams |
| Build and tests | Local build/test results below |
| Fresh app opens without simulator | Release WPF `ContentRendered` marker and exit code 0 |

## Verification

Installed SDK: 10.0.402 x64. Verification ran on Windows with .NET 10 and Windows Desktop runtime 10.0.13.

- `dotnet build ADM.sln`: passed, zero warnings, zero errors.
- `dotnet test ADM.sln`: passed; 44 tests, zero failures, zero skipped across five suites.
- Release build: passed, zero warnings, zero errors.
- Release tests: passed before the final receipt-only regression was added (43 tests); the final Debug run includes that additional test (44).
- `dotnet format ADM.sln --verify-no-changes --no-restore`: passed.
- `scripts/Test-DesktopStartup.ps1 -Configuration Release`: passed. WPF rendered, viewmodel/fake dependencies resolved, monitor started, and process closed cleanly.
- CI workflow is configured but has not been executed on GitHub; no remote exists.

TRX outputs are local ignored artifacts under `artifacts/test-results`, `artifacts/release-test-results`, and `artifacts/final-test-results`. Smoke diagnostics are under `%LOCALAPPDATA%\ADM\logs`.

The initial build exposed one missing test-host package reference; it was corrected before successful verification. The first sandboxed CLI attempts could not access normal .NET caches; approved builds restored packages and ran normally.

## Limitations and next milestone

The UI is a minimal read-only foundation shell, not M1's four-section experience. Full event reducers, conversation parsing, scheduling, service prerequisites, boarding/closure workflows and simulator monitoring are not operational. There are no real API connections, persistence, migrations, profile editing, notifications, delay simulation, recovery or sector-transition detection. Fake deduplication is memory-only. Simulator SDK/API validation remains M3 work. Profiles are synthetic fixtures, not airline procedures; only three specification example codes are in the development allowlist.

No API commands, endpoint schemas or aircraft support are claimed. Unsupported fake cancellation/equipment operations return Rejected. Accepted requests do not generate state observations. There is no pushback command contract.

Windows setup and exact execution commands are in `README.md`. The next recommended milestone is **M1 — Premium desktop experience**, using mock data. Work stopped at M0.

## Created file manifest

The following list covers source/configuration/documentation files; build outputs, NuGet caches and Git internals are excluded. The supplied technical specification is excluded because it was not modified.

- `.editorconfig`
- `.github/workflows/build.yml`
- `.gitignore`
- `ADM.sln`
- `Directory.Build.props`
- `docs/architecture.md`
- `docs/compatibility.md`
- `docs/gsx-api-capabilities.md`
- `docs/m0-report.md`
- `docs/release-checklist.md`
- `docs/state-machine.md`
- `global.json`
- `profiles/airline-catalogue.json`
- `profiles/airline-default.json`
- `profiles/airport-default.json`
- `profiles/dispatchers-default.json`
- `profiles/README.md`
- `README.md`
- `scripts/Test-DesktopStartup.ps1`
- `src/ADM.Application/ADM.Application.csproj`
- `src/ADM.Application/AirlineIdentityResolver.cs`
- `src/ADM.Application/FoundationMonitor.cs`
- `src/ADM.Application/FoundationServicePlanResolver.cs`
- `src/ADM.Application/SampleSessionFactory.cs`
- `src/ADM.Compatibility/ADM.Compatibility.csproj`
- `src/ADM.Contracts/ADM.Contracts.csproj`
- `src/ADM.Contracts/IntegrationContracts.cs`
- `src/ADM.Contracts/OperationalContracts.cs`
- `src/ADM.Conversation/ADM.Conversation.csproj`
- `src/ADM.Desktop/ADM.Desktop.csproj`
- `src/ADM.Desktop/App.xaml`
- `src/ADM.Desktop/App.xaml.cs`
- `src/ADM.Desktop/MainViewModel.cs`
- `src/ADM.Desktop/MainWindow.xaml`
- `src/ADM.Desktop/MainWindow.xaml.cs`
- `src/ADM.Domain/ADM.Domain.csproj`
- `src/ADM.Domain/OperationalTypes.cs`
- `src/ADM.Domain/Profiles.cs`
- `src/ADM.Domain/Sessions.cs`
- `src/ADM.Domain/StateTransitions.cs`
- `src/ADM.Infrastructure/ADM.Infrastructure.csproj`
- `src/ADM.Infrastructure/LocalFileLoggerProvider.cs`
- `src/ADM.Infrastructure/SystemOperationalClock.cs`
- `src/ADM.Integrations.Gsx/ADM.Integrations.Gsx.csproj`
- `src/ADM.Integrations.Gsx/FakeGsxGateway.cs`
- `src/ADM.Integrations.Sim/ADM.Integrations.Sim.csproj`
- `src/ADM.Integrations.Sim/FakeSimulatorTelemetry.cs`
- `src/ADM.Integrations.SimBrief/ADM.Integrations.SimBrief.csproj`
- `src/ADM.Operations/ADM.Operations.csproj`
- `src/ADM.Simulation/ADM.Simulation.csproj`
- `tests/ADM.Application.Tests/ADM.Application.Tests.csproj`
- `tests/ADM.Application.Tests/AirlineIdentityTests.cs`
- `tests/ADM.Application.Tests/ProfileJsonContractTests.cs`
- `tests/ADM.Application.Tests/ProfileSnapshotTests.cs`
- `tests/ADM.Conversation.Tests/ADM.Conversation.Tests.csproj`
- `tests/ADM.Conversation.Tests/ConversationContractTests.cs`
- `tests/ADM.Domain.Tests/ADM.Domain.Tests.csproj`
- `tests/ADM.Domain.Tests/StateTransitionTests.cs`
- `tests/ADM.EndToEnd.Tests/ADM.EndToEnd.Tests.csproj`
- `tests/ADM.EndToEnd.Tests/FoundationCompositionTests.cs`
- `tests/ADM.Integration.ContractTests/ADM.Integration.ContractTests.csproj`
- `tests/ADM.Integration.ContractTests/FakeAdapterTests.cs`

