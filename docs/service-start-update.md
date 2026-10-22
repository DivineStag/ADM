# Service-start conversation and fuel/catering pairing

21 October 2026. This is a focused update to M5 following the pilot's live feedback; it does not start M6 or claim new live acceptance.

## Behavior

After an explicit connection, a loaded flight, aircraft/integration acknowledgements and safe current telemetry, ADM prepares the operation automatically and opens Chat. The optional **Prepare operation** button remains available for retry after correcting setup. Preparing a flight sends no service requests. The dispatcher asks **Are we ready to start services?** with **Yes, start services** and **Not yet** replies. Typed replies use the same parser. A contextual pilot approval is persisted before requests become eligible. Boarding and closure retain their separate approvals.

**Services starting. I'll keep you posted when you need to do something.** is delivered once, after the first GSX service request is accepted. Rejection or uncertain delivery does not earn that message. Repeated polls and subsequent service starts do not repeat it. A restored, unapproved operation cannot start by reconnecting or reconciling alone.

Fuel and catering now form a pilot-requested dispatch pair when configured in the same phase. They are two independent GSX service.trigger calls, sent back-to-back with separate journal identities and receipts. Each still requires its own current GSX availability, supported service, policy permission and resolved prerequisites. GSX determines whether actual execution overlaps or waits. This is not a claim of certified aircraft concurrency. Water/lavatory and other services do not gain unrestricted overlap. Configuring different fuel/catering phases keeps them sequential. A rejected/unknown fuel delivery prevents the paired catering send.

Routine GSX waiting transitions still update real holds, readiness and diagnostic logs. They no longer emit unsolicited Chat messages or notification sounds. Services retains the detailed status; asking **What's the delay?** still returns the current real reason. Chat's passive progress line stays compact. Service questions, safety/recovery failures, boarding/closure approvals and explicit answers remain available.

The retained pilot log shows that the previous build sent Catering after Refuelling reported completed/6; the new pairing changes that intended request timing. No real service was requested as part of this development.

## Build and verification

The pilot's original Release application was initially running and locked its DLLs. It was left running while an isolated SDK artifacts build verified the changes. The pilot subsequently closed the session, allowing a normal Release rebuild too. The isolated commands remain useful for verification without replacing an active process:

```powershell
Set-Location 'C:\Projects\ADM'
dotnet restore ADM.sln --artifacts-path artifacts/service-start-build
dotnet build ADM.sln -c Release --no-restore --artifacts-path artifacts/service-start-build -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet test ADM.sln -c Release --no-build --no-restore --artifacts-path artifacts/service-start-build
```

The normal launch path is `C:\Projects\ADM\src\ADM.Desktop\bin\Release\net10.0-windows\ADM.Desktop.exe`. The isolated executable is `C:\Projects\ADM\artifacts\service-start-build\bin\ADM.Desktop\release\ADM.Desktop.exe`. Run only one normal ADM instance; both paths use the same protected dispatch database. No pilot database or profile was edited by the tests. Existing saved operations retain their applied plan and service-start decision and restore paused; the new introduction applies to new operations.

The startup and UI scripts accept optional `-ExecutablePath` to verify an isolated build. Both isolated and normal Release builds passed with **0 warnings and 0 errors**. All **242 xUnit tests passed**, with no failures or skips (Domain 62, Application 32, Conversation 39, Desktop 26, Integration 36, EndToEnd 47). TRX results are in `artifacts/service-start-final-tests`. All **36 WPF checks passed**, including the new start-service reply and detailed Services status bindings, with no binding errors. The compact service-start screenshot was visually inspected. Test providers and UI fixtures are explicitly synthetic and do not substitute for the next simulator run.

## Files

The normal Release startup/shutdown check and final formatting verification also passed.

Domain operations/reducer/command policy/overlap policy; Application live coordinator/checkpoint validation; Contracts pilot intents; Conversation parser/quick replies; Desktop live/main viewmodels, Flight/Services views and WPF harness; both desktop test scripts. New end-to-end coverage is in `ServiceStartWorkflowTests.cs`; existing crash and no-dispatch fixtures explicitly model the new start gate. Conversation tests cover contextual yes/no and unprompted start rejection.

## Next live check

For a new operation, confirm preparation automatically opens Chat and sends nothing before approval. Reply Not yet, then Yes, start services; expect one starting update. Let water/lavatory resolve, then verify separate fuel and catering requests occur while both are eligible. Confirm routine vehicle/service waiting stays out of Chat while still blocking unsafe readiness and remaining visible in Services. Continue the pending M5 boarding and closure checks from `m5-live-test-guide.md`.

## Superseding UX and fuel-start correction (21 October, later update)

The current normal desktop automatically connects, remembers aircraft setup and receives its plan from GSX; the older explicit-connect/direct-import instructions above are historical. It still asks before starting services. Flight-plan/planned-load receipt wording is source-qualified and does not claim a final loadsheet.

Pilot logs showed fuel accepted followed about 40 ms later by catering accepted, but only catering was subsequently observed requested. The current implementation therefore supersedes back-to-back-on-receipt dispatch: wait for observed fuel Requested/Active/Completed before the separate catering request. This allows intended overlap after GSX recognizes fuel, with each service independently eligible. Accepted requests with no start observation pause after 20 seconds, excluding GSX menu/input waits. No automatic replay or false completion is introduced. See [current UX report, validation and remaining live checks](simple-ux-report.md).
