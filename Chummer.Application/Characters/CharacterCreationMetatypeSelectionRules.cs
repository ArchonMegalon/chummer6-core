using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static class CharacterCreationMetatypeSelectionRules
{
    public static bool IsValid(CharacterCreationMetatypeSelectionIdentity? identity) => identity is null
        || Canonical(identity.ParentMetatypeOptionId) && (identity.MetavariantOptionId is null
            || Canonical(identity.MetavariantOptionId) && identity.MetavariantOptionId != identity.ParentMetatypeOptionId);

    private static bool Canonical(string? value) => Guid.TryParseExact(value, "D", out var id)
        && id != Guid.Empty && value == id.ToString("D");

    public static bool Matches(CharacterCreationMetatypeSelectionIdentity? supplied,
        CharacterCreationMetatypeSelectionIdentity? projected, string optionId)
        => IsValid(supplied) && (projected is null
            ? supplied is null || supplied.MetavariantOptionId is null && supplied.ParentMetatypeOptionId == optionId
            : supplied == projected && IsValid(projected) && projected.MetavariantOptionId == optionId);

    public static CharacterCreationMetatypeOptionProjection? Resolve(CharacterCreationMetatypeCatalogAuthority catalog,
        string requested, CharacterCreationMetatypeSelectionIdentity? identity)
    {
        if (!catalog.IsAuthoritative || catalog.Blockers is not { Count: 0 }
            || catalog.Options is null || !IsValid(identity)) return null;
        // The collection is part of the contract. A malformed child without its
        // pair cannot masquerade as a base, even when its GUID is globally unique.
        bool variant = identity?.MetavariantOptionId is not null;
        var candidates = variant ? catalog.MetavariantOptions ?? [] : catalog.Options;
        var rows = candidates
            .Where(row => row is not null)
            .Where(row => (row.Label == requested || row.OptionId == requested)
                && Matches(identity, row.MetatypeIdentity, row.OptionId)).Take(2).ToArray();
        if (rows is not [{ IsEnabled: true, Blockers.Count: 0 } selected]) return null;
        if (variant != (selected.MetatypeIdentity?.MetavariantOptionId is not null)) return null;
        if (selected.MetatypeIdentity is { } pair)
        {
            var parents = catalog.Options.Where(row => row is not null && row.OptionId == pair.ParentMetatypeOptionId).Take(2).ToArray();
            if (parents is not [{ IsEnabled: true, Blockers.Count: 0, MetatypeIdentity: null } parent]
                || parent.Label != selected.ParentMetatypeLabel || parent.Category != selected.ParentMetatypeCategory) return null;
        }
        return selected;
    }

    // Saved aliases are validated together; a variant GUID never substitutes for a parent GUID.
    public static CharacterCreationMetatypeOptionProjection? ResolveSaved(CharacterCreationMetatypeCatalogAuthority catalog,
        XElement character, bool allowLifeModuleVariant)
    {
        if (!CharacterMetatypeIdentity.TryRead(character, out var name, out var id, out var childName, out var childId)) return null;
        if (childId is null || childId == Guid.Empty)
        {
            if (childName is not ("" or "None")) return null;
            var row = Resolve(catalog, id?.ToString("D") ?? name, null);
            return row?.Label == name ? row : null;
        }
        if (!allowLifeModuleVariant || id is null || childName.Length == 0) return null;
        var selected = Resolve(catalog, childId.Value.ToString("D"), new(id.Value.ToString("D"), childId.Value.ToString("D")));
        return selected?.Label == childName && selected.ParentMetatypeLabel == name ? selected : null;
    }
}
