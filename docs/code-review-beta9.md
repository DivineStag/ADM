# Beta.9 maintenance review — 23 October 2026

This review covers GSX connection/receipt handling, live coordination and recovery, simulator-thread ownership, service/approval guards, SQLite and profile persistence, chat delivery, WPF lifecycle and collections, diagnostic logging, and the existing update/installer boundaries. It is an engineering review with regression evidence, not universal aircraft compatibility certification.

## Correctness fixes

- **GSX receive/send deadlock:** an engine event can require `state.get` before the receive loop reaches a service result. Previously the service held the same send semaphore while waiting for that result. Wire writes and serialized service-command tracking now have separate locks. A regression injects the engine event before the result and requires accepted delivery without retry.
- **Explicit restart after uncertain delivery:** the adapter's uncertainty block previously survived every later restart request. The coordinator now invokes a dedicated recovery capability for an explicit restart of an uncertain/failed service. A new GSX snapshot, matching connection generation, and a planned, triggerable, unblocked service are required before the uncertainty block is lifted. The original intent still returns its original unknown receipt; the new request needs a new durable identity/version. This uses the existing documented `state.get` command, not a new service verb. Performing work is never treated as available or complete.
- **Chat cursor:** starting a live session now clears both the delayed-delivery queue and its position. A previous session cannot cause the new session's early messages to be skipped.
- **Shutdown and startup:** the worker captures its own cancellation source/coordinator instead of looking up mutable replacements. Expected pre-start task cancellation is handled during stop, stopped telemetry is cleared, and cross-thread connection/simulator references have explicit visibility. Normal startup respects the persisted automatic-connection preference.
- **Persistence validation:** unsupported or ambiguous database schema versions are rejected before schema creation. Checkpoint validation remains before every write, with a regression proving an invalid save cannot replace the valid stored session.
- **Diagnostics:** an unwritable/locked/full diagnostics folder no longer throws into handling or shutdown. Oversized profiles fall back to a valid backup; oversized/null-entry capability catalogues fail explicitly. Support reports now identify the actual product/beta version instead of only the fixed assembly version.

## Reduced repeated work

- GSX immutable snapshot projections are reused until a provider frame or connection change invalidates them. Repeated UI reads no longer rebuild the service dictionary/evidence objects.
- The optional GSX observation feed retains at most 256 recent events. It is a best-effort telemetry feed; the live coordinator uses current snapshots and the durable command journal. Brief completion evidence remains latched in the snapshot until a new cycle/reset, independently of event-feed overflow.
- SQLite checks/creates its schema once per owned store lifetime. Connection-local full-synchronous durability and busy timeout still apply to every opened connection; transactional send reservations remain intact.
- Chat queue draining compacts pending entries in one pass, and empty drains allocate no list. Delayed-reply timers stop when idle. Existing ordering, immediate pilot/system lines and stale-question filtering are covered by tests and WPF acceptance.
- Unchanged service/sector collections no longer clear and refill on every UI poll, preserving selection/scroll state and avoiding redundant collection notifications.
- Diagnostics retain at most seven daily files where filesystem permissions allow and cap each day's output at 4 MiB. Individual pathological entries are bounded too.

These changes remove measurable repeated allocations/operations by construction and regression checks; no simulator frame-rate or wall-clock speedup claim is made.

## Removed obsolete desktop paths

Removed the unused account/import and cached-flight commands, importer stub/dependency, duplicate gate/reconciliation commands, unused release-status collection and unfiltered operation list. Removed unused production DI registrations; the offline foundation monitor/fake adapters now run only during verification. The removed importer-stub test was replaced by coverage of the actual GSX/manual startup path. Profiles, histories and legacy enum/JSON slots remain readable; no user data or command audit was deleted.

## Validation

Release build: zero warnings/errors. **442 tests pass**: Application 47, Conversation 76, Domain 84, EndToEnd 101, Integration contracts 60, Desktop 74. The added/expanded tests cover the lock interleaving, explicit recovery, original-intent non-replay, snapshot reuse, bounded telemetry, logging failures/retention, database schema and invalid saves, oversized profile/catalogue fallback, unchanged UI collections, product version and a large mixed chat backlog.

All **42 WPF checks** pass, including both local sound decodes, actual delayed replies, profile editing, small-window/input behaviour and zero binding errors. Installer qualification and exact-commit GitHub CI results are recorded with delivery and in ignored artifacts. No live GSX services are requested by these checks.

The weekend's aircraft-specific handling, reconnect/cancellation and consecutive-sector checks remain required. Stable publication is still a separate action; this cleanup does not publish a release.
