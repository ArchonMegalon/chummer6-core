#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PACKAGE_ID = "next90-m143-core-keep-export-print-supplement-and-rule-environment-receipts-deterministi"
FRONTIER_ID = 2778308338
MILESTONE_ID = 143
WORK_TASK_ID = "143.2"
TITLE = "Keep export, print, supplement, and rule-environment receipts deterministic enough to prove the workflows rather than just render them."
TASK = TITLE
OWNED_SURFACES = ["keep_export_print_supplement_and_rule_environment_receip:core"]
ALLOWED_PATHS = ["src", "tests", "docs", "scripts"]
PACKAGE_REPO = "chummer6-core"
PUBLISHED_RECEIPT_PATH = "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.generated.json"
DEFAULT_OUTPUT_RELATIVE_PATH = Path(".codex-studio") / "published" / "NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.generated.json"

REQUIRED_FILES = {
    "tool_catalog_contracts": Path("Chummer.Contracts/Api/ToolCatalogModels.cs"),
    "workspace_contracts": Path("Chummer.Contracts/Workspaces/CharacterWorkspaceModels.cs"),
    "tool_catalog_service": Path("Chummer.Infrastructure/Xml/XmlToolCatalogService.cs"),
    "workspace_service": Path("Chummer.Application/Workspaces/WorkspaceService.cs"),
    "test_program": Path("Chummer.CoreEngine.Tests/Program.cs"),
    "tool_catalog_mstest": Path("Chummer.Tests/ToolCatalogServiceTests.cs"),
    "workspace_service_mstest": Path("Chummer.Tests/WorkspaceServiceTests.cs"),
    "api_integration_mstest": Path("Chummer.Tests/ApiIntegrationTests.cs"),
    "repo_verify": Path("scripts/ai/verify.sh"),
    "docs": Path("docs/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.md"),
}

REQUIRED_SNIPPETS = {
    "tool_catalog_contracts": [
        "public sealed record Sr6SuccessorLaneDeterministicReceipt(",
        "string ParityFamilyId,",
        "string Sr6SupplementLanePosture,",
        "string Sr6SuccessorLaneReceipt,",
        "string HouseRuleLanePosture,",
        "string OnlineStorageReceiptPosture,",
        "int OnlineStorageCoveragePercent);",
    ],
    "workspace_contracts": [
        "public sealed record WorkspaceExchangeDeterministicReceipt(",
        "string RuleEnvironmentPosture,",
        "string RuleEnvironmentSummary,",
        "string RuleEnvironmentFingerprint,",
        "string SettingsProfile,",
        "string GameplayOption,",
        "IReadOnlyList<string> BannedWareGrades);",
        "WorkspaceExchangeDeterministicReceipt? ExchangeDeterministicReceipt = null);",
    ],
    "tool_catalog_service": [
        "private static Sr6SuccessorLaneDeterministicReceipt BuildSr6SuccessorDeterministicReceipt(",
        'ParityFamilyId: "family:sr6_supplements_designers_and_house_rules",',
        "Sr6SuccessorLaneReceipt: BuildSr6SuccessorLaneReceipt(",
        "OnlineStorageCoveragePercent: onlineStorageCoveragePercent);",
    ],
    "workspace_service": [
        'private const string ExchangeParityFamilyId = "family:sheet_export_print_viewer_and_exchange";',
        "private static WorkspaceExchangeDeterministicReceipt BuildExchangeDeterministicReceipt(",
        "WorkspaceRuleEnvironmentReceipt ruleEnvironment = BuildRuleEnvironmentReceipt(rulesetId, payload);",
        "RuleEnvironmentFingerprint: ruleEnvironment.Fingerprint,",
        "BannedWareGrades: ruleEnvironment.BannedWareGrades);",
    ],
    "test_program": [
        'string.Equals(filter, "parity-m143", StringComparison.OrdinalIgnoreCase)',
        "ExportPrintSupplementAndRuleEnvironmentReceiptsStayDeterministic();",
        '"family:sr6_supplements_designers_and_house_rules"',
        '"family:sheet_export_print_viewer_and_exchange"',
        "Workspace export and print exchange receipts should keep the same deterministic rule-environment fingerprint.",
    ],
    "tool_catalog_mstest": [
        "Assert.IsNotNull(response.Sr6SuccessorDeterministicReceipt);",
        'Assert.AreEqual("family:sr6_supplements_designers_and_house_rules", response.Sr6SuccessorDeterministicReceipt!.ParityFamilyId);',
        'Assert.AreEqual("governed", response.Sr6SuccessorDeterministicReceipt!.Sr6SupplementLanePosture);',
        'Assert.AreEqual("missing", response.Sr6SuccessorDeterministicReceipt.Sr6SupplementLanePosture);',
        "StringAssert.Contains(response.Sr6SuccessorLaneReceipt, \"Supplement posture is missing\");",
    ],
    "workspace_service_mstest": [
        "Assert.IsNotNull(export.Value?.ExchangeDeterministicReceipt);",
        'Assert.AreEqual("export", export.Value?.ExchangeDeterministicReceipt?.SurfaceKind);',
        'Assert.AreEqual("governed", export.Value?.ExchangeDeterministicReceipt?.RuleEnvironmentPosture);',
        'Assert.AreEqual("print", print.Value?.ExchangeDeterministicReceipt?.SurfaceKind);',
        "export.Value?.ExchangeDeterministicReceipt?.RuleEnvironmentFingerprint,",
        "print.Value?.ExchangeDeterministicReceipt?.RuleEnvironmentFingerprint);",
    ],
    "api_integration_mstest": [
        'Assert.IsNotNull(response["sr6SuccessorDeterministicReceipt"]);',
        'Assert.AreEqual("family:sr6_supplements_designers_and_house_rules", sr6SuccessorDeterministicReceipt?["parityFamilyId"]?.GetValue<string>());',
    ],
    "repo_verify": [
        "test -f docs/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.md",
        "test -f scripts/verify-next90-m143-export-print-supplement-rule-environment-receipts.py",
        "test -f tests/test_next90_m143_export_print_supplement_rule_environment_receipts.py",
        'CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m143 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false',
        "python3 tests/test_next90_m143_export_print_supplement_rule_environment_receipts.py",
        "python3 scripts/verify-next90-m143-export-print-supplement-rule-environment-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.generated.json --check",
    ],
    "docs": [
        PACKAGE_ID,
        str(FRONTIER_ID),
        "Sr6SuccessorLaneDeterministicReceipt",
        "WorkspaceExchangeDeterministicReceipt",
        "family:sr6_supplements_designers_and_house_rules",
        "family:sheet_export_print_viewer_and_exchange",
        "menu:open_for_printing",
        "menu:open_for_export",
        "menu:file_print_multiple",
        "workflow:sr6_supplements",
        "workflow:house_rules",
        "exactly one canonical package row in each staged queue root",
        "CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m143 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false",
    ],
}

CANONICAL_AUTHORITY_FILES = {
    "successor_registry": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml"),
        [
            "  - id: 143",
            "    title: Direct parity proof for print/export/exchange and SR6 supplements or house-rule workflows",
            "    - id: '143.2'",
            "      owner: chummer6-core",
            f"      title: {TITLE}",
        ],
    ),
    "successor_queue": (
        Path("/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Keep export, print, supplement, and rule-environment receipts deterministic enough to prove the workflows rather",
            f"  package_id: {PACKAGE_ID}",
            "  work_task_id: '143.2'",
            "  frontier_id: 2778308338",
            "  milestone_id: 143",
            "  repo: chummer6-core",
            "  - keep_export_print_supplement_and_rule_environment_receip:core",
        ],
    ),
    "design_successor_queue": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Keep export, print, supplement, and rule-environment receipts deterministic enough to prove the workflows rather",
            f"  package_id: {PACKAGE_ID}",
            "  work_task_id: '143.2'",
            "  frontier_id: 2778308338",
            "  milestone_id: 143",
            "  repo: chummer6-core",
            "  - keep_export_print_supplement_and_rule_environment_receip:core",
        ],
    ),
}

AUTHORITY_ROW_MARKERS = {
    "successor_registry": "    - id: '143.2'\n",
    "successor_queue": "- title: Keep export, print, supplement, and rule-environment receipts deterministic enough to prove the workflows rather\n",
    "design_successor_queue": "- title: Keep export, print, supplement, and rule-environment receipts deterministic enough to prove the workflows rather\n",
}

EXPECTED_AUTHORITY_ROW_COUNTS = {
    "successor_registry": 1,
    "successor_queue": 1,
    "design_successor_queue": 1,
}


@dataclass(frozen=True)
class ProofFileStatus:
    key: str
    path: Path
    exists: bool
    digest: str | None
    digest_scope: str
    missing_snippets: list[str]

    def to_json(self) -> dict[str, Any]:
        return {
            "key": self.key,
            "path": str(self.path),
            "exists": self.exists,
            "digest": self.digest,
            "digest_scope": self.digest_scope,
            "missing_snippets": self.missing_snippets,
            "status": "passed" if self.exists and not self.missing_snippets else "failed",
        }


def sha256_digest(text: str) -> str:
    return f"sha256:{hashlib.sha256(text.encode('utf-8')).hexdigest()}"


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def extract_authority_scope(key: str, content: str) -> tuple[str, str]:
    if key == "successor_registry":
        start_marker = "  - id: 143\n"
        next_marker = "  - id: 144\n"
        scope_label = "milestone-row"
    elif key in {"successor_queue", "design_successor_queue"}:
        scoped_rows = extract_queue_package_rows(content)
        if scoped_rows:
            return "\n".join(scoped_rows), "package-rows"
        return content, "full-file"
    else:
        return content, "full-file"

    start = content.find(start_marker)
    if start < 0:
        return content, "full-file"

    end = content.find(next_marker, start + len(start_marker))
    scoped = content[start:] if end < 0 else content[start:end]
    return scoped.rstrip(), scope_label


def extract_queue_package_rows(content: str) -> list[str]:
    rows: list[str] = []
    start_marker = AUTHORITY_ROW_MARKERS["successor_queue"]

    mode_index = content.find("\nmode:")
    if mode_index >= 0:
        rows.extend(extract_package_row_blocks(content[:mode_index], start_marker))

    items_index = content.find("\nitems:\n")
    if items_index >= 0:
        rows.extend(extract_package_row_blocks(content[items_index + 1 :], start_marker))

    return rows


def extract_package_row_blocks(content: str, start_marker: str) -> list[str]:
    blocks: list[str] = []
    search_start = 0
    while True:
        start = content.find(start_marker, search_start)
        if start < 0:
            break

        next_marker = content.find("\n- title:", start + len(start_marker))
        block = content[start:] if next_marker < 0 else content[start:next_marker]
        blocks.append(block.rstrip())
        search_start = start + len(start_marker)

    return blocks


def count_authority_rows(key: str, content: str) -> int:
    if key == "successor_registry":
        return content.count(AUTHORITY_ROW_MARKERS[key])

    if key in {"successor_queue", "design_successor_queue"}:
        return len(extract_queue_package_rows(content))

    return content.count(AUTHORITY_ROW_MARKERS[key])


def inspect_file(path: Path, key: str, required_snippets: list[str]) -> ProofFileStatus:
    if not path.exists():
        return ProofFileStatus(key=key, path=path, exists=False, digest=None, digest_scope="full-file", missing_snippets=required_snippets)

    content = read_text(path)
    if key in CANONICAL_AUTHORITY_FILES:
        scope_text, scope_label = extract_authority_scope(key, content)
    elif key == "repo_verify":
        start_marker = "test -f docs/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.md"
        end_marker = "test -f scripts/verify-next90-m114-rule-environment-studio.py"
        start = content.find(start_marker)
        end = content.find(end_marker, start + len(start_marker)) if start >= 0 else -1
        if start >= 0 and end > start:
            scope_text = content[start:end].rstrip()
            scope_label = "m143-verify-block"
        else:
            scope_text = content
            scope_label = "full-file"
    else:
        scope_text, scope_label = content, "full-file"

    missing_snippets = [snippet for snippet in required_snippets if snippet not in content]
    return ProofFileStatus(
        key=key,
        path=path,
        exists=True,
        digest=sha256_digest(scope_text),
        digest_scope=scope_label,
        missing_snippets=missing_snippets,
    )


def build_payload(repo_root: Path, out_path: Path) -> dict[str, Any]:
    proof_files = [
        inspect_file(repo_root / relative_path, key, REQUIRED_SNIPPETS.get(key, []))
        for key, relative_path in REQUIRED_FILES.items()
    ]
    authority_files = [
        inspect_file(path, key, snippets)
        for key, (path, snippets) in CANONICAL_AUTHORITY_FILES.items()
    ]

    authority_row_counts: dict[str, int] = {}
    authority_row_issues: dict[str, str] = {}
    for key, (path, _) in CANONICAL_AUTHORITY_FILES.items():
        if not path.exists():
            continue

        count = count_authority_rows(key, read_text(path))
        authority_row_counts[key] = count
        expected = EXPECTED_AUTHORITY_ROW_COUNTS[key]
        if count != expected:
            authority_row_issues[key] = f"expected {expected} canonical row(s), found {count}"

    all_files = proof_files + authority_files
    missing_files = [status.key for status in all_files if not status.exists]
    snippet_failures = {
        status.key: status.missing_snippets
        for status in all_files
        if status.exists and status.missing_snippets
    }
    passed = not missing_files and not snippet_failures and not authority_row_issues

    return {
        "generated_at": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
        "status": "passed" if passed else "failed",
        "package_id": PACKAGE_ID,
        "frontier_id": FRONTIER_ID,
        "milestone_id": MILESTONE_ID,
        "work_task_id": WORK_TASK_ID,
        "title": TITLE,
        "task": TASK,
        "repo": PACKAGE_REPO,
        "repo_root": str(repo_root.resolve()),
        "owned_surfaces": OWNED_SURFACES,
        "allowed_paths": ALLOWED_PATHS,
        "published_receipt_path": str((repo_root / ".codex-studio" / "published" / "NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.generated.json").resolve()),
        "receipt_path": str(out_path),
        "proof_anchor_count": len(proof_files),
        "authority_anchor_count": len(authority_files),
        "proof_files": [status.to_json() for status in proof_files],
        "authority_files": [status.to_json() for status in authority_files],
        "authority_row_counts": authority_row_counts,
        "expected_authority_row_counts": EXPECTED_AUTHORITY_ROW_COUNTS,
        "parity_family_ids": [
            "family:sheet_export_print_viewer_and_exchange",
            "family:sr6_supplements_designers_and_house_rules",
        ],
        "output_route_ids": [
            "menu:open_for_printing",
            "menu:open_for_export",
            "menu:file_print_multiple",
        ],
        "supplement_route_ids": [
            "workflow:sr6_supplements",
            "workflow:house_rules",
        ],
        "verification_commands": [
            "CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m143 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false",
            "python3 tests/test_next90_m143_export_print_supplement_rule_environment_receipts.py",
            "python3 scripts/verify-next90-m143-export-print-supplement-rule-environment-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.generated.json",
        ],
        "test_filter": "parity-m143",
        "unresolved": {
            "missing_files": missing_files,
            "snippet_failures": snippet_failures,
            "authority_row_issues": authority_row_issues,
        },
    }


def without_generated_at(payload: dict[str, Any]) -> dict[str, Any]:
    comparable = dict(payload)
    comparable.pop("generated_at", None)
    return comparable


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path("."))
    parser.add_argument("--out", type=Path, default=DEFAULT_OUTPUT_RELATIVE_PATH)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    repo_root = args.repo_root.resolve()
    out_path = args.out.resolve()
    out_path.parent.mkdir(parents=True, exist_ok=True)

    payload = build_payload(repo_root, out_path)

    if args.check and out_path.exists():
        checked_in = json.loads(out_path.read_text(encoding="utf-8"))
        if without_generated_at(payload) != without_generated_at(checked_in):
            print(f"checked-in receipt is stale: {out_path}")
            return 1

    out_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(out_path)
    return 0 if payload["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
