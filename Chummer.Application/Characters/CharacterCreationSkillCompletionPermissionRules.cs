using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Validates a permission inventory against an already admitted skill authority.
/// Permissions are catalog identities, not numeric goals or recommendations. This
/// check does not admit source authority or grant permission to mutate a character.
/// </summary>
public static class CharacterCreationSkillCompletionPermissionRules
{
    /// <summary>
    /// Returns a detached, read-only inventory in the original order, or false with an empty
    /// snapshot. The bound is the authority's active + knowledge + group cardinality;
    /// identities must be exact and unique. Count and enumeration must agree.
    /// Cancellation is observed while reading both catalogs and permissions.
    /// </summary>
    public static bool TrySnapshot(
        CharacterCreationSkillsAuthority authority,
        IReadOnlyList<CharacterCreationSkillCompletionTarget>? permissions,
        out IReadOnlyList<CharacterCreationSkillCompletionTarget> snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        snapshot = [];
        if (authority?.ActiveSkills is null || authority.KnowledgeSkills is null
            || authority.SkillGroups is null || permissions is null)
            return false;

        int activeCount = authority.ActiveSkills.Count;
        int knowledgeCount = authority.KnowledgeSkills.Count;
        int groupCount = authority.SkillGroups.Count;
        if (activeCount < 0 || knowledgeCount < 0 || groupCount < 0)
            return false;
        long maximumPermissions = (long)activeCount + knowledgeCount + groupCount;
        int permissionCount = permissions.Count;
        if (permissionCount < 0 || permissionCount > maximumPermissions)
            return false;

        var known = new HashSet<(string Kind, string SourceId)>();
        if (!TryVisit(authority.ActiveSkills, activeCount, skill =>
                skill.Kind == CharacterCreationSkillKinds.Active
                && AddKnown(skill.Kind, skill.SourceSkillId), cancellationToken)
            || !TryVisit(authority.KnowledgeSkills, knowledgeCount, skill =>
                skill.Kind == CharacterCreationSkillKinds.Knowledge
                && AddKnown(skill.Kind, skill.SourceSkillId), cancellationToken)
            || !TryVisit(authority.SkillGroups, groupCount, group =>
                AddKnown(CharacterCreationSkillCompletionKinds.Group, group.GroupId), cancellationToken))
            return false;

        var seen = new HashSet<(string Kind, string SourceId)>();
        var copy = new List<CharacterCreationSkillCompletionTarget>();
        if (!TryVisit(permissions, permissionCount, target =>
            {
                var key = (target.Kind, target.SourceId);
                if (!known.Contains(key) || !seen.Add(key)) return false;
                copy.Add(target);
                return true;
            }, cancellationToken))
            return false;
        snapshot = copy.AsReadOnly();
        return true;

        bool AddKnown(string kind, string sourceId) =>
            !string.IsNullOrWhiteSpace(sourceId) && known.Add((kind, sourceId));
    }

    // Every copy checks its declared count; numeric goals also retain their fixed
    // operation bound. Do not use ToArray on a caller collection: ICollection.CopyTo
    // and Count need not match enumeration.
    internal static bool TrySnapshotBounded<T>(IReadOnlyList<T>? values, int maximumCount,
        out IReadOnlyList<T> snapshot, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        snapshot = [];
        if (values is null) return false;
        int count = values.Count;
        if (count < 0 || count > maximumCount) return false;
        var copy = new List<T>();
        if (!TryVisit(values, count, value => { copy.Add(value); return true; }, cancellationToken))
            return false;
        snapshot = copy.AsReadOnly();
        return true;
    }

    // Capture the relevant catalogs once, including their nested collections,
    // before any enclosing JSON detachment can normalize a dishonest Count or
    // enumerate forever. This is structural copying, not source admission: the
    // ordinary Core authority/digest checks still evaluate this same observation.
    internal static bool TrySnapshotAuthorityCatalogs(CharacterCreationSkillsAuthority? authority,
        out CharacterCreationSkillsAuthority snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        snapshot = null!;
        if (authority is null
            || !TrySnapshotSkills(authority.ActiveSkills, out var active, cancellationToken)
            || !TrySnapshotSkills(authority.KnowledgeSkills, out var knowledge, cancellationToken)
            || !TrySnapshotBounded(authority.SkillGroups, int.MaxValue, out var groups, cancellationToken))
            return false;
        var groupCopy = new List<CharacterCreationSkillGroupCatalogEntry>();
        foreach (var group in groups)
        {
            if (!TrySnapshotBounded(group.MemberSkillSourceIds, int.MaxValue, out var members, cancellationToken)
                || !TrySnapshotBounded(group.SourceAnchorIds, int.MaxValue, out var anchors, cancellationToken))
                return false;
            groupCopy.Add(group with { MemberSkillSourceIds = members, SourceAnchorIds = anchors });
        }
        snapshot = authority with { ActiveSkills = active, KnowledgeSkills = knowledge, SkillGroups = groupCopy.AsReadOnly() };
        return true;
    }

    private static bool TrySnapshotSkills(IReadOnlyList<CharacterCreationSkillCatalogEntry>? skills,
        out IReadOnlyList<CharacterCreationSkillCatalogEntry> snapshot, CancellationToken cancellationToken)
    {
        snapshot = [];
        if (!TrySnapshotBounded(skills, int.MaxValue, out var rows, cancellationToken)) return false;
        var copy = new List<CharacterCreationSkillCatalogEntry>();
        foreach (var skill in rows)
        {
            if (!TrySnapshotBounded(skill.Specializations, int.MaxValue, out var specializations, cancellationToken)
                || !TrySnapshotBounded(skill.SourceAnchorIds, int.MaxValue, out var anchors, cancellationToken))
                return false;
            copy.Add(skill with { Specializations = specializations, SourceAnchorIds = anchors });
        }
        snapshot = copy.AsReadOnly();
        return true;
    }

    private static bool TryVisit<T>(IReadOnlyList<T> values, int count, Func<T, bool> accept,
        CancellationToken cancellationToken) where T : class
    {
        int read = 0;
        using var enumerator = values.GetEnumerator();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool hasNext = enumerator.MoveNext();
            cancellationToken.ThrowIfCancellationRequested();
            if (!hasNext) return read == count;
            if (read >= count || enumerator.Current is not { } value || !accept(value))
                return false;
            read++;
        }
    }
}
