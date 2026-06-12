from __future__ import annotations

import unittest
from pathlib import Path

import yaml


REPO_ROOT = Path(__file__).resolve().parents[1]
RULESET_ROOTS = [
    REPO_ROOT / "docs" / "rulesets" / "sr4-rule-authority",
    REPO_ROOT / "docs" / "rulesets" / "sr6-rule-authority",
]
DOTNET_TEST_PREFIX = "dotnet test Chummer.Tests/Chummer.Tests.csproj --framework net10.0 --no-restore --filter "


def load_yaml(path: Path) -> dict:
    return yaml.safe_load(path.read_text(encoding="utf-8")) or {}


def verification_commands(path: Path) -> list[str]:
    payload = load_yaml(path)
    if "gates" in payload:
        return [str(gate.get("command")) for gate in payload["gates"] if isinstance(gate, dict) and gate.get("command")]
    commands: list[str] = []
    for package in payload.get("workpackages", []):
        if isinstance(package, dict):
            commands.extend(str(command) for command in package.get("verification", []) if command)
    return commands


class RuleAuthorityVerificationCommandTests(unittest.TestCase):
    def test_dotnet_verification_commands_are_project_and_framework_pinned(self) -> None:
        for root in RULESET_ROOTS:
            for path in (root / "VERIFICATION_MATRIX.yaml", root / f"{root.name.split('-')[0].upper()}_IMPLEMENTATION_WORKPACKAGES.yaml"):
                with self.subTest(path=path):
                    commands = verification_commands(path)
                    dotnet_commands = [command for command in commands if command.startswith("dotnet test")]
                    self.assertGreater(len(dotnet_commands), 0)
                    for command in dotnet_commands:
                        self.assertTrue(command.startswith(DOTNET_TEST_PREFIX), command)
                        self.assertNotEqual("dotnet test --filter", command[:21])

    def test_human_review_matrix_gates_require_ready_signoff(self) -> None:
        for root in RULESET_ROOTS:
            with self.subTest(root=root):
                commands = verification_commands(root / "VERIFICATION_MATRIX.yaml")
                human_review_commands = [command for command in commands if "verify_rule_authority_human_review.py" in command]
                self.assertEqual(1, len(human_review_commands))
                self.assertIn("--require-ready", human_review_commands[0])


if __name__ == "__main__":
    unittest.main()
