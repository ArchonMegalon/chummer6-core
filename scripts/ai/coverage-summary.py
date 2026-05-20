#!/usr/bin/env python3
from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def main() -> int:
    if len(sys.argv) not in (3, 4):
        print("usage: coverage-summary.py <coverage-root> <summary-json> [repo-root]", file=sys.stderr)
        return 2

    coverage_root = Path(sys.argv[1])
    summary_json = Path(sys.argv[2])
    repo_root = Path(sys.argv[3]).resolve() if len(sys.argv) == 4 else Path.cwd().resolve()
    files = sorted(coverage_root.rglob("coverage.cobertura.xml"))
    if not files:
        print(f"no coverage.cobertura.xml files found under {coverage_root}", file=sys.stderr)
        return 1

    line_valid = 0
    line_covered = 0
    branch_valid = 0
    branch_covered = 0
    class_count = 0
    skipped_external_classes = 0

    for file in files:
        root = ET.parse(file).getroot()
        for cls in root.findall(".//class"):
            filename = cls.attrib.get("filename")
            if not filename:
                continue

            try:
                resolved = Path(filename).resolve()
            except OSError:
                continue

            if not str(resolved).startswith(str(repo_root)):
                skipped_external_classes += 1
                continue

            class_count += 1
            lines = cls.findall("./lines/line")
            line_valid += len(lines)
            line_covered += sum(1 for line in lines if int(line.attrib.get("hits", "0")) > 0)

            for line in lines:
                if line.attrib.get("branch", "").lower() != "true":
                    continue
                conditions = line.attrib.get("condition-coverage", "")
                if "(" not in conditions or "/" not in conditions:
                    continue
                covered_part = conditions.split("(", 1)[1].split(")", 1)[0]
                covered_count, valid_count = covered_part.split("/", 1)
                branch_covered += int(covered_count)
                branch_valid += int(valid_count)

    line_rate = 0.0 if line_valid == 0 else line_covered / line_valid
    branch_rate = 0.0 if branch_valid == 0 else branch_covered / branch_valid
    summary = {
        "coverage_root": str(coverage_root),
        "repo_root": str(repo_root),
        "report_count": len(files),
        "class_count": class_count,
        "skipped_external_classes": skipped_external_classes,
        "line_covered": line_covered,
        "line_valid": line_valid,
        "line_rate": line_rate,
        "branch_covered": branch_covered,
        "branch_valid": branch_valid,
        "branch_rate": branch_rate,
    }
    summary_json.parent.mkdir(parents=True, exist_ok=True)
    summary_json.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")

    print(
        f"coverage summary: lines {line_covered}/{line_valid} ({line_rate:.2%}), "
        f"branches {branch_covered}/{branch_valid} ({branch_rate:.2%}), "
        f"classes {class_count}, external classes skipped {skipped_external_classes}, reports {len(files)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
