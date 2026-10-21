using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Explicit starting cash and the exact free fallback lifestyle, separate from creation purchases.</summary>
public static partial class CharacterCreationFinalizationStartingCashRules
{
    public static string Digest(CharacterCreationFinalizationStartingCash value) =>
        CharacterCreationFinalizationDigest.Compute(value with { AuthorityDigest = string.Empty });

    public static bool SourcesMatch(CharacterCreationStartingNuyenSource? source,
        CharacterCreationLifestylesAuthority? lifestyles)
    {
        if (!CharacterCreationKarmaFinalizationBudgetRules.IsValidStartingCashSource(source)
            || lifestyles is null || !CharacterCreationLifestylesRules.IsValidAuthority(lifestyles)
            || source!.SettingsProfileId != lifestyles.SettingsProfileId
            || source.RawProfileInputsDigest != lifestyles.ProfileDigest
            || source.SourceInputsDigest != lifestyles.SourceDigest) return false;
        var options = lifestyles.LifestyleOptions.Where(item => item.SourceId.ToString("D") == source.SourceId).Take(2).ToArray();
        return options is [{ IsSelectable: true, EligibilityIsExact: true } option]
            && option.Name == source.Name && option.StartingNuyenDice == source.Dice
            && option.StartingNuyenMultiplier == source.Multiplier
            && option.SourceBook == source.SourceBook && option.Page == source.Page;
    }

    public static bool TryPrepare(CharacterCreationStartingNuyenSource? source,
        CharacterCreationLifestylesAuthority? lifestyles, CharacterCreationStartingCashChoice? choice,
        out CharacterCreationFinalizationStartingCash? result)
    {
        result = null;
        if (!SourcesMatch(source, lifestyles) || choice is null
            || choice.SourceAuthorityDigest != source!.AuthorityDigest
            || choice.DiceTotal < source.Dice || choice.DiceTotal > (long)source.Dice * 6) return false;
        try { _ = checked(choice.DiceTotal * source.Multiplier); }
        catch (OverflowException) { return false; }
        var candidate = new CharacterCreationFinalizationStartingCash(source, lifestyles!, choice, string.Empty);
        result = candidate with { AuthorityDigest = Digest(candidate) };
        return true;
    }

    public static bool IsValid(CharacterCreationFinalizationStartingCash? value)
        => value is not null && TryPrepare(value.Source, value.Lifestyles, value.Choice, out var expected)
            && value.AuthorityDigest == expected!.AuthorityDigest;

    public static bool MatchesPurchases(CharacterCreationFinalizationStartingCash value,
        CharacterCreationLifestylesDraft? draft)
    {
        if (!IsValid(value)) return false;
        var option = value.Lifestyles.LifestyleOptions.Single(row => row.SourceId.ToString("D") == value.Source.SourceId);
        if (draft is not { Lifestyles.Count: > 0 })
            return value.Source.Name == "Street" && option.BaseCost == 0;
        return draft.SourceAuthorityDigest == value.Lifestyles.AuthorityDigest
            && draft.Lifestyles.Any(row => row.BaseLifestyleOptionId == option.OptionId)
            && draft.ProjectionAuthority.LifestyleOptions.Any(row =>
                CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(row, option));
    }

    public static bool TryProject(CharacterWorkspaceId id, CharacterCreationFinalizationStartingCash? value,
        out decimal amount, out XElement? lifestyle)
    {
        amount = 0; lifestyle = null;
        if (!IsValid(value) || !MatchesPurchases(value!, null) || string.IsNullOrWhiteSpace(id.Value)) return false;
        if (!TryProjectDefaultLifestyle718(id, value!.Source, value.Lifestyles, out _, out lifestyle))
            return false;
        amount = checked(value.Choice.DiceTotal * value.Source.Multiplier);
        return true;
    }
}
