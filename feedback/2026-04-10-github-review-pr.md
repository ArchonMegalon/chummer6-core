# GitHub Codex Review

PR: https://github.com/ArchonMegalon/chummer6-core/pull/9

Findings:
- [high] WORKLIST.md [contracts] wl202-missing-deterministic-error-retry-contracts
WORKLIST.md (WL-202) requires deterministic error classes with retriable/non-retriable flags and safe action suggestions.; BuildKitCompatibilityReceipt exposes only booleans plus narrative strings (`RuntimeCompatibilitySummary`, `SessionRuntimeSummary`, `CampaignReturnSummary`, `SupportClosureSummary`, `NextSafeActionSummary`) and no machine-readable error/retry classification fields.; WorkspacePortabilityReceipt exposes `NextSafeAction` as free text with no retry/fallback/continue class enum/flag contract.; DefaultHubProjectCompatibilityService composes notes by concatenating prose (`"Next safe action: ..."`) rather than emitting structured recovery class output.; HubProjectCompatibilityServiceTests validate English substring content (e.g., contains `"Next safe action:"`) instead of asserting deterministic recovery/error class fields.
Expected fix: Add explicit engine contract fields for deterministic failure/recovery classification (including retriable flag and safe-action class such as retry/fallback/continue), populate them from service outputs, and add tests that assert those structured fields rather than prose-only notes.
