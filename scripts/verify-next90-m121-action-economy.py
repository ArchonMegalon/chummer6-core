#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PACKAGE_ID = "next90-m121-core-implement-actionbudgetresult-actionaffordance-turnledger"
FRONTIER_ID = 1122632660
MILESTONE_ID = 121
WORK_TASK_ID = "121.1"
TITLE = "Implement ActionBudgetResult, ActionAffordance, TurnLedgerDelta, and source-anchor receipts for the first SR6 combat-round proof."
TASK = "Implement ActionBudgetResult, ActionAffordance, TurnLedgerDelta, and source-anchor receipts for the first SR6 combat-round proof."
OWNED_SURFACES = ["implement_actionbudgetresult_actionaffordance_turnledger:core"]
ALLOWED_PATHS = ["src", "tests", "docs", "scripts"]
PACKAGE_REPO = "chummer6-core"
SCRIPT_DIR = Path(__file__).resolve().parent
PACKAGE_REPO_ROOT = str(SCRIPT_DIR.parent)
PUBLISHED_RECEIPT_PATH = str((SCRIPT_DIR.parent / ".codex-studio" / "published" / "NEXT90_M121_ACTION_ECONOMY.generated.json").resolve())
DEFAULT_OUTPUT_RELATIVE_PATH = Path(".codex-studio") / "published" / "NEXT90_M121_ACTION_ECONOMY.generated.json"

REQUIRED_FILES = {
    "contracts": Path("Chummer.Contracts/Session/SessionActionBudgetContracts.cs"),
    "service": Path("Chummer.Application/Session/DefaultSessionActionBudgetService.cs"),
    "service_interface": Path("Chummer.Application/Session/ISessionActionBudgetService.cs"),
    "test_program": Path("Chummer.CoreEngine.Tests/Program.cs"),
    "mstest": Path("Chummer.Tests/SessionActionBudgetServiceTests.cs"),
    "repo_verify": Path("scripts/ai/verify.sh"),
    "docs": Path("docs/NEXT90_M121_ACTION_ECONOMY_CONTRACTS.md"),
}

REQUIRED_SNIPPETS = {
    "contracts": [
        "public static class SessionTurnLedgerDeltaStates",
        'public const string Previewable = "previewable";',
        "SourceAnchor? SourceAnchor = null",
        "public sealed record SessionTurnLedgerDelta(",
        "IReadOnlyList<string> TurnLedgerDeltaIds,",
        "int SourceAnchorReceiptCount,",
        "int MissingSourceAnchorReceiptCount,",
        "IReadOnlyList<SessionTurnLedgerDelta> TurnLedger,",
    ],
    "service": [
        "SessionTurnLedgerDelta[] turnLedger = BuildTurnLedger(",
        "SourceAnchor = NormalizeSourceAnchor(receipt.SourceAnchor)",
        'SourceAnchorRef: "sr6_core_major_actions"',
        'SourceAnchorRef: "sr6_core_full_defense"',
        "missingTurnLedgerReceiptSourceAnchorCount = turnLedger.Count(",
        "HasMissingRequiredReceiptSourceAnchors(delta)",
        "private static SessionTurnLedgerDelta[] BuildTurnLedger(",
        'BuildTurnLedgerDeltaId("convert-four-minor-to-anytime-major"',
        "ResolveReceiptSourceAnchors(",
        "MissingSourceAnchorReceiptCount: missingSourceAnchorReceiptCount,",
        "GetPreferredReceiptSourceAnchors(actionKey)",
        "private static SourceAnchor BuildDefaultSourceAnchor(",
    ],
    "service_interface": [
        "public interface ISessionActionBudgetService",
        "SessionActionBudgetResult Compute(SessionActionBudgetInput input);",
    ],
    "test_program": [
        'string.Equals(filter, "next90-m121-action-economy", StringComparison.OrdinalIgnoreCase)',
        "Sr6CombatRoundActionEconomyPublishesAnchoredTurnLedgerProof();",
        "Sr6CombatRoundActionEconomyDowngradesWhenSourceAnchorReceiptsGoMissing();",
        "Sr6CombatRoundActionEconomyDowngradesWhenRequiredTurnLedgerAnchorsAreMissing();",
        "turn-ledger-on-turn-convert-four-minor-to-anytime-major",
        "MissingSourceAnchorReceiptCount,",
    ],
    "mstest": [
        "Compute_marks_full_defense_available_when_four_minor_actions_remain",
        'delta.ActionKey == "convert-four-minor-to-anytime-major"',
        'Assert.AreEqual("stale", result.DeterministicReceipt!.ActionBudgetPosture);',
        "SourceAnchorReceiptCount",
        "MissingSourceAnchorReceiptCount",
        "Compute_downgrades_deterministic_receipt_when_required_turn_ledger_source_anchor_refs_are_missing",
    ],
    "repo_verify": [
        "test -f docs/NEXT90_M121_ACTION_ECONOMY_CONTRACTS.md",
        "test -f scripts/verify-next90-m121-action-economy.py",
        "test -f tests/test_next90_m121_action_economy.py",
        "CHUMMER_CORE_ENGINE_TEST_FILTER=next90-m121-action-economy dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false",
        "python3 tests/test_next90_m121_action_economy.py",
        "python3 scripts/verify-next90-m121-action-economy.py --repo-root . --out .codex-studio/published/NEXT90_M121_ACTION_ECONOMY.generated.json --check",
    ],
    "docs": [
        PACKAGE_ID,
        "SessionTurnLedgerDelta",
        "turnLedgerDeltaIds",
        "sourceAnchorReceiptCount",
        "convert-four-minor-to-anytime-major",
        "CHUMMER_CORE_ENGINE_TEST_FILTER=next90-m121-action-economy dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false",
    ],
}

CANONICAL_AUTHORITY_FILES = {
    "successor_registry": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml"),
        [
            "  - id: 121",
            "    title: Live action economy, source anchors, and GM Runboard",
            "    - id: '121.1'",
            "      owner: chummer6-core",
            "      title: Implement ActionBudgetResult, ActionAffordance, TurnLedgerDelta, and source-anchor receipts for the first SR6 combat-round",
        ],
    ),
    "successor_queue": (
        Path("/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Implement ActionBudgetResult, ActionAffordance, TurnLedgerDelta, and source-anchor receipts for the first SR6 combat-round",
            "  package_id: next90-m121-core-implement-actionbudgetresult-actionaffordance-turnledger",
            "  work_task_id: '121.1'",
            "  frontier_id: 1122632660",
            "  milestone_id: 121",
            "  repo: chummer6-core",
            "  - implement_actionbudgetresult_actionaffordance_turnledger:core",
        ],
    ),
    "design_successor_queue": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Implement ActionBudgetResult, ActionAffordance, TurnLedgerDelta, and source-anchor receipts for the first SR6 combat-round",
            "  package_id: next90-m121-core-implement-actionbudgetresult-actionaffordance-turnledger",
            "  work_task_id: '121.1'",
            "  frontier_id: 1122632660",
            "  milestone_id: 121",
            "  repo: chummer6-core",
            "  - implement_actionbudgetresult_actionaffordance_turnledger:core",
        ],
    ),
}


@dataclass(frozen=True)
class ProofFileStatus:
    key: str
    path: Path
    exists: bool
    digest: str | None
    missing_snippets: list[str]

    def to_json(self) -> dict[str, Any]:
        return {
            "key": self.key,
            "path": str(self.path),
            "exists": self.exists,
            "digest": self.digest,
            "missing_snippets": self.missing_snippets,
            "status": "passed" if self.exists and not self.missing_snippets else "failed",
        }


def sha256_digest(text: str) -> str:
    return f"sha256:{hashlib.sha256(text.encode('utf-8')).hexdigest()}"


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def inspect_file(path: Path, key: str, required_snippets: list[str]) -> ProofFileStatus:
    if not path.exists():
        return ProofFileStatus(key=key, path=path, exists=False, digest=None, missing_snippets=required_snippets)

    content = read_text(path)
    missing_snippets = [snippet for snippet in required_snippets if snippet not in content]
    return ProofFileStatus(
        key=key,
        path=path,
        exists=True,
        digest=sha256_digest(content),
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

    all_files = proof_files + authority_files
    missing_files = [status.key for status in all_files if not status.exists]
    snippet_failures = {
        status.key: status.missing_snippets
        for status in all_files
        if status.exists and status.missing_snippets
    }
    passed = not missing_files and not snippet_failures

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
        "published_receipt_path": str((repo_root / ".codex-studio" / "published" / "NEXT90_M121_ACTION_ECONOMY.generated.json").resolve()),
        "receipt_path": str(out_path),
        "proof_anchor_count": len(proof_files),
        "authority_anchor_count": len(authority_files),
        "proof_files": [status.to_json() for status in proof_files],
        "authority_files": [status.to_json() for status in authority_files],
        "verification_commands": [
            "CHUMMER_CORE_ENGINE_TEST_FILTER=next90-m121-action-economy dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false",
            "python3 tests/test_next90_m121_action_economy.py",
            "python3 scripts/verify-next90-m121-action-economy.py --repo-root . --out .codex-studio/published/NEXT90_M121_ACTION_ECONOMY.generated.json",
        ],
        "first_combat_round_actions": [
            "take-major-action",
            "take-minor-action",
            "full-defense",
            "convert-four-minor-to-anytime-major",
        ],
        "source_anchor_receipts": [
            "sr6_core_major_actions",
            "sr6_core_minor_actions",
            "sr6_core_full_defense",
            "sr6_core_anytime_major_conversion",
        ],
        "test_filter": "next90-m121-action-economy",
        "unresolved": {
            "missing_files": missing_files,
            "snippet_failures": snippet_failures,
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
