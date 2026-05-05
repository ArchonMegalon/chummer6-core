#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PACKAGE_ID = "next90-m113-core-opposition-packet-contracts"
FRONTIER_ID = 2325506990
MILESTONE_ID = 113
WORK_TASK_ID = "113.2"
TITLE = "Define opposition and scene packet contracts with rules-backed stats"
TASK = "Define opposition and scene packet contracts with rules-backed stats and bounded-loss receipts."
OWNED_SURFACES = ["gm_prep_packets", "opposition_contracts"]
ALLOWED_PATHS = ["src", "tests", "docs", "scripts"]
PACKAGE_REPO = "chummer6-core"
PACKAGE_REPO_ROOT = "/docker/chummercomplete/chummer-core-engine"
PUBLISHED_RECEIPT_PATH = "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/OPPOSITION_PACKET_CONTRACTS.generated.json"
DEFAULT_OUTPUT_RELATIVE_PATH = Path(".codex-studio") / "published" / "OPPOSITION_PACKET_CONTRACTS.generated.json"

REQUIRED_FILES = {
    "contracts": Path("Chummer.Contracts/Campaign/OppositionPacketContracts.cs"),
    "service": Path("Chummer.Application/Campaign/DefaultOppositionPacketContractService.cs"),
    "service_interface": Path("Chummer.Application/Campaign/IOppositionPacketContractService.cs"),
    "tests": Path("Chummer.CoreEngine.Tests/OppositionPacketContractContractTests.cs"),
    "test_program": Path("Chummer.CoreEngine.Tests/Program.cs"),
    "service_registration": Path("Chummer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs"),
    "docs": Path("docs/OPPOSITION_PACKET_CONTRACTS.md"),
}

AUTHORITY_FILES = {
    "successor_registry": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml"),
        [
            "  - id: 113",
            "    title: Crew, roster, opposition packets, and GM prep library",
            "      - id: 113.2",
            "        owner: chummer6-core",
            "        title: Define opposition and scene packet contracts with rules-backed stats and bounded-loss receipts.",
        ],
    ),
    "successor_queue": (
        Path("/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Define opposition and scene packet contracts with rules-backed stats",
            "  task: Define opposition and scene packet contracts with rules-backed stats and bounded-loss receipts.",
            f"  package_id: {PACKAGE_ID}",
            f"  work_task_id: {WORK_TASK_ID}",
            "  milestone_id: 113",
            "  status: complete",
            "  repo: chummer6-core",
            "  - gm_prep_packets",
            "  - opposition_contracts",
        ],
    ),
    "design_successor_queue": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Define opposition and scene packet contracts with rules-backed stats",
            "  task: Define opposition and scene packet contracts with rules-backed stats and bounded-loss receipts.",
            f"  package_id: {PACKAGE_ID}",
            f"  work_task_id: {WORK_TASK_ID}",
            "  milestone_id: 113",
            "  status: complete",
            "  repo: chummer6-core",
            "  - gm_prep_packets",
            "  - opposition_contracts",
        ],
    ),
}

REQUIRED_SNIPPETS = {
    "contracts": [
        "public sealed record GmPrepPacketBoundedLossReceipt(",
        "public sealed record GmPrepPacketRuleStat(",
        "public sealed record OppositionPacketContract(",
        "public sealed record ScenePacketContract(",
        "IReadOnlyList<GmPrepPacketRuleStat> PacketStats,",
        "string? SourcePacketId = null,",
        "int? RuleStatCount = null,",
    ],
    "service": [
        "CreateEntryReceipt",
        "CreateAggregateReceipt",
        "BuildScenePacket",
        "BuildOppositionPacket",
        "RulesetEvidencePointerKinds.RuleReference",
        "BuildPacketStats(",
        "gm-prep.packet.contracts.aggregate",
    ],
    "service_interface": [
        "IReadOnlyList<OppositionPacketContract> ListOppositionPackets",
        "IReadOnlyList<ScenePacketContract> ListScenePackets",
    ],
    "tests": [
        'service.GetOppositionPacket(OwnerScope.LocalSingleUser, "red-samurai", RulesetDefaults.Sr5)',
        'service.GetOppositionPacket(OwnerScope.LocalSingleUser, "renraku-security", RulesetDefaults.Sr5)',
        'service.GetScenePacket(OwnerScope.LocalSingleUser, "renraku-checkpoint", RulesetDefaults.Sr5)',
        "GmPrepPacketBoundedLossPostures.ReviewRequired",
        "runtime-fingerprint-missing",
        "runtime-fingerprint-mixed",
        "missing-entry:missing-scout",
        "PacketStats.Any",
        "RuntimeBoundStatCount",
        "SourcePacketId",
    ],
    "test_program": [
        "OppositionPacketContractContractTests.Run();",
    ],
    "service_registration": [
        "services.AddSingleton<IOppositionPacketContractService, DefaultOppositionPacketContractService>();",
    ],
    "docs": [
        PACKAGE_ID,
        str(FRONTIER_ID),
        "python3 tests/test_opposition_packet_contract_receipt.py",
        "CHUMMER_CORE_ENGINE_TEST_FILTER=opposition-packet-contracts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj",
        "python3 scripts/verify-opposition-packet-contracts.py --repo-root . --out .codex-studio/published/OPPOSITION_PACKET_CONTRACTS.generated.json",
        "packet-level peak stats",
        "receipt context",
    ],
}


@dataclass(frozen=True)
class ProofFileStatus:
    key: str
    relative_path: Path
    exists: bool
    digest: str | None
    missing_snippets: list[str]

    def to_json(self) -> dict[str, Any]:
        return {
            "key": self.key,
            "path": str(self.relative_path),
            "exists": self.exists,
            "digest": self.digest,
            "missing_snippets": self.missing_snippets,
            "status": "passed" if self.exists and not self.missing_snippets else "failed",
        }


def sha256_digest(text: str) -> str:
    return f"sha256:{hashlib.sha256(text.encode('utf-8')).hexdigest()}"


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def inspect_file(repo_root: Path, key: str, relative_path: Path) -> ProofFileStatus:
    target = repo_root / relative_path
    if not target.exists():
        return ProofFileStatus(key, relative_path, False, None, REQUIRED_SNIPPETS.get(key, []))

    content = read_text(target)
    missing_snippets = [snippet for snippet in REQUIRED_SNIPPETS.get(key, []) if snippet not in content]
    return ProofFileStatus(
        key=key,
        relative_path=relative_path,
        exists=True,
        digest=sha256_digest(content),
        missing_snippets=missing_snippets,
    )


def inspect_authority_file(key: str, path: Path, required_snippets: list[str]) -> ProofFileStatus:
    if not path.exists():
        return ProofFileStatus(key, path, False, None, required_snippets)

    content = read_text(path)
    missing_snippets = [snippet for snippet in required_snippets if snippet not in content]
    digest_source = content if missing_snippets else "\n".join(required_snippets)
    return ProofFileStatus(
        key=key,
        relative_path=path,
        exists=True,
        digest=sha256_digest(digest_source),
        missing_snippets=missing_snippets,
    )


def build_payload(repo_root: Path, out_path: Path) -> dict[str, Any]:
    proof_files = [inspect_file(repo_root, key, path) for key, path in REQUIRED_FILES.items()]
    authority_files = [
        inspect_authority_file(key, path, snippets)
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
            "CHUMMER_CORE_ENGINE_TEST_FILTER=opposition-packet-contracts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj",
            "python3 tests/test_opposition_packet_contract_receipt.py",
            "python3 scripts/verify-opposition-packet-contracts.py --repo-root . --out .codex-studio/published/OPPOSITION_PACKET_CONTRACTS.generated.json",
        ],
        "contract_extensions": [
            "packet_receipt_context",
            "packet_stat_aggregation",
        ],
        "authority_expectations": {
            "queue_status": "complete",
            "design_queue_status": "complete",
            "allowed_paths": ALLOWED_PATHS,
            "owned_surfaces": OWNED_SURFACES,
        },
        "seeded_examples": {
            "entry_packet_id": "red-samurai",
            "pack_packet_id": "renraku-security",
            "scene_packet_id": "renraku-checkpoint",
            "sr6_scene_packet_id": "ancients-smash-and-grab",
            "review_required_pack_id": "broken-pack",
            "review_required_scene_id": "broken-scene",
            "runtime_unbound_entry_id": "runtime-unbound-guard",
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
    return 0 if payload["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
