using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCreationFinalizationProjector;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationCompletionFinalizationProjector
{
    private static bool ValidateEssenceFinalGraph1222(XElement root, SelectedGraphInputs graph,
        ICollection<CharacterCreationFinalizationDelta> deltas, ref int order)
    {
        if (graph.Essence1222 is null) return true; // Historical route grants no Essence capability.
        if (!graph.Essence1222.ApplyEssence1222(root)) return false;
        var execution = graph.Essence1222.AppliedEssence1222;
        if (execution is null) return false;
        var losses = execution.Result.Attributes.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var attributes = graph.Skills.Attributes.Attributes.Select(attribute =>
        {
            if (!losses.TryGetValue(attribute.AttributeId, out var loss)) return attribute;
            return attribute with { Current = loss.Current, Minimum = loss.Minimum,
                Maximum = loss.Maximum, AugmentedMaximum = loss.AugmentedMaximum };
        }).ToArray();
        CharacterCreationMagicResonanceTalentOption talent;
        CharacterCreationMagicResonanceSelections selections;
        CharacterCreationMysticAdeptPowerPointPolicy? policy;
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> traditions, streams, powers, spells, forms;
        switch (graph.Awakened)
        {
            case FullAwakenedGraphInputs full:
                talent = full.Inputs.SelectedTalent;
                selections = full.Projection.Selections;
                var authority = full.Inputs.Authority;
                policy = authority.MysticAdeptPowerPointPolicy;
                traditions = authority.Traditions; streams = authority.Streams;
                powers = authority.AdeptPowers; spells = authority.Spells; forms = authority.ComplexForms;
                break;
            case SelectedAwakenedGraphInputs selected:
                talent = selected.Authority.Talent;
                selections = selected.Authority.ProposedSelections;
                policy = selected.Authority.MysticAdeptPowerPointPolicy;
                traditions = selected.Authority.Traditions; streams = selected.Authority.Streams;
                powers = selected.Authority.AdeptPowers; spells = selected.Authority.Spells; forms = selected.Authority.ComplexForms;
                break;
            default: throw new InvalidDataException("Missing talent source.");
        }
        int Current(string id) => losses.TryGetValue(id, out var loss) ? loss.Current : 0;
        bool separate = talent.Kind == CharacterCreationMagicResonanceKinds.MysticAdept && policy?.UsesSeparateMagicAttribute == true;
        var effective = new CharacterCreationMagicResonanceEffectiveAttributes(Current("MAG"), Current("RES"), Current("DEP"),
            separate ? Current("MAGAdept") : talent.Kind == CharacterCreationMagicResonanceKinds.Adept ? Current("MAG") : 0m)
        { MagicAdept = separate ? Current("MAGAdept") : null };
        string[] blockers = CharacterCreationMagicResonanceService.CheckEssenceSelections1222(talent, policy,
            traditions, streams, powers, spells, forms, effective, selections, execution.Result.NonEssencePowerPoints - execution.Result.PowerPointLoss);
        // Native burnout changes the talent/allocations. Preserve the player's
        // choices and require a new choice instead of silently removing that graph.
        foreach (var loss in losses.Values)
            if (loss.Maximum < 1 || (execution.ShownValue && loss.AugmentedReduction >= loss.Maximum))
                blockers = blockers.Append("creation-essence-awakened-access1222:" + loss.Id).ToArray();
        if (blockers.Length == 0 && !CharacterCreationSelectedPrerequisiteConsumer.TryCheckFinalGraph(root,
            graph.Prerequisite, talent.GrantedQualitySources ?? [], attributes, out _))
            blockers = [CharacterCreationSelectedPrerequisiteConsumer.Blocker];
        if (blockers.Length != 0)
        {
            graph.Essence1222.RejectEssence1222(blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).First());
            return false;
        }
        // Preserve identity, order, allocation costs and source anchors. The existing
        // attribute receipt must describe the final native value, not its pre-loss preview.
        ReconcileEssenceDeltas1222(deltas, execution.Result);
        AddDelta(deltas, ref order, "essence:total", CharacterCreationFinalizationDeltaKinds.Attribute, "ESS",
            null, execution.Result.Essence.ToString(CultureInfo.InvariantCulture), 0, 0,
            [CharacterCreationBootstrapProfiles.SettingsSourceAnchor(execution.ProfileId) + ":essenceformat"]);
        return true;
    }
    internal static void ReconcileEssenceDeltas1222(ICollection<CharacterCreationFinalizationDelta> deltas,
        CharacterCreationEssenceRules1222.Result result)
    {
        var losses = result.Attributes.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var reconciled = deltas.Select(delta => delta.Kind == CharacterCreationFinalizationDeltaKinds.Attribute
            && losses.TryGetValue(delta.TargetId, out var loss)
            ? delta with { AfterValue = loss.Current.ToString(CultureInfo.InvariantCulture) } : delta).ToArray();
        deltas.Clear();
        foreach (var delta in reconciled) deltas.Add(delta);
    }

}

// This calls the existing selection evaluator with the retained full/selected
// source authority. It is a predicate only; selections are never rewritten.
public sealed partial class CharacterCreationMagicResonanceService
{
    internal static string[] CheckEssenceSelections1222(CharacterCreationMagicResonanceTalentOption talent,
        CharacterCreationMysticAdeptPowerPointPolicy? policy,
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> traditions,
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> streams,
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> powers,
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> spells,
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> forms,
        CharacterCreationMagicResonanceEffectiveAttributes effective,
        CharacterCreationMagicResonanceSelections selections, decimal powerPointAdjustment)
    {
        var blockers = new List<string>();
        if ((selections.Tradition is not null || selections.Spells.Count != 0) && effective.Magic < 1)
            blockers.Add("creation-essence-awakened-access1222:MAG");
        if (selections.AdeptPowers.Count != 0 && (effective.MagicAdept ?? effective.Magic) < 1)
            blockers.Add("creation-essence-awakened-access1222:MAGAdept");
        if ((selections.Stream is not null || selections.ComplexForms.Count != 0) && effective.Resonance < 1)
            blockers.Add("creation-essence-awakened-access1222:RES");
        _ = EvaluateSelections(new MagicSelectionSources(traditions, streams, powers, spells, forms, policy),
            talent, effective, selections, blockers, powerPointAdjustment);
        return blockers.Select(blocker => "creation-essence-post-loss1222:" + blocker)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}
