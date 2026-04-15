#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[1]
GENERATOR_PATH = REPO_ROOT / "scripts" / "generate-engine-proof-pack.py"


def load_generator() -> Any:
    spec = importlib.util.spec_from_file_location("generate_engine_proof_pack", GENERATOR_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load generator from {GENERATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class EngineProofPackGeneratorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = load_generator()
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)
        self.output_path = self.root / ".codex-studio" / "published" / "ENGINE_PROOF_PACK.generated.json"
        self._seed_passing_repo()

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_build_payload_passes_with_all_required_oracles_budgets_and_successor_metadata(self) -> None:
        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual("next90-m104-core-proof-pack", payload["package_id"])
        self.assertEqual(104, payload["milestone_id"])
        self.assertEqual("next_90_day_product_advance", payload["successor_wave_package"]["program_wave"])
        self.assertEqual(["engine_proof_pack", "import_oracle_discipline"], payload["successor_wave_package"]["owned_surfaces"])
        self.assertEqual("passed", payload["successor_wave_authority"]["status"])
        self.assertEqual([], payload["unresolved"]["oracle_suites"])
        self.assertEqual([], payload["unresolved"]["performance_budgets"])
        self.assertEqual([], payload["unresolved"]["release_commands"])
        self.assertEqual([], payload["unresolved"]["successor_wave_authority"])
        self.assertEqual([], payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_a_suite_evidence_symbol_is_missing(self) -> None:
        (self.root / "Chummer.CoreEngine.Tests" / "Program.cs").write_text("wrong symbol\n", encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("creation", payload["unresolved"]["oracle_suites"])
        self.assertIn("advancement", payload["unresolved"]["oracle_suites"])

    def test_build_payload_fails_closed_when_budget_workload_is_not_executable(self) -> None:
        source_path = self.root / "Chummer.Benchmarks" / "MigrationWorkspaceBenchmarks.cs"
        source_path.write_text(source_path.read_text(encoding="utf-8").replace("runtime.explain.trace", "runtime.trace"), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("explain", payload["unresolved"]["performance_budgets"])
        explain_budget = next(row for row in payload["performance_budgets"] if row["id"] == "explain")
        self.assertTrue(explain_budget["missing_executable_workload"])

    def test_build_payload_fails_closed_when_adjacent_import_oracle_is_missing(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = ["Genesis"]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("missing_adjacent_oracle:CommLink6", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_successor_queue_loses_package_authority(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_path.write_text("package_id: different-package\n", encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(
            "package_id: next90-m104-core-proof-pack",
            payload["successor_wave_authority"]["missing_queue_tokens"],
        )

    def test_planned_generated_output_does_not_create_first_run_self_failure(self) -> None:
        self.assertFalse(self.output_path.exists())

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual([], payload["unresolved"]["release_commands"])

    def _seed_passing_repo(self) -> None:
        self._write("Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj", "<Project />\n")
        self._write("Chummer.Benchmarks/Chummer.Benchmarks.csproj", "<Project />\n")
        self._write(
            "Chummer.CoreEngine.Tests/Program.cs",
            "LegacyChummer5FixtureCorpusImportsRoundTripThroughWorkspaceService\n",
        )
        self._write("Chummer.Tests/TestFiles/Fuzzy-chargen.chum5", "fixture\n")
        self._write("Chummer.Tests/TestFiles/Munin_Career.chum5", "fixture\n")
        self._write("Chummer.CoreEngine.Tests/HeroLabRulesParityAudit.cs", "audit\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/HeroLab/Sr5/Two Banshees.por", "fixture\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-technomancer-hacker.chum4", "fixture\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/HeroLab/Sr6/sr6-starter.hlo.json", "{}\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-hermetic-mage.chum4", "fixture\n")
        self._write("Chummer.Tests/TestFiles/Spirit_Warden.chum5", "fixture\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-rigger-wheelman.chum4", "fixture\n")
        self._write("Chummer.Tests/TestFiles/Apex Predator.chum5", "fixture\n")
        self._write("Chummer.Infrastructure/Xml/XmlToolCatalogService.cs", "BuildSourceToggleLaneReceipt\n")
        self._write("Chummer.Tests/ApiIntegrationTests.cs", "sourceToggleLaneReceipt\n")
        self._write("Chummer.Application/Content/DefaultRuleProfileApplicationService.cs", "service\n")
        self._write("Chummer.Application/Content/DefaultRuntimeLockDiffService.cs", "service\n")
        self._write(
            "Chummer.Benchmarks/workspace-benchmark-budgets.json",
            json.dumps(
                {
                    "workloads": [
                        {"name": "workspace.import.bastion", "maxMeanMilliseconds": 250, "maxAllocatedBytes": 32000000},
                        {"name": "workspace.section.skills.bastion", "maxMeanMilliseconds": 180, "maxAllocatedBytes": 32000000},
                        {"name": "workspace.save.bastion", "maxMeanMilliseconds": 80, "maxAllocatedBytes": 16000000},
                        {"name": "runtime.explain.trace", "maxMeanMilliseconds": 220, "maxAllocatedBytes": 24000000},
                        {"name": "workspace.export.bastion", "maxMeanMilliseconds": 160, "maxAllocatedBytes": 96000000},
                    ]
                }
            ),
        )
        self._write(
            "Chummer.Benchmarks/MigrationWorkspaceBenchmarks.cs",
            "\n".join(
                [
                    "workspace.import.bastion",
                    "workspace.section.skills.bastion",
                    "workspace.save.bastion",
                    "runtime.explain.trace",
                    "workspace.export.bastion",
                ]
            ),
        )
        self._write(
            ".codex-studio/published/IMPORT_PARITY_CERTIFICATION.generated.json",
            json.dumps(
                {
                    "status": "passed",
                    "import_oracles": [
                        {"name": "Chummer4", "sources_covered": 1, "sources_expected": 1},
                        {"name": "Chummer5a", "sources_covered": 1, "sources_expected": 1},
                        {"name": "Hero Lab Classic", "sources_covered": 1, "sources_expected": 1},
                    ],
                    "adjacent_oracles": ["Genesis", "CommLink6"],
                }
            ),
        )
        self._seed_successor_wave_authority()

    def _write(self, relative_path: str, content: str) -> None:
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def _seed_successor_wave_authority(self) -> None:
        registry_path = self.root / "successor-registry.yaml"
        queue_path = self.root / "successor-queue.yaml"
        registry_path.write_text(
            "\n".join(
                [
                    "milestones:",
                    "  - id: 104",
                    "    title: Engine proof pack, explain budgets, and import-oracle discipline",
                    "    work_tasks:",
                    "      - id: 104.1",
                    "        owner: chummer6-core",
                    "      - id: 104.2",
                    "        owner: chummer6-core",
                ]
            )
            + "\n",
            encoding="utf-8",
        )
        queue_path.write_text(
            "\n".join(
                [
                    "items:",
                    "  - package_id: next90-m104-core-proof-pack",
                    "    milestone_id: 104",
                    "    repo: chummer6-core",
                    "    owned_surfaces:",
                    "      - engine_proof_pack",
                    "      - import_oracle_discipline",
                ]
            )
            + "\n",
            encoding="utf-8",
        )
        self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"] = str(registry_path)
        self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"] = str(queue_path)


if __name__ == "__main__":
    unittest.main()
