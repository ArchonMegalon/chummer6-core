namespace Chummer.Application.Characters;

/// <summary>Current, enabled, amended source rows for a captured SR5 read. No mutation authority.</summary>
public interface ICharacterSheetOwnerSourceContext
{
    bool TryResolveSheetOwnerSource(string kind, Guid sourceId, out CharacterSheetOwnerSource? source);
    bool TryResolveSheetNamedSource(string kind, string identity, out CharacterSheetOwnerSource? source)
    { source = null; return false; }
    // Legacy grant references use the first canonical name match (e.g. Mundane
    // and Drake Natural Weapon); ordinary named lookups remain unambiguous.
    bool TryResolveSheetGrantSource(string kind, string identity, out CharacterSheetOwnerSource? source) =>
        TryResolveSheetNamedSource(kind, identity, out source);
    bool TryResolveSheetMetavariant(Guid parentId, string identity, out CharacterSheetOwnerSource? source)
    { source = null; return false; }
    // Effective category evidence from the same selected source context. Missing,
    // ambiguous or unsupported custom category mechanics fail closed.
    bool TryResolveSheetSpellCategory(string category, out CharacterSheetOwnerSource? source)
    { source = null; return false; }
    bool TryResolveSheetWareGrade(string kind, string name, out CharacterSheetOwnerSource? source);
}

/// <summary>Engine-local source evidence. Includes effective catalog, custom data and profile inputs.</summary>
public sealed record CharacterSheetOwnerSource(string Kind, Guid SourceId, string Name, string SourceBook,
    string SourceXml, string CatalogDigest, string CustomDataDigest, string ProfileDigest, string PolicyDigest);
