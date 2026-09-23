using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

internal static partial class CharacterCreationFoundationLifeModuleQualityWritePlanner
{
    internal static CharacterCreationFoundationSequenceWritePlanResult BuildSequence(
        string rulesetId, string characterXml, CharacterCreationFoundationDraftLedger draft,
        CharacterCreationLifeModuleSequenceCompilation sequence, IReadOnlyList<LifeModuleLegalOptionDto> catalog,
        byte[]? sourceBytes, string catalogRawXmlDigest, CharacterCreationFoundationSkillSourceAuthority? skills,
        CharacterCreationFoundationQualitySourceAuthority? qualities,
        CharacterCreationFoundationQualityLevelSourceAuthority? levels, string defaultNotesColor = "Chocolate")
    {
        var blockers = new List<string>();
        try
        {
            if (rulesetId != RulesetDefaults.Sr5 || sourceBytes is null || skills is null || qualities is null || levels is null
                || "sha256:" + Convert.ToHexStringLower(SHA256.HashData(sourceBytes)) != catalogRawXmlDigest
                || draft.CharacterEffectsApplied || draft.CompilationStatus != CharacterCreationFoundationDraftStatuses.PendingFinalization
                || CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(draft) != draft.DraftDigest
                || CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(characterXml) != draft.BaseRawCharacterXmlDigest
                || sequence.WorkspaceId != draft.WorkspaceId || sequence.DraftRevision != draft.DraftRevision
                || sequence.DraftDigest != draft.DraftDigest || sequence.SourceDigest != draft.SourceDigest
                || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(sequence.SourceContextDigest)
                || CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(sequence with { CompilationDigest = string.Empty }) != sequence.CompilationDigest)
                return Failure(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
            if (!draft.ModuleSelectionFinished || !sequence.SelectionFinished
                || sequence.Occurrences.Count != 1 + (draft.AdditionalModules?.Count ?? 0)
                || !LifeModuleJourneyStageOrders.Required.All(stage => sequence.Occurrences.Any(row => row.StageOrder == stage)))
                return Failure(CharacterCreationFoundationBlockers.FinalizationRequiredStagesIncomplete);

            // The isolated compiler intentionally cannot authorize group resolution.
            // Recheck every effect below and account for every push; never simply
            // turn its composite "unsupported" status into an admission decision.
            blockers.AddRange(sequence.Blockers.Where(blocker => blocker is not
                (CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired
                 or CharacterCreationFoundationBlockers.FinalizationEffectUnsupported)));
            if (blockers.Count != 0) return new(null, blockers.Distinct(StringComparer.Ordinal).ToArray());
            using var stream = new MemoryStream(sourceBytes, writable: false);
            XElement source = XDocument.Load(stream).Root ?? throw new InvalidOperationException();
            XElement character = XDocument.Parse(characterXml).Root ?? throw new InvalidOperationException();
            if (source.Name != "chummer" || source.Elements("modules").Count() != 1
                || character.Name != "character" || character.Elements("qualities").Count() > 1
                || character.Elements("improvements").Count() > 1)
                return Failure(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);

            var expectedLevels = CharacterCreationFoundationService.ResolveSequenceQualityLevels(sequence.Occurrences, qualities, levels, blockers);
            var selections = sequence.QualityLevels.Where(level => level.InstancePrompt is not null && level.InstanceValue is not null)
                .Select(level => new KeyValuePair<string, string>(level.InstancePrompt!.PromptId, level.InstanceValue!))
                .Concat((sequence.DependentQualityInstances ?? []).Where(row => row.InstanceValue is not null)
                    .Select(row => new KeyValuePair<string, string>(row.InstancePrompt.PromptId, row.InstanceValue!)))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var resolved = CharacterCreationFoundationQualityInstanceResolver.Resolve(expectedLevels, sequence.Occurrences, qualities,
                selections, blockers, out var dependentInstances);
            if (blockers.Count != 0 || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(resolved, sequence.QualityLevels)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(dependentInstances, sequence.DependentQualityInstances ?? []))
                return Failure(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);

            var qualityXml = new List<string>();
            var improvements = new List<string>();
            var owners = new List<CharacterCreationFoundationSequenceModuleOwner>();
            var dispositions = new List<CharacterCreationFoundationSequencePushDisposition>();
            int dependentCount = 0;
            for (int index = 0; index < sequence.Occurrences.Count; index++)
            {
                var occurrence = sequence.Occurrences[index];
                var entry = index == 0 ? null : draft.AdditionalModules![index - 1];
                var selection = entry?.Selection ?? draft.Selection;
                int stage = Math.Min(index + 1, LifeModuleJourneyStageOrders.RealLife);
                string occurrenceId = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
                { Semantics = "life-module-occurrence/v1", draft.WorkspaceId, Order = index + 1, StageOrder = stage, Selection = selection });
                if (occurrence.Order != index + 1 || occurrence.OccurrenceId != occurrenceId
                    || occurrence.Selection != selection || occurrence.StageOrder != stage)
                    return Failure(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
                var module = catalog.Single(item => item.ModuleId == selection.ModuleId);
                var version = selection.VersionId is null ? null : module.Versions.Single(item => item.VersionId == selection.VersionId);
                if (version is null && module.Versions.Count != 0)
                    return Failure(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
                var compiled = CharacterCreationFoundationEffectCompiler.Compile(rulesetId, draft, module, version,
                    skills, qualities, sequence.SourceContextDigest, levels, entry);
                if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(compiled, occurrence.Compilation)
                    || compiled.Requirements.Any(item => item.CompilationStatus != CharacterCreationFoundationEffectCompilationStatuses.Supported)
                    || compiled.Blockers.Contains(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict)
                    || compiled.Effects.Any(effect => effect.PromptIds.Count != 0
                        || (effect.EffectKind is not ("pushtext" or "addqualities")
                            && effect.CompilationStatus != CharacterCreationFoundationEffectCompilationStatuses.Supported)))
                    return Failure(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);

                XElement effective = ResolveEffectiveModule(source, selection);
                XElement? admitted = ParseSource(effective.ToString(SaveOptions.DisableFormatting), blockers);
                var definition = admitted is null ? null : ReadDefinition(admitted, module, version, defaultNotesColor, blockers);
                var projected = entry?.ProjectedEffects ?? draft.ProjectedEffects;
                XElement[] rawEffects = effective.Element("bonus")!.Elements().ToArray();
                if (definition is null || blockers.Count != 0 || rawEffects.Length != projected.Count
                    || rawEffects.Where((effect, i) => !XNode.DeepEquals(effect, XElement.Parse(projected[i].RawXml))).Any())
                    return Failure(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);

                string ownerId = SequenceQualityId(new { draft.WorkspaceId, draft.DraftDigest, sequence.CompilationDigest, occurrence.OccurrenceId });
                var reserved = ResolveSequencePushes(occurrence, sequence.QualityLevels, dispositions);
                if (reserved is null) return Failure(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                var dependentSelections = dependentInstances.Where(row => row.OccurrenceId == occurrence.OccurrenceId)
                    .ToDictionary(row => row.ConsumerId, row => row.InstanceValue!, StringComparer.Ordinal);
                var graph = CreateCompositeWriteGraph(draft.WorkspaceId, draft, compiled, qualities,
                    ownerId, definition.Name, defaultNotesColor, reserved, dependentSelections);
                if (graph is null) return Failure(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                qualityXml.Add(CreateQuality(effective, definition, ownerId, defaultNotesColor).ToString(SaveOptions.DisableFormatting));
                qualityXml.AddRange(graph.DependentQualities.Select(node => node.ToString(SaveOptions.DisableFormatting)));
                improvements.AddRange(graph.Improvements.Select(node => node.ToString(SaveOptions.DisableFormatting)));
                dependentCount += graph.DependentQualities.Length;
                owners.Add(new(occurrence.OccurrenceId, definition.SourceId, ownerId, definition.Karma));
            }

            var existing = character.Element("qualities")?.Elements("quality").ToArray() ?? [];
            if (existing.Any(item => owners.Any(owner => owner.SourceId == item.Element("sourceid")?.Value)
                    && item.Element("qualitysource")?.Value == "LifeModule"))
                return Failure(CharacterCreationFoundationBlockers.PendingDraftConflict);
            foreach (var level in sequence.QualityLevels)
            {
                var groupNames = levels.GetQualityNames(level.Group);
                var groupIds = groupNames.Select(name => qualities.TryResolveExact(name, out var target) ? target!.SourceId : string.Empty)
                    .ToHashSet(StringComparer.Ordinal);
                if (existing.Any(item => groupNames.Contains(item.Element("name")?.Value ?? string.Empty, StringComparer.Ordinal)
                        || groupIds.Contains(item.Element("sourceid")?.Value ?? "missing"))
                    || character.Element("improvements")?.Elements("improvement").Any(item =>
                        item.Element("improvementttype")?.Value == "QualityLevel" && item.Element("improvedname")?.Value == level.Group) == true)
                    return Failure(CharacterCreationFoundationBlockers.PendingDraftConflict);
                if (!qualities.TryGetDefinition(level.Target, out var definition, out _) || definition is null
                    || !CharacterCreationFoundationQualityInstanceResolver.TryInspectSelection(definition, out bool needsText)
                    || (needsText && string.IsNullOrWhiteSpace(level.InstanceValue)))
                    return Failure(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                string id = SequenceQualityId(new { draft.WorkspaceId, draft.DraftDigest, sequence.CompilationDigest, level.Group, level.Target });
                XElement? winner = CreateDependentQuality(definition, id, level.InstanceValue ?? string.Empty,
                    string.Empty, defaultNotesColor, qualityLevel: true);
                if (winner is null) return Failure(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                qualityXml.Add(winner.ToString(SaveOptions.DisableFormatting));
                foreach (var bonus in definition.Element("bonus")?.Elements() ?? [])
                {
                    if (bonus.Name == "selecttext") continue; // Already validated/resolved; never execute its catalog XPath.
                    if (!TryCreateDependentBonusImprovement(bonus, id, defaultNotesColor, out var improvement))
                        return Failure(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                    if (improvement is not null) improvements.Add(improvement.ToString(SaveOptions.DisableFormatting));
                }
            }

            var allNames = existing.Select(item => item.Element("name")?.Value ?? string.Empty)
                .Concat(qualityXml.Select(xml => XElement.Parse(xml).Element("name")!.Value)).ToHashSet(StringComparer.Ordinal);
            foreach (var level in sequence.QualityLevels)
            {
                qualities.TryGetDefinition(level.Target, out var definition, out _);
                if (!MatchesQualityConditions(definition!, allNames))
                    return Failure(CharacterCreationFoundationBlockers.FinalizationRequirementUnsupported);
            }
            if (qualityXml.Select(xml => XElement.Parse(xml).Element("guid")!.Value).Distinct(StringComparer.Ordinal).Count() != qualityXml.Count)
                return Failure(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
            var plan = new CharacterCreationFoundationSequenceWritePlan("life-module-full-effect-graph/v1", draft.WorkspaceId,
                draft.DraftRevision, draft.DraftDigest, draft.BaseRawCharacterXmlDigest, sequence.CompilationDigest,
                sequence.SourceDigest, sequence.SourceContextDigest, catalogRawXmlDigest, owners.ToArray(), qualityXml.ToArray(), improvements.ToArray(),
                dispositions.ToArray(), dependentCount, sequence.QualityLevels.Count, owners.Sum(owner => (decimal)owner.KarmaCost), string.Empty);
            return new(plan with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(plan) }, []);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            return Failure(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
        }
    }

    private static XElement ResolveEffectiveModule(XElement root, CharacterCreationFoundationSelection selection)
    {
        XElement module = root.Element("modules")!.Elements("module").Single(node => node.Element("id")?.Value == selection.ModuleId);
        XElement effective;
        if (selection.VersionId is null)
            effective = new XElement(module);
        else
        {
            var version = module.Element("versions")!.Elements("version").Single(node => node.Element("id")?.Value == selection.VersionId);
            effective = new XElement(version);
            // Legacy GetNodeOverrideable: version scalars win; parent bonus is
            // appended after version bonus. Never manufacture source flags from a DTO.
            foreach (var child in module.Elements().Where(child => child.Name != "versions"))
            {
                if (effective.Element(child.Name) is not XElement current) effective.Add(new XElement(child));
                else if (child.Name == "bonus") current.Add(child.Nodes());
            }
        }
        effective.Elements("versions").Remove();
        if (effective.Element("bonus") is null) effective.Add(new XElement("bonus"));
        if (effective.Element("selectable") is XElement selectable && !bool.Parse(selectable.Value))
            throw new InvalidOperationException("Source module is not selectable.");
        return effective;
    }

    private static HashSet<string>? ResolveSequencePushes(CharacterCreationLifeModuleOccurrenceCompilation occurrence,
        IReadOnlyList<CharacterCreationLifeModuleQualityLevelResolution> levels,
        ICollection<CharacterCreationFoundationSequencePushDisposition> dispositions)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var push in occurrence.Compilation.SelectionPushes)
        {
            if (occurrence.Compilation.SelectionBindings.Any(binding => binding.PushEffectId == push.EffectId)) continue;
            var effects = occurrence.Compilation.Effects.Where(effect => effect.EffectKind == "qualitylevel").ToArray();
            if (effects.Length != 1 || push.Order >= effects[0].Order
                || occurrence.Compilation.SelectionPushes.Count(other => !occurrence.Compilation.SelectionBindings.Any(binding => binding.PushEffectId == other.EffectId)) != 1)
                return null;
            var level = levels.SingleOrDefault(item => item.Group == effects[0].Parameters["@group"]);
            if (level is null || level.InstanceValue is null) return null;
            int tier = int.Parse(effects[0].TargetId, CultureInfo.InvariantCulture);
            string disposition;
            if (tier < level.Level) disposition = "retired-superseded-quality-tier";
            else if (tier == level.Level && level.InstancePrompt?.Options.Any(option => option.IsEnabled && option.SourceValue == push.Literal) == true)
                disposition = push.Literal == level.InstanceValue ? "winning-quality-instance" : "retired-unselected-instance";
            else return null;
            reserved.Add(push.EffectId);
            dispositions.Add(new(occurrence.OccurrenceId, push.EffectId, push.InstructionDigest, level.Group, disposition));
        }
        return reserved;
    }

    private static bool MatchesQualityConditions(XElement quality, IReadOnlySet<string> names)
    {
        foreach (string condition in new[] { "required", "forbidden" })
        {
            XElement? node = quality.Element(condition);
            if (node is null || (!node.HasElements && string.IsNullOrWhiteSpace(node.Value))) continue;
            if (node.HasAttributes || node.Elements().Count() != 1 || node.Element("oneof") is not XElement oneof
                || oneof.HasAttributes || !oneof.HasElements || oneof.Elements().Any(item => item.Name != "quality"
                    || item.HasAttributes || item.HasElements || string.IsNullOrWhiteSpace(item.Value) || item.Value != item.Value.Trim())
                || node.Nodes().Concat(oneof.Nodes()).Any(item => item is not XElement && item is not XComment
                    && (item is not XText text || !string.IsNullOrWhiteSpace(text.Value)))) return false;
            bool matches = oneof.Elements().Any(item => names.Contains(item.Value));
            if (condition == "required" ? !matches : matches) return false;
        }
        return true;
    }

    private static string SequenceQualityId<T>(T value)
    {
        string seed = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
        char[] hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).AsSpan(0, 32).ToArray();
        hex[12] = '8'; hex[16] = '8';
        return Guid.ParseExact(new string(hex), "N").ToString("D");
    }

    private static CharacterCreationFoundationSequenceWritePlanResult Failure(string blocker) => new(null, [blocker]);
}

internal sealed record CharacterCreationFoundationSequenceWritePlanResult(CharacterCreationFoundationSequenceWritePlan? Plan,
    IReadOnlyList<string> Blockers)
{
    public bool IsReady => Plan is not null && Blockers.Count == 0;
}
