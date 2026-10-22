# GSX Remote API v2 verification record

Reviewed on 20 October 2026: installed `C:\Program Files (x86)\Addon Manager\couatl\GSX\GSX_manual_MSFS.pdf`, 291 pages, **Couatl Remote API v2 — Developer Guide & SDK Reference**, pp.265–291. SHA-256: `EE4AE760168E7045D3598905D3E1B4A3CCB56337FDB50586E7456C3DC4FEAFC1`. The document identifies the shipping Remote API as v2 and its distinct wire protocol as **1**. No GSX package version is inferred from that branding. The manual itself is not redistributed.

## Implemented contract

| Surface | Document reference | ADM behavior |
|---|---|---|
| Local transport | §4, p.269 | `ws://127.0.0.1:<port>/`; re-read `%APPDATA%\Virtuali\CouatlAddons.ini`, `[gsx] remote_server_port` on every reconnect; documented default 8744 |
| Handshake | §5–6, pp.269–270 | Require envelope `v:1`, `protocol:1`, Couatl/MSFS, no authentication requirement; feature-detect `state` and `services` |
| Subscription | §7, p.270 | Subscribe to `state` after every connection; await full snapshot |
| State | §8–8.1, pp.271–272 | Snapshot `services` array and whole-array `/services` patches; remove stale services; ignore unknown IDs/keys |
| Requests | §10, pp.279–280 | `command`, correlation `id`, `verb:"service.trigger"`, `args:{service:"<canonical id>"}`; no client `v` or `ts` |
| Results | §11, pp.280–281 | Match correlation ID; accepted/rejected/unknown receipt. Acceptance never generates completion |
| Engine/reconnect | §4, §9 | Invalidate observations/capabilities on engine/socket loss; resubscribe on reconnect; `state.get` on engine restart without socket replacement |
| Canonical IDs | Appendix B, p.291 | Deboarding, Cleaning, Water, Lavatory, **Refueling**, Catering, Boarding, intersected with actual advertised service array |

State mapping (Appendix A, p.291) requires semantic and numeric agreement:

| GSX semantic/raw | Domain observation |
|---|---|
| available / 1 | Planned; request also requires `canTrigger:true` and no `waiting` block |
| requested / 4 | Requested |
| performing / 5 | Active |
| completing / 7 | Active; not complete |
| completed / 6 | Completed with GSX-source evidence |
| unavailable / 2, bypassed / 3, unknown or inconsistent values | Unknown; never fabricated cancellation, authorized skip or completion |

| Capability | M3 real adapter | Offline demonstration |
|---|---|---|
| Known service request | Implemented, gated on validated hello, snapshot and live availability | Hypothetical capabilities, Simulation evidence |
| Service observation | Implemented from documented semantic/raw state | Explicit Simulation injection |
| Cancellation | **Disabled**; no service cancellation verb documented | Cancellation exercise remains explicitly simulated |
| `service.bypass` | **Disabled**; reserved, not available; not cancellation | Not sent |
| Door close/equipment release | **Disabled**; no aircraft-specific, direction-specific action and evidence verified | Simulated release or manual-fallback domain tests |
| Concurrent services | No real compatible group validated; scheduler defaults to serial dispatch | Four ground services share a validated synthetic group |
| Pushback / `Departure` | No command contract or mapping; ignored | Pilot-owned handover only |

## Runtime proof and limitations

`GsxRemoteGateway` owns a cancellation-aware connection loop, bounded incoming message size, serialized outbound requests, process-local intent deduplication and receipt correlation. Timeouts/disconnects produce Unknown, never automatic resend. An uncertain service is blocked from a new intent for that adapter's lifetime. Durable reconciliation and command-worker integration remain M4. Initial provider snapshots must be reconciled to the active sector before automation; existing completion from a prior operation must not be blindly imported.

`ADM.IntegrationProof` is the opt-in manual test harness. The normal desktop still uses its labelled offline workflow and fake DI adapters. The harness observes GSX and real SimConnect, optionally requests **one explicitly named preboarding service**, and captures timestamped JSONL request/result/observation evidence. It does not connect the scheduler to real services or permit boarding, cancellation, closure or pushback. A request requires fresh on-ground, standstill, parking-brake and engines-off telemetry plus live GSX availability.

An initial offline probe correctly reported missing live evidence. A later private live probe demonstrated SimConnect telemetry, acceptance of an approved Water request, and independent requested/active observations. No completion is claimed. Raw captures remain local; this verifies one setup rather than universal aircraft support. See the [M3 report](m3-report.md).

## SimConnect

The installed official MSFS 2024 SDK `C:\MSFS 2024 SDK` reports version **1.5.7**. The adapter compiles against its managed wrapper when available; binaries are copied only into ignored development output, not committed. The absent-SDK build reports an unavailable diagnostic. Distribution/licence review remains required before packaging vendor binaries.

Reference: [official managed-client guide](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/programming-simconnect-clients-using-managed-code/), [data definitions](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/api-reference/events-and-data/simconnect_addtodatadefinition/), [periodic object data](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/api-reference/events-and-data/simconnect_requestdataonsimobject/), [SimVars](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simvars/simulation-variables/).

Creation, subscription, receive dispatch and disposal execute on one dedicated thread using the SDK event handle. Read-only user-aircraft telemetry contains on-ground, ground speed (knots), parking brake, engine count/combustion for engines 1–4, latitude/longitude and title. Unknown/invalid values stay nullable. Airport, stand and validated ICAO callsign remain unknown; no gate readiness is inferred. Reconnect clears stale data. An engine count outside 1–4 yields unknown engine status. No aircraft writes, arbitrary events or undocumented LVars are exposed.


## M5 adapter use

The desktop now uses the same verified service.trigger contract for Boarding after an eligible pilot approval. Appendix B's Boarding ID is supported; Departure remains excluded. No new cancellation, door or equipment verb was inferred. The current release manifest is empty, so M5 uses explicit manual obligations and pilot-attested completion.

The live snapshot retains a completed/6 observation through a later available/1 state until a new cycle or connection/context invalidation, preserving brief completion evidence between worker polls. Raw Observations and protocol mapping are unchanged. Available/1 by itself still never maps to Completed. The application may resolve an uninterrupted optional routine Active → Available cycle as CycleEnded under the pilot-requested policy; it never labels this verified completion, and cannot apply it to fuel, boarding or required work. See [M5 report](m5-report.md). The full live exit gate remains pending.

## Read-only handler plan and GSX input state (21 October UX update)

Verified against the installed manual: §8.13 pp275–276 describes hello feature `handlerData`, snapshot `handlerData` and coarse `/handlerData` updates. Documented handler proxies (pp210–211) provide aircraft `icaoTypeDesignator`/`icaoType`, `title`, `handlerName`; airport `icao`; gate `uiName`; and simbrief `callsign`, `originICAO`, `destinationICAO`, `passengers`, `plan_ramp`, `zfw`, `sched_out`, `last_error`. Weight values serialize as kilograms regardless of display `unitsName`. Aircraft and location fallback fields are cleared on connection/context invalidation; changed handler location invalidates retained completion evidence.

The adapter feature-detects this read-only data, validates route/type/date and ignores malformed/absent plans. GSX's proxy contains upstream cached SimBrief plan data, not a verified final loadsheet. Planned fields carry explicit provenance. No menu selection, input submission or plan-fetch command was added. Documented `menuShown` and `prompt` snapshot/coarse updates (§8.2/§8.10) are read to pause requests while the pilot answers GSX.

Existing service.trigger is unchanged. Fuel acceptance is not an operational start: same-phase catering now waits for fuel Requested/Active/Completed, and accepted unobserved requests pause for explicit recovery. A bounded read-only localhost connection probe was refused while GSX was unavailable; real ingestion/state-shape and revised handling acceptance are pending. See [UX report](simple-ux-report.md).

## Later M7 input/data correction

An ordinary menuShown flag indicates only a visible menu (§8.2), not a question. MenuOpen and validated text-input AwaitingInput now remain separate. Both pause new requests quietly; only a prompt with generation/title becomes a real input hold, without dispatcher notifications. No input/menu mutation command was added. A five-second read-only live probe verified handler plan/figures and the actual SDK/GSX aircraft title difference in casing; matching now ignores case. Retained evidence and remaining live-service limits are in [M7 UX follow-up](m7-ux-followup.md).

## GSX-owned physical readiness (22 October 2026)

Per the pilot's instruction, ADM removes duplicate cockpit-state service guards. The installed API guide §10 p279 confirms service.trigger scripts the live menu without bypassing GSX's state machine. canTrigger in §8.1 pp271–272 means the service is available; it does not guarantee physical readiness or a successful start. Existing observed-state, uncertain-delivery and approval safeguards remain. No new protocol functionality is used. See [current policy](gsx-readiness-policy.md).
