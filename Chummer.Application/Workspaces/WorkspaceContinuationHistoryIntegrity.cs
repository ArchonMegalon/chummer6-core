using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Pure consistency checks over complete, already bounded continuation data.
/// Success is not source legality, provenance, restore admission or permission
/// to replay a historical command. No workspace or history is changed here.
/// </summary>
internal static class WorkspaceContinuationHistoryIntegrity
{
    private static readonly JsonSerializerOptions ShapeOptions = new()
    {
        IgnoreReadOnlyProperties = true,
        RespectNullableAnnotations = true,
        MaxDepth = 128,
        Converters = { new NonNullStringMapConverter() }
    };

    internal static bool TryValidate(OwnerScope expectedOwner, WorkspaceContinuationSnapshot? candidate)
    {
        try
        {
            WorkspaceDocumentSnapshot? workspace = candidate?.Workspace;
            WorkspaceDocumentState? state = workspace?.Document?.State;
            if (string.IsNullOrWhiteSpace(expectedOwner.NormalizedValue)
                || (expectedOwner.UsesLocalSingleUserValue && !expectedOwner.IsLocalSingleUser)
                || candidate is null || workspace is null || state is null
                || !string.Equals(candidate.OwnerId, expectedOwner.NormalizedValue, StringComparison.Ordinal)
                || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(workspace.Id)
                || workspace.ContentRevision < 1 || workspace.SavedRevision < 0
                || workspace.SavedRevision > workspace.ContentRevision
                || !Enum.IsDefined(workspace.Document.Format)
                || state.SchemaVersion < 1 || string.IsNullOrWhiteSpace(state.RulesetId)
                || !string.Equals(state.RulesetId, RulesetDefaults.NormalizeOptional(state.RulesetId), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(state.PayloadKind) || string.IsNullOrWhiteSpace(state.Payload)
                // Do not let the public AuxiliaryState convenience getter turn
                // a malformed null into an apparently valid empty history.
                || state.AuxiliaryState is null || candidate.DelegatedGmCharacterEdits is null)
                return false;

            // Some existing persisted-shape checks intentionally inspect only
            // part of a draft. Enforce required nested members first. STJ does
            // not enforce nullability of collection elements, hence the walk.
            JsonElement shape = JsonSerializer.SerializeToElement(candidate, ShapeOptions);
            if (!HasNonNullCollectionElements(shape)
                || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(
                    workspace.Id, workspace.ContentRevision, state.AuxiliaryState)
                || !IsConsistentGraph(workspace.Id, workspace.ContentRevision, workspace.SavedRevision,
                    CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(state.Payload),
                    state.AuxiliaryState, archived: false))
                return false;

            DelegatedGmCharacterEditLedgerEntry[] ledger = candidate.DelegatedGmCharacterEdits
                .Select(receipt => new DelegatedGmCharacterEditLedgerEntry(
                    receipt.IdempotencyKeySha256, receipt.CommandSha256, receipt))
                .ToArray();
            if (!DelegatedGmCharacterEditLedgerValidator.IsValidLedger(
                    expectedOwner, workspace.Id, workspace.ContentRevision, ledger))
                return false;
            if (!WorkspaceContinuationReceiptConsistency.IsValid(candidate))
                return false;

            return CharacterCareerReputationTransaction.IsValidHistory(new(
                workspace.Id, workspace.Document, workspace.ContentRevision,
                workspace.SavedRevision, workspace.LastUpdatedUtc));
        }
        catch (Exception error) when (error is JsonException or XmlException or ArgumentException
                                      or InvalidOperationException or NullReferenceException
                                      or IndexOutOfRangeException or KeyNotFoundException
                                      or FormatException or OverflowException or NotSupportedException)
        {
            // A malformed in-process DTO is no more authoritative than malformed
            // wire data. Fail closed without weakening the shared validators.
            return false;
        }
    }

    private static bool IsConsistentGraph(CharacterWorkspaceId id, long revision, long savedRevision,
        string rawXmlDigest, WorkspaceDocumentAuxiliaryState state, bool archived)
    {
        // Only an archive has an exact historical XML binding. Active drafts
        // may be stale after an owner edit; current-source evaluation decides
        // whether they can continue, not this historical consistency check.
        if (!HasConsistentDrafts(id, revision, state, archived ? rawXmlDigest : null))
            return false;

        if (state.CharacterCreationFinalizationReceipts is { Count: > 0 } finalizations
            && !MatchesCheckpoint(revision, savedRevision, rawXmlDigest,
                finalizations[^1].Receipt.ContentRevision, finalizations[^1].Receipt.SavedRevision,
                finalizations[^1].Receipt.RawCharacterXmlDigest))
            return false;
        if (state.CharacterCreationContactReceipts is { Count: > 0 } contacts
            && !MatchesCheckpoint(revision, savedRevision, rawXmlDigest,
                contacts[^1].Receipt.ContentRevision, contacts[^1].Receipt.SavedRevision,
                contacts[^1].Receipt.ContentDigestAfter))
            return false;
        if (state.CharacterCreationLifestyleReceipts is { Count: > 0 } lifestyles
            && !MatchesCheckpoint(revision, savedRevision, rawXmlDigest,
                lifestyles[^1].Receipt.ContentRevision, lifestyles[^1].Receipt.SavedRevision,
                lifestyles[^1].Receipt.ContentDigestAfter))
            return false;
        // After Run uses unprefixed hashes and checkpoints the committed revision.
        if (state.CharacterAfterRunRewardReceipts is { Count: > 0 } rewards
            && !MatchesCheckpoint(revision, savedRevision, rawXmlDigest,
                rewards[^1].CommittedWorkspaceRevision, rewards[^1].CommittedWorkspaceRevision,
                "sha256:" + rewards[^1].CharacterPayloadDigestAfter))
            return false;
        if (state.CharacterAfterRunSettlementReceipts is { Count: > 0 } settlements
            && !MatchesCheckpoint(revision, savedRevision, rawXmlDigest,
                settlements[^1].CommittedWorkspaceRevision, settlements[^1].CommittedWorkspaceRevision,
                "sha256:" + settlements[^1].CharacterPayloadDigestAfter))
            return false;

        if (state.CharacterCreationFinalizationArchive is not { } archive)
            return true;
        // Shared shape validation already rejects nested archives and binds the
        // complete archive to this single receipt. Reuse its historical context.
        var finalization = state.CharacterCreationFinalizationReceipts![0].Receipt;
        return IsConsistentGraph(id, finalization.PreviousContentRevision,
            finalization.PreviousSavedRevision, finalization.PreviousRawCharacterXmlDigest,
            archive.State, archived: true);
    }

    private static bool MatchesCheckpoint(long revision, long savedRevision, string rawXmlDigest,
        long receiptRevision, long receiptSavedRevision, string receiptXmlDigest) =>
        savedRevision >= receiptSavedRevision
        && (revision != receiptRevision || SameDigest(rawXmlDigest, receiptXmlDigest));

    private static bool HasConsistentDrafts(CharacterWorkspaceId id, long revision,
        WorkspaceDocumentAuxiliaryState state, string? historicalXmlDigest)
    {
        var foundation = state.CharacterCreationFoundationDraft;
        if (foundation is not null
            && (foundation.DraftRevision > foundation.BaseContentRevision
                || !CharacterCreationFoundationDraftLedgerIntegrity.IsValidPending(foundation, id, revision,
                    historicalXmlDigest ?? foundation.BaseRawCharacterXmlDigest, foundation.SourceDigest)))
            return false;

        var prerequisite = state.CharacterCreationPrerequisiteDraft;
        if (prerequisite is not null
            && (!HasDraftIdentity(id, revision, prerequisite.WorkspaceId, prerequisite.DraftRevision,
                    prerequisite.BaseContentRevision, prerequisite.BaseRawCharacterXmlDigest, historicalXmlDigest)
                || !SameDigest(prerequisite.DraftDigest,
                    CharacterCreationPrerequisiteDraftIntegrity.ComputeDigest(prerequisite))
                || !HasIntrinsicAssignments(prerequisite)))
            return false;

        var attributes = state.CharacterCreationAttributesDraft;
        if (attributes is not null
            && (prerequisite is null
                || !HasDraftIdentity(id, revision, attributes.WorkspaceId, attributes.DraftRevision,
                    attributes.BaseContentRevision, attributes.BaseRawCharacterXmlDigest, historicalXmlDigest)
                || !HasPrerequisiteReference(attributes.BaseContentRevision, attributes.PrerequisiteDraftRevision,
                    attributes.PrerequisiteDraftDigest, attributes.PrerequisiteAuthorityDigest,
                    prerequisite, historicalXmlDigest is not null)
                || !SameDigest(attributes.DraftDigest, CharacterCreationAttributesDraftIntegrity.ComputeDigest(attributes))
                || attributes.PrerequisiteDraftRevision == prerequisite.DraftRevision
                    && !CharacterCreationAttributesDraftIntegrity.IsStructurallyValidPending(attributes, id, revision,
                        historicalXmlDigest ?? attributes.BaseRawCharacterXmlDigest, prerequisite)))
            return false;

        // The full Skills and Magic validators also require catalog authorities.
        // Do not manufacture those from the candidate or substitute current
        // sources for historical sources. Check only persisted intrinsic links.
        var skills = state.CharacterCreationSkillsDraft;
        if (skills is not null
            && (prerequisite is null || attributes is null
                || !HasDraftIdentity(id, revision, skills.WorkspaceId, skills.DraftRevision,
                    skills.BaseContentRevision, skills.BaseRawCharacterXmlDigest, historicalXmlDigest)
                || !HasDependencies(skills.BaseContentRevision, skills.PrerequisiteDraftRevision,
                    skills.PrerequisiteDraftDigest, skills.PrerequisiteAuthorityDigest,
                    skills.AttributesDraftRevision, skills.AttributesDraftDigest, prerequisite, attributes,
                    historicalXmlDigest is not null)
                || !HasPointBudget(skills.ActivePointTotal, skills.ActivePointUsed)
                || !HasPointBudget(skills.SkillGroupPointTotal, skills.SkillGroupPointUsed)
                || !HasPointBudget(skills.KnowledgePointTotal, skills.KnowledgePointUsed)
                || skills.KnowledgePointOverflowToActive < 0
                || !SameDigest(skills.DraftDigest, CharacterCreationSkillsDraftIntegrity.ComputeDigest(skills))))
            return false;

        var magic = state.CharacterCreationMagicResonanceDraft;
        return magic is null || prerequisite is not null && attributes is not null
            && HasDraftIdentity(id, revision, magic.WorkspaceId, magic.DraftRevision,
                magic.BaseContentRevision, magic.BaseRawCharacterXmlDigest, historicalXmlDigest)
            && HasDependencies(magic.BaseContentRevision, magic.PrerequisiteDraftRevision,
                magic.PrerequisiteDraftDigest, magic.PrerequisiteAuthorityDigest,
                magic.AttributesDraftRevision, magic.AttributesDraftDigest, prerequisite, attributes,
                historicalXmlDigest is not null)
            && magic.AssignedMagic >= 0 && magic.AssignedResonance >= 0 && magic.AssignedDepth >= 0
            && HasMagicBudget(magic.TraditionBudget) && HasMagicBudget(magic.StreamBudget)
            && HasMagicBudget(magic.AdeptPowerPointBudget) && HasMagicBudget(magic.SpellBudget)
            && HasMagicBudget(magic.ComplexFormBudget)
            && SameDigest(magic.DraftDigest, CharacterCreationMagicResonanceDraftIntegrity.ComputeDigest(magic));
    }

    private static bool HasDraftIdentity(CharacterWorkspaceId id, long revision,
        CharacterWorkspaceId draftId, long draftRevision, long baseRevision,
        string baseXmlDigest, string? historicalXmlDigest) =>
        draftId == id && draftRevision > 0 && draftRevision <= baseRevision
        && baseRevision > 0 && baseRevision < revision
        && CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(baseXmlDigest)
        && (historicalXmlDigest is null || SameDigest(baseXmlDigest, historicalXmlDigest));

    private static bool HasDependencies(long baseRevision, long prerequisiteRevision,
        string prerequisiteDigest, string authorityDigest, long attributesRevision, string attributesDigest,
        CharacterCreationPrerequisiteDraft prerequisite, CharacterCreationAttributesDraft attributes, bool archived) =>
        HasPrerequisiteReference(baseRevision, prerequisiteRevision, prerequisiteDigest, authorityDigest,
            prerequisite, archived)
        && HasReference(baseRevision, attributesRevision, attributesDigest,
            attributes.BaseContentRevision, attributes.DraftRevision, attributes.DraftDigest, archived);

    private static bool HasPrerequisiteReference(long baseRevision, long prerequisiteRevision,
        string prerequisiteDigest, string authorityDigest, CharacterCreationPrerequisiteDraft prerequisite, bool archived) =>
        HasReference(baseRevision, prerequisiteRevision, prerequisiteDigest,
            prerequisite.BaseContentRevision, prerequisite.DraftRevision, prerequisite.DraftDigest, archived)
        && CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(authorityDigest)
        && (prerequisiteRevision != prerequisite.DraftRevision || SameDigest(authorityDigest, prerequisite.AuthorityDigest));

    private static bool HasReference(long baseRevision, long referencedRevision, string referencedDigest,
        long currentBaseRevision, long currentDraftRevision, string currentDigest, bool archived) =>
        referencedRevision > 0 && referencedRevision <= currentDraftRevision
        && CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(referencedDigest)
        && (referencedRevision == currentDraftRevision
            ? baseRevision > currentBaseRevision && SameDigest(referencedDigest, currentDigest)
            // A stale active reference is historical but not reconstructible
            // here; only current evaluation may decide whether to continue it.
            : !archived);

    private static bool HasPointBudget(int total, int used) => total >= 0 && used >= 0 && used <= total;

    private static bool HasMagicBudget(CharacterCreationMagicResonanceBudgetState budget) =>
        !string.IsNullOrWhiteSpace(budget.Kind) && budget.Kind == budget.Kind.Trim()
        && budget.Total >= 0m && budget.Used >= 0m && budget.Used <= budget.Total
        && budget.Remaining == budget.Total - budget.Used && budget.Blockers is not null;

    private static bool HasIntrinsicAssignments(CharacterCreationPrerequisiteDraft prerequisite)
    {
        for (int index = 0; index < prerequisite.Assignments.Count; index++)
        {
            var assignment = prerequisite.Assignments[index];
            if (assignment.Order != index
                || !string.Equals(assignment.CategoryId,
                    CharacterCreationPriorityCategoryIds.Ordered[index], StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(assignment.Rank) || assignment.Rank != assignment.Rank.Trim()
                || !Guid.TryParseExact(assignment.SourceId, "D", out Guid sourceId) || sourceId == Guid.Empty
                || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(assignment.SourceNodeDigest)
                || assignment.SumToTenValue < 0)
                return false;
        }
        return true;
    }

    private static bool SameDigest(string left, string right) =>
        CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(left, right);

    private static bool HasNonNullCollectionElements(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().All(item => item.ValueKind != JsonValueKind.Null
                && HasNonNullCollectionElements(item));
        return element.ValueKind != JsonValueKind.Object
            || element.EnumerateObject().All(property => HasNonNullCollectionElements(property.Value));
    }

    // These persisted maps carry nonnullable prompt/effect/source strings.
    // Nullable annotations alone do not validate dictionary value elements.
    private sealed class NonNullStringMapConverter : JsonConverter<IReadOnlyDictionary<string, string>>
    {
        public override IReadOnlyDictionary<string, string> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("History validation does not deserialize state.");

        public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var pair in value)
            {
                if (pair.Key is null || pair.Value is null)
                    throw new JsonException("History map entries must not be null.");
                writer.WriteString(pair.Key, pair.Value);
            }
            writer.WriteEndObject();
        }
    }
}
