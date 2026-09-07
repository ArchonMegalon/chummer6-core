using System.Xml.Linq;

namespace Chummer.Application.Characters;

/// <summary>
/// Initial persisted fields emitted by Chummer5 Character.Save for a new runner.
/// Only Creation owns initialization. This is not a permissive Career/import
/// reader: present fields are preserved, including values that must be rejected
/// by the consuming domain. Older Creation drafts initialize missing fields only
/// in the explicit, digest-bound finalization preview/commit transaction.
/// </summary>
internal static class CharacterCreationCareerBaseline
{
    internal static IReadOnlyList<string> InitializeMissing(XElement root)
    {
        var added = new List<string>();
        foreach ((string name, string value) in new[]
        {
            ("streetcred", "0"), ("notoriety", "0"), ("publicawareness", "0"),
            ("burntstreetcred", "0"), ("expenses", ""), ("improvements", ""), ("contacts", "")
        })
        {
            XElement[] matches = root.Elements().Where(node => node.Name.LocalName == name).Take(2).ToArray();
            if (matches.Length > 1 || matches.Length == 1 && matches[0].Name != name)
                throw new InvalidDataException($"Ambiguous Career field: {name}.");
            if (matches.Length != 0) continue;
            root.Add(new XElement(name, value));
            added.Add(name);
        }
        return added;
    }
}
