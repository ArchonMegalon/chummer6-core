using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationContactsService
{
    /// <summary>Read original source and history, including before any Attributes draft exists.</summary>
    public CharacterCreationContactResult<CharacterCreationContactsCompletionInputs> LoadCompletionInputs(
        CharacterCreationContactsLoadRequest request) => LoadCompletionInputs(request, false);

    internal CharacterCreationContactResult<CharacterCreationContactsCompletionInputs> LoadCompletionInputs(
        CharacterCreationContactsLoadRequest request, bool knowledgeModifiers)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return ReadFailure<CharacterCreationContactsCompletionInputs>(read);
        if (_sourceData is null || _characterQueries is null)
            return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                CharacterCreationContactsBlockers.AuthorityUnavailable);
        using ICharacterSourceDataResolverOperationScope? sourceScope =
            (_sourceData as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        ICharacterSourceDataResolver source = sourceScope ?? _sourceData;
        try
        {
            var state = workspace.Document.AuxiliaryState;
            if (RulesetDefaults.NormalizeOptional(workspace.Document.RulesetId) != RulesetDefaults.Sr5
                || state.CharacterCreationBootstrapBinding is not { } bootstrap
                || bootstrap.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
                || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(workspace.Id, workspace.ContentRevision, state)
                || !CharacterCreationBootstrapAuthority.TryValidatePending(workspace, source, out _))
                return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                    CharacterCreationContactsBlockers.AuthorityUnavailable);
            XElement? root = XDocument.Parse(workspace.Document.Content).Root;
            // Foreign original contacts have metadata and ownership not represented
            // by a creation selection. Do not discard or convert those contacts.
            if (root?.Name != "character" || ParseBool(ReadValue(root, "created"))
                || root.Elements("contacts").Count() > 1
                || root.Element("contacts")?.Elements().Any() == true)
                return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                    CharacterCreationContactsBlockers.CharacterDocumentInvalid);
            var context = source.TryCreateContext(workspace.Document.Content);
            if (context is null || !context.TryResolveCreationContactsPolicy(out var policy) || policy is null
                || !context.TryResolveCreationCarryoverPolicy(out var carryover) || carryover is null)
                return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                    CharacterCreationContactsBlockers.BudgetAuthorityRequired);

            var qualitiesResult = new CharacterCreationQualitiesService(_workspaceStore, source,
                new CharacterCreationPrerequisiteService(_workspaceStore, _characterQueries, source),
                new CharacterCreationAttributesService(_workspaceStore, source), _characterQueries)
                .LoadCompletionInputs(new(workspace.Id), knowledgeModifiers);
            if (qualitiesResult.Value is not { } qualities)
                return new(CharacterCreationContactOutcomes.Blocked, null, qualitiesResult.Blockers);
            IReadOnlyList<CharacterCreationTalentQualitySource> talentSources = [];
            if (qualities.PrerequisiteDraft.TalentSelection is not { GrantedQualities: not null } selectedTalent)
                return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                    CharacterCreationContactsBlockers.AuthorityUnavailable);
            if (selectedTalent.GrantedQualities.Count != 0)
            {
                if (!CharacterCreationTalentQualityGrants.TryResolveTalent(qualities.PrerequisiteDraft, context,
                        out _, out var talent))
                    return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                        CharacterCreationContactsBlockers.AuthorityUnavailable);
                talentSources = talent.GrantedQualitySources!;
            }
            if (!CharacterCreationLegacySourceProjector.AreCompletionGrantEffectsIndependent(
                    qualities.PrerequisiteDraft, talentSources))
                return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                    CharacterCreationContactsBlockers.AuthorityUnavailable);
            var receipts = state.CharacterCreationContactReceipts ?? [];
            if (!CharacterCreationContactReceiptLedgerIntegrity.IsValidLedger(workspace.Id,
                    workspace.ContentRevision, receipts))
                return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                    CharacterCreationContactsBlockers.ReceiptLedgerCorrupt);
            var pending = state.CharacterCreationContactsDraft;
            if (pending is not null || receipts.Count != 0)
            {
                // Immutable history has its original input identity. A composite
                // edit admits today's effects below; it never reseals this draft.
                if (pending is null || !(CharacterCreationContactsDraftRules.AdmitsRetainedCompletionHistory(
                        workspace, pending, policy, carryover)
                    || CharacterCreationContactsDraftRules.TryLoad(workspace, source, out var admitted, out _)
                        && admitted is not null && CharacterCreationContactsDraftRules.InputsMatch(pending, admitted)))
                    return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                        CharacterCreationContactsBlockers.BudgetAuthorityRequired);
            }
            if (!CharacterCreationKarmaContactsRules.TryFreeze(pending?.Contacts ?? [], out var contacts))
                return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                    CharacterCreationContactsBlockers.ContactInvalid);
            var effects = CharacterContactEditSemanticsResolver.ReadEffects(root);
            var binding = new CharacterCreationContactBinding(workspace.Id, workspace.ContentRevision,
                workspace.ContentRevision, workspace.SavedRevision,
                CharacterCreationContactsDraftRules.RawDigest(workspace.Document), workspace.Document.AuxiliaryStateDigest,
                CompletionSourceDigest(qualities, bootstrap, policy, carryover, effects, talentSources),
                CharacterCreationContactsAuthorityEvaluator.RulesDigest,
                CharacterCreationContactsAuthorityEvaluator.RuntimeDigest);
            var inputs = new CharacterCreationContactsCompletionInputs(CharacterCreationContactsCompletionSchemas.InputsV1,
                binding, qualities, bootstrap, policy, carryover, pending, receipts, contacts, effects, string.Empty)
                { TalentQualitySources = talentSources };
            inputs = inputs with { SnapshotDigest = CharacterCreationContactsCompletionRules.ComputeInputsDigest(inputs) };
            if (!IsValidCompletionInputs(inputs))
                return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Conflict,
                    CharacterCreationContactsCompletionSchemas.SnapshotConflict);
            // Each source lookup fences changes during this synchronous read.
            if (!context.TryResolveCreationContactsPolicy(out var afterPolicy) || !CompletionEqual(policy, afterPolicy)
                || !context.TryResolveCreationCarryoverPolicy(out var afterCarryover) || !CompletionEqual(carryover, afterCarryover)
                || selectedTalent.GrantedQualities.Count != 0
                    && (!CharacterCreationTalentQualityGrants.TryResolveTalent(qualities.PrerequisiteDraft, context,
                        out _, out var afterTalent) || !CompletionEqual(talentSources, afterTalent.GrantedQualitySources)))
                return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                    CharacterCreationContactsBlockers.StaleSourceDigest);
            if (!CharacterCreationCompositeBaselineReader.TryRead(workspace, out var composite))
                return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                    CharacterCreationCompositeBaselineReader.Invalid);
            if (composite is not null)
            {
                var selected = composite.Selections;
                var current = PreviewCompletion(inputs, selected.AttributeAllocations,
                    selected.QualityOptionIds, selected.Contacts);
                if (current.Blockers.Count != 0)
                    return new(CharacterCreationContactOutcomes.Blocked, null, current.Blockers);
            }
            return new(CharacterCreationContactOutcomes.Available, DetachCompletion(inputs), []);
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or IOException or OverflowException)
        {
            return Blocked<CharacterCreationContactsCompletionInputs>(CharacterCreationContactOutcomes.Blocked,
                CharacterCreationContactsBlockers.AuthorityUnavailable);
        }
    }

    /// <summary>Reevaluate actual proposed Attributes and Qualities before deriving either contact pool.</summary>
    public static CharacterCreationContactResult<CharacterCreationContactsCompletionProjection> PreviewCompletion(
        CharacterCreationContactsCompletionInputs inputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        IReadOnlyList<string> qualityOptionIds,
        IReadOnlyList<CharacterCreationKarmaContactSelection> contacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        ArgumentNullException.ThrowIfNull(qualityOptionIds);
        ArgumentNullException.ThrowIfNull(contacts);
        cancellationToken.ThrowIfCancellationRequested();
        inputs = DetachCompletion(inputs);
        attributeAllocations = attributeAllocations.ToArray();
        qualityOptionIds = qualityOptionIds.ToArray();
        if (inputs.Schema != CharacterCreationContactsCompletionSchemas.InputsV1
            || !FixedEquals(inputs.SnapshotDigest, CharacterCreationContactsCompletionRules.ComputeInputsDigest(inputs)))
            return Blocked<CharacterCreationContactsCompletionProjection>(CharacterCreationContactOutcomes.Blocked,
                CharacterCreationContactsBlockers.AuthorityUnavailable);
        if (!IsValidCompletionInputs(inputs))
            return Blocked<CharacterCreationContactsCompletionProjection>(CharacterCreationContactOutcomes.Conflict,
                CharacterCreationContactsCompletionSchemas.SnapshotConflict);
        if (!CharacterCreationKarmaContactsRules.TryFreeze(contacts, out var frozen))
            return Blocked<CharacterCreationContactsCompletionProjection>(CharacterCreationContactOutcomes.Blocked,
                CharacterCreationContactsBlockers.ContactInvalid);
        frozen = DetachCompletion(frozen);
        var upstreamResult = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs.Qualities,
            attributeAllocations, qualityOptionIds, cancellationToken);
        if (upstreamResult.Value is not { } upstream || upstream.Qualities.Blockers.Count != 0)
            return new(CharacterCreationContactOutcomes.Blocked, null, upstreamResult.Blockers);
        var pricing = PriceSelectedCompletion(new(inputs.Policy, inputs.Qualities.PrerequisiteDraft,
            inputs.TalentQualitySources, inputs.OriginalEffects, inputs.Contacts,
            upstream.Attributes.Attributes, upstream.Qualities.Selections, frozen,
            inputs.Binding.SourceDigest, upstream.Blockers,
            inputs.Qualities.Authority.CostPolicy ?? CharacterCreationQualityCostPolicy.Default,
            inputs.Qualities.Authority.QualityKarmaLimit,
            inputs.Qualities.Authority.MayExceedPositiveQualityLimit), cancellationToken);
        if (pricing.Value is not { } priced)
            return new(pricing.Outcome, null, pricing.Blockers);
        var result = new CharacterCreationContactsCompletionProjection(CharacterCreationContactsCompletionSchemas.ProjectionV1,
            inputs.Binding, inputs.SnapshotDigest, upstream, priced.Contacts, priced.ContactBudget,
            priced.HighPlacesBudget, priced.AdditionalKarmaCost, priced.Blockers, string.Empty)
            { GroupKarma = priced.GroupKarma };
        result = result with { ProposalDigest = CharacterCreationContactsCompletionRules.ComputeProjectionDigest(result) };
        cancellationToken.ThrowIfCancellationRequested();
        return new(pricing.Outcome, DetachCompletion(result), priced.Blockers);
    }

    /// <summary>
    /// Price against already evaluated Attributes/Qualities and actual original
    /// contacts/effects. SourceProvenanceSeed identifies admitted source evidence;
    /// it does not pretend these rows are the old full completion input graph.
    /// Source/history admission remains the responsibility of the calling layer.
    /// </summary>
    internal static CharacterCreationContactResult<SelectedCompletionContactsPrice> PriceSelectedCompletion(
        SelectedCompletionContactsPricingInput inputs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();
        inputs = DetachCompletion(inputs);
        if (inputs.Policy is null || inputs.Prerequisite is null || inputs.TalentQualitySources is null
            || inputs.OriginalEffects is null || inputs.OriginalEffects.Any(effect => effect is null)
            || inputs.Attributes is null || inputs.Attributes.Any(attribute => attribute is null)
            || inputs.Attributes.Select(attribute => attribute.AttributeId).Distinct(StringComparer.Ordinal).Count() != inputs.Attributes.Count
            || inputs.QualitySelections is null || inputs.UpstreamBlockers is null
            || !CharacterCreationKarmaContactsRules.IsValidPolicy(inputs.Policy)
            || !CharacterCreationQualitiesRules.IsCanonicalDigest(inputs.SourceProvenanceSeed)
            || !CharacterCreationKarmaContactsRules.TryFreeze(inputs.OriginalContacts, out _))
            return Blocked<SelectedCompletionContactsPrice>(CharacterCreationContactOutcomes.Blocked,
                CharacterCreationContactsBlockers.AuthorityUnavailable);
        if (!CharacterCreationKarmaContactsRules.TryFreeze(inputs.Contacts, out var frozen))
            return Blocked<SelectedCompletionContactsPrice>(CharacterCreationContactOutcomes.Blocked,
                CharacterCreationContactsBlockers.ContactInvalid);
        if (!CharacterCreationLegacySourceProjector.AreCompletionGrantEffectsIndependent(
                inputs.Prerequisite, inputs.TalentQualitySources)
            || !CharacterCreationLegacySourceProjector.TryProjectCompletionQualityEffects(inputs.QualitySelections,
                out var qualityEffects))
            return Blocked<SelectedCompletionContactsPrice>(CharacterCreationContactOutcomes.Blocked,
                CharacterCreationContactsBlockers.AuthorityUnavailable);

        // The source compiler fails closed for unsupported effects (including
        // attribute modifiers). Supported effects are shared with finalization,
        // not inferred from quality names or caller-provided allowance scalars.
        CharacterContactRuleEffect[] effects = inputs.OriginalEffects.Concat(qualityEffects
            .Where(effect => !CharacterCreationSkillKarmaModifierRules.IsSupportedType(effect.ImprovementType))
            .Select(effect =>
            new CharacterContactRuleEffect(effect.ImprovementType, effect.ImprovedName,
                effect.Value.ToString(CultureInfo.InvariantCulture), string.Empty, false, 1, string.Empty))).ToArray();
        var attributes = inputs.Attributes.ToDictionary(attribute => attribute.AttributeId,
            attribute => attribute.Current, StringComparer.Ordinal);
        if (!CharacterCreationKarmaContactsRules.TryContactPoints(inputs.Policy, attributes, out int allowance)
            || !attributes.TryGetValue("CHA", out int charisma) || charisma < 0)
            return Blocked<SelectedCompletionContactsPrice>(CharacterCreationContactOutcomes.Blocked,
                CharacterCreationContactsBlockers.BudgetAuthorityRequired);
        cancellationToken.ThrowIfCancellationRequested();
        var blockers = new List<string>(inputs.UpstreamBlockers);
        var proposedInputs = frozen.Select(CompletionContactInput).ToArray();
        var semanticsById = new Dictionary<Guid, CharacterContactEditSemantics>();
        foreach (var contact in frozen)
        {
            if (!CharacterContactEditSemanticsResolver.TryResolve(CompletionContactInput(contact), effects, out var semantics))
            {
                blockers.Add(CharacterCreationContactsBlockers.AuthorityUnavailable);
                continue;
            }
            // The saved-document resolver normalizes legacy values. A proposed
            // selection cannot use that normalization to understate its cost.
            if (contact.Connection != semantics.Connection || contact.Loyalty != semantics.Loyalty)
                blockers.Add(CharacterCreationContactsBlockers.ContactInvalid);
            var original = inputs.OriginalContacts.SingleOrDefault(value => value.ContactId == contact.ContactId)
                ?? new CharacterCreationKarmaContactSelection(contact.ContactId, contact.Identity, 1, 1);
            if (!CharacterContactEditSemanticsResolver.TryResolve(CompletionContactInput(original),
                    effects, out var originalSemantics))
                blockers.Add(CharacterCreationContactsBlockers.AuthorityUnavailable);
            else if (!originalSemantics.IdentityEditable && contact.Identity != original.Identity
                    || !originalSemantics.ConnectionEditable && contact.Connection != originalSemantics.Connection
                    || !originalSemantics.LoyaltyEditable && contact.Loyalty != originalSemantics.Loyalty
                    || !originalSemantics.GroupEditable && contact.IsGroup != originalSemantics.IsGroup
                    || !originalSemantics.FreeEditable && contact.Free != originalSemantics.Free
                    || !originalSemantics.FamilyEditable && contact.Family != originalSemantics.Family
                    || !originalSemantics.BlackmailEditable && contact.Blackmail != originalSemantics.Blackmail)
                blockers.Add(CharacterCreationContactsBlockers.FieldNotEditable);
            semanticsById.Add(contact.ContactId, semantics);
        }
        foreach (var original in inputs.OriginalContacts.Where(original => !frozen.Any(contact => contact.ContactId == original.ContactId)))
            if (!CharacterContactEditSemanticsResolver.TryResolve(CompletionContactInput(original),
                    inputs.OriginalEffects, out var semantics) || !semantics.CanDelete)
                blockers.Add(CharacterCreationContactsBlockers.FieldNotEditable);
        var authority = CharacterCreationContactsAuthorityEvaluator.EvaluateInputs(false,
            inputs.SourceProvenanceSeed, allowance, true, charisma, true, proposedInputs, effects, []);
        blockers.AddRange(authority.AuthorityBlockers);
        blockers.AddRange(authority.ContactBudget.Blockers);
        blockers.AddRange(authority.HighPlacesBudget.Blockers);
        bool paidGroup = semanticsById.Values.Any(semantics => semantics.IsGroup && !semantics.Free);
        if (authority.Contacts.Any(contact => contact.ContactPointCost > 7 && !contact.CountsAgainstHighPlacesBudget))
            blockers.Add(CharacterCreationKarmaContactsRules.ContactLimitExceeded);
        var projections = new List<CharacterCreationContactProjection>();
        foreach (var contact in frozen)
        {
            var cost = authority.Contacts.SingleOrDefault(value => value.ContactId == contact.ContactId);
            if (cost is null || !semanticsById.TryGetValue(contact.ContactId, out var semantics)) continue;
            var projection = new CharacterCreationContactProjection(contact.ContactId, contact.Identity,
                semantics.Connection, semantics.Loyalty, semantics.IsGroup, semantics.Free, semantics.Family,
                semantics.Blackmail, cost.ContactPointCost, cost.CountsAgainstContactBudget,
                cost.CountsAgainstHighPlacesBudget, BuildFields(contact.Identity, semantics, created: false),
                CharacterCreationContactSourceAnchors.All, string.Empty) { CanDelete = semantics.CanDelete };
            projections.Add(projection with
            {
                ContactDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(projection)
            });
        }
        CharacterCreationContactGroupKarmaEvaluation? groupKarma = null;
        int? additionalKarma = authority.AuthorityBlockers.Count == 0 ? 0 : null;
        if (paidGroup)
        {
            additionalKarma = null;
            if (authority.AuthorityBlockers.Count == 0 && authority.ContactBudget.IsExact
                && authority.HighPlacesBudget.IsExact && projections.Count == frozen.Length
                && !blockers.Contains(CharacterCreationContactsBlockers.ContactInvalid)
                && !blockers.Contains(CharacterCreationContactsBlockers.FieldNotEditable)
                && CharacterCreationKarmaContactsRules.TryPriceGroups(inputs.Policy, inputs.QualityCostPolicy,
                    inputs.QualityKarmaLimit, inputs.QualitySelections.Select(item => new CharacterCreationQualityCostItem(
                        item.KarmaCost, item.CountsAgainstQualityLimit, item.CountsAgainstKarma, item.IsMetagenic)).ToArray(),
                    projections.Where(contact => contact.IsGroup && !contact.Free).Select(contact => contact.ContactPointCost),
                    out groupKarma))
            {
                additionalKarma = groupKarma!.AdditionalKarmaCost;
                if (groupKarma.WithGroups.PositiveLimitKarma > inputs.QualityKarmaLimit
                    && !inputs.MayExceedPositiveQualityLimit)
                    blockers.Add(CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
            }
            if (groupKarma is null)
                blockers.Add(CharacterCreationContactsCompletionSchemas.KarmaContributionUnavailable);
        }
        string[] normalized = NormalizeCompletion(blockers);
        var result = new SelectedCompletionContactsPrice(projections, authority.ContactBudget,
            authority.HighPlacesBudget, additionalKarma, normalized) { GroupKarma = groupKarma };
        cancellationToken.ThrowIfCancellationRequested();
        return new(normalized.Length == 0 ? CharacterCreationContactOutcomes.Available : CharacterCreationContactOutcomes.Blocked,
            result, normalized);
    }

    private static bool IsValidCompletionInputs(CharacterCreationContactsCompletionInputs inputs)
    {
        if (inputs.Schema != CharacterCreationContactsCompletionSchemas.InputsV1 || inputs.Binding is not { } binding
            || inputs.Qualities?.Binding is not { } qualities || inputs.BootstrapBinding is not { } bootstrap
            || inputs.Qualities.Authority is null || inputs.Qualities.PrerequisiteDraft is null
            || inputs.Qualities.Attributes is null || !CharacterCreationBootstrapBindingDigest.IsValid(bootstrap)
            || inputs.Receipts is null || inputs.OriginalEffects is null || inputs.OriginalEffects.Any(effect => effect is null)
            || inputs.TalentQualitySources is null || inputs.TalentQualitySources.Any(source => source is null)
            || !CharacterCreationKarmaContactsRules.TryFreeze(inputs.Contacts, out var contacts)
            || !CharacterCreationKarmaContactsRules.IsValidPolicy(inputs.Policy)
            || !CharacterCreationKarmaFinalizationBudgetRules.IsValidPolicy(inputs.CarryoverPolicy)
            || !FixedEquals(inputs.SnapshotDigest, CharacterCreationContactsCompletionRules.ComputeInputsDigest(inputs))
            || binding.ContentRevision <= 0 || binding.WorkspaceRevision != binding.ContentRevision
            || binding.ContentRevision != binding.SavedRevision || binding.WorkspaceId != qualities.WorkspaceId
            || binding.ContentRevision != qualities.ContentRevision || binding.SavedRevision != qualities.SavedRevision
            || !FixedEquals(binding.ContentDigest, qualities.RawCharacterXmlDigest)
            || !FixedEquals(binding.AuxiliaryStateDigest, qualities.AuxiliaryStateDigest)
            || bootstrap.WorkspaceId != binding.WorkspaceId || bootstrap.RawCharacterXmlDigest != binding.ContentDigest
            || bootstrap.BuildMethod != qualities.BuildMethod
            || bootstrap.SettingsProfileId != inputs.Policy.SettingsProfileId
            || bootstrap.RawProfileInputsDigest != inputs.Policy.RawProfileInputsDigest
            || bootstrap.SettingsProfileId != inputs.CarryoverPolicy.SettingsProfileId
            || bootstrap.RawProfileInputsDigest != inputs.CarryoverPolicy.RawProfileInputsDigest
            || bootstrap.SettingsProfileId != inputs.Qualities.Authority.SettingsProfileId
            || binding.RulesDigest != CharacterCreationContactsAuthorityEvaluator.RulesDigest
            || binding.RuntimeDigest != CharacterCreationContactsAuthorityEvaluator.RuntimeDigest
            || binding.SourceDigest != CompletionSourceDigest(inputs.Qualities, bootstrap,
                inputs.Policy, inputs.CarryoverPolicy, inputs.OriginalEffects, inputs.TalentQualitySources)
            || !CharacterCreationContactReceiptLedgerIntegrity.IsValidLedger(binding.WorkspaceId,
                binding.ContentRevision, inputs.Receipts)) return false;
        if (inputs.PendingDraft is not { } pending)
            return contacts.Length == 0 && inputs.Receipts.Count == 0;
        return inputs.Receipts.Count > 0
            && inputs.Receipts[^1].Receipt.WritePlan.PendingDraft is { } receiptedDraft
            && CompletionEqual(pending, receiptedDraft)
            && CharacterCreationContactsDraftRules.IsValidShape(binding.WorkspaceId, binding.ContentRevision, pending)
            && pending.RawCharacterXmlDigest == binding.ContentDigest
            && pending.PrerequisiteDraftDigest == inputs.Qualities.PrerequisiteDraft.DraftDigest
            // PendingDraft/Receipts describe historical inputs. Qualities holds
            // current inputs; PreviewCompletion re-evaluates the exact contacts
            // with their current effects. Only the source-bound loader may admit
            // a changed binding, against a semantically replayed composite origin.
            && CompletionEqual(pending.Policy, inputs.Policy) && CompletionEqual(pending.CarryoverPolicy, inputs.CarryoverPolicy)
            && CompletionEqual(pending.Contacts, contacts);
    }

    private static string CompletionSourceDigest(CharacterCreationQualitiesCompletionInputs qualities,
        CharacterCreationBootstrapBinding bootstrap, CharacterCreationKarmaContactsPolicy policy,
        CharacterCreationKarmaCarryoverPolicy carryover, IReadOnlyList<CharacterContactRuleEffect> effects,
        IReadOnlyList<CharacterCreationTalentQualitySource> talentSources)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = CharacterCreationContactsCompletionSchemas.InputsV1,
            bootstrap.BindingDigest, qualities.SnapshotDigest, Policy = policy.AuthorityDigest,
            Carryover = carryover.AuthorityDigest, OriginalEffects = effects, TalentQualitySources = talentSources
        });

    private static CharacterContactEditInput CompletionContactInput(CharacterCreationKarmaContactSelection contact)
        => new(contact.ContactId.ToString("D"), false, false, false, contact.IsGroup, contact.Free,
            contact.Family, contact.Blackmail, contact.Connection, contact.Loyalty, false);

    private static bool CompletionEqual<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);

    private static string[] NormalizeCompletion(IEnumerable<string> blockers)
        => blockers.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static T DetachCompletion<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
