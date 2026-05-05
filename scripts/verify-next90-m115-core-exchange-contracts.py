#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PACKAGE_ID = "next90-m115-core-exchange-contracts"
FRONTIER_ID = 2537363316
MILESTONE_ID = 115
WORK_TASK_ID = "115.1"
TITLE = "Define portable dossier, replay, recap, and exchange contracts"
TASK = "Add lineage, compatibility, loss, provenance, and portability receipts for dossier, campaign, replay, recap, and external exchange outputs."
OWNED_SURFACES = ["portable_dossier_contracts", "replay_recap_exchange_contracts"]
ALLOWED_PATHS = ["src", "tests", "docs", "scripts"]
PACKAGE_REPO = "chummer6-core"
PACKAGE_REPO_ROOT = "/docker/chummercomplete/chummer-core-engine"
PUBLISHED_RECEIPT_PATH = "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json"
DEFAULT_OUTPUT_RELATIVE_PATH = Path(".codex-studio") / "published" / "NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json"

REQUIRED_FILES = {
    "contracts": Path("Chummer.Contracts/Workspaces/WorkspacePortabilityContracts.cs"),
    "workspace_service": Path("Chummer.Application/Workspaces/WorkspaceService.cs"),
    "core_engine_tests": Path("Chummer.CoreEngine.Tests/Program.cs"),
    "workspace_tests": Path("Chummer.Tests/WorkspaceServiceTests.cs"),
    "repo_verify": Path("scripts/ai/verify.sh"),
    "docs": Path("docs/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.md"),
    "receipt_test": Path("tests/test_next90_m115_core_exchange_contracts.py"),
    "receipt_verifier": Path("scripts/verify-next90-m115-core-exchange-contracts.py"),
}

REQUIRED_SNIPPETS = {
    "contracts": [
        'public const string CampaignBundleV1 = "chummer.campaign-bundle.v1";',
        'public const string ReplayTimelineV1 = "chummer.replay-timeline.v1";',
        'public const string SessionRecapV1 = "chummer.session-recap.v1";',
        'public const string ExternalExchangeV1 = "chummer.external-exchange.v1";',
        'public const string CampaignBundle = "campaign-bundle";',
        'public const string ReplayTimeline = "replay-timeline";',
        'public const string SessionRecap = "session-recap";',
        'public const string ExternalExchange = "external-exchange";',
        "public static class WorkspacePortabilityRevocationStates",
        "public sealed record WorkspacePortabilityRelatedOutputReceipt(",
        "WorkspacePortabilityRevocationReceipt Revocation);",
        "WorkspacePortabilityRevocationReceipt? Revocation = null,",
        "IReadOnlyList<WorkspacePortabilityRelatedOutputReceipt>? RelatedOutputs = null",
    ],
    "workspace_service": [
        "BuildRelatedOutputReceipt(",
        'outputKind: WorkspacePortabilityOutputKinds.CampaignBundle,',
        'workflowId: "workflow.campaign.bundle",',
        'outputKind: WorkspacePortabilityOutputKinds.ReplayTimeline,',
        'workflowId: "workflow.replay.timeline",',
        'outputKind: WorkspacePortabilityOutputKinds.SessionRecap,',
        'workflowId: "workflow.recap.session",',
        'outputKind: WorkspacePortabilityOutputKinds.ExternalExchange,',
        'workflowId: "workflow.exchange.external",',
        'formatId: WorkspacePortabilityFormatIds.CampaignBundleV1,',
        'formatId: WorkspacePortabilityFormatIds.ReplayTimelineV1,',
        'formatId: WorkspacePortabilityFormatIds.SessionRecapV1,',
        'formatId: WorkspacePortabilityFormatIds.ExternalExchangeV1,',
        "SourceArtifactId = receiptId,",
        "SourceFormatId = WorkspacePortabilityFormatIds.PortableDossierV1",
        "WorkspacePortabilityRevocationReceipt revocation = BuildRevocationReceipt(",
        "revocation: BuildRevocationReceipt(",
        "Revocation: revocation,",
        "WorkspacePortabilityRevocationReceipt revocation,",
        "State: WorkspacePortabilityRevocationStates.Revocable,",
        "RelatedOutputs: relatedOutputs",
    ],
    "workspace_tests": [
        "WorkspacePortabilityOutputKinds.NativeWorkspaceXml",
        "WorkspacePortabilityOutputKinds.PortableDossier",
        '"format-review-required"',
        '"native-workspace-review"',
        "Assert.AreEqual(5, result.Value.Portability?.RelatedOutputs?.Count);",
        "WorkspacePortabilityRevocationStates.Revocable",
        "\"workspace-portability:portable-dossier\"",
        "output.Revocation.State == WorkspacePortabilityRevocationStates.Revocable",
        "WorkspacePortabilityOutputKinds.CampaignBundle",
        "workflow.campaign.bundle",
        "workflow.replay.timeline",
        "workflow.recap.session",
        "workflow.exchange.external",
        "WorkspacePortabilityFormatIds.CampaignBundleV1",
        "WorkspacePortabilityFormatIds.ReplayTimelineV1",
        "WorkspacePortabilityFormatIds.SessionRecapV1",
        "WorkspacePortabilityFormatIds.ExternalExchangeV1",
        "output.Provenance.SourceArtifactId, packageId",
        "WorkspacePortabilityLossStates.BoundedLoss",
    ],
    "core_engine_tests": [
        'string.Equals(filter, "core-exchange-contracts", StringComparison.OrdinalIgnoreCase)',
        "WorkspaceExportPortabilityReceiptsCoverAllGovernedOutputLanes();",
        '"workflow.campaign.bundle"',
        '"workflow.replay.timeline"',
        '"workflow.recap.session"',
        '"workflow.exchange.external"',
        '"workspace-portability:external-exchange"',
    ],
    "repo_verify": [
        "test -f docs/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.md",
        "test -f scripts/verify-next90-m115-core-exchange-contracts.py",
        'CHUMMER_CORE_ENGINE_TEST_FILTER=core-exchange-contracts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false',
        "python3 tests/test_next90_m115_core_exchange_contracts.py",
        "python3 scripts/verify-next90-m115-core-exchange-contracts.py --repo-root . --out .codex-studio/published/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json --check",
    ],
    "docs": [
        PACKAGE_ID,
        "`formatId`",
        "`relatedOutputs`",
        "`revocation`",
        "`native-workspace-xml`",
        "`format-review-required`",
        "`native-workspace-review`",
        "campaign federation",
        "replay timeline",
        "session recap",
        "external exchange",
        "portable dossier package",
        ".codex-studio/published/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json",
        "python3 tests/test_next90_m115_core_exchange_contracts.py",
    ],
    "receipt_test": [
        'PACKAGE_ID = "next90-m115-core-exchange-contracts"',
        "NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json",
    ],
    "receipt_verifier": [
        'PACKAGE_ID = "next90-m115-core-exchange-contracts"',
        'FRONTIER_ID = 2537363316',
        'WORK_TASK_ID = "115.1"',
        'PUBLISHED_RECEIPT_PATH = "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json"',
    ],
}

AUTHORITY_FILES = {
        "successor_registry": (
            Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml"),
            [
                "  - id: 115",
                "    title: Portable dossier, campaign federation, replay, recap, and external exchange",
                "    status: complete",
                "      - id: 115.1",
                "        owner: chummer6-core",
                "        title: Define portable dossier, recap, replay, and exchange contracts with loss and compatibility receipts.",
            ],
        ),
    "successor_queue": (
        Path("/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Define portable dossier, replay, recap, and exchange contracts",
            "  task: Add lineage, compatibility, loss, provenance, and portability receipts for dossier, campaign, replay, recap, and external",
            "    exchange outputs.",
            f"  package_id: {PACKAGE_ID}",
            f"  work_task_id: {WORK_TASK_ID}",
            "  milestone_id: 115",
            "  status: complete",
            "  repo: chummer6-core",
            "  - portable_dossier_contracts",
            "  - replay_recap_exchange_contracts",
        ],
    ),
    "design_successor_queue": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Define portable dossier, replay, recap, and exchange contracts",
            "  task: Add lineage, compatibility, loss, provenance, and portability receipts for dossier, campaign, replay, recap, and external",
            "    exchange outputs.",
            f"  package_id: {PACKAGE_ID}",
            f"  work_task_id: {WORK_TASK_ID}",
            "  milestone_id: 115",
            "  status: complete",
            "  repo: chummer6-core",
            "  - portable_dossier_contracts",
            "  - replay_recap_exchange_contracts",
        ],
    ),
}


@dataclass(frozen=True)
class FileStatus:
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


def inspect_file(path: Path, key: str, required_snippets: list[str]) -> FileStatus:
    if not path.exists():
        return FileStatus(key=key, path=path, exists=False, digest=None, missing_snippets=required_snippets)

    content = read_text(path)
    missing_snippets = [snippet for snippet in required_snippets if snippet not in content]
    return FileStatus(
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
        for key, (path, snippets) in AUTHORITY_FILES.items()
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
            "CHUMMER_CORE_ENGINE_TEST_FILTER=core-exchange-contracts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false",
            "python3 tests/test_next90_m115_core_exchange_contracts.py",
            "python3 scripts/verify-next90-m115-core-exchange-contracts.py --repo-root . --out .codex-studio/published/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json",
        ],
        "covered_output_kinds": [
            "portable-dossier",
            "campaign-bundle",
            "replay-timeline",
            "session-recap",
            "external-exchange",
        ],
        "authority_expectations": {
            "registry_status": "complete",
            "queue_status": "complete",
            "design_queue_status": "complete",
            "allowed_paths": ALLOWED_PATHS,
            "owned_surfaces": OWNED_SURFACES,
        },
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
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
