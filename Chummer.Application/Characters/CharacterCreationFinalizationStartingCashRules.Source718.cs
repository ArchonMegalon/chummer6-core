using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationFinalizationStartingCashRules
{
    // The same projection is used before a roll is chosen and by TryProject
    // after the explicit cash choice. It executes Street's actual economics
    // and saved fields; it does not invent a dice total or claim cash was paid.
    public static bool TryProjectDefaultLifestyle718(CharacterWorkspaceId id,
        CharacterCreationStartingNuyenSource source, CharacterCreationLifestylesAuthority authority,
        out CharacterCreationLifestyleProjection? projection, out XElement? lifestyle)
    {
        projection = null;
        lifestyle = null;
        if (!SourcesMatch(source, authority) || string.IsNullOrWhiteSpace(id.Value)) return false;
        var option = authority.LifestyleOptions.Single(row => row.SourceId.ToString("D") == source.SourceId);
        if (source.Name != "Street" || option.BaseCost != 0) return false;
        var configuration = new CharacterCreationLifestyleConfiguration(
            CharacterCreationFinalizationProjector.StableGuid("priority-default-lifestyle:" + id.Value), option.OptionId,
            option.Name, CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId, 1, 100m, 0,
            false, false, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, []);
        if (!CharacterCreationLifestylesRules.TryProject(configuration, authority, out var result, out _)
            || result.Economics.TotalCost != 0) return false;
        projection = result;
        lifestyle = CharacterCreationLifestylesService.BuildLifestyleElement(result, null, authority);
        return true;
    }
}
