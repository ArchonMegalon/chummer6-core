using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Executes the ordinary purchased/racial/talent compilers on a disposable graph,
/// then checks every admitted racial/talent source against its final state. No
/// saved draft, prices, or policy authority is invented. The temporary compiler
/// seed never replaces the admitted review seed used for native instance IDs.
/// Full review and selected replay supply their independently admitted inputs.
/// </summary>
internal static class CharacterCreationSelectedPrerequisiteConsumer
{
    internal const string Blocker = "creation-selected-prerequisites-not-projectable";
    private static readonly string[] Flags = ["magenabled", "resenabled", "depenabled"];

    // Assessment accepts this execution, never a deserialized diagnostic DTO.
    internal sealed class Execution(CharacterCreationSelectedPrerequisiteEvaluation evaluation)
    {
        internal CharacterCreationSelectedPrerequisiteEvaluation Evaluation { get; } = evaluation;
        private readonly string? authorityDigest;
        private readonly string? compiledGrantDigest;

        // This is called only after all canonical compilers and final predicates
        // succeed. Nothing in the serialized evidence can reconstruct this trace.
        internal Execution(CharacterCreationSelectedPrerequisiteEvaluation evaluation,
            CharacterCreationCompleteAssessment? authority, XElement root,
            IReadOnlyList<CharacterCreationFinalizationDelta> deltas) : this(evaluation)
        {
            if (authority is not { Schema: CharacterCreationCompleteAssessment.SchemaV4,
                    Phase: CharacterCreationCompletePhase.Authority }
                || authority.InputsSnapshotDigest != Evaluation.InputsSnapshotDigest
                || authority.SelectionsDigest != Evaluation.SelectionsDigest) return;
            authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
            compiledGrantDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                Authority = authorityDigest, Evaluation,
                Graph = root.ToString(SaveOptions.DisableFormatting), Deltas = deltas
            });
        }

        internal string? CompiledGrantDigestFor(CharacterCreationCompleteAssessment authority)
            => authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
                && authorityDigest == CharacterCreationFinalizationDigest.Compute(authority)
                && CharacterCreationFinalizationDigest.IsCanonical(compiledGrantDigest)
                    ? compiledGrantDigest : null;
    }
    private delegate bool ApplyTalent(XElement root, List<CharacterCreationFinalizationDelta> deltas, ref int order);

    internal static bool MatchesEvidence(Execution execution, CharacterCreationSelectedPrerequisiteEvaluation? evidence)
        => evidence is { Schema: CharacterCreationSelectedPrerequisiteEvaluation.SchemaV1, Checks: not null, EnabledFlags: not null }
            && evidence.Checks.Count == execution.Evaluation.Checks.Count && evidence.EnabledFlags.Count <= Flags.Length
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(execution.Evaluation, evidence);

    internal static Execution? EvaluateFull(CharacterCreationMagicResonanceCompletionInputs inputs,
        CharacterCreationMagicResonanceCompletionProjection magic, CharacterCreationAttributeSkillsPreview skills,
        CharacterCreationQualitiesCompletionEvaluation qualities, string snapshot, string selectionsDigest,
        CharacterCreationCompleteAssessment? completeAuthority = null)
        => Evaluate(inputs.PrerequisiteDraft, inputs.SelectedTalent.GrantedQualitySources ?? [],
            inputs.Attributes.Attributes, skills, qualities, snapshot, selectionsDigest, completeAuthority,
            (XElement root, List<CharacterCreationFinalizationDelta> deltas, ref int order) =>
                CharacterCreationAwakenedLegacyProjector.TryApply(root, inputs.PrerequisiteDraft,
                    skills.Attributes.Attributes, skills.Skills, skills.SkillGroups, inputs, magic, snapshot, deltas, ref order));

    internal static Execution? EvaluateSelected(CharacterCreationPrerequisiteDraft prerequisite,
        IReadOnlyList<CharacterCreationAttributeProjection> originalAttributes,
        CharacterCreationSelectedMagicAuthority magic, CharacterCreationAttributeSkillsPreview skills,
        CharacterCreationQualitiesCompletionEvaluation qualities, string snapshot, string selectionsDigest,
        CharacterCreationCompleteAssessment? completeAuthority = null)
        => Evaluate(prerequisite, magic.Talent.GrantedQualitySources ?? [], originalAttributes, skills, qualities,
            snapshot, selectionsDigest, completeAuthority,
            (XElement root, List<CharacterCreationFinalizationDelta> deltas, ref int order) =>
                CharacterCreationAwakenedLegacyProjector.TryApplySelected(root, prerequisite, originalAttributes,
                    skills.Attributes.Attributes, skills.Skills, skills.SkillGroups, magic, snapshot, deltas, ref order));

    private static Execution? Evaluate(CharacterCreationPrerequisiteDraft prerequisite,
        IReadOnlyList<CharacterCreationTalentQualitySource> talentSources,
        IReadOnlyList<CharacterCreationAttributeProjection> originalAttributes,
        CharacterCreationAttributeSkillsPreview skills, CharacterCreationQualitiesCompletionEvaluation qualities,
        string snapshot, string selectionsDigest, CharacterCreationCompleteAssessment? completeAuthority, ApplyTalent apply)
    {
        try
        {
            if (!CharacterCreationFinalizationDigest.IsCanonical(snapshot)
                || !CharacterCreationFinalizationDigest.IsCanonical(selectionsDigest)
                || prerequisite.HeritageSelection is not { } heritage) return null;
            var root = new XElement("character", new XElement("qualities"), new XElement("gears"),
                new XElement("metatype", heritage.MetatypeName));
            foreach (string flag in Flags) root.Add(new XElement(flag, "False"));
            var deltas = new List<CharacterCreationFinalizationDelta>();
            int order = 0;
            CharacterCreationFinalizationProjector.BuildQualityAndEffectGraph(root, qualities.Selections,
                checked((int)skills.Attributes.CreationKarmaBudget.Total),
                checked((int)skills.Attributes.CreationKarmaBudget.Used), qualities.KarmaRemaining,
                qualities.SourceAnchorIds, snapshot, deltas, ref order);
            if (!CharacterCreationPriorityRacialQualities.TryApply(root, heritage, prerequisite.DraftDigest, deltas, ref order)
                || !apply(root, deltas, ref order)
                || !TryCheckFinalGraph(root, prerequisite, talentSources, skills.Attributes.Attributes, out var checks)) return null;
            var execution = new Execution(new(CharacterCreationSelectedPrerequisiteEvaluation.SchemaV1, snapshot, selectionsDigest,
                CharacterCreationFinalizationDigest.Compute(originalAttributes),
                CharacterCreationFinalizationDigest.Compute(skills.Attributes.Attributes),
                CharacterCreationFinalizationDigest.Compute(qualities.Selections),
                // Retain only predicate inputs, independent of temporary GUIDs.
                CharacterCreationFinalizationDigest.Compute(root.Element("qualities")!.Elements("quality").Select(row => new
                {
                    SourceId = row.Element("sourceid")!.Value,
                    Name = row.Element("name")!.Value,
                    Extra = row.Element("extra")?.Value
                }).ToArray()),
                Flags.Where(flag => bool.Parse(root.Element(flag)!.Value)).ToArray(), checks),
                completeAuthority, root, deltas);
            return execution;
        }
        catch (Exception error) when (IsMalformed(error)) { return null; }
    }

    /// <summary>Also used after the actual finalization graph has materialized talent grants and flags.</summary>
    internal static bool TryCheckFinalGraph(XElement root, CharacterCreationPrerequisiteDraft prerequisite,
        IReadOnlyList<CharacterCreationTalentQualitySource> talentSources,
        IReadOnlyList<CharacterCreationAttributeProjection> attributes,
        out CharacterCreationSelectedPrerequisiteCheck[] checks)
    {
        checks = [];
        try
        {
            if (prerequisite.HeritageSelection is null || prerequisite.TalentSelection is null
                || talentSources is null || talentSources.Any(source => source is null)
                || attributes is null || attributes.Any(attribute => attribute is null)
                || !talentSources.Select(source => source.Reference)
                    .SequenceEqual(prerequisite.TalentSelection.GrantedQualities, StringComparer.Ordinal)) return false;
            var flags = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (flag, attribute) in new[] { ("magenabled", "MAG"), ("resenabled", "RES"), ("depenabled", "DEP") })
            {
                var nodes = root.Elements(flag).ToArray();
                var rows = attributes.Where(row => row.AttributeId == attribute).ToArray();
                if (nodes.Length != 1 || nodes[0].HasAttributes || nodes[0].HasElements
                    || !bool.TryParse(nodes[0].Value, out bool enabled)
                    || rows.Length != 1 || rows[0].IsEnabled != enabled) return false;
                if (enabled) flags.Add(flag);
            }
            var sources = (prerequisite.HeritageSelection?.RacialQualitySources ?? [])
                .Select(source => (Origin: "racial", Source: source))
                .Concat(talentSources.Select(source => (Origin: "talent", Source: source))).ToArray();
            if (sources.Length > CharacterCreationSelectedPrerequisiteEvaluation.MaximumChecks
                || sources.Any(row => !CharacterCreationTalentQualitySourceRules.IsValidSource(row.Source))
                || sources.Select(row => (row.Origin, row.Source.SourceId)).Distinct().Count() != sources.Length
                || root.Elements("qualities").Count() != 1) return false;
            var result = new List<CharacterCreationSelectedPrerequisiteCheck>();
            foreach (var (origin, source) in sources)
            {
                var saved = root.Element("qualities")!.Elements("quality")
                    .Where(row => row.Element("sourceid")?.Value == source.SourceId).ToArray();
                if (saved.Length != 1 || saved[0].Element("name")?.Value != source.Name) return false;
                CharacterCreationAwakenedLegacyProjector.CheckRestrictions(
                    XElement.Parse(source.CanonicalSourceXml), root, [], flags);
                result.Add(new(origin, source.SourceId, CharacterCreationFinalizationDigest.Compute(source)));
            }
            checks = result.ToArray();
            return true;
        }
        catch (Exception error) when (IsMalformed(error)) { return false; }
    }

    private static bool IsMalformed(Exception error) => error is XmlException or ArgumentException
        or InvalidDataException or InvalidOperationException or OverflowException or FormatException;
}
