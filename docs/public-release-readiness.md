# Public release readiness — 23 October 2026

Current candidate: **0.8.0-beta.8**. The source is prepared for public review; this is not clearance to publish a stable installer.

## Completed engineering review

- Skip-to commands follow the saved profile order. Planned optional services are skipped; active optional work needs observed cancellation or completion before the target proceeds. Required work, fuel and boarding approval remain enforced. At most one contextual shortcut is added to quick replies.
- The owner confirmed on 23 October 2026 that the bundled native/managed SimConnect client pair may be distributed with ADM. This closes the project redistribution check on the basis of that confirmation; Microsoft ownership and separate terms remain unchanged.
- The owner selected unsigned distribution. The draft workflow no longer needs a certificate or signing secret, and release-notice selection is independent of signing. Windows may show an unknown-publisher warning.
- Natural local chat, brief quick replies, combined manual gate/boarding approval and conversational final sign-off. No physical door/equipment or pushback commands are dispatched.
- Open GSX/browser launching removed. Recovery offers a read-only state recheck instead.
- Supplied outgoing Pop sound integrated with shared mute/volume handling and a separate player. No send sound on rejected sends or restored history.
- Dispatcher messages wait a random 1–5 seconds in a presentation-only queue. Pilot/system messages remain immediate; service commands, persistence and telemetry never wait for chat. Dispatcher order is preserved without accumulating delays. Cleared sectors discard queued replies; obsolete questions are dropped; quick replies wait for their message to appear.
- Freeware licence selected explicitly by the owner, with original-source reuse rights reserved. Installer and payload include it; third-party notices remain separate.
- Pixabay outgoing asset page/Content License verified and hashes recorded. Incoming Pixabay provenance confirmed by owner; exact incoming asset page was not independently retrieved. Source uses the original incoming WAV fallback, while the supplied incoming MP3 remains optional for local packaging.
- NuGet review reports no known vulnerable direct or transitive packages across 19 projects. This is a dated advisory check, not a guarantee against future vulnerabilities.
- Source scan found no credential-pattern matches. SDK DLLs, personal records, private live captures, logs and installers remain excluded from source. The supplied logo and default profiles remain included.
- Updater review retains HTTPS/repository/host restrictions, exact size/digest checks, bounded downloads and handling-aware installation. No updater token or hosted backend is required.
- CI/release Actions are pinned to verified official commits. The known hosted-UI failure was an unavailable audio device (0xC00D11BA), not a build/test failure. CI now reports only that specific hardware limit as a skipped decode; actual media decoding is qualified locally. Other media errors still fail.
- Release workflow remains explicit, protected and draft-only, with fresh tests, dependency review, UI checks and installer checksum generation. Signing is not required by owner decision. Source pushes do not publish installers.

## Validation

Release build: zero warnings/errors. All 428 automated tests passed locally (Application 48, Conversation 76, Domain 84, EndToEnd 92, Integration contracts 55, Desktop 73). New cases cover polite skip-to commands, custom order, required work, cancellation evidence, repeated requests, initial approval and checkpoint recovery. WPF acceptance includes both sounds decoding without playback, removal of Open GSX, and actual delayed reply delivery. Detailed reports remain in ignored artifacts/beta8-tests-final and artifacts/ui. The current remote status is available at https://github.com/DivineStag/ADM/actions and must be green for the exact candidate before publishing. Final package/native setup and remote CI results are reported with delivery.

## Remaining external release gates

1. **Release runner:** configure the controlled Windows packaging runner, or build the installer locally. The owner selected unsigned distribution; signing is not a release gate. Windows may show an unknown-publisher warning.
2. **Live qualification:** owner confirmed the preceding setup installs on a fresh machine. Final end-to-end handling of this candidate, cancellation/reconnect and consecutive live sectors still need pilot confirmation, along with clean-machine Start/Apps/repair checks and documented supported aircraft/GSX versions. Offline fixtures do not establish universal compatibility.

Only publish a stable ADM-Setup.exe after these points are resolved. Drafts/prereleases remain outside the automatic stable update channel. No public installer is published by this preparation.
