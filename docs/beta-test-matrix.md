# M8 qualification matrix

22 October 2026. A passing fixture is not live aircraft compatibility evidence. Record exact Windows, GSX, MSFS and aircraft versions for each later run.

| Configuration / scenario | Evidence | Status |
| --- | --- | --- |
| Current developer Windows x64: native setup, installed startup, setup rerun, cached repair, uninstall | artifacts/m8-native-installer-results.txt, 12 checks; Start/Apps registration suppressed | Pass locally |
| Packaged phone/tablet UI, dark/light, navigation, profiles, recovery, sound decoding | artifacts/ui/ui-results.txt, 33 checks | Pass locally |
| All six xUnit suites; domain scheduling/approvals/recovery/persistence | artifacts/gsx-owned-readiness-tests, 363 tests | Pass offline |
| Clean Windows without SDK or .NET, Start entry, Apps uninstall, installation repair | Not yet run; local developer machine is insufficient | Pending |
| MSFS/GSX connected: read-only plan and aircraft match | Prior M7 follow-up live probe | Partial live evidence |
| Fenix A319 cockpit mapping (superseded) | Historical beta.2 probe; current beta.3 delegates readiness to GSX and removes this mapping | Historical only |
| Water request / GSX performing observation | Prior M3 capture | Partial live evidence |
| Supported narrowbody, competing Any2GSX automation off: water/lavatory/fuel/catering through boarding and closure | Record exact aircraft/version, requests and state observations | Pending |
| Second aircraft family / widebody, without Any2GSX | Record capability differences/manual actions | Pending |
| Any2GSX integration-only enabled, its service automation disabled | Verify zero competing automatic requests | Pending |
| GSX connection loss / ambiguous delivery / cancellation then explicit restart | Verify no duplicate request or false completion | Pending live |
| Two consecutive sectors, clean reopen, airborne/manual chat clearing | M7 synthetic continuous/restart acceptance retained | Pass offline; live pending |
| GSX versions | Record exact tested versions; no unrestricted compatibility claim | Pending |
| Keyboard, scaling, screen reader and small window usability | Current binding/layout checks; human accessibility review needed | Partial |

The pilot does not install the SDK. Clean-PC qualification must use only the packaged runtime and separately installed MSFS/GSX. The owner confirmed redistribution of the bundled SimConnect client pair on 23 October 2026; the live and clean-PC qualification checks remain separate.
