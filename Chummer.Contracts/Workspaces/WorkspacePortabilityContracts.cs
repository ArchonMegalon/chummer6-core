namespace Chummer.Contracts.Workspaces;

public static class WorkspacePortabilityFormatIds
{
    public const string PortableDossierV1 = "chummer.portable-dossier.v1";
    public const string NativeWorkspaceXmlV1 = "chummer.workspace.native-xml.v1";
    public const string CampaignBundleV1 = "chummer.campaign-bundle.v1";
    public const string ReplayTimelineV1 = "chummer.replay-timeline.v1";
    public const string SessionRecapV1 = "chummer.session-recap.v1";
    public const string ExternalExchangeV1 = "chummer.external-exchange.v1";
}

public static class WorkspacePortabilityOutputKinds
{
    public const string PortableDossier = "portable-dossier";
    public const string NativeWorkspaceXml = "native-workspace-xml";
    public const string CampaignBundle = "campaign-bundle";
    public const string ReplayTimeline = "replay-timeline";
    public const string SessionRecap = "session-recap";
    public const string ExternalExchange = "external-exchange";
}

public static class WorkspacePortabilityCompatibilityStates
{
    public const string Compatible = "compatible";
    public const string CompatibleWithWarnings = "compatible-with-warnings";
    public const string Incompatible = "incompatible";
}

public static class WorkspacePortabilityLossStates
{
    public const string None = "none";
    public const string BoundedLoss = "bounded-loss";
    public const string BlockingLoss = "blocking-loss";
}

public static class WorkspacePortabilityNoteSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
}

public static class WorkspacePortabilityExchangeModes
{
    public const string InspectOnly = "inspect-only";
    public const string Merge = "merge";
    public const string Replace = "replace";
}

public static class WorkspacePortabilityRevocationStates
{
    public const string Active = "active";
    public const string Revocable = "revocable";
}

public sealed record WorkspacePortabilityNote(
    string Code,
    string Severity,
    string Summary);

public sealed record WorkspacePortabilityLineageEntry(
    string StageId,
    string ArtifactId,
    string FormatId,
    string Summary);

public sealed record WorkspacePortabilityCompatibilityReceipt(
    string SourceRulesetId,
    string TargetRulesetId,
    string State,
    IReadOnlyList<string> WarningCodes,
    IReadOnlyList<string> BlockingCodes);

public sealed record WorkspacePortabilityLossReceipt(
    string State,
    string Summary,
    IReadOnlyList<string> AffectedSections);

public sealed record WorkspacePortabilityProvenanceReceipt(
    string ReceiptId,
    DateTimeOffset GeneratedAtUtc,
    string SourceArtifactId,
    string SourceFormatId,
    string PayloadSha256);

public sealed record WorkspacePortabilityEnvelopeReceipt(
    string OutputKind,
    string PortabilityPosture,
    string Summary,
    IReadOnlyList<string> SupportedExchangeModes);

public sealed record WorkspacePortabilityRevocationReceipt(
    string State,
    string FamilyId,
    string ArtifactId,
    string Scope,
    string Summary,
    IReadOnlyList<string> SupersedesArtifactIds);

public sealed record WorkspacePortabilityRelatedOutputReceipt(
    string OutputKind,
    string WorkflowId,
    string Summary,
    IReadOnlyList<WorkspacePortabilityLineageEntry> Lineage,
    WorkspacePortabilityCompatibilityReceipt Compatibility,
    WorkspacePortabilityLossReceipt Loss,
    WorkspacePortabilityProvenanceReceipt Provenance,
    WorkspacePortabilityEnvelopeReceipt PortabilityEnvelope,
    WorkspacePortabilityRevocationReceipt Revocation);

public sealed record WorkspacePortabilityReceipt(
    string FormatId,
    string CompatibilityState,
    string ContextSummary,
    string ReceiptSummary,
    string ProvenanceSummary,
    string PayloadSha256,
    string NextSafeAction,
    IReadOnlyList<string> SupportedExchangeModes,
    IReadOnlyList<WorkspacePortabilityNote> Notes,
    string OutputKind = WorkspacePortabilityOutputKinds.PortableDossier,
    IReadOnlyList<WorkspacePortabilityLineageEntry>? Lineage = null,
    WorkspacePortabilityCompatibilityReceipt? Compatibility = null,
    WorkspacePortabilityLossReceipt? Loss = null,
    WorkspacePortabilityProvenanceReceipt? Provenance = null,
    WorkspacePortabilityEnvelopeReceipt? PortabilityEnvelope = null,
    WorkspacePortabilityRevocationReceipt? Revocation = null,
    IReadOnlyList<WorkspacePortabilityRelatedOutputReceipt>? RelatedOutputs = null);
