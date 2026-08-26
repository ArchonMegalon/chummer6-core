from pathlib import Path
import re
import unittest


REPO_ROOT = Path(__file__).resolve().parents[1]
CONTRACT = REPO_ROOT / "Chummer.Contracts/LifeModules/OriginDossierContracts.cs"


class OriginDossierContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = CONTRACT.read_text(encoding="utf-8")

    def record_body(self, name: str) -> str:
        match = re.search(
            rf"public sealed record {name}\b(?P<body>.*?)(?=\npublic (?:sealed record|static class)|\Z)",
            self.source,
            re.DOTALL,
        )
        self.assertIsNotNone(match, f"missing contract {name}")
        return match.group("body")

    def test_live_turn_reads_to_decision_and_withholds_continuation(self) -> None:
        turn = self.record_body("LifeModuleNarrativeTurnSeed")
        choice = self.record_body("LifeModuleNarrativeChoiceSeed")

        self.assertIn("string VisibleStoryMarkdown", turn)
        self.assertIn("string DecisionPrompt", turn)
        self.assertIn("IReadOnlyList<LifeModuleNarrativeChoiceSeed> LegalChoices", turn)
        self.assertIn("public bool StoryEndsAtDecisionPoint { get; } = true", turn)
        self.assertIn("public bool WithholdsContinuationUntilAccepted { get; } = true", choice)

    def test_story_arc_provider_can_only_propose_for_existing_choices(self) -> None:
        seed = self.record_body("OriginStoryArcSeed")
        proposal = self.record_body("OriginStoryArcProposal")

        self.assertIn("IReadOnlyList<string> AllowedChoiceIds", seed)
        self.assertIn("string BoundSeedDigest", proposal)
        self.assertIn("public bool CanAddLifeModuleChoices { get; }", proposal)
        self.assertIn("public bool AffectsMechanics { get; }", proposal)

    def test_narrative_layers_are_separate_and_noncanonical_layers_have_no_authority(self) -> None:
        canonical = self.record_body("OriginCanonicalNarrativeLayer")
        player = self.record_body("OriginPlayerNarrativeLayer")
        provider = self.record_body("OriginProviderNarrativeLayer")

        self.assertIn("string MechanicsSnapshotDigest", canonical)
        self.assertIn("AcceptedLifeModuleDecisionsOnly", canonical)
        self.assertNotIn("MechanicsSnapshotDigest", player)
        self.assertNotIn("MechanicsSnapshotDigest", provider)
        self.assertIn("PresentationOnly", player)
        self.assertIn("PresentationOnly", provider)
        self.assertIn("public bool AffectsMechanics { get; }", player)
        self.assertIn("public bool AffectsMechanics { get; }", provider)

    def test_publication_identity_is_fixed_while_runner_is_prominent(self) -> None:
        metadata = self.record_body("OriginTechnicalPublicationMetadata")
        attribution = self.record_body("OriginRunnerAttribution")

        self.assertIn('public const string ChummerRunId = "chummer.run"', metadata)
        self.assertIn("public string AuthorId { get; } = ChummerRunId", metadata)
        self.assertIn("public string PublisherId { get; } = ChummerRunId", metadata)
        self.assertIn('public string Placement { get; } = "cover-primary"', attribution)
        self.assertIn("public bool IsProminent { get; } = true", attribution)
        self.assertIn("public bool IsTechnicalAuthor { get; }", attribution)

    def test_votes_and_artifact_rewards_cannot_enter_mechanics(self) -> None:
        proof = self.record_body("OriginNarrativeMechanicsIsolationProof")
        recognition = self.record_body("OriginPublicRecognitionSnapshot")
        manifest = self.record_body("OriginPublicArtifactManifest")

        self.assertNotIn("OriginPublicRecognitionSnapshot", proof)
        self.assertNotIn("VoteCount", proof)
        self.assertNotIn("PublicRank", proof)
        self.assertNotIn("UnlockedArtifactKinds", proof)
        self.assertIn("public bool VotesAffectMechanics { get; }", proof)
        self.assertIn("public bool PublicRewardsAffectMechanics { get; }", proof)
        self.assertIn("public bool ArtifactUnlocksAffectMechanics { get; }", proof)
        self.assertIn("public bool AffectsMechanics { get; }", recognition)
        self.assertIn("public bool PublicMetricsAffectMechanics { get; }", manifest)

    def test_public_release_requires_explicit_owner_consent_and_supports_downloads(self) -> None:
        consent = self.record_body("OriginStoryReleaseConsent")
        artifact = self.record_body("OriginPublicArtifactDescriptor")
        manifest = self.record_body("OriginPublicArtifactManifest")

        self.assertIn("bool ApprovedForPublicRelease", consent)
        self.assertIn("bool ApprovedForPublicDownload", consent)
        self.assertIn("bool CanView", artifact)
        self.assertIn("bool CanDownload", artifact)
        self.assertIn("public bool RequiresExplicitOwnerRelease { get; } = true", manifest)

    def test_public_reward_artifacts_are_provider_neutral(self) -> None:
        artifact_kinds = self.source[
            self.source.index("public static class OriginPublicArtifactKinds") :
            self.source.index("public static class OriginArtifactAvailabilityStates")
        ]

        self.assertIn('public const string Audiobook = "audiobook"', artifact_kinds)
        self.assertIn('public const string RenderedScene = "rendered-scene"', artifact_kinds)
        self.assertNotIn("Magicfit", artifact_kinds)
        self.assertNotIn("NeuronWriter", artifact_kinds)


if __name__ == "__main__":
    unittest.main()
