namespace Chummer.Contracts.Workspaces;

public static class WorkspacePortabilityFormatIds
{
    public const string PortableDossierV1 = "chummer.portable-dossier.v1";
    public const string NativeWorkspaceXmlV1 = "chummer.workspace.native-xml.v1";
}

public static class WorkspacePortabilityCompatibilityStates
{
    public const string Compatible = "compatible";
    public const string CompatibleWithWarnings = "compatible-with-warnings";
    public const string Incompatible = "incompatible";
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

public sealed record WorkspacePortabilityNote(
    string Code,
    string Severity,
    string Summary);

public sealed record WorkspacePortabilityReceipt(
    string FormatId,
    string CompatibilityState,
    string ContextSummary,
    string ReceiptSummary,
    string ProvenanceSummary,
    string PayloadSha256,
    string NextSafeAction,
    IReadOnlyList<string> SupportedExchangeModes,
    IReadOnlyList<WorkspacePortabilityNote> Notes);
