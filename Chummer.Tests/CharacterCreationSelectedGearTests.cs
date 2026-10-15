using System.Text.Json;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationSelectedGearTests
{
    [TestMethod]
    public void Selected_gear_retains_exact_detached_source_rows_and_reprices_original_and_proposed_baskets()
    {
        using var context = ReadyContext.Create(true);
        var full = Authority(context);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var available = full.Options.Where(option => option.IsSelectable && option.PackageCost > 0m).Take(2).ToArray();
        Assert.AreEqual(2, available.Length);
        var originalIds = new List<string> { available[0].OptionId };
        var proposedIds = new List<string> { available[1].OptionId, available[0].OptionId };
        var mutableOptions = full.Options.ToList();
        full = Seal(full with { Options = mutableOptions });
        Assert.IsTrue(CharacterCreationGearRules.TryCaptureSelectedAuthority(full, originalIds, proposedIds, out var witness));
        Assert.IsNotNull(witness);
        Assert.IsTrue(CharacterCreationGearRules.IsValidSelectedAuthority(witness));
        Assert.AreEqual(full.AuthorityDigest, witness.ProvenanceAuthorityDigest);
        Assert.AreNotEqual(full.AuthorityDigest, witness.WitnessDigest);
        Assert.AreEqual(2, witness.Options.Count);
        Assert.AreEqual(JsonSerializer.Serialize(full.Options.Where(row => proposedIds.Contains(row.OptionId))
            .OrderBy(row => row.OptionId, StringComparer.Ordinal)), JsonSerializer.Serialize(witness.Options));
        Assert.AreEqual(JsonSerializer.Serialize(full.SourceAnchorIds), JsonSerializer.Serialize(witness.SourceAnchorIds));
        Assert.AreEqual(full.MaximumAvailability, witness.MaximumAvailability);
        Assert.AreEqual(full.MaximumQuantityPerLine, witness.MaximumQuantityPerLine);
        Assert.IsTrue(JsonSerializer.SerializeToUtf8Bytes(witness).Length
            < JsonSerializer.SerializeToUtf8Bytes(full).Length / 2);
        CharacterCreationGearSelection[] original = [new(available[0].OptionId, 1)];
        CharacterCreationGearSelection[] proposed = [new(available[1].OptionId, 2), new(available[0].OptionId, 3)];
        AssertSameProjection(full, witness, original, 1_000_000m);
        AssertSameProjection(full, witness, proposed, 1_000_000m);
        AssertSameProjection(full, witness, proposed, 0m);
        AssertSameProjection(full, witness, [new(available[0].OptionId, 0)], 1_000_000m);
        AssertSameProjection(full, witness, [new(available[0].OptionId, full.MaximumQuantityPerLine + 1)], 1_000_000m);
        AssertSameProjection(full, witness, [new(available[0].OptionId, 1), new(available[0].OptionId, 1)], 1_000_000m);
        AssertSameProjection(full, witness, null, 1_000_000m);
        AssertSameProjection(full, witness, [], -1m);

        string captured = JsonSerializer.Serialize(witness);
        originalIds.Clear();
        proposedIds.Clear();
        mutableOptions.Clear();
        Assert.AreEqual(captured, JsonSerializer.Serialize(witness));
        Assert.IsTrue(CharacterCreationGearRules.IsValidSelectedAuthority(witness));
        var cold = JsonSerializer.Deserialize<CharacterCreationSelectedGearAuthority>(captured)!;
        Assert.IsTrue(CharacterCreationGearRules.IsValidSelectedAuthority(cold));
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Selected_gear_empty_closure_and_unsupported_rows_keep_ordinary_rules_without_fake_catalog_entries()
    {
        using var context = ReadyContext.Create(true);
        var full = Authority(context);
        Assert.IsTrue(CharacterCreationGearRules.TryCaptureSelectedAuthority(full, [], [], out var empty));
        Assert.IsNotNull(empty);
        Assert.AreEqual(0, empty.Options.Count);
        AssertSameProjection(full, empty, [], 500m);
        Assert.IsFalse(CharacterCreationGearRules.IsValidAuthority(Seal(full with { Options = [] })),
            "The new historical entry point must not weaken ordinary full-catalog admission.");
        var unsupported = full.Options.First(row => !row.IsSelectable);
        Assert.IsTrue(CharacterCreationGearRules.TryCaptureSelectedAuthority(full, [], [unsupported.OptionId], out var selected));
        AssertSameProjection(full, selected!, [new(unsupported.OptionId, 1)], 500m);
        var omitted = full.Options.First(row => row.IsSelectable);
        Assert.IsFalse(CharacterCreationGearRules.TryProjectSelectedBasket([new(omitted.OptionId, 1)], empty,
            1_000_000m, out _, out _, out var blockers));
        CollectionAssert.Contains(blockers, CharacterCreationGearBlockers.InvalidOption);
    }

    [TestMethod]
    public void Selected_gear_rejects_invalid_full_catalog_and_malformed_or_resealed_witnesses()
    {
        using var context = ReadyContext.Create(true);
        var full = Authority(context);
        var selected = full.Options.First(row => row.IsSelectable);
        var unused = full.Options.First(row => row.OptionId != selected.OptionId);
        Assert.IsTrue(CharacterCreationGearRules.TryCaptureSelectedAuthority(full, [], [selected.OptionId], out var witness));
        Assert.IsNotNull(witness);
        var badCatalogs = new[]
        {
            Seal(full with { Options = full.Options.Append(unused).ToArray() }),
            Seal(full with { Options = full.Options.Select(row => row.OptionId == unused.OptionId
                ? row with { SourceNodeXml = row.SourceNodeXml + " " } : row).ToArray() }),
            Seal(full with { Options = full.Options.Select(row => row.OptionId == unused.OptionId
                ? row with { Blockers = null! } : row).ToArray() }),
            Seal(full with { Blockers = null! })
        };
        foreach (var bad in badCatalogs)
        {
            Assert.IsFalse(CharacterCreationGearRules.TryCaptureSelectedAuthority(bad, [], [selected.OptionId], out var rejected));
            Assert.IsNull(rejected, "Unused invalid source rows must not vanish during capture.");
        }
        Assert.IsFalse(CharacterCreationGearRules.TryCaptureSelectedAuthority(full, [], ["missing-option"], out _));
        Assert.IsFalse(CharacterCreationGearRules.TryCaptureSelectedAuthority(full, [], [selected.OptionId, selected.OptionId], out _));
        var malformed = new[]
        {
            witness with { Schema = "unknown" },
            witness with { ProvenanceAuthorityDigest = "not-a-digest" },
            witness with { OriginalOptionIds = null! },
            witness with { Options = [] },
            witness with { Options = witness.Options.Append(unused).ToArray() },
            witness with { Options = [selected, selected] },
            witness with { Options = [selected with { SourceNodeXml = selected.SourceNodeXml + " " }] },
            witness with { Options = [selected with { Blockers = null! }] },
            witness with { Options = [selected with { SourceAnchorIds = null! }] },
            witness with { Options = [null!] },
            witness with { SourceAnchorIds = [] }
        };
        foreach (var invalid in malformed)
        {
            var resealed = invalid with { WitnessDigest = CharacterCreationGearRules.ComputeSelectedAuthorityDigest(invalid) };
            Assert.IsFalse(CharacterCreationGearRules.IsValidSelectedAuthority(resealed));
            Assert.IsFalse(CharacterCreationGearRules.TryProjectSelectedBasket([new(selected.OptionId, 1)], resealed,
                1_000_000m, out _, out _, out var blockers));
            CollectionAssert.Contains(blockers, CharacterCreationGearBlockers.AuthorityUnavailable);
        }
        Assert.IsFalse(CharacterCreationGearRules.IsValidSelectedAuthority(witness with
        { WitnessDigest = "sha256:" + new string('0', 64) }));
    }

    private static CharacterCreationGearAuthority Authority(ReadyContext context)
    {
        var source = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationGearAuthority(out var authority));
        Assert.IsTrue(CharacterCreationGearRules.IsValidAuthority(authority));
        return authority;
    }

    private static CharacterCreationGearAuthority Seal(CharacterCreationGearAuthority value)
        => value with { AuthorityDigest = CharacterCreationGearRules.ComputeAuthorityDigest(value) };

    private static void AssertSameProjection(CharacterCreationGearAuthority full, CharacterCreationSelectedGearAuthority witness,
        IReadOnlyList<CharacterCreationGearSelection>? basket, decimal startingNuyen)
    {
        bool expected = CharacterCreationGearRules.TryProjectBasket(basket, full, startingNuyen,
            out var fullLines, out var fullBudget, out var fullBlockers);
        bool actual = CharacterCreationGearRules.TryProjectSelectedBasket(basket, witness, startingNuyen,
            out var lines, out var budget, out var blockers);
        Assert.AreEqual(expected, actual);
        Assert.AreEqual(JsonSerializer.Serialize(fullLines), JsonSerializer.Serialize(lines));
        Assert.AreEqual(JsonSerializer.Serialize(fullBudget), JsonSerializer.Serialize(budget));
        CollectionAssert.AreEqual(fullBlockers, blockers);
    }
}
