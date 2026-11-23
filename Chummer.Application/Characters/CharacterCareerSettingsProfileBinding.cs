using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Shared focus-bonding identity rule. A filename selects retained source bytes,
// not their author ID. The resolver owns byte/currentness verification; this
// identity check neither grants authority nor substitutes a settings profile.
internal static class CharacterCareerSettingsProfileBinding
{
    internal static bool MatchesSelectedProfile(string selection, XElement settings,
        ICharacterSourceDataContext context, string errorPrefix)
    {
        if (!CharacterRetainedSettingsFile.IsFileSelector(selection)) return selection == Scalar(settings, "id", errorPrefix);
        // Preserve the original focus guard's evaluation order and lazy fields.
        return context.TryResolveCreationSourceProfile(out var profile) && profile.SettingsProfileId == selection
            && profile.RetainedSettingsFile is { } file && file.IsSelectedBy(selection)
            && Scalar(settings, "id", errorPrefix) == file.AuthorProvidedId
            && Scalar(settings, "name", errorPrefix) == file.AuthorProvidedName;
    }

    // Exact Career scalar rules and caller error namespace. No trimming,
    // normalization, fallback values, or eager name requirement for GUID rows.
    private static string Scalar(XElement parent, string name, string errorPrefix)
    {
        var nodes = parent.Elements().Where(x => x.Name.LocalName == name).ToArray();
        Need(nodes.Length <= 1 && (nodes.Length == 0 || nodes[0].Name == name && !nodes[0].HasAttributes),
            errorPrefix + "_ambiguous_field");
        var node = nodes.SingleOrDefault();
        Need(node is not null, errorPrefix + "_missing_field_" + name);
        Need(node is null || !node.HasElements, errorPrefix + "_scalar_invalid");
        return node!.Value;
    }

    private static void Need(bool value, string error)
    {
        if (!value) throw new InvalidDataException(error);
    }
}
