using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Magical traditions available to SR6 magical talents. Ids reference the legacy
/// core-content traditions catalog (traditions.xml source ids) so character materialization
/// can carry the canonical source id; selection itself is edition-shaped for SR6.</summary>
public static class Sr6CreationTraditionRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p140-144";

    public static IReadOnlyList<Sr6CreationTraditionOption> Catalog() =>
    [
        new("shinto", "Shinto", SourceAnchor),
        new("witchcraft", "Witchcraft", SourceAnchor),
        new("hermetic", "Hermetic", SourceAnchor),
        new("shamanic", "Shamanic", SourceAnchor),
        new("chaos-magic", "Chaos Magic", SourceAnchor),
        new("christian-theurgy", "Christian Theurgy", SourceAnchor),
        new("buddhism", "Buddhism", SourceAnchor),
        new("druid", "Druid", SourceAnchor),
        new("hinduism", "Hinduism", SourceAnchor),
        new("islam", "Islam", SourceAnchor),
        new("black-magic", "Black Magic", SourceAnchor),
        new("aztec", "Aztec", SourceAnchor),
    ];

    public static bool Applies(string? talentId)
        => talentId is "magician" or "mystic-adept" or "aspected-magician";

    public static IReadOnlyList<Sr6CreationTraditionOption>? Options(Sr6CreationFoundationPreview foundation)
        => Applies(foundation.Selection.TalentId) ? Catalog() : null;

    public static CharacterCreationFoundationResult<Sr6CreationTraditionPreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationTraditionSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeTradition(selection, out var frozen))
            return Fail(Sr6CreationTraditionBlockers.InvalidSelection);
        if (!Applies(foundation.Selection.TalentId))
            return Fail(Sr6CreationTraditionBlockers.TalentRequired);
        var option = Catalog().SingleOrDefault(row => row.Id == frozen!.TraditionId);
        if (option is null)
            return Fail(Sr6CreationTraditionBlockers.InvalidSelection);
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-traditions.v1", SourceAnchor,
            Sr6CreationFoundationRules.CoreSourceSha256, Catalog = Catalog(), Selection = frozen
        });
        return new(CharacterCreationFoundationOutcomes.Success,
            new(option, authority, [SourceAnchor]), []);
    }

    private static CharacterCreationFoundationResult<Sr6CreationTraditionPreview> Fail(string blocker)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
