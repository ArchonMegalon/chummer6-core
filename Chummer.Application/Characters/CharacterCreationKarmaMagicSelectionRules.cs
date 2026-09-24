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

            var purchase = CharacterCreationMagicPurchaseRules.Evaluate(catalog.Policy, catalog.Catalogs, talent,
                skills.Access.RequiredUnlockChoices.Count > 0, skills.Selection.TalentUnlock,
                attributes.Attributes.Single(item => item.AttributeId == "MAG").Current,
                attributes.Attributes.Single(item => item.AttributeId == "RES").Current,
                effects.Elements("improvement").ToArray(), frozen, foundation.KarmaBudget.Remaining, false);
            if (purchase is null) return null;

            var subsetTalents = catalog.Talents with { Options = [talent], AuthorityDigest = string.Empty };
            subsetTalents = subsetTalents with { AuthorityDigest = CharacterCreationKarmaTalentAuthority.ComputeDigest(subsetTalents) };
            var identities = purchase.Sources.Select(item => item.Identity).ToHashSet();
            var subset = catalog with
            {
                Talents = subsetTalents,
                Catalogs = catalog.Catalogs.Select(slice => slice with
                    { Options = slice.Options.Where(item => identities.Contains(item.Identity)).ToArray() }).ToArray(),
                AuthorityDigest = string.Empty
            };
            subset = subset with { AuthorityDigest = CharacterCreationKarmaMagicRules.ComputeCatalogDigest(subset) };
            var quote = new CharacterCreationKarmaMagicQuote(CharacterCreationKarmaMagicQuote.SchemaV1,
                catalog.AuthorityDigest, subset, purchase.TalentKind, purchase.Access, attributes.QuoteDigest, skills.QuoteDigest, qualities.QuoteDigest,
                racialSources.ToArray(), talentSource, purchase.Selections, purchase.Sources, purchase.Cost, purchase.PowerPointsTotal, purchase.PowerPointsUsed,
                purchase.MaximumSpellsPerKind, purchase.MaximumComplexForms, purchase.Blockers, string.Empty);
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
    private static bool Same<T>(T left, T right) => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
}
