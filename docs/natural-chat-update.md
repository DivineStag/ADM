# Natural conversation update — beta.5

The local chat parser accepts anchored polite request wrappers, service synonyms, greetings, wellbeing questions, busy/day questions, thanks and farewells. Negated, conditional, unsupported and mixed commands remain unapproved. Unknown messages get a varied clarification instead of a fixed technical response. Casual messages leave operational state and the pending question unchanged and do not produce notification sounds. No model, network service or API key is used.

Dispatcher event phrases have additional bounded variants. Recent dispatcher wording is excluded across an eight-message window; when all variants have been used, the least recently used returns first. GSX plan receipt wording distinguishes planned load figures from a final loadsheet and never invents missing data. An unanswered services-start invitation is not repeated merely because its domain revision changes; fresh boarding/closure clearance remains required after a material prerequisite change.

Quick reply buttons use short captions. Selecting one sends a varied, explicit pilot sentence through the typed-message parser. The expansion must produce exactly the original typed intent in the current context; unsupported recovery shortcuts retain their original text.

Manual gate readiness is now confirmed atomically with explicit boarding approval, with User evidence and PilotAttested provenance. Missing/invalidated manual readiness allows a boarding question after all other prerequisites are met. No gate evidence or boarding command is created until approval. An observed Preparing/Held gate, active holds, missing fuel, connection issues or unavailable GSX boarding still block boarding. Real readiness evidence and GSX completion requirements remain intact. Legacy explicit gate confirmation is retained for checkpoint compatibility.

Live and demo chats use blue dispatcher bubbles and green pilot bubbles with separate foreground colours in both themes. Quiet service updates remain centred without message bubbles.

Verification results and the local beta installer are reported with delivery. Clean-PC installation of the preceding beta was confirmed by the owner; these conversation changes still need a live handling check.

## Verification

Release build: zero warnings and errors. All 400 tests passed: Application 48, Conversation 67, Domain 73, EndToEnd 91, Integration contracts 55, Desktop 66. Formatting and patch whitespace checks passed. All 36 WPF checks passed in both the development build and packaged executable, including distinct pilot/dispatcher colours in both themes, brief quick replies, unchanged separate boarding/closure approvals, and zero binding errors. Rendered chat was inspected. No live GSX services were requested by these checks.

Version: 0.8.0-beta.5. The conventional installer was built locally; no public release was published. Its location and hash are indexed in the ignored artifacts/m8-setup.json. Live simulator handling still needs pilot qualification.
