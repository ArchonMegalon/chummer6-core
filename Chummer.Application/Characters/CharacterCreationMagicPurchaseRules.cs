using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Shared typed purchase semantics after the caller admits its own
/// build-method sources, talent, effective attributes, skills and Karma budget.</summary>
internal static class CharacterCreationMagicPurchaseRules
{
    internal static CharacterCreationMagicPurchaseResult? Evaluate(CharacterCreationKarmaMagicPolicy policy,
        IReadOnlyList<CharacterCreationKarmaMagicCatalogSlice> catalogs, CharacterCreationKarmaTalentOption talent,
        bool requiresSkillUnlock, string? skillUnlock, int magic, int resonance, IReadOnlyList<XElement> effects,
        CharacterCreationMagicResonanceSelections selections, decimal remainingKarma, bool lifeModules)
    {
        try
        {
            if (!CharacterCreationKarmaMagicSelectionRules.TryFreeze(selections, out var frozen)
                || magic < 0 || resonance < 0 || remainingKarma < 0) return null;
            string[] tabs = effects.Where(item => Type(item) == "SpecialTab")
                .Select(item => item.Element("improvedname")!.Value).ToArray();
            bool adept = tabs.Contains("Adept", StringComparer.Ordinal);
            bool magician = tabs.Contains("Magician", StringComparer.Ordinal);
            bool technomancer = tabs.Contains("Technomancer", StringComparer.Ordinal);
            bool mundane = talent.OptionId == CharacterCreationKarmaTalentCatalog.MundaneOptionId;
            string kind = mundane ? "mundane" : technomancer && !adept && !magician && talent.EnabledAttribute == "RES"
                ? "technomancer" : talent.EnabledAttribute == "MAG" && !technomancer
                    ? adept && magician ? "mystic-adept" : adept ? "adept" : magician
                        ? requiresSkillUnlock ? "aspected-magician" : "magician" : "unsupported"
                    : "unsupported";
            if (!CharacterCreationKarmaMagicRules.TryCalculateCost(policy, kind, magic,
                frozen.Spells.Count, frozen.ComplexForms.Count, frozen.MysticAdeptPowerPoints, lifeModules, out var cost)) return null;
            var access = new CharacterCreationKarmaMagicAccess(magician, technomancer, adept,
                magician && (kind != "aspected-magician" || skillUnlock == "Sorcery"), technomancer);
            var blockers = new HashSet<string>(StringComparer.Ordinal);
            if (magician != (frozen.Tradition is not null))
                blockers.Add(magician ? CharacterCreationMagicResonanceBlockers.TraditionRequired : CharacterCreationMagicResonanceBlockers.TraditionInvalid);
            if (technomancer != (frozen.Stream is not null))
                blockers.Add(technomancer ? CharacterCreationMagicResonanceBlockers.StreamRequired : CharacterCreationMagicResonanceBlockers.StreamInvalid);
            if (!adept && frozen.AdeptPowers.Count > 0) blockers.Add(CharacterCreationMagicResonanceBlockers.PowerSelectionNotAllowed);
            if (!technomancer && frozen.ComplexForms.Count > 0) blockers.Add(CharacterCreationMagicResonanceBlockers.ComplexFormSelectionNotAllowed);
            if (!access.AllowsSpells && frozen.Spells.Count > 0) blockers.Add(CharacterCreationMagicResonanceBlockers.SpellSelectionNotAllowed);

            var selected = new List<(CharacterCreationMagicResonanceOptionIdentity Identity, int Levels)>();
            if (frozen.Tradition is not null) selected.Add((frozen.Tradition, 1));
            if (frozen.Stream is not null) selected.Add((frozen.Stream, 1));
            selected.AddRange(frozen.AdeptPowers.Select(item => (item.Identity, item.Levels)));
            selected.AddRange(frozen.Spells.Select(item => (item, 1)));
            selected.AddRange(frozen.ComplexForms.Select(item => (item, 1)));
            var sources = new List<CharacterCreationMagicResonanceOptionFinalizationSource>();
            foreach (var (identity, levels) in selected)
            {
                var option = catalogs.Single(slice => slice.Kind == identity.Kind).Options.SingleOrDefault(item => item.Identity == identity);
                if (!CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(option, levels, out var source)) return null;
                if (identity.Kind == CharacterCreationMagicResonanceKinds.AdeptPower
                    && levels > CharacterCreationAdeptPowerSourceRules.EffectiveMaximumLevels(option!, magic))
                    blockers.Add(CharacterCreationMagicResonanceBlockers.OptionInvalid);
                sources.Add(source!);
            }
            decimal totalPoints = kind == "adept" ? magic : cost!.MysticPowerPoints?.PowerPoints ?? 0;
            decimal usedPoints = sources.Where(item => item.Identity.Kind == "adept-power").Sum(item => checked(item.PointCost * item.Levels));
            if (usedPoints > totalPoints) blockers.Add(CharacterCreationMagicResonanceBlockers.PowerBudgetExceeded);
            // Legacy limits ordinary spells and rituals separately to twice MAG.
            // Neither purchase method receives Priority's free spell slots.
            int spellLimit = checked(magic * 2);
            int formLimit = policy.IgnoreComplexFormLimit ? int.MaxValue : checked(resonance * 2);
            if (sources.Count(item => item.Identity.Kind == "spell" && item.Category != "Rituals") > spellLimit
                || sources.Count(item => item.Identity.Kind == "spell" && item.Category == "Rituals") > spellLimit)
                blockers.Add(CharacterCreationMagicResonanceBlockers.SpellBudgetExceeded);
            if (frozen.ComplexForms.Count > formLimit) blockers.Add(CharacterCreationMagicResonanceBlockers.ComplexFormBudgetExceeded);
            string[] descriptors = effects.Where(item => Type(item) == "BlockSpellDescriptor").Select(item => item.Element("improvedname")!.Value).ToArray();
            string[] categories = effects.Where(item => Type(item) == "LimitSpellCategory").Select(item => item.Element("improvedname")!.Value).ToArray();
            foreach (var spell in sources.Where(item => item.Identity.Kind == "spell"))
            {
                string descriptor = XElement.Parse(spell.CanonicalSourceXml).Element("descriptor")?.Value ?? string.Empty;
                if (descriptors.Any(value => descriptor.Contains(value, StringComparison.Ordinal)) || categories.Any(value => value != spell.Category))
                    blockers.Add(CharacterCreationMagicResonanceBlockers.SpellSelectionNotAllowed);
            }
            if (cost!.TotalKarma > remainingKarma)
                blockers.Add(lifeModules ? CharacterCreationAttributesBlockers.GlobalKarmaExceeded : CharacterCreationKarmaMetatypeBlockers.BudgetExceeded);
            return new(kind, access, frozen, sources.ToArray(), cost, totalPoints, usedPoints, spellLimit, formLimit,
                blockers.Order(StringComparer.Ordinal).ToArray());
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException or OverflowException)
        { return null; }
    }

    private static string Type(XElement effect) => effect.Element("improvementttype")?.Value ?? string.Empty;
}

internal sealed record CharacterCreationMagicPurchaseResult(string TalentKind, CharacterCreationKarmaMagicAccess Access,
    CharacterCreationMagicResonanceSelections Selections, IReadOnlyList<CharacterCreationMagicResonanceOptionFinalizationSource> Sources,
    CharacterCreationKarmaMagicPurchaseCost Cost, decimal PowerPointsTotal, decimal PowerPointsUsed,
    int MaximumSpellsPerKind, int MaximumComplexForms, IReadOnlyList<string> Blockers);
