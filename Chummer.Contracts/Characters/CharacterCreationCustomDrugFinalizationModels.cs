using System.Xml.Linq;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationCustomDrugSchemas
{
    public const string ContributionV1 =
        "chummer.sr5.creation-custom-drug.finalization-contribution.v1";
}

public static class CharacterCreationCustomDrugOutcomes
{
    public const string Available = "available";
    public const string Applied = "applied";
    public const string Replayed = "replayed";
    public const string NotFound = "not-found";
    public const string Blocked = "blocked";
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
    public const string Unavailable = "unavailable";
}

public static class CharacterCreationCustomDrugBlockers
{
    public const string WorkspaceUnavailable = "creation-custom-drug-workspace-unavailable";
    public const string ExplicitConfirmationRequired =
        "creation-custom-drug-explicit-confirmation-required";
    public const string IdempotencyKeyInvalid = "creation-custom-drug-idempotency-key-invalid";
    public const string IdempotencyConflict = "creation-custom-drug-idempotency-conflict";
    public const string StaleWorkspaceRevision = "creation-custom-drug-stale-workspace-revision";
    public const string StaleAuxiliaryStateDigest = "creation-custom-drug-stale-auxiliary-digest";
    public const string StaleCharacterDigest = "creation-custom-drug-stale-character-digest";
    public const string StaleCatalogDigest = "creation-custom-drug-stale-catalog-digest";
    public const string StaleRulesDigest = "creation-custom-drug-stale-rules-digest";
    public const string StaleQuoteDigest = "creation-custom-drug-stale-quote-digest";
    public const string InvalidIdentity = "creation-custom-drug-invalid-identity";
    public const string ProjectionRejected = "creation-custom-drug-projection-rejected";
    public const string PersistenceAuthorityRequired =
        "creation-custom-drug-persistence-authority-required";
}

/// <summary>
/// Durable, Core-owned hand-off to the SR5 whole-build finalizer. It contains the
/// exact source-bound quote, generated identities, and canonical legacy payload,
/// but grants no independent character-write authority.
/// </summary>
public sealed record CharacterCreationCustomDrugFinalizationContribution(
    string SchemaId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    string ExpectedCharacterDigest,
    string ExpectedCatalogDigest,
    string ExpectedRulesDigest,
    CharacterCustomDrugSelection Selection,
    CharacterCustomDrugQuote Quote,
    CharacterCustomDrugInstanceId NewDrugInstanceId,
    IReadOnlyList<Guid> NewComponentInstanceIds,
    string ProjectedDrugXml,
    string ProjectedDrugXmlDigest,
    string RequestIdempotencyKeyDigest,
    string RequestCommandDigest,
    string ContributionDigest)
{
    public CharacterCustomDrugCommitCommand ToVerificationCommand() => new(
        ExpectedContentRevision,
        ExpectedCharacterDigest,
        ExpectedCatalogDigest,
        ExpectedRulesDigest,
        Quote.QuoteDigest,
        FinalizerIdempotencyKey(WorkspaceId, NewDrugInstanceId),
        Selection,
        NewDrugInstanceId,
        NewComponentInstanceIds);

    private static string FinalizerIdempotencyKey(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugInstanceId drugId) =>
        $"creation-custom-drug:{CharacterCreationFinalizationDigest.ComputeUtf8(workspaceId.Value)}:{drugId.Value:N}";
}

public sealed record CharacterCreationCustomDrugQueueRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    long ExpectedSavedRevision,
    string ExpectedAuxiliaryStateDigest,
    CharacterCustomDrugCommitCommand VerificationCommand,
    string IdempotencyKey,
    bool ExplicitlyConfirmed);

public sealed record CharacterCreationCustomDrugLoadRequest(CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationCustomDrugResult(
    string Outcome,
    CharacterCreationCustomDrugFinalizationContribution? Contribution,
    IReadOnlyList<string> Blockers)
{
    public bool Success => Outcome is CharacterCreationCustomDrugOutcomes.Available
        or CharacterCreationCustomDrugOutcomes.Applied
        or CharacterCreationCustomDrugOutcomes.Replayed;
}

public static class CharacterCreationCustomDrugContributionRules
{
    public const int MaximumProjectedDrugXmlLength = 1_000_000;

    public static string ComputeContributionDigest(
        CharacterCreationCustomDrugFinalizationContribution value) =>
        CharacterCreationFinalizationDigest.Compute(value with { ContributionDigest = string.Empty });

    public static string ComputeRequestIdempotencyKeyDigest(string value) =>
        CharacterCreationFinalizationDigest.ComputeUtf8(
            "chummer.sr5.creation-custom-drug.queue-idempotency.v1\0" + value);

    public static string ComputeRequestCommandDigest(
        CharacterCreationCustomDrugQueueRequest value) =>
        CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.sr5.creation-custom-drug.queue-command.v1",
            value.WorkspaceId,
            value.ExpectedContentRevision,
            value.ExpectedSavedRevision,
            value.ExpectedAuxiliaryStateDigest,
            value.VerificationCommand,
            ExplicitlyConfirmed = true
        });

    public static bool IsValid(
        CharacterCreationCustomDrugFinalizationContribution? value,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision) =>
        value?.ExpectedContentRevision == persistedContentRevision
        && IsPersistable(value, workspaceId, persistedContentRevision);

    /// <summary>
    /// Validates a durable contribution even after another typed Creation lane has
    /// advanced the workspace. Such a contribution remains readable but is stale;
    /// only <see cref="IsValid"/> may authorize finalization or a current replay.
    /// </summary>
    public static bool IsPersistable(
        CharacterCreationCustomDrugFinalizationContribution? value,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision)
    {
        if (value is null
            || !string.Equals(value.SchemaId, CharacterCreationCustomDrugSchemas.ContributionV1,
                StringComparison.Ordinal)
            || value.WorkspaceId != workspaceId
            || value.ExpectedContentRevision <= 0
            || value.ExpectedContentRevision > persistedContentRevision
            || !CharacterCustomDrugRules.IsCanonicalDigest(value.ExpectedCharacterDigest)
            || !CharacterCustomDrugRules.IsCanonicalDigest(value.ExpectedCatalogDigest)
            || !CharacterCustomDrugRules.IsCanonicalDigest(value.ExpectedRulesDigest)
            || value.Selection is null
            || value.Selection.Components is not { Count: > 0 and <= 256 }
            || value.Quote is not { Exact: true }
            || value.Quote.Components is null
            || !CharacterCustomDrugRules.IsCanonicalDigest(value.Quote.QuoteDigest)
            || value.NewDrugInstanceId.Value == Guid.Empty
            || value.NewComponentInstanceIds is null
            || value.NewComponentInstanceIds.Count != value.Selection.Components.Count
            || value.NewComponentInstanceIds.Any(static item => item == Guid.Empty)
            || value.NewComponentInstanceIds.Distinct().Count()
               != value.NewComponentInstanceIds.Count
            || value.NewComponentInstanceIds.Contains(value.NewDrugInstanceId.Value)
            || value.ProjectedDrugXml is not
                { Length: > 0 and <= MaximumProjectedDrugXmlLength }
            || !CharacterCustomDrugRules.IsCanonicalDigest(value.ProjectedDrugXmlDigest)
            || !string.Equals(
                value.ProjectedDrugXmlDigest,
                CharacterCustomDrugRules.ComputeCharacterDigest(value.ProjectedDrugXml),
                StringComparison.Ordinal)
            || !CharacterCreationFinalizationDigest.IsCanonical(
                value.RequestIdempotencyKeyDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(value.RequestCommandDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(value.ContributionDigest)
            || !CharacterCreationFinalizationDigest.EqualsFixedTime(
                value.ContributionDigest,
                ComputeContributionDigest(value))
            || !IsProjectedPayloadBound(value))
            return false;

        CharacterCustomDrugCommitCommand command = value.ToVerificationCommand();
        return command.NewDrugInstanceId == value.NewDrugInstanceId
               && command.NewComponentInstanceIds.SequenceEqual(value.NewComponentInstanceIds)
               && string.Equals(
                   command.ExpectedQuoteDigest,
                   value.Quote.QuoteDigest,
                   StringComparison.Ordinal);
    }

    private static bool IsProjectedPayloadBound(
        CharacterCreationCustomDrugFinalizationContribution value)
    {
        try
        {
            XElement drug = XElement.Parse(value.ProjectedDrugXml, LoadOptions.None);
            if (drug.Name != "drug" || drug.HasAttributes)
                return false;
            XElement[] guids = drug.Elements("guid").Take(2).ToArray();
            XElement[] components = drug.Elements("drugcomponents").Take(2).ToArray();
            if (guids.Length != 1
                || components.Length != 1
                || !Guid.TryParseExact(guids[0].Value, "D", out Guid drugId)
                || drugId != value.NewDrugInstanceId.Value)
                return false;
            XElement[] saved = components[0].Elements("drugcomponent").ToArray();
            if (saved.Length != value.Selection.Components.Count)
                return false;
            for (int index = 0; index < saved.Length; index++)
            {
                string? guidText = saved[index].Elements("guid").SingleOrDefault()?.Value;
                string? sourceText = saved[index].Elements("sourceid").SingleOrDefault()?.Value;
                if (!Guid.TryParseExact(guidText, "D", out Guid componentInstanceId)
                    || componentInstanceId != value.NewComponentInstanceIds[index]
                    || !Guid.TryParseExact(sourceText, "D", out Guid sourceId)
                    || sourceId != value.Selection.Components[index].ComponentId.Value)
                    return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or System.Xml.XmlException)
        {
            return false;
        }
    }
}
