# M6 — operational variability and polish

21 October 2026. M6 extends the existing M5 service-start conversation, fuel/catering pairing, quiet GSX waiting and approval/recovery boundaries. It does not implement M7 multi-sector detection. No live simulator requests were made during this milestone; M4/M5 end-to-end live acceptance remains pending.

## Implemented behavior

A pure SHA-256-seeded engine makes one decision per eligible turnaround after the first accepted preboarding-service request. Defaults are 25% probability and weighted 2–7 minute durations, capped by the configured maximum of 0–10 minutes. Disabled realism, zero probability/cap, a real/pilot hold, unsafe workflow state or a non-turnaround suppresses incidents. The decision is persisted even when no incident is selected; recovery cannot reroll it.

One originating incident is selected from gate-document checks, gate-team delay and passenger processing. Its three linked effects cover gate readiness, boarding clearance and the earliest boarding estimate. These are effects of the same incident, not extra random delays. Actual service completion/cancellation is never simulated by this engine. Ground work can continue while simulation-owned boarding effects remain active.

All fictional messages explicitly say **simulated**. Services shows an estimated earliest boarding time with a reminder that service/gate checks still apply. Essential Chat emits at most one incident notice and a useful resolution; resolution is coalesced into the boarding question when both become ready together. Existing GSX waiting chatter remains quiet. New phrase variants follow the saved dispatcher's personality and retain semantic deduplication.

**Skip the gate delay** atomically marks the root skipped and clears all dependent effects; real and pilot holds remain. A newly observed real/pilot hold supersedes a simulated incident without falsely reporting that the real problem cleared. Natural expiry resolves all effects together. Gate/boarding changes invalidate boarding clearance; independent approved preboarding-service instructions survive simulated gate changes.

## Profiles and configuration

Settings now provides working global realism enable/probability/maximum controls. **Airline realism tuning**, inside a collapsed section of the profile editor, overrides probability and can shorten the maximum. A blank field inherits; the global maximum is a hard cap. Policies for each operation and family/type previews remain available. All edits are for future operations, with options and gate rules captured in the new session's immutable snapshot.

**Profile files** exposes import/export through standard Windows file selection. Import is size-bounded, schema/domain-validated and staged for review. It does not replace the active operation or write a saved profile until Save handling profile is selected. Imported file revision numbers are mapped to the next local revision, preserving other operations and overrides. Export validates before atomic replacement. The current catalogue/editor supports GENERIC, BAW, RYR and UAL; importing another designator reports that limitation rather than silently changing identity.

Optional `%LOCALAPPDATA%\ADM\airport-profiles.json` selects an exact ICAO airport rule, then GENERIC, then safe built-in manual defaults. Preparation is bounded by both the airport rule and the global delay maximum. Simulated gate readiness is opt-in and labelled Simulation; it is never represented as a real gate observation. Missing/corrupt/invalid rules retain manual gate checks with diagnostics. A pilot gate attestation made during a simulated incident is held until that incident clears. An interruption invalidates old gate attestations.

Example airport catalogue (copy/adapt `profiles/airport-default.json`):

```json
{
  "schemaVersion": 1,
  "airports": [
    { "schemaVersion": 1, "icao": "GENERIC", "preparationSeconds": 0, "simulatedReadiness": false },
    { "schemaVersion": 1, "icao": "EGLL", "preparationSeconds": 45, "simulatedReadiness": true }
  ]
}
```

The optional airline profile property is `"realism": { "probabilityPercent": 25, "maximumDelayMinutes": 4 }`. Use the editor rather than manually changing active checkpoint data. Global settings live in appearance.json; airline tuning uses the existing revisioned JSON repository and backups.

## Architecture and recovery

- Domain adds typed realism options, incident/effect/status records and a constrained transactional update event. It projects only simulation-owned holds and source-qualified gate state; service facts remain separate.
- Contracts exposes the pure realism engine, airport catalogue and profile-transfer ports. The obsolete unused disruption scaffold is replaced by the implemented port.
- ADM.Simulation implements the deterministic decision/time progression with no I/O, simulator calls or wall-clock reads.
- Application invokes it inside the serialized live coordinator, including accepted-receipt checkpoints, observation changes and reconciliation. SQLite persists the complete graph, timing, decision, persona and existing command journal atomically with operational state. Recovery preserves original expiry rather than extending or recreating the incident. This uses the existing checkpoint/history schema as the recovery source; separate relational incident/effect/gate-history projections are not introduced in M6.
- Infrastructure implements validated airport lookup and bounded/atomic profile transfer. Existing JSON revision conflicts/backups still apply.
- Desktop adds viewmodels, collapsed controls, file-dialog abstraction and notification integration. New Settings text and navigation labels use neutral .resx resources; dispatcher event text is in language-qualified JSON catalogues. English is shipped; additional translations are not claimed, and legacy diagnostic/safety fallbacks remain English.

Live restore now accepts only M6-owned simulated gate state alongside real service evidence. It still rejects simulated service observations as live completion. Disconnect snapshots pending commands before invalidation so an unsent/uncertain boarding request cannot be mistaken for a new clean plan. Releases and boarding/closure approvals retain their existing interruption guards.

## Files created or changed

New files: Domain `OperationalRealism.cs`; Simulation `SeededDisruptionEngine.cs`; Infrastructure `JsonOperationalProfiles.cs`; Desktop `Services/ProfileFileDialogs.cs`, `Resources/UiStrings.resx`, `Resources/UiText.cs`; EndToEnd tests `M6RealismTests.cs`, `M6ProfileTests.cs`, `M6LiveCoordinatorTests.cs`; Desktop tests `M6ProfileEditorTests.cs`; this report and acceptance evidence.

Updated: Domain sessions/profiles/operations/reducer; Contracts operational ports; Application coordinator/profile codec/checkpoint validation/live setup; Desktop App/DI, main and live/profile viewmodels, preferences, main navigation, Settings/Services views and UI verification; Desktop/EndToEnd project references; airport/persona JSON; the existing service-start test for semantic phrase matching; README, architecture/state/compatibility/release notes and milestone status.

## Verification

Release build passed with 0 warnings and 0 errors. All 263 xUnit tests passed (Domain 63, Application 32, Conversation 39, Desktop 30, Integration contracts 36, EndToEnd 63). All 40 WPF UI checks passed, the startup/render/clean-exit smoke test passed, and formatting verification passed. No live connection or GSX request was made.

The deterministic 50-session run completed every full start → services → boarding → closure → manual checks → handover workflow. It selected 13 incidents (5 document, 5 gate-team, 3 passenger) and left 37 sessions incident-free. Starting messages used six variants. The suite asserts duration bounds, one parent/three effects, no premature boarding, no duplicate command IDs/intents, no repeated polling notices, stable persona and no false simulated/verified provenance.

Synthetic service-source enums in contract tests exercise live guards; they are not actual simulator proof. The 50-session evidence is retained at `docs/evidence/m6-fifty-session-evidence.json`, with runtime test output under artifacts.

## Windows setup and next steps

Windows x64, .NET SDK 10.0.402 (or accepted later 10.0.4xx patch), and the installed official MSFS 2024 SDK for live telemetry. The build detects `C:\MSFS 2024 SDK`; use `-p:MsfsSdkPath="D:\MSFS 2024 SDK"` for another installation. Visual Studio with .NET desktop development and .NET 10 support may open ADM.sln with ADM.Desktop as startup project.

```powershell
Set-Location 'C:\Projects\ADM'
dotnet restore ADM.sln
dotnet build ADM.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet test ADM.sln -c Release --no-build --no-restore
dotnet run --project .\src\ADM.Desktop\ADM.Desktop.csproj -c Release --no-build
```

The usual executable is `src\ADM.Desktop\bin\Release\net10.0-windows\ADM.Desktop.exe`. An SDK-free runtime machine needs the x64 .NET 10 Windows Desktop runtime; vendor SDK redistribution remains subject to the existing licensing review. No AI service, account key, backend or new network dependency was added.

For a later live check, prepare a **new Turnaround** with the usual Any2GSX setup and explicit service-start approval. Temporarily use a 100% chance and short maximum if deliberately testing a simulated incident; verify that services continue, boarding waits, Skip clears only simulation and Chat stays quiet during routine GSX waits. Restore the preferred probability for future operations. Test profile export/import and an optional airport rule, keeping simulated readiness labels visible. Continue the remaining M5 boarding/closure live acceptance separately.

M7 is the next development milestone, only on instruction. Broader real-aircraft qualification, public release licensing/installer work and actual translations remain later work. Existing saved operations keep their prior snapshots; M6 settings apply when a new operation is created.
