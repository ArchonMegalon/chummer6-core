#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
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


def without_generated_at(payload: dict[str, Any]) -> dict[str, Any]:
    comparable = dict(payload)
    comparable.pop("generated_at", None)
    return comparable


class EngineProofPackReceiptReproducibilityTests(unittest.TestCase):
    def test_checked_in_engine_proof_pack_matches_generator_except_timestamp(self) -> None:
        generator = load_generator()
        receipt_path = REPO_ROOT / ".codex-studio" / "published" / "ENGINE_PROOF_PACK.generated.json"
        checked_in_payload = json.loads(receipt_path.read_text(encoding="utf-8"))

        regenerated_payload = generator.build_payload(REPO_ROOT, receipt_path)

        self.assertEqual(
            without_generated_at(regenerated_payload),
            without_generated_at(checked_in_payload),
            "Checked-in ENGINE_PROOF_PACK.generated.json should be reproducible from the generator except generated_at.",
        )

    def test_generator_check_mode_accepts_current_checked_in_receipt(self) -> None:
        receipt_path = REPO_ROOT / ".codex-studio" / "published" / "ENGINE_PROOF_PACK.generated.json"

        result = subprocess.run(
            [
                sys.executable,
                str(GENERATOR_PATH),
                "--repo-root",
                str(REPO_ROOT),
                "--out",
                str(receipt_path),
                "--check",
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            check=False,
        )

        self.assertEqual("", result.stderr)
        self.assertEqual(0, result.returncode)
        self.assertIn(str(receipt_path), result.stdout)


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
        self.assertEqual(3227666051, payload["frontier_id"])
        self.assertEqual(104, payload["milestone_id"])
        self.assertEqual("verify_closed_package_only", payload["queue_completion_action"])
        self.assertEqual("verify_closed_package_only", payload["design_queue_completion_action"])
        self.assertEqual(
            "M104 chummer6-core engine proof pack is complete; future shards must verify this receipt, queue row, design queue row, and closeout note instead of reopening the proof-pack package.",
            payload["queue_do_not_reopen_reason"],
        )
        self.assertEqual(
            "M104 chummer6-core engine proof pack is complete; future shards must verify this receipt, queue row, design queue row, and closeout note instead of reopening the proof-pack package.",
            payload["design_queue_do_not_reopen_reason"],
        )
        self.assertEqual([], payload["queue_closure_field_drift"])
        self.assertEqual("next_90_day_product_advance", payload["successor_wave_package"]["program_wave"])
        self.assertEqual(["engine_proof_pack", "import_oracle_discipline"], payload["successor_wave_package"]["owned_surfaces"])
        self.assertEqual("passed", payload["successor_wave_authority"]["status"])
        self.assertEqual(
            "verify_closed_package_only",
            payload["successor_wave_authority"]["queue_completion_action"],
        )
        self.assertEqual(
            "verify_closed_package_only",
            payload["successor_wave_authority"]["design_queue_completion_action"],
        )
        self.assertEqual(
            "M104 chummer6-core engine proof pack is complete; future shards must verify this receipt, queue row, design queue row, and closeout note instead of reopening the proof-pack package.",
            payload["successor_wave_authority"]["queue_do_not_reopen_reason"],
        )
        self.assertEqual(
            "M104 chummer6-core engine proof pack is complete; future shards must verify this receipt, queue row, design queue row, and closeout note instead of reopening the proof-pack package.",
            payload["successor_wave_authority"]["design_queue_do_not_reopen_reason"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["queue_closure_field_drift"])
        self.assertEqual("passed", payload["closeout_document"]["status"])
        self.assertEqual([], payload["unresolved"]["closeout_document"])
        self.assertEqual("skipped", payload["local_commit_proofs"]["status"])
        self.assertEqual([], payload["unresolved"]["local_commit_proofs"])
        self.assertIn(
            "56048971",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "769e7259",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "d4b3b0ba",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "a2173476",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "dafc1205",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "65df3894",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "4a56911d",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "4b124997",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "2187db33",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "b488d109",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "b6fddf74",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "3b9a29c2",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "f6608678",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "a3cbb548",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "df0527b2",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "8574f63f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "6b3a662c",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "3b63478f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "31c75c02",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "ef46554c",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "0771b7ea",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "fdb6a273",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "d2ee91a9",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "cd30503f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "e10f2739",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "e7d4270e",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "bbc877d7",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "56ff7283",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "7ae79416",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "a613bdb2",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "353921e7",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "9de2455b",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "d8e826a3",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "7a1f0e7c",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "d464cfab",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "a1a2d956",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "abf63719",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "bbc7fba8",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "a1a1d505",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "18d03556",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "77cb53cf",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "f914ce6a",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "3c242c2f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "c2872b40",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "18365058",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "5031ee41",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "cbce6a19",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "71441924",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "df1330b4",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "6610ff2e",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "2c8742ad",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "5baebb73",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "40babebd",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "22171b35",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "c6fbd75f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "96eca660",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "05e47cff",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "93d06011",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "31aec38a",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "ceccc309",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "5dff1a2e",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "2301a043",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "5c75316f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "28be988f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "c6a2ee8e",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "6684fc89",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "ccbfc6b2",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "2a3ebcb9",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "7501f49a",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "ac961fe1",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "36311e16",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "db3cc033",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "be5755a6",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "8ffec2b1",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "ee9d88b1",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "eacefaf2",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "e4e502a1",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "1f2c5724",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "1bcb9b7e",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "e04d7b88",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "58656418",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "73638668",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "a404b474",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "51bb2d8f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "507f1f6b",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "43638c3e",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "b0776012",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "5f50cb7b",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "c58d18e1",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "67e0f654",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "d584120b",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "39c875fd",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "f1b6c5ca",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "faf14925",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "64b8f873",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "06a2e06a",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "6d25fb18",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "cc6cf25b",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "bb9af238",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "44512fcf",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "4db6d429",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "adc72a7e",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "5e808a1b",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "c323b4ad",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "7a432bc3",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "c124e4af",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "5a649e57",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "c01dfa10",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "1a98d904",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "af67ecfd",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "870be707",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "498dff3d",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "b8000b80",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "ecbb466c",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "a2c8ad9f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "2c98f61c",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "2e4e8e81",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "b5d46938",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "c1300863",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "8f4702a5",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "aeeeaf6e",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "c84b251f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "29b17c68",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "262030df",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertEqual("complete", payload["successor_wave_authority"]["closure_requirements"]["status"])
        self.assertEqual(3227666051, payload["successor_wave_authority"]["closure_requirements"]["frontier_id"])
        self.assertEqual("00800059", payload["successor_wave_authority"]["closure_requirements"]["landed_commit"])
        self.assertEqual("passed", payload["successor_wave_authority"]["status"])
        self.assertEqual([], payload["unresolved"]["oracle_suites"])
        self.assertEqual([], payload["unresolved"]["performance_budgets"])
        self.assertEqual([], payload["unresolved"]["release_commands"])
        self.assertEqual([], payload["unresolved"]["successor_wave_authority"])
        self.assertEqual([], payload["unresolved"]["release_channel_binding"])
        self.assertEqual([], payload["unresolved"]["import_oracle_discipline"])
        self.assertEqual("passed", payload["oracle_suite_summary"]["coverage_status"])
        self.assertEqual(8, payload["oracle_suite_summary"]["required_suite_count"])
        self.assertEqual(8, payload["oracle_suite_summary"]["published_suite_count"])
        self.assertEqual(8, payload["oracle_suite_summary"]["passed_suite_count"])
        self.assertEqual(10, payload["oracle_suite_summary"]["required_golden_fixture_count"])
        self.assertEqual(10, payload["oracle_suite_summary"]["published_golden_fixture_count"])
        self.assertEqual(["sr4", "sr5", "sr6"], payload["oracle_suite_summary"]["covered_rulesets"])
        self.assertEqual("promoted_desktop_release", payload["oracle_suite_summary"]["release_scope"])
        self.assertEqual("passed", payload["performance_budget_summary"]["coverage_status"])
        self.assertEqual(5, payload["performance_budget_summary"]["required_budget_count"])
        self.assertEqual(5, payload["performance_budget_summary"]["published_budget_count"])
        self.assertEqual(5, payload["performance_budget_summary"]["passed_budget_count"])
        self.assertEqual("promoted_desktop_release", payload["performance_budget_summary"]["release_scope"])
        self.assertEqual(
            [
                "dotnet build Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release --nologo -m:1 && dotnet Chummer.CoreEngine.Tests/bin/Release/net10.0/Chummer.CoreEngine.Tests.dll",
                "dotnet run --project Chummer.Benchmarks/Chummer.Benchmarks.csproj -c Release -- --budget-check --budget-file Chummer.Benchmarks/workspace-benchmark-budgets.json",
            ],
            payload["performance_budget_summary"]["release_commands"],
        )
        self.assertEqual(
            ["dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release"],
            payload["import_oracle_discipline"]["required_source_receipt_commands"],
        )
        self.assertEqual(
            ["core-engine-tests: ok"],
            payload["import_oracle_discipline"]["required_source_receipt_evidence"],
        )
        self.assertEqual(5, payload["import_oracle_discipline"]["required_source_receipt_coverage_total"])
        self.assertEqual(
            {
                "sources_covered": 5,
                "sources_expected": 5,
                "coverage_percent": 100,
            },
            payload["import_oracle_discipline"]["source_receipt_coverage"],
        )
        self.assertEqual([], payload["oracle_suite_summary"]["missing_required_suite_ids"])
        self.assertEqual([], payload["oracle_suite_summary"]["unexpected_published_suite_ids"])
        self.assertEqual([], payload["oracle_suite_summary"]["duplicate_published_suite_ids"])
        self.assertEqual([], payload["performance_budget_summary"]["missing_required_budget_ids"])
        self.assertEqual([], payload["performance_budget_summary"]["unexpected_published_budget_ids"])
        self.assertEqual([], payload["performance_budget_summary"]["duplicate_published_budget_ids"])
        self.assertEqual([], payload["import_oracle_discipline"]["missing_required_source_receipt_commands"])
        self.assertEqual([], payload["import_oracle_discipline"]["missing_required_source_receipt_evidence"])
        self.assertEqual([], payload["import_oracle_discipline"]["unexpected_source_receipt_commands"])
        self.assertEqual([], payload["import_oracle_discipline"]["unexpected_source_receipt_evidence"])
        self.assertEqual([], payload["import_oracle_discipline"]["duplicate_source_receipt_commands"])
        self.assertEqual([], payload["import_oracle_discipline"]["duplicate_source_receipt_evidence"])
        self.assertEqual([], payload["import_oracle_discipline"]["disallowed_source_receipt_command_tokens"])
        self.assertEqual([], payload["import_oracle_discipline"]["disallowed_source_receipt_evidence_tokens"])
        self.assertEqual(
            ["Chummer4", "Chummer5a", "Hero Lab Classic"],
            payload["import_oracle_discipline"]["published_import_oracle_names"],
        )
        self.assertEqual(
            ["Genesis", "CommLink6"],
            payload["import_oracle_discipline"]["published_adjacent_oracle_names"],
        )
        self.assertEqual([], payload["import_oracle_discipline"]["malformed_import_oracle_rows"])
        self.assertEqual([], payload["import_oracle_discipline"]["malformed_adjacent_oracle_rows"])
        self.assertEqual([], payload["import_oracle_discipline"]["unexpected_import_oracle_names"])
        self.assertEqual([], payload["import_oracle_discipline"]["unexpected_adjacent_oracle_names"])
        self.assertEqual("passed", payload["release_channel_binding"]["status"])
        self.assertEqual("docker", payload["release_channel_binding"]["channel_id"])
        self.assertEqual(["src", "tests", "docs", "scripts"], payload["successor_wave_authority"]["queue_allowed_paths"])
        self.assertEqual([], payload["successor_wave_authority"]["unexpected_queue_allowed_paths"])
        self.assertEqual(
            ["engine_proof_pack", "import_oracle_discipline"],
            payload["successor_wave_authority"]["queue_owned_surfaces"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["unexpected_queue_owned_surfaces"])
        self.assertEqual([], payload["successor_wave_authority"]["design_queue_missing_tokens"])
        self.assertEqual([], payload["successor_wave_authority"]["design_queue_missing_proof_anchors"])
        self.assertEqual([], payload["successor_wave_authority"]["off_package_queue_proof_anchors"])
        self.assertEqual([], payload["successor_wave_authority"]["design_queue_off_package_proof_anchors"])
        self.assertIn(
            "/docker/chummercomplete/chummer-core-engine/docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md",
            payload["successor_wave_authority"]["closure_requirements"]["proof_anchors"],
        )
        self.assertEqual({}, payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"])
        self.assertEqual([], payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"])
        self.assertEqual([], payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"])
        self.assertEqual(1, payload["successor_wave_authority"]["queue_package_row_count"])
        self.assertEqual(0, payload["successor_wave_authority"]["duplicate_queue_package_rows"])
        self.assertEqual(1, payload["successor_wave_authority"]["design_queue_package_row_count"])
        self.assertEqual(0, payload["successor_wave_authority"]["duplicate_design_queue_package_rows"])
        self.assertEqual("passed", payload["successor_wave_authority"]["queue_mirror_parity_status"])
        self.assertEqual([], payload["successor_wave_authority"]["off_package_package_commit_citations"])
        self.assertEqual("skipped", payload["successor_wave_authority"]["package_commit_citations"]["status"])
        self.assertIn(
            "8dd516ef",
            [row["commit"] for row in payload["successor_wave_authority"]["package_commit_citations"]["commits"]],
        )
        self.assertEqual([], payload["successor_wave_authority"]["queue_proof_missing_from_design_queue"])
        self.assertEqual([], payload["successor_wave_authority"]["design_queue_proof_missing_from_queue"])
        self.assertEqual(["src", "tests", "docs", "scripts"], payload["successor_wave_authority"]["design_queue_allowed_paths"])
        self.assertEqual(
            ["engine_proof_pack", "import_oracle_discipline"],
            payload["successor_wave_authority"]["design_queue_owned_surfaces"],
        )
        self.assertEqual(
            [
                "avalonia:linux:linux-x64",
                "avalonia:windows:win-x64",
                "avalonia:macos:osx-arm64",
            ],
            payload["release_channel_binding"]["required_promoted_desktop_tuples"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_proof_anchors"])
        matrix_suite = next(row for row in payload["oracle_suites"] if row["id"] == "matrix")
        self.assertEqual("matrix_edge_cases", matrix_suite["coverage_focus"])
        self.assertEqual(["sr4", "sr6"], matrix_suite["rulesets"])
        self.assertEqual("promoted_desktop_release", matrix_suite["release_scope"])
        self.assertEqual(2, matrix_suite["fixture_count"])
        self.assertEqual(2, matrix_suite["golden_fixture_count"])
        self.assertEqual(4, matrix_suite["total_fixture_count"])
        self.assertEqual(
            [
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/sr4-parity-corpus.golden.json",
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/sr6-parity-corpus.golden.json",
            ],
            matrix_suite["golden_fixtures"],
        )
        self.assertEqual([], matrix_suite["missing_golden_fixtures"])
        explain_budget = next(row for row in payload["performance_budgets"] if row["id"] == "explain")
        self.assertEqual("desktop_release_explain", explain_budget["release_gate"])
        self.assertEqual(
            "Compose an explain trace receipt for the promoted desktop release proof pack.",
            explain_budget["scenario"],
        )
        self.assertEqual("promoted_desktop_release", explain_budget["release_scope"])

    def test_generator_check_mode_fails_closed_for_stale_checked_in_receipt(self) -> None:
        payload = self.generator.build_payload(self.root, self.output_path)
        stale_payload = dict(payload)
        stale_payload["package_id"] = "stale-package"
        self.output_path.parent.mkdir(parents=True, exist_ok=True)
        self.output_path.write_text(json.dumps(stale_payload, indent=2) + "\n", encoding="utf-8")

        result = subprocess.run(
            [
                sys.executable,
                str(GENERATOR_PATH),
                "--repo-root",
                str(self.root),
                "--out",
                str(self.output_path),
                "--check",
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            check=False,
        )

        self.assertEqual(1, result.returncode)
        self.assertIn("checked-in receipt is stale", result.stderr)

    def test_build_payload_fails_closed_when_a_suite_evidence_symbol_is_missing(self) -> None:
        (self.root / "Chummer.CoreEngine.Tests" / "Program.cs").write_text("wrong symbol\n", encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("creation", payload["unresolved"]["oracle_suites"])
        self.assertIn("advancement", payload["unresolved"]["oracle_suites"])

    def test_build_payload_fails_closed_when_a_suite_golden_fixture_is_missing(self) -> None:
        (self.root / "Chummer.CoreEngine.Tests" / "Fixtures" / "Contracts" / "sr6-parity-corpus.golden.json").unlink()

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("matrix", payload["unresolved"]["oracle_suites"])
        matrix_suite = next(row for row in payload["oracle_suites"] if row["id"] == "matrix")
        self.assertEqual(
            ["Chummer.CoreEngine.Tests/Fixtures/Contracts/sr6-parity-corpus.golden.json"],
            matrix_suite["missing_golden_fixtures"],
        )

    def test_build_payload_fails_closed_when_budget_workload_is_not_executable(self) -> None:
        source_path = self.root / "Chummer.Benchmarks" / "MigrationWorkspaceBenchmarks.cs"
        source_path.write_text(source_path.read_text(encoding="utf-8").replace("runtime.explain.trace", "runtime.trace"), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("explain", payload["unresolved"]["performance_budgets"])
        explain_budget = next(row for row in payload["performance_budgets"] if row["id"] == "explain")
        self.assertTrue(explain_budget["missing_executable_workload"])

    def test_oracle_suite_summary_fails_closed_when_required_suite_id_is_missing(self) -> None:
        suites = [
            {"id": "creation", "status": "passed", "rulesets": ["sr5"]},
            {"id": "advancement", "status": "passed", "rulesets": ["sr5"]},
            {"id": "augment", "status": "passed", "rulesets": ["sr5"]},
            {"id": "matrix", "status": "passed", "rulesets": ["sr4", "sr6"]},
            {"id": "magic", "status": "passed", "rulesets": ["sr4", "sr5"]},
            {"id": "vehicle", "status": "passed", "rulesets": ["sr4", "sr5"]},
            {"id": "source_toggle", "status": "passed", "rulesets": ["sr5"]},
            {"id": "vehicle", "status": "passed", "rulesets": ["sr4", "sr5"]},
        ]

        summary = self.generator._build_oracle_suite_summary(suites)

        self.assertEqual("failed", summary["coverage_status"])
        self.assertEqual(8, summary["published_suite_count"])
        self.assertEqual(8, summary["passed_suite_count"])
        self.assertEqual(["amend_package"], summary["missing_required_suite_ids"])
        self.assertEqual([], summary["unexpected_published_suite_ids"])
        self.assertEqual(["vehicle"], summary["duplicate_published_suite_ids"])

    def test_oracle_suite_summary_fails_closed_when_unexpected_suite_id_is_present(self) -> None:
        suites = [
            {"id": "creation", "status": "passed", "rulesets": ["sr5"]},
            {"id": "advancement", "status": "passed", "rulesets": ["sr5"]},
            {"id": "augment", "status": "passed", "rulesets": ["sr5"]},
            {"id": "matrix", "status": "passed", "rulesets": ["sr4", "sr6"]},
            {"id": "magic", "status": "passed", "rulesets": ["sr4", "sr5"]},
            {"id": "vehicle", "status": "passed", "rulesets": ["sr4", "sr5"]},
            {"id": "source_toggle", "status": "passed", "rulesets": ["sr5"]},
            {"id": "legacy_shadow", "status": "passed", "rulesets": ["sr5"]},
        ]

        summary = self.generator._build_oracle_suite_summary(suites)

        self.assertEqual("failed", summary["coverage_status"])
        self.assertEqual(8, summary["published_suite_count"])
        self.assertEqual(8, summary["passed_suite_count"])
        self.assertEqual(["amend_package"], summary["missing_required_suite_ids"])
        self.assertEqual(["legacy_shadow"], summary["unexpected_published_suite_ids"])
        self.assertEqual([], summary["duplicate_published_suite_ids"])

    def test_oracle_suite_summary_keeps_required_golden_fixture_count_canonical_when_published_rows_shrink(self) -> None:
        suites = [
            {"id": "creation", "status": "passed", "rulesets": ["sr5"], "golden_fixture_count": 1},
            {"id": "advancement", "status": "passed", "rulesets": ["sr5"], "golden_fixture_count": 0},
            {"id": "augment", "status": "passed", "rulesets": ["sr5"], "golden_fixture_count": 0},
            {"id": "matrix", "status": "passed", "rulesets": ["sr4", "sr6"], "golden_fixture_count": 1},
            {"id": "magic", "status": "passed", "rulesets": ["sr4", "sr5"], "golden_fixture_count": 0},
            {"id": "vehicle", "status": "passed", "rulesets": ["sr4", "sr5"], "golden_fixture_count": 0},
            {"id": "source_toggle", "status": "passed", "rulesets": ["sr5"], "golden_fixture_count": 1},
            {"id": "amend_package", "status": "passed", "rulesets": ["sr5"], "golden_fixture_count": 0},
        ]

        summary = self.generator._build_oracle_suite_summary(suites)

        self.assertEqual(10, summary["required_golden_fixture_count"])
        self.assertEqual(3, summary["published_golden_fixture_count"])
        self.assertEqual("passed", summary["coverage_status"])

    def test_budget_summary_fails_closed_when_required_budget_id_is_missing(self) -> None:
        budgets = [
            {"id": "import", "status": "passed"},
            {"id": "load", "status": "passed"},
            {"id": "diff_apply", "status": "passed"},
            {"id": "explain", "status": "passed"},
            {"id": "explain", "status": "passed"},
        ]

        summary = self.generator._build_budget_summary(budgets)

        self.assertEqual("failed", summary["coverage_status"])
        self.assertEqual(5, summary["published_budget_count"])
        self.assertEqual(5, summary["passed_budget_count"])
        self.assertEqual(["export_prep"], summary["missing_required_budget_ids"])
        self.assertEqual([], summary["unexpected_published_budget_ids"])
        self.assertEqual(["explain"], summary["duplicate_published_budget_ids"])

    def test_budget_summary_fails_closed_when_unexpected_budget_id_is_present(self) -> None:
        budgets = [
            {"id": "import", "status": "passed"},
            {"id": "load", "status": "passed"},
            {"id": "diff_apply", "status": "passed"},
            {"id": "explain", "status": "passed"},
            {"id": "legacy_budget", "status": "passed"},
        ]

        summary = self.generator._build_budget_summary(budgets)

        self.assertEqual("failed", summary["coverage_status"])
        self.assertEqual(5, summary["published_budget_count"])
        self.assertEqual(5, summary["passed_budget_count"])
        self.assertEqual(["export_prep"], summary["missing_required_budget_ids"])
        self.assertEqual(["legacy_budget"], summary["unexpected_published_budget_ids"])
        self.assertEqual([], summary["duplicate_published_budget_ids"])

    def test_build_payload_fails_closed_when_budget_workload_is_only_in_factory(self) -> None:
        source_path = self.root / "Chummer.Benchmarks" / "MigrationWorkspaceBenchmarks.cs"
        source_text = source_path.read_text(encoding="utf-8")
        source_text = source_text.replace(
            '[Benchmark(Description = "runtime.explain.trace")]',
            '[Benchmark(Description = "runtime.explain.summary")]',
        )
        source_path.write_text(source_text, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("explain", payload["unresolved"]["performance_budgets"])
        explain_budget = next(row for row in payload["performance_budgets"] if row["id"] == "explain")
        self.assertTrue(explain_budget["missing_executable_workload"])

    def test_build_payload_fails_closed_when_budget_workload_is_duplicated(self) -> None:
        budget_path = self.root / "Chummer.Benchmarks" / "workspace-benchmark-budgets.json"
        budget_payload = json.loads(budget_path.read_text(encoding="utf-8"))
        budget_payload["workloads"].append(
            {"name": "runtime.explain.trace", "maxMeanMilliseconds": 220, "maxAllocatedBytes": 24000000}
        )
        budget_path.write_text(json.dumps(budget_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("explain", payload["unresolved"]["performance_budgets"])
        explain_budget = next(row for row in payload["performance_budgets"] if row["id"] == "explain")
        self.assertTrue(explain_budget["duplicate_workload"])
        self.assertIn("runtime.explain.trace", explain_budget["duplicate_workload_names"])

    def test_build_payload_fails_closed_when_budget_thresholds_are_malformed(self) -> None:
        budget_path = self.root / "Chummer.Benchmarks" / "workspace-benchmark-budgets.json"
        budget_payload = json.loads(budget_path.read_text(encoding="utf-8"))
        for workload in budget_payload["workloads"]:
            if workload["name"] == "runtime.explain.trace":
                workload["maxMeanMilliseconds"] = "fast"
                workload["maxAllocatedBytes"] = True
        budget_path.write_text(json.dumps(budget_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("explain", payload["unresolved"]["performance_budgets"])
        explain_budget = next(row for row in payload["performance_budgets"] if row["id"] == "explain")
        self.assertTrue(explain_budget["malformed_threshold"])
        self.assertEqual(0, explain_budget["max_mean_milliseconds"])
        self.assertEqual(0, explain_budget["max_allocated_bytes"])

    def test_build_payload_fails_closed_when_budget_thresholds_are_numeric_strings(self) -> None:
        budget_path = self.root / "Chummer.Benchmarks" / "workspace-benchmark-budgets.json"
        budget_payload = json.loads(budget_path.read_text(encoding="utf-8"))
        for workload in budget_payload["workloads"]:
            if workload["name"] == "runtime.explain.trace":
                workload["maxMeanMilliseconds"] = "220"
                workload["maxAllocatedBytes"] = "24000000"
        budget_path.write_text(json.dumps(budget_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("explain", payload["unresolved"]["performance_budgets"])
        explain_budget = next(row for row in payload["performance_budgets"] if row["id"] == "explain")
        self.assertTrue(explain_budget["malformed_threshold"])
        self.assertEqual(0, explain_budget["max_mean_milliseconds"])
        self.assertEqual(0, explain_budget["max_allocated_bytes"])

    def test_build_payload_fails_closed_when_budget_allocated_bytes_are_fractional(self) -> None:
        budget_path = self.root / "Chummer.Benchmarks" / "workspace-benchmark-budgets.json"
        budget_payload = json.loads(budget_path.read_text(encoding="utf-8"))
        for workload in budget_payload["workloads"]:
            if workload["name"] == "workspace.export.bastion":
                workload["maxAllocatedBytes"] = 1024.5
        budget_path.write_text(json.dumps(budget_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("export_prep", payload["unresolved"]["performance_budgets"])
        export_budget = next(row for row in payload["performance_budgets"] if row["id"] == "export_prep")
        self.assertTrue(export_budget["malformed_threshold"])
        self.assertEqual(0, export_budget["max_allocated_bytes"])

    def test_build_payload_fails_closed_when_budget_allocated_bytes_are_integer_valued_float(self) -> None:
        budget_path = self.root / "Chummer.Benchmarks" / "workspace-benchmark-budgets.json"
        budget_payload = json.loads(budget_path.read_text(encoding="utf-8"))
        for workload in budget_payload["workloads"]:
            if workload["name"] == "workspace.export.bastion":
                workload["maxAllocatedBytes"] = 1024.0
        budget_path.write_text(json.dumps(budget_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("export_prep", payload["unresolved"]["performance_budgets"])
        export_budget = next(row for row in payload["performance_budgets"] if row["id"] == "export_prep")
        self.assertTrue(export_budget["malformed_threshold"])
        self.assertEqual(0, export_budget["max_allocated_bytes"])

    def test_build_payload_fails_closed_when_adjacent_import_oracle_is_missing(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = ["Genesis"]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("missing_adjacent_oracle:CommLink6", payload["unresolved"]["import_oracle_discipline"])
        self.assertIn("malformed_adjacent_oracle:Genesis", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_adjacent_import_oracles_are_name_only(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = ["Genesis", "CommLink6"]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("malformed_adjacent_oracle:Genesis", payload["unresolved"]["import_oracle_discipline"])
        self.assertIn("malformed_adjacent_oracle:CommLink6", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_required_import_oracle_is_duplicated(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["import_oracles"] = [
            {"name": "Chummer4", "sources_covered": 0, "sources_expected": 1},
            {"name": "Chummer4", "sources_covered": 1, "sources_expected": 1},
            {"name": "Chummer5a", "sources_covered": 1, "sources_expected": 1},
            {"name": "Hero Lab Classic", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("duplicate_import_oracle:chummer4", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_unexpected_import_oracle_is_present(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["import_oracles"].append({"name": "Legacy Toolbox", "sources_covered": 1, "sources_expected": 1})
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("unexpected_import_oracle:Legacy Toolbox", payload["unresolved"]["import_oracle_discipline"])
        self.assertEqual(
            ["Legacy Toolbox"],
            payload["import_oracle_discipline"]["unexpected_import_oracle_names"],
        )

    def test_build_payload_fails_closed_when_import_oracle_row_is_malformed(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["import_oracles"].append({"sources_covered": 1, "sources_expected": 1})
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("malformed_import_oracle_row:3", payload["unresolved"]["import_oracle_discipline"])
        self.assertEqual([3], payload["import_oracle_discipline"]["malformed_import_oracle_rows"])

    def test_build_payload_fails_closed_when_required_import_oracle_counts_are_malformed(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["import_oracles"] = [
            {"name": "Chummer4", "sources_covered": "all", "sources_expected": 1},
            {"name": "Chummer5a", "sources_covered": 1, "sources_expected": 1},
            {"name": "Hero Lab Classic", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_import_oracle:Chummer4", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_required_import_oracle_counts_are_numeric_strings(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["import_oracles"] = [
            {"name": "Chummer4", "sources_covered": "1", "sources_expected": "1"},
            {"name": "Chummer5a", "sources_covered": 1, "sources_expected": 1},
            {"name": "Hero Lab Classic", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_import_oracle:Chummer4", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_required_import_oracle_counts_are_booleans(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["import_oracles"] = [
            {"name": "Chummer4", "sources_covered": True, "sources_expected": True},
            {"name": "Chummer5a", "sources_covered": 1, "sources_expected": 1},
            {"name": "Hero Lab Classic", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_import_oracle:Chummer4", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_required_import_oracle_counts_are_fractional(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["import_oracles"] = [
            {"name": "Chummer4", "sources_covered": 1.5, "sources_expected": 1.5},
            {"name": "Chummer5a", "sources_covered": 1, "sources_expected": 1},
            {"name": "Hero Lab Classic", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_import_oracle:Chummer4", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_required_import_oracle_counts_are_integer_floats(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["import_oracles"] = [
            {"name": "Chummer4", "sources_covered": 1.0, "sources_expected": 1.0},
            {"name": "Chummer5a", "sources_covered": 1, "sources_expected": 1},
            {"name": "Hero Lab Classic", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_import_oracle:Chummer4", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_required_import_oracle_is_overcounted(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["import_oracles"] = [
            {"name": "Chummer4", "sources_covered": 2, "sources_expected": 1},
            {"name": "Chummer5a", "sources_covered": 1, "sources_expected": 1},
            {"name": "Hero Lab Classic", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_import_oracle:Chummer4", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_adjacent_import_oracle_is_undercovered(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = [
            {"name": "Genesis", "sources_covered": 1, "sources_expected": 1},
            {"name": "CommLink6", "sources_covered": 0, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_adjacent_oracle:CommLink6", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_adjacent_import_oracle_is_duplicated(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = [
            {"name": "Genesis", "sources_covered": 0, "sources_expected": 1},
            {"name": "Genesis", "sources_covered": 1, "sources_expected": 1},
            {"name": "CommLink6", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("duplicate_adjacent_oracle:genesis", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_unexpected_adjacent_import_oracle_is_present(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"].append({"name": "Shadowrun Online", "sources_covered": 1, "sources_expected": 1})
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "unexpected_adjacent_oracle:Shadowrun Online",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertEqual(
            ["Shadowrun Online"],
            payload["import_oracle_discipline"]["unexpected_adjacent_oracle_names"],
        )

    def test_build_payload_fails_closed_when_adjacent_import_oracle_row_is_malformed(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"].append({"sources_covered": 1, "sources_expected": 1})
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("malformed_adjacent_oracle_row:2", payload["unresolved"]["import_oracle_discipline"])
        self.assertEqual([2], payload["import_oracle_discipline"]["malformed_adjacent_oracle_rows"])

    def test_build_payload_fails_closed_when_adjacent_import_oracle_counts_are_malformed(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = [
            {"name": "Genesis", "sources_covered": "all", "sources_expected": 1},
            {"name": "CommLink6", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_adjacent_oracle:Genesis", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_adjacent_import_oracle_counts_are_numeric_strings(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = [
            {"name": "Genesis", "sources_covered": "1", "sources_expected": "1"},
            {"name": "CommLink6", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_adjacent_oracle:Genesis", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_adjacent_import_oracle_counts_are_booleans(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = [
            {"name": "Genesis", "sources_covered": True, "sources_expected": True},
            {"name": "CommLink6", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_adjacent_oracle:Genesis", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_adjacent_import_oracle_counts_are_fractional(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = [
            {"name": "Genesis", "sources_covered": 1.5, "sources_expected": 1.5},
            {"name": "CommLink6", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_adjacent_oracle:Genesis", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_adjacent_import_oracle_counts_are_integer_floats(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = [
            {"name": "Genesis", "sources_covered": 1.0, "sources_expected": 1.0},
            {"name": "CommLink6", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_adjacent_oracle:Genesis", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_adjacent_import_oracle_is_overcounted(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = [
            {"name": "Genesis", "sources_covered": 2, "sources_expected": 1},
            {"name": "CommLink6", "sources_covered": 1, "sources_expected": 1},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("incomplete_adjacent_oracle:Genesis", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_loses_commands(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["commands"] = []
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_commands", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_identity_drifts(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["contract_name"] = "chummer6-core.operator_summary"
        cert["schema_version"] = 2
        cert["proof_kind"] = "status_helper_summary"
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_identity:contract_name", payload["unresolved"]["import_oracle_discipline"])
        self.assertIn("source_receipt_identity:schema_version", payload["unresolved"]["import_oracle_discipline"])
        self.assertIn("source_receipt_identity:proof_kind", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_has_malformed_commands(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["commands"] = [
            "dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release",
            "",
            {"command": "not a release-bound command string"},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_commands_malformed", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_loses_evidence(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["evidence"] = []
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_evidence", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_has_malformed_evidence(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["evidence"] = [
            "core-engine-tests: ok",
            " ",
            {"path": "docs/ENGINE_PROOF_PACK.md"},
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_evidence_malformed", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_loses_required_command(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["commands"] = [
            "dotnet test Chummer.Tests/Chummer.Tests.csproj --filter ImportParity",
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        required_command = "dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release"
        self.assertEqual("failed", payload["status"])
        self.assertIn(
            f"source_receipt_required_command:{required_command}",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertEqual(
            [required_command],
            payload["import_oracle_discipline"]["missing_required_source_receipt_commands"],
        )

    def test_build_payload_fails_closed_when_import_receipt_loses_required_evidence(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["evidence"] = ["import-parity-summary: ok"]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "source_receipt_required_evidence:core-engine-tests: ok",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertEqual(
            ["core-engine-tests: ok"],
            payload["import_oracle_discipline"]["missing_required_source_receipt_evidence"],
        )

    def test_build_payload_fails_closed_when_import_receipt_adds_non_release_bound_rows(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["commands"] = cert["commands"] + [
            "dotnet test Chummer.Tests/Chummer.Tests.csproj --filter ImportParity",
        ]
        cert["evidence"] = cert["evidence"] + [
            "operator-summary: import parity passed",
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "source_receipt_unexpected_command:dotnet test Chummer.Tests/Chummer.Tests.csproj --filter ImportParity",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_unexpected_evidence:operator-summary: import parity passed",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertEqual(
            ["dotnet test Chummer.Tests/Chummer.Tests.csproj --filter ImportParity"],
            payload["import_oracle_discipline"]["unexpected_source_receipt_commands"],
        )
        self.assertEqual(
            ["operator-summary: import parity passed"],
            payload["import_oracle_discipline"]["unexpected_source_receipt_evidence"],
        )

    def test_build_payload_fails_closed_when_import_receipt_has_duplicate_command_or_evidence(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["commands"] = cert["commands"] + cert["commands"]
        cert["evidence"] = cert["evidence"] + cert["evidence"]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        required_command = "dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release"
        self.assertEqual("failed", payload["status"])
        self.assertIn(
            f"source_receipt_duplicate_command:{required_command}",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_duplicate_evidence:core-engine-tests: ok",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertEqual(
            [required_command],
            payload["import_oracle_discipline"]["duplicate_source_receipt_commands"],
        )
        self.assertEqual(
            ["core-engine-tests: ok"],
            payload["import_oracle_discipline"]["duplicate_source_receipt_evidence"],
        )

    def test_build_payload_fails_closed_when_import_receipt_cites_active_run_evidence(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["commands"] = cert["commands"] + ["supervisor status helper import parity pass"]
        cert["evidence"] = cert["evidence"] + ["Prompt path: /var/lib/codex-fleet/run/prompt.txt"]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:supervisor status",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:status helper",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:supervisor status helper",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_evidence_disallowed_active_run_proof:/var/lib/codex-fleet/",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_evidence_disallowed_active_run_proof:prompt path:",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertEqual(
            ["supervisor status", "supervisor status helper", "status helper"],
            payload["import_oracle_discipline"]["disallowed_source_receipt_command_tokens"],
        )
        self.assertEqual(
            ["/var/lib/codex-fleet/", "prompt path:"],
            payload["import_oracle_discipline"]["disallowed_source_receipt_evidence_tokens"],
        )

    def test_build_payload_fails_closed_when_import_receipt_cites_supervisor_status_eta_helper_instruction(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["commands"] = cert["commands"] + [
            "Do not run supervisor status or eta helpers inside this worker run"
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:do not run supervisor status",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:do not run supervisor status or eta helpers",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:supervisor status or eta helpers",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertEqual(
            [
                "do not run supervisor status",
                "do not run supervisor status or eta helpers",
                "supervisor status",
                "supervisor status or eta helpers",
                "ETA helper",
                "ETA helpers",
            ],
            payload["import_oracle_discipline"]["disallowed_source_receipt_command_tokens"],
        )

    def test_build_payload_fails_closed_when_import_receipt_cites_docker_fleet_state_run_path(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["evidence"] = cert["evidence"] + [
            "/docker/fleet/state/chummer_design_supervisor/shard-4/runs/run/prompt.txt"
        ]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "source_receipt_evidence_disallowed_active_run_proof:/docker/fleet/state/chummer_design_supervisor/",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertEqual(
            ["/docker/fleet/state/chummer_design_supervisor/"],
            payload["import_oracle_discipline"]["disallowed_source_receipt_evidence_tokens"],
        )

    def test_build_payload_fails_closed_when_import_receipt_cites_percent_encoded_active_run_evidence(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["commands"] = cert["commands"] + ["supervisor%20helper%20loop%20import%20proof"]
        cert["evidence"] = cert["evidence"] + ["Prompt%20path%3A%20%2Fvar%2Flib%2Fcodex-fleet%2Frun%2Fprompt.txt"]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:supervisor helper",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:helper loop",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_evidence_disallowed_active_run_proof:/var/lib/codex-fleet/",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_evidence_disallowed_active_run_proof:prompt path:",
            payload["unresolved"]["import_oracle_discipline"],
        )

    def test_build_payload_fails_closed_when_import_receipt_cites_form_encoded_active_run_evidence(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["commands"] = cert["commands"] + ["supervisor+helper+loop+import+proof"]
        cert["evidence"] = cert["evidence"] + ["Prompt+path%3A+%2Fvar%2Flib%2Fcodex-fleet%2Frun%2Fprompt.txt"]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:supervisor helper",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:helper loop",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_evidence_disallowed_active_run_proof:/var/lib/codex-fleet/",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_evidence_disallowed_active_run_proof:prompt path:",
            payload["unresolved"]["import_oracle_discipline"],
        )

    def test_build_payload_fails_closed_when_import_receipt_cites_implementation_only_retry_prompt(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["commands"] = cert["commands"] + ["implementation-only retry import proof"]
        cert["evidence"] = cert["evidence"] + ["Previous attempt burned time; import oracle is complete"]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:implementation-only retry",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_command_disallowed_active_run_proof:implementation-only",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_evidence_disallowed_active_run_proof:previous attempt burned time",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertIn(
            "source_receipt_evidence_disallowed_active_run_proof:previous attempt",
            payload["unresolved"]["import_oracle_discipline"],
        )
        self.assertEqual(
            ["implementation-only retry", "implementation-only"],
            payload["import_oracle_discipline"]["disallowed_source_receipt_command_tokens"],
        )
        self.assertEqual(
            ["previous attempt", "previous attempt burned time"],
            payload["import_oracle_discipline"]["disallowed_source_receipt_evidence_tokens"],
        )

    def test_build_payload_fails_closed_when_import_receipt_coverage_summary_is_missing(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert.pop("coverage", None)
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_coverage", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_coverage_summary_is_incomplete(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["coverage"] = {"sources_covered": 3, "sources_expected": 4, "coverage_percent": 75}
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_coverage", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_coverage_summary_omits_adjacent_oracles(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["coverage"] = {"sources_covered": 4, "sources_expected": 4, "coverage_percent": 100}
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual(5, payload["import_oracle_discipline"]["required_source_receipt_coverage_total"])
        self.assertIn("source_receipt_coverage", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_coverage_percent_is_missing(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["coverage"] = {"sources_covered": 5, "sources_expected": 5}
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_coverage", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_coverage_percent_is_string_encoded(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["coverage"] = {"sources_covered": 5, "sources_expected": 5, "coverage_percent": "100"}
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_coverage", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_coverage_percent_is_boolean(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["coverage"] = {"sources_covered": 5, "sources_expected": 5, "coverage_percent": True}
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_coverage", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_coverage_percent_is_under_full(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["coverage"] = {"sources_covered": 5, "sources_expected": 5, "coverage_percent": 99}
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_coverage", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_coverage_percent_is_over_full(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["coverage"] = {"sources_covered": 5, "sources_expected": 5, "coverage_percent": 101}
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_coverage", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_coverage_summary_is_malformed(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["coverage"] = {"sources_covered": True, "sources_expected": 5.5, "coverage_percent": 100}
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_coverage", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_import_receipt_coverage_summary_uses_integer_floats(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["coverage"] = {"sources_covered": 5.0, "sources_expected": 5.0, "coverage_percent": 100}
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("source_receipt_coverage", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_release_channel_loses_promoted_tuple(self) -> None:
        release_path = self.generator.RELEASE_CHANNEL_PATH
        release_payload = json.loads(release_path.read_text(encoding="utf-8"))
        release_payload["desktopTupleCoverage"]["desktopRouteTruth"] = [
            row
            for row in release_payload["desktopTupleCoverage"]["desktopRouteTruth"]
            if row["tupleId"] != "avalonia:windows:win-x64"
        ]
        release_path.write_text(json.dumps(release_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "required_promoted_tuple:avalonia:windows:win-x64",
            payload["unresolved"]["release_channel_binding"],
        )

    def test_build_payload_fails_closed_when_release_channel_primary_tuple_is_not_primary(self) -> None:
        release_path = self.generator.RELEASE_CHANNEL_PATH
        release_payload = json.loads(release_path.read_text(encoding="utf-8"))
        for row in release_payload["desktopTupleCoverage"]["desktopRouteTruth"]:
            if row["tupleId"] == "avalonia:linux:linux-x64":
                row["routeRole"] = "fallback"
                row["parityPosture"] = "explicit_fallback"
        release_path.write_text(json.dumps(release_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "required_promoted_tuple:avalonia:linux:linux-x64",
            payload["unresolved"]["release_channel_binding"],
        )
        linux_tuple = next(
            row
            for row in payload["release_channel_binding"]["promoted_primary_tuples"]
            if row["tuple_id"] == "avalonia:linux:linux-x64"
        )
        self.assertIn("routeRole:fallback", linux_tuple["unresolved"])
        self.assertIn("parityPosture:explicit_fallback", linux_tuple["unresolved"])

    def test_build_payload_fails_closed_when_release_channel_artifact_is_not_on_shelf(self) -> None:
        release_path = self.generator.RELEASE_CHANNEL_PATH
        release_payload = json.loads(release_path.read_text(encoding="utf-8"))
        release_payload["artifacts"] = [
            row
            for row in release_payload["artifacts"]
            if row["artifactId"] != "avalonia-osx-arm64-installer"
        ]
        release_path.write_text(json.dumps(release_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "required_promoted_tuple:avalonia:macos:osx-arm64",
            payload["unresolved"]["release_channel_binding"],
        )
        macos_tuple = next(
            row
            for row in payload["release_channel_binding"]["promoted_primary_tuples"]
            if row["tuple_id"] == "avalonia:macos:osx-arm64"
        )
        self.assertIn("artifact_not_on_shelf:avalonia-osx-arm64-installer", macos_tuple["unresolved"])

    def test_build_payload_fails_closed_when_release_channel_duplicates_promoted_tuple(self) -> None:
        release_path = self.generator.RELEASE_CHANNEL_PATH
        release_payload = json.loads(release_path.read_text(encoding="utf-8"))
        release_payload["desktopTupleCoverage"]["desktopRouteTruth"].append(
            {
                "tupleId": "avalonia:linux:linux-x64",
                "head": "avalonia",
                "platform": "linux",
                "rid": "linux-x64",
                "artifactId": "avalonia-linux-x64-installer",
                "routeRole": "fallback",
                "promotionState": "promoted",
                "parityPosture": "explicit_fallback",
                "updateEligibility": "eligible",
                "revokeState": "not_revoked",
                "installPosture": "installer_first",
            }
        )
        release_path.write_text(json.dumps(release_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "duplicate_desktop_tuple:avalonia:linux:linux-x64",
            payload["unresolved"]["release_channel_binding"],
        )

    def test_build_payload_fails_closed_when_release_channel_duplicates_artifact_id(self) -> None:
        release_path = self.generator.RELEASE_CHANNEL_PATH
        release_payload = json.loads(release_path.read_text(encoding="utf-8"))
        release_payload["artifacts"].append({"artifactId": "avalonia-linux-x64-installer"})
        release_path.write_text(json.dumps(release_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "duplicate_artifact_id:avalonia-linux-x64-installer",
            payload["unresolved"]["release_channel_binding"],
        )

    def test_build_payload_fails_closed_when_release_channel_has_malformed_artifact_rows(self) -> None:
        release_path = self.generator.RELEASE_CHANNEL_PATH
        release_payload = json.loads(release_path.read_text(encoding="utf-8"))
        release_payload["artifacts"].append({"artifactId": ""})
        release_payload["artifacts"].append("avalonia-linux-x64-installer")
        release_path.write_text(json.dumps(release_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("malformed_artifact_row:3", payload["unresolved"]["release_channel_binding"])
        self.assertIn("malformed_artifact_row:4", payload["unresolved"]["release_channel_binding"])

    def test_build_payload_fails_closed_when_release_channel_has_malformed_route_truth_rows(self) -> None:
        release_path = self.generator.RELEASE_CHANNEL_PATH
        release_payload = json.loads(release_path.read_text(encoding="utf-8"))
        release_payload["desktopTupleCoverage"]["desktopRouteTruth"].append({"tupleId": ""})
        release_payload["desktopTupleCoverage"]["desktopRouteTruth"].append("avalonia:linux:linux-x64")
        release_path.write_text(json.dumps(release_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("malformed_desktop_tuple_row:3", payload["unresolved"]["release_channel_binding"])
        self.assertIn("malformed_desktop_tuple_row:4", payload["unresolved"]["release_channel_binding"])

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

    def test_build_payload_fails_closed_when_successor_queue_token_only_exists_on_another_item(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_text = queue_text.replace("    status: complete\n", "    status: in_progress\n")
        queue_text += "\n  - package_id: different-package\n    status: complete\n"
        queue_path.write_text(queue_text, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("status: complete", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_has_duplicate_package_rows(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_text += "\n  - package_id: next90-m104-core-proof-pack\n    status: complete\n"
        queue_path.write_text(queue_text, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(2, payload["successor_wave_authority"]["queue_package_row_count"])
        self.assertEqual(1, payload["successor_wave_authority"]["duplicate_queue_package_rows"])
        self.assertIn(
            "duplicate_queue_item:next90-m104-core-proof-pack",
            payload["successor_wave_authority"]["missing_queue_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_has_duplicate_proof_items(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        proof_item = "/docker/chummercomplete/chummer-core-engine/docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        queue_path.write_text(
            queue_text.replace(f"      - {proof_item}\n", f"      - {proof_item}\n      - {proof_item}\n"),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(
            f"duplicate_proof_item:{proof_item}",
            payload["successor_wave_authority"]["missing_queue_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_loses_completion_status(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(queue_text.replace("    status: complete\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("status: complete", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_closeout_loses_do_not_reopen_handoff(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        closeout_path.write_text(
            closeout_text.replace("Do not reopen this core package for adjacent M104 work.", ""),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_required_tokens", payload["unresolved"]["closeout_document"])
        self.assertIn(
            "Do not reopen this core package for adjacent M104 work.",
            payload["closeout_document"]["missing_tokens"],
        )

    def test_build_payload_fails_closed_when_closeout_loses_benchmark_budget_command(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        benchmark_command = (
            "dotnet run --project Chummer.Benchmarks/Chummer.Benchmarks.csproj -c Release -- "
            "--budget-check --budget-file Chummer.Benchmarks/workspace-benchmark-budgets.json"
        )
        closeout_path.write_text(closeout_text.replace(benchmark_command, ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_required_tokens", payload["unresolved"]["closeout_document"])
        self.assertIn(benchmark_command, payload["closeout_document"]["missing_tokens"])

    def test_build_payload_fails_closed_when_closeout_cites_active_run_evidence_path(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        closeout_path.write_text(
            closeout_text + "\nProof: /var/lib/codex-fleet/run/TASK_LOCAL_TELEMETRY.generated.json\n",
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_disallowed_active_run_evidence", payload["unresolved"]["closeout_document"])
        self.assertIn("/var/lib/codex-fleet/", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("TASK_LOCAL_TELEMETRY.generated.json", payload["closeout_document"]["disallowed_evidence_tokens"])

    def test_build_payload_fails_closed_when_closeout_cites_docker_fleet_state_run_path(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        closeout_path.write_text(
            closeout_text
            + "\nProof: /docker/fleet/state/chummer_design_supervisor/shard-4/runs/run/prompt.txt\n",
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_disallowed_active_run_evidence", payload["unresolved"]["closeout_document"])
        self.assertIn(
            "/docker/fleet/state/chummer_design_supervisor/",
            payload["closeout_document"]["disallowed_evidence_tokens"],
        )

    def test_build_payload_fails_closed_when_closeout_cites_percent_encoded_active_run_evidence(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        closeout_path.write_text(
            closeout_text
            + "\nProof: supervisor%20helper%20loops%20from%20%2Fvar%2Flib%2Fcodex-fleet%2Frun\n",
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_disallowed_active_run_evidence", payload["unresolved"]["closeout_document"])
        self.assertIn("/var/lib/codex-fleet/", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("supervisor helper", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("supervisor helper loops", payload["closeout_document"]["disallowed_evidence_tokens"])

    def test_build_payload_fails_closed_when_closeout_cites_form_encoded_active_run_evidence(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        closeout_path.write_text(
            closeout_text
            + "\nProof: supervisor+helper+loops+from+%2Fvar%2Flib%2Fcodex-fleet%2Frun\n",
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_disallowed_active_run_evidence", payload["unresolved"]["closeout_document"])
        self.assertIn("/var/lib/codex-fleet/", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("supervisor helper", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("supervisor helper loops", payload["closeout_document"]["disallowed_evidence_tokens"])

    def test_build_payload_fails_closed_when_closeout_cites_active_run_handoff_labels(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        closeout_path.write_text(
            closeout_text
            + "\nProof: Open milestone ids: 3227666051; Focus profiles: next_90_day_successor_wave\n",
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_disallowed_active_run_evidence", payload["unresolved"]["closeout_document"])
        self.assertIn("open milestone ids:", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("focus profiles:", payload["closeout_document"]["disallowed_evidence_tokens"])

    def test_build_payload_fails_closed_when_closeout_cites_supervisor_helper_loop_evidence(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        closeout_path.write_text(
            closeout_text + "\nProof: supervisor helper loops reported this package as complete.\n",
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_disallowed_active_run_evidence", payload["unresolved"]["closeout_document"])
        self.assertIn("supervisor helper", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("supervisor helper loop", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("supervisor helper loops", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("helper loop", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("helper loops", payload["closeout_document"]["disallowed_evidence_tokens"])

    def test_build_payload_fails_closed_when_closeout_cites_supervisor_status_eta_helper_instruction(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        closeout_path.write_text(
            closeout_text + "\nProof: Do not run supervisor status or eta helpers inside this worker run.\n",
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_disallowed_active_run_evidence", payload["unresolved"]["closeout_document"])
        self.assertIn("do not run supervisor status", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn(
            "do not run supervisor status or eta helpers",
            payload["closeout_document"]["disallowed_evidence_tokens"],
        )
        self.assertIn(
            "supervisor status or eta helpers",
            payload["closeout_document"]["disallowed_evidence_tokens"],
        )

    def test_build_payload_fails_closed_when_closeout_cites_operator_status_snippet_evidence(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        closeout_path.write_text(
            closeout_text + "\nEvidence: Historical operator status snippets marked this package complete.\n",
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_disallowed_active_run_evidence", payload["unresolved"]["closeout_document"])
        self.assertIn("operator status snippet", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("operator status snippets", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("historical operator status", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("historical operator status snippet", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("historical operator status snippets", payload["closeout_document"]["disallowed_evidence_tokens"])

    def test_build_payload_fails_closed_when_closeout_contains_historical_operator_status_stale_note(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        closeout_path.write_text(
            closeout_text + "\nHistorical operator status snippets are stale notes, not proof.\n",
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_disallowed_active_run_evidence", payload["unresolved"]["closeout_document"])
        self.assertIn("historical operator status", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("historical operator status snippet", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("historical operator status snippets", payload["closeout_document"]["disallowed_evidence_tokens"])

    def test_build_payload_fails_closed_when_closeout_cites_implementation_only_retry_prompt(self) -> None:
        closeout_path = self.root / "docs" / "NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        closeout_text = closeout_path.read_text(encoding="utf-8")
        closeout_path.write_text(
            closeout_text + "\nProof: Previous attempt burned time, this retry is implementation-only.\n",
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["closeout_document"]["status"])
        self.assertIn("closeout_disallowed_active_run_evidence", payload["unresolved"]["closeout_document"])
        self.assertIn("implementation-only", payload["closeout_document"]["disallowed_evidence_tokens"])
        self.assertIn("previous attempt burned time", payload["closeout_document"]["disallowed_evidence_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_loses_frontier_id(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(queue_text.replace("    frontier_id: 3227666051\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("frontier_id: 3227666051", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_loses_landed_commit(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(queue_text.replace("    landed_commit: 00800059\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("landed_commit: 00800059", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_loses_completion_action(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(queue_text.replace("    completion_action: verify_closed_package_only\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("completion_action: verify_closed_package_only", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_loses_do_not_reopen_reason(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "    do_not_reopen_reason: M104 chummer6-core engine proof pack is complete; future shards must verify this receipt, queue row, design queue row, and closeout note instead of reopening the proof-pack package.\n",
                "",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(
            "do_not_reopen_reason: M104 chummer6-core engine proof pack is complete;",
            payload["successor_wave_authority"]["missing_queue_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_loses_allowed_path_authority(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(queue_text.replace("      - scripts\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("- scripts", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_adds_unassigned_allowed_path(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace("      - scripts\n", "      - scripts\n      - Chummer.Core\n"),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(["Chummer.Core"], payload["successor_wave_authority"]["unexpected_queue_allowed_paths"])
        self.assertIn("unexpected_allowed_path:Chummer.Core", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_adds_unassigned_owned_surface(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace("      - import_oracle_discipline\n", "      - import_oracle_discipline\n      - desktop_ui_receipts\n"),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(["desktop_ui_receipts"], payload["successor_wave_authority"]["unexpected_queue_owned_surfaces"])
        self.assertIn("unexpected_owned_surface:desktop_ui_receipts", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_registry_token_only_exists_on_another_milestone(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_text = registry_text.replace("        owner: chummer6-core\n", "        owner: chummer6-ui\n")
        registry_text += "\n  - id: 105\n    work_tasks:\n      - id: 105.1\n        owner: chummer6-core\n"
        registry_path.write_text(registry_text, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("104.1:owner: chummer6-core", payload["successor_wave_authority"]["missing_registry_tokens"])
        self.assertIn("104.2:owner: chummer6-core", payload["successor_wave_authority"]["missing_registry_tokens"])

    def test_build_payload_fails_closed_when_registry_task_completion_token_only_exists_on_later_task(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_text = registry_text.replace("        status: complete\n", "", 1)
        registry_text += (
            "      - id: 104.3\n"
            "        owner: chummer6-core\n"
            "        status: complete\n"
            "        evidence:\n"
            "          - required oracle suites creation, advancement, augment, matrix, magic, vehicle, source_toggle, and amend_package\n"
            "          - python3 tests/test_engine_proof_pack_generator.py exits 0\n"
        )
        registry_path.write_text(registry_text, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("104.1:status: complete", payload["successor_wave_authority"]["missing_registry_tokens"])
        self.assertEqual(
            ["status: complete"],
            payload["successor_wave_authority"]["missing_registry_task_tokens"]["104.1"],
        )

    def test_build_payload_fails_closed_when_successor_registry_task_cites_active_run_proof(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "          - successor_wave_authority=passed\n",
                "          - successor_wave_authority=passed\n"
                "          - Operator Telemetry transcript\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            {"104.2": ["operator telemetry"]},
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"],
        )
        self.assertIn(
            "104.2:disallowed_active_run_proof:operator telemetry",
            payload["successor_wave_authority"]["missing_registry_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_loses_proof_anchor(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        proof_anchor = "/docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md"
        queue_path.write_text(queue_text.replace(f"      - {proof_anchor}\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(proof_anchor, payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_accepts_wrapped_successor_queue_proof_tokens(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        original = (
            "      - /docker/chummercomplete/chummer-core-engine commit 8dd516ef makes failed engine proof pack generation exit nonzero while still\n"
            "        writing diagnostic receipts.\n"
        )
        wrapped = (
            "      - /docker/chummercomplete/chummer-core-engine commit 8dd516ef makes failed engine proof pack\n"
            "        generation exit nonzero while still writing diagnostic receipts.\n"
        )
        queue_path.write_text(queue_text.replace(original, wrapped), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual("passed", payload["successor_wave_authority"]["status"])
        self.assertNotIn("source_queue", payload["unresolved"]["successor_wave_authority"])

    def test_build_payload_fails_closed_when_successor_queue_proof_anchor_does_not_resolve(self) -> None:
        missing_anchor = str(self.root / "docs" / "missing-engine-proof-pack.md")
        original_anchors = self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS
        original_tokens = self.generator.SUCCESSOR_QUEUE_TOKENS
        self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = (missing_anchor,)
        self.generator.SUCCESSOR_QUEUE_TOKENS = original_tokens + (missing_anchor,)
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_text = queue_text.replace(
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
            f"      - {missing_anchor}\n",
        )
        queue_path.write_text(queue_text, encoding="utf-8")
        try:
            payload = self.generator.build_payload(self.root, self.output_path)
        finally:
            self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = original_anchors
            self.generator.SUCCESSOR_QUEUE_TOKENS = original_tokens

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual([missing_anchor], payload["successor_wave_authority"]["missing_queue_proof_anchors"])

    def test_build_payload_fails_closed_when_successor_queue_proof_anchor_only_exists_on_later_item(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        proof_anchor = "/docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md"
        queue_text = queue_text.replace(f"      - {proof_anchor}\n", "")
        queue_text += (
            "\n"
            "  - title: Later unrelated package\n"
            "    package_id: different-package\n"
            "    proof:\n"
            f"      - {proof_anchor}\n"
        )
        queue_path.write_text(queue_text, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(proof_anchor, payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_anchor_points_to_sibling_package_repo(self) -> None:
        original_anchors = self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS
        off_package_anchor = "/docker/chummercomplete/chummer6-ui-finish/scripts/ai/milestones/next90-m104-ui-explain-receipts-check.sh"
        self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = original_anchors + (off_package_anchor,)
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_text = queue_text.replace(
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
            f"      - {off_package_anchor}\n",
        )
        queue_path.write_text(queue_text, encoding="utf-8")
        try:
            payload = self.generator.build_payload(self.root, self.output_path)
        finally:
            self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = original_anchors

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual([off_package_anchor], payload["successor_wave_authority"]["off_package_queue_proof_anchors"])

    def test_build_payload_fails_closed_when_successor_queue_adds_extra_sibling_package_proof_path(self) -> None:
        off_package_anchor = "/docker/chummercomplete/chummer6-ui-finish/scripts/ai/milestones/next90-m104-ui-explain-receipts-check.sh"
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                f"      - {off_package_anchor}\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual([off_package_anchor], payload["successor_wave_authority"]["off_package_queue_proof_anchors"])

    def test_build_payload_fails_closed_when_successor_queue_adds_extra_missing_package_proof_path(self) -> None:
        missing_anchor = "/docker/chummercomplete/chummer-core-engine/docs/missing-extra-m104-proof.md"
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                f"      - {missing_anchor}\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(missing_anchor, payload["successor_wave_authority"]["missing_queue_proof_anchors"])

    def test_build_payload_fails_closed_when_successor_queue_cites_active_run_proof(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - /var/lib/codex-fleet/chummer_design_supervisor/shard-4/ACTIVE_RUN_HANDOFF.generated.md\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["/var/lib/codex-fleet/", "ACTIVE_RUN_HANDOFF"],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_docker_fleet_state_run_path(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - /docker/fleet/state/chummer_design_supervisor/shard-4/runs/run/prompt.txt\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["/docker/fleet/state/chummer_design_supervisor/"],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_task_local_telemetry_proof(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - /var/lib/codex-fleet/chummer_design_supervisor/shard-4/runs/run/TASK_LOCAL_TELEMETRY.generated.json\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["/var/lib/codex-fleet/", "TASK_LOCAL_TELEMETRY", "task-local telemetry"],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_mixed_case_active_run_helper_proof(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Active Run Helper transcript\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["active run helper", "active-run helper"],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_percent_encoded_helper_proof(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - supervisor%20helper%20loops%20from%20%2Fvar%2Flib%2Fcodex-fleet%2Frun\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "/var/lib/codex-fleet/",
                "supervisor helper",
                "supervisor helper loop",
                "supervisor helper loops",
                "helper loop",
                "helper loops",
            ],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_form_encoded_helper_proof(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - supervisor+helper+loops+from+%2Fvar%2Flib%2Fcodex-fleet%2Frun\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "/var/lib/codex-fleet/",
                "supervisor helper",
                "supervisor helper loop",
                "supervisor helper loops",
                "helper loop",
                "helper loops",
            ],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_separator_obfuscated_helper_proof(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - supervisor-helper_loop output reports this package complete\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "supervisor helper",
                "supervisor helper loop",
                "helper loop",
            ],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_successor_wave_telemetry(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - successor-wave telemetry remaining milestones and critical path prove completion\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["successor-wave telemetry", "remaining milestones", "critical path"],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_task_local_telemetry_fields(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - frontier_briefs says status complete; polling_disabled and status_query_supported are true.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["frontier_briefs", "status_query_supported", "polling_disabled"],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_focus_field_names(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - focus_owners and focus_texts said this package was complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["focus_owners", "focus_texts", "focus owners:", "focus texts:"],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_active_run_handoff_field_output(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Prompt path: /var/lib/codex-fleet/chummer_design_supervisor/shard-4/runs/run/prompt.txt\n"
                "      - Open milestone ids: 3227666051\n"
                "      - Focus profiles: next_90_day_successor_wave\n"
                "      - Recent stderr tail reports the package row as complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "/var/lib/codex-fleet/",
                "focus_profiles",
                "open milestone ids:",
                "focus profiles:",
                "prompt path:",
                "recent stderr tail",
            ],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_supervisor_helper_loop_evidence(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - supervisor helper loops reported this package as complete\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "supervisor helper",
                "supervisor helper loop",
                "supervisor helper loops",
                "helper loop",
                "helper loops",
            ],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_supervisor_status_helper_proof(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Supervisor status helper output reports this package complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["supervisor status", "supervisor status helper", "status helper"],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_supervisor_eta_helper_proof(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Supervisor ETA helper output reports this package complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["ETA helper", "supervisor ETA", "supervisor ETA helper"],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_historical_operator_status_snippets(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Historical operator status snippets marked this package complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "operator status snippet",
                "operator status snippets",
                "historical operator status",
                "historical operator status snippet",
                "historical operator status snippets",
            ],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_implementation_only_retry_prompt(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Previous attempt burned time on supervisor loops; this retry is implementation-only.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["implementation-only", "previous attempt", "previous attempt burned time"],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_retry_orientation_prompt(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Run these exact commands first and do not invent another orientation step.\n"
                "      - Writable scope roots: /docker/fleet and /docker/chummercomplete.\n"
                "      - If you stop, report only: What shipped, What remains, Exact blocker.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "run these exact commands first",
                "do not invent another orientation step",
                "writable scope roots:",
                "if you stop, report only:",
                "what shipped:",
                "what remains:",
                "exact blocker:",
            ],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_cites_worker_orientation_rules(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Execution discipline: keep implementation inside the package repo.\n"
                "      - Required order: verify the package first.\n"
                "      - First action rule: open the telemetry file before queue proof.\n"
                "      - Assigned successor queue package: next90-m104-core-proof-pack.\n"
                "      - Assigned slice authority: engine_proof_pack.\n"
                "      - Successor frontier detail: 3227666051 status complete.\n"
                "      - Execution rules inside this run: use the worker-safe handoff.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "first action rule:",
                "execution discipline:",
                "required order:",
                "execution rules inside this run:",
                "assigned slice authority:",
                "assigned successor queue package:",
                "successor frontier detail:",
            ],
            payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"],
        )

    def test_build_payload_fails_closed_when_registry_cites_active_run_handoff_labels(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "        - successor_wave_authority=passed\n",
                "        - successor_wave_authority=passed\n"
                "        - Open milestone ids: 3227666051\n"
                "        - Frontier ids: 3227666051\n"
                "        - Focus texts: next90-m104-core-proof-pack\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["focus_texts", "frontier ids:", "open milestone ids:", "focus texts:"],
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"]["104.2"],
        )

    def test_build_payload_fails_closed_when_registry_cites_task_local_telemetry_fields(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "        - successor_wave_authority=passed\n",
                "        - successor_wave_authority=passed\n"
                "        - first_commands, slice_summary, and frontier_briefs prove this worker completed.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["frontier_briefs", "first_commands", "slice_summary"],
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"]["104.2"],
        )

    def test_build_payload_fails_closed_when_registry_cites_focus_field_names(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "        - successor_wave_authority=passed\n",
                "        - successor_wave_authority=passed\n"
                "        - focus_owners and focus_profiles marked the worker complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["focus_owners", "focus_profiles", "focus profiles:", "focus owners:"],
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"]["104.2"],
        )

    def test_build_payload_fails_closed_when_registry_cites_supervisor_status_helper_proof(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "        - successor_wave_authority=passed\n",
                "        - successor_wave_authority=passed\n"
                "        - Supervisor status helper output reports this package complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["supervisor status", "supervisor status helper", "status helper"],
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"]["104.2"],
        )

    def test_build_payload_fails_closed_when_registry_cites_supervisor_eta_helper_proof(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "        - successor_wave_authority=passed\n",
                "        - successor_wave_authority=passed\n"
                "        - Supervisor ETA helper output reports this package complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["ETA helper", "supervisor ETA", "supervisor ETA helper"],
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"]["104.2"],
        )

    def test_build_payload_fails_closed_when_registry_cites_supervisor_helper_loop_evidence(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "        - successor_wave_authority=passed\n",
                "        - successor_wave_authority=passed\n"
                "        - supervisor helper loops reported this package as complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "supervisor helper",
                "supervisor helper loop",
                "supervisor helper loops",
                "helper loop",
                "helper loops",
            ],
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"]["104.2"],
        )

    def test_build_payload_fails_closed_when_registry_cites_operator_ooda_loop_proof(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "        - successor_wave_authority=passed\n",
                "        - successor_wave_authority=passed\n"
                "        - operator/OODA loop helper output reports this package complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["operator/OODA loop", "OODA loop"],
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"]["104.2"],
        )

    def test_build_payload_fails_closed_when_registry_cites_plain_ooda_loop_proof(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "        - successor_wave_authority=passed\n",
                "        - successor_wave_authority=passed\n"
                "        - OODA loop owns telemetry and reports this package complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["OODA loop"],
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"]["104.2"],
        )

    def test_build_payload_fails_closed_when_registry_cites_implementation_only_retry_prompt(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "        - successor_wave_authority=passed\n",
                "        - successor_wave_authority=passed\n"
                "        - Previous attempt burned time; this retry is implementation-only.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["implementation-only", "previous attempt", "previous attempt burned time"],
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"]["104.2"],
        )

    def test_build_payload_fails_closed_when_registry_cites_retry_orientation_prompt(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "        - successor_wave_authority=passed\n",
                "        - successor_wave_authority=passed\n"
                "        - Current steering focus: next90-m104-core-proof-pack.\n"
                "        - Read these files directly first, then use the shard runtime handoff.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "read these files directly first",
                "use the shard runtime handoff",
                "current steering focus:",
            ],
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"]["104.2"],
        )

    def test_build_payload_fails_closed_when_registry_cites_worker_orientation_rules(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "        - successor_wave_authority=passed\n",
                "        - successor_wave_authority=passed\n"
                "        - Execution discipline: keep implementation inside the package repo.\n"
                "        - Required order: verify the package first.\n"
                "        - First action rule: open the telemetry file before queue proof.\n"
                "        - Assigned successor queue package: next90-m104-core-proof-pack.\n"
                "        - Assigned slice authority: engine_proof_pack.\n"
                "        - Successor frontier detail: 3227666051 status complete.\n"
                "        - Execution rules inside this run: use the worker-safe handoff.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "first action rule:",
                "execution discipline:",
                "required order:",
                "execution rules inside this run:",
                "assigned slice authority:",
                "assigned successor queue package:",
                "successor frontier detail:",
            ],
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"]["104.2"],
        )

    def test_build_payload_fails_closed_when_design_queue_loses_package_authority(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        design_queue_path.write_text("package_id: different-package\n", encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(
            "package_id: next90-m104-core-proof-pack",
            payload["successor_wave_authority"]["design_queue_missing_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_loses_completion_action(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace("    completion_action: verify_closed_package_only\n", ""),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(
            "completion_action: verify_closed_package_only",
            payload["successor_wave_authority"]["design_queue_missing_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_loses_do_not_reopen_reason(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "    do_not_reopen_reason: M104 chummer6-core engine proof pack is complete; future shards must verify this receipt, queue row, design queue row, and closeout note instead of reopening the proof-pack package.\n",
                "",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(
            "do_not_reopen_reason: M104 chummer6-core engine proof pack is complete;",
            payload["successor_wave_authority"]["design_queue_missing_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_fleet_and_design_queue_proof_rows_drift(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        proof_item = "/docker/chummercomplete/chummer-core-engine/docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        design_queue_path.write_text(queue_text.replace(f"      - {proof_item}\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("queue_mirror_parity", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual("failed", payload["successor_wave_authority"]["queue_mirror_parity_status"])
        self.assertIn(
            proof_item,
            payload["successor_wave_authority"]["queue_proof_missing_from_design_queue"],
        )

    def test_build_payload_fails_closed_when_fleet_and_design_queue_do_not_reopen_reason_drifts(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "    do_not_reopen_reason: M104 chummer6-core engine proof pack is complete; future shards must verify this receipt, queue row, design queue row, and closeout note instead of reopening the proof-pack package.\n",
                "    do_not_reopen_reason: M104 chummer6-core engine proof pack is complete; future shards must verify only the design queue row before reopening this slice.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("queue_mirror_parity", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(["do_not_reopen_reason"], payload["successor_wave_authority"]["queue_closure_field_drift"])
        self.assertEqual(
            "M104 chummer6-core engine proof pack is complete; future shards must verify this receipt, queue row, design queue row, and closeout note instead of reopening the proof-pack package.",
            payload["successor_wave_authority"]["queue_do_not_reopen_reason"],
        )
        self.assertEqual(
            "M104 chummer6-core engine proof pack is complete; future shards must verify only the design queue row before reopening this slice.",
            payload["successor_wave_authority"]["design_queue_do_not_reopen_reason"],
        )
        self.assertEqual(
            payload["successor_wave_authority"]["queue_do_not_reopen_reason"],
            payload["queue_do_not_reopen_reason"],
        )
        self.assertEqual(
            payload["successor_wave_authority"]["design_queue_do_not_reopen_reason"],
            payload["design_queue_do_not_reopen_reason"],
        )
        self.assertEqual(
            payload["successor_wave_authority"]["queue_closure_field_drift"],
            payload["queue_closure_field_drift"],
        )

    def test_build_payload_fails_closed_when_fleet_and_design_queue_completion_action_drifts(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "    completion_action: verify_closed_package_only\n",
                "    completion_action: reopen_package\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("queue_mirror_parity", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(["completion_action"], payload["successor_wave_authority"]["queue_closure_field_drift"])
        self.assertEqual("verify_closed_package_only", payload["successor_wave_authority"]["queue_completion_action"])
        self.assertEqual("reopen_package", payload["successor_wave_authority"]["design_queue_completion_action"])
        self.assertEqual(
            payload["successor_wave_authority"]["queue_completion_action"],
            payload["queue_completion_action"],
        )
        self.assertEqual(
            payload["successor_wave_authority"]["design_queue_completion_action"],
            payload["design_queue_completion_action"],
        )
        self.assertEqual(
            payload["successor_wave_authority"]["queue_closure_field_drift"],
            payload["queue_closure_field_drift"],
        )

    def test_build_payload_fails_closed_when_queue_cited_package_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        cited_commit_proof = (
            "      - /docker/chummercomplete/chummer-core-engine commit 2f430d09 "
            "pins the current 498dff3d queue mirror proof floor in the generator, unit tests, and checked-in receipt.\n"
        )
        queue_path.write_text(queue_text + cited_commit_proof, encoding="utf-8")
        design_queue_path.write_text(queue_text + cited_commit_proof, encoding="utf-8")

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("2f430d09") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("package_commit_citations", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["2f430d09"],
            payload["successor_wave_authority"]["package_commit_citations"]["missing_commits"],
        )

    def test_build_payload_fails_closed_when_queue_cites_sibling_repo_commit(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        sibling_commit_proof = (
            "      - /docker/chummercomplete/chummer6-ui-finish commit 1234abcd "
            "claims the UI-owned proof floor is enough to close this package.\n"
        )
        queue_path.write_text(queue_text + sibling_commit_proof, encoding="utf-8")
        design_queue_path.write_text(queue_text + sibling_commit_proof, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("off_package_package_commit_citations", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["/docker/chummercomplete/chummer6-ui-finish commit 1234abcd"],
            payload["successor_wave_authority"]["off_package_package_commit_citations"],
        )

    def test_build_payload_fails_closed_when_design_queue_has_duplicate_package_rows(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        queue_text += "\n  - package_id: next90-m104-core-proof-pack\n    status: complete\n"
        design_queue_path.write_text(queue_text, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(2, payload["successor_wave_authority"]["design_queue_package_row_count"])
        self.assertEqual(1, payload["successor_wave_authority"]["duplicate_design_queue_package_rows"])
        self.assertIn(
            "duplicate_queue_item:next90-m104-core-proof-pack",
            payload["successor_wave_authority"]["design_queue_missing_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_has_duplicate_proof_items(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        proof_item = "/docker/chummercomplete/chummer-core-engine/docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md"
        design_queue_path.write_text(
            queue_text.replace(f"      - {proof_item}\n", f"      - {proof_item}\n      - {proof_item}\n"),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(
            f"duplicate_proof_item:{proof_item}",
            payload["successor_wave_authority"]["design_queue_missing_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_anchor_points_to_sibling_package_repo(self) -> None:
        original_anchors = self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS
        off_package_anchor = "/docker/chummercomplete/chummer6-ui-finish/scripts/ai/milestones/next90-m104-ui-explain-receipts-check.sh"
        self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = original_anchors + (off_package_anchor,)
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        queue_text = queue_text.replace(
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
            f"      - {off_package_anchor}\n",
        )
        design_queue_path.write_text(queue_text, encoding="utf-8")
        try:
            payload = self.generator.build_payload(self.root, self.output_path)
        finally:
            self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = original_anchors

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [off_package_anchor],
            payload["successor_wave_authority"]["design_queue_off_package_proof_anchors"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_adds_extra_sibling_package_proof_path(self) -> None:
        off_package_anchor = "/docker/chummercomplete/chummer6-ui-finish/scripts/ai/milestones/next90-m104-ui-explain-receipts-check.sh"
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                f"      - {off_package_anchor}\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [off_package_anchor],
            payload["successor_wave_authority"]["design_queue_off_package_proof_anchors"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_adds_extra_missing_package_proof_path(self) -> None:
        missing_anchor = "/docker/chummercomplete/chummer-core-engine/docs/missing-extra-m104-proof.md"
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                f"      - {missing_anchor}\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(missing_anchor, payload["successor_wave_authority"]["design_queue_missing_proof_anchors"])
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_cites_active_run_proof(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Active-Run Telemetry Helper Output\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["active-run telemetry"],
            payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_cites_active_run_helper_command_proof(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - active-run helper command transcript\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["active run helper", "active-run helper", "active-run helper command"],
            payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_cites_task_local_telemetry_fields(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - frontier_briefs says status complete; polling_disabled and status_query_supported are true.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["frontier_briefs", "status_query_supported", "polling_disabled"],
            payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_cites_focus_field_names(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - focus_profiles and focus_texts proved this worker was done.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["focus_profiles", "focus_texts", "focus profiles:", "focus texts:"],
            payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_cites_separator_obfuscated_task_local_telemetry(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - task.local.telemetry helper output marked this package complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["TASK_LOCAL_TELEMETRY", "task-local telemetry"],
            payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_cites_docker_fleet_state_run_path(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - /docker/fleet/state/chummer_design_supervisor/shard-4/runs/run/prompt.txt\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["/docker/fleet/state/chummer_design_supervisor/"],
            payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_cites_supervisor_helper_loop_evidence(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - supervisor helper loops reported this package as complete\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "supervisor helper",
                "supervisor helper loop",
                "supervisor helper loops",
                "helper loop",
                "helper loops",
            ],
            payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_cites_supervisor_status_helper_proof(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Supervisor status helper output reports this package complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["supervisor status", "supervisor status helper", "status helper"],
            payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_cites_supervisor_eta_helper_proof(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Supervisor ETA helper output reports this package complete.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["ETA helper", "supervisor ETA", "supervisor ETA helper"],
            payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_cites_worker_orientation_rules(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - Execution discipline: keep implementation inside the package repo.\n"
                "      - Required order: verify the package first.\n"
                "      - First action rule: open the telemetry file before queue proof.\n"
                "      - Assigned successor queue package: next90-m104-core-proof-pack.\n"
                "      - Assigned slice authority: engine_proof_pack.\n"
                "      - Successor frontier detail: 3227666051 status complete.\n"
                "      - Execution rules inside this run: use the worker-safe handoff.\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [
                "first action rule:",
                "execution discipline:",
                "required order:",
                "execution rules inside this run:",
                "assigned slice authority:",
                "assigned successor queue package:",
                "successor frontier detail:",
            ],
            payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_adds_unassigned_allowed_path(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace("      - scripts\n", "      - scripts\n      - Chummer.Core\n"),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(["Chummer.Core"], payload["successor_wave_authority"]["unexpected_design_queue_allowed_paths"])
        self.assertIn(
            "unexpected_allowed_path:Chummer.Core",
            payload["successor_wave_authority"]["design_queue_missing_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_adds_unassigned_owned_surface(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace("      - import_oracle_discipline\n", "      - import_oracle_discipline\n      - desktop_ui_receipts\n"),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(["desktop_ui_receipts"], payload["successor_wave_authority"]["unexpected_design_queue_owned_surfaces"])
        self.assertIn(
            "unexpected_owned_surface:desktop_ui_receipts",
            payload["successor_wave_authority"]["design_queue_missing_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_required_local_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("56048971") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("56048971", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["56048971"])

    def test_build_payload_fails_closed_when_latest_package_guard_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("769e7259") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("769e7259", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["769e7259"])

    def test_build_payload_fails_closed_when_current_package_guard_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("d4b3b0ba") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("d4b3b0ba", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["d4b3b0ba"])

    def test_build_payload_fails_closed_when_latest_current_guard_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("a2173476") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("a2173476", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["a2173476"])

    def test_build_payload_fails_closed_when_active_run_proof_hygiene_guard_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("dafc1205") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("dafc1205", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["dafc1205"])

    def test_build_payload_fails_closed_when_latest_active_run_proof_hygiene_guard_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("65df3894") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("65df3894", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["65df3894"])

    def test_build_payload_fails_closed_when_active_run_hygiene_guard_binding_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("4a56911d") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("4a56911d", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["4a56911d"])

    def test_build_payload_fails_closed_when_current_proof_pack_guard_binding_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("4b124997") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("4b124997", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["4b124997"])

    def test_build_payload_fails_closed_when_latest_proof_pack_guard_authority_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("2187db33") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("2187db33", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["2187db33"])

    def test_build_payload_fails_closed_when_current_m104_proof_pack_authority_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("b488d109") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("b488d109", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["b488d109"])

    def test_build_payload_fails_closed_when_latest_m104_proof_pack_authority_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("b6fddf74") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("b6fddf74", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["b6fddf74"])

    def test_build_payload_fails_closed_when_latest_m104_local_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("3b9a29c2") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("3b9a29c2", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["3b9a29c2"])

    def test_build_payload_fails_closed_when_current_m104_local_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("f6608678") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("f6608678", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["f6608678"])

    def test_build_payload_fails_closed_when_latest_m104_receipt_refresh_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("a3cbb548") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("a3cbb548", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["a3cbb548"])

    def test_build_payload_fails_closed_when_latest_m104_receipt_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("df0527b2") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("df0527b2", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["df0527b2"])

    def test_build_payload_fails_closed_when_current_m104_receipt_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("8574f63f") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("8574f63f", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["8574f63f"])

    def test_build_payload_fails_closed_when_current_m104_proof_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("6b3a662c") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("6b3a662c", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["6b3a662c"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("3b63478f") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("3b63478f", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["3b63478f"])

    def test_build_payload_fails_closed_when_latest_m104_closed_package_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("31c75c02") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("31c75c02", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["31c75c02"])

    def test_build_payload_fails_closed_when_current_m104_closed_package_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("ef46554c") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("ef46554c", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["ef46554c"])

    def test_build_payload_fails_closed_when_latest_m104_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("0771b7ea") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("0771b7ea", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["0771b7ea"])

    def test_build_payload_fails_closed_when_current_m104_engine_proof_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("fdb6a273") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("fdb6a273", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["fdb6a273"])

    def test_build_payload_fails_closed_when_current_m104_engine_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("d2ee91a9") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("d2ee91a9", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["d2ee91a9"])

    def test_build_payload_fails_closed_when_current_m104_engine_proof_floor_queue_citation_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("cd30503f") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("cd30503f", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["cd30503f"])

    def test_build_payload_fails_closed_when_current_m104_queue_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("e10f2739") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("e10f2739", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["e10f2739"])

    def test_build_payload_fails_closed_when_current_m104_queue_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("e7d4270e") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("e7d4270e", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["e7d4270e"])

    def test_build_payload_fails_closed_when_current_m104_helper_hygiene_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("18d03556") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("18d03556", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["18d03556"])

    def test_build_payload_fails_closed_when_current_m104_helper_hygiene_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("f914ce6a") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("f914ce6a", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["f914ce6a"])

    def test_build_payload_fails_closed_when_current_m104_helper_hygiene_queue_citation_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("3c242c2f") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("3c242c2f", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["3c242c2f"])

    def test_build_payload_fails_closed_when_current_m104_queue_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("c2872b40") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("c2872b40", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["c2872b40"])

    def test_build_payload_fails_closed_when_d8_m104_proof_pack_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("d8e826a3") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("d8e826a3", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["d8e826a3"])

    def test_build_payload_fails_closed_when_latest_m104_proof_pack_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("7a1f0e7c") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("7a1f0e7c", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["7a1f0e7c"])

    def test_build_payload_fails_closed_when_d464_m104_proof_pack_local_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("d464cfab") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("d464cfab", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["d464cfab"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("bbc877d7") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("bbc877d7", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["bbc877d7"])

    def test_build_payload_fails_closed_when_latest_m104_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("56ff7283") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("56ff7283", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["56ff7283"])

    def test_build_payload_fails_closed_when_current_head_m104_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("7ae79416") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("7ae79416", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["7ae79416"])

    def test_build_payload_fails_closed_when_current_m104_engine_proof_pack_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("a613bdb2") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("a613bdb2", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["a613bdb2"])

    def test_build_payload_fails_closed_when_current_m104_engine_proof_pack_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("353921e7") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("353921e7", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["353921e7"])

    def test_build_payload_fails_closed_when_9de_m104_proof_pack_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("9de2455b") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("9de2455b", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["9de2455b"])

    def test_build_payload_fails_closed_when_current_m104_proof_pack_local_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("a1a2d956") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("a1a2d956", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["a1a2d956"])

    def test_build_payload_fails_closed_when_latest_m104_proof_pack_local_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("abf63719") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("abf63719", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["abf63719"])

    def test_build_payload_fails_closed_when_current_m104_engine_proof_pack_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("bbc7fba8") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("bbc7fba8", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["bbc7fba8"])

    def test_build_payload_fails_closed_when_latest_m104_engine_proof_pack_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("a1a1d505") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("a1a1d505", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["a1a1d505"])

    def test_build_payload_fails_closed_when_current_m104_queue_proof_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("ea449f7b") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("ea449f7b", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["ea449f7b"])

    def test_build_payload_fails_closed_when_current_m104_queue_proof_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("18365058") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("18365058", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["18365058"])

    def test_build_payload_fails_closed_when_latest_m104_queue_proof_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("5031ee41") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("5031ee41", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["5031ee41"])

    def test_build_payload_fails_closed_when_current_m104_queue_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("cbce6a19") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("cbce6a19", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["cbce6a19"])

    def test_build_payload_fails_closed_when_latest_m104_queue_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("71441924") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("71441924", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["71441924"])

    def test_build_payload_fails_closed_when_current_m104_queue_proof_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("df1330b4") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("df1330b4", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["df1330b4"])

    def test_build_payload_fails_closed_when_current_m104_queue_floor_resolution_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("6610ff2e") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("6610ff2e", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["6610ff2e"])

    def test_build_payload_fails_closed_when_current_m104_duplicate_queue_row_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("2c8742ad") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("2c8742ad", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["2c8742ad"])

    def test_build_payload_fails_closed_when_latest_m104_queue_proof_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("5baebb73") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("5baebb73", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["5baebb73"])

    def test_build_payload_fails_closed_when_latest_m104_queue_proof_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("40babebd") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("40babebd", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["40babebd"])

    def test_build_payload_fails_closed_when_current_m104_proof_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("22171b35") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("22171b35", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["22171b35"])

    def test_build_payload_fails_closed_when_current_m104_non_mutating_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("96eca660") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("96eca660", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["96eca660"])

    def test_build_payload_fails_closed_when_current_m104_queue_bound_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("05e47cff") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("05e47cff", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["05e47cff"])

    def test_build_payload_fails_closed_when_current_m104_queue_bound_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("93d06011") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("93d06011", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["93d06011"])

    def test_build_payload_fails_closed_when_current_m104_queue_bound_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("31aec38a") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("31aec38a", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["31aec38a"])

    def test_build_payload_fails_closed_when_current_m104_queue_bound_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("ceccc309") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("ceccc309", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["ceccc309"])

    def test_build_payload_fails_closed_when_current_m104_worker_safe_closure_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("5dff1a2e") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("5dff1a2e", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["5dff1a2e"])

    def test_build_payload_fails_closed_when_current_m104_worker_safe_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("2301a043") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("2301a043", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["2301a043"])

    def test_build_payload_fails_closed_when_current_m104_worker_safe_proof_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("5c75316f") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("5c75316f", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["5c75316f"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("28be988f") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("28be988f", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["28be988f"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("c6a2ee8e") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("c6a2ee8e", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["c6a2ee8e"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("6684fc89") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("6684fc89", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["6684fc89"])

    def test_build_payload_fails_closed_when_latest_m104_proof_floor_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("ccbfc6b2") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("ccbfc6b2", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["ccbfc6b2"])

    def test_build_payload_fails_closed_when_current_m104_ccb_proof_floor_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("2a3ebcb9") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("2a3ebcb9", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["2a3ebcb9"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("7501f49a") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("7501f49a", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["7501f49a"])

    def test_build_payload_fails_closed_when_current_m104_local_proof_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("ac961fe1") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("ac961fe1", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["ac961fe1"])

    def test_build_payload_fails_closed_when_current_m104_queue_cited_local_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("36311e16") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("36311e16", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["36311e16"])

    def test_build_payload_fails_closed_when_current_m104_queue_cited_proof_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("be5755a6") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("be5755a6", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["be5755a6"])

    def test_build_payload_fails_closed_when_latest_m104_queue_proof_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("8ffec2b1") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("8ffec2b1", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["8ffec2b1"])

    def test_build_payload_fails_closed_when_current_m104_local_queue_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("ee9d88b1") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("ee9d88b1", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["ee9d88b1"])

    def test_build_payload_fails_closed_when_current_m104_local_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("eacefaf2") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("eacefaf2", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["eacefaf2"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("e4e502a1") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("e4e502a1", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["e4e502a1"])

    def test_build_payload_fails_closed_when_current_m104_proof_pack_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("1f2c5724") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("1f2c5724", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["1f2c5724"])

    def test_build_payload_fails_closed_when_current_m104_proof_pack_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("1bcb9b7e") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("1bcb9b7e", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["1bcb9b7e"])

    def test_build_payload_fails_closed_when_current_m104_checked_in_receipt_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("e04d7b88") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("e04d7b88", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["e04d7b88"])

    def test_build_payload_fails_closed_when_current_m104_worker_proof_hygiene_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("58656418") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("58656418", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["58656418"])

    def test_build_payload_fails_closed_when_current_m104_worker_proof_hygiene_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("73638668") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("73638668", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["73638668"])

    def test_build_payload_fails_closed_when_current_m104_latest_worker_proof_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("a404b474") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("a404b474", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["a404b474"])

    def test_build_payload_fails_closed_when_current_m104_proof_pack_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("51bb2d8f") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("51bb2d8f", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["51bb2d8f"])

    def test_build_payload_fails_closed_when_current_m104_proof_pack_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("507f1f6b") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("507f1f6b", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["507f1f6b"])

    def test_build_payload_fails_closed_when_current_m104_proof_pack_guard_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("43638c3e") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("43638c3e", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["43638c3e"])

    def test_build_payload_fails_closed_when_current_m104_proof_pack_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("b0776012") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("b0776012", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["b0776012"])

    def test_build_payload_fails_closed_when_current_m104_engine_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("c58d18e1") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("c58d18e1", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["c58d18e1"])

    def test_build_payload_fails_closed_when_latest_m104_engine_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("67e0f654") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("67e0f654", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["67e0f654"])

    def test_build_payload_fails_closed_when_current_m104_local_engine_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("d584120b") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("d584120b", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["d584120b"])

    def test_build_payload_fails_closed_when_queue_cited_m104_local_engine_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("39c875fd") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("39c875fd", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["39c875fd"])

    def test_build_payload_fails_closed_when_queued_m104_proof_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("f1b6c5ca") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("f1b6c5ca", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["f1b6c5ca"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("faf14925") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("faf14925", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["faf14925"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_requirement_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("64b8f873") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("64b8f873", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["64b8f873"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_requirement_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("06a2e06a") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("06a2e06a", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["06a2e06a"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_receipt_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("6d25fb18") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("6d25fb18", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["6d25fb18"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("cc6cf25b") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("cc6cf25b", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["cc6cf25b"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("bb9af238") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("bb9af238", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["bb9af238"])

    def test_build_payload_fails_closed_when_latest_m104_proof_floor_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("44512fcf") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("44512fcf", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["44512fcf"])

    def test_build_payload_fails_closed_when_latest_m104_local_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("4db6d429") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("4db6d429", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["4db6d429"])

    def test_build_payload_fails_closed_when_current_m104_latest_local_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("adc72a7e") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("adc72a7e", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["adc72a7e"])

    def test_build_payload_fails_closed_when_current_m104_queue_cited_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("5e808a1b") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("5e808a1b", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["5e808a1b"])

    def test_build_payload_fails_closed_when_current_m104_queue_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("c323b4ad") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("c323b4ad", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["c323b4ad"])

    def test_build_payload_fails_closed_when_latest_m104_queue_proof_floor_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("7a432bc3") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("7a432bc3", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["7a432bc3"])

    def test_build_payload_fails_closed_when_current_m104_proof_pack_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("c124e4af") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("c124e4af", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["c124e4af"])

    def test_build_payload_fails_closed_when_current_m104_handoff_anchor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("5a649e57") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("5a649e57", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["5a649e57"])

    def test_build_payload_fails_closed_when_current_m104_handoff_proof_anchor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("c01dfa10") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("c01dfa10", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["c01dfa10"])

    def test_build_payload_fails_closed_when_current_m104_documented_handoff_anchor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("1a98d904") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("1a98d904", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["1a98d904"])

    def test_build_payload_fails_closed_when_current_m104_handoff_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("af67ecfd") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("af67ecfd", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["af67ecfd"])

    def test_build_payload_fails_closed_when_current_m104_handoff_proof_floor_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("870be707") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("870be707", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["870be707"])

    def test_build_payload_fails_closed_when_current_m104_queue_mirror_parity_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("498dff3d") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("498dff3d", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["498dff3d"])

    def test_build_payload_fails_closed_when_current_m104_ooda_telemetry_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("b8000b80") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("b8000b80", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["b8000b80"])

    def test_build_payload_fails_closed_when_current_m104_ooda_proof_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("ecbb466c") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("ecbb466c", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["ecbb466c"])

    def test_build_payload_fails_closed_when_current_m104_active_run_handoff_field_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("a2c8ad9f") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("a2c8ad9f", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["a2c8ad9f"])

    def test_build_payload_fails_closed_when_current_m104_handoff_evidence_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("2c98f61c") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("2c98f61c", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["2c98f61c"])

    def test_build_payload_fails_closed_when_current_m104_handoff_evidence_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("2e4e8e81") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("2e4e8e81", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["2e4e8e81"])

    def test_build_payload_fails_closed_when_current_m104_engine_proof_pack_discipline_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("b5d46938") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("b5d46938", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["b5d46938"])

    def test_build_payload_fails_closed_when_current_m104_engine_proof_pack_discipline_guard_pin_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("c1300863") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("c1300863", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["c1300863"])

    def test_build_payload_fails_closed_when_current_m104_release_bound_proof_discipline_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("8f4702a5") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("8f4702a5", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["8f4702a5"])

    def test_build_payload_fails_closed_when_current_m104_package_commit_citation_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("aeeeaf6e") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("aeeeaf6e", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["aeeeaf6e"])

    def test_list_item_block_for_nested_queue_key_stops_before_later_package(self) -> None:
        text = "\n".join(
            [
                "items:",
                "  - title: Target",
                "    package_id: next90-m104-core-proof-pack",
                "    status: in_progress",
                "  - title: Later",
                "    package_id: different-package",
                "    status: complete",
            ]
        )

        block = self.generator._extract_list_item_block(text, "package_id: next90-m104-core-proof-pack")

        self.assertIn("title: Target", block)
        self.assertIn("status: in_progress", block)
        self.assertNotIn("title: Later", block)
        self.assertNotIn("status: complete", block)

    def test_planned_generated_output_does_not_create_first_run_self_failure(self) -> None:
        self.assertFalse(self.output_path.exists())

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual([], payload["unresolved"]["release_commands"])

    def test_main_returns_nonzero_when_generated_pack_is_failed(self) -> None:
        (self.root / "Chummer.CoreEngine.Tests" / "Program.cs").write_text("wrong symbol\n", encoding="utf-8")
        with mock.patch(
            "sys.argv",
            [
                "generate-engine-proof-pack.py",
                "--repo-root",
                str(self.root),
                "--out",
                str(self.output_path),
            ],
        ):
            exit_code = self.generator.main()

        self.assertEqual(1, exit_code)
        generated = json.loads(self.output_path.read_text(encoding="utf-8"))
        self.assertEqual("failed", generated["status"])
        self.assertIn("creation", generated["unresolved"]["oracle_suites"])

    def test_main_returns_zero_when_generated_pack_passes(self) -> None:
        with mock.patch(
            "sys.argv",
            [
                "generate-engine-proof-pack.py",
                "--repo-root",
                str(self.root),
                "--out",
                str(self.output_path),
            ],
        ):
            exit_code = self.generator.main()

        self.assertEqual(0, exit_code)
        generated = json.loads(self.output_path.read_text(encoding="utf-8"))
        self.assertEqual("passed", generated["status"])

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
        self._write("Chummer.CoreEngine.Tests/Fixtures/Contracts/sr5-parity-corpus.golden.json", "{}\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Contracts/session-ledger.golden.json", "{}\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Contracts/runtime-lock-diff.golden.json", "{}\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Contracts/sr4-parity-corpus.golden.json", "{}\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Contracts/sr6-parity-corpus.golden.json", "{}\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Contracts/explain-trace.golden.json", "{}\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Contracts/runtime-summary.golden.json", "{}\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Contracts/buildkit-manifest.normalized.golden.json", "{}\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Contracts/runtime-lock-install-preview.normalized.golden.json", "{}\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Contracts/runtime-lock-install-candidate.normalized.golden.json", "{}\n")
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
                    '[Benchmark(Description = "workspace.import.bastion")]',
                    'public object ImportBastionLegacyWorkspace() => new();',
                    '[Benchmark(Description = "workspace.section.skills.bastion")]',
                    'public object GetSkillsSectionFromImportedBastionWorkspace() => new();',
                    '[Benchmark(Description = "workspace.save.bastion")]',
                    'public object SaveImportedBastionWorkspace() => new();',
                    '[Benchmark(Description = "runtime.explain.trace")]',
                    'public object ComposeExplainTraceReceipt() => new();',
                    '[Benchmark(Description = "workspace.export.bastion")]',
                    'public object PrepareExportForImportedBastionWorkspace() => new();',
                    "internal static IReadOnlyList<BenchmarkWorkload> CreateBudgetWorkloads() =>",
                    "[",
                    '    new BenchmarkWorkload(Name: "workspace.import.bastion"),',
                    '    new BenchmarkWorkload(Name: "workspace.section.skills.bastion"),',
                    '    new BenchmarkWorkload(Name: "workspace.save.bastion"),',
                    '    new BenchmarkWorkload(Name: "runtime.explain.trace"),',
                    '    new BenchmarkWorkload(Name: "workspace.export.bastion"),',
                    "];",
                ]
            ),
        )
        self._write(
            ".codex-studio/published/IMPORT_PARITY_CERTIFICATION.generated.json",
            json.dumps(
                {
                    "contract_name": "chummer6-core.import_parity_certification",
                    "schema_version": 1,
                    "proof_kind": "local_parity_harness",
                    "status": "passed",
                    "commands": [
                        "dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release"
                    ],
                    "import_oracles": [
                        {"name": "Chummer4", "sources_covered": 1, "sources_expected": 1},
                        {"name": "Chummer5a", "sources_covered": 1, "sources_expected": 1},
                        {"name": "Hero Lab Classic", "sources_covered": 1, "sources_expected": 1},
                    ],
                    "adjacent_oracles": [
                        {"name": "Genesis", "sources_covered": 1, "sources_expected": 1},
                        {"name": "CommLink6", "sources_covered": 1, "sources_expected": 1},
                    ],
                    "coverage": {
                        "sources_covered": 5,
                        "sources_expected": 5,
                        "coverage_percent": 100,
                    },
                    "evidence": [
                        "core-engine-tests: ok"
                    ],
                }
            ),
        )
        self._seed_successor_wave_authority()
        self._write(
            "docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md",
            "\n".join(
                [
                    "# Next90 M104 Core Proof Pack Closeout",
                    "",
                    "Package: `next90-m104-core-proof-pack`",
                    "Frontier: `3227666051`",
                    "Milestone: `104`",
                    "Owner: `chummer6-core`",
                    "",
                    "## Closed Scope",
                    "",
                    "- `.codex-studio/published/ENGINE_PROOF_PACK.generated.json` reports `status=passed`.",
                    "- `successor_wave_authority` reports `status=passed`.",
                    "- `.codex-studio/published/ENGINE_PROOF_PACK.generated.json` publishes `published_golden_fixture_count=10`.",
                    "- Every required oracle suite row keeps explicit checked-in `golden_fixtures` metadata.",
                    "- The Fleet queue mirror and design-owned queue each contain exactly one `next90-m104-core-proof-pack` row.",
                    "- The Fleet queue mirror and design-owned queue keep matching `completion_action` and exact `do_not_reopen_reason` text, so closure instructions cannot drift between mirrors.",
                    "- That row remains `status: complete`, keeps `frontier_id: 3227666051`, keeps `landed_commit: 00800059`, and keeps `completion_action: verify_closed_package_only`.",
                    "- That row keeps a package-specific `do_not_reopen_reason`, so later shards must verify the closed package instead of reopening M104.",
                    "- The row keeps only the assigned allowed paths: `src`, `tests`, `docs`, and `scripts`.",
                    "- The row keeps only the assigned owned surfaces: `engine_proof_pack` and `import_oracle_discipline`.",
                    "- Queue proof anchors resolve inside `/docker/chummercomplete/chummer-core-engine`.",
                    "- Local commit proof includes `498dff3d`, `ecbb466c`, `a2c8ad9f`, `2c98f61c`, `2e4e8e81`, `8f4702a5`, `c84b251f`, `29b17c68`, and `262030df`, the queue-mirror parity guard, package-commit citation floor, release-summary guard, closure-instruction guard, and current M104 proof guard anchors.",
                    "- Registry and queue evidence do not cite task-local telemetry, active-run handoff field labels, or supervisor helper loops as release proof.",
                    "- The same proof-hygiene ban applies after percent-decoding and HTML unescaping, after URL form-decoding, and after separator normalization.",
                    "",
                    "## Do Not Reopen",
                    "",
                    "Do not reopen this core package for adjacent M104 work.",
                    "",
                    "```bash",
                    "python3 scripts/generate-engine-proof-pack.py --check",
                    "python3 tests/test_engine_proof_pack_generator.py",
                    "dotnet build Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release --nologo -m:1",
                    "dotnet Chummer.CoreEngine.Tests/bin/Release/net10.0/Chummer.CoreEngine.Tests.dll",
                    "dotnet run --project Chummer.Benchmarks/Chummer.Benchmarks.csproj -c Release -- --budget-check --budget-file Chummer.Benchmarks/workspace-benchmark-budgets.json",
                    "```",
                ]
            )
            + "\n",
        )

    def _write(self, relative_path: str, content: str) -> None:
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def _seed_successor_wave_authority(self) -> None:
        registry_path = self.root / "successor-registry.yaml"
        queue_path = self.root / "successor-queue.yaml"
        design_queue_path = self.root / "successor-design-queue.yaml"
        registry_path.write_text(
            "\n".join(
                [
                    "milestones:",
                    "  - id: 104",
                    "    title: Engine proof pack, explain budgets, and import-oracle discipline",
                    "    work_tasks:",
                    "      - id: 104.1",
                    "        owner: chummer6-core",
                    "        status: complete",
                    "        evidence:",
                    "          - required oracle suites creation, advancement, augment, matrix, magic, vehicle, source_toggle, and amend_package",
                    "          - python3 tests/test_engine_proof_pack_generator.py exits 0",
                    "      - id: 104.2",
                    "        owner: chummer6-core",
                    "        status: complete",
                    "        evidence:",
                    "          - successor_wave_authority=passed",
                    "          - /docker/chummercomplete/chummer-core-engine/docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md",
                    "          - /docker/chummercomplete/chummer-core-engine commit 8dd516ef makes failed engine proof pack generation exit nonzero while still writing diagnostic receipts.",
                    "          - /docker/chummercomplete/chummer-core-engine commit c88178fa tightens design-owned queue scope proof so canonical allowed-path or owned-surface drift cannot keep M104 passed.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 769e7259 pins local commit proof through guard 56048971 so the completed M104 proof pack cannot pass if the latest guard disappears.",
                    "          - /docker/chummercomplete/chummer-core-engine commit d4b3b0ba requires the current 769e7259 guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit a2173476 requires the current d4b3b0ba guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 4b124997 binds M104 proof pack generation, tests, documentation, and checked-in receipt to active-run hygiene guard 4a56911d.",
                    "          - /docker/chummercomplete/chummer-core-engine commit b488d109 pins the latest M104 proof pack authority so future shards verify the closed package instead of repeating it.",
                    "          - /docker/chummercomplete/chummer-core-engine commit b6fddf74 tightens the current M104 proof pack authority guard so future shards verify the latest closed package.",
                    "          - /docker/chummercomplete/chummer-core-engine commit f6608678 tightens the latest M104 proof pack local guard so future shards verify the closed package.",
                    "          - /docker/chummercomplete/chummer-core-engine commit a3cbb548 refreshes the M104 engine proof receipt after latest local guard tightening.",
                    "          - /docker/chummercomplete/chummer-core-engine commit df0527b2 tightens the M104 proof pack receipt guard so future shards verify the latest closed package.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 8574f63f pins the M104 proof pack receipt guard.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 6b3a662c requires the current 8574f63f guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 3b63478f pins the current 6b3a662c guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit cd30503f pins the current d2ee91a9 engine proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit e10f2739 pins the current cd30503f queue proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 3c242c2f pins the current f914ce6a helper hygiene proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit ea449f7b pins the current c2872b40 queue proof floor guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 18365058 pins the current ea449f7b queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 5031ee41 pins the current 18365058 queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit cbce6a19 pins the current 5031ee41 queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 71441924 pins the current cbce6a19 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit df1330b4 pins the latest 71441924 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 6610ff2e tightens the M104 proof pack so the df1330b4 queue proof floor must resolve locally.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 2c8742ad fail-closes duplicate M104 package rows in Fleet and design queue staging so future shards verify the unique completed package.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 40babebd pins the latest 5baebb73 queue proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 22171b35 pins the current 40babebd proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 96eca660 pins the current c6fbd75f non-mutating proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 05e47cff binds the M104 proof guard to successor queues so future shards verify the current closed-package guard.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 93d06011 pins the current 05e47cff queue-bound proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 31aec38a pins the current 93d06011 queue-bound proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit ceccc309 pins the current 31aec38a queue-bound proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 5dff1a2e tightens worker-safe closure evidence guards for task-local files and run-control helper transcripts.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 2301a043 pins the current 5dff1a2e worker-safe proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 5c75316f pins the current 2301a043 worker-safe proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 28be988f pins the current 5c75316f proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit c6a2ee8e pins the current 28be988f proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 6684fc89 pins the current c6a2ee8e proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit ccbfc6b2 pins the current 6684fc89 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 2a3ebcb9 pins the current ccbfc6b2 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 7501f49a pins the current 2a3ebcb9 proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 36311e16 pins the current ac961fe1 local proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit be5755a6 pins the current db3cc033 queue-cited proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 8ffec2b1 pins the latest be5755a6 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 73638668 pins the current 58656418 worker proof hygiene guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit c58d18e1 pins the current 5f50cb7b engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 39c875fd pins the current d584120b local engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit f1b6c5ca pins the queued 39c875fd proof floor into the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit faf14925 pins the current f1b6c5ca proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 64b8f873 requires the current faf14925 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 06a2e06a pins the current 64b8f873 proof floor requirement in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 6d25fb18 pins the current 06a2e06a proof floor receipt in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit cc6cf25b pins the M104 current proof floor guard.",
                    "          - /docker/chummercomplete/chummer-core-engine commit bb9af238 pins the current cc6cf25b proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 44512fcf pins the current bb9af238 proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 5e808a1b pins the current adc72a7e proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit c323b4ad pins the current 5e808a1b queue proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 7a432bc3 pins the current c323b4ad queue proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit c124e4af pins the current 7a432bc3 proof pack guard floor in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 870be707 pins the current af67ecfd handoff proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit b8000b80 tightens the M104 OODA telemetry proof guard so plain governor-loop evidence cannot close the package.",
                    "          - /docker/chummercomplete/chummer-core-engine commit ecbb466c pins the current b8000b80 OODA proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "          - /docker/chummercomplete/chummer-core-engine commit aeeeaf6e tightens M104 package-commit citation proof so registry, Fleet queue, and design queue package-local commit evidence must resolve locally.",
                    "          - /docker/chummercomplete/chummer-core-engine commit c84b251f pins the M104 package-commit citation guard as an explicit local proof floor.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 29b17c68 tightens the M104 engine proof pack release summaries so suite and budget metadata stay release-bound and explainable.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 262030df tightens the M104 proof-pack closure guards so queue closure instructions and import aggregate coverage fail closed.",
                    "          - dotnet run --project Chummer.Benchmarks/Chummer.Benchmarks.csproj -c Release -- --budget-check --budget-file Chummer.Benchmarks/workspace-benchmark-budgets.json exits 0",
                ]
            )
            + "\n",
            encoding="utf-8",
        )
        queue_text = (
            "\n".join(
                [
                    "items:",
                    "  - package_id: next90-m104-core-proof-pack",
                    "    frontier_id: 3227666051",
                    "    milestone_id: 104",
                    "    repo: chummer6-core",
                    "    status: complete",
                    "    landed_commit: 00800059",
                    "    completion_action: verify_closed_package_only",
                    "    do_not_reopen_reason: M104 chummer6-core engine proof pack is complete; future shards must verify this receipt, queue row, design queue row, and closeout note instead of reopening the proof-pack package.",
                    "    proof:",
                    "      - /docker/chummercomplete/chummer-core-engine/.codex-studio/published/ENGINE_PROOF_PACK.generated.json",
                    "      - /docker/chummercomplete/chummer-core-engine/scripts/generate-engine-proof-pack.py",
                    "      - /docker/chummercomplete/chummer-core-engine/tests/test_engine_proof_pack_generator.py",
                    "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md",
                    "      - /docker/chummercomplete/chummer-core-engine/docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md",
                    "      - /docker/chummercomplete/chummer-core-engine commit 8dd516ef makes failed engine proof pack generation exit nonzero while still writing diagnostic receipts.",
                    "      - /docker/chummercomplete/chummer-core-engine commit c88178fa tightens design-owned queue scope proof so canonical allowed-path or owned-surface drift cannot keep M104 passed.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 769e7259 pins local commit proof through guard 56048971 so future shards verify the closed package instead of repeating it.",
                    "      - /docker/chummercomplete/chummer-core-engine commit d4b3b0ba requires the current 769e7259 guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit a2173476 requires the current d4b3b0ba guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 4b124997 binds M104 proof pack generation, tests, documentation, and checked-in receipt to active-run hygiene guard 4a56911d.",
                    "      - /docker/chummercomplete/chummer-core-engine commit b488d109 pins the latest M104 proof pack authority so future shards verify the closed package instead of repeating it.",
                    "      - /docker/chummercomplete/chummer-core-engine commit b6fddf74 tightens the current M104 proof pack authority guard so future shards verify the latest closed package.",
                    "      - /docker/chummercomplete/chummer-core-engine commit f6608678 tightens the latest M104 proof pack local guard so future shards verify the closed package.",
                    "      - /docker/chummercomplete/chummer-core-engine commit a3cbb548 refreshes the M104 engine proof receipt after latest local guard tightening.",
                    "      - /docker/chummercomplete/chummer-core-engine commit df0527b2 tightens the M104 proof pack receipt guard so future shards verify the latest closed package.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 8574f63f pins the M104 proof pack receipt guard.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 6b3a662c requires the current 8574f63f guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 3b63478f pins the current 6b3a662c guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit cd30503f pins the current d2ee91a9 engine proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit e10f2739 pins the current cd30503f queue proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 3c242c2f pins the current f914ce6a helper hygiene proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit ea449f7b pins the current c2872b40 queue proof floor guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 18365058 pins the current ea449f7b queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 5031ee41 pins the current 18365058 queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit cbce6a19 pins the current 5031ee41 queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 71441924 pins the current cbce6a19 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit df1330b4 pins the latest 71441924 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 6610ff2e tightens the M104 proof pack so the df1330b4 queue proof floor must resolve locally.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 2c8742ad fail-closes duplicate M104 package rows in Fleet and design queue staging so future shards verify the unique completed package.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 40babebd pins the latest 5baebb73 queue proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 22171b35 pins the current 40babebd proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 96eca660 pins the current c6fbd75f non-mutating proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 05e47cff binds the M104 proof guard to successor queues so future shards verify the current closed-package guard.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 93d06011 pins the current 05e47cff queue-bound proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 31aec38a pins the current 93d06011 queue-bound proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit ceccc309 pins the current 31aec38a queue-bound proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 5dff1a2e tightens worker-safe closure evidence guards for task-local files and run-control helper transcripts.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 2301a043 pins the current 5dff1a2e worker-safe proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 5c75316f pins the current 2301a043 worker-safe proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 28be988f pins the current 5c75316f proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit c6a2ee8e pins the current 28be988f proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 6684fc89 pins the current c6a2ee8e proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit ccbfc6b2 pins the current 6684fc89 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 2a3ebcb9 pins the current ccbfc6b2 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 7501f49a pins the current 2a3ebcb9 proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 36311e16 pins the current ac961fe1 local proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit be5755a6 pins the current db3cc033 queue-cited proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 8ffec2b1 pins the latest be5755a6 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 73638668 pins the current 58656418 worker proof hygiene guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit c58d18e1 pins the current 5f50cb7b engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 39c875fd pins the current d584120b local engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit f1b6c5ca pins the queued 39c875fd proof floor into the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit faf14925 pins the current f1b6c5ca proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 64b8f873 requires the current faf14925 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 06a2e06a pins the current 64b8f873 proof floor requirement in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 6d25fb18 pins the current 06a2e06a proof floor receipt in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit cc6cf25b pins the M104 current proof floor guard.",
                    "      - /docker/chummercomplete/chummer-core-engine commit bb9af238 pins the current cc6cf25b proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 44512fcf pins the current bb9af238 proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 5e808a1b pins the current adc72a7e proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit c323b4ad pins the current 5e808a1b queue proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 7a432bc3 pins the current c323b4ad queue proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit c124e4af pins the current 7a432bc3 proof pack guard floor in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 870be707 pins the current af67ecfd handoff proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit b8000b80 tightens the M104 OODA telemetry proof guard so plain governor-loop evidence cannot close the package.",
                    "      - /docker/chummercomplete/chummer-core-engine commit ecbb466c pins the current b8000b80 OODA proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
                    "      - /docker/chummercomplete/chummer-core-engine commit aeeeaf6e tightens M104 package-commit citation proof so registry, Fleet queue, and design queue package-local commit evidence must resolve locally.",
                    "      - /docker/chummercomplete/chummer-core-engine commit c84b251f pins the M104 package-commit citation guard as an explicit local proof floor.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 29b17c68 tightens the M104 engine proof pack release summaries so suite and budget metadata stay release-bound and explainable.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 262030df tightens the M104 proof-pack closure guards so queue closure instructions and import aggregate coverage fail closed.",
                    "    allowed_paths:",
                    "      - src",
                    "      - tests",
                    "      - docs",
                    "      - scripts",
                    "    owned_surfaces:",
                    "      - engine_proof_pack",
                    "      - import_oracle_discipline",
                ]
            )
            + "\n"
        )
        queue_path.write_text(queue_text, encoding="utf-8")
        design_queue_path.write_text(queue_text, encoding="utf-8")
        self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"] = str(registry_path)
        self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"] = str(queue_path)
        self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"] = str(design_queue_path)
        release_channel_path = self.root / "release-channel.generated.json"
        release_channel_path.write_text(
            json.dumps(
                {
                    "status": "published",
                    "rolloutState": "promoted_preview",
                    "channelId": "docker",
                    "version": "run-test",
                    "releaseProof": {"status": "passed"},
                    "artifacts": [
                        {"artifactId": "avalonia-linux-x64-installer"},
                        {"artifactId": "avalonia-win-x64-installer"},
                        {"artifactId": "avalonia-osx-arm64-installer"},
                    ],
                    "desktopTupleCoverage": {
                        "complete": True,
                        "desktopRouteTruth": [
                            {
                                "tupleId": "avalonia:linux:linux-x64",
                                "head": "avalonia",
                                "platform": "linux",
                                "rid": "linux-x64",
                                "artifactId": "avalonia-linux-x64-installer",
                                "routeRole": "primary",
                                "promotionState": "promoted",
                                "parityPosture": "flagship_primary",
                                "updateEligibility": "eligible",
                                "revokeState": "not_revoked",
                                "installPosture": "installer_first",
                            },
                            {
                                "tupleId": "avalonia:windows:win-x64",
                                "head": "avalonia",
                                "platform": "windows",
                                "rid": "win-x64",
                                "artifactId": "avalonia-win-x64-installer",
                                "routeRole": "primary",
                                "promotionState": "promoted",
                                "parityPosture": "flagship_primary",
                                "updateEligibility": "eligible",
                                "revokeState": "not_revoked",
                                "installPosture": "installer_first",
                            },
                            {
                                "tupleId": "avalonia:macos:osx-arm64",
                                "head": "avalonia",
                                "platform": "macos",
                                "rid": "osx-arm64",
                                "artifactId": "avalonia-osx-arm64-installer",
                                "routeRole": "primary",
                                "promotionState": "promoted",
                                "parityPosture": "flagship_primary",
                                "updateEligibility": "eligible",
                                "revokeState": "not_revoked",
                                "installPosture": "installer_first",
                            },
                        ],
                    },
                }
            )
            + "\n",
            encoding="utf-8",
        )
        self.generator.RELEASE_CHANNEL_PATH = release_channel_path


if __name__ == "__main__":
    unittest.main()
