# GitHub connection — 22 October 2026

Repository: https://github.com/DivineStag/ADM. The connected account is DivineStag and has repository write/admin permission. The repository was public and empty when inspected. Origin and the application/build default update repository are now DivineStag/ADM.

Source transfer includes the solution, application/test code, 25 default profiles, generated notification WAV, supplied logo/icon, build scripts and documentation. Excluded: SDK/vendor client binaries, build outputs, installers, personal operational databases/logs, account credentials and the optional incoming MP3 (public source uses the original WAV fallback). Local playback still prefers that MP3 when present; public checkouts use ADM's original generated tone. No third-party SDK is committed.

The Windows foundation workflow builds and tests on hosted Windows without needing the SDK. Live release packaging remains an explicit protected workflow on the developer release runner with the official SDK. The owner selected unsigned distribution; no signing configuration is required. It creates a draft; no installer is automatically published by pushing source.

An empty/no-stable-release repository shows “No public release is available yet”, not “up to date”. Once a qualified stable ADM-Setup.exe is published with a GitHub SHA-256 digest, configured builds can check/download it using existing validation. No public release has been created during setup. The owner selected the ADM freeware licence with source reuse rights reserved. Runtime redistribution and final live qualification remain separate gates.

## Verification and upload status

The clean committed source snapshot (without the SDK or supplied MP3) builds with zero warnings/errors and passes all **365 tests**, startup/shutdown and **33 WPF checks**. Local MP3-preference tests and the linked installer UI also pass. Formatting verification passes. Snapshot evidence is indexed by artifacts/github-source-check.json.

The GitHub connection now permits repository writes. The public transfer preserves the existing test file and excludes private live-flight captures and personal filesystem paths. The uploaded tree is compared with the local source snapshot. No public installer release is created by this migration.

Current candidate changes, pinned workflows and release gates are recorded in public-release-readiness.md. The supplied outgoing Pixabay Pop asset is now included with its own notices. Original migration verification above is historical.
