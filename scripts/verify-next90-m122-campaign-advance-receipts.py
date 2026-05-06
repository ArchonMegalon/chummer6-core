#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PACKAGE_ID = "next90-m122-core-add-deterministic-reward-downtime-goal-update-and-conseq"
FRONTIER_ID = 1771239378
MILESTONE_ID = 122
WORK_TASK_ID = "122.2"
TITLE = "Add deterministic reward, downtime, goal-update, and consequence receipt contracts consumed by adoption and BLACK LEDGER flows."
TASK = TITLE
OWNED_SURFACES = ["add_deterministic_reward_downtime_goal:core"]
ALLOWED_PATHS = ["src", "tests", "docs", "scripts"]
PACKAGE_REPO = "chummer6-core"
PACKAGE_REPO_ROOT = "/docker/chummercomplete/chummer-core-engine"
PUBLISHED_RECEIPT_PATH = "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.generated.json"
DEFAULT_OUTPUT_RELATIVE_PATH = Path(".codex-studio") / "published" / "NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.generated.json"

REQUIRED_FILES = {
    "contracts": Path("Chummer.Contracts/Campaign/CampaignAdvanceReceiptContracts.cs"),
    "service": Path("Chummer.Application/Campaign/DefaultCampaignAdvanceReceiptService.cs"),
    "service_interface": Path("Chummer.Application/Campaign/ICampaignAdvanceReceiptService.cs"),
    "dependency_injection": Path("Chummer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs"),
    "test_program": Path("Chummer.CoreEngine.Tests/Program.cs"),
    "mstest": Path("Chummer.Tests/CampaignAdvanceReceiptServiceTests.cs"),
    "compliance": Path("Chummer.Tests/Compliance/MigrationComplianceTests.cs"),
    "repo_verify": Path("scripts/ai/verify.sh"),
    "docs": Path("docs/NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.md"),
}

REQUIRED_SNIPPETS = {
    "contracts": [
        "public static class CampaignAdvanceReceiptFamilies",
        'public const string AdoptionRunnerGoalAndBlackLedger = "family:campaign_adoption_runner_goal_and_black_ledger";',
        "public static class CampaignAdoptionConfidencePostures",
        "public sealed record CampaignAdoptionConfidenceReceipt(",
        "public sealed record RunnerRewardReceipt(",
        "public sealed record DowntimeAllocationReceipt(",
        "public sealed record RunnerGoalUpdateReceipt(",
        "public sealed record BlackLedgerConsequenceReceipt(",
        "public sealed record CampaignAdvanceDeterministicReceipt(",
        'public const string PlayerSafe = "player-safe";',
    ],
    "service": [
        "CampaignAdoptionConfidenceReceipt adoption = BuildAdoptionReceipt(",
        "DowntimeAllocationReceipt downtime = BuildDowntimeReceipt(",
        "RunnerGoalUpdateReceipt goal = BuildGoalReceipt(",
        "BuildRunSummarySeed(",
        "BuildShadowfeedSeed(",
        "ComputeDowntimeProgression(",
        "ComputeFactionResponseSeed(",
        "CampaignAdvanceReceiptFamilies.AdoptionRunnerGoalAndBlackLedger",
        "world-tick",
        "news-item",
        "BlackLedgerConsequenceAudiences.PlayerSafe",
    ],
    "service_interface": [
        "public interface ICampaignAdvanceReceiptService",
        "CampaignAdvanceReceiptBundle Build(CampaignAdvanceReceiptInput input);",
    ],
    "dependency_injection": [
        "AddSingleton<IAestheticDigestService, DefaultAestheticDigestService>();",
        "AddSingleton<ISemanticSeedService, DefaultSemanticSeedService>();",
        "AddSingleton<IRelationshipHeatService, DefaultRelationshipHeatService>();",
        "AddSingleton<ICampaignAdvanceReceiptService, DefaultCampaignAdvanceReceiptService>();",
    ],
    "test_program": [
        'string.Equals(filter, "next90-m122-campaign-advance-receipts", StringComparison.OrdinalIgnoreCase)',
        "CampaignAdvanceReceiptsStayDeterministic();",
        "CampaignAdvanceReceiptsDowngradeWhenAdoptionOrSpoilerStateDrifts();",
        "CampaignAdvanceReceiptsStayReviewRequiredWhenConflictsRemain();",
        "CampaignAdvanceReceiptsMarkGoalAchievedWhenRewardAndDowntimeFinishProgress();",
        "family:campaign_adoption_runner_goal_and_black_ledger",
        "news-item:shadowfeed-2048:rr-2048",
    ],
    "mstest": [
        "Build_emits_governed_campaign_adoption_goal_and_black_ledger_receipts",
        "Build_downgrades_adoption_and_consequence_when_runner_mapping_or_spoiler_state_drifts",
        "Build_marks_adoption_and_consequence_review_required_when_conflicts_remain_without_spoilers",
        "Build_marks_goal_achieved_when_reward_and_downtime_finish_progress",
        'Assert.AreEqual(BlackLedgerConsequenceAudiences.PlayerSafe, result.Consequence.Audience);',
        "Assert.AreEqual(CampaignAdvanceReceiptFamilies.AdoptionRunnerGoalAndBlackLedger, result.DeterministicReceipt.ParityFamilyId);",
        'Assert.AreEqual(RunnerGoalUpdateStates.Achieved, result.Goal.State);',
    ],
    "compliance": [
        "Campaign_advance_receipt_contracts_lock_in_adoption_goal_and_black_ledger_semantics",
        "CampaignAdvanceReceiptContracts.cs",
        "BuildRunSummarySeed",
        "ComputeFactionResponseSeed",
        "BlackLedgerConsequenceAudiences.PlayerSafe",
    ],
    "repo_verify": [
        "test -f docs/NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.md",
        "test -f scripts/verify-next90-m122-campaign-advance-receipts.py",
        "test -f tests/test_next90_m122_campaign_advance_receipts.py",
        "CHUMMER_CORE_ENGINE_TEST_FILTER=next90-m122-campaign-advance-receipts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false",
        "python3 tests/test_next90_m122_campaign_advance_receipts.py",
        "python3 scripts/verify-next90-m122-campaign-advance-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.generated.json --check",
    ],
    "docs": [
        PACKAGE_ID,
        "CampaignAdoptionConfidenceReceipt",
        "RunnerGoalUpdateReceipt",
        "BlackLedgerConsequenceReceipt",
        "review-required adoption",
        "world-tick",
        "player-safe news item",
        "CHUMMER_CORE_ENGINE_TEST_FILTER=next90-m122-campaign-advance-receipts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false",
    ],
}

CANONICAL_AUTHORITY_FILES = {
    "successor_registry": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml"),
        [
            "  - id: 122",
            "    title: Campaign adoption, runner goals, and first BLACK LEDGER consequence",
            "    - id: '122.2'",
            "      owner: chummer6-core",
            "      title: Add deterministic reward, downtime, goal-update, and consequence receipt contracts consumed by adoption and BLACK",
        ],
    ),
    "successor_queue": (
        Path("/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Add deterministic reward, downtime, goal-update, and consequence receipt contracts consumed by adoption and BLACK",
            "  package_id: next90-m122-core-add-deterministic-reward-downtime-goal-update-and-conseq",
            "  work_task_id: '122.2'",
            "  frontier_id: 1771239378",
            "  milestone_id: 122",
            "  repo: chummer6-core",
            "  - add_deterministic_reward_downtime_goal:core",
        ],
    ),
    "design_successor_queue": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Add deterministic reward, downtime, goal-update, and consequence receipt contracts consumed by adoption and BLACK",
            "  package_id: next90-m122-core-add-deterministic-reward-downtime-goal-update-and-conseq",
            "  work_task_id: '122.2'",
            "  frontier_id: 1771239378",
            "  milestone_id: 122",
            "  repo: chummer6-core",
            "  - add_deterministic_reward_downtime_goal:core",
        ],
    ),
}

AUTHORITY_ROW_MARKERS = {
    "successor_registry": "    - id: '122.2'\n",
    "successor_queue": "- title: Add deterministic reward, downtime, goal-update, and consequence receipt contracts consumed by adoption and BLACK\n",
    "design_successor_queue": "- title: Add deterministic reward, downtime, goal-update, and consequence receipt contracts consumed by adoption and BLACK\n",
}

EXPECTED_AUTHORITY_ROW_COUNTS = {
    "successor_registry": 1,
    "successor_queue": 2,
    "design_successor_queue": 2,
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
        start_marker = "  - id: 122\n"
        next_marker = "  - id: 123\n"
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
        marker = AUTHORITY_ROW_MARKERS[key]
        scoped, _ = extract_authority_scope(key, content)
        return scoped.count(marker)

    if key in {"successor_queue", "design_successor_queue"}:
        return len(extract_queue_package_rows(content))

    marker = AUTHORITY_ROW_MARKERS.get(key)
    return 0 if marker is None else content.count(marker)


def inspect_file(path: Path, key: str, required_snippets: list[str]) -> ProofFileStatus:
    if not path.exists():
        return ProofFileStatus(
            key=key,
            path=path,
            exists=False,
            digest=None,
            digest_scope="missing",
            missing_snippets=required_snippets,
        )

    content = read_text(path)
    missing_snippets = [snippet for snippet in required_snippets if snippet not in content]
    digest_content, digest_scope = extract_authority_scope(key, content)
    return ProofFileStatus(
        key=key,
        path=path,
        exists=True,
        digest=sha256_digest(digest_content),
        digest_scope=digest_scope,
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
    authority_row_counts = {
        key: count_authority_rows(key, read_text(path))
        for key, (path, _) in CANONICAL_AUTHORITY_FILES.items()
    }
    authority_row_issues = {
        key: count
        for key, count in authority_row_counts.items()
        if count != EXPECTED_AUTHORITY_ROW_COUNTS.get(key, 1)
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
        "repo_root": PACKAGE_REPO_ROOT,
        "owned_surfaces": OWNED_SURFACES,
        "allowed_paths": ALLOWED_PATHS,
        "published_receipt_path": PUBLISHED_RECEIPT_PATH,
        "receipt_path": str(out_path),
        "proof_anchor_count": len(proof_files),
        "authority_anchor_count": len(authority_files),
        "proof_files": [status.to_json() for status in proof_files],
        "authority_files": [status.to_json() for status in authority_files],
        "authority_row_counts": authority_row_counts,
        "verification_commands": [
            "CHUMMER_CORE_ENGINE_TEST_FILTER=next90-m122-campaign-advance-receipts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false",
            "python3 tests/test_next90_m122_campaign_advance_receipts.py",
            "python3 scripts/verify-next90-m122-campaign-advance-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.generated.json",
        ],
        "receipt_family": "family:campaign_adoption_runner_goal_and_black_ledger",
        "world_tick_id": "world-tick:campaign-7:rr-2048",
        "news_item_id": "news-item:shadowfeed-2048:rr-2048",
        "test_filter": "next90-m122-campaign-advance-receipts",
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
        checked_in_payload = json.loads(out_path.read_text(encoding="utf-8"))
        if without_generated_at(payload) != without_generated_at(checked_in_payload):
            print(f"checked-in receipt is stale: {out_path}")
            return 1

    out_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(out_path)
    return 0 if payload["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
