# M8 implementation and local beta — 22 October 2026

The requested UI cleanup, guided recovery and local Windows packaging are implemented and tested. **M8's public-release acceptance is still pending**: clean-machine/live qualification and third-party redistribution decisions remain open. No public release or next milestone was started.

## User-facing changes

- Four tabs: Chat, Profiles, Flight, Settings. Services and the status rail are removed. Profiles has compact service choices, airline creation, aircraft-specific overrides and optional service ordering. Boarding/fuel constraints remain enforced.
- Settings focuses on appearance, message sound and optional delays; limits and support are collapsed. Old hidden do-not-disturb preferences migrate into the visible mute choice. Automatic connection is enabled on normal launch.
- Flight keeps the current plan, manual fallback and sector history. Removed the advanced recovery console and duplicated setup controls. Final arrival is absent from both handling and profile choices; legacy enum/JSON identities remain readable. Deboarding stays disabled.
- Chat supplies specific recovery actions: reconnect, enter flight details, open GSX, review/resume, restart an uncertain available service, or repair installation. Recovery does not claim cancellation/completion or replay an unknown request. A running fault is not falsely described as paused.
- The supplied MP3 remains the dispatcher sound. Quiet system service lines never notify. Clean visible launch and archival audit behaviour remain unchanged.

## Deployment

Pilots need Windows x64, MSFS 2024 and GSX; **no developer SDK or separate .NET installation**. The self-contained local package contains .NET/WPF and the matched SimConnect client DLL pair, not SDK tools/libraries. SDK availability is a developer build concern.

The per-user installer verifies the payload manifest before writing, rejects unsafe paths and directory links, refuses unrelated populated folders or a running installed ADM, and rolls back partial application writes on failure. Updates/uninstall preserve personal data and unowned files. Repair validates the retained installer against its package manifest, closes ADM safely and runs the installer. The unsigned package has no publisher signature; manifest hashes detect damage, not publisher identity.

Local package: `C:\Projects\ADM\artifacts\releases\ADM-0.8.0-beta.1-local-beta.zip`

Extract the whole ZIP and run **Install.cmd**. Open ADM from Start after loading MSFS/GSX. The default install is `%LOCALAPPDATA%\Programs\ADM`. Keep the extracted download for repair; if moved, select that ADM installer folder. Use Windows Apps or the installed Uninstall-ADM.ps1 to uninstall. Personal `%LOCALAPPDATA%\ADM` data is retained. Full [Windows instructions](windows-beta-setup.md).

## Architecture and changed files

The existing Domain/Application/Infrastructure ports and immutable operational plan remain. This milestone changes Desktop presentation and deployment; it introduces no new real GSX command.

- Desktop shell: `MainWindow.xaml/.cs`, `App.xaml.cs`, `ADM.Desktop.csproj`.
- Presentation: `MainViewModel.cs`, `LiveOperationsViewModel.cs`, new `RecoveryGuide.cs`.
- Views: new `ProfilesView.xaml/.cs`; simplified `SettingsView.xaml`, `FlightView.xaml`, `LiveChatView.xaml`; removed ServicesView and LiveServicesView XAML/code-behind.
- Services: new `SupportReport.cs` (explicit allowlist export, local GSX help) and `InstallationRepair.cs` (runtime presence and validated installer lookup).
- Verification: new `M8RecoveryTests.cs`; updated `ShellAndProfileTests.cs`, `SimpleStartupTests.cs` and Desktop UI verification harness.
- Packaging: new `Publish-Beta.ps1`, `Install-ADM.ps1`, `Uninstall-ADM.ps1`, `Test-BetaInstaller.ps1`. Generated payload, manifest, ZIP and checksum live under artifacts/releases; latest paths in artifacts/m8-package.json.
- Documentation: README, this report, Windows setup, release checklist, qualification matrix, privacy, licensing inventory/notices, compatibility/architecture/state-machine addenda and specification M8 status.

Paths in the source groups above are relative to `src/ADM.Desktop` and `tests/ADM.Desktop.Tests` respectively. Existing profile, command journal and GSX wire contracts are retained.

## Verification

| Check | Result |
| --- | --- |
| Release solution build | Pass, zero warnings/errors |
| xUnit | 341 passed: Domain 73, Application 47, Conversation 39, Desktop 52, Integration 51, EndToEnd 79 |
| WPF phone/tablet, themes, profiles, quiet lines, guided recovery and binding checks | 28 passed, both developer output and packaged executable |
| Installer / update / uninstall / corruption / traversal / rollback / self-contained startup | 13 passed in isolated workspace folders |
| Formatting verification | Pass |
| Supplied sound | Decodes; no sound played in verification |

Evidence: artifacts/m8-final-tests, artifacts/m8-installer-results.txt, artifacts/ui/ui-results.txt and m8-*.png screenshots. These checks used fake adapters/isolated storage and sent no real GSX service requests. Installer tests suppressed real shortcut/registry registration, so they do not substitute for clean-PC acceptance.

## Remaining gates

The [qualification matrix](beta-test-matrix.md) and [release checklist](release-checklist.md) identify the outstanding work. The installed SDK terms did not establish a verified file-specific redistribution grant for the client pair. Public bundling remains gated; this local technical package does not establish legal clearance. The owner must also choose an original-code licence and establish the supplied MP3's public bundling rights. See [licensing review](licensing/release-review.md).

Real full handling, aircraft/version breadth, Any2GSX coexistence, clean Windows installation/repair/registration and human accessibility checks remain pending. Unsupported GSX cancellation and universal door/equipment release still need manual handling. Planned GSX figures are not a final loadsheet. Continue M8 qualification next; do not begin another milestone yet.

## Superseding delivery follow-up

The [M8 distribution follow-up](m8-distribution-followup.md) replaces the pilot-facing script installer with a conventional setup executable, adds the supplied icon/defaults and configured GitHub update checks, fixes window input handling and verifies the current Fenix brake reading. Its 357 regression tests, 33 WPF checks and 12 native installer checks are the current evidence. Earlier script installer results above remain historical.
