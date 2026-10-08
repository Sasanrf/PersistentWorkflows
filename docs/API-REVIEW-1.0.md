# Public API review — 1.0.0

Reviewed the four packaged projects against preview.2 for the first stable release. The production C# sources and migrations are unchanged by this release preparation; version metadata, compatibility/audit settings, tests, CI and documentation changed.

| Area | Decision |
|---|---|
| Definitions and builders | Retain typed ordered steps, explicit names and definition/context versions; no renaming before 1.0 |
| Runner | Preserve enqueue/start distinction, manual resume semantics, immutable signals, cancellation and conditional audited retry |
| Results/context | Preserve outcome factories, stable compact-GUID idempotency keys, signal access and attempt metadata |
| Execution policies | Preserve validated retry/lease/timeout options; no implicit EF transient-retry behavior change |
| Provider contract | Preserve IWorkflowStore atomicity/fencing requirements and all state types; keep EF details out of Core |
| Legacy/public infrastructure | Retain repository/unit-of-work, DbContext/entities, registry/definition and design-time surfaces; avoid a last-minute breaking removal |
| Persistence | Retain both migration IDs, schema and stored status names; no schema change for preview.2 consumers |

Every package enables SDK package validation with baseline `0.1.0-preview.2`. `dotnet pack` must pass its compatibility checks; no compatibility suppressions are introduced. These checks supplement source review and behavior tests; they do not establish correctness of every external implementation or deployment. See SUPPORT.md for the 1.x compatibility commitment and STABLE-VALIDATION.md for actual results.
