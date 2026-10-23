# Conversational sign-off — beta.6

The final door question is roleplay and session completion only, following the owner's updated product requirement. Once boarding is reliably complete and other service requirements and holds are resolved, ADM asks whether the pilot is happy to close the doors. Explicit approval ends the ADM handling session immediately and sends a safe-flight sign-off. Declining keeps the session open.

ADM sends no door-closing, jetway/stairs, ground-equipment, chock or pushback commands. No manual equipment confirmations are required and no release states are fabricated as closed/clear. Physical actions remain pilot/GSX owned. Legacy equipment intents are rejected by the final dispatch policy, including intents restored from old checkpoints. The existing serialisation types remain to read historical data.

Boarding approval, fuel dependencies, reliable boarding completion and real holds remain in force. Duty monitoring and next-sector preparation continue after the conversational sign-off. No equipment-clear promise is made by the new handover message. Completed chat offers no further operational quick replies.

This policy supersedes earlier specification and milestone descriptions requiring physical closure/release verification. No GSX commands were invented for this change.

## Verification

Release build passed with zero warnings/errors. All 401 automated tests passed (Application 48, Conversation 67, Domain 74, EndToEnd 91, Integration contracts 55, Desktop 66). All 36 WPF checks passed. Coverage includes immediate sign-off without physical releases or fabricated equipment completion, separate boarding/sign-off approvals, restart before sign-off, completed-session disconnects, legacy release-intent rejection and consecutive sectors. Formatting and patch whitespace checks passed. Live simulator handling remains a pilot qualification step.

Local beta.6 package details are indexed in ignored artifacts/m8-setup.json; no public release was published.
