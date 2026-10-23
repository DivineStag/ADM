# ADM — Aircraft Dispatch Manager

Freeware Windows companion for MSFS 2024, built with C#/.NET 10 and WPF. ADM manages GSX ground services through a local dispatcher conversation. No AI subscription, ADM account, API key or hosted backend is required. Pushback always belongs to the pilot.

## Use the local beta

Pilots need Windows x64, MSFS 2024 and GSX. **No MSFS SDK or separate .NET installation is needed.** The self-contained beta includes the required runtime files. The SDK is a developer build dependency only.

Run **ADM-Setup.exe** and follow the normal Windows setup wizard. It installs per-user, creates Start/Apps entries, and caches setup for repair. See [Windows setup, repair and uninstall](docs/windows-beta-setup.md). This is a local qualification package: public distribution awaits licensing and real-device qualification.

1. Load an aircraft at a stand and let GSX load its flight plan. Enable GSX's local Remote API.
2. Open ADM; connections start automatically. For each aircraft, review the integration setup once. Keep needed Any2GSX fuel/payload integration and disable its competing service automation.
3. Approve the dispatcher's start question in **Chat**. Boarding and the final conversational sign-off each need separate approval.
4. Follow any specific recovery button shown in Chat. Quiet service-status lines have no message sound.

The four tabs are **Chat**, **Profiles**, **Flight** and **Settings**. The supplied logo is embedded in the app/installer icon. All 25 supplied profiles are bundled defaults; personal changes take precedence. The flight callsign selects the matching airline and aircraft in Profiles. Profiles contains airline creation, aircraft overrides, service choices and optional ordering. Settings contains theme, sound, optional simulated delays and support. Final arrival and deboarding are absent from public choices. Changes to saved profiles apply to future operations, never silently to active work.

GSX supplies its cached plan and available planned load figures, which are not a final loadsheet. No separate ADM SimBrief account/import is required. If a usable matching plan is unavailable, use Flight's optional manual entry. Normal reopening starts a clean visible session; saved profiles/setup and archived operational audit remain.

Default services are water, lavatory, fuel and catering in the preboarding phase, then boarding and finish. Fuel and catering have separate requests: catering waits for observed GSX fuel Requested/Active/Completed, not merely a command receipt. Live availability and aircraft capabilities govern execution. Boarding cannot precede required fuel or its other prerequisites. Unsupported cancellation remains manual. The closing-doors question is a conversational sign-off only: approval finishes ADM without closing doors, disconnecting equipment or requiring manual release confirmations. Physical actions remain with the pilot and GSX; no pushback command exists.

## Developer build

Use Windows x64 and .NET SDK **10.0.402** (or an accepted later 10.0.4xx patch). Visual Studio with .NET 10 and the .NET desktop development workload is optional. Open ADM.sln with ADM.Desktop as startup project.

```powershell
Set-Location 'C:\Projects\ADM'
dotnet restore ADM.sln
dotnet build ADM.sln -c Release
dotnet test ADM.sln -c Release --no-build
dotnet run --project .\src\ADM.Desktop\ADM.Desktop.csproj -c Release --no-build
```

Developer builds use the official MSFS 2024 SDK at `C:\MSFS 2024 SDK`; override with `-p:MsfsSdkPath="D:\MSFS 2024 SDK"`. Without it the solution builds with an unavailable adapter diagnostic. Pilots use the packaged application, which includes the matching client runtime pair. No SDK installer/tools are shipped.

## Verification and release status

The current beta includes natural local chat, varied quick replies, a separate outgoing sound, 1–5 second dispatcher reply delays, distinct message colours and a conversational door sign-off. Approving that sign-off finishes ADM without door/equipment commands. The Open GSX browser shortcut is removed. Services continue immediately while messages wait to appear.

ADM is freeware with source reuse rights reserved under [the ADM licence](LICENSE). Third-party components and audio retain their own terms. See [current release readiness](docs/public-release-readiness.md), [licensing review](docs/licensing/release-review.md) and [media provenance](docs/licensing/media-provenance.json). Earlier milestone reports are historical. No public installer has been released yet.

```powershell
dotnet format ADM.sln --verify-no-changes --no-restore
.\scripts\Test-DesktopStartup.ps1 -Configuration Release
.\scripts\Test-DesktopUi.ps1 -Configuration Release
.\scripts\Publish-Installer.ps1
```

Data stays in `%LOCALAPPDATA%\ADM`. Uninstall retains it. Support export is an explicit local save of an allowlisted status report; nothing is uploaded. See [architecture](docs/architecture.md), [state machine](docs/state-machine.md), [GSX evidence](docs/gsx-api-capabilities.md) and [compatibility](docs/compatibility.md). Historical milestone reports retain their original context; this README and the current release-readiness report describe the current UI/deployment.

## GitHub and updates

A configured build checks GitHub's latest stable release at startup. Installing an update is explicit, verifies the installer digest/size and waits until handling is inactive. The project repository is [DivineStag/ADM](https://github.com/DivineStag/ADM). Builds target its public stable releases; no installer release has been published. See [repository setup, signing and update behaviour](docs/github-distribution.md). The conventional full installer is downloaded through GitHub's latest-release link rather than a separate web bootstrapper.

[GSX now owns cockpit service-readiness checks](docs/gsx-readiness-policy.md). ADM no longer polls engines/brakes or independently blocks on parked-state readings. SimConnect remains for aircraft identity and passive flight-phase tracking. Fuel/boarding dependencies, approvals, GSX availability and request/completion safeguards remain. The earlier Fenix-specific mapping is historical and no longer used.

## Source checkout

The public source includes ADM's original generated notification WAV. The separately supplied MP3 is excluded until public redistribution evidence is confirmed; local installations prefer that MP3 when present. SDK/vendor runtime DLLs, installers, logs and operational databases are excluded from Git. Without a local SDK, developer builds and fake-adapter tests work, while live SimConnect is unavailable. Pilots will use the self-contained installer once distribution is cleared. The source is prepared for public review with all rights reserved pending the owner's licence choice; no open-source licence has been silently selected.
