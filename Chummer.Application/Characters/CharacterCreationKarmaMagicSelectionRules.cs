using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Pending Karma magic, priced from the selected profile. Callers own source,
/// workspace and owner admission. Nothing here applies a character mutation.
/// </summary>
public static class CharacterCreationKarmaMagicSelectionRules
{
    public const int MaximumSelections = 256;

    public static CharacterCreationKarmaMetatypeQuote WithoutMagic(CharacterCreationKarmaMetatypeQuote foundation)
    {
        ArgumentNullException.ThrowIfNull(foundation);
        if (foundation.Magic is not { } magic) return foundation;
        if (magic.Cost is null || foundation.KarmaBudget is null)
            throw new ArgumentException("A magic quote requires its cost and foundation budget.", nameof(foundation));
        var result = foundation with
        {
            Magic = null,
            KarmaBudget = foundation.KarmaBudget with
            {
                Used = foundation.KarmaBudget.Used - magic.Cost.TotalKarma,
                Remaining = foundation.KarmaBudget.Remaining + magic.Cost.TotalKarma
            },
            QuoteDigest = string.Empty
        };
        return result with { QuoteDigest = Hash(result) };
    }

    public static bool TryFreeze(CharacterCreationMagicResonanceSelections? selection,
        out CharacterCreationMagicResonanceSelections frozen)
    {
        frozen = new(null, null, [], [], []);
        if (selection is not { AdeptPowers: not null, Spells: not null, ComplexForms: not null,
                MysticAdeptPowerPoints: >= 0 }
            || selection.AdeptPowers.Count > MaximumSelections || selection.Spells.Count > MaximumSelections
            || selection.ComplexForms.Count > MaximumSelections) return false;
        var powers = selection.AdeptPowers.Take(MaximumSelections + 1).ToArray();
        var spells = selection.Spells.Take(MaximumSelections + 1).ToArray();
        var forms = selection.ComplexForms.Take(MaximumSelections + 1).ToArray();
        if (powers.Length > MaximumSelections || spells.Length > MaximumSelections || forms.Length > MaximumSelections
            || powers.Any(item => item is null || item.Levels <= 0 || !Identity(item.Identity, "adept-power"))
            || spells.Any(item => !Identity(item, "spell")) || forms.Any(item => !Identity(item, "complex-form"))
            || selection.Tradition is not null && !Identity(selection.Tradition, "tradition")
            || selection.Stream is not null && !Identity(selection.Stream, "stream")
            || powers.Select(item => item.Identity).Distinct().Count() != powers.Length
            || spells.Distinct().Count() != spells.Length || forms.Distinct().Count() != forms.Length) return false;
        frozen = new(selection.Tradition, selection.Stream,
            powers.OrderBy(item => item.Identity.SourceId, StringComparer.Ordinal).ToArray(),
            spells.OrderBy(item => item.SourceId, StringComparer.Ordinal).ToArray(),
            forms.OrderBy(item => item.SourceId, StringComparer.Ordinal).ToArray())
            { MysticAdeptPowerPoints = selection.MysticAdeptPowerPoints };
        return true;
    }

    public static CharacterCreationKarmaMagicQuote? Evaluate(CharacterCreationKarmaMagicCatalog catalog,
        CharacterCreationKarmaMetatypeQuote foundation,
        IReadOnlyList<CharacterCreationTalentQualitySource> racialSources,
        CharacterCreationTalentQualitySource? talentSource, CharacterCreationMagicResonanceSelections selections)
    {
        try
        {
            if (!IsValidCatalog(catalog)
                || foundation is not { Magic: null, CanSelect: true, Blockers.Count: 0, Talent: { } talent,
                    Attributes: { CanSelect: true } attributes, Skills: { CanSelect: true } skills,
                    Qualities: { } qualities, KarmaBudget: { IsExact: true, Remaining: >= 0 } }
                || catalog.RawProfileInputsDigest != foundation.Binding.SourceProfileDigest
                || catalog.SettingsProfileId != attributes.Policy.SettingsProfileId
                || !Same(talent, catalog.Talents.Options.SingleOrDefault(item => item.OptionId == talent.OptionId))
                || !TryFreeze(selections, out var frozen)
                || !CharacterCreationKarmaEffectsProjector.TryProject(foundation, racialSources, talentSource,
                    out var effects)) return null;

            string[] tabs = effects.Elements("improvement").Where(item => Type(item) == "SpecialTab")
                .Select(item => item.Element("improvedname")!.Value).ToArray();
            bool adept = tabs.Contains("Adept", StringComparer.Ordinal);
            bool magician = tabs.Contains("Magician", StringComparer.Ordinal);
            bool technomancer = tabs.Contains("Technomancer", StringComparer.Ordinal);
            bool mundane = talent.OptionId == CharacterCreationKarmaTalentCatalog.MundaneOptionId;
            string kind = mundane ? "mundane" : technomancer && !adept && !magician && talent.EnabledAttribute == "RES"
                ? "technomancer" : talent.EnabledAttribute == "MAG" && !technomancer
                    ? adept && magician ? "mystic-adept" : adept ? "adept" : magician
                        ? skills.Access.RequiredUnlockChoices.Count > 0 ? "aspected-magician" : "magician" : "unsupported"
                    : "unsupported";
            int magic = attributes.Attributes.Single(item => item.AttributeId == "MAG").Current;
            int resonance = attributes.Attributes.Single(item => item.AttributeId == "RES").Current;
            if (!CharacterCreationKarmaMagicRules.TryCalculateCost(catalog.Policy, kind, magic,
                    frozen.Spells.Count, frozen.ComplexForms.Count, frozen.MysticAdeptPowerPoints, out var cost)) return null;
            var access = new CharacterCreationKarmaMagicAccess(magician, technomancer, adept,
                magician && (kind != "aspected-magician" || skills.Selection.TalentUnlock == "Sorcery"), technomancer);
            var blockers = new HashSet<string>(StringComparer.Ordinal);
            if (magician != (frozen.Tradition is not null))
                blockers.Add(magician ? CharacterCreationMagicResonanceBlockers.TraditionRequired
                    : CharacterCreationMagicResonanceBlockers.TraditionInvalid);
            if (technomancer != (frozen.Stream is not null))
                blockers.Add(technomancer ? CharacterCreationMagicResonanceBlockers.StreamRequired
                    : CharacterCreationMagicResonanceBlockers.StreamInvalid);
            if (!adept && frozen.AdeptPowers.Count > 0) blockers.Add(CharacterCreationMagicResonanceBlockers.PowerSelectionNotAllowed);
            if (!technomancer && frozen.ComplexForms.Count > 0) blockers.Add(CharacterCreationMagicResonanceBlockers.ComplexFormSelectionNotAllowed);
            if (!access.AllowsSpells && frozen.Spells.Count > 0)
                blockers.Add(CharacterCreationMagicResonanceBlockers.SpellSelectionNotAllowed);

            var selected = new List<(CharacterCreationMagicResonanceOptionIdentity Identity, int Levels)>();
            if (frozen.Tradition is not null) selected.Add((frozen.Tradition, 1));
            if (frozen.Stream is not null) selected.Add((frozen.Stream, 1));
            selected.AddRange(frozen.AdeptPowers.Select(item => (item.Identity, item.Levels)));
            selected.AddRange(frozen.Spells.Select(item => (item, 1)));
            selected.AddRange(frozen.ComplexForms.Select(item => (item, 1)));
            var sources = new List<CharacterCreationMagicResonanceOptionFinalizationSource>();
            foreach (var (identity, levels) in selected)
            {
                var option = catalog.Catalogs.Single(slice => slice.Kind == identity.Kind).Options
                    .SingleOrDefault(item => item.Identity == identity);
                if (!CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(option, levels, out var source))
                    return null;
                if (identity.Kind == CharacterCreationMagicResonanceKinds.AdeptPower
                    && levels > CharacterCreationAdeptPowerSourceRules.EffectiveMaximumLevels(option!, magic))
                    blockers.Add(CharacterCreationMagicResonanceBlockers.OptionInvalid);
                sources.Add(source!);
            }
            decimal totalPoints = kind == "adept" ? magic : cost!.MysticPowerPoints?.PowerPoints ?? 0;
            decimal usedPoints = sources.Where(item => item.Identity.Kind == "adept-power")
                .Sum(item => checked(item.PointCost * item.Levels));
            if (usedPoints > totalPoints) blockers.Add(CharacterCreationMagicResonanceBlockers.PowerBudgetExceeded);
            // Chummer5 SelectSpell.AcceptForm limits ordinary spells and rituals
            // separately to twice current MAG. It does not use Priority's free slots.
            int spellLimit = checked(magic * 2);
            int formLimit = catalog.Policy.IgnoreComplexFormLimit ? int.MaxValue : checked(resonance * 2);
            if (sources.Count(item => item.Identity.Kind == "spell" && item.Category != "Rituals") > spellLimit
                || sources.Count(item => item.Identity.Kind == "spell" && item.Category == "Rituals") > spellLimit)
                blockers.Add(CharacterCreationMagicResonanceBlockers.SpellBudgetExceeded);
            if (frozen.ComplexForms.Count > formLimit) blockers.Add(CharacterCreationMagicResonanceBlockers.ComplexFormBudgetExceeded);
            // Every currently compiled quality effect that can constrain spell
            // selection is applied. New compiler capabilities must extend this list.
            string[] descriptors = effects.Elements("improvement").Where(item => Type(item) == "BlockSpellDescriptor")
                .Select(item => item.Element("improvedname")!.Value).ToArray();
            string[] categories = effects.Elements("improvement").Where(item => Type(item) == "LimitSpellCategory")
                .Select(item => item.Element("improvedname")!.Value).ToArray();
            foreach (var spell in sources.Where(item => item.Identity.Kind == "spell"))
            {
                string descriptor = XElement.Parse(spell.CanonicalSourceXml).Element("descriptor")?.Value ?? string.Empty;
                if (descriptors.Any(value => descriptor.Contains(value, StringComparison.Ordinal))
                    || categories.Any(value => value != spell.Category))
                    blockers.Add(CharacterCreationMagicResonanceBlockers.SpellSelectionNotAllowed);
            }
            if (cost!.TotalKarma > foundation.KarmaBudget.Remaining)
                blockers.Add(CharacterCreationKarmaMetatypeBlockers.BudgetExceeded);

            var subsetTalents = catalog.Talents with { Options = [talent], AuthorityDigest = string.Empty };
            subsetTalents = subsetTalents with { AuthorityDigest = CharacterCreationKarmaTalentAuthority.ComputeDigest(subsetTalents) };
            var identities = selected.Select(item => item.Identity).ToHashSet();
            var subset = catalog with
            {
                Talents = subsetTalents,
                Catalogs = catalog.Catalogs.Select(slice => slice with
                    { Options = slice.Options.Where(item => identities.Contains(item.Identity)).ToArray() }).ToArray(),
                AuthorityDigest = string.Empty
            };
            subset = subset with { AuthorityDigest = CharacterCreationKarmaMagicRules.ComputeCatalogDigest(subset) };
            var quote = new CharacterCreationKarmaMagicQuote(CharacterCreationKarmaMagicQuote.SchemaV1,
                catalog.AuthorityDigest, subset, kind, access, attributes.QuoteDigest, skills.QuoteDigest, qualities.QuoteDigest,
                racialSources.ToArray(), talentSource, frozen, sources.ToArray(), cost, totalPoints, usedPoints,
                spellLimit, formLimit, blockers.Order(StringComparer.Ordinal).ToArray(), string.Empty);
            return quote with { QuoteDigest = Hash(quote) };
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or InvalidDataException or OverflowException) { return null; }
    }

    /// <summary>Replay arithmetic from retained source rows, not admission against current source files.</summary>
    public static bool IsValid(CharacterCreationKarmaMagicQuote? quote, CharacterCreationKarmaMetatypeQuote foundation,
        CharacterCreationMagicResonanceSelections? selections)
    {
        if (quote is null) return selections is null;
        if (quote is not { Schema: CharacterCreationKarmaMagicQuote.SchemaV1, Blockers.Count: 0,
                ProjectionCatalog: not null, Access: not null, RacialSources: not null, Cost: not null, Sources: not null, Selections: not null }
            || foundation is not { KarmaBudget: not null }
            || selections is null
            || !CharacterCreationMagicResonanceDigest.IsCanonical(quote.SourceAuthorityDigest)
            || quote.QuoteDigest != Hash(quote with { QuoteDigest = string.Empty })) return false;
        var expected = Evaluate(quote.ProjectionCatalog, WithoutMagic(foundation), quote.RacialSources, quote.TalentSource, selections);
        if (expected is null) return false;
        expected = expected with { SourceAuthorityDigest = quote.SourceAuthorityDigest, QuoteDigest = string.Empty };
        expected = expected with { QuoteDigest = Hash(expected) };
        return Same(quote, expected);
    }

    public static bool IsValidCatalog(CharacterCreationKarmaMagicCatalog? catalog)
    {
        string[] kinds = ["tradition", "stream", "adept-power", "spell", "complex-form"];
        return catalog is { Schema: CharacterCreationKarmaMagicCatalog.SchemaV1, Policy: not null,
                Talents: { Options: not null }, Catalogs.Count: 5, SourceAnchorIds.Count: > 0 }
            && CharacterCreationKarmaMagicRules.IsValidPolicy(catalog.Policy)
            && catalog.SettingsProfileId == catalog.Policy.SettingsProfileId
            && catalog.SettingsProfileId == catalog.Talents.SettingsProfileId
            && catalog.RawProfileInputsDigest == catalog.Talents.RawProfileInputsDigest
            && CharacterCreationMagicResonanceDigest.IsCanonical(catalog.RawProfileInputsDigest)
            && CharacterCreationMagicResonanceDigest.IsCanonical(catalog.CustomDataInputsDigest)
            && catalog.Talents.AuthorityDigest == CharacterCreationKarmaTalentAuthority.ComputeDigest(catalog.Talents)
            && catalog.Talents.Options.All(item => item is not null)
            && catalog.Talents.Options.Select(item => item.OptionId).Distinct(StringComparer.Ordinal).Count() == catalog.Talents.Options.Count
            && catalog.Catalogs.All(slice => slice is not null && slice.Options is not null
                && CharacterCreationMagicResonanceDigest.IsCanonical(slice.EffectiveSourceDigest)
                && slice.Options.All(item => item is not null && Identity(item.Identity, slice.Kind))
                && slice.Options.Select(item => item.Identity).Distinct().Count() == slice.Options.Count)
            && catalog.Catalogs.Select(slice => slice.Kind).Order(StringComparer.Ordinal).SequenceEqual(kinds.Order(StringComparer.Ordinal))
            && catalog.AuthorityDigest == CharacterCreationKarmaMagicRules.ComputeCatalogDigest(catalog);
    }

    private static bool Identity(CharacterCreationMagicResonanceOptionIdentity? identity, string kind) =>
        identity is not null && identity.Kind == kind && Guid.TryParseExact(identity.SourceId, "D", out var id)
        && id != Guid.Empty && id.ToString("D") == identity.SourceId;
    private static string Type(XElement effect) => effect.Element("improvementttype")?.Value ?? string.Empty;
    private static bool Same<T>(T left, T right) => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
}
