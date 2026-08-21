namespace Chummer.Contracts.Characters;

public enum CharacterSustainedObjectAction
{
    Update,
    Delete
}

public enum CharacterPsycheActiveSurface
{
    Magician,
    Technomancer
}

public sealed record CharacterSustainedObjectIdentity(
    string LinkedObjectType,
    Guid LinkedObjectId,
    int Occurrence);

public sealed record CharacterSustainedObjectBasis(
    CharacterSustainedObjectIdentity Identity,
    string DisplayName,
    int Force,
    int NetHits,
    bool SelfSustained,
    bool SelfSustainedEditable);

public sealed record CharacterSustainedObjectState(
    CharacterSustainedObjectIdentity Identity,
    string DisplayName,
    int Force,
    int NetHits,
    bool SelfSustained,
    bool SelfSustainedEditable);

public sealed record CharacterPsycheActiveState(
    bool CareerMode,
    bool Active,
    bool MagicianControlAvailable,
    bool TechnomancerControlAvailable);

public static class CharacterSustainedObjectRules
{
    public const int MinimumForce = 0;
    public const int MaximumForce = 100;
    public const int MinimumNetHits = 0;
    public const int MaximumNetHits = 100;

    public static bool TryProjectAll(
        IReadOnlyList<CharacterSustainedObjectBasis> basis,
        out IReadOnlyList<CharacterSustainedObjectState>? states)
    {
        ArgumentNullException.ThrowIfNull(basis);
        states = null;

        var nextOccurrence = new Dictionary<(string Type, Guid Id), int>();
        var projected = new List<CharacterSustainedObjectState>(basis.Count);
        foreach (CharacterSustainedObjectBasis item in basis)
        {
            CharacterSustainedObjectIdentity identity = item.Identity;
            if (!IsSupportedLinkedObjectType(identity.LinkedObjectType)
                || identity.LinkedObjectId == Guid.Empty
                || identity.Occurrence < 0
                || string.IsNullOrWhiteSpace(item.DisplayName)
                || item.Force is < MinimumForce or > MaximumForce
                || item.NetHits is < MinimumNetHits or > MaximumNetHits
                || item.SelfSustainedEditable
                    != !string.Equals(identity.LinkedObjectType, "CritterPower", StringComparison.Ordinal))
            {
                return false;
            }

            var key = (identity.LinkedObjectType, identity.LinkedObjectId);
            int expectedOccurrence = nextOccurrence.GetValueOrDefault(key);
            if (identity.Occurrence != expectedOccurrence)
            {
                return false;
            }
            nextOccurrence[key] = expectedOccurrence + 1;
            projected.Add(new CharacterSustainedObjectState(
                identity,
                item.DisplayName.Trim(),
                item.Force,
                item.NetHits,
                item.SelfSustained,
                item.SelfSustainedEditable));
        }

        states = projected;
        return true;
    }

    public static bool CanUpdate(
        CharacterSustainedObjectState state,
        int force,
        int netHits,
        bool selfSustained)
    {
        ArgumentNullException.ThrowIfNull(state);
        return force is >= MinimumForce and <= MaximumForce
            && netHits is >= MinimumNetHits and <= MaximumNetHits
            && (state.SelfSustainedEditable || selfSustained == state.SelfSustained);
    }

    public static bool CanDelete(bool confirmed) => confirmed;

    public static bool CanSetPsycheActive(
        CharacterPsycheActiveState state,
        CharacterPsycheActiveSurface surface,
        bool value)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.CareerMode || value == state.Active)
        {
            return false;
        }

        return surface switch
        {
            CharacterPsycheActiveSurface.Magician => state.MagicianControlAvailable,
            CharacterPsycheActiveSurface.Technomancer => state.TechnomancerControlAvailable,
            _ => false
        };
    }

    public static bool IsSupportedLinkedObjectType(string? linkedObjectType)
        => linkedObjectType is "Spell" or "ComplexForm" or "CritterPower";
}
