from __future__ import annotations

import contextlib
import io
import re
import textwrap
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from unittest.mock import patch


WORKFLOW = Path(__file__).resolve().parents[1] / ".github/workflows/package-plane.yml"
NAMESPACE = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
CLASSES = {
    "Chummer.Tests.WorkspaceRuleQuestionServiceTests": 104,
    "Chummer.Tests.WorkspaceRuleQuestionIntegrityTests": 4,
}
ZERO_COUNTERS = (
    "failed", "error", "timeout", "aborted", "inconclusive",
    "passedButRunAborted", "notRunnable", "notExecuted", "disconnected",
    "warning", "completed", "inProgress", "pending",
)


class WorkspaceRuleQuestionWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")
        cls.run_step = cls.workflow.split(
            "      - name: Build and run workspace rule question tests\n", 1
        )[1].split("      - name: Retain original workspace rule question TRX\n", 1)[0]
        cls.upload_step = cls.workflow.split(
            "      - name: Retain original workspace rule question TRX\n", 1
        )[1].split("      - name:", 1)[0]
        cls.verifier = textwrap.dedent(
            cls.run_step.split("<<'PY'\n", 1)[1].rsplit("          PY\n", 1)[0]
        )

    def test_hosted_step_reuses_exact_sdk_restored_project_and_only_two_classes(self) -> None:
        self.assertLess(
            self.workflow.index("- name: Build and run affected authority tests"),
            self.workflow.index("- name: Build and run workspace rule question tests"),
        )
        self.assertLess(
            self.workflow.index("- name: Build and run workspace rule question tests"),
            self.workflow.index("- name: Restore deterministic feature-slice graph"),
        )
        self.assertIn('test "$(dotnet --version)" = "10.0.103"', self.run_step)
        self.assertIn("dotnet test Chummer.Tests/Chummer.Tests.csproj", self.run_step)
        for flag in ("--configuration Release", "--framework net10.0", "--no-restore", "--disable-build-servers", "-m:1"):
            self.assertIn(flag, self.run_step)
        self.assertNotIn("dotnet restore", self.run_step)
        self.assertNotIn("--no-build", self.run_step)
        self.assertEqual(
            ["|".join(f"FullyQualifiedName~{name}" for name in CLASSES)],
            re.findall(r'--filter "([^"]+)"', self.run_step),
        )
        self.assertIn("timeout-minutes: 10", self.run_step)
        self.assertIn("set -euo pipefail", self.run_step)
        self.assertNotIn("continue-on-error", self.run_step)
        self.assertIn("python3 -m unittest -v tests/test_workspace_rule_question_workflow.py", self.run_step)

    def test_original_trx_is_unique_to_run_attempt_and_retained_even_on_failure(self) -> None:
        result_directory = "${{ runner.temp }}/chummer-workspace-rule-question-${{ github.run_id }}-${{ github.run_attempt }}"
        self.assertIn(f"WORKSPACE_RULE_RESULTS: {result_directory}", self.run_step)
        self.assertIn('test ! -e "${WORKSPACE_RULE_RESULTS}"', self.run_step)
        self.assertIn('--results-directory "${WORKSPACE_RULE_RESULTS}"', self.run_step)
        self.assertIn('--logger "trx;LogFileName=workspace-rule-question.trx"', self.run_step)
        self.assertIn('python3 - "${WORKSPACE_RULE_RESULTS}/workspace-rule-question.trx"', self.run_step)
        self.assertIn("if: always()", self.upload_step)
        self.assertIn("actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02", self.upload_step)
        self.assertIn("name: chummer-workspace-rule-question-${{ github.run_id }}-${{ github.run_attempt }}", self.upload_step)
        self.assertIn(f"path: {result_directory}/workspace-rule-question.trx", self.upload_step)
        for setting in ("if-no-files-found: error", "retention-days: 5", "overwrite: false", "include-hidden-files: false"):
            self.assertIn(setting, self.upload_step)
        self.assertNotIn(".write(", self.verifier)
        self.assertNotIn("write_text", self.verifier)

    @staticmethod
    def runner_shape_fixture() -> ET.Element:
        # Synthetic parser fixture only: never used as hosted execution evidence.
        def tag(name: str) -> str:
            return f"{{{NAMESPACE}}}{name}"

        root = ET.Element(tag("TestRun"))
        summary = ET.SubElement(root, tag("ResultSummary"), outcome="Completed")
        ET.SubElement(summary, tag("Counters"), total="108", executed="108", passed="108", **{name: "0" for name in ZERO_COUNTERS})
        definitions = ET.SubElement(root, tag("TestDefinitions"))
        results = ET.SubElement(root, tag("Results"))
        for class_name, count in CLASSES.items():
            for index in range(count):
                identity = f"{class_name}:{index}"
                definition = ET.SubElement(definitions, tag("UnitTest"), id=identity)
                ET.SubElement(definition, tag("TestMethod"), className=class_name)
                ET.SubElement(results, tag("UnitTestResult"), testId=identity, executionId=identity, outcome="Passed")
        return root

    def verify_fixture(self, root: ET.Element) -> None:
        with patch("xml.etree.ElementTree.parse", return_value=ET.ElementTree(root)), \
                patch("sys.argv", ["verify-trx", "original-runner-output.trx"]), \
                contextlib.redirect_stdout(io.StringIO()):
            exec(compile(self.verifier, "package-plane-workspace-rule-verifier", "exec"), {})

    def test_verifier_admits_exact_pass_counts_without_rewriting_the_trx(self) -> None:
        root = self.runner_shape_fixture()
        before = ET.tostring(root)
        self.verify_fixture(root)
        self.assertEqual(before, ET.tostring(root))

    def test_verifier_rejects_missing_wrong_or_nonzero_counters(self) -> None:
        for name in ("total", "executed", "passed", *ZERO_COUNTERS, "skipped"):
            with self.subTest(counter=name):
                root = self.runner_shape_fixture()
                counters = root.find(f"{{{NAMESPACE}}}ResultSummary/{{{NAMESPACE}}}Counters")
                counters.set(name, "1")
                with self.assertRaises(SystemExit):
                    self.verify_fixture(root)
        for name in ("total", "executed", "passed", *ZERO_COUNTERS):
            with self.subTest(missing=name):
                root = self.runner_shape_fixture()
                counters = root.find(f"{{{NAMESPACE}}}ResultSummary/{{{NAMESPACE}}}Counters")
                del counters.attrib[name]
                with self.assertRaises(SystemExit):
                    self.verify_fixture(root)

    def test_verifier_rejects_missing_results_failed_rows_duplicates_and_class_drift(self) -> None:
        for mutation in ("missing-row", "failed-row", "skipped-row", "duplicate-execution", "unknown-test", "wrong-class", "wrong-class-count", "incomplete-run"):
            with self.subTest(mutation=mutation):
                root = self.runner_shape_fixture()
                results = root.find(f"{{{NAMESPACE}}}Results")
                methods = root.findall(f"{{{NAMESPACE}}}TestDefinitions/{{{NAMESPACE}}}UnitTest/{{{NAMESPACE}}}TestMethod")
                if mutation == "missing-row":
                    results.remove(results[0])
                elif mutation in ("failed-row", "skipped-row"):
                    results[0].set("outcome", "Failed" if mutation == "failed-row" else "NotExecuted")
                elif mutation == "duplicate-execution":
                    results[0].set("executionId", results[1].get("executionId"))
                elif mutation == "unknown-test":
                    results[0].set("testId", "unknown")
                elif mutation in ("wrong-class", "wrong-class-count"):
                    methods[0].set("className", "UnrelatedTests" if mutation == "wrong-class" else list(CLASSES)[1])
                else:
                    root.find(f"{{{NAMESPACE}}}ResultSummary").set("outcome", "Aborted")
                with self.assertRaises(SystemExit):
                    self.verify_fixture(root)


if __name__ == "__main__":
    unittest.main()
