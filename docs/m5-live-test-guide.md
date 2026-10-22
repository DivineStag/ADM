# M5 — later simulator test

This is the remaining action list for when the pilot has time. Development/offline verification does not establish real M4/M5 acceptance.

Current service-start and fuel/catering request timing is described in [the service-start update](service-start-update.md).

## Windows build and launch

Use Windows x64, .NET SDK 10.0.402 (or accepted later 10.0.4xx patch) and the official installed MSFS 2024 SDK for live telemetry. The build detects `C:\MSFS 2024 SDK`; for another location add `-p:MsfsSdkPath="D:\MSFS 2024 SDK"` to the build. The offline demo does not need the simulator/SDK. Visual Studio with .NET desktop development and .NET 10 support can open ADM.sln with ADM.Desktop as startup project.

```powershell
Set-Location 'C:\Projects\ADM'
dotnet restore ADM.sln
dotnet build ADM.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet test ADM.sln -c Release --no-build --no-restore
dotnet run --project .\src\ADM.Desktop\ADM.Desktop.csproj -c Release --no-build
```

A framework-dependent executable requires the x64 .NET 10 Windows Desktop runtime on a machine without the SDK. Vendor SDK binary redistribution and installer testing remain unresolved; local development builds are not a public release package.

## Actions for the next test

1. Use a suitable test flight at the plan's origin stand, stationary, engines off and parking brake on. Keep competing Any2GSX service control disabled as already confirmed, retaining any required aircraft/fuel/payload integration. Let GSX initialize with its remote server enabled.
2. If the previous operation is restored, it will remain paused. Review it before reconciling. Old Unknown water work from the earlier build is deliberately not guessed complete. For a fresh acceptance flight, archive the old operation and start a new one only after preparing a new test handling cycle; do not delete the database to force retries.
3. In Settings, save the intended airline/operation/type profile. In Flight, explicitly import or enter the matching flight, confirm aircraft/checklist and select the operation. Saved profile changes apply to a new operation. Connect; once checks and telemetry are ready, ADM prepares the operation and opens Chat. Reply to **Are we ready to start services?** before any service requests. **Prepare operation** is an explicit retry option. Mark the gate ready only after checking it.
4. Reproduce the earlier sequence: let water run and skip lavatory while it is still planned. Water returning idle should advance automatically without a routine completion prompt. Confirm the status distinguishes **cycle ended** from **verified complete**, then confirm refuelling and catering receive separate back-to-back requests when both are eligible in the same phase. Routine waiting should appear in Services without unsolicited Chat messages. A service already requested needs supported cancellation or manual GSX handling, not Skip.
5. Let fuel and other configured work finish. If GSX emits completed/6, completion is automatic. If fuel returns idle without that evidence, independently verify the required fuel is aboard and use **Confirm fuel complete**. Required work and real holds still block boarding; there is no blanket confirmation bypass.
6. At the boarding question, test Hold boarding, then Resume boarding and approve the fresh question. ADM must request boarding once. Wait for GSX completion. If boarding returns idle without completion evidence, check the actual boarding outcome before **Confirm boarding complete**. Never confirm merely to move the workflow along.
7. At the separate closure question, approve close-up. Carry out and confirm each manual item in turn: doors, jetway/stairs, then ground equipment. The current manifest has no verified automatic release actions. Items remain pending until your confirmation. Check that an old **Confirm doors closed** reply cannot satisfy a later equipment item.
8. Confirm the final handover appears only after all three checks, records pilot-attested release and leaves pushback entirely to you. ADM must not open or operate pushback. Normal telemetry monitoring continues after handover.
9. Separately test interruption/restart before completion. Requests must not repeat; boarding/closure clearance and partial release checks must be invalidated. Explicit reconnect/reconciliation and fresh approvals are required. Resolve a genuinely unknown GSX outcome manually rather than retrying it blindly.

Test first-departure and turnaround applicability. Fuel/catering is an explicitly pilot-requested pair gated by each service's live availability; GSX decides actual execution. Other overlap requires a validated capability group; none is shipped. Different saved phases separate fuel/catering as well.

## Evidence and troubleshooting

Normal state is under `%LOCALAPPDATA%\ADM`: `dispatch.db` with command/checkpoint history, `profiles` with validated JSON/backups, optional `aircraft-capabilities.json`, preferences and `logs`. Keep the relevant log and database for the acceptance review. Do not modify a database being used by ADM. The account ID and raw SimBrief OFP are not saved.

A handling pause and a socket disconnect are different. The log now records pause reasons. An optional routine cycle can end without verified completion; a profile-required service still needs reliable evidence. Lost delivery, engine restart, aircraft/context change or stale telemetry remains a genuine stop condition. No routine confirmation is added to hide these conditions.

No questions need answering before this test. Report which stage was reached and any displayed pause reason if the sequence cannot finish.
