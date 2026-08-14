using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

public sealed record CharacterPetEditSemantics(
    bool IdentityEditable,
    bool CanDelete);

public static class CharacterPetEditSemanticsResolver
{
    public static bool TryResolve(
        XElement pet,
        out CharacterPetEditSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(pet);

        if (!string.Equals(ReadValue(pet, "type"), "Pet", StringComparison.OrdinalIgnoreCase))
        {
            semantics = null!;
            return false;
        }

        bool linked = !string.IsNullOrWhiteSpace(ReadValue(pet, "file"))
            || !string.IsNullOrWhiteSpace(ReadValue(pet, "relative"));
        semantics = new CharacterPetEditSemantics(
            IdentityEditable: !linked,
            CanDelete: true);
        return true;
    }

    private static string ReadValue(XElement element, string name)
        => element.Element(name)?.Value.Trim() ?? string.Empty;
}
