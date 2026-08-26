namespace Chummer.Contracts.LifeModules;

/// <summary>
/// Versioned contract names for the live Origin Dossier projection. These
/// payloads are ruleset-neutral; ruleset-specific services supply only
/// canonical life-module decisions, facts, and source anchors.
/// </summary>
public static class OriginDossierSchemas
{
    public const string NarrativeTurnSeedV1 = "chummer.origin_dossier.narrative_turn_seed.v1";
    public const string StoryArcSeedV1 = "chummer.origin_dossier.story_arc_seed.v1";
    public const string StoryArcProposalV1 = "chummer.origin_dossier.story_arc_proposal.v1";
    public const string EditionSnapshotV1 = "chummer.origin_dossier.edition_snapshot.v1";
    public const string PublicArtifactManifestV1 = "chummer.origin_dossier.public_artifact_manifest.v1";
    public const string MechanicsIsolationProofV1 = "chummer.origin_dossier.mechanics_isolation_proof.v1";
}

/// <summary>
/// Fixed technical publication identity. A player owns and is prominently
/// named in their runner's story, but is never silently represented as its
/// legal or technical author by this contract.
/// </summary>
public sealed record OriginTechnicalPublicationMetadata
{
    public const string ChummerRunId = "chummer.run";
    public const string ChummerRunDisplayName = "Chummer.run";

    public string AuthorId { get; } = ChummerRunId;

    public string AuthorDisplayName { get; } = ChummerRunDisplayName;

    public string PublisherId { get; } = ChummerRunId;

    public string PublisherDisplayName { get; } = ChummerRunDisplayName;
}

public static class OriginNarrativeLayerKinds
{
    public const string Canonical = "canonical";
    public const string Player = "player";
    public const string Provider = "provider";
}

public static class OriginNarrativeAuthorityKinds
{
    public const string AcceptedLifeModuleDecisionsOnly = "accepted-life-module-decisions-only";
    public const string PresentationOnly = "presentation-only";
}

public static class OriginNarrativeNonMechanicsInputKinds
{
    public const string PlayerProse = "player-prose";
    public const string ProviderProse = "provider-prose";
    public const string PublicVotes = "public-votes";
    public const string PublicRank = "public-rank";
    public const string PublicRecognition = "public-recognition";
    public const string PublicArtifactRewards = "public-artifact-rewards";
    public const string ArtifactUnlocks = "artifact-unlocks";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        PlayerProse,
        ProviderProse,
        PublicVotes,
        PublicRank,
        PublicRecognition,
        PublicArtifactRewards,
        ArtifactUnlocks
    });
}

public static class OriginPublicArtifactKinds
{
    public const string OriginBook = "origin-book";
    public const string Audiobook = "audiobook";
    public const string RenderedScene = "rendered-scene";
    public const string DownloadBundle = "download-bundle";
}

public static class OriginArtifactAvailabilityStates
{
    public const string Planned = "planned";
    public const string Eligible = "eligible";
    public const string Rendering = "rendering";
    public const string Available = "available";
    public const string Rejected = "rejected";
}

public static class OriginStoryReleaseStates
{
    public const string PrivateDraft = "private-draft";
    public const string ConsentRequired = "consent-required";
    public const string ApprovedForPublicRelease = "approved-for-public-release";
    public const string Published = "published";
    public const string Withdrawn = "withdrawn";
}

/// <summary>
/// A canonical fact is derived from an accepted choice and may be used to
/// narrate that choice. It is not a copy of sourcebook prose.
/// </summary>
public sealed record OriginCanonicalNarrativeFact(
    string FactId,
    string FactKind,
    string LocalizedSummary,
    string AcceptedDecisionId,
    IReadOnlyList<string> SourceAnchorIds,
    string FactDigest);

/// <summary>
/// A choice the engine has already found legal. The next story passage is
/// deliberately absent: the reader sees the story only through the current
/// decision point and the continuation is projected after acceptance.
/// </summary>
public sealed record LifeModuleNarrativeChoiceSeed(
    string ChoiceId,
    string Label,
    string DecisionCommandDigest,
    string MechanicsPreviewDigest,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsLegal)
{
    public bool WithholdsContinuationUntilAccepted { get; } = true;
}

/// <summary>
/// Live read-then-decide payload for one life-module turn. VisibleStoryMarkdown
/// ends at DecisionPrompt. A successful choice produces a new seed whose story
/// continues from the accepted decision; it never pre-renders later branches.
/// </summary>
public sealed record LifeModuleNarrativeTurnSeed(
    string Schema,
    string RulesetId,
    string WorkspaceId,
    long WorkspaceRevision,
    string OwnerId,
    string RunnerId,
    string RunnerDisplayName,
    string Locale,
    string JourneyId,
    string StageId,
    int StageOrder,
    string TurnId,
    int TurnSequence,
    string VisibleStoryMarkdown,
    string DecisionPrompt,
    IReadOnlyList<LifeModuleNarrativeChoiceSeed> LegalChoices,
    IReadOnlyList<OriginCanonicalNarrativeFact> CanonicalFacts,
    IReadOnlyList<string> AcceptedDecisionIds,
    string PreviousTurnDigest,
    string DecisionGraphDigest,
    string ContentDigest,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest,
    string SeedDigest)
{
    public bool StoryEndsAtDecisionPoint { get; } = true;

    public string MechanicsAuthority { get; } = OriginNarrativeAuthorityKinds.AcceptedLifeModuleDecisionsOnly;
}

/// <summary>
/// Canonical layer: facts and chapter boundaries derived from accepted engine
/// decisions. Only this layer can refer to the mechanics snapshot.
/// </summary>
public sealed record OriginCanonicalNarrativeLayer(
    string RulesetId,
    IReadOnlyList<string> AcceptedDecisionIds,
    IReadOnlyList<OriginCanonicalNarrativeFact> Facts,
    IReadOnlyList<string> ChapterProjectionDigests,
    string DecisionGraphDigest,
    string MechanicsSnapshotDigest,
    string LayerDigest)
{
    public string LayerKind { get; } = OriginNarrativeLayerKinds.Canonical;

    public string MechanicsAuthority { get; } = OriginNarrativeAuthorityKinds.AcceptedLifeModuleDecisionsOnly;
}

public sealed record OriginPlayerNarrativeContribution(
    string ContributionId,
    string ChapterId,
    string Markdown,
    string BoundAcceptedDecisionId,
    string ContributionDigest);

/// <summary>
/// Player-authored wording is visible and attributable, but cannot create,
/// remove, or mutate a life-module decision or mechanical effect.
/// </summary>
public sealed record OriginPlayerNarrativeLayer(
    string OwnerId,
    IReadOnlyList<OriginPlayerNarrativeContribution> Contributions,
    string BoundDecisionGraphDigest,
    string LayerDigest)
{
    public string LayerKind { get; } = OriginNarrativeLayerKinds.Player;

    public string MechanicsAuthority { get; } = OriginNarrativeAuthorityKinds.PresentationOnly;

    public bool AffectsMechanics { get; }
}

public sealed record OriginProviderNarrativePassage(
    string PassageId,
    string ChapterId,
    string Markdown,
    string BoundAcceptedDecisionId,
    IReadOnlyList<string> ReferencedCanonicalFactIds,
    string PassageDigest);

/// <summary>
/// Provider-neutral narrative output. LTDs may design prose and story arcs,
/// but their output remains a proposal bound to canonical facts and choices.
/// </summary>
public sealed record OriginProviderNarrativeLayer(
    string ProposalId,
    string ProviderRouteReceiptDigest,
    IReadOnlyList<OriginProviderNarrativePassage> Passages,
    string BoundSeedDigest,
    string BoundDecisionGraphDigest,
    string LayerDigest)
{
    public string LayerKind { get; } = OriginNarrativeLayerKinds.Provider;

    public string MechanicsAuthority { get; } = OriginNarrativeAuthorityKinds.PresentationOnly;

    public bool AffectsMechanics { get; }
}

public sealed record OriginNarrativeChapterProjection(
    string ChapterId,
    int Sequence,
    string Title,
    string VisibleMarkdown,
    string ThroughAcceptedDecisionId,
    string CanonicalLayerDigest,
    string PlayerLayerDigest,
    string ProviderLayerDigest,
    string ChapterDigest);

/// <summary>
/// Provider input for continuing a story after a decision or designing bounded
/// branches at the current decision. It contains only the facts and legal
/// choices supplied by the engine, never an open mechanics mutation surface.
/// </summary>
public sealed record OriginStoryArcSeed(
    string Schema,
    string ArcSeedId,
    LifeModuleNarrativeTurnSeed CurrentTurn,
    OriginCanonicalNarrativeLayer CanonicalLayer,
    IReadOnlyList<OriginNarrativeChapterProjection> VisibleChapters,
    IReadOnlyList<string> AllowedCanonicalFactIds,
    IReadOnlyList<string> AllowedChoiceIds,
    IReadOnlyList<string> ToneTags,
    string SeedDigest);

public sealed record OriginStoryBranchProposal(
    string ChoiceId,
    string ProposedChapterTitle,
    string ContinuationMarkdown,
    IReadOnlyList<string> ReferencedCanonicalFactIds,
    string BranchDigest);

/// <summary>
/// An LTD/provider may propose one continuation per already-legal choice. It
/// cannot add choices or mechanics, and the proposal must be validated against
/// the bound seed before any passage enters an edition.
/// </summary>
public sealed record OriginStoryArcProposal(
    string Schema,
    string ProposalId,
    string BoundArcSeedId,
    string BoundSeedDigest,
    string ProviderRouteReceiptDigest,
    IReadOnlyList<OriginStoryBranchProposal> Branches,
    IReadOnlyList<string> ValidationBlockers,
    string ProposalDigest)
{
    public string MechanicsAuthority { get; } = OriginNarrativeAuthorityKinds.PresentationOnly;

    public bool CanAddLifeModuleChoices { get; }

    public bool AffectsMechanics { get; }
}

/// <summary>
/// Cover-first attribution. The runner and owning player are the prominent
/// subject of the story even though Chummer.run remains the technical author
/// and publisher in publication metadata.
/// </summary>
public sealed record OriginRunnerAttribution(
    string RunnerId,
    string RunnerDisplayName,
    string OwnerId,
    string OwnerDisplayName,
    string AttributionLabelKey,
    string AttributionDigest)
{
    public string Placement { get; } = "cover-primary";

    public bool IsProminent { get; } = true;

    public bool IsTechnicalAuthor { get; }
}

/// <summary>
/// Machine-checkable firewall evidence. No engagement or narrative value is a
/// constructor input, so neither can participate in the mechanics digest. The
/// only admitted input class is the accepted life-module decision graph.
/// </summary>
public sealed record OriginNarrativeMechanicsIsolationProof(
    string Schema,
    string RulesetId,
    IReadOnlyList<string> AcceptedDecisionIds,
    string AcceptedDecisionGraphDigest,
    string MechanicsSnapshotDigest,
    string ProofDigest)
{
    public string SoleMechanicsInputAuthority { get; } = OriginNarrativeAuthorityKinds.AcceptedLifeModuleDecisionsOnly;

    public IReadOnlyList<string> ExcludedInputKinds { get; } = OriginNarrativeNonMechanicsInputKinds.All;

    public bool PlayerNarrativeAffectsMechanics { get; }

    public bool ProviderNarrativeAffectsMechanics { get; }

    public bool VotesAffectMechanics { get; }

    public bool RankAffectsMechanics { get; }

    public bool RecognitionAffectsMechanics { get; }

    public bool PublicRewardsAffectMechanics { get; }

    public bool ArtifactUnlocksAffectMechanics { get; }
}

public sealed record OriginStoryReleaseConsent(
    string OwnerId,
    string EditionDigest,
    string TermsVersion,
    string ConsentReceiptDigest,
    DateTimeOffset GrantedAt,
    bool ApprovedForPublicRelease,
    bool ApprovedForPublicDownload);

/// <summary>
/// Immutable projection of a completed or in-progress Origin Dossier book.
/// Changing a life-module decision creates a new decision graph and edition;
/// public metrics never rewrite this snapshot.
/// </summary>
public sealed record OriginEditionSnapshot(
    string Schema,
    string EditionId,
    int EditionNumber,
    string RulesetId,
    string WorkspaceId,
    long WorkspaceRevision,
    OriginTechnicalPublicationMetadata Publication,
    OriginRunnerAttribution RunnerAttribution,
    OriginCanonicalNarrativeLayer CanonicalLayer,
    OriginPlayerNarrativeLayer PlayerLayer,
    OriginProviderNarrativeLayer ProviderLayer,
    IReadOnlyList<OriginNarrativeChapterProjection> Chapters,
    OriginNarrativeMechanicsIsolationProof MechanicsIsolation,
    string ReleaseState,
    string PreviousEditionDigest,
    string EditionDigest);

public sealed record OriginPublicArtifactDescriptor(
    string ArtifactId,
    string ArtifactKind,
    string AvailabilityState,
    string MediaType,
    string PublicRoute,
    string DownloadRoute,
    string ContentDigest,
    bool CanView,
    bool CanDownload);

/// <summary>
/// Public voting and recognition are intentionally outside the edition and
/// mechanics proof. They can select nonmechanical media work such as an
/// audiobook or rendered scenes, but can never change the runner.
/// </summary>
public sealed record OriginPublicRecognitionSnapshot(
    long VoteCount,
    decimal WeightedScore,
    int? PublicRank,
    IReadOnlyList<string> UnlockedArtifactKinds,
    string RecognitionDigest)
{
    public string MechanicsAuthority { get; } = OriginNarrativeAuthorityKinds.PresentationOnly;

    public bool AffectsMechanics { get; }
}

public sealed record OriginPublicArtifactManifest(
    string Schema,
    string ManifestId,
    string PublicSlug,
    string EditionId,
    string EditionDigest,
    OriginTechnicalPublicationMetadata Publication,
    OriginRunnerAttribution RunnerAttribution,
    OriginStoryReleaseConsent ReleaseConsent,
    OriginPublicRecognitionSnapshot Recognition,
    IReadOnlyList<OriginPublicArtifactDescriptor> Artifacts,
    string ManifestDigest)
{
    public bool RequiresExplicitOwnerRelease { get; } = true;

    public bool PublicMetricsAffectMechanics { get; }
}
