# Release gates — 22 October 2026

M8 engineering and local packaging are implemented. Public/community release is **not accepted yet**.

- [x] Simplified Chat / Profiles / Flight / Settings navigation; no Services page, Final arrival or deboarding options.
- [x] Contextual recovery actions, sanitized local support export and privacy notice.
- [x] Self-contained Windows x64 qualification package, including the matching SimConnect client runtime pair; no pilot SDK or .NET install.
- [x] Isolated install/upgrade/uninstall, rollback, manifest integrity and path-safety tests; personal/unowned files preserved.
- [x] Release build, all 341 automated tests, 28 WPF checks, 13 installer checks and formatting pass.
- [x] Version, setup/repair instructions, licence inventory and third-party notices recorded.
- [ ] Qualify a genuinely clean Windows PC with no SDK/.NET installation, including Start shortcut, Apps uninstall entry and repair flow.
- [ ] Complete [real-device matrix](beta-test-matrix.md), including live fuel/catering, boarding/closure, interruptions and consecutive sectors. Retain M3 water evidence and M6/M7 synthetic evidence without treating them as full live proof.
- [ ] Verify exact SimConnect native/managed binary redistribution permission before public bundling.
- [ ] Owner selects original-code licence and confirms supplied MP3 public app-bundling rights. See [review](licensing/release-review.md).
- [ ] Review accessibility with real users and document supported GSX/aircraft/version combinations.
- [ ] Publish only after the above gates pass. No upload has occurred.

Pushback remains pilot-owned. No test or recovery path may manufacture completion, replay uncertain service requests or override live fuel/boarding dependencies. Any2GSX service automation must be disabled while needed aircraft integration remains enabled.

## M8 distribution follow-up

Conventional ADM-Setup.exe supersedes the pilot-facing script installation. It includes the supplied icon and 25 default profiles. Configured GitHub builds check stable releases automatically, with explicit verified installation after handling. The checkout is not linked/uploaded to a GitHub repository yet. The optional protected release workflow creates a signed draft for human review. Current local builds remain unsigned. See [follow-up report](m8-distribution-followup.md) and [GitHub setup](github-distribution.md); native setup/shortcut/registration must be qualified independently of the earlier script-installer evidence.

Beta.3 delegates physical aircraft readiness to GSX; all 363 tests, 33 packaged UI checks, packaged startup and formatting pass. MSFS/GSX were unavailable for the later read-only probe, so the live qualification gate remains pending. [Current policy and installer](gsx-readiness-policy.md).

GitHub target is now DivineStag/ADM. The linked beta and clean source (without SDK/MP3) pass 365 tests and 33 UI checks. Initial upload is blocked by connector HTTP 403 / absent desktop Git authentication; no remote source or release exists yet. See [connection handover](github-connection.md).
