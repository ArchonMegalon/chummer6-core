# SR6 Reviewer Decision Packet

Generated: 2026-06-16T09:09:16Z

Status: awaiting_human_decision

## Baseline

- Selected core baseline: `Shadowrun_6_Downloadversion_2024.pdf`
- Supplements in scope: `False`
- Human review file: `/docker/chummercomplete/_completion/sr6_rule_authority/SR6_HUMAN_RULE_REVIEW.md`

## Review Checklist

- Row-level mapping status: `pending_human_review`
- Indexed units: `5213`
- Errata status: `pending_reviewed_application`
- Errata recommended decision: `pending_manual_review`
- Fixture status: `core_seed_fixture_pack_passed`
- Explain receipt status: `core_seed_receipt_pack_available`
- Rulefact count: `447`

## Required Human Actions

- review row-level mapping packet and approve or reject normalized public-safe records
- review errata packet and record applied/not_applicable/defer decision
- complete human rule review signoff

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
