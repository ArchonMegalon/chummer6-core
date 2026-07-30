from __future__ import annotations

import importlib.util
import io
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest.mock import patch
from types import SimpleNamespace


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT = REPO_ROOT / "scripts" / "verify_rule_authority_matrix.py"


def load_module():
    spec = importlib.util.spec_from_file_location("verify_rule_authority_matrix", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


class RuleAuthorityMatrixCliTests(unittest.TestCase):
    def test_dotnet_runner_serializes_build_and_rejects_noop_success(self) -> None:
        module = load_module()
        completed = SimpleNamespace(returncode=0, stdout="Build succeeded.\n", stderr="")

        with patch.object(module.subprocess, "run", return_value=completed) as run:
            result = module.default_runner(
                "dotnet test Chummer.Tests/Chummer.Tests.csproj --filter Sr4RuleFactRegistryTests",
                120,
            )

        self.assertEqual(3, result["returncode"])
        self.assertTrue(result["no_test_matches"])
        self.assertIn("-m:1 -p:UseSharedCompilation=false", run.call_args.args[0])

    def test_no_ruleset_arguments_run_both_matrices(self) -> None:
        module = load_module()
        result = {"status": "pass"}

        with patch.object(module, "materialize", return_value=result) as materialize:
            with redirect_stdout(io.StringIO()):
                self.assertEqual(0, module.main([]))

        self.assertEqual(
            [
                unittest.mock.call("sr4", 120),
                unittest.mock.call("sr6", 120),
            ],
            materialize.call_args_list,
        )

    def test_explicit_ruleset_selection_is_preserved(self) -> None:
        module = load_module()

        with patch.object(module, "materialize", return_value={"status": "pass"}) as materialize:
            with redirect_stdout(io.StringIO()):
                self.assertEqual(0, module.main(["sr6", "--timeout-seconds", "45"]))

        materialize.assert_called_once_with("sr6", 45)

    def test_summary_only_omits_passing_gate_output(self) -> None:
        module = load_module()
        result = {
            "ruleset": "sr4",
            "status": "pass",
            "failed_gates": [],
            "unexpected_failed_gates": [],
            "gates": [
                {
                    "id": "SR4-G001",
                    "title": "Copyright boundary",
                    "returncode": 0,
                    "no_test_matches": False,
                    "pass": True,
                    "stdout_tail": "large passing output",
                    "stderr_tail": "",
                }
            ],
        }
        output = io.StringIO()

        with patch.object(module, "materialize", return_value=result):
            with redirect_stdout(output):
                self.assertEqual(0, module.main(["sr4", "--summary-only"]))

        self.assertNotIn("large passing output", output.getvalue())
        self.assertIn('"status": "pass"', output.getvalue())


if __name__ == "__main__":
    unittest.main()
