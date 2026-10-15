using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using static Chummer.Application.Characters.CharacterCreationFinalizationProjector;

namespace Chummer.Application.Characters;

/// <summary>
/// Projects a whole reviewed proposal directly from evaluated Core rows. No
/// intermediate saved draft or receipt is manufactured. This pure operation
/// cannot establish current source or owner authority: an atomic consumer must
/// freshly resolve and replay its inputs under the mutation lease.
/// </summary>
public static class CharacterCreationCompletionFinalizationProjector
{
    public static CharacterCreationFoundationResult<CharacterCreationCompletionFinalizationProjection> Project(
        WorkspaceStoredDocument original,
        CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReview review,
        CharacterCreationStartingNuyenSource startingCashSource,
        CharacterCreationStartingCashChoice? cashChoice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(startingCashSource);
        var evaluated = EvaluateAndProject(original, bootstrap, inputs, review.Request,
            _ => startingCashSource, cashChoice, review, cancellationToken);
        return new(evaluated.Outcome, evaluated.Value?.Projection, evaluated.Blockers);
    }

    /// <summary>
    /// Evaluates an owned observation once, then projects those exact rows. The
    /// source callback receives only the admitted lifestyle source ID, never a
    /// mutable review. This does not establish current mutation authority.
    /// </summary>
    internal static CharacterCreationFoundationResult<EvaluatedProjection> ReviewAndProject(
        WorkspaceStoredDocument original,
        CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest proposal,
        Func<Guid?, CharacterCreationStartingNuyenSource?> resolveStartingCash,
        CharacterCreationStartingCashChoice? cashChoice,
        CancellationToken cancellationToken = default)
        => EvaluateAndProject(original, bootstrap, inputs, proposal, resolveStartingCash,
            cashChoice, null, cancellationToken);

    // The full-review entry point retains the detached authority for the transaction,
    // not an evaluator token which could bypass later replay or source admission.
    internal sealed record EvaluatedProjection(
        CharacterCreationCompletionReviewInputs Inputs,
        CharacterCreationCompletionBootstrapSnapshot Bootstrap,
        CharacterCreationCompletionReviewRequest Proposal,
        Guid? StartingLifestyleSourceId,
        CharacterCreationCompletionFinalizationProjection Projection);

    // Internal graph inputs are evaluated rows, not a saved draft, a full review,
    // or save authority. A selected-history caller must first authenticate the
    // actual observation and replay all selections, effects and shared budgets.
    // The three provenance digests retain the admitted projection identity; they
    // are not digests of these reduced rows or evidence of current source authority.
    internal sealed record EvaluatedGraphInputs(
        WorkspaceStoredDocument Original,
        string InputsSnapshotDigest,
        string OriginalReviewDigest,
        string BootstrapSourceSnapshotDigest,
        CharacterCreationPrerequisiteDraft Prerequisite,
        CharacterCreationAttributeSkillsPreview Skills,
        int SelectedActiveSkillPoints,
        int SelectedSkillGroupPoints,
        IReadOnlyList<string> SkillsSourceAnchorIds,
        EvaluatedQualities Qualities,
        EvaluatedResources Resources,
        EvaluatedGear Gear,
        EvaluatedLifestyles Lifestyles,
        EvaluatedContacts Contacts,
        CharacterCreationKarmaCarryoverPolicy CarryoverPolicy,
        int RemainingCreationKarma,
        decimal RemainingNuyen,
        AwakenedGraphInputs Awakened)
    {
        internal CharacterCreationLoadoutCompletionProjection? Loadout { get; init; }
    }

    internal sealed record EvaluatedQualities(
        IReadOnlyList<CharacterCreationQualitySelection> Selections,
        int KarmaRemaining,
        IReadOnlyList<string> SourceAnchorIds);

    internal sealed record EvaluatedResources(
        CharacterCreationResourceAllocationOption SelectedOption,
        CharacterCreationResourcesBudget Budget);

    internal sealed record EvaluatedGear(
        IReadOnlyList<CharacterCreationGearLine> Lines,
        CharacterCreationGearBudget Budget,
        IReadOnlyList<string> SourceAnchorIds);

    internal sealed record EvaluatedLifestyles(
        CharacterCreationLifestylesAuthority Authority,
        IReadOnlyList<CharacterCreationLifestyleProjection> Lifestyles,
        Guid? StartingLifestyleId,
        IReadOnlyList<string> SourceAnchorIds);

    internal sealed record EvaluatedContacts(
        CharacterCreationKarmaContactsPolicy Policy,
        IReadOnlyList<CharacterCreationKarmaContactSelection> Selections);

    internal abstract record AwakenedGraphInputs(IReadOnlyList<string> SourceAnchorIds);

    internal sealed record FullAwakenedGraphInputs(
        CharacterCreationMagicResonanceCompletionInputs Inputs,
        CharacterCreationMagicResonanceCompletionProjection Projection)
        : AwakenedGraphInputs(Projection.SourceAnchorIds);

    internal sealed record SelectedAwakenedGraphInputs(
        CharacterCreationSelectedMagicAuthority Authority,
        IReadOnlyList<CharacterCreationAttributeProjection> OriginalAttributes,
        IReadOnlyList<string> SourceAnchorIds)
        : AwakenedGraphInputs(SourceAnchorIds);

    private static CharacterCreationFoundationResult<EvaluatedProjection> EvaluateAndProject(
        WorkspaceStoredDocument original,
        CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest proposal,
        Func<Guid?, CharacterCreationStartingNuyenSource?> resolveStartingCash,
        CharacterCreationStartingCashChoice? cashChoice,
        CharacterCreationCompletionReview? suppliedReview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(resolveStartingCash);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            inputs = Detach(inputs);
            if (suppliedReview is not null)
            {
                suppliedReview = Detach(suppliedReview);
                proposal = suppliedReview.Request;
            }
            bootstrap = Detach(bootstrap);
            original = original with { Document = original.Document with
            {
                State = original.Document.State with { AuxiliaryState = Detach(original.Document.AuxiliaryState) }
            } };
            var replay = CharacterCreationCompletionReviewService.Review(inputs, proposal, cancellationToken);
            if (replay.Outcome != CharacterCreationFoundationOutcomes.Success || replay.Value is null)
                return new(replay.Outcome, null, replay.Blockers);
            var review = replay.Value;
            if (!MatchesOriginal(original, bootstrap, inputs))
                return Refused(CharacterCreationCompletionFinalizationSchemas.ObservationConflict,
                    CharacterCreationFoundationOutcomes.Conflict);
            if (suppliedReview is not null && !Same(review, suppliedReview)
                || review.Skills is null || review.UpstreamBudget is null
                || review.Lifestyles is null || review.Contacts is null || review.Blockers.Count != 0)
                return Refused(CharacterCreationCompletionFinalizationSchemas.ReviewConflict,
                    CharacterCreationFoundationOutcomes.Conflict);
            if (review.Balances.RemainingCreationKarma is not >= 0
                || review.Balances.RemainingNuyen is not >= 0
                || review.Balances.KarmaOverspend != 0 || review.Balances.NuyenOverspend != 0)
                return Refused(CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);
            if (!TryGetStartingLifestyleSourceId(review, out var startingLifestyleSourceId)
                || resolveStartingCash(startingLifestyleSourceId) is not { } resolvedStartingCash)
                return Refused(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
            var startingCashSource = Detach(resolvedStartingCash);

            var prerequisite = inputs.Skills.PrerequisiteDraft;
            var lifestyles = review.Lifestyles;
            var gear = lifestyles.Gear;
            var resources = gear.Resources;
            var qualities = review.UpstreamBudget.Qualities.Qualities;
            var graph = new EvaluatedGraphInputs(original, inputs.SnapshotDigest, review.ReviewDigest,
                bootstrap.SnapshotDigest, prerequisite, review.Skills,
                inputs.Skills.SelectedActiveSkillPoints, inputs.Skills.SelectedSkillGroupPoints,
                inputs.Skills.Authority.SourceAnchorIds,
                new(qualities.Selections, qualities.KarmaRemaining, qualities.SourceAnchorIds),
                new(resources.SelectedOption, resources.Budget),
                new(gear.Lines, gear.Budget, gear.SourceAnchorIds),
                new(lifestyles.Authority, lifestyles.Lifestyles, lifestyles.StartingLifestyleId, lifestyles.SourceAnchorIds),
                new(inputs.Contacts.Policy, review.Request.Selections.Contacts), inputs.Contacts.CarryoverPolicy,
                review.Balances.RemainingCreationKarma.Value, review.Balances.RemainingNuyen.Value,
                new FullAwakenedGraphInputs(inputs.Magic, review.UpstreamBudget.Magic))
                { Loadout = review.Loadout };
            var projected = ProjectEvaluated(graph, startingCashSource, cashChoice, cancellationToken);
            return new(projected.Outcome, projected.Value is null ? null :
                new(inputs, bootstrap, review.Request, startingLifestyleSourceId, projected.Value), projected.Blockers);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
            or OverflowException or XmlException or ArgumentException or FormatException or JsonException)
        {
            return Refused(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        }
    }

    /// <summary>
    /// Builds the common graph only after the caller's full or selected-history
    /// admission. This is not a public review bypass or a mutation capability.
    /// Selected Magic is independently replayed with original and proposed
    /// attributes; all other supplied rows must be freshly evaluated by the caller.
    /// </summary>
    internal static CharacterCreationFoundationResult<CharacterCreationCompletionFinalizationProjection> ProjectEvaluated(
        EvaluatedGraphInputs graph,
        CharacterCreationStartingNuyenSource startingCashSource,
        CharacterCreationStartingCashChoice? cashChoice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(startingCashSource);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var original = graph.Original;
            var prerequisite = graph.Prerequisite;
            var skills = graph.Skills;
            var attributes = skills.Attributes;
            var lifestyles = graph.Lifestyles;
            var gear = graph.Gear;
            var resources = graph.Resources;
            var qualities = graph.Qualities;
            var carryover = graph.CarryoverPolicy;
            var originalBootstrap = original.Document.AuxiliaryState.CharacterCreationBootstrapBinding
                ?? throw new InvalidDataException("Missing original bootstrap binding.");
            if (graph.RemainingCreationKarma < 0 || graph.RemainingNuyen < 0)
                return RefusedProjection(CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);
            if (!CharacterCreationKarmaFinalizationBudgetRules.IsValidPolicy(carryover)
                || carryover.SettingsProfileId != originalBootstrap.SettingsProfileId
                || carryover.RawProfileInputsDigest != originalBootstrap.RawProfileInputsDigest
                || carryover.MaximumNuyen != resources.Budget.CarryoverLimit)
                return RefusedProjection(CharacterCreationFinalizationBlockers.CarryoverPolicyUnavailable);
            if (cashChoice is null)
                return RefusedProjection(CharacterCreationFinalizationBlockers.StartingCashChoiceRequired);
            if (!CharacterCreationFinalizationStartingCashRules.TryPrepare(startingCashSource,
                    lifestyles.Authority, cashChoice, out var cashAuthority)
                || cashAuthority is null || !MatchesStartingLifestyle(lifestyles, cashAuthority)
                || startingCashSource.SettingsProfileId != carryover.SettingsProfileId
                || startingCashSource.RawProfileInputsDigest != carryover.RawProfileInputsDigest)
                return RefusedProjection(CharacterCreationFinalizationBlockers.StartingCashChoiceInvalid);

            // Legal unused pools remain visible for review; they are neither
            // silently spent nor transformed into another kind of currency.
            var unused = new CharacterCreationCompletionUnusedPoints(
                RemainingPoints(attributes.NormalPointBudget), RemainingPoints(attributes.SpecialPointBudget),
                RemainingPoints(skills.ActiveSkillPointBudget),
                RemainingPoints(skills.SkillGroupPointBudget),
                RemainingPoints(skills.KnowledgeSkillPointBudget));
            int karmaBefore = graph.RemainingCreationKarma;
            int karmaAfter = checked((int)Math.Min(karmaBefore, carryover.MaximumKarma));
            decimal nuyenBefore = graph.RemainingNuyen;
            decimal carriedNuyen = Math.Min(nuyenBefore, carryover.MaximumNuyen);
            decimal cash = checked(cashChoice.DiceTotal * startingCashSource.Multiplier);
            decimal careerNuyen = checked(carriedNuyen + cash);

            XDocument document = XDocument.Parse(original.Document.Content, LoadOptions.None);
            XElement root = document.Root ?? throw new InvalidDataException("Missing character root.");
            if (root.Name != "character" || !bool.TryParse(ReadDirect(root, "created"), out bool created) || created)
                return RefusedProjection(CharacterCreationFinalizationBlockers.CharacterAlreadyCreated);
            foreach (string name in new[] { "qualities", "gears", "contacts", "lifestyles" })
                EnsureEmptyContainer(root, name);
            // New loadout proposals own these containers only after an explicit
            // source-bound review. Old archives must retain their original graph
            // shape, including the absence of empty weapons/armors containers.
            if (graph.Loadout is not null)
            {
                EnsureEmptyContainer(root, "weapons");
                EnsureEmptyContainer(root, "armors");
                if (root.Element("weapons")?.HasAttributes == true || root.Element("armors")?.HasAttributes == true)
                    return RefusedProjection(CharacterCreationLoadoutCompletionSchemas.ExistingGraphUnsupported);
            }
            var deltas = new List<CharacterCreationFinalizationDelta>();
            int order = 0;
            foreach (string field in CharacterCreationCareerBaseline.InitializeMissing(root))
                AddDelta(deltas, ref order, $"career-initialization:{field}",
                    CharacterCreationFinalizationDeltaKinds.Lifecycle, field, null, ReadDirect(root, field), 0, 0, []);
            AddDelta(deltas, ref order, "lifecycle:created", CharacterCreationFinalizationDeltaKinds.Lifecycle,
                "created", "False", "True", 0, 0, prerequisite.SourceAnchorIds);
            var heritage = prerequisite.HeritageSelection!;
            AddDelta(deltas, ref order, "metatype:selected", CharacterCreationFinalizationDeltaKinds.Metatype,
                heritage.MetatypeSourceId, ReadDirect(root, "metatype"), heritage.MetatypeName,
                heritage.KarmaCost, 0, heritage.SourceAnchorIds, heritage.MetatypeName);
            SetDirect(root, "metatype", heritage.MetatypeName);
            SetDirect(root, "metavariant", heritage.MetavariantName ?? string.Empty);
            SetDirect(root, "metatypebp", Number(heritage.KarmaCost));
            SetDirect(root, "buildmethod", prerequisite.BuildMethod);
            foreach (var assignment in prerequisite.Assignments.OrderBy(value => value.Order))
            {
                string legacy = assignment.CategoryId switch
                {
                    CharacterCreationPriorityCategoryIds.Heritage => "prioritymetatype",
                    CharacterCreationPriorityCategoryIds.Talent => "priorityspecial",
                    CharacterCreationPriorityCategoryIds.Attributes => "priorityattributes",
                    CharacterCreationPriorityCategoryIds.Skills => "priorityskills",
                    CharacterCreationPriorityCategoryIds.Resources => "priorityresources",
                    _ => throw new InvalidDataException("Unknown Priority category.")
                };
                string value = string.Create(CultureInfo.InvariantCulture, $"{assignment.Rank},{assignment.SumToTenValue}");
                SetDirect(root, legacy, value);
                AddDelta(deltas, ref order, $"priority:{assignment.CategoryId}", CharacterCreationFinalizationDeltaKinds.Build,
                    assignment.CategoryId, null, value, 0, 0, assignment.SourceAnchorIds);
            }
            SetDirect(root, "prioritytalent", prerequisite.TalentSelection!.Value);
            SetDirect(root, "sumtoten", Number(prerequisite.SumToTenTarget.GetValueOrDefault()));
            ReplaceDirect(root, BuildAttributes(attributes.Attributes, deltas, ref order));
            ReplaceDirect(root, BuildSkills(skills.Skills, skills.SkillGroups,
                graph.SelectedActiveSkillPoints, graph.SelectedSkillGroupPoints,
                graph.OriginalReviewDigest, deltas, ref order));
            foreach (string flag in new[] { "magenabled", "resenabled", "depenabled", "adept", "magician", "technomancer", "ai" })
                SetDirect(root, flag, "False");
            BuildQualityAndEffectGraph(root, qualities.Selections,
                checked((int)attributes.CreationKarmaBudget.Total), checked((int)attributes.CreationKarmaBudget.Used),
                qualities.KarmaRemaining, qualities.SourceAnchorIds, graph.OriginalReviewDigest, deltas, ref order);
            if (!CharacterCreationPriorityRacialQualities.TryApply(root, heritage, prerequisite.DraftDigest, deltas, ref order))
                return RefusedProjection(CharacterCreationFinalizationBlockers.AwakenedEffectsNotProjectable);
            ReplaceDirect(root, BuildGearGraph(gear.Lines, graph.OriginalReviewDigest, deltas, ref order));
            if (graph.Loadout is { } loadout)
            {
                if (loadout.Blockers.Count != 0
                    || loadout.ProjectionDigest != CharacterCreationLoadoutCompletionRules.ComputeProjectionDigest(loadout)
                    || !CharacterCreationLoadoutSourceProjector.TryBuildGraph(loadout.Lines,
                        graph.OriginalReviewDigest, out var weapons, out var armor))
                    return RefusedProjection(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
                ReplaceDirect(root, new XElement("weapons", weapons));
                ReplaceDirect(root, new XElement("armors", armor));
                foreach (var line in loadout.Lines)
                    AddDelta(deltas, ref order, $"{line.Kind}:{line.OptionId}", line.Kind,
                        line.OptionId, null, Number(line.Quantity), 0, line.TotalCost,
                        line.SourceAnchorIds, line.Name);
            }
            if (!TryApplyAwakened(root, graph, deltas, ref order))
                return RefusedProjection(CharacterCreationFinalizationBlockers.AwakenedEffectsNotProjectable);
            CharacterCareerReputationProjector.ValidateSavedInputShape(root);

            if (!CharacterCreationKarmaContactsRules.TryContactPoints(graph.Contacts.Policy,
                    attributes.Attributes.ToDictionary(value => value.AttributeId, value => value.Current), out int contactPoints))
                return RefusedProjection(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
            SetDirect(root, "contactpoints", Number(contactPoints));
            ReplaceDirect(root, new XElement("contacts", graph.Contacts.Selections
                .Select(CharacterCreationKarmaContactsRules.BuildContactElement)));
            // Validate the actual projected graph, including the newly applied
            // quality/talent contact effects, not only its proposal budgets.
            if (CharacterCreationContactsDraftRules.ValidatePointOnlySelection(
                    new WorkspaceDocument(document.ToString(SaveOptions.DisableFormatting), RulesetDefaults.Sr5)).Length != 0)
                return RefusedProjection(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
            foreach (var contact in graph.Contacts.Selections)
                AddDelta(deltas, ref order, $"contact:{contact.ContactId:D}", "contact", contact.ContactId.ToString("D"),
                    null, contact.Identity.Name, 0, 0, graph.Contacts.Policy.SourceAnchorIds, contact.Identity.Name);
            if (lifestyles.Lifestyles.Count == 0)
            {
                if (!CharacterCreationFinalizationStartingCashRules.TryProject(original.Id, cashAuthority, out decimal fallbackCash,
                        out var fallback) || fallbackCash != cash || fallback is null)
                    return RefusedProjection(CharacterCreationFinalizationBlockers.StartingCashChoiceInvalid);
                ReplaceDirect(root, new XElement("lifestyles", fallback));
            }
            else
            {
                ReplaceDirect(root, new XElement("lifestyles", lifestyles.Lifestyles.Select(value =>
                    CharacterCreationLifestylesService.BuildLifestyleElement(value, null, lifestyles.Authority))));
                foreach (var row in lifestyles.Lifestyles)
                    AddDelta(deltas, ref order, $"lifestyle:{row.Configuration.LifestyleId:D}", "lifestyle",
                        row.Configuration.LifestyleId.ToString("D"), null, Number(row.Configuration.Increments),
                        0, row.Economics.TotalCost, row.SourceAnchorIds, row.Configuration.Name);
            }
            SetDirect(root, "karma", Number(karmaAfter));
            SetDirect(root, "nuyen", Number(careerNuyen));
            SetDirect(root, "startingnuyen", Number(resources.Budget.TotalStartingNuyen));
            SetDirect(root, "nuyenbp", Number(resources.SelectedOption.KarmaInvestment));
            SetDirect(root, "created", "True");
            root.Elements(CharacterCreationBootstrapXml.MarkerElement).Remove();
            AddDelta(deltas, ref order, "resources:starting-nuyen", CharacterCreationFinalizationDeltaKinds.Resources,
                "startingnuyen", null, Number(resources.Budget.TotalStartingNuyen), resources.SelectedOption.KarmaInvestment,
                0, resources.Budget.SourceAnchorIds);
            AddDelta(deltas, ref order, "resources:remaining-nuyen", CharacterCreationFinalizationDeltaKinds.Resources,
                "nuyen", null, Number(nuyenBefore), 0,
                checked(gear.Budget.BasketCost + (graph.Loadout?.PurchaseCost ?? 0m)),
                graph.Loadout is null ? gear.SourceAnchorIds : gear.SourceAnchorIds
                    .Concat(graph.Loadout.Lines.SelectMany(line => line.SourceAnchorIds))
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            AddDelta(deltas, ref order, "carryover:karma", CharacterCreationFinalizationDeltaKinds.Resources,
                "karma", Number(karmaBefore), Number(karmaAfter), 0, 0, carryover.SourceAnchorIds);
            AddDelta(deltas, ref order, "carryover:nuyen", CharacterCreationFinalizationDeltaKinds.Resources,
                "nuyen", Number(nuyenBefore), Number(carriedNuyen), 0, 0, carryover.SourceAnchorIds);
            AddDelta(deltas, ref order, "starting-cash:lifestyle", CharacterCreationFinalizationDeltaKinds.Resources,
                "lifestyle", null, startingCashSource.Name, 0, 0, startingCashSource.SourceAnchorIds);
            AddDelta(deltas, ref order, "starting-cash:dice", CharacterCreationFinalizationDeltaKinds.Resources,
                "starting-cash-dice", null, Number(cashChoice.DiceTotal), 0, 0, startingCashSource.SourceAnchorIds);
            AddDelta(deltas, ref order, "starting-cash:nuyen", CharacterCreationFinalizationDeltaKinds.Resources,
                "nuyen", Number(carriedNuyen), Number(careerNuyen), 0, 0, startingCashSource.SourceAnchorIds);

            string authorityDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = CharacterCreationCompletionFinalizationSchemas.ProjectionV2,
                InputsSnapshotDigest = graph.InputsSnapshotDigest,
                ReviewDigest = graph.OriginalReviewDigest,
                BootstrapSourceSnapshotDigest = graph.BootstrapSourceSnapshotDigest,
                CarryoverPolicyDigest = carryover.AuthorityDigest,
                StartingCashAuthorityDigest = cashAuthority.AuthorityDigest
            });
            string xml = document.ToString(SaveOptions.DisableFormatting);
            var binding = new CharacterCreationFinalizationBinding(original.Id, original.ContentRevision,
                original.SavedRevision, ComputeRawCharacterXmlDigest(original.Document.Content),
                original.Document.AuxiliaryStateDigest, prerequisite.BuildMethod, authorityDigest);
            var projection = new CharacterCreationCompletionFinalizationProjection(
                CharacterCreationCompletionFinalizationSchemas.ProjectionV2, binding, graph.InputsSnapshotDigest,
                graph.OriginalReviewDigest, graph.BootstrapSourceSnapshotDigest, carryover, cashAuthority, xml,
                ComputeRawCharacterXmlDigest(xml), deltas.ToArray(), deltas.SelectMany(value => value.SourceAnchorIds)
                    .Concat(prerequisite.SourceAnchorIds)
                    .Concat(attributes.Attributes.SelectMany(value => value.SourceAnchorIds))
                    .Concat(graph.SkillsSourceAnchorIds)
                    .Concat(graph.Awakened.SourceAnchorIds)
                    .Concat(qualities.SourceAnchorIds)
                    .Concat(resources.Budget.SourceAnchorIds)
                    .Concat(gear.SourceAnchorIds)
                    .Concat(lifestyles.SourceAnchorIds)
                    .Concat(graph.Contacts.Policy.SourceAnchorIds)
                    .Concat(carryover.SourceAnchorIds)
                    .Concat(startingCashSource.SourceAnchorIds)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                karmaBefore, karmaAfter, nuyenBefore, carriedNuyen, cash, careerNuyen, unused, string.Empty);
            projection = projection with { ProjectionDigest = CharacterCreationCompletionFinalizationRules.ComputeProjectionDigest(projection) };
            cancellationToken.ThrowIfCancellationRequested();
            return new(CharacterCreationFoundationOutcomes.Success, projection, []);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
            or OverflowException or XmlException or ArgumentException or FormatException or JsonException)
        {
            return RefusedProjection(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        }
    }

    private static bool TryApplyAwakened(XElement root, EvaluatedGraphInputs graph,
        ICollection<CharacterCreationFinalizationDelta> deltas, ref int order)
        => graph.Awakened switch
        {
            FullAwakenedGraphInputs full => CharacterCreationAwakenedLegacyProjector.TryApply(
                root, graph.Prerequisite, graph.Skills.Attributes.Attributes,
                graph.Skills.Skills, graph.Skills.SkillGroups, full.Inputs, full.Projection,
                graph.OriginalReviewDigest, deltas, ref order),
            SelectedAwakenedGraphInputs selected => CharacterCreationAwakenedLegacyProjector.TryApplySelected(
                root, graph.Prerequisite, selected.OriginalAttributes, graph.Skills.Attributes.Attributes,
                graph.Skills.Skills, graph.Skills.SkillGroups, selected.Authority,
                graph.OriginalReviewDigest, deltas, ref order),
            _ => false
        };

    private static bool TryGetStartingLifestyleSourceId(CharacterCreationCompletionReview review, out Guid? sourceId)
    {
        sourceId = null;
        var lifestyles = review.Lifestyles!;
        if (lifestyles.StartingLifestyleId is not Guid selected) return lifestyles.Lifestyles.Count == 0;
        var configuration = review.Request.Selections.Lifestyles.SingleOrDefault(value => value.LifestyleId == selected);
        if (configuration is null) return false;
        var option = lifestyles.Authority.LifestyleOptions.SingleOrDefault(value =>
            value.OptionId == configuration.BaseLifestyleOptionId);
        if (option is null) return false;
        sourceId = option.SourceId;
        return true;
    }

    private static bool MatchesOriginal(WorkspaceStoredDocument original,
        CharacterCreationCompletionBootstrapSnapshot bootstrap, CharacterCreationCompletionReviewInputs inputs)
    {
        var auxiliary = original.Document.AuxiliaryState;
        var binding = inputs.Skills.Binding;
        string rawDigest = ComputeRawCharacterXmlDigest(original.Document.Content);
        if (original.Document.RulesetId != RulesetDefaults.Sr5 || original.Document.Format != WorkspaceDocumentFormat.NativeXml
            || original.ContentRevision <= 0 || original.ContentRevision != original.SavedRevision
            || original.Id != binding.WorkspaceId || original.ContentRevision != binding.ContentRevision
            || original.SavedRevision != binding.SavedRevision || rawDigest != binding.RawCharacterXmlDigest
            || original.Document.AuxiliaryStateDigest != binding.AuxiliaryStateDigest
            || auxiliary.CharacterCreationFoundationDraft is not null || auxiliary.LifeModuleDecisionAcceptances is not null
            || auxiliary.CharacterCreationFinalizationArchive is not null
            || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(original.Id, original.ContentRevision, auxiliary)
            || !bootstrap.IsValid()
            || bootstrap.RawCharacterXmlDigest != rawDigest
            || !CharacterCreationBootstrapAuthority.TryPrepareBinding(original.Id, original.Document,
                bootstrap.CreateFrozenContext(), out var prepared, out _, out _)
            || !Same(prepared, auxiliary.CharacterCreationBootstrapBinding)
            || !Same(prepared, inputs.Contacts.BootstrapBinding)
            || !Same(auxiliary.CharacterCreationPrerequisiteDraft, inputs.Skills.PrerequisiteDraft)
            || !CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(auxiliary.CharacterCreationPrerequisiteDraft,
                original.Id, original.ContentRevision, rawDigest, bootstrap.Prerequisite)) return false;
        var gear = inputs.Lifestyles.Gear;
        var resources = gear.Resources;
        return Same(auxiliary.CharacterCreationAttributesDraft, inputs.Skills.Attributes.PendingDraft)
            && Same(auxiliary.CharacterCreationSkillsDraft, inputs.Skills.SavedSkillsState?.PendingDraft)
            && Same(auxiliary.CharacterCreationMagicResonanceDraft, inputs.Magic.SavedMagicState?.PendingDraft)
            && Same(auxiliary.CharacterCreationQualitiesDraft, inputs.Lifestyles.Qualities.SavedQualitiesState?.PendingDraft)
            && Same(auxiliary.CharacterCreationResourcesDraft, resources.PendingDraft)
            && Same(auxiliary.CharacterCreationGearDraft, gear.State.PendingDraft)
            && Same(auxiliary.CharacterCreationContactsDraft, inputs.Contacts.PendingDraft)
            && Same(auxiliary.CharacterCreationLifestylesDraft, resources.LifestylesDraft)
            && Same(auxiliary.CharacterCreationResourcesReceipts ?? [], resources.Receipts)
            && Same(auxiliary.CharacterCreationGearReceipts ?? [], gear.Receipts)
            && Same(auxiliary.CharacterCreationContactReceipts ?? [], inputs.Contacts.Receipts)
            && Same(auxiliary.CharacterCreationLifestyleReceipts ?? [], resources.LifestyleReceipts);
    }

    private static bool MatchesStartingLifestyle(EvaluatedLifestyles lifestyles,
        CharacterCreationFinalizationStartingCash cash)
    {
        if (lifestyles.Lifestyles.Count == 0)
            return lifestyles.StartingLifestyleId is null && CharacterCreationFinalizationStartingCashRules.MatchesPurchases(cash, null);
        var selected = lifestyles.Lifestyles.Where(value => value.Configuration.LifestyleId == lifestyles.StartingLifestyleId).ToArray();
        if (selected.Length != 1) return false;
        var options = lifestyles.Authority.LifestyleOptions.Where(value =>
            value.OptionId == selected[0].Configuration.BaseLifestyleOptionId).ToArray();
        return options.Length == 1 && options[0].SourceId.ToString("D") == cash.Source.SourceId;
    }

    private static int RemainingPoints(CharacterCreationBudgetState budget)
    {
        if (!budget.IsExact || budget.Blockers.Count != 0 || budget.Remaining < 0
            || budget.Remaining != decimal.Truncate(budget.Remaining))
            throw new InvalidDataException("Point budget is not exact.");
        return checked((int)budget.Remaining);
    }
    private static void EnsureEmptyContainer(XElement root, string name)
    {
        var nodes = root.Elements(name).ToArray();
        if (nodes.Length > 1 || nodes.Any(value => value.HasElements || !string.IsNullOrWhiteSpace(value.Value)))
            throw new InvalidDataException("Owned creation container is already populated.");
    }
    private static string? ReadDirect(XElement root, string name)
    {
        var nodes = root.Elements(name).ToArray();
        if (nodes.Length > 1) throw new InvalidDataException("Ambiguous character field.");
        return nodes.FirstOrDefault()?.Value;
    }
    private static void SetDirect(XElement root, string name, string value)
    {
        _ = ReadDirect(root, name);
        root.SetElementValue(name, value);
    }
    private static void ReplaceDirect(XElement root, XElement value)
    {
        _ = ReadDirect(root, value.Name.LocalName);
        if (root.Element(value.Name) is { } existing) existing.ReplaceWith(value);
        else root.Add(value);
    }
    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
    private static T Detach<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))
        ?? throw new InvalidDataException("Unable to detach completion observation.");
    private static CharacterCreationFoundationResult<EvaluatedProjection> Refused(
        string blocker, string outcome = CharacterCreationFoundationOutcomes.Blocked) => new(outcome, null, [blocker]);
    private static CharacterCreationFoundationResult<CharacterCreationCompletionFinalizationProjection> RefusedProjection(
        string blocker) => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
