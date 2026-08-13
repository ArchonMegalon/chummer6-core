#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PACKAGE_ID = "next90-m141-core-bind-import-oracle-custom-data-and-amend-package-flows-to-deterministic"
FRONTIER_ID = 4304178368
FLAGSHIP_FRONTIER_ID = 2350979521
MILESTONE_ID = 141
WORK_TASK_ID = "141.2"
TITLE = "Bind import-oracle, custom-data, and amend-package flows to deterministic receipts that can be cited by parity and workflow gates."
TASK = TITLE
OWNED_SURFACES = ["bind_import_oracle_custom_data_and_amend_package_flows_t:core"]
ALLOWED_PATHS = ["src", "tests", "docs", "scripts"]
PACKAGE_REPO = "chummer6-core"
PACKAGE_REPO_ROOT = "."
PUBLISHED_RECEIPT_PATH = ".codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json"
DEFAULT_OUTPUT_RELATIVE_PATH = Path(".codex-studio") / "published" / "NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json"
TRANSLATOR_ROUTE_ID = "source:translator_route"
CUSTOM_DATA_FAMILY_ID = "family:custom_data_xml_and_translator_bridge"
IMPORT_ORACLE_FAMILY_ID = "family:legacy_and_adjacent_import_oracles"
EXPECTED_ENGINE_PROOF_PACK_REQUIRED_SUITE_IDS = [
    "creation",
    "advancement",
    "augment",
    "matrix",
    "magic",
    "vehicle",
    "source_toggle",
    "amend_package",
]
EXPECTED_ENGINE_PROOF_PACK_SUITES = {
    "source_toggle": {
        "status": "passed",
        "coverage_focus": "sourcebook_and_settings_discipline",
        "rulesets": ["sr5"],
        "release_scope": "promoted_desktop_release",
        "golden_fixture_count": 1,
        "evidence": [
            "Chummer.Infrastructure/Xml/XmlToolCatalogService.cs::BuildSourceToggleLaneReceipt",
            "Chummer.Tests/ApiIntegrationTests.cs::sourceToggleLaneReceipt",
        ],
        "golden_fixtures": [
            "Chummer.CoreEngine.Tests/Fixtures/Contracts/buildkit-manifest.normalized.golden.json",
        ],
    },
    "amend_package": {
        "status": "passed",
        "coverage_focus": "custom_data_and_xml_diff_apply",
        "rulesets": ["sr5"],
        "release_scope": "promoted_desktop_release",
        "golden_fixture_count": 2,
        "evidence": [
            "Chummer.Application/Content/DefaultRuleProfileApplicationService.cs",
            "Chummer.Application/Content/DefaultRuntimeLockDiffService.cs",
        ],
        "golden_fixtures": [
            "Chummer.CoreEngine.Tests/Fixtures/Contracts/runtime-lock-install-preview.normalized.golden.json",
            "Chummer.CoreEngine.Tests/Fixtures/Contracts/runtime-lock-install-candidate.normalized.golden.json",
        ],
    },
}

REQUIRED_FILES = {
    "contracts": Path("Chummer.Contracts/Api/ToolCatalogModels.cs"),
    "tool_catalog_service": Path("Chummer.Infrastructure/Xml/XmlToolCatalogService.cs"),
    "tool_catalog_tests": Path("Chummer.Tests/ToolCatalogServiceTests.cs"),
    "api_integration_tests": Path("Chummer.Tests/ApiIntegrationTests.cs"),
    "engine_proof_pack_doc": Path("docs/ENGINE_PROOF_PACK.md"),
    "m141_docs": Path("docs/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.md"),
    "m141_closeout": Path("docs/NEXT90_M141_IMPORT_ROUTE_CLOSEOUT.md"),
    "repo_verify": Path("scripts/ai/verify.sh"),
    "engine_proof_pack_generator": Path("scripts/generate-engine-proof-pack.py"),
    "engine_proof_pack_generator_tests": Path("tests/test_engine_proof_pack_generator.py"),
    "engine_proof_pack_published": Path(".codex-studio/published/ENGINE_PROOF_PACK.generated.json"),
    "import_parity_certification": Path(".codex-studio/published/IMPORT_PARITY_CERTIFICATION.generated.json"),
}

REQUIRED_SNIPPETS = {
    "contracts": [
        "CustomDataXmlBridgeDeterministicReceipt",
        "TranslatorLaneDeterministicReceipt",
        "ImportOracleLaneDeterministicReceipt",
        "AmendPackageDeterministicReceipt",
        "Sr6SuccessorLaneDeterministicReceipt",
        "CustomDataXmlBridgeDeterministicReceipt? CustomDataXmlBridgeDeterministicReceipt = null",
        "TranslatorLaneDeterministicReceipt? TranslatorDeterministicReceipt = null",
        "ImportOracleLaneDeterministicReceipt? ImportOracleDeterministicReceipt = null",
        "AmendPackageDeterministicReceipt? AmendPackageDeterministicReceipt = null",
    ],
    "tool_catalog_service": [
        'private const string CustomDataXmlBridgeParityFamilyId = "family:custom_data_xml_and_translator_bridge";',
        'private const string TranslatorParityRouteId = "source:translator_route";',
        'private const string ImportOracleParityFamilyId = "family:legacy_and_adjacent_import_oracles";',
        "BuildCustomDataXmlBridgeDeterministicReceipt(",
        "BuildTranslatorDeterministicReceipt(",
        "BuildImportOracleDeterministicReceipt(",
        "BuildAmendPackageDeterministicReceipt(",
        "BuildAdjacentSr6OracleLaneReceipt(summary)",
    ],
    "tool_catalog_tests": [
        'Assert.AreEqual("family:custom_data_xml_and_translator_bridge", response.CustomDataXmlBridgeDeterministicReceipt!.ParityFamilyId);',
        'Assert.AreEqual("source:translator_route", response.TranslatorDeterministicReceipt!.ParityRouteId);',
        'Assert.AreEqual("family:legacy_and_adjacent_import_oracles", response.ImportOracleDeterministicReceipt!.ParityFamilyId);',
        'Assert.AreEqual("chummer6-core.engine_proof_pack", response.AmendPackageDeterministicReceipt!.ProofContractName);',
        "Master_index_reports_governed_translator_lane_when_corpus_and_language_overlay_bridge_exist",
        "Master_index_reports_governed_import_oracle_lane_when_fixture_families_and_certification_are_present",
    ],
    "api_integration_tests": [
        'Assert.IsNotNull(response["customDataXmlBridgeDeterministicReceipt"]);',
        'Assert.IsNotNull(response["translatorDeterministicReceipt"]);',
        'Assert.IsNotNull(response["importOracleDeterministicReceipt"]);',
        'Assert.IsNotNull(response["amendPackageDeterministicReceipt"]);',
        'Assert.AreEqual("source:translator_route", translatorDeterministicReceipt?["parityRouteId"]?.GetValue<string>());',
        'Assert.AreEqual("family:legacy_and_adjacent_import_oracles", importOracleDeterministicReceipt?["parityFamilyId"]?.GetValue<string>());',
        'Assert.AreEqual("chummer6-core.engine_proof_pack", amendPackageDeterministicReceipt?["proofContractName"]?.GetValue<string>());',
    ],
    "engine_proof_pack_doc": [
        "source_toggle",
        "amend_package",
        "NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json",
    ],
    "m141_docs": [
        PACKAGE_ID,
        str(FLAGSHIP_FRONTIER_ID),
        TRANSLATOR_ROUTE_ID,
        CUSTOM_DATA_FAMILY_ID,
        IMPORT_ORACLE_FAMILY_ID,
        "customDataXmlBridgeDeterministicReceipt",
        "translatorDeterministicReceipt",
        "importOracleDeterministicReceipt",
        "amendPackageDeterministicReceipt",
        ".codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json",
        "worker handoff artifacts",
        "run-local telemetry artifacts",
        "python3 tests/test_next90_m141_import_route_receipts.py",
    ],
    "m141_closeout": [
        PACKAGE_ID,
        str(FRONTIER_ID),
        str(FLAGSHIP_FRONTIER_ID),
        ".codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json",
        "worker handoff artifacts",
        "run-local telemetry artifacts",
        "python3 tests/test_next90_m141_import_route_receipts.py",
        "python3 scripts/verify-next90-m141-import-route-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json --check",
        "Do not reopen this core package for desktop screenshot capture, Hub parity-claim posture, Fleet gate materialization, or EA compare-packet work.",
    ],
    "repo_verify": [
        "test -f scripts/verify-next90-m141-import-route-receipts.py",
        "test -f tests/test_next90_m141_import_route_receipts.py",
        "python3 tests/test_next90_m141_import_route_receipts.py",
        "python3 scripts/verify-next90-m141-import-route-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json --check",
        "worker handoff artifacts",
        "run-local telemetry artifacts",
    ],
    "engine_proof_pack_generator": [
        '"id": "source_toggle"',
        '"id": "amend_package"',
        '"coverage_focus": "custom_data_and_xml_diff_apply"',
        '"Chummer.Application/Content/DefaultRuleProfileApplicationService.cs"',
        '"Chummer.Application/Content/DefaultRuntimeLockDiffService.cs"',
    ],
    "engine_proof_pack_generator_tests": [
        'self.assertEqual(["amend_package"], summary["missing_required_suite_ids"])',
        '{"id": "amend_package", "status": "passed", "rulesets": ["sr5"], "golden_fixture_count": 0}',
        "required oracle suites creation, advancement, augment, matrix, magic, vehicle, source_toggle, and amend_package",
    ],
    "engine_proof_pack_published": [
        '"id": "source_toggle"',
        '"id": "amend_package"',
        '"coverage_focus": "custom_data_and_xml_diff_apply"',
        'DefaultRuntimeLockDiffService.cs',
    ],
    "import_parity_certification": [
        '"contract_name": "chummer6-core.import_parity_certification"',
        '"name": "Genesis"',
        '"name": "CommLink6"',
        '"coverage_percent": 100',
    ],
}

CANONICAL_AUTHORITY_FILES = {
    "successor_registry": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml"),
        [
            "  - id: 141",
            "    title: Direct parity proof for translator, XML amendment, Hero Lab, and adjacent import routes",
            "    - id: '141.2'",
            "      owner: chummer6-core",
            "      title: Bind import-oracle, custom-data, and amend-package flows to deterministic receipts that can be cited by parity and workflow gates.",
        ],
    ),
    "successor_queue": (
        Path("/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Bind import-oracle, custom-data, and amend-package flows to deterministic receipts that can be cited by parity and",
            f"  package_id: {PACKAGE_ID}",
            "  work_task_id: '141.2'",
            "  frontier_id: 4304178368",
            "  milestone_id: 141",
            "  repo: chummer6-core",
            "  - bind_import_oracle_custom_data_and_amend_package_flows_t:core",
        ],
    ),
    "design_successor_queue": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Bind import-oracle, custom-data, and amend-package flows to deterministic receipts that can be cited by parity and",
            f"  package_id: {PACKAGE_ID}",
            "  work_task_id: '141.2'",
            "  frontier_id: 4304178368",
            "  milestone_id: 141",
            "  repo: chummer6-core",
            "  - bind_import_oracle_custom_data_and_amend_package_flows_t:core",
        ],
    ),
}

AUTHORITY_ROW_MARKERS = {
    "successor_registry": "    - id: '141.2'\n",
    "successor_queue": "- title: Bind import-oracle, custom-data, and amend-package flows to deterministic receipts that can be cited by parity and\n",
    "design_successor_queue": "- title: Bind import-oracle, custom-data, and amend-package flows to deterministic receipts that can be cited by parity and\n",
}

EXPECTED_AUTHORITY_ROW_COUNTS = {
    "successor_registry": 1,
    "successor_queue": 1,
    "design_successor_queue": 1,
}

DISALLOWED_ACTIVE_RUN_PROOF_TOKENS = (
    "/var/lib/codex-fleet/",
    "TASK_LOCAL_TELEMETRY.generated.json",
    "ACTIVE_RUN_HANDOFF.generated.md",
    "run these exact commands first",
    "do not invent another orientation step",
    "first action rule:",
    "use the shard runtime handoff",
    "execution discipline:",
    "execution rules inside this run:",
    "assigned successor queue package:",
    "successor frontier detail:",
)
EXPECTED_CLOSED_PACKAGE_REASON = (
    "M141 chummer6-core import-route deterministic receipt lane is complete; future shards must verify "
    "the closed-package receipt, Python guard tests, canonical registry row, and queue mirrors instead "
    "of reopening this slice."
)
EXPECTED_REGISTRY_CLOSURE_SNIPPETS = [
    "status: complete",
    "completion_action: verify_closed_package_only",
    EXPECTED_CLOSED_PACKAGE_REASON,
    ".codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json",
    "python3 tests/test_next90_m141_import_route_receipts.py",
    "python3 scripts/verify-next90-m141-import-route-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json --check",
]
EXPECTED_QUEUE_CLOSURE_SNIPPETS = [
    "status: complete",
    "completion_action: verify_closed_package_only",
    "landed_commit: unlanded",
    EXPECTED_CLOSED_PACKAGE_REASON,
    "proof:",
    "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json",
    "python3 tests/test_next90_m141_import_route_receipts.py",
    "python3 scripts/verify-next90-m141-import-route-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json --check",
]


@dataclass(frozen=True)
class ProofFileStatus:
    key: str
    path: Path
    exists: bool
    digest: str | None
    digest_scope: str
    missing_snippets: list[str]

    def to_json(self, repo_root: Path | None = None) -> dict[str, Any]:
        return {
            "key": self.key,
            "path": receipt_path_for(self.path, repo_root),
            "exists": self.exists,
            "digest": self.digest,
            "digest_scope": self.digest_scope,
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


def normalize_json(value: Any) -> str:
    return json.dumps(value, indent=2, sort_keys=True) + "\n"


def extract_authority_scope(key: str, content: str) -> tuple[str, str]:
    if key == "successor_registry":
        scoped_rows = extract_successor_registry_package_rows(content)
        if scoped_rows:
            return "\n".join(scoped_rows), "work-task-row"
        return content, "full-file"

    if key in {"successor_queue", "design_successor_queue"}:
        scoped_rows = extract_queue_package_rows(content)
        if scoped_rows:
            return "\n".join(scoped_rows), "package-rows"
        return content, "full-file"

    return content, "full-file"


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


def extract_successor_registry_package_rows(content: str) -> list[str]:
    rows: list[str] = []
    start_marker = AUTHORITY_ROW_MARKERS["successor_registry"]
    search_start = 0
    while True:
        start = content.find(start_marker, search_start)
        if start < 0:
            break

        next_task_marker = content.find("\n    - id: '", start + len(start_marker))
        next_milestone_marker = content.find("\n  - id: ", start + len(start_marker))
        candidate_markers = [marker for marker in (next_task_marker, next_milestone_marker) if marker >= 0]
        end = min(candidate_markers) if candidate_markers else -1
        row = content[start:] if end < 0 else content[start:end]
        rows.append(row.rstrip())
        search_start = start + len(start_marker)

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
        return len(extract_successor_registry_package_rows(content))

    if key in {"successor_queue", "design_successor_queue"}:
        return len(extract_queue_package_rows(content))

    marker = AUTHORITY_ROW_MARKERS.get(key)
    return 0 if marker is None else content.count(marker)


def find_disallowed_active_run_tokens(content: str) -> list[str]:
    content_lower = content.lower()
    return [
        token
        for token in DISALLOWED_ACTIVE_RUN_PROOF_TOKENS
        if token.lower() in content_lower
    ]


def build_authority_semantic_issues() -> dict[str, list[str]]:
    issues: dict[str, list[str]] = {}

    queue_path, _ = CANONICAL_AUTHORITY_FILES["successor_queue"]
    design_queue_path, _ = CANONICAL_AUTHORITY_FILES["design_successor_queue"]
    if queue_path.exists() and design_queue_path.exists():
        queue_scope, _ = extract_authority_scope("successor_queue", read_text(queue_path))
        design_queue_scope, _ = extract_authority_scope("design_successor_queue", read_text(design_queue_path))
        if queue_scope != design_queue_scope:
            issues["queue_mirror_drift"] = [
                "fleet_and_design_queue_package_rows_differ"
            ]

    for key, (path, _) in CANONICAL_AUTHORITY_FILES.items():
        if not path.exists():
            continue

        scoped_content, _ = extract_authority_scope(key, read_text(path))
        disallowed_tokens = find_disallowed_active_run_tokens(scoped_content)
        if disallowed_tokens:
            issues[f"{key}_disallowed_active_run_proof"] = disallowed_tokens

        expected_closure_snippets = (
            EXPECTED_REGISTRY_CLOSURE_SNIPPETS
            if key == "successor_registry"
            else EXPECTED_QUEUE_CLOSURE_SNIPPETS
            if key in {"successor_queue", "design_successor_queue"}
            else []
        )
        missing_closure_snippets = [
            snippet for snippet in expected_closure_snippets if snippet not in scoped_content
        ]
        if missing_closure_snippets:
            issues[f"{key}_closure_drift"] = [
                f"missing_closure_snippet:{snippet}" for snippet in missing_closure_snippets
            ]

    return issues


def extract_proof_scope(key: str, content: str) -> tuple[str, str]:
    if key == "engine_proof_pack_published":
        payload = json.loads(content)
        oracle_suites = [
            suite
            for suite in payload.get("oracle_suites", [])
            if suite.get("id") in {"source_toggle", "amend_package"}
        ]
        scope = {
            "status": payload.get("status"),
            "package_id": payload.get("package_id"),
            "frontier_id": payload.get("frontier_id"),
            "oracle_suites": oracle_suites,
        }
        return normalize_json(scope), "stable-json-subset"

    if key == "import_parity_certification":
        payload = json.loads(content)
        scope = {
            "contract_name": payload.get("contract_name"),
            "status": payload.get("status"),
            "proof_kind": payload.get("proof_kind"),
            "import_oracles": payload.get("import_oracles"),
            "adjacent_oracles": payload.get("adjacent_oracles"),
            "coverage": payload.get("coverage"),
        }
        return normalize_json(scope), "stable-json-subset"

    return content, "full-file"


def inspect_file(path: Path, key: str, required_snippets: list[str], *, authority: bool) -> ProofFileStatus:
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
    digest_content, digest_scope = (
        extract_authority_scope(key, content) if authority else extract_proof_scope(key, content)
    )
    return ProofFileStatus(
        key=key,
        path=path,
        exists=True,
        digest=sha256_digest(digest_content),
        digest_scope=digest_scope,
        missing_snippets=missing_snippets,
    )


def build_supporting_receipt_semantic_issues(repo_root: Path) -> dict[str, list[str]]:
    issues: dict[str, list[str]] = {}

    for key in ("m141_docs", "m141_closeout"):
        path = repo_root / REQUIRED_FILES[key]
        if not path.exists():
            continue

        disallowed_tokens = find_disallowed_active_run_tokens(read_text(path))
        if disallowed_tokens:
            issues[key] = [f"disallowed_active_run_proof:{token}" for token in disallowed_tokens]

    engine_proof_pack_path = repo_root / REQUIRED_FILES["engine_proof_pack_published"]
    if engine_proof_pack_path.exists():
        payload = json.loads(read_text(engine_proof_pack_path))
        engine_issues: list[str] = []

        if payload.get("contract_name") != "chummer6-core.engine_proof_pack":
            engine_issues.append(f"unexpected_contract_name:{payload.get('contract_name')}")
        if payload.get("status") != "passed":
            engine_issues.append(f"unexpected_status:{payload.get('status')}")
        if payload.get("package_id") != "next90-m104-core-proof-pack":
            engine_issues.append(f"unexpected_package_id:{payload.get('package_id')}")
        if payload.get("frontier_id") != 3227666051:
            engine_issues.append(f"unexpected_frontier_id:{payload.get('frontier_id')}")
        if payload.get("required_oracle_suite_ids") != EXPECTED_ENGINE_PROOF_PACK_REQUIRED_SUITE_IDS:
            engine_issues.append(
                "unexpected_required_suite_ids:"
                + ",".join(str(item) for item in payload.get("required_oracle_suite_ids", []))
            )
        missing_required_suite_ids = payload.get("missing_required_suite_ids")
        if missing_required_suite_ids not in (None, []):
            engine_issues.append(
                "unexpected_missing_required_suite_ids:"
                + ",".join(str(item) for item in missing_required_suite_ids)
            )

        suites = {
            suite.get("id"): suite
            for suite in payload.get("oracle_suites", [])
            if isinstance(suite, dict) and suite.get("id") in {"source_toggle", "amend_package"}
        }
        for suite_id, expected_suite in EXPECTED_ENGINE_PROOF_PACK_SUITES.items():
            suite = suites.get(suite_id)
            if suite is None:
                engine_issues.append(f"missing_suite:{suite_id}")
                continue
            if suite.get("status") != expected_suite["status"]:
                engine_issues.append(f"unexpected_suite_status:{suite_id}:{suite.get('status')}")
            if suite.get("coverage_focus") != expected_suite["coverage_focus"]:
                engine_issues.append(
                    f"unexpected_{suite_id}_coverage_focus:{suite.get('coverage_focus')}"
                )
            if suite.get("rulesets") != expected_suite["rulesets"]:
                engine_issues.append(
                    f"unexpected_{suite_id}_rulesets:"
                    + ",".join(str(item) for item in suite.get("rulesets", []))
                )
            if suite.get("release_scope") != expected_suite["release_scope"]:
                engine_issues.append(f"unexpected_{suite_id}_release_scope:{suite.get('release_scope')}")
            if suite.get("golden_fixture_count") != expected_suite["golden_fixture_count"]:
                engine_issues.append(
                    f"unexpected_{suite_id}_golden_fixture_count:{suite.get('golden_fixture_count')}"
                )
            if suite.get("evidence") != expected_suite["evidence"]:
                engine_issues.append(
                    f"unexpected_{suite_id}_evidence:"
                    + ",".join(str(item) for item in suite.get("evidence", []))
                )
            if suite.get("golden_fixtures") != expected_suite["golden_fixtures"]:
                engine_issues.append(
                    f"unexpected_{suite_id}_golden_fixtures:"
                    + ",".join(str(item) for item in suite.get("golden_fixtures", []))
                )

        if engine_issues:
            issues["engine_proof_pack_published"] = engine_issues

    import_parity_path = repo_root / REQUIRED_FILES["import_parity_certification"]
    if import_parity_path.exists():
        payload = json.loads(read_text(import_parity_path))
        parity_issues: list[str] = []

        if payload.get("contract_name") != "chummer6-core.import_parity_certification":
            parity_issues.append(f"unexpected_contract_name:{payload.get('contract_name')}")
        if payload.get("schema_version") != 1:
            parity_issues.append(f"unexpected_schema_version:{payload.get('schema_version')}")
        if payload.get("status") != "passed":
            parity_issues.append(f"unexpected_status:{payload.get('status')}")
        if payload.get("proof_kind") != "local_parity_harness":
            parity_issues.append(f"unexpected_proof_kind:{payload.get('proof_kind')}")

        import_oracles = [
            item
            for item in payload.get("import_oracles", [])
            if isinstance(item, dict)
        ]
        import_oracle_names = [item.get("name") for item in import_oracles]
        if import_oracle_names != ["Chummer4", "Chummer5a", "Hero Lab Classic"]:
            parity_issues.append(f"unexpected_import_oracles:{','.join(import_oracle_names)}")
        for item, expected_name in zip(import_oracles, ["Chummer4", "Chummer5a", "Hero Lab Classic"]):
            if item.get("name") != expected_name:
                continue
            covered = item.get("sources_covered")
            expected = item.get("sources_expected")
            if covered != 1 or expected != 1:
                parity_issues.append(
                    f"unexpected_import_oracle_counts:{expected_name}:{covered}:{expected}"
                )

        adjacent_oracles = [
            item
            for item in payload.get("adjacent_oracles", [])
            if isinstance(item, dict)
        ]
        adjacent_oracle_names = [item.get("name") for item in adjacent_oracles]
        if adjacent_oracle_names != ["Genesis", "CommLink6"]:
            parity_issues.append(f"unexpected_adjacent_oracles:{','.join(adjacent_oracle_names)}")
        for item, expected_name in zip(adjacent_oracles, ["Genesis", "CommLink6"]):
            if item.get("name") != expected_name:
                continue
            covered = item.get("sources_covered")
            expected = item.get("sources_expected")
            if covered != 1 or expected != 1:
                parity_issues.append(
                    f"unexpected_adjacent_oracle_counts:{expected_name}:{covered}:{expected}"
                )

        coverage = payload.get("coverage", {})
        if not isinstance(coverage, dict) or coverage.get("coverage_percent") != 100:
            parity_issues.append(f"unexpected_coverage_percent:{coverage.get('coverage_percent') if isinstance(coverage, dict) else None}")
        if isinstance(coverage, dict) and (
            coverage.get("sources_covered") != 5 or coverage.get("sources_expected") != 5
        ):
            parity_issues.append(
                f"unexpected_coverage_counts:{coverage.get('sources_covered')}:{coverage.get('sources_expected')}"
            )

        if parity_issues:
            issues["import_parity_certification"] = parity_issues

    return issues


def build_payload(repo_root: Path, out_path: Path) -> dict[str, Any]:
    proof_files = [
        inspect_file(repo_root / relative_path, key, REQUIRED_SNIPPETS.get(key, []), authority=False)
        for key, relative_path in REQUIRED_FILES.items()
    ]
    authority_files = [
        inspect_file(path, key, snippets, authority=True)
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
    authority_semantic_issues = build_authority_semantic_issues()
    supporting_receipt_semantic_issues = build_supporting_receipt_semantic_issues(repo_root)
    passed = (
        not missing_files
        and not snippet_failures
        and not authority_row_issues
        and not authority_semantic_issues
        and not supporting_receipt_semantic_issues
    )

    return {
        "generated_at": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
        "status": "passed" if passed else "failed",
        "package_id": PACKAGE_ID,
        "frontier_id": FRONTIER_ID,
        "flagship_frontier_id": FLAGSHIP_FRONTIER_ID,
        "milestone_id": MILESTONE_ID,
        "work_task_id": WORK_TASK_ID,
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
        "authority_row_counts": authority_row_counts,
        "verification_commands": [
            "python3 tests/test_next90_m141_import_route_receipts.py",
            "python3 scripts/verify-next90-m141-import-route-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json --check",
        ],
        "master_index_route": "/api/tools/master-index",
        "parity_route_id": TRANSLATOR_ROUTE_ID,
        "receipt_family_ids": [CUSTOM_DATA_FAMILY_ID, IMPORT_ORACLE_FAMILY_ID],
        "deterministic_receipt_fields": [
            "customDataXmlBridgeDeterministicReceipt",
            "translatorDeterministicReceipt",
            "importOracleDeterministicReceipt",
            "amendPackageDeterministicReceipt",
        ],
        "engine_proof_pack_required_suite_ids": ["source_toggle", "amend_package"],
        "supporting_receipts": [
            ".codex-studio/published/ENGINE_PROOF_PACK.generated.json",
            ".codex-studio/published/IMPORT_PARITY_CERTIFICATION.generated.json",
        ],
        "unresolved": {
            "missing_files": missing_files,
            "snippet_failures": snippet_failures,
            "authority_row_issues": authority_row_issues,
            "authority_semantic_issues": authority_semantic_issues,
            "supporting_receipt_semantic_issues": supporting_receipt_semantic_issues,
        },
    }


def without_generated_at(payload: dict[str, Any]) -> dict[str, Any]:
    comparable = dict(payload)
    comparable.pop("generated_at", None)
    return comparable


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--out", type=Path, default=DEFAULT_OUTPUT_RELATIVE_PATH)
    parser.add_argument("--check", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    repo_root = args.repo_root.resolve()
    out_path = args.out.resolve()
    out_path.parent.mkdir(parents=True, exist_ok=True)

    payload = build_payload(repo_root, out_path)

    if args.check:
        if not out_path.exists():
            print(f"missing receipt: {out_path}")
            return 1

        checked_in_payload = json.loads(out_path.read_text(encoding="utf-8"))
        if without_generated_at(payload) != without_generated_at(checked_in_payload):
            print(f"checked-in receipt is stale: {out_path}")
            return 1
        if payload["status"] != "passed":
            print(f"checked-in receipt does not pass current proof: {out_path}")
            return 1

        print(out_path)
        return 0

    out_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(out_path)
    return 0 if payload["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
