#!/usr/bin/env python3
from __future__ import annotations

import json
import sys
import hashlib
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))
from rule_authority_errata_sources import errata_sources_for_ruleset


REPO_ROOT = Path(__file__).resolve().parents[1]
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion")
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"
PDF_SOURCE_IDENTITY = {
    "sr4": [Path("/mnt/pcloud/personal/Roleplay/sr/(SR4) Shadowrun 4e Core Rules.pdf")],
    "sr6": [
        Path("/mnt/pcloud/personal/Roleplay/sr/Shadowrun Sixth World.pdf"),
        Path("/mnt/pcloud/personal/Roleplay/sr/Shadowrun_6_Downloadversion_2024.pdf"),
        Path("/mnt/pcloud/personal/Roleplay/sr/Shadowrun_Street_Wyrd_(Core_Magic_Rulebook).pdf"),
        Path("/mnt/pcloud/personal/Roleplay/sr/Shadowrun - 6e - Krime Katalog.pdf"),
    ],
}


def now() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text.rstrip() + "\n", encoding="utf-8")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def pdf_source_identity(ruleset: str) -> list[dict[str, Any]]:
    sources = []
    for path in PDF_SOURCE_IDENTITY.get(ruleset, []):
        exists = path.is_file()
        sources.append({
            "file": path.name,
            "path": str(path),
            "exists": exists,
            "bytes": path.stat().st_size if exists else None,
            "sha256": sha256(path) if exists else None,
        })
    return sources


def indexed_source_files(table_imports: dict[str, Any]) -> list[str]:
    from_sources = [
        str(source.get("file"))
        for source in table_imports.get("sources", [])
        if isinstance(source, dict) and source.get("file")
    ]
    if from_sources:
        return from_sources

    from_files = [
        str(item.get("file"))
        for item in table_imports.get("files", [])
        if isinstance(item, dict) and item.get("file")
    ]
    source_path = str(table_imports.get("source_path") or "").strip()
    if source_path and from_files:
        return [f"{source_path}/{name}" for name in from_files]
    return from_files


def build_review_handoff(ruleset: str, row_level: dict[str, Any], errata_receipt: dict[str, Any]) -> str:
    upper = ruleset.upper()
    row_packet = row_level.get("review_packet", {})
    errata_packet = errata_receipt.get("review_packet", {})
    lines = [
        f"# {upper} Rule Authority Review Handoff",
        "",
        f"Generated: {now()}",
        "",
        "## Current Verdict",
        "",
        f"- Ready token: withheld",
        f"- Row-level mapping: `{row_level.get('status')}`",
        f"- Errata posture: `{errata_receipt.get('status')}`",
        f"- Ready for gold: `{bool(row_level.get('ready_for_gold') and errata_receipt.get('ready_for_gold'))}`",
        "",
        "## Machine-Completed Evidence",
        "",
        f"- Indexed units: `{row_packet.get('indexed_unit_count')}`",
        f"- Source count: `{row_packet.get('source_count')}`",
        f"- Registry rulefacts: `{row_level.get('registry_rulefact_count')}`",
        f"- Public-safe row receipt: `{row_level.get('public_safe')}`",
        f"- Errata source metadata count: `{errata_packet.get('source_count')}`",
        "",
        "## Human Decisions Required",
        "",
        "- Confirm source identity, license posture, and edition fit.",
        "- Map indexed rows or line hashes into normalized public-safe rule records.",
        "- Apply, reject as not applicable, or explicitly defer errata sources.",
        "- Confirm no sourcebook prose, art, page images, examples, or table text are promoted.",
        "- Sign off before any ready token is emitted.",
        "",
        "## Decision Fields",
        "",
        "- Row-level decision: `pending | approved | rejected | defer`",
        "- Errata decision: `pending | applied | not_applicable | defer`",
        "- Final reviewer: `pending`",
        "- Final review timestamp: `pending`",
        "",
        "## Blocking Receipts",
        "",
        f"- `{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json`",
        f"- `{upper}_ERRATA_SOURCE_POSTURE.generated.json`",
        f"- `{upper}_HUMAN_RULE_REVIEW.md`",
    ]
    private_registry = row_packet.get("private_registry")
    if private_registry:
        lines.extend(["", "## Private Review Registry", "", f"- `{private_registry}`"])
    return "\n".join(lines)


def build_human_rule_review(ruleset: str, row_level: dict[str, Any], errata_receipt: dict[str, Any]) -> str:
    upper = ruleset.upper()
    row_packet = row_level.get("review_packet", {})
    errata_packet = errata_receipt.get("review_packet", {})
    private_registry = row_packet.get("private_registry") or "none"
    indexed_sources = row_packet.get("indexed_source_files") or []
    indexed_source_lines = [f"- `{source}`" for source in indexed_sources] or ["- `none`"]
    source_identity = row_packet.get("source_identity") or []
    source_identity_lines = [
        f"- `{source.get('file')}` at `{source.get('path')}`; exists=`{source.get('exists')}`; sha256=`{source.get('sha256')}`"
        for source in source_identity
        if isinstance(source, dict)
    ] or ["- `none`"]
    lines = [
        f"# {upper} Human Rule Review",
        "",
        f"Generated: {now()}",
        "Status: pending",
        "Row-level decision: pending",
        "Errata decision: pending",
        "Reviewer: pending",
        "Review timestamp: pending",
        "Ready token approved: false",
        "",
        "## Machine Evidence",
        "",
        f"- Rulefacts indexed: `{row_level.get('registry_rulefact_count')}`",
        f"- Row-level mapping status: `{row_level.get('status')}`",
        f"- Indexed unit count: `{row_packet.get('indexed_unit_count')}`",
        f"- Source count: `{row_packet.get('source_count')}`",
        f"- Source baseline decision status: `{row_packet.get('source_baseline_decision_status')}`",
        f"- Errata posture status: `{errata_receipt.get('status')}`",
        f"- Errata source count: `{errata_packet.get('source_count')}`",
        f"- Public copy policy: `{row_packet.get('public_copy_policy')}`",
        "",
        "## Required Human Decisions",
        "",
        "- Confirm the indexed source surface is the correct edition authority.",
        "- Select or reject the edition/source baseline when multiple books are indexed.",
        "- Confirm row-level mappings are normalized facts, not copied source prose or tables.",
        "- Apply, reject as not applicable, or explicitly defer every applicable errata source.",
        "- Confirm fixture expectations are valid against reviewed rule authority.",
        "- Approve the ready token only after row-level and errata decisions are complete.",
        "",
        "## Review Inputs",
        "",
        f"- `{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json`",
        f"- `{upper}_ERRATA_SOURCE_POSTURE.generated.json`",
        f"- `{upper}_RULE_AUTHORITY_REVIEW_HANDOFF.md`",
        f"- Private registry: `{private_registry}`",
        "",
        "## Indexed Source Files",
        "",
        *indexed_source_lines,
        "",
        "## Source Identity Evidence",
        "",
        *source_identity_lines,
        "",
        "## Approval Contract",
        "",
        "Leave this file pending until review is complete. A ready review must change:",
        "",
        "- `Status: approved`",
        "- `Row-level decision: approved`",
        "- `Errata decision: applied`, `not_applicable`, or `defer` with written rationale",
        "- `Reviewer: <human reviewer>`",
        "- `Review timestamp: <UTC ISO-8601 timestamp>`",
        "- `Ready token approved: true`",
        "- `Errata defer rationale: <reason>` when the errata decision is `defer`",
        "- `Source baseline decision: <selected baseline>` when multiple source files are indexed",
    ]
    return "\n".join(lines)


def build_row_level_mapping_receipt(ruleset: str, table_imports: dict[str, Any], registry: dict[str, Any]) -> dict[str, Any]:
    source_kind = str(table_imports.get("source_kind", ""))
    if ruleset == "sr4":
        indexed_units = int(table_imports.get("row_count") or 0)
        indexed_label = "indexed_structured_rows"
        minimum = 1000
        review_subject = "legacy Chummer structured XML rows"
        category_counts = {
            str(item.get("file")): int(item.get("row_count") or 0)
            for item in table_imports.get("files", [])
            if isinstance(item, dict) and item.get("file")
        }
    else:
        indexed_units = int(table_imports.get("candidate_table_line_count") or 0)
        indexed_label = "indexed_candidate_lines"
        minimum = 1000
        review_subject = "private PDF table-candidate line hashes"
        category_counts = table_imports.get("category_table_candidate_counts", {})
    indexed_sources = indexed_source_files(table_imports)
    source_count = int(table_imports.get("sourcebook_count") or table_imports.get("file_count") or 0)
    source_baseline_required = ruleset == "sr6" and len(indexed_sources) > 1
    source_baseline_status = "pending_human_review" if source_baseline_required else "single_source"
    remaining_gate = str(table_imports.get("remaining_gate") or "")
    if "review" not in remaining_gate.lower():
        remaining_gate = "human review of indexed row-level authority mapping"

    return {
        "contract_name": f"chummer.{ruleset}.row_level_authority_mapping",
        "generated_at_utc": now(),
        "ruleset": ruleset,
        "status": "pending_human_review",
        "public_safe": True,
        "registry_rulefact_count": int(registry.get("rulefact_count") or 0),
        indexed_label: indexed_units,
        "source_kind": source_kind,
        "remaining_gate": remaining_gate,
        "ready_for_gold": False,
        "source_baseline_decision_status": source_baseline_status,
        "evidence_strength": "substantial_machine_indexed" if indexed_units >= minimum else "insufficient_indexed_surface",
        "machine_observations": {
            "table_status": table_imports.get("status"),
            "has_nonempty_index": indexed_units > 0,
            "requires_review_marker": "review" in str(table_imports.get("status", "")).lower() or "human review" in str(table_imports.get("remaining_gate", "")).lower(),
        },
        "machine_completed": {
            "indexed_surface_built": indexed_units > 0,
            "public_safe_receipt": True,
            "registry_rulefacts_available": int(registry.get("rulefact_count") or 0) > 0,
            "source_metadata_available": True,
        },
        "human_required": {
            "review_subject": review_subject,
            "must_confirm_license_and_source_identity": True,
            "must_confirm_edition_fit": True,
            "must_select_source_baseline": source_baseline_required,
            "must_map_rows_to_normalized_rule_records": True,
            "must_confirm_no_sourcebook_prose_is_promoted": True,
            "must_sign_off_before_ready_token": True,
        },
        "review_packet": {
            "decision": "pending",
            "required_decision_values": ["approved", "rejected", "defer"],
            "indexed_unit_count": indexed_units,
            "category_or_file_counts": category_counts,
            "private_registry": table_imports.get("private_registry"),
            "source_count": source_count,
            "indexed_source_files": indexed_sources,
            "source_identity": pdf_source_identity(ruleset),
            "source_baseline_decision_status": source_baseline_status,
            "public_copy_policy": table_imports.get("public_copy_policy"),
            "reviewer_output_expected": f"{ruleset.upper()}_HUMAN_RULE_REVIEW.md",
        },
    }


def build_errata_receipt(ruleset: str, errata_profile: dict[str, Any]) -> dict[str, Any]:
    sources = errata_sources_for_ruleset(ruleset)
    return {
        "contract_name": f"chummer.{ruleset}.errata_source_posture",
        "generated_at_utc": now(),
        "ruleset": ruleset,
        "status": "pending_reviewed_application",
        "required_before_gold": bool(errata_profile.get("required_before_gold")),
        "production_claim_allowed": bool(errata_profile.get("production_claim_allowed", False)),
        "sources": sources,
        "ready_for_gold": False,
        "machine_observations": {
            "profile_status": errata_profile.get("status"),
            "source_count": len(sources),
            "has_committed_source_metadata": len(sources) > 0,
        },
        "machine_completed": {
            "errata_profile_receipt_exists": True,
            "source_metadata_committed": len(sources) > 0,
            "production_claim_blocked_while_pending": True,
        },
        "human_required": {
            "must_confirm_applicable_errata_sources": True,
            "must_apply_or_explicitly_defer_each_applicable_change": True,
            "must_confirm_no_unreviewed_errata_changes_affect_ready_fixtures": True,
            "must_sign_off_before_ready_token": True,
        },
        "review_packet": {
            "decision": "pending",
            "required_decision_values": ["applied", "not_applicable", "defer"],
            "source_count": len(sources),
            "source_ids": [str(source.get("id")) for source in sources if isinstance(source, dict)],
            "reviewer_output_expected": f"{ruleset.upper()}_HUMAN_RULE_REVIEW.md",
        },
    }


def materialize_ruleset(ruleset: str) -> tuple[dict[str, Any], dict[str, Any]]:
    root = COMPLETION_ROOT / f"{ruleset}_rule_authority"
    upper = ruleset.upper()
    table_imports = load_json(root / f"{upper}_TABLE_IMPORTS.generated.json")
    errata = load_json(root / f"{upper}_ERRATA_PROFILE.generated.json")
    registry = load_json(PUBLISHED_ROOT / f"{upper}_RULEFACT_REGISTRY.generated.json")

    row_level = build_row_level_mapping_receipt(ruleset, table_imports, registry)
    errata_receipt = build_errata_receipt(ruleset, errata)
    return row_level, errata_receipt


def main() -> int:
    for ruleset in ("sr4", "sr6"):
        row_level, errata_receipt = materialize_ruleset(ruleset)
        upper = ruleset.upper()
        write_json(COMPLETION_ROOT / f"{ruleset}_rule_authority" / f"{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json", row_level)
        write_json(COMPLETION_ROOT / f"{ruleset}_rule_authority" / f"{upper}_ERRATA_SOURCE_POSTURE.generated.json", errata_receipt)
        write_json(PUBLISHED_ROOT / f"{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json", row_level)
        write_json(PUBLISHED_ROOT / f"{upper}_ERRATA_SOURCE_POSTURE.generated.json", errata_receipt)
        handoff = build_review_handoff(ruleset, row_level, errata_receipt)
        human_review = build_human_rule_review(ruleset, row_level, errata_receipt)
        write_text(COMPLETION_ROOT / f"{ruleset}_rule_authority" / f"{upper}_RULE_AUTHORITY_REVIEW_HANDOFF.md", handoff)
        write_text(PUBLISHED_ROOT / f"{upper}_RULE_AUTHORITY_REVIEW_HANDOFF.md", handoff)
        write_text(COMPLETION_ROOT / f"{ruleset}_rule_authority" / f"{upper}_HUMAN_RULE_REVIEW.md", human_review)
        write_text(PUBLISHED_ROOT / f"{upper}_HUMAN_RULE_REVIEW.md", human_review)

    summary = {
        "status": "ok",
        "rulesets": ["sr4", "sr6"],
    }
    print(json.dumps(summary, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
