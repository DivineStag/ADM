# ADM privacy notice

ADM runs locally. It has no ADM account, hosted backend, advertising, paid AI or automatic diagnostic upload. It reads MSFS telemetry through SimConnect and communicates with GSX's local Remote API. GSX supplies its cached flight plan; any upstream SimBrief account or network traffic belongs to GSX, not a separate ADM import service. Opening GSX from a recovery button opens its localhost page in your browser.

Under `%LOCALAPPDATA%\ADM`, ADM stores JSON profiles/preferences/aircraft setup, SQLite flight and handling audit (including callsign, route, dispatcher, approvals and Chat), and local diagnostic logs. A clean visible session on launch archives previous handling; it does not delete that audit. Logs may contain operational identifiers, error details and local paths. Nothing is sent automatically. Closing ADM and removing its local data folder removes those records; back up wanted profiles first. Uninstall deliberately keeps them.

Save support report asks where to save a JSON file. The allowlist contains application version, timestamp, connection states, stage, service enum states, menu/prompt flags and suggested recovery action. It excludes Chat, flight-plan identity, credentials, raw logs, exceptions and personal paths. Review any file before choosing to share it yourself. ADM does not send it for you.

## Optional configured GitHub updates

A release build linked to the project's public repository checks GitHub's latest stable release on startup and when requested in Settings. It sends normal HTTP connection details and the ADM updater identifier, without flight, chat, profile or local path content. Installation is explicit and uses a checked installer download; updates do not run during handling. An unconfigured local beta makes no update requests. Downloaded installers are kept under LocalAppData/ADM/updates; personal-data retention remains unchanged.
