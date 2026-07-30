from __future__ import annotations

import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]


class LocalContractDependencyBootstrapTests(unittest.TestCase):
    def test_release_ruleset_gate_builds_sibling_contracts_before_no_restore_test(self) -> None:
        script = (REPO_ROOT / "scripts" / "ai" / "test-ruleset-depth.sh").read_text(encoding="utf-8")

        bootstrap = 'bash "$SCRIPT_DIR/build-local-contract-dependencies.sh" Release'
        self.assertIn(bootstrap, script)
        self.assertLess(script.index(bootstrap), script.index('"${rules_command[@]}"'))

    def test_dependency_builder_covers_both_hint_path_contract_owners(self) -> None:
        script = (REPO_ROOT / "scripts" / "ai" / "build-local-contract-dependencies.sh").read_text(encoding="utf-8")

        self.assertIn("chummer-hub-registry/Chummer.Hub.Registry.Contracts", script)
        self.assertIn("chummer.run-services/Chummer.Run.Contracts", script)
        self.assertIn('--configuration "$configuration"', script)

    def test_stable_local_package_version_evicts_the_repo_local_restore_cache(self) -> None:
        script = (REPO_ROOT / "scripts" / "ai" / "bootstrap-contracts-feed.sh").read_text(encoding="utf-8")

        self.assertIn('contracts_cache_root="${NUGET_PACKAGES:-$repo_root/.tmp/nuget/packages}"', script)
        self.assertNotIn('contracts_cache_root="${NUGET_PACKAGES:-$HOME/.nuget/packages}"', script)

    def test_general_builder_forwards_its_configuration_to_sibling_contracts(self) -> None:
        script = (REPO_ROOT / "scripts" / "ai" / "build.sh").read_text(encoding="utf-8")

        self.assertIn('"$(dirname "$0")/build-local-contract-dependencies.sh" "$build_configuration"', script)


if __name__ == "__main__":
    unittest.main()
