using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerSpellLearningOutcome
{
    Available, Applied, Replayed, NotFound, IdempotencyConflict, Conflict,
    Corrupt, Unavailable, Missing, UnknownOutcome, Canceled
}

public enum CharacterCareerSpellLearningKind { Spell, Ritual }

/// <summary>One ordinary paid learning operation. Variants are explicit and currently refused.</summary>
public sealed record CharacterCareerSpellLearningRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, Guid SourceId,
    CharacterCareerSpellLearningKind Kind, DateTime ExpenseDateLocal,
    bool Limited = false, bool Extended = false, bool Alchemical = false, bool Free = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCareerSpellLearningCatalogBinding? CatalogBinding = null);

public sealed record CharacterCareerSpellLearningBinding(
    string OwnerId, CharacterWorkspaceId WorkspaceId, long WorkspaceRevision,
    string SourceDigest, string AuxiliaryStateDigest, string RuleStateDigest, string RuntimeDigest);

/// <summary>Persist the preview command before confirmation; hashes are bindings, not authority.</summary>
public sealed record CharacterCareerSpellLearningCommand(
    CharacterCareerSpellLearningRequest Request, CharacterCareerSpellLearningBinding Binding,
    bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);

public sealed record CharacterCareerSpellLearningCostEffect(string Type, string Category, decimal Value);

public sealed record CharacterCareerSpellLearningQuote(
    Guid SourceId, string Name, string Category, string Book, string Page,
    int ProfileKarmaCost, IReadOnlyList<CharacterCareerSpellLearningCostEffect> CostEffects,
    int KarmaCost, int KarmaBefore, int KarmaAfter, int EffectiveMagic,
    Guid TraditionSourceId, string SpellXml, string TraditionXml);

public sealed record CharacterCareerSpellLearningPreview(
    CharacterCareerSpellLearningCommand Command, CharacterCareerSpellLearningQuote Quote, string PreviewDigest);

public sealed record CharacterCareerSpellLearningReceipt(
    CharacterCareerSpellLearningCommand Command, CharacterCareerSpellLearningQuote Quote, string CommandDigest,
    Guid SpellInstanceId, Guid ExpenseId, long CommittedWorkspaceRevision, string CharacterPayloadDigestAfter,
    string PreviousReceiptDigest, string ReceiptDigest);

public sealed record CharacterCareerSpellLearningPreviewResult(
    CharacterCareerSpellLearningOutcome Outcome, CharacterCareerSpellLearningPreview? Preview = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);

public sealed record CharacterCareerSpellLearningResult(
    CharacterCareerSpellLearningOutcome Outcome, CharacterCareerSpellLearningReceipt? Receipt = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);

public sealed record CharacterCareerSpellLearningCatalogRequest(
    CharacterWorkspaceId WorkspaceId, long? ExpectedWorkspaceRevision = null);

/// <summary>Evidence from the saved workspace and selected source/profile. Never authorization by itself.</summary>
public sealed record CharacterCareerSpellLearningCatalogBinding(
    string OwnerId, CharacterWorkspaceId WorkspaceId, long WorkspaceRevision,
    string SourceDigest, string AuxiliaryStateDigest, string RuntimeDigest,
    string SettingsProfileId, string ProfileDigest, string CatalogSourceDigest, string ContextDigest);

public enum CharacterCareerSpellLearningOptionKind { Ordinary, Limited, Extended, Alchemical, Free }

public sealed record CharacterCareerSpellLearningCatalogOption(
    CharacterCareerSpellLearningOptionKind Kind, bool IsAvailable, IReadOnlyList<string> Blockers);

/// <summary>Null Kind means an unsupported source category; Category always retains the actual source value.</summary>
public sealed record CharacterCareerSpellLearningCatalogEntry(
    Guid SourceId, string Name, CharacterCareerSpellLearningKind? Kind, string Category, string Book, string Page,
    bool IsAvailable, int? KarmaCost, IReadOnlyList<string> Blockers,
    IReadOnlyList<CharacterCareerSpellLearningCatalogOption> Options);

public sealed record CharacterCareerSpellLearningCatalog(
    CharacterCareerSpellLearningCatalogBinding Binding, IReadOnlyList<string> EnabledSourcebooks,
    IReadOnlyList<string> Categories, IReadOnlyList<CharacterCareerSpellLearningCatalogEntry> Entries);

public sealed record CharacterCareerSpellLearningCatalogResult(
    CharacterCareerSpellLearningOutcome Outcome, CharacterCareerSpellLearningCatalog? Catalog = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
