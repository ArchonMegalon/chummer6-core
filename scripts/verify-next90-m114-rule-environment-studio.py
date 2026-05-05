#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PACKAGE_ID = "next90-m114-core-rule-environment-studio"
FRONTIER_ID = 1542103988
MILESTONE_ID = 114
TITLE = "Publish rule-environment studio contracts and explain receipts"
TASK = "Build amend-package lifecycle, promotion, diff, and explain-receipt contracts for downstream UI and support surfaces."
OWNED_SURFACES = ["rule_environment_studio", "explain_receipts:engine"]
ALLOWED_PATHS = ["src", "tests", "docs", "scripts"]
PACKAGE_REPO = "chummer6-core"
PACKAGE_REPO_ROOT = "/docker/chummercomplete/chummer-core-engine"
PUBLISHED_RECEIPT_PATH = "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/NEXT90_M114_RULE_ENVIRONMENT_STUDIO.generated.json"
DEFAULT_OUTPUT_RELATIVE_PATH = Path(".codex-studio") / "published" / "NEXT90_M114_RULE_ENVIRONMENT_STUDIO.generated.json"

REQUIRED_FILES = {
    "contracts": Path("Chummer.Contracts/Content/RuleEnvironmentStudioContracts.cs"),
    "service": Path("Chummer.Application/Content/DefaultRuleEnvironmentStudioService.cs"),
    "service_interface": Path("Chummer.Application/Content/IRuleEnvironmentStudioService.cs"),
    "di": Path("Chummer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs"),
    "test_program": Path("Chummer.CoreEngine.Tests/Program.cs"),
    "repo_verify": Path("scripts/ai/verify.sh"),
    "docs": Path("docs/NEXT90_M114_RULE_ENVIRONMENT_STUDIO_CONTRACTS.md"),
}

REQUIRED_SNIPPETS = {
    "contracts": [
        'public const string FirstPin = "first-pin";',
        'public const string Clear = "clear";',
        'public const string RequiresReview = "requires-review";',
        'public const string Engine = "engine";',
        "public sealed record RuleEnvironmentStudioLifecycleProjection(",
        "public sealed record RuleEnvironmentStudioDiffProjection(",
        "RuntimeLockDiffProjection? Delta = null",
        "public sealed record RuleEnvironmentStudioExplainReceiptProjection(",
        "string DiffStatus,",
        "string CurrentStage,",
        "string PromotionTargetStage,",
        "IReadOnlyList<string> RequiredCoverageKinds",
        "public sealed record RuleEnvironmentStudioProjection(",
    ],
    "service": [
        "public sealed class DefaultRuleEnvironmentStudioService : IRuleEnvironmentStudioService",
        "FindCurrentRuntime(",
        "BuildDiff(",
        "RuleEnvironmentStudioDiffStatuses.FirstPin",
        "RuleEnvironmentStudioDiffStatuses.Clear",
        "RuleEnvironmentStudioDiffStatuses.RequiresReview",
        "BuildLifecycle(",
        "BuildExplainReceipt(",
        "CalculationReportPrivacyModes.SupportCase",
        "DiffStatus: diff.Status",
        "CurrentStage: lifecycle.CurrentStage",
        "PromotionTargetStage: lifecycle.PromotionTargetStage",
        "ExplainValuePacketCoverageKinds.BeforeAfterDelta",
        "ExplainValuePacketCoverageKinds.MechanicalResult",
        "ExplainValuePacketCoverageKinds.SourceAnchor",
        "ExplainValuePacketCoverageKinds.Warning",
        "DefaultExplainValuePacketService.MaxCounterfactuals",
    ],
    "service_interface": [
        "public interface IRuleEnvironmentStudioService",
        "RuleEnvironmentStudioProjection? GetProfileProjection(",
    ],
    "di": [
        "services.AddSingleton<IRuleEnvironmentStudioService, DefaultRuleEnvironmentStudioService>();",
    ],
    "test_program": [
        'string.Equals(filter, "rule-environment-studio", StringComparison.OrdinalIgnoreCase)',
        "RuntimeLockDiffIsDeterministicAndParameterized();",
        "RuleEnvironmentStudioProjectionPublishesLifecycleDiffAndExplainContracts();",
        "RuleEnvironmentStudioProjectionPublishesFirstPinGuidance();",
        "RuleEnvironmentStudioProjectionPublishesClearGuidanceWhenRuntimeIsAlreadyPinned();",
        "RuleEnvironmentStudioProjectionPublishesExplainReceiptFloorWithoutWarnings();",
        "CreateRuleEnvironmentStudioService(",
        "RuleEnvironmentStudioDiffStatuses.FirstPin",
        "RuleEnvironmentStudioDiffStatuses.Clear",
        "RuleEnvironmentStudioDiffStatuses.RequiresReview",
    ],
    "repo_verify": [
        "test -f docs/NEXT90_M114_RULE_ENVIRONMENT_STUDIO_CONTRACTS.md",
        "CHUMMER_CORE_ENGINE_TEST_FILTER=rule-environment-studio dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false",
        "python3 tests/test_next90_m114_rule_environment_studio.py",
        "python3 scripts/verify-next90-m114-rule-environment-studio.py --repo-root . --out .codex-studio/published/NEXT90_M114_RULE_ENVIRONMENT_STUDIO.generated.json --check",
    ],
    "docs": [
        PACKAGE_ID,
        "deterministic first-pin, clear, and requires-review diff states",
        "explain-receipt lifecycle and diff-state fields so support and UI flows can cite promotion posture without rejoining the surrounding projection locally",
        "matching no-warning runtimes collapse explain requirements to the mechanical-result plus source-anchor floor",
        "`scripts/ai/verify.sh` now runs the focused rule-environment contract lane",
        "CHUMMER_CORE_ENGINE_TEST_FILTER=rule-environment-studio dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false",
        "python3 tests/test_next90_m114_rule_environment_studio.py",
        "python3 scripts/verify-next90-m114-rule-environment-studio.py --repo-root . --out .codex-studio/published/NEXT90_M114_RULE_ENVIRONMENT_STUDIO.generated.json",
    ],
}

CANONICAL_AUTHORITY_FILES = {
    "successor_registry": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml"),
        [
            "  - id: 114",
            "    title: Rule-environment studio and explain receipts everywhere",
            "      - id: 114.1",
            "        owner: chummer6-core",
            "        title: Publish rule-environment studio contracts for amend-package lifecycle, promotion, diff, and explain receipts.",
        ],
    ),
    "successor_queue": (
        Path("/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Publish rule-environment studio contracts and explain receipts",
            "  task: Build amend-package lifecycle, promotion, diff, and explain-receipt contracts for downstream UI and support surfaces.",
            f"  package_id: {PACKAGE_ID}",
            "  work_task_id: 114.1",
            "  milestone_id: 114",
            "  repo: chummer6-core",
            "  - rule_environment_studio",
            "  - explain_receipts:engine",
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
        "verification_commands": [
            "CHUMMER_CORE_ENGINE_TEST_FILTER=rule-environment-studio dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false",
            "python3 tests/test_next90_m114_rule_environment_studio.py",
            "python3 scripts/verify-next90-m114-rule-environment-studio.py --repo-root . --out .codex-studio/published/NEXT90_M114_RULE_ENVIRONMENT_STUDIO.generated.json",
        ],
        "diff_statuses": ["first-pin", "clear", "requires-review"],
        "explain_receipt_contract_fields": [
            "currentStage",
            "diffStatus",
            "promotionTargetStage",
        ],
        "required_explain_coverage_kinds": [
            "mechanical-result",
            "source-anchor",
            "warning",
            "before-after-delta",
        ],
        "test_filter": "rule-environment-studio",
        "unresolved": {
            "missing_files": missing_files,
            "snippet_failures": snippet_failures,
        },
    }


def without_generated_at(payload: dict[str, Any]) -> dict[str, Any]:
    comparable = dict(payload)
    comparable.pop("generated_at", None)
    return comparable


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--out", type=Path, default=None)
    parser.add_argument("--check", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    repo_root = args.repo_root.resolve()
    out_path = args.out.resolve() if args.out is not None else (repo_root / DEFAULT_OUTPUT_RELATIVE_PATH).resolve()
    payload = build_payload(repo_root, out_path)

    if args.check:
        if not out_path.exists():
            print(f"missing receipt: {out_path}")
            return 1

        existing = json.loads(read_text(out_path))
        if without_generated_at(existing) != without_generated_at(payload):
            print(f"checked-in receipt is stale: {out_path}")
            return 1

        print(out_path)
        return 0

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(out_path)
    return 0 if payload["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
