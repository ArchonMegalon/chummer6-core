using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationMagicResonanceService
{
    /// <summary>Capture only from a fully validated original completion read.
    /// No original draft, authority or source is rewritten/resealed. The selected
    /// union retains exact payloads, including automatic quality and gear grants.</summary>
    public static CharacterCreationFoundationResult<CharacterCreationSelectedMagicAuthority> CaptureSelectedMagicAuthority(
        CharacterCreationMagicResonanceCompletionInputs inputs,
        CharacterCreationMagicResonanceSelections proposed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(proposed);
        cancellationToken.ThrowIfCancellationRequested();
        inputs = Detach(inputs);
        proposed = Detach(proposed);
        if (!SelectedFullInputShape(inputs) || !HasCompletionSelectionShape(proposed)
            || !IsValidCompletionInputs(inputs)
            || !MatchesCompletionAttributes(inputs.Binding, inputs.PrerequisiteDraft, inputs.Attributes))
            return SelectedReject<CharacterCreationSelectedMagicAuthority>(CharacterCreationSelectedMagicRules.Invalid);
        if (!RetainsSelectedMagic(inputs.Selections, proposed))
            return SelectedReject<CharacterCreationSelectedMagicAuthority>(CharacterCreationSelectedMagicRules.ManualChoiceConflict);
        var source = inputs.Authority;
        var metatypes = source.Metatypes.Where(row => row.MetatypeSourceId
            == inputs.PrerequisiteDraft.HeritageSelection?.MetatypeSourceId).Take(2).ToArray();
        if (metatypes.Length != 1)
            return SelectedReject<CharacterCreationSelectedMagicAuthority>(CharacterCreationMagicResonanceBlockers.MetatypePrerequisiteUnresolved);
        var original = NormalizeSelections(inputs.Selections);
        proposed = NormalizeSelections(proposed);
        var union = SelectedIdentities(original, proposed).ToHashSet();
        CharacterCreationMagicResonanceCatalogOption[] Slice(IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> catalog)
            => catalog.Where(row => union.Contains(row.Identity)).ToArray();
        var witness = new CharacterCreationSelectedMagicAuthority(CharacterCreationSelectedMagicRules.Schema,
            source.AuthorityDigest, inputs.SnapshotDigest, source.SettingsProfileId,
            source.PrerequisiteAuthorityDigest, source.SourceInputsDigest, source.CustomDataInputsDigest,
            source.GmPolicyDigest, source.RuntimeDigest, inputs.PrerequisiteDraft.DraftRevision,
            inputs.PrerequisiteDraft.DraftDigest,
            CharacterCreationSelectedMagicRules.ComputeAttributesDigest(inputs.Attributes.Attributes),
            inputs.SavedMagicState is not null, inputs.SelectedTalent, metatypes[0], source.MysticAdeptPowerPointPolicy,
            original, proposed, Slice(source.Traditions), Slice(source.Streams), Slice(source.AdeptPowers),
            Slice(source.Spells), Slice(source.ComplexForms), source.SourceAnchorIds.ToArray(), string.Empty);
        witness = witness with { PayloadDigest = CharacterCreationSelectedMagicRules.ComputePayloadDigest(witness) };
        if (!IsValidSelectedMagicAuthority(witness))
            return SelectedReject<CharacterCreationSelectedMagicAuthority>(CharacterCreationSelectedMagicRules.Invalid);
        cancellationToken.ThrowIfCancellationRequested();
        return new(CharacterCreationFoundationOutcomes.Success, Detach(witness), []);
    }

    /// <summary>Pure historical replay. The caller supplies separately evaluated
    /// original/proposed Attributes and the actual original prerequisite. These
    /// rows still require Core attribute/source admission in the enclosing replay.
    /// A self-consistent witness is not current-source or ownership authority.</summary>
    public static CharacterCreationFoundationResult<CharacterCreationSelectedMagicReplay> EvaluateSelectedMagic(
        CharacterCreationSelectedMagicAuthority witness,
        CharacterCreationPrerequisiteDraft prerequisite,
        IReadOnlyList<CharacterCreationAttributeProjection> originalAttributes,
        IReadOnlyList<CharacterCreationAttributeProjection> proposedAttributes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(witness);
        ArgumentNullException.ThrowIfNull(prerequisite);
        ArgumentNullException.ThrowIfNull(originalAttributes);
        ArgumentNullException.ThrowIfNull(proposedAttributes);
        cancellationToken.ThrowIfCancellationRequested();
        witness = Detach(witness);
        prerequisite = Detach(prerequisite);
        originalAttributes = Detach(originalAttributes);
        proposedAttributes = Detach(proposedAttributes);
        if (!IsValidSelectedMagicAuthority(witness)
            || prerequisite.Assignments is null || prerequisite.Assignments.Any(row => row is null)
            || originalAttributes.Any(row => row is null) || proposedAttributes.Any(row => row is null))
            return SelectedReject<CharacterCreationSelectedMagicReplay>(CharacterCreationSelectedMagicRules.Invalid);
        if (prerequisite.DraftRevision != witness.PrerequisiteDraftRevision
            || prerequisite.DraftDigest != witness.PrerequisiteDraftDigest
            || prerequisite.AuthorityDigest != witness.PrerequisiteAuthorityDigest
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(prerequisite.DraftDigest,
                CharacterCreationPrerequisiteDraftIntegrity.ComputeDigest(prerequisite))
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(witness.OriginalAttributesDigest,
                CharacterCreationSelectedMagicRules.ComputeAttributesDigest(originalAttributes)))
            return SelectedReject<CharacterCreationSelectedMagicReplay>(CharacterCreationSelectedMagicRules.HistoryMismatch);
        var blockers = new List<string>();
        if (prerequisite.Assignments.Count(row => row.CategoryId == CharacterCreationPriorityCategoryIds.Talent) != 1)
            return SelectedReject<CharacterCreationSelectedMagicReplay>(CharacterCreationMagicResonanceBlockers.PriorityAssignmentMismatch);
        var talent = ResolveSelectedTalent(prerequisite, [witness.Talent], [witness.Metatype], blockers);
        if (talent is null || blockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, Normalize(blockers));
        var original = Evaluate(originalAttributes, witness.OriginalSelections);
        var proposed = Evaluate(proposedAttributes, witness.ProposedSelections);
        // Initial missing decisions are allowed, but an already saved original
        // cannot be legitimized by evaluating it with the proposed future MAG.
        if (witness.OriginalHadSavedMagic && original.Blockers.Count != 0)
            return SelectedReject<CharacterCreationSelectedMagicReplay>(CharacterCreationSelectedMagicRules.HistoryMismatch);
        if (original.Blockers.Contains(CharacterCreationMagicResonanceBlockers.AttributesDraftInvalid))
            return SelectedReject<CharacterCreationSelectedMagicReplay>(CharacterCreationMagicResonanceBlockers.AttributesDraftInvalid);
        var replay = new CharacterCreationSelectedMagicReplay(witness.PayloadDigest, witness.OriginalAttributesDigest,
            CharacterCreationSelectedMagicRules.ComputeAttributesDigest(proposedAttributes), original, proposed, string.Empty);
        replay = replay with { ReplayDigest = CharacterCreationSelectedMagicRules.ComputeReplayDigest(replay) };
        cancellationToken.ThrowIfCancellationRequested();
        return new(proposed.Blockers.Count == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, replay, proposed.Blockers);

        CharacterCreationSelectedMagicEvaluation Evaluate(IReadOnlyList<CharacterCreationAttributeProjection> attributes,
            CharacterCreationMagicResonanceSelections selections)
        {
            var failures = new List<string>();
            var allocations = attributes.Select(row => new CharacterCreationAttributeAllocation(
                row.AttributeId, row.PriorityPointsSpent, row.KarmaLevels)).ToArray();
            if (!CharacterCreationMagicResonanceFinalizationRules.TryResolveEffectiveAttributes(
                talent, attributes, allocations, out var effective,
                witness.MysticAdeptPowerPointPolicy, witness.Metatype.SeparateMagicAttribute))
            {
                failures.Add(CharacterCreationMagicResonanceBlockers.AttributesDraftInvalid);
                effective = new(0, 0, 0, 0m);
            }
            var evaluated = EvaluateSelections(new MagicSelectionSources(witness.Traditions, witness.Streams,
                witness.AdeptPowers, witness.Spells, witness.ComplexForms, witness.MysticAdeptPowerPointPolicy),
                talent, effective, selections, failures);
            return new(evaluated.Selections, effective, evaluated.TraditionBudget, evaluated.StreamBudget,
                evaluated.PowerBudget, evaluated.SpellBudget, evaluated.FormBudget, evaluated.MysticAdeptPowerPoints,
                evaluated.SourceAnchorIds, Normalize(failures));
        }
    }

    /// <summary>Checks the selected representation only, not membership in a live
    /// full catalogue. Full membership is established by Capture and subsequently
    /// bound by the enclosing saved archive/current-source admission.</summary>
    public static bool IsValidSelectedMagicAuthority(CharacterCreationSelectedMagicAuthority? witness)
    {
        if (witness is null || witness.Schema != CharacterCreationSelectedMagicRules.Schema
            || string.IsNullOrWhiteSpace(witness.SettingsProfileId) || witness.PrerequisiteDraftRevision <= 0
            || !new[] { witness.ProvenanceAuthorityDigest, witness.OriginalInputsSnapshotDigest,
                    witness.PrerequisiteAuthorityDigest, witness.SourceInputsDigest, witness.CustomDataInputsDigest,
                    witness.GmPolicyDigest, witness.RuntimeDigest, witness.PrerequisiteDraftDigest,
                    witness.OriginalAttributesDigest, witness.PayloadDigest }.All(CharacterCreationMagicResonanceDigest.IsCanonical)
            || !HasCompletionSelectionShape(witness.OriginalSelections)
            || !HasCompletionSelectionShape(witness.ProposedSelections)
            || !RetainsSelectedMagic(witness.OriginalSelections, witness.ProposedSelections)
            || !Same(witness.OriginalSelections, NormalizeSelections(witness.OriginalSelections))
            || !Same(witness.ProposedSelections, NormalizeSelections(witness.ProposedSelections))
            || !ValidSelectedTalentShape(witness.Talent)
            || !CharacterCreationMagicResonanceDraftIntegrity.IsValidTalent(witness.Talent)
            || !CharacterCreationMagicResonanceFinalizationRules.TryProjectTalent(witness.Talent, out _)
            || witness.Metatype is not { SourceAnchorIds: not null }
            || !Guid.TryParseExact(witness.Metatype.MetatypeSourceId, "D", out var metatypeId) || metatypeId == Guid.Empty
            || string.IsNullOrWhiteSpace(witness.Metatype.MetatypeName) || string.IsNullOrWhiteSpace(witness.Metatype.MetatypeCategory)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(witness.Metatype.SourceNodeDigest)
            || !CanonicalAnchors(witness.Metatype.SourceAnchorIds) || !CanonicalAnchors(witness.SourceAnchorIds)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(witness.PayloadDigest,
                CharacterCreationSelectedMagicRules.ComputePayloadDigest(witness))) return false;
        if (witness.MysticAdeptPowerPointPolicy is { } policy
            && (!CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(policy)
                || policy.SettingsProfileId != witness.SettingsProfileId
                || policy.SourceAnchorIds.Any(anchor => !witness.SourceAnchorIds.Contains(anchor, StringComparer.Ordinal)))) return false;
        var union = SelectedIdentities(witness.OriginalSelections, witness.ProposedSelections).ToHashSet();
        var selected = new List<CharacterCreationMagicResonanceOptionIdentity>();
        foreach (var (rows, kind) in new[]
        {
            (witness.Traditions, CharacterCreationMagicResonanceKinds.Tradition),
            (witness.Streams, CharacterCreationMagicResonanceKinds.Stream),
            (witness.AdeptPowers, CharacterCreationMagicResonanceKinds.AdeptPower),
            (witness.Spells, CharacterCreationMagicResonanceKinds.Spell),
            (witness.ComplexForms, CharacterCreationMagicResonanceKinds.ComplexForm)
        })
        {
            if (rows is null || rows.Any(row => !ValidSelectedOptionShape(row) || row.Identity.Kind != kind)
                || rows.Select(row => row.Identity).Distinct().Count() != rows.Count
                || !rows.SequenceEqual(rows.OrderBy(row => row.Name, StringComparer.Ordinal)
                    .ThenBy(row => row.Identity.SourceId, StringComparer.Ordinal))) return false;
            foreach (var row in rows)
            {
                if (!union.Contains(row.Identity) || row.Schema != CharacterCreationMagicResonanceSchemas.CatalogOptionV1
                    || !CanonicalAnchors(row.SourceAnchorIds)
                    || !CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(rows, row.Identity, 1, out _)) return false;
                selected.Add(row.Identity);
            }
        }
        return selected.Count == union.Count && selected.ToHashSet().SetEquals(union);
    }

    private static bool SelectedFullInputShape(CharacterCreationMagicResonanceCompletionInputs inputs)
        => inputs is { Binding: not null, Attributes: { Binding: not null, Attributes: not null },
            PrerequisiteDraft: { Assignments: not null }, Authority: { Talents: not null, Metatypes: not null,
                Traditions: not null, Streams: not null, AdeptPowers: not null, Spells: not null, ComplexForms: not null } }
            && ValidSelectedTalentShape(inputs.SelectedTalent)
            && inputs.Authority.Talents.All(ValidSelectedTalentShape)
            && inputs.Authority.Metatypes.All(row => row is { SourceAnchorIds: not null })
            && inputs.PrerequisiteDraft.Assignments.All(row => row is not null)
            && inputs.PrerequisiteDraft.Assignments.Count(row => row.CategoryId == CharacterCreationPriorityCategoryIds.Talent) == 1
            && inputs.Attributes.Attributes.All(row => row is not null)
            && inputs.Authority.Traditions.Concat(inputs.Authority.Streams).Concat(inputs.Authority.AdeptPowers)
                .Concat(inputs.Authority.Spells).Concat(inputs.Authority.ComplexForms).All(ValidSelectedOptionShape);

    private static bool ValidSelectedTalentShape(CharacterCreationMagicResonanceTalentOption? row)
        => row is { Identity: not null, RequiredMetatypeNames: not null, RequiredMetatypeCategories: not null,
            ForbiddenMetatypeNames: not null, SourceAnchorIds: not null, Blockers: not null };
    private static bool ValidSelectedOptionShape(CharacterCreationMagicResonanceCatalogOption? row)
        => row is { Identity: not null, SourceAnchorIds: not null, Blockers: not null };
    private static bool CanonicalAnchors(IReadOnlyList<string>? anchors)
        => anchors is { Count: > 0 } && anchors.All(value => !string.IsNullOrWhiteSpace(value) && value == value.Trim())
            && anchors.SequenceEqual(anchors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    private static IEnumerable<CharacterCreationMagicResonanceOptionIdentity> SelectedIdentities(
        CharacterCreationMagicResonanceSelections original, CharacterCreationMagicResonanceSelections proposed)
        => new[] { original, proposed }.SelectMany(row => new[] { row.Tradition, row.Stream }
            .OfType<CharacterCreationMagicResonanceOptionIdentity>().Concat(row.AdeptPowers.Select(power => power.Identity))
            .Concat(row.Spells).Concat(row.ComplexForms));
    private static bool RetainsSelectedMagic(CharacterCreationMagicResonanceSelections original,
        CharacterCreationMagicResonanceSelections proposed)
        => (original.Tradition is null || original.Tradition == proposed.Tradition)
            && (original.Stream is null || original.Stream == proposed.Stream)
            && original.MysticAdeptPowerPoints <= proposed.MysticAdeptPowerPoints
            && original.AdeptPowers.All(row => proposed.AdeptPowers.Count(other => other == row) == 1)
            && original.Spells.All(row => proposed.Spells.Count(other => other == row) == 1)
            && original.ComplexForms.All(row => proposed.ComplexForms.Count(other => other == row) == 1);
    private static CharacterCreationFoundationResult<T> SelectedReject<T>(string blocker) where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
