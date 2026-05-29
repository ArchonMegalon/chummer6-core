# Codex Operator Rule Authority Review

Generated: 2026-05-29T15:19:33.078644Z

Status: operator review complete; rule authority remains blocked.

## Scope

This audit checks local source identity, edition fit, receipt consistency, provider coverage, seed fixture execution, table-import posture, errata posture, and copyright boundary. It does not copy sourcebook prose, page images, fiction, or art.

## Source Identity

- sr4: exists=True, sha256_matches_expected=True, page_count=378, page_count_matches_expected=True
- sr5: exists=True, sha256_matches_expected=True, page_count=482, page_count_matches_expected=True
- sr6_2019: exists=True, sha256_matches_expected=True, page_count=322, page_count_matches_expected=True
- sr6_2024: exists=True, sha256_matches_expected=True, page_count=354, page_count_matches_expected=True

## Rule Receipts

### SR4
- rulefact_count: 7
- rulefact_ids: ['sr4.dice.type', 'sr4.dice.hit_faces', 'sr4.dice.rule_of_six', 'sr4.dice.glitch', 'sr4.dice.critical_glitch', 'sr4.dice.buy_hits', 'sr4.dice.long_shot']
- implemented_provider_count: 14
- missing_implemented_providers: []
- missing_profile_status: []
- provider_status: pass
- table_import_status: structured_legacy_data_indexed_pending_human_review
- table_row_count: 6989
- table_file_count: 27
- fixture_status: seed_fixtures_passed
- fixture_passed: 21
- fixture_failed: 0
- explain_status: seed_receipts_available
- errata_status: pending
- copyright_status: pass
- readiness_token_allowed: False

### SR5
- acceptance_proof_status: pass
- serious_implementation_claim: allowed

### SR6
- rulefact_count: 5
- rulefact_ids: ['sr6.dice.hit_faces', 'sr6.dice.glitch', 'sr6.dice.critical_glitch', 'sr6.dice.buy_hits', 'sr6.dice.wild_die']
- implemented_provider_count: 18
- missing_implemented_providers: []
- missing_profile_status: []
- provider_status: provider_classes_covered_not_authority_ready
- table_import_status: private_pdf_line_hash_import_indexed_pending_review
- table_row_count: None
- table_file_count: None
- fixture_status: seed_fixtures_passed
- fixture_passed: 37
- fixture_failed: 0
- explain_status: seeded
- errata_status: pending
- copyright_status: pass
- readiness_token_allowed: False

## Findings

- source_identity: pass - Local SR4, SR5, SR6 2019, and SR6 2024 PDFs are present and SHA-pinned.
- copyright_boundary: pass - No review artifact commits sourcebook prose, page images, fiction, or art.
- provider_class_coverage: pass - SR4 and SR6 required provider classes are present with no missing implementation/profile entries.
- seed_fixture_execution: pass - Focused SR4/SR6 seed fixture receipts report zero failures.
- rulefact_depth: blocker - RuleFact registries still contain seed-level dice/core facts only, not the full P0/P1 chapter authority corpus.
- row_level_table_mapping: blocker - SR4 structured legacy data and SR6 private PDF line hashes are indexed, but not reviewed into normalized row-level authority records.
- errata_application: blocker - Official SR6 errata/update sources are identified, but errata deltas are not applied and reviewed in providers/table records; SR4 errata profile also remains pending.
- human_signoff: blocker - This is a Codex operator audit, not independent human/editorial/legal signoff.

## Decision

Do not promote SR4 or SR6 to rule-authority ready from this audit alone. The remaining work is not code-class discovery; it is reviewed row-level rule/data mapping, errata application, a larger authority fixture corpus, public-safe explain receipts for every authority rule, and independent human signoff.
