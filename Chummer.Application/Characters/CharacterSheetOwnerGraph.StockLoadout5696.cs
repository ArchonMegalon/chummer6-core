using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    // Only native owners produced during an existing private current evaluation
    // can borrow its already validated, deeply immutable loadout authority.
    // The source query and mutable current checks still run for every read.
    private CharacterCurrentRuleEnvironmentContext? stockCurrentObservation;
    private CharacterCurrentRuleEnvironmentConsumers.RetainedEvaluation2764? stockEvaluation;
    private string? stockMatchedGraphDigest;
    private CharacterCreationLoadoutAuthority? stockMatchedAuthority;
    private bool stockGraphInvalid;


    internal void BindCurrentStockObservation(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source,
        CharacterCurrentRuleEnvironmentConsumers.RetainedEvaluation2764 evaluation)
    {
        if (stockEvaluation is not null || !ReferenceEquals(context, source)
            || !ReferenceEquals(current.RetainedEvaluation2764, evaluation)
            || !evaluation.Allows(current, source)) return;
        stockCurrentObservation = current;
        stockEvaluation = evaluation;
    }

    // Reuse only a complete native owner compilation already observed by the
    // stock kernel in this private evaluation. Re-read every original source
    // witness before returning it; no stored/public digest grants this reuse.
    internal bool HasNativeStockObservation8048 => stockMatchedGraphDigest is not null;

    internal bool ReobserveNativeOwners8048(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source,
        CharacterCurrentRuleEnvironmentConsumers.RetainedEvaluation2764 evaluation)
    {
        if (!ReferenceEquals(stockCurrentObservation, current) || !ReferenceEquals(stockEvaluation, evaluation)
            || !ReferenceEquals(context, source) || !evaluation.Allows(current, source)
            || !checks.All(check => check())) return false;
        var calculation = XElement.Parse(current.CharacterXml, LoadOptions.PreserveWhitespace);
        calculation.Elements("improvements").Remove();
        calculation.Add(new XElement("improvements", Effects.Select(row => new XElement(row))));
        return MatchesCurrentStockLoadout5696(calculation)
            && checks.All(check => check()) && evaluation.Allows(current, source);
    }

    private bool TryReadCurrentStockAuthority(out CharacterCreationLoadoutAuthority authority, bool validate)
    {
        authority = CharacterCreationLoadoutAuthority.Unavailable;
        if (stockEvaluation is not { } evaluation)
            return context.TryResolveCreationLoadoutAuthority(out authority)
                && (!validate || CharacterCreationLoadoutRules.IsValidAuthority(authority));
        // A closed or poisoned lifetime never falls back to a fresh observation.
        // A later evaluation of the same character cannot revive this graph.
        return stockCurrentObservation is { } current
            && ReferenceEquals(current.RetainedEvaluation2764, evaluation)
            && evaluation.Allows(current, context)
            && CharacterCurrentRuleEnvironmentConsumers.TryCurrentLoadoutAuthority6218(current, context, out authority)
            && evaluation.Allows(current, context);
    }

    // CritterPower.Save preserves the source bonus, including its <armor> effect.
    // It is not an inventory owner. Reuse the reconciled owner/effect compiler
    // and its live whole-graph/source fence, never a category or element name alone.
    internal bool IsCurrentCritterArmorBonus5925(XElement calculation, XElement node)
    {
        if (node.Name != "armor" || node.HasElements
            || node.Parent is not { } bonus || bonus.Name != "bonus"
            || bonus.Parent is not { } power || power.Name != "critterpower"
            || power.Parent is not { } powers || powers.Name != "critterpowers"
            || !ReferenceEquals(powers.Parent, calculation)) return false;
        return MatchesCurrentStockLoadout5696(calculation)
            && owners.Any(owner => owner.Kind == "CritterPower" && owner.Id == Id(power, "guid"));
    }

    // Calculation authority only. This is the SAME reconciled sheet owner graph,
    // not a saved digest, a Creation grant, or permission to acquire inventory.
    // Recheck both inventories and the live source witnesses before/after the
    // existing stock kernel. All saved effects remain in the calculation tree.
    internal bool MatchesCurrentStockLoadout5696(XElement calculation)
    {
        // A successful whole-owner/source reconciliation can be observed again
        // only inside the same privately issued current evaluation. Source and
        // mutable-current queries still run before and after each graph comparison.
        // Public/live contexts never enter this path. No owner result or proof
        // survives disposal, invalidation, or an observed mutation of our graph.
        if (stockMatchedGraphDigest is { } matched)
        {
            if (stockGraphInvalid || !TryReadCurrentStockAuthority(out var observed, validate: false)
                || !ReferenceEquals(observed, stockMatchedAuthority)) return false;
            var expectedCurrent = new XElement(root);
            expectedCurrent.Elements("improvements").Remove();
            expectedCurrent.Add(new XElement("improvements", Effects.Select(row => new XElement(row))));
            if (CharacterCreationOwnedGearGraph1233.GraphDigest(expectedCurrent) != matched)
            {
                stockGraphInvalid = true;
                return false;
            }
            return CharacterCreationOwnedGearGraph1233.GraphDigest(calculation) == matched
                && TryReadCurrentStockAuthority(out var after, validate: false)
                && ReferenceEquals(after, stockMatchedAuthority);
        }
        if (!CharacterCreationFinalizationDigest.IsCanonical(AuthorityDigest)
            || !Flag(root, "created") || Text(root, "buildmethod", "") != "Priority"
            || !checks.All(check => check())) return false;
        var expected = new XElement(root);
        expected.Elements("improvements").Remove();
        expected.Add(new XElement("improvements", Effects.Select(row => new XElement(row))));
        if (CharacterCreationOwnedGearGraph1233.GraphDigest(expected)
                != CharacterCreationOwnedGearGraph1233.GraphDigest(calculation)
            || !TryReadCurrentStockAuthority(out var authority, validate: true)
            || authority.SettingsProfileId != Text(root, "settings")) return false;
        // Reference equality is only used after private issuance and validation.
        // Public/live sources retain their original whole-value comparison.
        string? digest = stockEvaluation is null ? CharacterCreationFinalizationDigest.Compute(authority) : null;
        var roundOwners5701 = new HashSet<Guid>();
        if (root.Descendants("clips").Any(clips => clips.HasElements))
        {
            var fire = ProjectAmmunition5701(calculation, [], out var custody);
            if (custody is null) return false;
            roundOwners5701.UnionWith(fire.Select(state => state.Identity.WeaponId));
        }
        foreach (var (container, kind, entry) in new[] { ("weapons", "Weapon", "weapon"), ("armors", "Armor", "armor") })
        {
            var rows = root.Element(container)?.Elements().ToArray() ?? [];
            if (rows.Length != owners.Count(owner => owner.Kind == kind)) return false;
            foreach (var row in rows)
            {
                if (row.Name != entry || !CharacterCreationOriginalInventory3475.IsCurrentStockOwner5696(row, authority, roundOwners5701.Contains(Id(row, "guid")))) return false;
                var owner = owners.SingleOrDefault(owner => owner.Kind == kind && owner.Id == Id(row, "guid"));
                var options = authority.Options.Where(option => option.Kind == entry && option.SourceId == Id(row, "sourceid")).ToArray();
                if (owner is null || options is not [var option]
                    || !XNode.DeepEquals(Parse(option.SourceNodeXml), owner.Source)) return false;
            }
        }
        bool matchedCurrent = TryReadCurrentStockAuthority(out var final, validate: false)
            && (stockEvaluation is null ? CharacterCreationFinalizationDigest.Compute(final) == digest
                : ReferenceEquals(final, authority)) && checks.All(check => check());
        if (matchedCurrent && stockEvaluation is not null)
        {
            if (!TryReadCurrentStockAuthority(out var admitted, validate: false)
                || !ReferenceEquals(admitted, authority)) return false;
            stockMatchedGraphDigest = CharacterCreationOwnedGearGraph1233.GraphDigest(expected);
            stockMatchedAuthority = admitted;
        }
        return matchedCurrent;
    }
}
