# SR4 Reviewer Decision Packet

Generated: 2026-06-20T22:36:47Z

Status: awaiting_human_decision

Estimated review time: ~20 minutes

## Fast Path

Approve SR4 if bounded XML spot checks do not reveal contradictions. No separate errata call is required.

## Baseline

- Selected core baseline: `legacy Chummer4 XML as implemented for core readiness`
- Supplements in scope: `False`
- Human review file: `/docker/chummercomplete/_completion/sr4_rule_authority/SR4_HUMAN_RULE_REVIEW.md`

## Review Checklist

- Row-level mapping status: `pending_human_review`
- Indexed units: `6989`
- Errata status: `not_applicable_by_policy`
- Errata recommended decision: `not_applicable`
- Fixture status: `core_seed_fixture_pack_passed`
- Explain receipt status: `core_seed_receipt_pack_available`
- Rulefact count: `449`

## Required Human Actions

- review row-level mapping packet and approve or reject normalized public-safe records
- complete human rule review signoff

## Preferred Signoff Path

- spot-check the high-volume XML files listed in the handoff and approve row-level mapping if no contradiction is found
- keep Errata decision at not_applicable
- approve the human review file and rerun the ready checks

## Pass Criteria

- selected source identity exists and matches the recorded sha256
- bounded spot checks do not reveal contradictions in normalized authority mapping
- no sourcebook prose, art, tables, examples, or page images are promoted into public-safe receipts
- errata remains not_applicable under the selected core-only scope

## Why This Should Pass

- core baseline is explicit and supplements are out of scope
- fixture and explain alignment already pass
- review burden is limited to row-level spot checks and final signoff

## Suggested Default Decisions

- Row-level decision: `approved if bounded spot checks do not reveal contradictions`
- Errata decision: `not_applicable`
- Errata rationale: `no official errata sources are in scope for the selected SR4 core-only baseline`

## Decision Table

- `approve`: spot checks align and no contradiction is found
- `reject`: a concrete contradiction is found in the normalized row-level mapping


## Review Inputs

- Row-level mapping: `/docker/chummercomplete/_completion/sr4_rule_authority/SR4_ROW_LEVEL_AUTHORITY_MAPPING.generated.json`
- Errata posture: `/docker/chummercomplete/_completion/sr4_rule_authority/SR4_ERRATA_SOURCE_POSTURE.generated.json`
- Review handoff: `/docker/chummercomplete/_completion/sr4_rule_authority/SR4_RULE_AUTHORITY_REVIEW_HANDOFF.md`
- Private registry: `none`

## Exact File Edits

- `Status: approved`
- `Row-level decision: approved`
- `Errata decision: not_applicable`
- `Reviewer: <human reviewer>`
- `Review timestamp: <UTC ISO-8601 timestamp>`
- `Ready token approved: true`

## Rerun Commands

- `python3 /docker/chummercomplete/chummer-core-engine/scripts/verify_rule_authority_human_review.py sr4 --require-ready`
- `python3 /docker/chummercomplete/chummer-core-engine/scripts/materialize_rule_authority_reviewer_packets.py`
- `python3 /docker/chummercomplete/chummer-core-engine/scripts/materialize_rule_authority_blocker_receipts.py`
- `python3 /docker/chummercomplete/chummer-core-engine/scripts/audit_rule_authority_operator_review.py`
- `bash /docker/chummercomplete/chummer-core-engine/scripts/ai/verify.sh`

## Signoff Preconditions

- row-level decision is approved
- human review file is approved with ready token approved true
