using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Deterministic financial completion rules. Current-source admission, lifestyle
/// ownership/effects, whole-build legality and the atomic transition are separate
/// responsibilities. Never converts starting cash into creation spending money.
/// </summary>
public static class CharacterCreationKarmaFinalizationBudgetRules
{
    public const string CarryoverAnchor = "Chummer/Forms/Character Forms/CharacterCreate.cs#ValidateCharacter";
    public const string ResourceRoundingAnchor = "Chummer/Forms/Character Forms/CharacterCreate.cs#CalculateBPandRefreshBPDisplays";
    public const string StartingCashAnchor = "Chummer/Forms/Selection Forms/SelectLifestyleStartingNuyen.cs#GetStartingNuyenAsync";

    public static string PolicyDigest(CharacterCreationKarmaCarryoverPolicy policy)
        => Hash(policy with { AuthorityDigest = string.Empty });

    public static bool IsValidPolicy(CharacterCreationKarmaCarryoverPolicy? policy)
        => policy is { Schema: CharacterCreationKarmaCarryoverPolicy.SchemaV1, MaximumKarma: >= 0,
                MaximumNuyen: >= 0, SourceAnchorIds.Count: > 0 }
            && !string.IsNullOrWhiteSpace(policy.SettingsProfileId)
            && Digest(policy.RawProfileInputsDigest) && policy.SourceAnchorIds.All(Anchor)
            && policy.AuthorityDigest == PolicyDigest(policy);

    public static CharacterCreationStartingNuyenSource? ProjectStartingCashSource(string xml,
        string settingsProfileId, string profileDigest, string sourceInputsDigest)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settingsProfileId) || !Digest(profileDigest) || !Digest(sourceInputsDigest)
                || xml is not { Length: > 0 and <= 128 * 1024 }) return null;
            var row = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            if (row.Name != "lifestyle" || row.HasAttributes || row.Element("hide") is not null
                || !Scalar(row, "id", out string id) || !Guid.TryParseExact(id, "D", out var guid) || guid == Guid.Empty
                || !Scalar(row, "name", out string name) || !Scalar(row, "source", out string book)
                || !Scalar(row, "page", out string page)
                || !Scalar(row, "dice", out string diceText) || !int.TryParse(diceText, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int dice) || dice is < 0 or > int.MaxValue / 6
                || !Scalar(row, "multiplier", out string multiplierText)
                || !decimal.TryParse(multiplierText, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal multiplier) || multiplier < 0) return null;
            var result = new CharacterCreationStartingNuyenSource(CharacterCreationStartingNuyenSource.SchemaV1,
                settingsProfileId, profileDigest, sourceInputsDigest, guid.ToString("D"), name, dice, multiplier,
                book, page, xml, CharacterCreationGearRules.ComputeSourceNodeDigest(xml),
                [$"lifestyles.xml#lifestyle:{guid:D}", $"{book}:{page}", StartingCashAnchor], string.Empty);
            return result with { AuthorityDigest = Hash(result) };
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public static bool IsValidStartingCashSource(CharacterCreationStartingNuyenSource? source)
        => source is not null && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(source,
            ProjectStartingCashSource(source.SourceNodeXml, source.SettingsProfileId,
                source.RawProfileInputsDigest, source.SourceInputsDigest));

    public static CharacterCreationKarmaFinalizationBudgetQuote? Evaluate(
        CharacterCreationKarmaCarryoverPolicy policy, CharacterCreationStartingNuyenSource source,
        CharacterCreationKarmaMetatypeQuote foundation, int diceTotal)
    {
        try
        {
            if (!IsValidPolicy(policy) || !IsValidStartingCashSource(source)
                || policy.SettingsProfileId != source.SettingsProfileId
                || policy.RawProfileInputsDigest != source.RawProfileInputsDigest
                || diceTotal < source.Dice || diceTotal > checked(source.Dice * 6)
                || !CompleteFoundation(foundation, policy) || !MatchesStartingLifestyle(source, foundation)) return null;

            var resources = foundation.Resources!;
            // Legacy DecimalExtensions.StandardRound is CEILING for nonnegative
            // values, not midpoint rounding. Keep historic pending quotes intact
            // and expose the exact finalization adjustment instead of discarding it.
            decimal adjustment = decimal.Ceiling(resources.KarmaInvestment) - resources.KarmaInvestment;
            decimal unroundedKarma = foundation.KarmaBudget.Remaining - adjustment;
            if (unroundedKarma < 0 || decimal.Truncate(unroundedKarma) != unroundedKarma
                || unroundedKarma > int.MaxValue) return null;
            int karma = (int)unroundedKarma;
            int carriedKarma = Math.Min(karma, policy.MaximumKarma);
            decimal available = foundation.Lifestyles?.Budget.Remaining ?? foundation.Gear!.Budget.RemainingNuyen;
            decimal carriedNuyen = Math.Min(available, policy.MaximumNuyen);
            decimal starting = checked(diceTotal * source.Multiplier);
            decimal careerNuyen = checked(carriedNuyen + starting);
            var result = new CharacterCreationKarmaFinalizationBudgetQuote(
                CharacterCreationKarmaFinalizationBudgetQuote.SchemaV1, foundation.QuoteDigest, policy, source, diceTotal,
                adjustment, karma, carriedKarma, karma - carriedKarma, available, carriedNuyen,
                available - carriedNuyen, starting, careerNuyen,
                policy.SourceAnchorIds.Concat(source.SourceAnchorIds).Concat(foundation.SourceAnchorIds)
                    .Append(CarryoverAnchor).Append(ResourceRoundingAnchor)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), string.Empty);
            return result with { QuoteDigest = Hash(result) };
        }
        catch (Exception error) when (error is OverflowException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public static bool IsValid(CharacterCreationKarmaFinalizationBudgetQuote? quote,
        CharacterCreationKarmaMetatypeQuote foundation)
        => quote is not null && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(quote,
            Evaluate(quote.Policy, quote.StartingCashSource, foundation, quote.DiceTotal));

    private static bool CompleteFoundation(CharacterCreationKarmaMetatypeQuote? quote,
        CharacterCreationKarmaCarryoverPolicy policy)
    {
        if (quote is not { Schema: CharacterCreationKarmaMetatypeSchemas.QuoteV1, CanSelect: true, Blockers.Count: 0,
                Binding: not null, Metatype: { IsEnabled: true, KarmaCost: >= 0, Blockers.Count: 0 },
                Talent: { IsEnabled: true, KarmaCost: >= 0, Blockers.Count: 0 }, Attributes.Policy: not null,
                Skills: { Policy: not null, Basis: not null }, Qualities.Policy: not null,
                Resources.Policy: not null, Gear.Basis: not null,
                KarmaBudget: { IsExact: true, Total: >= 0 and <= int.MaxValue, Remaining: >= 0, Blockers.Count: 0 } budget }
            || quote.QuoteDigest != Hash(quote with { QuoteDigest = string.Empty })
            || quote.Binding.SourceProfileDigest != policy.RawProfileInputsDigest
            || quote.Resources.Policy.SettingsProfileId != policy.SettingsProfileId
            || quote.Resources.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest
            || quote.Attributes.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest
            || quote.Skills.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest
            || quote.Qualities.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest
            || quote.Gear.Basis.ProfileDigest != policy.RawProfileInputsDigest
            || budget.BudgetId != CharacterCreationBudgetIds.Karma || budget.Unit != "karma"
            || decimal.Truncate(budget.Total) != budget.Total
            || !CharacterCreationKarmaAttributesRules.IsValid(quote.Attributes, quote.Metatype, quote.Talent, quote.Attributes.Allocations)
            || !CharacterCreationKarmaSkillsRules.IsValid(quote.Skills, quote.Metatype, quote.Talent, quote.Attributes, quote.Skills.Selection)
            || quote.Qualities.Selections is null || quote.Qualities.Selections.Any(item => item is null)
            || !CharacterCreationKarmaQualitiesRules.IsValid(quote.Qualities, quote.Metatype, quote.Talent,
                quote.Qualities.Selections.Select(item => item.OptionId).ToArray())
            || quote.Contacts is { } contacts && (contacts.Lines is null || contacts.Lines.Any(line => line?.Selection is null))
            || !CharacterCreationKarmaContactsRules.IsValid(CharacterCreationKarmaMagicSelectionRules.WithoutMagic(quote),
                quote.Contacts?.Lines?.Select(line => line.Selection).ToArray())
            || quote.Lifestyles is { } lifestyles && (lifestyles.Lines is null || lifestyles.Lines.Any(line => line?.Configuration is null))
            || !CharacterCreationKarmaLifestylesRules.IsValidForFoundation(CharacterCreationKarmaMagicSelectionRules.WithoutMagic(quote),
                quote.Lifestyles?.Lines?.Select(line => line.Configuration).ToArray(), quote.Lifestyles?.StartingLifestyleId)
            || !CharacterCreationKarmaMagicSelectionRules.IsValid(quote.Magic, quote, quote.Magic?.Selections)
            || quote.Magic is { } magic && magic.SourceAuthorityDigest != quote.Binding.MagicAuthorityDigest
            || quote.Gear.Lines is null || quote.Gear.Lines.Any(item => item is null)) return false;
        decimal beforeSkills = budget.Total - quote.Metatype.KarmaCost - quote.Talent.KarmaCost
            - quote.Attributes.KarmaUsed - quote.Qualities.Costs.NetKarmaSpent;
        decimal beforeResources = beforeSkills - quote.Skills.KarmaUsed;
        return quote.Skills.KarmaAvailable == beforeSkills
            && CharacterCreationKarmaResourcesRules.IsValid(quote.Resources, quote.Attributes,
                quote.Resources.KarmaInvestment, beforeResources)
            && CharacterCreationKarmaGearRules.IsValid(quote.Gear, quote.Resources,
                quote.Gear.Lines.Select(item => new CharacterCreationGearSelection(item.OptionId, item.Quantity)).ToArray())
            && budget.Remaining == beforeResources - quote.Resources.KarmaInvestment - (quote.Contacts?.KarmaUsed ?? 0)
                - (quote.Magic?.Cost.TotalKarma ?? 0)
            && budget.Used == budget.Total - budget.Remaining;
    }

    private static bool MatchesStartingLifestyle(CharacterCreationStartingNuyenSource source,
        CharacterCreationKarmaMetatypeQuote foundation)
    {
        if (foundation.Lifestyles is not { Lines.Count: > 0 } lifestyles) return true;
        var selected = lifestyles.Lines.SingleOrDefault(line => line.Configuration.LifestyleId == lifestyles.StartingLifestyleId);
        var option = lifestyles.ProjectionAuthority.LifestyleOptions.SingleOrDefault(row => row.SourceId == selected?.SourceId);
        return option is not null && source.SourceId == option.SourceId.ToString("D")
            && source.SourceInputsDigest == lifestyles.ProjectionAuthority.SourceDigest
            && source.Name == option.Name && source.SourceBook == option.SourceBook && source.Page == option.Page
            && source.Dice == option.StartingNuyenDice && source.Multiplier == option.StartingNuyenMultiplier;
    }

    private static bool Scalar(XElement row, string name, out string value)
    {
        var nodes = row.Elements(name).Take(2).ToArray();
        value = nodes.Length == 1 && !nodes[0].HasElements && !nodes[0].HasAttributes ? nodes[0].Value.Trim() : string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool Anchor(string value) => !string.IsNullOrWhiteSpace(value);
    private static bool Digest(string value) => CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(value);
    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
}
