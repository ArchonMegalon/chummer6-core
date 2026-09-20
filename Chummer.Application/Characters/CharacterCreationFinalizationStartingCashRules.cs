using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Explicit starting cash and the exact free fallback lifestyle, separate from creation purchases.</summary>
public static class CharacterCreationFinalizationStartingCashRules
{
    public static string Digest(CharacterCreationFinalizationStartingCash value) =>
        CharacterCreationFinalizationDigest.Compute(value with { AuthorityDigest = string.Empty });

    public static bool SourcesMatch(CharacterCreationStartingNuyenSource? source,
        CharacterCreationLifestylesAuthority? lifestyles)
    {
        if (!CharacterCreationKarmaFinalizationBudgetRules.IsValidStartingCashSource(source)
            || lifestyles is null || !CharacterCreationLifestylesRules.IsValidAuthority(lifestyles)
            || source!.Name != "Street" || source.SettingsProfileId != lifestyles.SettingsProfileId
            || source.RawProfileInputsDigest != lifestyles.ProfileDigest
            || source.SourceInputsDigest != lifestyles.SourceDigest) return false;
        var options = lifestyles.LifestyleOptions.Where(item => item.SourceId.ToString("D") == source.SourceId).Take(2).ToArray();
        return options is [{ IsSelectable: true, EligibilityIsExact: true, BaseCost: 0 } option]
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

    public static bool TryProject(CharacterWorkspaceId id, CharacterCreationFinalizationStartingCash? value,
        out decimal amount, out XElement? lifestyle)
    {
        amount = 0; lifestyle = null;
        if (!IsValid(value) || string.IsNullOrWhiteSpace(id.Value)) return false;
        var source = value!.Source;
        var option = value.Lifestyles.LifestyleOptions.Single(item => item.SourceId.ToString("D") == source.SourceId);
        var configuration = new CharacterCreationLifestyleConfiguration(
            CharacterCreationFinalizationProjector.StableGuid("priority-default-lifestyle:" + id.Value), option.OptionId,
            option.Name, CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId, 1, 100m, 0,
            false, false, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, []);
        if (!CharacterCreationLifestylesRules.TryProject(configuration, value.Lifestyles, out var projection, out _)
            || projection.Economics.TotalCost != 0) return false;
        amount = checked(value.Choice.DiceTotal * source.Multiplier);
        lifestyle = CharacterCreationLifestylesService.BuildLifestyleElement(projection, null, value.Lifestyles);
        return true;
    }
}
