# Incoming notification

`incoming.wav` is an original, procedurally generated two-note notification. It contains no sampled or third-party audio. It is a 0.44-second, mono, 44.1 kHz, 16-bit PCM WAV with short attack/decay envelopes.

Regenerate it from the repository root with `scripts/New-NotificationSound.ps1`. The asset is provided for redistribution with ADM without a separate audio licence or attribution requirement. The project's source-distribution licence remains a separate release decision.

Playback uses WPF's local MediaPlayer. The notification player receives normalized volume only after mute, do-not-disturb and volume policies are checked. It reports missing files and playback failures to the Settings page. Unit tests verify those policies; actual audibility depends on the user's Windows audio device.
