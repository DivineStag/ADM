# Service and workflow states

M0's `ServiceTransitions.Apply` remains the individual-edge guard. M2 adds OperationalReducer around it, returning a new immutable OperationalState, journaled command intent and semantic notices. Identical observations and event IDs are idempotent.

| From | Permitted next states |
|---|---|
| Planned | Eligible, Skipped, NotRequired |
| Eligible | RequestPending, Skipped |
| RequestPending | Requested, Failed, Unknown |
| Requested | Active, CancelPending, Failed, Unknown |
| Active | Completed, CancelPending, Failed, Unknown |
| CancelPending | Cancelled, Completed, Failed, Unknown |

Completed and Cancelled transitions require referenced evidence from GSX, SimConnect, explicit pilot attestation or an explicitly simulated observation. A System receipt is insufficient. Simulation evidence remains labelled and cannot be promoted to external verification. M2 additionally permits explicit pilot resume from Skipped, Cancelled or Failed through its guarded instruction path. Completed remains terminal; Unknown requires reconciliation.

`ApprovalValidity` checks session ID, kind, prerequisite revision and invalidation. M2 increments revisions on material changes/disconnect and checks capabilities, gate evidence, holds and current service state before requesting approval. Command dispatch is also revalidated against the latest journaled intent. A late receipt for an older service cycle cannot change its replacement cycle.

Workflow and duty enums remain separate. The M2 workflow is Planning → ArrivalHandling/PreBoardServices → AwaitingBoardingApproval → Boarding → AwaitingClosureApproval → ClosingAndReleasing → Completed, with NeedsAttention on uncertain/failed work. A final arrival completes its configured arrival handling without a boarding or pushback request. No duty-tracker transition is implied by ground completion.

Boarding requires resolved preboarding work, gate evidence, no active holds and fresh approval. A required fuel skip is never fuel completion; it needs its own manual attestation. Cancellation stays CancelPending after an accepted receipt and only becomes Cancelled on observed evidence. Unknown states cannot be resumed or replayed blindly. Unsupported cancellations produce manual guidance without claiming success.

Closure requires completed boarding and a separate approval. Supported equipment release is observed in safe order; unsupported actions remain pending until explicitly manually attested. Final wording distinguishes verified, attested and simulated release. Material holds, gate changes, failures and disconnects invalidate approvals. Repeated gate polls with only a newer evidence timestamp do not.

OperationalCommandPolicy checks queued intent again before dispatch. The demonstration injects explicitly simulated receipts/observations. M4's separate opt-in live coordinator adds real preboarding dispatch, SQLite journaling, restart recovery and explicit GSX reconciliation. Duty lifecycle detection remains later work.

## Configurable ordering and overlap

M3 adds immutable service phases to the applied plan. Default: deboarding, then cleaning if required; water/lavatory/refuelling/catering share the next phase; boarding is last. Earlier phases are barriers. Existing hard dependencies remain, and validation rejects cycles or boarding at/before refuelling. Both approval gates are unchanged.

Equal phases express desired overlap, not proof of aircraft support. A validated compatibility group must contain the candidate and every in-flight service. Without one, requests remain serial. The demo supplies a synthetic four-service group; the real adapter supplies none. Independent work can continue during another service's cancellation, but required cancelled work still blocks boarding. CancelPending boarding stays in the boarding stage and cannot prompt for a new approval or closure.

OperationalCommandPolicy rechecks resolved dependencies before dispatch. Connection loss, uncertain receipts and changed prerequisites still invalidate progress. Editing/saving an order does not mutate an active session.

## M4 live evidence and recovery

Triggerable service availability is explicit state; losing boarding availability invalidates its pending question. A live request requires both domain eligibility and the provider's current triggerability. Known idle-unavailable/bypassed work stays planned if no request was sent. Neither state proves completion. Waiting observations add real holds.

ReconcileService is an explicit GSX-evidence transition used while scheduling is frozen. It can adopt reviewed current service activity, invalidate old completion contradicted by a reset, or preserve uncertain delivery as Unknown. Ordinary completion does not make a service automatically replayable. The durable command journal and current snapshot must both be considered. A definitive rejection can be replanned only after explicit reconciliation.

Restore pauses dispatch and clears gate readiness; explicit connect and reconcile are required. Stale telemetry, changed aircraft/context or connection generation pauses the operation. M4 reaches AwaitingBoardingApproval but does not dispatch boarding or release commands; M5 live handling remains unimplemented. No pushback path exists.


## M5 cycle outcome and handover

CycleEnded is a distinct terminal workflow outcome for optional routine work observed Active → Available in the same uninterrupted GSX generation. It retains GSX cycle evidence and Unknown completion provenance, and is not verified completion or cancellation. It resolves dependencies only for nonrequired services other than Refuelling/Boarding. Mandatory work cannot be bypassed with this outcome. A fuel/boarding cycle returning idle without completed evidence instead remains Unknown until a scoped explicit completion attestation or reconciliation resolves it.

The gateway retains explicit completed/6 observations through idle snapshots, clearing them at new activity, removal, engine loss or context change. That protects the worker from missing brief terminal events.

M5's live worker now dispatches approved Boarding. Pending observations and last-moment availability/holds are rechecked. Invalidated unsent boarding intent returns to Planned for a fresh question. Closure requires completed boarding and resolved preceding work, then a separate approval. Current automatic release capabilities are empty: manual Doors, JetwayOrStairs and GroundEquipment confirmations are scoped to the current obligation and persisted with pilot provenance. Disconnect/restart clears release checks and approval. Completed stops outbound handling and hands pushback entirely to the pilot while monitoring continues. Real M4/M5 acceptance is still pending.

## Service-start approval and paired request policy

New live sessions require a User-evidenced ServicesStart approval before scheduling or dispatch. A first accepted preboarding request produces the single Services starting message. Not yet leaves the question pending without requests; start approval is separate from boarding and closure. Restored unapproved sessions cannot dispatch after reconciliation alone.

RequestFuelAndCateringTogether authorizes only the fuel/catering request pair in the same configured phase, subject to each live GSX availability and required policy/dependency checks. Commands remain separate, journaled and serialized on the wire. GSX controls actual simultaneous execution. Uncertain fuel delivery blocks the paired catering send. Other overlap is unchanged.

Routine live waiting changes retain real holds but suppress automatic Chat notices through ChangeHold.Notify=false. Explicit delay questions still return the real cause. See [current behavior](service-start-update.md).

## M6 linked simulated holds

After the first accepted ground-service request, a persisted deterministic decision may create one originating gate-document, gate-team or passenger-processing incident. Three owned effects represent gate readiness, boarding clearance and earliest boarding estimate. Two simulation-owned boarding-scope holds block clearance without blocking ground work. Source-qualified gate evidence retains a valid pilot attestation until the incident clears; an interruption invalidates it.

Expiry resolves all effects; Skip the gate delay marks the root skipped and removes its effects atomically. A real/pilot hold supersedes simulation and keeps its own reason and blocker. No simulated event completes a service or authorizes boarding/closure. Routine polling produces no new notices; resolution merges into an immediately available boarding question. Pending preboarding Ask permissions survive simulated gate changes; other invalidations replan unsent Ask intents for fresh permission. Recovery preserves the original incident and deadline and still requires normal reconciliation. See [M6 report](m6-report.md).

## M7 independent duty tracker

GroundHandling → ReleasedForFlight is persisted with completed handling; it never commands flight movement. Two qualifying airborne observations separated by at least five seconds enter EnrouteObserved, archive live handling and clear chat. A parked aircraft after observed flight yields an ArrivalCandidate only. Airport/stand are taken from telemetry or current GSX context, never inferred as a ready gate from coordinates. Changed aircraft, telemetry gaps and implausible position jumps require location review. Startup parked without observed departure does not prove arrival. A pilot-confirmed next plan creates a new sector, session, dispatcher and plan with empty approvals.

Manual Clear chat persists a presentation boundary and preserves the event/command audit. Closing after Completed leaves only archived handling and a waiting duty; unfinished operations still recover paused. A service restart is explicit replanning, not completion or successful cancellation. Unknown work needs current availability and reconciliation. Boarding restart invalidates its previous approval and requires a fresh question. Deboarding is removed from new operations and controls; untouched legacy steps are skipped by a recorded migration, while existing in-flight facts remain historical evidence. See [M7 report](m7-report.md).

## Current startup, observed fuel start and GSX questions

Automatic connection and plan preparation do not authorize services. Fresh safe telemetry, an acknowledged aircraft configuration and a matching GSX plan lead to the ServicesStart question. Its existing explicit pilot decision still gates scheduling. Saved Ask policies, boarding and closure approvals remain unchanged.

For same-phase fuel/catering, accepted fuel delivery alone cannot release catering: GSX must report fuel Requested, Active or Completed. An accepted request still unobserved after 20 seconds becomes Unknown and suspends handling. GSX menu/prompt state adds one real all-operations hold, blocks new requests and excludes input waiting from that timeout. It is cleared by observed GSX input closure, not by a simulated-delay skip. Reconciliation plus a fresh triggerable state and explicit restart are required; reconnect never silently resends. See [UX report](simple-ux-report.md).

## Current M7 launch and input semantics

The pilot requested a clean visible session on each normal Desktop launch. FreshLaunch archives unfinished handling and ends the prior duty without changing service facts, cancelling GSX work or deleting command reservations. Fresh approvals are required for new handling; current provider state is still reconciled. This supersedes normal Desktop reopening into paused prior Chat; backend recovery and continuous multi-sector operation remain supported.

MenuOpen is separate from a validated text-input AwaitingInput. Both stop new sends silently; neither generates repeated dispatcher question messages. Observed-service timeline entries have IsSystem provenance, no sender and no sound, and do not enter recent dispatcher conversation context. See [M7 UX follow-up](m7-ux-followup.md).

## M8 recovery presentation

Contextual recovery changes the available UI action, not service evidence. Fresh triggerability is required before offering an explicit uncertain-service restart. Reconnect restores for review and review/resume reconciles current provider states; neither silently replays a request. A GSX prompt/menu offers its local UI. Missing runtime files offer installation repair without any SDK setup requirement. Healthy handling shows no recovery card. Final arrival is removed from public choices while legacy values remain readable. [M8 evidence](m8-report.md).
