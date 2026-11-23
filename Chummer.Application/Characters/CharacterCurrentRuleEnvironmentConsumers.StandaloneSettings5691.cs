using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    internal static bool HasImportedSettingsIdentity5691(CharacterCreationSourceProfileAuthority profile)
        => profile.RetainedSettingsFile is { } file
            ? file.IsSelectedBy(profile.SettingsProfileId)
            : Guid.TryParseExact(profile.SettingsProfileId, "D", out var id) && id != Guid.Empty;

    internal static CharacterRetainedSettingsFile? ImportedSettingsFile5691(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source)
        => UsesImportedPriorityCareer5667(current) && current.IsUnchanged
            && source.TryResolveCreationSourceProfile(out var profile)
            && profile.SettingsProfileId == ScalarCreation1949(ParseCreation1949(current.CharacterXml), "settings")
            && profile.RetainedSettingsFile is { } file && file.IsSelectedBy(profile.SettingsProfileId)
                ? file : null;

    private static bool TryReadSelectedMysticPolicy5691(ICharacterSourceDataContext source,
        string selection, string digest, XElement row)
    {
        if (Guid.TryParse(selection, out _))
            return CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(selection, digest,
                row.ToString(SaveOptions.DisableFormatting), out _);
        return source.TryResolveCreationSourceProfile(out var profile) && profile.SettingsProfileId == selection
            && profile.RetainedSettingsFile is { } file && file.IsSelectedBy(selection)
            && CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(selection, digest,
                row.ToString(SaveOptions.DisableFormatting), file, out _);
    }

    private static bool MatchesSettingsRow5691(ICharacterSourceDataContext source, XElement row, string selection)
    {
        // Keep native and custom-GUID row comparison exactly as before.
        if (Guid.TryParse(selection, out _)) return ScalarCreation1949(row, "id") == selection;
        if (!source.TryResolveCreationSourceProfile(out var profile) || profile.SettingsProfileId != selection)
            return false;
        if (profile.RetainedSettingsFile is not { } file)
            return ScalarCreation1949(row, "id") == selection;
        return file.IsSelectedBy(selection) && row.Elements("id").Count() == 1 && row.Elements("name").Count() == 1
            && ScalarCreation1949(row, "id") == file.AuthorProvidedId
            && ScalarCreation1949(row, "name") == file.AuthorProvidedName;
    }
}
