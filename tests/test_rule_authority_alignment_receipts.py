from __future__ import annotations

import importlib.util
import json
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts" / "materialize_rule_authority_alignment_receipts.py"


def load_module():
    spec = importlib.util.spec_from_file_location("materialize_rule_authority_alignment_receipts", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


class RuleAuthorityAlignmentReceiptTests(unittest.TestCase):
    def test_alignment_receipts_pass_for_current_core_scope(self) -> None:
        module = load_module()
        for ruleset in ("sr4", "sr6"):
            payload = module.build_alignment(ruleset)
            self.assertEqual("pass", payload["status"])
            self.assertEqual("pass", payload["fixture_alignment"]["status"])
            self.assertEqual("pass", payload["explain_alignment"]["status"])

    def test_published_alignment_receipts_exist(self) -> None:
        for ruleset in ("SR4", "SR6"):
            path = REPO_ROOT / ".codex-studio" / "published" / f"{ruleset}_AUTHORITY_ALIGNMENT.generated.json"
            self.assertTrue(path.is_file())
            payload = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual("pass", payload["status"])


if __name__ == "__main__":
    unittest.main()
