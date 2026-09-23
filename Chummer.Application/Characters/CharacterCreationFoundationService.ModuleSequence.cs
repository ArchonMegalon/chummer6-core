using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationFoundationService
{
    private static CharacterCreationLifeModuleSequenceCompilation CompileModuleSequence(
        string rulesetId,
        IReadOnlyList<LifeModuleLegalOptionDto> capturedModules,
        CharacterCreationFoundationDraftLedger draft,
        CharacterCreationFoundationEffectCompilation nationality,
        CharacterCreationFoundationSkillSourceAuthority? skills,
        CharacterCreationFoundationQualitySourceAuthority? qualities,
        CharacterCreationFoundationQualityLevelSourceAuthority? levels,
        string sourceContextDigest,
        IReadOnlyDictionary<string, string>? qualityInstanceValues)
    {
        var occurrences = new List<CharacterCreationLifeModuleOccurrenceCompilation>();
        var blockers = new List<string>();
        AddOccurrence(LifeModuleJourneyStageOrders.Nationality, draft.Selection,
            draft.FollowUpValues, draft.SourceAnchorIds, nationality);

        var additional = draft.AdditionalModules ?? [];
        for (int index = 0; index < additional.Count; index++)
        {
            CharacterCreationLifeModuleDraftEntry entry = additional[index];
            int stage = Math.Min(LifeModuleJourneyStageOrders.FormativeYears + index,
                LifeModuleJourneyStageOrders.RealLife);
            var entryBlockers = new List<string>();
            var projected = ProjectModuleEntry(capturedModules, entry.Selection,
                entry.FollowUpValues, draft.RequestedMetatype, stage, entryBlockers);
            if (entryBlockers.Count != 0 || projected is null
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(projected, entry))
            {
                // Never classify saved, rehashed client projections as source truth.
                blockers.AddRange(entryBlockers);
                blockers.Add(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
                continue;
            }

            LifeModuleLegalOptionDto module = capturedModules.Single(option =>
                option.ModuleId == entry.Selection.ModuleId);
            var version = ResolveVersion(module, entry.Selection.VersionId, entryBlockers);
            blockers.AddRange(entryBlockers);
            var compilation = CharacterCreationFoundationEffectCompiler.Compile(rulesetId, draft,
                module, version, skills, qualities, sourceContextDigest, levels, projected);
            AddOccurrence(stage, entry.Selection, projected.FollowUpValues,
                projected.SourceAnchorIds, compilation, index + 2);
        }

        if (!draft.ModuleSelectionFinished || !LifeModuleJourneyStageOrders.Required.All(stage =>
                occurrences.Any(occurrence => occurrence.StageOrder == stage)))
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationRequiredStagesIncomplete);

        // A complete compilation is not yet the atomic full-graph writer. Do not
        // grant mutation authority merely because each module was inspected.
        blockers.Add(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
        CharacterCreationLifeModuleQualityLevelResolution[] resolutions = ResolveSequenceQualityLevels(
            occurrences, qualities, levels, blockers);
        resolutions = CharacterCreationFoundationQualityInstanceResolver.Resolve(
            resolutions, occurrences, qualities, qualityInstanceValues, blockers, out var dependentInstances);
        var result = new CharacterCreationLifeModuleSequenceCompilation(
            "chummer.character_creation_life_module_sequence.v1", draft.WorkspaceId,
            draft.DraftRevision, draft.DraftDigest, draft.SourceDigest, sourceContextDigest,
            draft.ModuleSelectionFinished, occurrences.ToArray(), resolutions,
            blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), string.Empty)
        { DependentQualityInstances = dependentInstances.Length == 0 ? null : dependentInstances };
        return result with
        {
            CompilationDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(result)
        };

        void AddOccurrence(int stage, CharacterCreationFoundationSelection selection,
            IReadOnlyDictionary<string, string> answers, IReadOnlyList<string> anchors,
            CharacterCreationFoundationEffectCompilation compilation, int order = 1)
        {
            string identity = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
            {
                Semantics = "life-module-occurrence/v1", draft.WorkspaceId, Order = order,
                StageOrder = stage, Selection = selection
            });
            occurrences.Add(new(order, identity, stage, selection,
                answers.OrderBy(answer => answer.Key, StringComparer.Ordinal)
                    .ToDictionary(answer => answer.Key, answer => answer.Value, StringComparer.Ordinal),
                anchors.ToArray(), compilation));
            blockers.AddRange(compilation.Blockers.Where(blocker =>
                blocker != CharacterCreationFoundationBlockers.FinalizationRequiredStagesIncomplete));
        }
    }

    internal static CharacterCreationLifeModuleQualityLevelResolution[] ResolveSequenceQualityLevels(
        IReadOnlyList<CharacterCreationLifeModuleOccurrenceCompilation> occurrences,
        CharacterCreationFoundationQualitySourceAuthority? qualities,
        CharacterCreationFoundationQualityLevelSourceAuthority? levels,
        ICollection<string> blockers)
    {
        var contributions = occurrences.SelectMany(occurrence => occurrence.Compilation.Effects
            .Where(effect => effect.EffectKind == "qualitylevel")
            .Select(effect => (Occurrence: occurrence, Effect: effect))).ToArray();
        if (contributions.Length == 0) return [];
        if (qualities is null || levels is null)
        {
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
            return [];
        }

        var results = new List<CharacterCreationLifeModuleQualityLevelResolution>();
        foreach (var group in contributions.GroupBy(item =>
                     item.Effect.Parameters.GetValueOrDefault("@group") ?? string.Empty, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var values = new List<CharacterCreationLifeModuleQualityLevelContribution>();
            bool valid = true;
            foreach (var item in group)
            {
                if (item.Effect.CompilationStatus != CharacterCreationFoundationEffectCompilationStatuses.Supported
                    || !int.TryParse(item.Effect.TargetId, NumberStyles.None, CultureInfo.InvariantCulture, out int tier))
                {
                    valid = false;
                    break;
                }
                values.Add(new(item.Occurrence.OccurrenceId, item.Effect.EffectId,
                    item.Effect.InstructionDigest, tier));
            }
            if (!valid || !levels.TryResolveHighest(group.Key, values.Select(value => value.Level).ToArray(),
                    qualities, out int highest, out var target) || target is null)
            {
                blockers.Add(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                continue;
            }
            results.Add(new(group.Key, highest, target, levels.SourceDigest, values.ToArray(),
                group.SelectMany(item => item.Effect.SourceAnchorIds).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray()));
        }
        return results.ToArray();
    }
}
