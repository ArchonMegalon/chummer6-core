using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using static Chummer.Application.Characters.CharacterCareerReputationProjector;

namespace Chummer.Application.Characters;

/// <summary>
/// In-process reputation computation over a validated original and compiled selection.
/// Retained inputs are re-admitted on every evaluation, including cold use. Neither
/// this object nor its calculations authorize a save or discharge CharacterMechanics,
/// arbitrary prerequisites, source/amendment coverage, errata or campaign policy.
/// </summary>
internal sealed class CharacterCreationReputationEffectsExecution
{
    private readonly CharacterCreationOriginalCharacterSource original;
    private readonly string requestDigest;
    private readonly CharacterCareerReputationSettings settings;
    private readonly XElement candidate;
    private readonly CharacterCreationFinalizationDelta[] deltas;
    private readonly XElement finalImprovements;
    private readonly AmmunitionIdentity[] ammunitionIdentities;

    private CharacterCreationReputationEffectsExecution(CharacterCreationOriginalCharacterSource original,
        CharacterCreationCompletionReviewRequest request, CharacterCareerReputationSettings settings,
        XElement candidate, IReadOnlyList<CharacterCreationFinalizationDelta> deltas,
        XElement finalImprovements, GraphEvaluation creation, string bindingDigest,
        AmmunitionIdentity[] ammunitionIdentities)
    {
        this.original = original;
        requestDigest = CharacterCreationFinalizationDigest.Compute(request);
        this.settings = settings;
        this.candidate = new(candidate);
        this.deltas = DetachDeltas(deltas);
        this.finalImprovements = new(finalImprovements);
        this.ammunitionIdentities = ammunitionIdentities;
        Creation = creation;
        BindingDigest = bindingDigest;
    }

    internal GraphEvaluation Creation { get; }
    internal string BindingDigest { get; }
    internal XElement CopyCompiledGraph() => new(candidate);
    internal IReadOnlyList<CharacterCreationFinalizationDelta> CopyDeltas() => DetachDeltas(deltas);

    internal static bool TryEvaluate(WorkspaceStoredDocument observation,
        CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationCompletionReviewRequest request,
        CharacterCreationRuleSourceCapture capture, ICharacterFileQueries queries,
        CharacterCreationOriginalCharacterSource original,
        CharacterCreationCompleteAssessment authority, CharacterCreationSelectedEffectsConsumer effects,
        out CharacterCreationReputationEffectsExecution? execution, out string error,
        CancellationToken cancellationToken = default, CharacterCreationCampaignExecutionContext? campaign = null)
    {
        execution = null;
        error = "creation_reputation_inputs_unavailable";
        cancellationToken.ThrowIfCancellationRequested();
        if (observation is null || bootstrap is null || request is null || capture is null || queries is null
            || original is null || authority is null || effects is null) return false;
        try
        {
            var suppliedRequest = request;
            request = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(JsonSerializer.Serialize(request))!;
            if (!bootstrap.IsValid() || !original.MatchesObservation(observation, bootstrap)
                || !original.MatchesInputs(request.InputsSnapshotDigest)) return false;
            // Reuse the existing source/original admission helper. A matching public
            // capture digest alone cannot replace this reconstruction and comparison.
            var admitted = new CharacterCreationRuleSourceExecutionContext(capture, queries)
                .Resolve(observation, request, cancellationToken);
            if (admitted is null || !original.MatchesInputs(admitted.Inputs.SnapshotDigest)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(authority,
                    CharacterCreationCompleteAssessmentService.FromFullAuthority(admitted.Inputs, request, admitted))
                || effects.DigestFor(authority) is not { } effectsDigest) return false;

            // Settings must be explicitly resolved from the admitted retained capture,
            // using the original XML. No default false and no ambient source fallback.
            error = "creation_reputation_settings_unavailable";
            var resolver = capture.CreateReplayResolver();
            if (resolver is null) return false;
            using var scope = (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var context = (scope ?? resolver).TryCreateContext(observation.Document.Content);
            if (context is null || !context.TryResolveCareerReputationSettings(out var settings, out string ruleState)
                || settings is null || string.IsNullOrWhiteSpace(ruleState)) return false;

            var candidate = effects.CopyCompiledGraph();
            var deltas = DetachDeltas(effects.CopyDeltas());
            error = "creation_reputation_graph_mismatch";
            // CopyDeltas detaches the array in the existing consumer, but nested
            // anchor lists can still be mutable. Verify its actual compiled payload,
            // not just the cached digest, before binding this execution to it.
            if (effectsDigest != CharacterCreationFinalizationDigest.Compute(new
                {
                    Authority = CharacterCreationFinalizationDigest.Compute(authority),
                    Operation = CharacterCreationConsumerPolicyOperation.SelectedEffects,
                    Graph = candidate.ToString(SaveOptions.DisableFormatting), Deltas = deltas
                })) return false;
            if (!TryEvaluateGraph(candidate, settings, CharacterReputationPhase.Creation,
                out var creation, out error)) return false;
            var originalGraph = original.CopyDocument().Root!;
            var preserved = originalGraph.Element("improvements")?.Elements().ToArray() ?? [];
            var combined = candidate.Element("improvements")!.Elements().ToArray();
            if (combined.Length < preserved.Length
                || !preserved.Select((row, index) => XNode.DeepEquals(row, combined[index])).All(same => same))
            {
                error = "creation_reputation_graph_mismatch";
                return false;
            }
            // Review evaluates creation only. Its admitted selections and finalization
            // seed determine native provenance; caller-supplied final IDs never do.
            var ruleSources = new CharacterCreationRuleSourceExecutionContext(capture, queries);
            var reviewed = campaign is { IsCommittedReplay: true }
                ? CharacterCreationCompletionReviewService.ReplayCommittedCampaign(
                    admitted.Inputs, request, observation, ruleSources, campaign, cancellationToken)
                : CharacterCreationCompletionReviewService.Review(admitted.Inputs, request,
                    cancellationToken, ruleSources, campaign);
            if (reviewed.Outcome != CharacterCreationFoundationOutcomes.Success || reviewed.Value is not { } review
                || !TryBuildFinalImprovements(admitted.Inputs, review, original, out var finalImprovements, cancellationToken)
                || !TryBindAmmunition(review, request, authority, candidate,
                    out var ammunitionIdentities, cancellationToken)
                || deltas.Count(row => row.Kind == "ammunition-assignment") != ammunitionIdentities.Length) return false;
            string binding = CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "creation-reputation-effects/v1",
                Observation = new { observation.Id, observation.ContentRevision, observation.SavedRevision,
                    RawCharacterXmlDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                        observation.Document.Content), observation.Document.AuxiliaryStateDigest },
                Bootstrap = bootstrap.SnapshotDigest, admitted.Inputs.SnapshotDigest,
                Request = CharacterCreationFinalizationDigest.Compute(request),
                Capture = capture.IntegrityDigest, CapturePolicy = capture.EffectivePolicyDigest,
                Settings = settings, RuleState = ruleState, Effects = effectsDigest,
                Graph = candidate.ToString(SaveOptions.DisableFormatting), Deltas = deltas,
                FinalImprovements = finalImprovements!.ToString(SaveOptions.DisableFormatting),
                Calculation = creation
            });
            error = "creation_reputation_settings_changed";
            if (!context.TryResolveCareerReputationSettings(out var finalSettings, out string finalRuleState)
                || settings != finalSettings || !string.Equals(ruleState, finalRuleState, StringComparison.Ordinal)
                || request.RuleSourceCaptureDigest != capture.IntegrityDigest
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(request, suppliedRequest)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            execution = new(original, request, settings, candidate, deltas, finalImprovements!, creation!, binding,
                ammunitionIdentities);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsUnresolved(exception))
        {
            execution = null;
            error = "creation_reputation_inputs_unresolved";
            return false;
        }
    }

    /// <summary>
    /// Handoff for ProjectEvaluated after its actual Finish lifecycle projection.
    /// The caller supplies that graph and its complete ordered deltas, never a DTO
    /// calculation or a fabricated career flag. This validates the reputation subject,
    /// not the other finalization domains. Original rows must retain their exact values;
    /// generated rows must match the canonical final compiler's native ID seed.
    /// All final XML and deltas are privately bound, including those source names.
    /// </summary>
    internal bool TryEvaluateFinalGraph(WorkspaceStoredDocument observation,
        CharacterCreationCompletionBootstrapSnapshot bootstrap, CharacterCreationCompletionReviewRequest request,
        XElement actualFinalGraph, IReadOnlyList<CharacterCreationFinalizationDelta> actualFinalDeltas,
        out FinishEvaluation? finish, out string error, CancellationToken cancellationToken = default)
    {
        finish = null;
        error = "creation_reputation_final_graph_mismatch";
        cancellationToken.ThrowIfCancellationRequested();
        if (observation is null || bootstrap is null || request is null || actualFinalGraph is null
            || actualFinalDeltas is null) return false;
        try
        {
            if (!bootstrap.IsValid() || !original.MatchesObservation(observation, bootstrap)
                || requestDigest != CharacterCreationFinalizationDigest.Compute(request)) return false;
            var graph = new XElement(actualFinalGraph);
            var finalDeltas = DetachDeltas(actualFinalDeltas);
            if (graph.Elements(CharacterCreationBootstrapXml.MarkerElement).Any()
                || !SameReputationSubject(graph) || !ContainsCompiledDeltas(graph, finalDeltas)) return false;
            if (!TryEvaluateGraph(graph, settings, CharacterReputationPhase.Career, out var calculation, out error))
                return false;
            cancellationToken.ThrowIfCancellationRequested();
            finish = new FinishEvaluation(calculation!, graph, finalDeltas,
                CharacterCreationFinalizationDigest.Compute(new
                {
                    Creation = BindingDigest, Graph = graph.ToString(SaveOptions.DisableFormatting),
                    Deltas = finalDeltas, Calculation = calculation
                }));
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsUnresolved(exception))
        {
            finish = null;
            error = "creation_reputation_final_graph_unresolved";
            return false;
        }
    }

    private bool SameReputationSubject(XElement final)
    {
        // Compare inputs, including inactive/unknown effects, not equal aggregate totals.
        foreach (string name in new[] { "streetcred", "notoriety", "publicawareness", "burntstreetcred", "expenses" })
            if (!SameProjectedNode(candidate.Element(name), final.Element(name))) return false;
        return SameProjectedNode(finalImprovements, final.Element("improvements"));
    }

    private static bool TryBuildFinalImprovements(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReview review, CharacterCreationOriginalCharacterSource original,
        out XElement? improvements, CancellationToken cancellationToken)
    {
        improvements = null;
        if (review.Skills is null || review.UpstreamBudget is null || review.Lifestyles is null
            || review.Contacts is null || inputs.Skills.PrerequisiteDraft.HeritageSelection is not { } heritage)
            return false;
        var qualities = review.UpstreamBudget.Qualities.Qualities;
        var lifestyles = review.Lifestyles;
        var graph = new CharacterCreationCompletionFinalizationProjector.SelectedGraphInputs(
            inputs.Skills.PrerequisiteDraft, review.Skills,
            inputs.Skills.SelectedActiveSkillPoints, inputs.Skills.SelectedSkillGroupPoints,
            new(qualities.Selections, qualities.KarmaRemaining, qualities.SourceAnchorIds),
            new(lifestyles.Gear.Lines, lifestyles.Gear.Budget, lifestyles.Gear.SourceAnchorIds),
            new(lifestyles.Authority, lifestyles.Lifestyles, lifestyles.StartingLifestyleId, lifestyles.SourceAnchorIds),
            new(inputs.Contacts.Policy, review.Request.Selections.Contacts,
                inputs.Contacts.Qualities.Authority.CostPolicy ?? CharacterCreationQualityCostPolicy.Default,
                inputs.Contacts.Qualities.Authority.QualityKarmaLimit,
                inputs.Contacts.Qualities.Authority.MayExceedPositiveQualityLimit, review.Contacts.GroupKarma),
            new CharacterCreationCompletionFinalizationProjector.FullAwakenedGraphInputs(inputs.Magic, review.UpstreamBudget.Magic),
            review.ReviewDigest, review.ReviewDigest)
        {
            Loadout = review.Loadout,
            AmmunitionAssignments = review.Request.Selections.AmmunitionAssignments,
            OriginalLoadoutInstances = inputs.Loadout?.PendingDraft?.Instances
        };
        // Reuse the shared source compilers for every generated effect, including
        // racial and awakened effects. Original improvements remain whole nodes.
        // Only the improvement graph is evidence here, never aggregate mechanics.
        var document = original.CopyDocument();
        var root = document.Root!;
        _ = CharacterCreationCareerBaseline.InitializeMissing(root);
        ValidateSavedInputShape(root);
        root.SetElementValue("metatype", heritage.MetatypeName);
        root.SetElementValue("metavariant", heritage.MetavariantName ?? string.Empty);
        root.SetElementValue("buildmethod", graph.Prerequisite.BuildMethod);
        int order = 0;
        if (CharacterCreationCompletionFinalizationProjector.CompileSelectedGraph(document, graph, [], ref order,
                cancellationToken) is not null || root.Element("improvements") is not { } generated) return false;
        improvements = new(generated);
        return true;
    }

    private bool ContainsCompiledDeltas(XElement finalGraph, CharacterCreationFinalizationDelta[] finalDeltas)
    {
        if (finalDeltas.Length < deltas.Length
            || finalDeltas.Zip(finalDeltas.Skip(1)).Any(pair => pair.First.Order >= pair.Second.Order)
            || finalDeltas.Count(row => row.Kind == "ammunition-assignment") != ammunitionIdentities.Length
            || !MatchesAmmunitionGraph(finalGraph, ammunitionIdentities, finalized: true)) return false;
        // Finish prepends build deltas and appends carryover/cash deltas. Its actual
        // compiled selection must still appear in order. Native instance IDs and
        // ordinal offsets differ. Only a canonically projected ammunition identity
        // may translate an AfterValue; all other values, anchors and targets stay exact.
        int position = 0;
        foreach (var expected in deltas)
        {
            while (position < finalDeltas.Length
                && !SameCompiledDelta(expected, finalDeltas[position], finalGraph)) position++;
            if (position == finalDeltas.Length) return false;
            position++;
        }
        return true;
    }

    private bool SameCompiledDelta(CharacterCreationFinalizationDelta expected,
        CharacterCreationFinalizationDelta actual, XElement finalGraph)
    {
        if (expected.Kind == "ammunition-assignment")
        {
            var identity = ammunitionIdentities.SingleOrDefault(row => row.OptionId == expected.TargetId);
            if (identity is null || expected.AfterValue != identity.CompilerWeaponId
                || actual.AfterValue != identity.FinalWeaponId) return false;
            // Translate only this verified association. Every other delta field,
            // including BeforeValue, source anchors and caliber, remains exact.
            return CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                expected with { Order = 0 }, actual with { Order = 0, AfterValue = identity.CompilerWeaponId });
        }
        if (expected.DeltaId != actual.DeltaId)
        {
            int separator = expected.DeltaId.LastIndexOf(':');
            if (separator < 0 || actual.DeltaId.LastIndexOf(':') != separator
                || expected.DeltaId[..separator] != actual.DeltaId[..separator]
                || !Guid.TryParse(expected.DeltaId[(separator + 1)..], out var before)
                || !Guid.TryParse(actual.DeltaId[(separator + 1)..], out var after)) return false;
            // Generated quality/gear deltas include the compiler's native ID. Admit
            // its reseeding only when the actual source instance otherwise matches.
            XElement? Instance(XElement root, Guid id) => root.Elements()
                .Where(container => container.Name == "qualities" || container.Name == "gears")
                .Elements().Where(row => Guid.TryParse(row.Element("guid")?.Value, out var native) && native == id)
                .SingleOrDefault();
            var left = Instance(candidate, before);
            var right = Instance(finalGraph, after);
            if (left is null || right is null || left.Element("sourceid")?.Value != expected.TargetId
                || right.Element("sourceid")?.Value != actual.TargetId) return false;
            left = new XElement(left); right = new XElement(right);
            left.Element("guid")!.Value = string.Empty; right.Element("guid")!.Value = string.Empty;
            if (!SameProjectedNode(left, right)) return false;
        }
        return CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            expected with { Order = 0 }, actual with { Order = 0, DeltaId = expected.DeltaId });
    }

    private sealed record AmmunitionIdentity(string OptionId, int GraphOrder, string GearSourceId, string WeaponSourceId,
        string CompilerWeaponId, string FinalWeaponId, XElement CompilerGear, XElement FinalGear,
        XElement[] CompilerWeapons, XElement[] FinalWeapons);

    private static bool TryBindAmmunition(CharacterCreationCompletionReview review,
        CharacterCreationCompletionReviewRequest request, CharacterCreationCompleteAssessment authority,
        XElement compiled,
        out AmmunitionIdentity[] identities, CancellationToken cancellationToken)
    {
        identities = [];
        if (request.Selections.AmmunitionAssignments is not { Count: > 0 } assignments)
            return true;

        // Reuse the admitted review and canonical native projectors, including
        // retained instance IDs. No ID is inferred from a name, total, or GUID shape.
        cancellationToken.ThrowIfCancellationRequested();
        if (review is not { Loadout: { } loadout, Lifestyles: { } lifestyles })
            return false;
        var gear = lifestyles.Gear.Lines;
        string compilerSeed = CharacterCreationFinalizationDigest.Compute(new
        { authority.Schema, authority.InputsSnapshotDigest, authority.SelectionsDigest });
        XElement Build(string seed)
        {
            if (!CharacterCreationLoadoutSourceProjector.TryBuildGraph(loadout.Lines, seed,
                    out var weapons, out _, loadout.RetainedInstances))
                throw new InvalidDataException("Unprojectable ammunition weapon identity.");
            int order = 0;
            var graph = new XElement("character", new XElement("weapons", weapons),
                CharacterCreationFinalizationProjector.BuildGearGraph(gear, seed, [], ref order));
            if (!CharacterCreationAmmunitionCompletionConsumer.TryProjectAssignments(graph,
                    assignments, gear, loadout.Lines, seed))
                throw new InvalidDataException("Unprojectable ammunition assignment.");
            return graph;
        }
        var compilerGraph = Build(compilerSeed);
        var finalGraph = Build(review.ReviewDigest);
        var assignmentOrder = compilerGraph.Descendants("ammunitionassignment").ToArray();
        identities = assignments.Select(assignment =>
        {
            string gearSource = gear.Single(row => row.OptionId == assignment.GearOptionId).SourceId.ToString("D");
            string weaponSource = loadout.Lines.Single(row => row.OptionId == assignment.Weapon.OptionId).SourceId.ToString("D");
            var compilerGear = SourceNodes(compilerGraph, "gears", "gear", gearSource).Single();
            var finalGear = SourceNodes(finalGraph, "gears", "gear", gearSource).Single();
            return new AmmunitionIdentity(assignment.GearOptionId,
                Array.IndexOf(assignmentOrder, compilerGear.Element("ammunitionassignment")!), gearSource, weaponSource,
                compilerGear.Element("ammunitionassignment")!.Element("weaponinstanceid")!.Value,
                finalGear.Element("ammunitionassignment")!.Element("weaponinstanceid")!.Value,
                new XElement(compilerGear), new XElement(finalGear),
                SourceNodes(compilerGraph, "weapons", "weapon", weaponSource),
                SourceNodes(finalGraph, "weapons", "weapon", weaponSource));
        }).ToArray();
        return MatchesAmmunitionGraph(compiled, identities, finalized: false);
    }

    private static XElement[] SourceNodes(XElement graph, string container, string kind, string sourceId)
        => graph.Elements(container).SingleOrDefault()?.Elements(kind)
            .Where(row => row.Element("sourceid")?.Value == sourceId).ToArray() ?? [];

    private static bool MatchesAmmunitionGraph(XElement graph, AmmunitionIdentity[] identities, bool finalized)
    {
        if (identities.Length == 0) return true;
        if (!graph.Descendants("ammunitionassignment").Select(row => row.Element("gearoptionid")?.Value)
                .SequenceEqual(identities.OrderBy(row => row.GraphOrder).Select(row => row.OptionId), StringComparer.Ordinal))
            return false;
        foreach (var identity in identities)
        {
            var gear = SourceNodes(graph, "gears", "gear", identity.GearSourceId);
            var weapons = SourceNodes(graph, "weapons", "weapon", identity.WeaponSourceId);
            var expectedWeapons = finalized ? identity.FinalWeapons : identity.CompilerWeapons;
            if (gear.Length != 1 || !SameProjectedNode(gear[0], finalized ? identity.FinalGear : identity.CompilerGear)
                || weapons.Length != expectedWeapons.Length
                || !weapons.Zip(expectedWeapons).All(pair => SameProjectedNode(pair.First, pair.Second))) return false;
        }
        return true;
    }

    private static bool SameProjectedNode(XElement? left, XElement? right)
    {
        // ProjectEvaluated parses with LoadOptions.None. Ignore only indentation
        // between elements, never scalar whitespace (names/unique/conditions) or CDATA.
        static XElement? WithoutIndentation(XElement? node)
        {
            if (node is null) return null;
            var copy = new XElement(node);
            foreach (var text in copy.DescendantNodes().OfType<XText>().Where(text => text is not XCData
                && text.Parent is { HasElements: true } && string.IsNullOrWhiteSpace(text.Value)
                && text.Ancestors().Select(parent => parent.Attribute(XNamespace.Xml + "space")?.Value)
                    .FirstOrDefault(value => value is not null) != "preserve").ToArray()) text.Remove();
            return copy;
        }
        return XNode.DeepEquals(WithoutIndentation(left), WithoutIndentation(right));
    }

    internal sealed class FinishEvaluation
    {
        private readonly XElement graph;
        private readonly CharacterCreationFinalizationDelta[] deltas;

        internal FinishEvaluation(GraphEvaluation calculation, XElement graph,
            CharacterCreationFinalizationDelta[] deltas, string bindingDigest)
        {
            Calculation = calculation;
            this.graph = new(graph);
            this.deltas = DetachDeltas(deltas);
            BindingDigest = bindingDigest;
        }

        internal GraphEvaluation Calculation { get; }
        internal string BindingDigest { get; }
        internal XElement CopyGraph() => new(graph);
        internal IReadOnlyList<CharacterCreationFinalizationDelta> CopyDeltas() => DetachDeltas(deltas);
        public override string ToString() => nameof(FinishEvaluation);
    }

    private static CharacterCreationFinalizationDelta[] DetachDeltas(IReadOnlyList<CharacterCreationFinalizationDelta> values)
        => JsonSerializer.Deserialize<CharacterCreationFinalizationDelta[]>(JsonSerializer.Serialize(values))!;

    private static bool IsUnresolved(Exception exception)
        => exception is InvalidDataException or XmlException or IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or FormatException or OverflowException or JsonException;

    public override string ToString() => nameof(CharacterCreationReputationEffectsExecution);
}
