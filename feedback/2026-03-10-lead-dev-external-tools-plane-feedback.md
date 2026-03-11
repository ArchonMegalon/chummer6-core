# Lead-dev feedback: core external-tools boundary

Date: 2026-03-10

Core stays external-tool agnostic.

Hold the line on these rules:

* no provider SDKs or direct vendor orchestration in core
* no external tool becomes engine truth, reducer truth, explain truth, or runtime truth
* only consume approved deterministic inputs or emit canonical DTOs for downstream repos to use

If a feature requires provider routing, approvals, docs/help, survey, previews, route rendering, or archive handling, it belongs elsewhere.
