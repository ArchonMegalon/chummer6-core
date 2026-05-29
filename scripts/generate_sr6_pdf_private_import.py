#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

from pypdf import PdfReader


REPO_ROOT = Path(__file__).resolve().parents[1]
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion/sr6_rule_authority")
PRIVATE_ROOT = COMPLETION_ROOT / "private"
SOURCE_ROOT = Path("/mnt/pcloud/personal/Roleplay/sr")

SOURCEBOOKS = [
    SOURCE_ROOT / "Shadowrun Sixth World.pdf",
    SOURCE_ROOT / "Shadowrun_6_Downloadversion_2024.pdf",
    SOURCE_ROOT / "Shadowrun_Street_Wyrd_(Core_Magic_Rulebook).pdf",
    SOURCE_ROOT / "Shadowrun - 6e - Krime Katalog.pdf",
]

CATEGORIES: dict[str, tuple[str, ...]] = {
    "priority_metatype": ("priority table", "metatype"),
    "skills": ("skills", "skill"),
    "qualities": ("qualities", "quality"),
    "weapons": ("weapons", "weapon", "ranged weapons", "melee weapons"),
    "armor": ("armor",),
    "gear": ("gear", "equipment"),
    "cyberware_bioware": ("cyberware", "bioware", "augmentations"),
    "magic_spells": ("spells", "rituals", "adept powers", "spirits"),
    "matrix": ("matrix", "programs", "devices"),
    "rigging_vehicles_drones": ("vehicles", "drones", "rcc", "autosofts"),
}

NUMERIC_RE = re.compile(r"(?<![A-Za-z])[-+]?(?:\d+[A-Za-z]?|\d+/\d+|\d+[.,]\d+)(?![A-Za-z])")
MONEY_RE = re.compile(r"(?:¥|nuyen|k¥)", re.IGNORECASE)
DICE_RE = re.compile(r"\b(?:\d+d6|[A-Z][A-Za-z]+\s*\+\s*[A-Z][A-Za-z]+)\b")


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def normalized_line_hash(line: str) -> str:
    normalized = " ".join(line.split()).casefold()
    return sha256_bytes(normalized.encode("utf-8"))


def classify_line(line: str) -> list[str]:
    lowered = line.casefold()
    return [
        category
        for category, needles in CATEGORIES.items()
        if any(needle in lowered for needle in needles)
    ]


def is_candidate_table_line(line: str) -> bool:
    numeric_count = len(NUMERIC_RE.findall(line))
    has_money = bool(MONEY_RE.search(line))
    has_dice = bool(DICE_RE.search(line))
    compact = " ".join(line.split())
    return len(compact) >= 8 and (numeric_count >= 2 or (numeric_count >= 1 and (has_money or has_dice)))


def page_lines(page) -> list[str]:
    text = page.extract_text() or ""
    return [" ".join(line.split()) for line in text.splitlines() if line.strip()]


def increment_all(target: dict[str, int], keys: Iterable[str]) -> None:
    for key in keys:
        target[key] = target.get(key, 0) + 1


def inspect_pdf(path: Path) -> dict:
    reader = PdfReader(str(path))
    source_hash = sha256_file(path)
    page_summaries = []
    row_hashes = []
    category_line_counts: dict[str, int] = {}
    category_table_candidate_counts: dict[str, int] = {}
    nonempty_line_count = 0

    for page_index, page in enumerate(reader.pages, start=1):
        lines = page_lines(page)
        nonempty_line_count += len(lines)
        page_categories: dict[str, int] = {}
        page_category_names: set[str] = set()
        for line in lines:
            page_category_names.update(classify_line(line))

        table_candidate_count = 0
        page_hash_input: list[str] = []

        for line_index, line in enumerate(lines, start=1):
            categories = classify_line(line)
            increment_all(category_line_counts, categories)
            increment_all(page_categories, categories)
            line_hash = normalized_line_hash(line)
            page_hash_input.append(line_hash)

            if is_candidate_table_line(line):
                table_candidate_count += 1
                candidate_categories = categories or sorted(page_category_names) or ["uncategorized"]
                increment_all(category_table_candidate_counts, candidate_categories)
                row_hashes.append({
                    "source_sha256": source_hash,
                    "page": page_index,
                    "line": line_index,
                    "line_sha256": line_hash,
                    "categories": candidate_categories,
                    "numeric_token_count": len(NUMERIC_RE.findall(line)),
                    "has_money_token": bool(MONEY_RE.search(line)),
                    "has_dice_expression": bool(DICE_RE.search(line)),
                })

        page_summaries.append({
            "page": page_index,
            "line_count": len(lines),
            "page_line_hash": sha256_bytes("\\n".join(page_hash_input).encode("utf-8")),
            "category_line_counts": page_categories,
            "candidate_table_line_count": table_candidate_count,
        })

    return {
        "file": path.name,
        "source_path": str(path),
        "source_sha256": source_hash,
        "bytes": path.stat().st_size,
        "page_count": len(reader.pages),
        "nonempty_line_count": nonempty_line_count,
        "candidate_table_line_count": len(row_hashes),
        "category_line_counts": category_line_counts,
        "category_table_candidate_counts": category_table_candidate_counts,
        "pages": page_summaries,
        "candidate_rows": row_hashes,
    }


def write_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main() -> int:
    missing = [str(path) for path in SOURCEBOOKS if not path.is_file()]
    if missing:
        print(json.dumps({"status": "fail", "missing": missing}, indent=2))
        return 1

    generated_at = datetime.now(timezone.utc).isoformat(timespec="seconds")
    sources = [inspect_pdf(path) for path in SOURCEBOOKS]
    candidate_count = sum(source["candidate_table_line_count"] for source in sources)
    line_count = sum(source["nonempty_line_count"] for source in sources)
    category_counts: dict[str, int] = {}
    category_candidate_counts: dict[str, int] = {}
    for source in sources:
        increment_all(category_counts, [
            category
            for category, count in source["category_line_counts"].items()
            for _ in range(count)
        ])
        increment_all(category_candidate_counts, [
            category
            for category, count in source["category_table_candidate_counts"].items()
            for _ in range(count)
        ])

    private_payload = {
        "status": "private_pdf_line_hash_import_indexed_pending_review",
        "ruleset": "sr6",
        "generated_at_utc": generated_at,
        "sourcebook_count": len(sources),
        "nonempty_line_count": line_count,
        "candidate_table_line_count": candidate_count,
        "category_line_counts": category_counts,
        "category_table_candidate_counts": category_candidate_counts,
        "sources": sources,
        "copyright_boundary": (
            "Private artifact stores hashes, positions, metrics, and categories only; "
            "it does not store copied sourcebook prose, page images, examples, item descriptions, or table cell text."
        ),
        "remaining_gate": "human review must map hashed candidate rows into normalized data records before SR6_RULE_AUTHORITY_READY",
    }

    public_sources = [
        {
            "file": source["file"],
            "source_path": source["source_path"],
            "source_sha256": source["source_sha256"],
            "bytes": source["bytes"],
            "page_count": source["page_count"],
            "nonempty_line_count": source["nonempty_line_count"],
            "candidate_table_line_count": source["candidate_table_line_count"],
            "category_line_counts": source["category_line_counts"],
            "category_table_candidate_counts": source["category_table_candidate_counts"],
        }
        for source in sources
    ]
    public_payload = {
        "status": "private_pdf_line_hash_import_indexed_pending_review",
        "ruleset": "sr6",
        "source_kind": "private_local_sourcebook_pdf_line_hashes",
        "generated_at_utc": generated_at,
        "sourcebook_count": len(sources),
        "nonempty_line_count": line_count,
        "candidate_table_line_count": candidate_count,
        "category_line_counts": category_counts,
        "category_table_candidate_counts": category_candidate_counts,
        "sources": public_sources,
        "private_registry": str(PRIVATE_ROOT / "SR6_TABLE_ROW_HASH_REGISTRY.private.generated.json"),
        "public_copy_policy": (
            "metadata only: hashes, positions, categories, and counts; no sourcebook prose, art, page images, "
            "examples, item descriptions, or table cell text"
        ),
        "remaining_gate": "human review must map hashed candidate rows into normalized data records before SR6_RULE_AUTHORITY_READY",
    }

    write_json(PRIVATE_ROOT / "SR6_TABLE_ROW_HASH_REGISTRY.private.generated.json", private_payload)
    write_json(COMPLETION_ROOT / "SR6_TABLE_IMPORTS.generated.json", public_payload)
    write_json(PUBLISHED_ROOT / "SR6_TABLE_IMPORTS.generated.json", public_payload)
    print(json.dumps({
        "status": "pass",
        "sourcebook_count": len(sources),
        "nonempty_line_count": line_count,
        "candidate_table_line_count": candidate_count,
        "private_registry": str(PRIVATE_ROOT / "SR6_TABLE_ROW_HASH_REGISTRY.private.generated.json"),
    }, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
