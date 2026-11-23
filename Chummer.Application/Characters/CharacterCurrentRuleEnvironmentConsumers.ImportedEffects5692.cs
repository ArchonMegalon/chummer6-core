using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    // Private calculation authority, never a persisted receipt or a grant capability.
    // CareerEffects is cleared on disposal or invalidation of the exact retained
    // evaluation (including observed ABA); detached XML is returned to each caller.
    private sealed record ImportedCareerEffects5688(CharacterSheetDerivedQuery.ImportedCareerSheet5699 Read,
        IReadOnlyList<(Guid OwnerId, Guid SourceId, string SourceXml)> GrantedQualities, string SkillCostsXml)
    {
        internal CharacterSheetOwnerGraph Owners => Read.Owners!;
        internal CharacterSheetDerivedSection Sheet => Read.Sheet;
    }

    private static CurrentCareerEffects1960? CompileImportedCareerEffects5688(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source,
        List<string> blockers, RetainedEvaluation2764? evaluation)
    {
        try
        {
            if (!UsesOwnedPriorityCareer9569(current) || current.Stage != CharacterCurrentRuleStage.Career
                || !current.IsUnchanged) return Fail("context-unavailable");
            var root = ParseCreation1949(current.CharacterXml);
            // MAG/RES selections are proved below by the retained Priority adapter.
            // Grade history is authenticated by the retained owner compiler below;
            // selected spirits are source-proved by the owner graph and existing fettering kernel.
            // Ordinary focus ownership is compiled and reconciled by the sheet owner graph.
            foreach (string flag in new[] { "depenabled", "ai", "ignorerules" })
                if (!ReadSkillSwitch1950(root, flag, false, out bool enabled) || enabled)
                    return Fail("talent-provenance-required");
            foreach (string counter in new[] { "burnededge", "specialattburn" })
                if (!ReadSkillInt1949(root, counter, 0, out int value) || value != 0)
                    return Fail(counter + "-history-unresolved");
            foreach (string container in new[] { "arts", "stackedfoci" })
                if (root.Elements(container).Any(row => row.HasElements || row.HasAttributes || DirectSkillText1949(row)))
                    return Fail("selected-" + container + "-unsupported");
            if (!source.TryResolveCareerSettingsProfile(out string settings, out string settingsState)
                || string.IsNullOrWhiteSpace(settingsState)
                || !CharacterSheetDerivedQuery.TryMetatype(root, source, out string metatypeXml,
                    out string metatypeState, out Guid parentId)
                || !source.TryResolveCreationMetatypeCatalog(out var catalog) || !catalog.IsAuthoritative
                || catalog.Blockers.Count != 0 || !catalog.SourceContext.IsAuthoritative
                || catalog.SourceContext.Blockers.Count != 0) return Fail("selected-source-unresolved");
            var selectedSource = ParseCreation1949(metatypeXml);
            var candidates = catalog.Options.Where(option => option.IsEnabled && option.Blockers.Count == 0
                && option.OptionId == ScalarCreation1949(selectedSource, "id")
                && option.Label == ScalarCreation1949(selectedSource, "name")).ToArray();
            CharacterCreationMetatypeOptionProjection? metatype = candidates.Length == 1 ? candidates[0] : null;
            if (metatype is null && !CharacterSpriteSource5751.IsSource(selectedSource)) return Fail("metatype-source-unresolved");
            if (evaluation is null && current.Capture.CreateCurrentEnvironmentResolution(
                    current.SemanticsProfile, current.SemanticsFingerprint) is null)
                return Fail("retained-resolution-unavailable");
            // This call consumes the original document and its capture. It exposes
            // owners only after complete sheet/effect/source reconciliation, never
            // the independent-trait evidence of an unavailable sheet.
            var read = CharacterSheetDerivedQuery.ParseImportedCareerForEvaluation6371(current, source, evaluation);
            var sheet = read.Sheet;
            var owners = read.Owners;
            if (sheet.UnavailableReason is not null || sheet.DefenseUnavailableReason is not null
                || sheet.AuthorityDigest is null || owners is null)
                return Fail("sheet-owner-closure:" + (sheet.UnavailableReason ?? sheet.DefenseUnavailableReason ?? "missing-authority"));
            if (owners.Sprite5751 is { } sprite)
            {
                if (catalog.SourceContext.MetatypeKarmaMultiplier is not int multiplier
                    || catalog.SourceContext.MinimumInitiativeDiceFallback is not int dice) return Fail("sprite-profile-unresolved");
                metatype = CharacterSpriteSource5751.Project(sprite, multiplier, dice);
            }
            if (metatype is null) return Fail("metatype-source-unresolved");
            if (owners.Effects.Any(row => ScalarCreation1949(row, "improvementttype") == "SkillGroupDisable")
                || !TryProjectOwnedSkillCosts5688(owners, out var costs, read.HasPrioritySelection9569 || owners.Sprite5751 is not null)) return Fail("skill-rank-or-cost-unconsumed");
            // The sheet compiler currently refuses attribute-price source clauses.
            // Keep that boundary explicit here: an empty modifier list is never a
            // substitute for a newly supported attribute price effect.
            if (owners.Effects.Any(row => ScalarCreation1949(row, "improvementttype")
                is "AttributeKarmaCost" or "AttributeKarmaCostMultiplier"))
                return Fail("attribute-cost-modifier-unconsumed");
            if (!current.IsUnchanged
                || !source.TryResolveCareerSettingsProfile(out string finalSettings, out string finalState)
                || settings != finalSettings || settingsState != finalState
                || !CharacterSheetDerivedQuery.TryMetatype(root, source, out string finalMetatype,
                    out string finalMetatypeState, out Guid finalParent)
                || metatypeXml != finalMetatype || metatypeState != finalMetatypeState || parentId != finalParent
                || !source.TryResolveCreationMetatypeCatalog(out var finalCatalog)
                || CharacterCreationFinalizationDigest.Compute(catalog) != CharacterCreationFinalizationDigest.Compute(finalCatalog))
                return Fail("source-drift");
            var graph = new XElement(root);
            graph.Elements("improvements").Remove();
            graph.Add(new XElement("improvements", owners.Effects.Select(row => new XElement(row))));
            var imported = new ImportedCareerEffects5688(read, owners.NonPurchasedQualities5688(),
                costs.ToString(SaveOptions.DisableFormatting));
            var result = new CurrentCareerEffects1960(graph, metatype, CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = "chummer.imported-priority-career-owner-effects.v1", current.BindingDigest,
                current.CaptureDigest, current.EffectivePolicyDigest, current.RequiredPackageSetDigest,
                current.SemanticsProfile, current.SemanticsFingerprint, settingsState, metatypeState,
                Catalog = catalog, owners.AuthorityDigest, Sheet = sheet.AuthorityDigest, imported.SkillCostsXml
            }), [], [], [], null, []) { Imported = imported };
            if (evaluation is not null)
            {
                if (!ReferenceEquals(evaluation, RetainedEvaluationFor2764(current, source))) return Fail("evaluation-changed");
                evaluation.CareerEffects = result with { Graph = new XElement(graph) };
            }
            return result;
        }
        catch (Exception error) when (CreationInputError1949(error) || error is IOException)
        { return Fail("selected-graph-malformed-or-unsupported"); }
        CurrentCareerEffects1960? Fail(string reason)
        { blockers.Add("current-imported-career-" + reason); return null; }
    }

    private static SelectedSkillEffects1950? ImportedSkillCosts5688(CurrentCareerEffects1960 selected, List<string> blockers)
    {
        if (selected.Imported is not { } imported)
        { blockers.Add("current-imported-career-owner-closure-missing"); return null; }
        return new(selected.Digest, ParseSkillsXml1949(imported.SkillCostsXml), [], selected, selected.Metatype.Movement)
            { MagicUnlocks5343 = CompiledMagicUnlocks5343(imported.Owners.Effects, imported.Read.HasPrioritySelection9569) };
    }
}
