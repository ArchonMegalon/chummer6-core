from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts" / "verify_rule_authority_matrix.py"


def load_module():
    spec = importlib.util.spec_from_file_location("verify_rule_authority_matrix", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


class RuleAuthorityMatrixRunnerTests(unittest.TestCase):
    def test_matrix_runner_classifies_only_human_review_failure_as_blocked(self) -> None:
        module = load_module()

        def runner(command: str, timeout_seconds: int) -> dict:
            return {"returncode": 1 if "--require-ready" in command else 0, "stdout_tail": "", "stderr_tail": ""}

        payload = module.build_payload("sr6", timeout_seconds=1, runner=runner)

        self.assertEqual("blocked", payload["status"])
        self.assertEqual(["SR6-G012"], payload["failed_gates"])
        self.assertEqual([], payload["unexpected_failed_gates"])
        human_review_gate = [gate for gate in payload["gates"] if gate["id"] == "SR6-G012"][0]
        self.assertTrue(human_review_gate["expected_ready_blocker"])

    def test_matrix_runner_classifies_non_human_failure_as_fail(self) -> None:
        module = load_module()

        def runner(command: str, timeout_seconds: int) -> dict:
            return {"returncode": 1 if "verify_sr4_table_imports.py" in command else 0, "stdout_tail": "", "stderr_tail": ""}

        payload = module.build_payload("sr4", timeout_seconds=1, runner=runner)

        self.assertEqual("fail", payload["status"])
        self.assertEqual(["SR4-G010"], payload["unexpected_failed_gates"])

    def test_default_runner_treats_zero_test_matches_as_failure(self) -> None:
        module = load_module()

        command = "python3 -c 'print(\"No test matches the given testcase filter `Missing` in test.dll\")'"
        result = module.default_runner(command, timeout_seconds=5)

        self.assertEqual(3, result["returncode"])
        self.assertTrue(result["no_test_matches"])


if __name__ == "__main__":
    unittest.main()
