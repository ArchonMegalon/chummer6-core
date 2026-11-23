using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerComplexFormLearningOutcome
{
    Available, Applied, Replayed, NotFound, IdempotencyConflict, Conflict,
    Corrupt, Unavailable, Missing, UnknownOutcome, Canceled
}

/// <summary>Ordinary, paid, previously unowned form. Repeat learning is unsupported by this slice.</summary>
public sealed record CharacterCareerComplexFormLearningRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, Guid SourceId, DateTime ExpenseDateLocal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCareerComplexFormLearningCatalogBinding? CatalogBinding = null);

public sealed record CharacterCareerComplexFormLearningBinding(
    string OwnerId, CharacterWorkspaceId WorkspaceId, long WorkspaceRevision,
    string SourceDigest, string AuxiliaryStateDigest, string RuleStateDigest, string RuntimeDigest);

/// <summary>Persist the exact confirmed command before dispatch. Bindings are evidence, not authority.</summary>
public sealed record CharacterCareerComplexFormLearningCommand(
    CharacterCareerComplexFormLearningRequest Request, CharacterCareerComplexFormLearningBinding Binding,
    bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);

public sealed record CharacterCareerComplexFormLearningCostEffect(string Type, decimal Value);

public sealed record CharacterCareerComplexFormLearningQuote(
    Guid SourceId, string Name, string Book, string Page,
    int ProfileKarmaCost, IReadOnlyList<CharacterCareerComplexFormLearningCostEffect> CostEffects,
    int KarmaCost, int KarmaBefore, int KarmaAfter, int EffectiveResonance,
    int OwnedFormCount, decimal ComplexFormLimit, bool IgnoreComplexFormLimit, string ComplexFormXml);

public sealed record CharacterCareerComplexFormLearningPreview(
    CharacterCareerComplexFormLearningCommand Command, CharacterCareerComplexFormLearningQuote Quote, string PreviewDigest);

public sealed record CharacterCareerComplexFormLearningReceipt(
    CharacterCareerComplexFormLearningCommand Command, CharacterCareerComplexFormLearningQuote Quote, string CommandDigest,
    Guid ComplexFormInstanceId, Guid ExpenseId, long CommittedWorkspaceRevision, string CharacterPayloadDigestAfter,
    string PreviousReceiptDigest, string ReceiptDigest);

public sealed record CharacterCareerComplexFormLearningPreviewResult(
    CharacterCareerComplexFormLearningOutcome Outcome, CharacterCareerComplexFormLearningPreview? Preview = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);

public sealed record CharacterCareerComplexFormLearningResult(
    CharacterCareerComplexFormLearningOutcome Outcome, CharacterCareerComplexFormLearningReceipt? Receipt = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);

public sealed record CharacterCareerComplexFormLearningCatalogRequest(
    CharacterWorkspaceId WorkspaceId, long? ExpectedWorkspaceRevision = null);

public sealed record CharacterCareerComplexFormLearningCatalogBinding(
    string OwnerId, CharacterWorkspaceId WorkspaceId, long WorkspaceRevision,
    string SourceDigest, string AuxiliaryStateDigest, string RuntimeDigest,
    string SettingsProfileId, string ProfileDigest, string CatalogSourceDigest, string ContextDigest);

/// <summary>Core supplies eligibility and cost. Disabled entries retain the exact source identity and blockers.</summary>
public sealed record CharacterCareerComplexFormLearningCatalogEntry(
    Guid SourceId, string Name, string Book, string Page,
    bool IsAvailable, int? KarmaCost, IReadOnlyList<string> Blockers);

public sealed record CharacterCareerComplexFormLearningCatalog(
    CharacterCareerComplexFormLearningCatalogBinding Binding, IReadOnlyList<string> EnabledSourcebooks,
    IReadOnlyList<CharacterCareerComplexFormLearningCatalogEntry> Entries);

public sealed record CharacterCareerComplexFormLearningCatalogResult(
    CharacterCareerComplexFormLearningOutcome Outcome, CharacterCareerComplexFormLearningCatalog? Catalog = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
