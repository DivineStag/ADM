# M8 follow-up: setup, profiles, input and parked detection

**Superseded readiness policy:** beta 0.8.0-beta.3 delegates physical service-readiness to GSX and removes live engine/brake polling, including the Fenix mapping. The earlier implementation/evidence below remains historical. See [current policy](gsx-readiness-policy.md).

22 October 2026. This supersedes the earlier M8 pilot-facing script installation. Local implementation and verification pass; public-release qualification remains open. No GitHub repository or public release was created.

## Delivered behaviour

- The supplied project logo is embedded as a multi-resolution Windows icon in ADM and its installer. The original PNG is unchanged.
- **ADM-Setup.exe** is a conventional per-user Windows wizard. It includes .NET/WPF and the matched SimConnect runtime pair, adds Start/Apps entries in normal installation, offers an optional desktop shortcut, retains setup for repair and provides a native uninstaller. No pilot PowerShell, SDK or separate .NET setup is needed.
- Configured builds check the project's GitHub stable releases automatically. Settings → Help & support offers explicit installation after handling is inactive. Asset origin, version, size and SHA-256 digest are checked; failed downloads cannot launch. ADM closes its connections before opening the setup wizard. No repository is baked into this local build, so it makes no update requests.
- The 25 supplied JSON profiles are bundled defaults. Personal profiles/valid rollback revisions take precedence; updates do not overwrite customisation. Missing or invalid personal data can use the validated bundled fallback. Default loading does not write personal files. The import's validated ICAO callsign selects the airline, aircraft and operation in Profiles, once per changed flight context, preserving drafts during repeated figure refreshes. Active operational plans remain immutable.
- Windows manages the native window frame and input bounds. ADM is neither topmost nor a transparent overlay. Dropdowns close on deactivation and their popup open state is bound both ways. File dialogs have an explicit ADM owner. A single normal instance is allowed.
- Fenix A319/A320/A321 brake readiness uses the verified cockpit variable read-only. Finite nonzero SDK Bool values are interpreted according to the official contract. Missing/stale values remain unknown. Preparation identifies the specific outstanding condition instead of repeating the combined parked/engines/brake instruction.

## Setup and execution

Built installer:

`C:\Projects\ADM\artifacts\releases\ADM-0.8.0-beta.2-local-setup\ADM-Setup.exe`

SHA-256: `9B64AF4DDA5C28618E38583F468B6616EC28FF7E0ADC51685F0879E1AD32424A`

Close the old ADM, run this setup and follow its wizard. Normal installation defaults to `%LOCALAPPDATA%\Programs\ADM`. Open Aircraft Dispatch Manager from Start after loading MSFS/GSX. Existing `%LOCALAPPDATA%\ADM` profiles, aircraft setup and archived history remain. Uninstall through Windows Apps. Chat's repair action opens the cached setup; if it is missing, select the downloaded ADM-Setup.exe. This local beta is unsigned, so Windows can identify an unknown publisher.

[Full Windows instructions](windows-beta-setup.md). The old Install.cmd/script ZIP is historical engineering output and is superseded by the setup executable.

## Verification

| Check | Result / evidence |
| --- | --- |
| Release solution build | Pass, zero warnings/errors |
| Automated regression suites | **357 passed**: Application 48, Conversation 39, Domain 73, EndToEnd 79, Integration 55, Desktop 63; artifacts/m8-followup-final-tests |
| WPF verification | **33 passed**, both developer output and installed self-contained payload; artifacts/ui/ui-results.txt |
| Native installer qualification | **12 passed**; artifacts/m8-native-installer-results.txt |
| Installed startup/shutdown | Pass with fake adapters and isolated state |
| Formatting | dotnet format --verify-no-changes passes |
| Read-only live Fenix A319 probe | Standard brake 0, cockpit brake 1; updated reading on ground=true, brake=true, engines=false; no service request |

Native setup checks include install, all 25 defaults, self-contained runtime, cached installer/hash, installed startup/UI, setup rerun, cached repair, uninstall and unowned-file preservation. The qualification build suppresses actual Start/Apps registration and uses an isolated workspace install folder. Clean-machine registration and the production wizard still need human qualification. The extra focus-test window belongs to the isolated verification process and is closed afterward.

The live probe also verified a matching aircraft and route; it does not prove a complete handling cycle. [Brake/profile evidence and primary references](parking-and-profiles.md). Tests sent no service requests and did not reset personal ADM data.

## Changed files

- Logo/deployment metadata: src/ADM.Desktop/Assets/ADM.ico and ADM.Desktop.csproj.
- Window/input: MainWindow.xaml/.cs, Themes/Controls.xaml, explicit owners in Services/ProfileFileDialogs.cs, SupportReport.cs and InstallationRepair.cs; Verification/NativeInputChecks.cs and UiVerification.cs.
- Defaults: profiles/bundled/*.json (25 files), JsonAirlineProfileRepository.cs and App's repository wiring.
- Flight/profile selection: MainViewModel.cs, Presentation/LiveOperationsViewModel.cs, ProfileEditorViewModel.cs and Views/FlightView.xaml.
- Updates: Services/GitHubUpdates.cs, Presentation/UpdatesViewModel.cs, Views/SettingsView.xaml and App startup/shutdown.
- Packaging: installer/ADM.iss, beta-notice.txt, scripts/Publish-Installer.ps1, Test-WindowsInstaller.ps1 and .github/workflows/release-draft.yml.
- Tests: BundledProfileTests.cs, GitHubUpdateTests.cs, SimpleStartupTests.cs, SimConnectTelemetryTests.cs and AutomaticPreparationTests.cs.
- Sim telemetry: SimConnectTelemetry.cs and ManagedSimConnectSession.cs.
- Documentation: README, Windows setup, GitHub distribution, this report, parking/profile evidence, privacy, release/compatibility/architecture/spec addenda and licence review.

Source paths above are relative to their existing project directories. No real GSX request verb or pushback path was added.

## Remaining decisions and qualification

Provide the intended GitHub owner/repository URL to connect future builds. Source upload and public distribution have not occurred. The prepared release workflow requires a protected release environment, release runner, signing and explicit human review; it produces a draft. [GitHub configuration and updater design](github-distribution.md).

The latest-release download URL always provides the full current installer. There is no separate web bootstrapper; the full setup also works offline after download. Update **checks** are automatic; installation remains pilot-controlled and cannot run during active handling. No live GitHub end-to-end update has been verified without an actual published repository/release.

Public shipping still needs original-code licence selection, runtime/media distribution clearance, signing, clean-PC installation/repair/registration, human accessibility review and the live aircraft/version matrix. GitHub hosting alone does not remove Windows publisher warnings. No new milestone has begun.

## Current beta.3 policy and installer

[GSX owns cockpit readiness](gsx-readiness-policy.md), superseding the temporary Fenix-specific solution above. Beta.3 removes brake/engine polling and duplicate parked-state service guards. It passes a clean Release build, all 363 regressions, 33 packaged WPF checks, packaged startup and formatting. A live read-only check found MSFS/GSX unavailable, so new live acceptance remains pending. The native installation mechanism is unchanged from the 12-check beta.2 qualification.

Current setup: `C:\Projects\ADM\artifacts\releases\ADM-0.8.0-beta.3-local-setup\ADM-Setup.exe`. All earlier setup paths above are historical.
