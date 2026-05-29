#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
from xml.etree import ElementTree


REPO_ROOT = Path(__file__).resolve().parents[1]
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion")

SR4_XML_ROOT = Path("/docker/fleet/repos/chummer4/Chummer/bin/Release/data")
SR5_XML_ROOT = Path("/docker/chummer5a/Chummer/data")
SR6_SOURCEBOOK_ROOT = Path("/mnt/pcloud/personal/Roleplay/sr")

SR6_SOURCEBOOKS = [
    "Shadowrun Sixth World.pdf",
    "Shadowrun_6_Downloadversion_2024.pdf",
    "Shadowrun_Street_Wyrd_(Core_Magic_Rulebook).pdf",
    "Shadowrun - 6e - Krime Katalog.pdf",
]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def rel_or_abs(path: Path) -> str:
    return str(path)


def inspect_xml_file(path: Path) -> dict:
    tree = ElementTree.parse(path)
    root = tree.getroot()
    containers: dict[str, int] = {}
    row_count = 0
    for child in list(root):
        count = len(list(child))
        containers[child.tag] = count
        row_count += count

    return {
        "file": path.name,
        "root": root.tag,
        "sha256": sha256(path),
        "container_counts": containers,
        "row_count": row_count,
    }


def inspect_xml_root(root: Path, ruleset: str) -> dict:
    files = sorted(root.glob("*.xml"))
    inspections = [inspect_xml_file(path) for path in files]
    return {
        "ruleset": ruleset,
        "status": "structured_legacy_data_indexed_pending_human_review",
        "source_kind": "legacy_chummer_structured_xml",
        "source_path": rel_or_abs(root),
        "file_count": len(inspections),
        "row_count": sum(item["row_count"] for item in inspections),
        "files": inspections,
        "public_copy_policy": (
            "metadata only: file names, hashes, XML container names, and counts; "
            "no sourcebook prose, art, page images, item descriptions, or stat rows"
        ),
        "remaining_gate": "human review must confirm licenses, edition fit, errata, and row-level mappings before READY",
    }


def apply_sr5_acceptance(payload: dict) -> dict:
    proof_path = PUBLISHED_ROOT / "SR5_ACCEPTANCE_PROOF.generated.json"
    if not proof_path.is_file():
        return payload

    proof = json.loads(proof_path.read_text(encoding="utf-8"))
    if proof.get("status") == "pass" and proof.get("serious_implementation_claim") == "allowed":
        payload["status"] = "accepted_sr5_structured_data_indexed"
        payload["acceptance_proof"] = str(proof_path)
        payload["coverage_threshold"] = proof.get("coverage_threshold")
        payload["remaining_gate"] = "none under current SR5 acceptance proof"
    return payload


def inspect_sr6_sources() -> dict:
    existing = COMPLETION_ROOT / "sr6_rule_authority" / "SR6_TABLE_IMPORTS.generated.json"
    if existing.is_file():
        payload = json.loads(existing.read_text(encoding="utf-8"))
        if payload.get("status") == "private_pdf_line_hash_import_indexed_pending_review":
            return payload

    sources = []
    for name in SR6_SOURCEBOOKS:
        path = SR6_SOURCEBOOK_ROOT / name
        if path.is_file():
            sources.append({
                "file": path.name,
                "source_path": rel_or_abs(path),
                "sha256": sha256(path),
                "bytes": path.stat().st_size,
            })

    return {
        "ruleset": "sr6",
        "status": "sourcebooks_indexed_no_structured_table_import",
        "source_kind": "private_local_sourcebooks",
        "source_root": rel_or_abs(SR6_SOURCEBOOK_ROOT),
        "sourcebook_count": len(sources),
        "sourcebooks": sources,
        "public_copy_policy": (
            "metadata only: file names, hashes, and sizes; no sourcebook prose, art, page images, "
            "examples, item descriptions, or stat rows"
        ),
        "remaining_gate": "controlled SR6 table extraction, review, errata profile, fixtures, and human signoff are still required before READY",
    }


def write_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main() -> int:
    generated_at = datetime.now(timezone.utc).isoformat(timespec="seconds")
    sr4 = inspect_xml_root(SR4_XML_ROOT, "sr4")
    sr5 = apply_sr5_acceptance(inspect_xml_root(SR5_XML_ROOT, "sr5"))
    sr6 = inspect_sr6_sources()

    for payload in (sr4, sr5, sr6):
        payload["generated_at_utc"] = generated_at

    combined = {
        "status": "pass",
        "generated_at_utc": generated_at,
        "rulesets": {
            "sr4": {
                "table_import_status": sr4["status"],
                "file_count": sr4["file_count"],
                "row_count": sr4["row_count"],
                "remaining_gate": sr4["remaining_gate"],
            },
            "sr5": {
                "table_import_status": sr5["status"],
                "file_count": sr5["file_count"],
                "row_count": sr5["row_count"],
                "remaining_gate": sr5["remaining_gate"],
            },
            "sr6": {
                "table_import_status": sr6["status"],
                "sourcebook_count": sr6["sourcebook_count"],
                "remaining_gate": sr6["remaining_gate"],
            },
        },
        "copyright_boundary": "Receipts contain metadata and counts only, not copyrighted rulebook prose or copied tables.",
    }

    write_json(PUBLISHED_ROOT / "SR456_TABLE_IMPORT_COVERAGE.generated.json", combined)
    write_json(PUBLISHED_ROOT / "SR4_TABLE_IMPORTS.generated.json", sr4)
    write_json(PUBLISHED_ROOT / "SR5_TABLE_IMPORTS.generated.json", sr5)
    write_json(PUBLISHED_ROOT / "SR6_TABLE_IMPORTS.generated.json", sr6)
    write_json(COMPLETION_ROOT / "sr4_rule_authority" / "SR4_TABLE_IMPORTS.generated.json", sr4)
    write_json(COMPLETION_ROOT / "sr6_rule_authority" / "SR6_TABLE_IMPORTS.generated.json", sr6)

    print(json.dumps(combined, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
