# ADM beta setup

Pilots need Windows x64, MSFS 2024 and GSX. **No MSFS SDK, .NET installation or development tools are needed.** The full application and runtime are in the installer.

1. Run **ADM-Setup.exe**. Follow the Windows setup wizard. Installation is for your account and does not request administrator rights. A Start menu entry and Windows Apps uninstall entry are created; a desktop shortcut is optional.
2. Load a flight at a stand in MSFS. Let GSX load its plan and enable its local Remote API. Open Aircraft Dispatch Manager from Start.
3. Review each aircraft's integration setup once. Keep needed Any2GSX fuel/payload integration and disable its competing service automation.
4. Approve services in Chat. Boarding and the final sign-off each need a separate approval. The door question finishes ADM only; doors, equipment and pushback remain with you and GSX. Service progress appears quietly in Chat.

Tabs: Chat, Profiles, Flight, Settings. The 25 supplied default profiles are included; your saved edits take precedence and remain through updates. The imported validated ICAO callsign selects the airline and aircraft in Profiles. A registration or two-letter commercial flight number is not treated as an ICAO airline code.

Use the specific action offered in Chat when a problem occurs. Repair installation opens the cached setup wizard; if unavailable, select the ADM-Setup.exe you downloaded. You do not need the SDK. Uninstall through Windows Apps; profiles, setup, preferences and archived history in LocalAppData/ADM are retained.

Configured GitHub builds check for a stable update automatically. Settings → Help & support shows update status and Install update. Installation is available after handling is finished. A local build with no repository configured makes no update request. The familiar full setup wizard also handles updates; it is not a command script or a separate web downloader.

The current unsigned local beta has not been cleared for public distribution. Windows can identify it as an unrecognised publisher. GitHub hosting does not remove that warning; signing and release qualification remain open. No Microsoft/Asobo or FSDT endorsement is claimed. See [distribution plan](github-distribution.md).

GSX owns the aircraft's brake/engine and physical service-readiness checks. ADM no longer repeats those cockpit checks; it still observes service availability, approvals, sequencing and current flight/connection identity. See [readiness policy](gsx-readiness-policy.md).
