# M4 Windows setup and deferred live acceptance

Historical M4 guide: the current M5 build extends handling through boarding and closure. Use [the current M5 guide](m5-live-test-guide.md) for that build.

**Run this later when simulator access is available.** M4's automated checks do not establish real workflow acceptance. The pilot has confirmed competing Any2GSX control is disabled; retain that configuration for the aircraft being tested.

## Build and launch

Windows x64, .NET SDK 10.0.402 (or accepted later 10.0.4xx patch), MSFS 2024 and a GSX version supporting the documented Remote API v2 are required for live use. Install the official MSFS 2024 SDK at `C:\MSFS 2024 SDK`, or pass `-p:MsfsSdkPath="D:\MSFS 2024 SDK"` at build. Visual Studio with .NET desktop development and .NET 10 support is optional; open ADM.sln and select ADM.Desktop. No API keys are needed.

```powershell
Set-Location 'C:\Projects\ADM'
dotnet restore ADM.sln
dotnet build ADM.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet test ADM.sln -c Release --no-build --no-restore
dotnet run --project .\src\ADM.Desktop\ADM.Desktop.csproj -c Release --no-build
```

Without the installed SDK the demo still builds/runs, but real telemetry reports SDK unavailable. Keep vendor SDK binaries out of public distribution until licensing review. On a machine without the SDK used to build ADM, running the framework-dependent desktop requires the x64 .NET 10 Windows Desktop runtime and a properly licensed SDK integration deployment.

## Prepare the operation

1. In Settings, select airline, operation and explicit aircraft type/family if using overrides. Configure Automatic/Ask/Disabled policies and phases; select **Save handling profile**. Boarding must follow all preboarding services, including fuel. Edits apply only to a new operation.
2. Load a test flight at the matching origin stand. Keep engines off, parking brake set and aircraft stationary. Let GSX initialize and enable its local remote server. Leave required Any2GSX fuel/payload/aircraft integration enabled while competing service automation stays off. ADM cannot inspect those settings.
3. In Flight, explicitly import the latest SimBrief plan using your Pilot ID/alias, load the last local cache, or enter and save a manual flight. Confirm route/type; plans older than 24 hours are rejected. Account identity is not saved. Family override keys are explicit and optional.
4. Select First departure or Turnaround and review the airline selection/provenance. Automatic detection only recognizes validated ICAO prefixes; registrations and unknown prefixes use GENERIC. Confirm aircraft and integration checklist items.
5. If GSX already shows completed services, only acknowledge that those results belong to this flight after checking them. Otherwise they remain uncertain. This checkbox is evidence review, not permission to invent completion.
6. Choose **Connect MSFS / GSX**, wait for both current connections, then **Start live handling**. This authorizes the configured preboarding service requests. GSX menu/operator choices still belong to the pilot. Mark **Gate checked and ready** only after a real gate check.

## Acceptance to record

- Run first-departure and turnaround cases. Check applicability, configured service order, Ask approvals and real GSX transitions. Request acceptance alone never proves completion. Confirm no duplicate requests while a service is pending or active.
- Confirm missing telemetry, motion, engines-on or brake-off prevents further requests. Disconnect/reconnect and application restart must require review/reconciliation before continuing. Unknown delivery must not resend automatically.
- Check a manual GSX change and a reload/reset. A completed service reset to available must not retain old fuel readiness. Review current services and choose **Reconcile current states**; unresolved Unknown work may require manual intervention rather than a retry.
- Verify all required services have reliable completion or explicit permitted resolution, gate readiness is attested and real holds cleared. The session should reach **Awaiting boarding approval** once. Preserve local diagnostics and the checkpoint database as acceptance evidence.
- Stop at that boundary. M4 does not send boarding, closure/disconnect, cancellation or pushback. **Archive operation** retains history without cancelling GSX work. M5 implementation remains separate.

Use a test flight where observing and manually resolving services is acceptable. No real parallel-service support is claimed by the empty shipped capability catalogue; the same-phase fallback is serial.

## Local data and diagnostics

Normal data is under `%LOCALAPPDATA%\ADM`: versioned airline JSON and backups, SQLite session/command history and normalized flight cache, optional `aircraft-capabilities.json`, appearance preferences and `logs`. The full OFP and SimBrief account ID are not retained. One process owns the dispatch database; close the other ADM instance if ownership is reported. Do not delete an active database or its ownership file to force a second controller.

Profile schema/revision errors retain a last-valid backup or generic fallback with diagnostics. Startup restores active operations paused; it never reconnects or repeats a saved request on its own. A visible operation status explains failed prerequisites. If the SDK is unavailable, correct its path and rebuild before live testing.

Offline desktop verification:

```powershell
.\scripts\Test-DesktopStartup.ps1 -Configuration Release
.\scripts\Test-DesktopUi.ps1 -Configuration Release
dotnet format ADM.sln --verify-no-changes --no-restore
```

The UI harness uses isolated data under `artifacts\ui\isolated` and no simulator/network calls. Results/screenshots are under `artifacts\ui`. These are desktop checks, not live acceptance.
