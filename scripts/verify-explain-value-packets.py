#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PACKAGE_ID = "next90-m145-core-explain-every-value-packets"
FRONTIER_ID = 1451045101
MILESTONE_ID = 145
TITLE = "Emit explanation packets, coverage-registry truth, and bounded counterfactual packets for every visible mechanical result."
TASK = "Emit first-party explanation packets, coverage-registry rows, and deterministic counterfactual packets for promoted visible mechanical results, legality states, warnings, and before-after deltas."
OWNED_SURFACES = ["explain_every_value_packets", "counterfactual_explain:core"]
ALLOWED_PATHS = ["src", "tests", "docs", "scripts"]
PACKAGE_REPO = "chummer6-core"
PACKAGE_REPO_ROOT = "."
PUBLISHED_RECEIPT_PATH = ".codex-studio/published/EXPLAIN_VALUE_PACKETS.generated.json"
DEFAULT_OUTPUT_RELATIVE_PATH = Path(".codex-studio") / "published" / "EXPLAIN_VALUE_PACKETS.generated.json"

REQUIRED_FILES = {
    "contracts": Path("Chummer.Contracts/Diagnostics/ExplainValuePacketContracts.cs"),
    "service": Path("Chummer.Application/Explain/DefaultExplainValuePacketService.cs"),
    "service_interface": Path("Chummer.Application/Explain/IExplainValuePacketService.cs"),
    "di": Path("Chummer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs"),
    "test_program": Path("Chummer.CoreEngine.Tests/Program.cs"),
    "docs": Path("docs/EXPLAIN_VALUE_PACKETS.md"),
}

REQUIRED_SNIPPETS = {
    "contracts": [
        'public const string MechanicalResult = "mechanical-result";',
        'public const string LegalityState = "legality-state";',
        'public const string Warning = "warning";',
        'public const string BeforeAfterDelta = "before-after-delta";',
        'public const string Counterfactual = "counterfactual";',
        'public const string SourceAnchor = "source-anchor";',
        'public const string Why = "why";',
        'public const string WhyNot = "why-not";',
        'public const string WhatIf = "what-if";',
        "public sealed record ExplainValuePacketCoverageRow(",
        "public sealed record ExplainCounterfactualPacket(",
        "public sealed record ExplainValuePacket(",
        "int CounterfactualOverflowCount);",
    ],
    "service": [
        "public const int MaxCounterfactuals = 3;",
        "ExplainValuePacketCoverageKinds.MechanicalResult",
        "ExplainValuePacketCoverageKinds.LegalityState",
        "ExplainValuePacketCoverageKinds.Warning",
        "ExplainValuePacketCoverageKinds.BeforeAfterDelta",
        "ExplainValuePacketCoverageKinds.Counterfactual",
        "ExplainValuePacketCoverageKinds.SourceAnchor",
        "AppendVisibleResultCoverageRows(",
        "BuildCoverageParameters(",
        "CounterfactualOverflowCount: Math.Max(0, (input.Counterfactuals?.Count ?? 0) - counterfactuals.Length)",
        "CollectSourceAnchors",
        "NormalizeCounterfactuals",
        "SupportedCounterfactualOutcomeKinds",
        "NormalizeCounterfactualOutcomeKind",
    ],
    "service_interface": [
        "ExplainValuePacket CreatePacket(ExplainValuePacketInput input);",
    ],
    "di": [
        "services.AddSingleton<IExplainValuePacketService, DefaultExplainValuePacketService>();",
    ],
    "test_program": [
        'string.Equals(filter, "explain-value-packets", StringComparison.OrdinalIgnoreCase)',
        "ExplainValuePacketsStayDeterministicAndBounded();",
        "DefaultExplainValuePacketService.MaxCounterfactuals",
        "packet.CounterfactualOverflowCount",
        "ExplainValuePacketCoverageKinds.SourceAnchor",
        '["why-not-0", "why-1", "what-if-2"]',
        "GetRequiredService<IExplainValuePacketService>()",
        "typeof(DefaultExplainValuePacketService)",
    ],
    "docs": [
        PACKAGE_ID,
        str(FRONTIER_ID),
        "bounded deterministic counterfactual packets, capped at `3`",
        "coverage-registry rows for the promoted surfaces the product must prove before closeout",
        "they only admit promoted outcome kinds: `why`, `why-not`, and `what-if`",
        "CHUMMER_CORE_ENGINE_TEST_FILTER=explain-value-packets dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false",
        "python3 tests/test_explain_value_packet_receipt.py",
        "python3 scripts/verify-explain-value-packets.py --repo-root . --out .codex-studio/published/EXPLAIN_VALUE_PACKETS.generated.json",
    ],
}

CANONICAL_AUTHORITY_FILES = {
    "successor_registry": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml"),
        [
            "  - id: 145",
            "    title: Explain every visible value with grounded follow-up and bounded presenter mode",
            "    - id: '145.1'",
            "      owner: chummer6-core",
            "      title: Emit explanation packets, coverage-registry truth, and bounded counterfactual packets for every visible mechanical result.",
        ],
    ),
    "successor_queue": (
        Path("/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Emit explanation packets, coverage-registry truth, and bounded counterfactual packets for every visible mechanical",
            "  task: Emit first-party explanation packets, coverage-registry rows, and deterministic counterfactual packets for promoted",
            f"  package_id: {PACKAGE_ID}",
            "  work_task_id: '145.1'",
            f"  frontier_id: {FRONTIER_ID}",
            "  milestone_id: 145",
            "  repo: chummer6-core",
            "  - explain_every_value_packets",
            "  - counterfactual_explain:core",
        ],
    ),
}


@dataclass(frozen=True)
class ProofFileStatus:
    key: str
    relative_path: Path
    exists: bool
    digest: str | None
    missing_snippets: list[str]

    def to_json(self, repo_root: Path | None = None) -> dict[str, Any]:
        return {
            "key": self.key,
            "path": receipt_path_for(self.relative_path, repo_root),
            "exists": self.exists,
            "digest": self.digest,
            "missing_snippets": self.missing_snippets,
            "status": "passed" if self.exists and not self.missing_snippets else "failed",
        }


def sha256_digest(text: str) -> str:
    return f"sha256:{hashlib.sha256(text.encode('utf-8')).hexdigest()}"


def receipt_path_for(path: Path, repo_root: Path | None) -> str:
    resolved = path.resolve()
    if repo_root is None:
        return str(resolved)
    try:
        return resolved.relative_to(repo_root.resolve()).as_posix()
    except ValueError:
        return str(resolved)




def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def inspect_file(path: Path, key: str, required_snippets: list[str]) -> ProofFileStatus:
    if not path.exists():
        return ProofFileStatus(key, path, False, None, required_snippets)

    content = read_text(path)
    missing_snippets = [snippet for snippet in required_snippets if snippet not in content]
    return ProofFileStatus(
        key=key,
        relative_path=path,
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
        inspect_file(authority_path, key, snippets)
        for key, (authority_path, snippets) in CANONICAL_AUTHORITY_FILES.items()
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
        "repo_root": ".",
        "owned_surfaces": OWNED_SURFACES,
        "allowed_paths": ALLOWED_PATHS,
        "published_receipt_path": PUBLISHED_RECEIPT_PATH,
        "receipt_path": receipt_path_for(out_path, repo_root),
        "proof_anchor_count": len(proof_files),
        "authority_anchor_count": len(authority_files),
        "proof_files": [status.to_json(repo_root) for status in proof_files],
        "authority_files": [status.to_json(repo_root) for status in authority_files],
        "verification_commands": [
            "CHUMMER_CORE_ENGINE_TEST_FILTER=explain-value-packets dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false",
            "python3 tests/test_explain_value_packet_receipt.py",
            "python3 scripts/verify-explain-value-packets.py --repo-root . --out .codex-studio/published/EXPLAIN_VALUE_PACKETS.generated.json",
        ],
        "coverage_registry_kinds": [
            "mechanical-result",
            "legality-state",
            "warning",
            "before-after-delta",
            "counterfactual",
            "source-anchor",
        ],
        "counterfactual_outcome_kinds": ["why", "why-not", "what-if"],
        "bounded_counterfactual_limit": 3,
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
