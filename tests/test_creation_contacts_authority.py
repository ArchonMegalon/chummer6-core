#!/usr/bin/env python3

from __future__ import annotations

import re
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]


class CreationContactsAuthoritySourceContracts(unittest.TestCase):
    def test_engine_contract_package_exports_the_typed_authority_surface(self) -> None:
        contracts = (REPO_ROOT / "Chummer.Contracts/Characters/CharacterCreationContactsModels.cs").read_text(
            encoding="utf-8"
        )
        for declaration in (
            "CharacterCreationContactBinding",
            "CharacterCreationContactEdit",
            "CharacterCreationContactsState",
            "CharacterCreationContactPreview",
            "CharacterCreationContactAtomicWritePlan",
            "CharacterCreationContactReceipt",
            "CharacterCreationContactReceiptLookupRequest",
        ):
            self.assertIn(f"public sealed record {declaration}", contracts)
        self.assertIn("public static class CharacterCreationContactBudgetIds", contracts)
        self.assertIn("public static IReadOnlyList<string> All", contracts)
        self.assertIn("CharacterCreationWizardStepIds.ContactsLifestyles", contracts)
        self.assertNotIn("IReadOnlyDictionary", contracts)
        self.assertNotRegex(contracts, r"\b(XDocument|XElement|XmlNode)\b")

    def test_application_input_boundary_never_accepts_a_write_plan_or_xml_path(self) -> None:
        interface = (REPO_ROOT / "Chummer.Application/Characters/ICharacterCreationContactsService.cs").read_text(
            encoding="utf-8"
        )
        models = (REPO_ROOT / "Chummer.Contracts/Characters/CharacterCreationContactsModels.cs").read_text(
            encoding="utf-8"
        )
        preview_request = re.search(
            r"public sealed record CharacterCreationContactPreviewRequest\((.*?)\);",
            models,
            re.DOTALL,
        )
        confirm_request = re.search(
            r"public sealed record CharacterCreationContactConfirmRequest\((.*?)\);",
            models,
            re.DOTALL,
        )
        self.assertIsNotNone(preview_request)
        self.assertIsNotNone(confirm_request)
        for request in (preview_request.group(1), confirm_request.group(1)):
            self.assertNotIn("WritePlan", request)
            self.assertNotIn("Dictionary", request)
            self.assertNotIn("Xml", request)
        self.assertIn("LookupReceipt", interface)

    def test_receipts_stay_in_non_payload_auxiliary_state_and_use_atomic_commit(self) -> None:
        auxiliary = (REPO_ROOT / "Chummer.Contracts/Workspaces/WorkspaceDocumentAuxiliaryState.cs").read_text(
            encoding="utf-8"
        )
        workspace = (REPO_ROOT / "Chummer.Contracts/Workspaces/CharacterWorkspaceModels.cs").read_text(
            encoding="utf-8"
        )
        service = (REPO_ROOT / "Chummer.Application/Characters/CharacterCreationContactsService.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("CharacterCreationContactReceipts", auxiliary)
        self.assertIn("public WorkspacePayloadEnvelope ToEnvelope()", workspace)
        self.assertNotIn("AuxiliaryState", workspace.split("public WorkspacePayloadEnvelope ToEnvelope()", 1)[1].split("}", 1)[0])
        self.assertIn("ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint", service)
        self.assertNotIn("ReplaceWorkspaceDocument(", service)

    def test_service_and_persistence_share_the_deterministic_budget_and_digest_evaluator(self) -> None:
        service = (REPO_ROOT / "Chummer.Application/Characters/CharacterCreationContactsService.cs").read_text(
            encoding="utf-8"
        )
        integrity = (
            REPO_ROOT
            / "Chummer.Application/Characters/CharacterCreationContactReceiptLedgerIntegrity.cs"
        ).read_text(encoding="utf-8")
        evaluator = (
            REPO_ROOT
            / "Chummer.Application/Characters/CharacterCreationContactsAuthorityEvaluator.cs"
        ).read_text(encoding="utf-8")
        authority_call = "CharacterCreationContactsAuthorityEvaluator.Evaluate"
        self.assertIn(authority_call, service)
        self.assertIn(authority_call, integrity)
        self.assertIn("ContactKarmaDiscount", evaluator)
        self.assertIn("FriendsInHighPlaces", evaluator)
        self.assertIn("ContactBudget.Overspend", integrity)
        self.assertIn("HighPlacesBudget.Overspend", integrity)
        for receipt_field in (
            "SourceDigest",
            "RulesDigest",
            "RuntimeDigest",
            "ContactPointsBefore",
            "ContactPointsAfter",
            "ContactPointsRemaining",
            "HighPlacesPointsBefore",
            "HighPlacesPointsAfter",
            "HighPlacesPointsRemaining",
        ):
            self.assertIn(f"receipt.{receipt_field}", integrity)

    def test_hosted_package_plane_runs_the_authority_and_source_contract_tests(self) -> None:
        workflow = (REPO_ROOT / ".github/workflows/package-plane.yml").read_text(encoding="utf-8")
        self.assertIn("CharacterCreationContactsServiceTests", workflow)
        self.assertIn("test_creation_contacts_authority.py", workflow)


if __name__ == "__main__":
    unittest.main()
