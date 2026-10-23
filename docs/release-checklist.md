# Release checklist — current candidate beta.7

The latest readiness review is [public-release-readiness.md](public-release-readiness.md). Earlier milestone reports retain historical evidence and are superseded where the owner changed the product requirements.

- [x] Chat/Profiles/Flight/Settings, simplified recovery, no Open GSX browser shortcut.
- [x] Local conversation, polite commands, brief varied quick replies and random 1–5 second presentation delays.
- [x] Separate outgoing/incoming sound playback, mute/volume handling and media provenance/notices.
- [x] Boarding approval also confirms manual gate readiness; real holds and fuel/service evidence remain enforced.
- [x] Final door question is conversational: approval ends ADM without equipment commands or fabricated door-clear evidence.
- [x] Source privacy/secret-pattern scan and NuGet direct/transitive security review.
- [x] Owner selected freeware with source reuse rights reserved; root licence is included by setup.
- [x] Automated build/tests and local WPF/audio/delay acceptance; evidence recorded in current readiness report.
- [x] Owner reports successful fresh-machine installation of the preceding setup.
- [ ] Confirm exact SimConnect binary redistribution permission.
- [ ] Configure signed Windows release runner/environment; current local package is unsigned.
- [ ] Complete this candidate's live aircraft/recovery/consecutive-sector matrix and clean-machine Start/Apps/repair checks.
- [x] Remote Windows CI is configured with tests, dependency review and WPF checks. Require a green run on the exact candidate before publication; see https://github.com/DivineStag/ADM/actions .
- [ ] Review draft assets, licence notices and checksum, then explicitly publish a stable release.

Source publication is separate from installer release. No public installer has been released. Users do not need the SDK or a paid AI service; MSFS 2024 and GSX remain separate installations. The pilot/GSX own doors, equipment and pushback.
