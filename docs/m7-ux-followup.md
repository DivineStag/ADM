# M7 follow-up: profiles, quiet Chat and fresh startup

21–22 October 2026. This extends the existing M7 multi-sector foundation; it does not begin M8 or claim completed live service qualification.

## Everyday changes

**Profiles:** Settings → Handling profiles now presents one service setting per row, rather than simultaneous airline/family/type columns. Choose an airline, flight type and scope: all aircraft, one ICAO aircraft type, or an advanced family key. Automatic / Ask / Disabled meanings are explained beside the editor. Aircraft and family scopes support Inherit. Existing custom phases and boarding-after-fuel validation remain; the resulting-plan preview is optional.

Expand **Add an airline**, enter its three-letter ICAO callsign code and display name, optionally copy the current profile, then create a draft. Change services and select **Save profile**. Saving explicitly registers the code as a user-maintained local mapping; it does not claim an authoritative airline catalogue lookup. New and imported airlines are discovered from validated per-user JSON on reopening. Unknown unsaved codes still use GENERIC. For aircraft settings, choose One aircraft type, select a saved type or enter its ICAO code, adjust services/order and save. Draft edits across operations/types are retained within the selected profile; other aircraft and active handling remain independent. Existing required flags/dependencies and untouched saved operations are preserved.

**Quiet service updates:** Chat now renders observed starts and completion/cycle-end status as small centred lines without dispatcher attribution or message bubbles. Command acceptance alone cannot produce “started”. Optional return-to-idle remains “returned idle”, rather than a false completion. Updates bypass the conversation engine and notifications, carry a persisted IsSystem flag and deduplicate by session/service/request-intent/status. Repeated polls, clear-chat and recovery cannot replay old lines. A new explicit request can produce a new start. Dispatcher messages and approvals retain their normal appearance and notification settings.

**Sound:** the supplied `bible_images-text-message-sound-effect-5-386088.mp3` is copied unchanged to `Assets/incoming.mp3` and is the sound used by NotificationPlayer. SHA-256: `D03BBDF4F72384121A72FAFBEB4C19D84E018DF8528E819F608F88F203AB76D1`. Mute, volume and do-not-disturb still apply. System lines never play it. The UI harness opens the file to verify decoding without calling Play.

## Further pilot feedback addressed

**Fresh launch:** normal Desktop initialization archives unfinished handling and ends the preceding duty before opening a new visible workspace. Chat, current flight, approvals and service state are not restored into that workspace. Aircraft setup, profiles and preferences remain saved. The archive preserves original service facts, command reservations and receipts; no physical GSX work is cancelled, falsely completed or blindly replayed. Older checkpoints without a duty receive an archival duty so they can be retired safely. A new flight is taken from current GSX data and needs a fresh start-services approval; busy/uncertain provider states still reconcile before requests. Multi-sector operation continues while ADM remains open. Explicit backend recovery remains covered for crash/reconciliation cases, but normal Desktop reopening uses a clean slate as requested.

**False questions:** `menuShown` means the GSX menu is open; it does not establish that GSX asked a question. Menu state and validated text-input prompts are now separate snapshot fields. A real prompt needs its generation ID and nonempty title; null/empty/malformed placeholders are ignored. Open menus pause new requests silently and show a compact close-menu status. Real text prompts use a quiet hold/status, never repeated dispatcher notifications. Input waits continue to suspend the stalled-request timer. No guessed menu answer or input command was added.

**Flight ingestion:** a five-second read-only probe of the live simulator/GSX verified a concrete bug: MSFS reported `737-800 PAX SSW HD`, while GSX reported `737-800 pax ssw hd`. The case-sensitive comparison rejected the same aircraft. Matching now trims and compares ordinally without letter case, while different aircraft, ICAO type, airport and unsafe telemetry still block preparation. A private read-only probe verified cached plan fields and the nine-component UTC departure tuple. Detailed flight figures and capture identifiers remain local. No service request was sent.

Plan identity no longer prevents late planned-load figures from refreshing. A same-identity update refreshes the displayed figures without replacing the coordinator's immutable applied service plan; a different sector remains staged for confirmation. If GSX explicitly removes its usable plan before handling starts, ADM stops treating the old cached data as current. An ended archived duty no longer suppresses the same flight's fresh GSX plan on reopening.

## Architecture and files

- Domain: `OperationalTypes.cs` adds backward-compatible IsSystem message provenance.
- Contracts/Application: profile-listing and user-registry ports; `AirlineIdentityResolver.cs`; new `FreshLaunch.cs`; `AutomaticPreparation.cs`; `LocalDispatchSession.cs` system timeline; `LiveDispatchCoordinator.cs` quiet input/menu handling.
- Infrastructure: `JsonAirlineProfileRepository.cs` discovers validated custom profiles with backup recovery.
- Desktop: `ProfileEditorViewModel.cs`, `LiveOperationsViewModel.cs`, `DemoModels.cs`, `DemoSessionViewModel.cs`, new `ConversationPresentation.cs`, Chat/LiveChat/Settings views, notification player/asset/project, main sound text and UI harness.
- GSX: `GsxProtocol.cs` validates input prompts; `GsxRemoteGateway.cs` separates MenuOpen/AwaitingInput. No new service command is introduced.
- Diagnostics: IntegrationProof includes the received GSX plan and SDK title in read-only evidence.
- Tests: new SimpleProfileTests and QuietServiceUpdateTests; startup, preparation, protocol, workflow, demo, asset and UI coverage updated. Existing M7 two-sector tests remain part of the regression suite.

The JSON/SQLite storage schema remains compatible. The backend can deserialize earlier messages without IsSystem; their default is a normal conversation message. Local setup does not store credentials, and direct SimBrief HTTP integration remains absent from Desktop.

## Validation and Windows execution

Final verification on 22 October 2026: the normal Release build passed with **0 warnings and 0 errors**. All **332 xUnit tests** passed with no failures or skips (Domain73, Application47, Conversation39, Desktop43, Integration51, EndToEnd79). Results are in `artifacts/m7-ux-final-tests/*.trx`. This includes both continuous and SQLite-restart two-sector M7 regressions, the 50-session M6 regression, custom profile persistence/scope isolation, clean startup preserving request audit, malformed prompt rejection, quiet menu toggles and aircraft-title matching.

All **51 WPF checks** passed with no binding errors, including creating an airline, applying scoped policies, service-order safeguards, unattributed status lines and decoding the supplied MP3 without playing it. Startup/render/clean-exit and formatting verification also passed. The final phone profile screen, airline-creation form and quiet Chat screen were visually inspected in `artifacts/ui`. New quiet status lines are counted separately from the M6 essential-conversation message budget; duplicate/status evidence assertions remain enforced. UI fixtures and automated tests use isolated storage and issue no real service commands. The initial ordinary build encountered DLL locks from the pilot's running session; the application was left open. After the pilot closed it, the normal Release output was rebuilt.

From `C:\Projects\ADM`:

```powershell
dotnet restore ADM.sln
dotnet build ADM.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet test ADM.sln -c Release --no-build --no-restore
.\scripts\Test-DesktopStartup.ps1 -Configuration Release
.\scripts\Test-DesktopUi.ps1 -Configuration Release
dotnet run --project .\src\ADM.Desktop\ADM.Desktop.csproj -c Release --no-build
```

Executable: `C:\Projects\ADM\src\ADM.Desktop\bin\Release\net10.0-windows\ADM.Desktop.exe`. Use Windows x64 and .NET 10 SDK 10.0.402; a runtime-only machine needs .NET 10 Windows Desktop Runtime. The live SDK/GSX server/Any2GSX setup requirements remain in the README. Only one normal ADM instance should run.

The next pilot check is the complete handling flow with the updated profile, quiet start lines and the chosen sound, then close/reopen and confirm a fresh visible session. The read-only probe qualifies actual plan ingestion and the identity mismatch; it does not qualify fuel/catering execution or boarding/release. Those live M4/M5 acceptance checks remain outstanding. No pushback, real cancellation, guessed menu submission or unsupported automatic release was introduced.
