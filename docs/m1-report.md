# M1 — Premium desktop experience

Date: 20 October 2026. M1 is complete as an offline interface preview. M2 has not been started. The original specification and the M0 domain/integration implementations were not changed.

## Acceptance criteria

| M1 requirement | Delivered |
|---|---|
| Dark/light compact/chat/tablet shell | Semantic resource dictionaries, rounded custom chrome, minimum 420 × 640, optional overview at 840 px and wider |
| Messages, composer, timestamps, quick replies, flight header | Four-section MVVM shell, distinct incoming/outgoing bubbles, shared presentation reply handler, Enter/Shift+Enter behavior |
| Services, Flight, Settings and status | Service timeline with inline confirmations; sample plan/aircraft; preferences and diagnostics |
| Gate chip, sector history, recovery banner, persona | Source-qualified simulated gate; read-only sample history; interruption preview; stable Sarah persona preview |
| Airline source, per-operation and aircraft editors | Mock BAW482 source badge, four airline choices, all three operation types, family/exact-type override editors and live resolved preview |
| Local sound and test preview | Original PCM notification, volume/mute/DND policy, Play notification control, missing/playback-failure feedback |
| Keyboard/accessibility testing | Live WPF focus traversal, named interactive controls, UI Automation Invoke, shared composer key handler and minimum-size checks |
| Exit: mock happy path with no external calls | Services → boarding approval → sample boarding → closure approval → separate sample release → pilot handover |

## Architecture and safety boundaries

MainViewModel handles navigation/preferences and owns separate demo-session and profile-editor viewmodels. The four views are WPF UserControls. Shared ObservableObject/RelayCommand classes support bindings without an additional MVVM package. Theme brushes and control templates are separate resources. Window code-behind handles only chrome and viewport sizing; Chat code-behind handles key input and scrolling.

The scripted presentation model has no GSX, SimConnect, HTTP or persistence dependency. It never starts a real service. Sample approvals are scoped to the current demo ID; Reset clears them. Cancellation remains pending until a separate explicit sample observation. Skipped fuel does not become completed. An interruption clears sample approvals and puts the sample into NeedsReview without replay. These are interface fixtures, not M2's operational reducer, scheduler or intent parser.

Profile edits are isolated per airline and operation in memory. M0's foundation resolver creates new immutable previews and records family/type source precedence. No edit replaces an active demo plan or saves an airline profile. The source badge, flight plan, aircraft, gate and history are clearly labelled mock/simulated data. No claim of verified airline procedures or real telemetry is made.

Appearance, notifications, rail and future-realism preferences save on exit to `%LOCALAPPDATA%\ADM\appearance.json` using temporary-file replacement. Invalid files are left intact and fall back visibly. Chat, SimBrief ID, Any2GSX acknowledgement and policy edits stay in memory. UI test modes use transient preferences, mute sound and cannot overwrite saved preferences.

The original notification asset uses mathematical tones only and is reproducible using `scripts/New-NotificationSound.ps1`. Its regenerated SHA-256 matched the included file exactly. No remote audio or third-party samples are used.

## Build and test evidence

Verified locally on x64 Windows using .NET SDK 10.0.402:

- `dotnet build ADM.sln -c Release`: passed, **0 warnings, 0 errors**.
- `dotnet test ADM.sln -c Release --no-build --logger trx --results-directory artifacts/m1-test-results`: **66 passed, 0 failed, 0 skipped**, across six test projects.
- Desktop tests: 22 passed, covering approval sequencing, holds, pending cancellation, mandatory fuel, stale demo rows, reset/disconnect behavior, profile isolation, notification policy, preference fallback/atomic save and the packaged WAV format.
- `dotnet format ADM.sln --verify-no-changes --no-restore`: passed.
- `scripts/Test-DesktopStartup.ps1 -Configuration Release`: passed, fresh ContentRendered evidence and clean exit.
- `scripts/Test-DesktopUi.ps1 -Configuration Release`: **24 live WPF acceptance checks passed**, with no binding errors during the exercised navigation, theme changes, editing and happy path.

The first WPF run caught a TwoWay binding to read-only progress. It was corrected to OneWay before successful checks. Visual inspection also caught record text in selectors and default scrollbars; both were corrected. Rendered phone/tablet, dark/light, minimum-size editor, completion and interruption screenshots were then inspected.

TRX files are under `artifacts/m1-test-results`. The UI report and screenshots are under `artifacts/ui`; these are ignored local build artifacts. The Windows CI workflow now runs the desktop checks and uploads their previews alongside test results. CI has not been run remotely because no remote repository was created.

UI verification uses the actual WPF window, controls, automation peers, focus traversal and the same key handler used by PreviewKeyDown. It does not inject physical operating-system keystrokes. Hardware audibility, Narrator, other DPI scales and other Windows machines have not been manually verified.

## Windows setup and execution

Requirements remain x64 Windows and .NET SDK 10.0.402 or a compatible 10.0.4xx patch. No simulator, SDK vendor binaries, API keys or backend is required. Initial NuGet restore needs Internet access; runtime is offline.

```powershell
Set-Location 'C:\Projects\ADM'
dotnet restore ADM.sln
dotnet build ADM.sln -c Release
dotnet test ADM.sln -c Release --no-build
dotnet run --project .\src\ADM.Desktop\ADM.Desktop.csproj -c Release --no-build
```

Alternatively open `ADM.sln` in a Visual Studio installation supporting .NET 10 with the .NET desktop development workload, and choose ADM.Desktop as the startup project. Or launch `src\ADM.Desktop\bin\Release\net10.0-windows\ADM.Desktop.exe` after building. A runtime-only machine needs the x64 .NET 10 Windows Desktop runtime.

Chat's demo control advances only sample observations. The two contextual approval replies are still required. Settings contains theme/audio controls, profile/aircraft previews, setup guidance and the interruption switch. See README for the full walkthrough and verification commands.

## Limitations and next milestone

No real integration, operational intent parser, service scheduler, real approval/closure workflow, SQLite session persistence, authoritative reconciliation, live sector tracking or disruption engine exists yet. Profile editing is a preview rather than a saved airline-profile editor. Any2GSX is not inspected or modified. SimBrief never sends a request. A complete installer/licensing/release review is still later work.

The next recommended milestone is **M2 — Domain workflows and messaging**, beginning with the immutable operational reducer, prerequisite guards, service graph and contextual intent handling. M1's presentation script must remain separate from operational command permissions. Work stopped at M1.

## Files modified

- `ADM.sln`
- `.github/workflows/build.yml`
- `README.md`
- `docs/architecture.md`
- `scripts/Test-DesktopStartup.ps1`
- `src/ADM.Desktop/ADM.Desktop.csproj`
- `src/ADM.Desktop/App.xaml`
- `src/ADM.Desktop/App.xaml.cs`
- `src/ADM.Desktop/MainViewModel.cs`
- `src/ADM.Desktop/MainWindow.xaml`
- `src/ADM.Desktop/MainWindow.xaml.cs`

## Files created

- `docs/m1-report.md`
- `scripts/Test-DesktopUi.ps1`
- `scripts/New-NotificationSound.ps1`
- `src/ADM.Desktop/Assets/incoming.wav`
- `src/ADM.Desktop/Assets/README.md`
- `src/ADM.Desktop/Presentation/ObservableObject.cs`
- `src/ADM.Desktop/Presentation/RelayCommand.cs`
- `src/ADM.Desktop/Presentation/DemoModels.cs`
- `src/ADM.Desktop/Presentation/DemoSessionViewModel.cs`
- `src/ADM.Desktop/Presentation/ProfileEditorViewModel.cs`
- `src/ADM.Desktop/Services/UiPreferences.cs`
- `src/ADM.Desktop/Services/TransientUiPreferencesStore.cs`
- `src/ADM.Desktop/Services/NotificationPlayer.cs`
- `src/ADM.Desktop/Themes/Dark.xaml`
- `src/ADM.Desktop/Themes/Light.xaml`
- `src/ADM.Desktop/Themes/Controls.xaml`
- `src/ADM.Desktop/Views/ChatView.xaml`
- `src/ADM.Desktop/Views/ChatView.xaml.cs`
- `src/ADM.Desktop/Views/ServicesView.xaml`
- `src/ADM.Desktop/Views/ServicesView.xaml.cs`
- `src/ADM.Desktop/Views/FlightView.xaml`
- `src/ADM.Desktop/Views/FlightView.xaml.cs`
- `src/ADM.Desktop/Views/SettingsView.xaml`
- `src/ADM.Desktop/Views/SettingsView.xaml.cs`
- `src/ADM.Desktop/Verification/UiVerification.cs`
- `tests/ADM.Desktop.Tests/ADM.Desktop.Tests.csproj`
- `tests/ADM.Desktop.Tests/DemoPresentationTests.cs`
- `tests/ADM.Desktop.Tests/ShellAndProfileTests.cs`
- `tests/ADM.Desktop.Tests/PreferenceStoreTests.cs`
- `tests/ADM.Desktop.Tests/NotificationAssetTests.cs`
