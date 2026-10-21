using System.Collections;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationSkillsServiceTests
{
    [TestMethod]
    public void Skill_permission_snapshot_is_exact_unique_detached_and_keeps_caller_order()
    {
        var authority = CreateSkillsAuthority();
        CharacterCreationSkillCompletionTarget[] permissions =
        [new(CharacterCreationSkillKinds.Knowledge, LanguageId),
            new(CharacterCreationSkillCompletionKinds.Group, authority.SkillGroups[0].GroupId),
            new(CharacterCreationSkillKinds.Active, ActiveIds[0])];
        var before = permissions.ToArray();
        var collection = new PermissionProbeList<CharacterCreationSkillCompletionTarget>(permissions, permissions.Length);
        Assert.IsTrue(CharacterCreationSkillCompletionPermissionRules.TrySnapshot(authority, collection, out var snapshot));
        CollectionAssert.AreEqual(before, snapshot.ToArray());
        Assert.IsTrue(collection.Disposed);
        permissions[0] = new("unknown", "unknown");
        CollectionAssert.AreEqual(before, snapshot.ToArray());
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(snapshot));
        Assert.IsFalse(snapshot is CharacterCreationSkillCompletionTarget[]);
        var exposed = (IList<CharacterCreationSkillCompletionTarget>)snapshot;
        Assert.IsTrue(exposed.IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => exposed[0] = new("unknown", "unknown"));
        CollectionAssert.AreEqual(before, snapshot.ToArray());
    }

    [TestMethod]
    public void Skill_permission_snapshot_rejects_null_duplicate_unknown_and_inconsistent_enumeration()
    {
        var authority = CreateSkillsAuthority();
        var first = new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, ActiveIds[0]);
        var second = new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, ActiveIds[1]);
        IReadOnlyList<CharacterCreationSkillCompletionTarget>?[] invalid =
        [null, [null!], [first, first], [new("ACTIVE", first.SourceId)],
            [new(first.Kind, "unknown")], [new(first.Kind, LanguageId)], [new(first.Kind, " ")],
            new PermissionProbeList<CharacterCreationSkillCompletionTarget>([first], -1),
            new PermissionProbeList<CharacterCreationSkillCompletionTarget>([first], int.MaxValue),
            new PermissionProbeList<CharacterCreationSkillCompletionTarget>([first], 0),
            new PermissionProbeList<CharacterCreationSkillCompletionTarget>([first], 2),
            new PermissionProbeList<CharacterCreationSkillCompletionTarget>([first, second], 1)];
        foreach (var permissions in invalid)
        {
            Assert.IsFalse(CharacterCreationSkillCompletionPermissionRules.TrySnapshot(authority, permissions, out var snapshot));
            Assert.AreEqual(0, snapshot.Count);
        }
        var endless = new PermissionProbeList<CharacterCreationSkillCompletionTarget>([first], 0, repeat: true);
        Assert.IsFalse(CharacterCreationSkillCompletionPermissionRules.TrySnapshot(authority, endless, out _));
        Assert.AreEqual(1, endless.MoveNextCalls);
        Assert.IsTrue(endless.Disposed);
        var overflowingCounts = authority with
        {
            ActiveSkills = new PermissionProbeList<CharacterCreationSkillCatalogEntry>([], int.MaxValue),
            KnowledgeSkills = new PermissionProbeList<CharacterCreationSkillCatalogEntry>([], int.MaxValue)
        };
        Assert.IsFalse(CharacterCreationSkillCompletionPermissionRules.TrySnapshot(overflowingCounts, [first], out _));
    }

    [TestMethod]
    public void Skill_permission_snapshot_observes_cancellation_before_and_during_catalog_and_permission_reads()
    {
        var authority = CreateSkillsAuthority();
        var first = new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, ActiveIds[0]);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            CharacterCreationSkillCompletionPermissionRules.TrySnapshot(authority, [first], out _, canceled.Token));
        using var duringPermissions = new CancellationTokenSource();
        var permissions = new PermissionProbeList<CharacterCreationSkillCompletionTarget>([first], 1,
            onMoveNext: duringPermissions.Cancel);
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            CharacterCreationSkillCompletionPermissionRules.TrySnapshot(authority, permissions, out _, duringPermissions.Token));
        Assert.AreEqual(1, permissions.MoveNextCalls);
        Assert.IsTrue(permissions.Disposed);
        using var duringCatalog = new CancellationTokenSource();
        var catalog = new PermissionProbeList<CharacterCreationSkillCatalogEntry>(authority.ActiveSkills,
            authority.ActiveSkills.Count, onMoveNext: duringCatalog.Cancel);
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            CharacterCreationSkillCompletionPermissionRules.TrySnapshot(authority with { ActiveSkills = catalog },
                [first], out _, duringCatalog.Token));
        Assert.IsTrue(catalog.Disposed);
    }

    [TestMethod]
    public void Skill_planner_bounds_permission_and_numeric_goal_reads_before_copying()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var first = new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, ActiveIds[0]);
            var request = SkillCompletionRequest(attributes, skills, [first]) with { Targets = [] };
            CharacterCreationSkillCompletionRequest[] invalid =
            [request with { PermittedTargetOrder = new PermissionProbeList<CharacterCreationSkillCompletionTarget>([first], 0, repeat: true) },
                request with { Targets = new PermissionProbeList<CharacterCreationSkillCompletionRatingTarget>([new(first.Kind, first.SourceId, 0)], 0, repeat: true) },
                request with { Targets = new PermissionProbeList<CharacterCreationSkillCompletionRatingTarget>([], 1) }];
            foreach (var candidate in invalid)
            {
                var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills, attributes.PendingDraft!.Allocations, candidate);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, result.Outcome);
                CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.RequestInvalid);
            }
            using var canceled = new CancellationTokenSource();
            var canceling = request with
            {
                Targets = new PermissionProbeList<CharacterCreationSkillCompletionRatingTarget>([new(first.Kind, first.SourceId, 0)], 1,
                    onMoveNext: canceled.Cancel)
            };
            Assert.ThrowsExactly<OperationCanceledException>(() => CharacterCreationSkillCompletionPlanner.Plan(
                attributes, skills, attributes.PendingDraft!.Allocations, canceling, canceled.Token));
        });
    }

    // Deliberately implements ICollection too: the validator must enumerate with a
    // bound rather than delegate copying to this untrusted collection's CopyTo.
    internal sealed class PermissionProbeList<T>(IReadOnlyList<T> items, int count,
        bool repeat = false, Action? onMoveNext = null, int? maximumMoveNextCalls = null,
        IReadOnlyList<T>? subsequentItems = null) : IReadOnlyList<T>, ICollection<T>
    {
        public int Count => count;
        public bool IsReadOnly => true;
        public T this[int index] => throw new InvalidOperationException("Unexpected index access");
        public int MoveNextCalls { get; private set; }
        public int EnumerationCalls { get; private set; }
        public bool Disposed { get; private set; }
        public IEnumerator<T> GetEnumerator()
        {
            try
            {
                var observed = EnumerationCalls++ == 0 ? items : subsequentItems ?? items;
                int index = 0;
                while (true)
                {
                    MoveNextCalls++;
                    // Even an old unbounded caller must fail instead of hanging
                    // the suite. A correct snapshot stops at Count + one probe.
                    if (MoveNextCalls > (maximumMoveNextCalls ?? (repeat ? 16 : int.MaxValue)))
                        throw new InvalidOperationException("Collection read exceeded the test probe limit");
                    onMoveNext?.Invoke();
                    if (index == observed.Count)
                    {
                        if (!repeat || observed.Count == 0) yield break;
                        index = 0;
                    }
                    yield return observed[index++];
                }
            }
            finally { Disposed = true; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void CopyTo(T[] array, int arrayIndex) => throw new InvalidOperationException("Unexpected CopyTo");
        public void Add(T item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(T item) => throw new NotSupportedException();
        public bool Remove(T item) => throw new NotSupportedException();
    }
}
