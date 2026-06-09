#!/usr/bin/env python3
from __future__ import annotations

import json
from datetime import UTC, datetime
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[1]
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion")
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"

ERRATA_SOURCES = {
    "sr4": [],
    "sr6": [
        {
            "id": "sr6_aug_2019",
            "url": "https://shadowrunsixthworld.com/wp-content/uploads/sites/5/2019/08/SR6-Core-Rulebook-Errata-Aug-2019.pdf",
            "observed_page_count": 10,
            "observed_sha256": "84a488965df544eb5661def7188baeef2a8d38d1fb006f00b5537e1850b6b5db",
        },
        {
            "id": "sr6_feb_2020",
            "url": "https://shadowrunsixthworld.com/wp-content/uploads/sites/5/2020/03/SR6-Core-Rulebook-Errata-Feb-2020.pdf",
            "observed_page_count": 6,
            "observed_sha256": None,
        },
        {
            "id": "sr6_city_edition_notice",
            "url": "https://shadowrunsixthworld.com/2021/09/15/hit-the-streets-with-shadowrun-sixth-world-city-edition-and-improved-dice-roller-app/",
            "observed_fact": "official notice says City Edition: Seattle includes latest errata and updates",
        },
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


def build_row_level_mapping_receipt(ruleset: str, table_imports: dict[str, Any], registry: dict[str, Any]) -> dict[str, Any]:
    source_kind = str(table_imports.get("source_kind", ""))
    if ruleset == "sr4":
        indexed_units = int(table_imports.get("row_count") or 0)
        indexed_label = "indexed_structured_rows"
        minimum = 1000
    else:
        indexed_units = int(table_imports.get("candidate_table_line_count") or 0)
        indexed_label = "indexed_candidate_lines"
        minimum = 1000

    return {
        "contract_name": f"chummer.{ruleset}.row_level_authority_mapping",
        "generated_at_utc": now(),
        "ruleset": ruleset,
        "status": "pending_human_review",
        "public_safe": True,
        "registry_rulefact_count": int(registry.get("rulefact_count") or 0),
        indexed_label: indexed_units,
        "source_kind": source_kind,
        "remaining_gate": table_imports.get("remaining_gate"),
        "ready_for_gold": False,
        "evidence_strength": "substantial_machine_indexed" if indexed_units >= minimum else "insufficient_indexed_surface",
        "machine_observations": {
            "table_status": table_imports.get("status"),
            "has_nonempty_index": indexed_units > 0,
            "requires_review_marker": "review" in str(table_imports.get("status", "")).lower() or "human review" in str(table_imports.get("remaining_gate", "")).lower(),
        },
    }


def build_errata_receipt(ruleset: str, errata_profile: dict[str, Any]) -> dict[str, Any]:
    return {
        "contract_name": f"chummer.{ruleset}.errata_source_posture",
        "generated_at_utc": now(),
        "ruleset": ruleset,
        "status": "pending_reviewed_application" if errata_profile.get("status") == "pending" else str(errata_profile.get("status") or "unknown"),
        "required_before_gold": bool(errata_profile.get("required_before_gold")),
        "production_claim_allowed": bool(errata_profile.get("production_claim_allowed", False)),
        "sources": ERRATA_SOURCES[ruleset],
        "ready_for_gold": False,
        "machine_observations": {
            "profile_status": errata_profile.get("status"),
            "source_count": len(ERRATA_SOURCES[ruleset]),
            "has_committed_source_metadata": len(ERRATA_SOURCES[ruleset]) > 0,
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

    summary = {
        "status": "ok",
        "rulesets": ["sr4", "sr6"],
    }
    print(json.dumps(summary, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
