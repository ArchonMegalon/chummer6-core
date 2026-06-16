# SR6 Reviewer Decision Packet

Generated: 2026-06-16T09:21:24Z

Status: awaiting_human_decision

## Baseline

- Selected core baseline: `Shadowrun_6_Downloadversion_2024.pdf`
- Supplements in scope: `False`
- Human review file: `/docker/chummercomplete/_completion/sr6_rule_authority/SR6_HUMAN_RULE_REVIEW.md`

## Review Checklist

- Row-level mapping status: `pending_human_review`
- Indexed units: `5213`
- Errata status: `pending_reviewed_application`
- Errata recommended decision: `applied`
- Fixture status: `core_seed_fixture_pack_passed`
- Explain receipt status: `core_seed_receipt_pack_available`
- Rulefact count: `447`

## Required Human Actions

- review row-level mapping packet and approve or reject normalized public-safe records
- review errata packet and record applied/not_applicable/defer decision
- complete human rule review signoff

## Preferred Signoff Path

- spot-check the 2024-core line-hash candidates listed in the handoff and approve row-level mapping if no contradiction is found
- prefer Errata decision applied if the 2024 baseline is accepted as the consolidated core source
- use defer only for a specific official errata source that cannot be reconciled to the 2024 baseline
- approve the human review file and rerun the ready checks

## Pass Criteria

- selected source identity exists and matches the recorded sha256
- bounded spot checks do not reveal contradictions in normalized authority mapping
- no sourcebook prose, art, tables, examples, or page images are promoted into public-safe receipts
- official errata decision is recorded against the selected 2024 core baseline

## Why This Should Pass

- core baseline is explicit and later than the listed 2019/2020 errata sources
- fixture and explain alignment already pass
- review burden is limited to bounded line-hash spot checks, one errata decision, and final signoff

## Suggested Default Decisions

- Row-level decision: `approved if bounded spot checks do not reveal contradictions`
- Errata decision: `applied unless a specific official errata source remains unreconciled to the selected 2024 core baseline`
- Errata rationale: `selected 2024 core baseline is the authority target; prefer applied if it is accepted as the consolidated official source`


## Review Inputs

- Row-level mapping: `/docker/chummercomplete/_completion/sr6_rule_authority/SR6_ROW_LEVEL_AUTHORITY_MAPPING.generated.json`
- Errata posture: `/docker/chummercomplete/_completion/sr6_rule_authority/SR6_ERRATA_SOURCE_POSTURE.generated.json`
- Review handoff: `/docker/chummercomplete/_completion/sr6_rule_authority/SR6_RULE_AUTHORITY_REVIEW_HANDOFF.md`
- Private registry: `/docker/chummercomplete/_completion/sr6_rule_authority/private/SR6_TABLE_ROW_HASH_REGISTRY.private.generated.json`

## Exact File Edits

- `Status: approved`
- `Row-level decision: approved`
- `Errata decision: applied | not_applicable | defer`
- `Reviewer: <human reviewer>`
- `Review timestamp: <UTC ISO-8601 timestamp>`
- `Ready token approved: true`
- `Errata defer rationale: <required when Errata decision is defer>`

## Rerun Commands

- `python3 /docker/chummercomplete/chummer-core-engine/scripts/verify_rule_authority_human_review.py sr6 --require-ready`
- `python3 /docker/chummercomplete/chummer-core-engine/scripts/materialize_rule_authority_reviewer_packets.py`
- `python3 /docker/chummercomplete/chummer-core-engine/scripts/materialize_rule_authority_blocker_receipts.py`
- `python3 /docker/chummercomplete/chummer-core-engine/scripts/audit_rule_authority_operator_review.py`
- `bash /docker/chummercomplete/chummer-core-engine/scripts/ai/verify.sh`

## Signoff Preconditions

- row-level decision is approved
- human review file is approved with ready token approved true
- errata decision is applied/not_applicable/defer with rationale
