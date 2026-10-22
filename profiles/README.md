# Profile fixtures and dispatcher catalogue

Airline/airport JSON files are synthetic preferences and contract examples, not airline operating procedures. `SampleSessionFactory` supplies the generic baseline; the airline fixture is checked against its typed contract. The M2 desktop uses an explicit in-memory airline simulation snapshot rather than loading user airline files.

The development airline allowlist is limited to the three examples in the specification. Unknown codes fall back to GENERIC. M2 adds explicit validated alias support and strict in-memory schema validation. `airline-profile.schema.json` describes the JSON shape; `ProfileJsonCodec` also validates dependency cycles, generic coverage, canonical override identities and required-service constraints. Atomic airline-profile storage, backups, editing/import/export and a complete licensed designator catalogue remain later work.

`dispatchers-default.json` is the validated runtime M2 catalogue, embedded into ADM.Conversation. It has three stable personas and multiple weighted variants for each approved essential semantic key. Selection is deterministic for a session ID and the assignment is retained in serialized checkpoints. Templates cannot replace operational receipt/failure clarifications or alter command permissions. No remote AI is used.

Automatic boarding always requires separate pilot approval. Policy mode alone is never authorization. Airport readiness profiles and generated delays remain future work; typed gate checks and holds can already be exercised using explicit evidence.

## Supplied defaults

The 25 JSON files in bundled/ were supplied from the owner's ADM profile folder on 22 October 2026. Only profile JSON was copied; locks, preferences, chat and flight history were excluded. Application startup validates and discovers them from Defaults/Profiles in the installation. A personal profile or valid rollback revision takes precedence; a bundled default is used when neither is available. First use does not create or overwrite personal files. Saving makes a revisioned user override. These are configurable operational preferences, not a claim of aircraft-system compatibility.
