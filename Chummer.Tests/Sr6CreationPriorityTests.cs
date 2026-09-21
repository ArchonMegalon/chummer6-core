using Chummer.Contracts.Characters;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Sr6CreationPriorityTests
{
    private readonly Sr6CharacterCreationProvider _provider = new();

    [TestMethod]
    public void Priority_projects_sr6_budgets_by_category_not_input_order()
    {
        var request = Request("Priority", "ABCDE");
        var result = _provider.EvaluatePriorities(request);
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(new Sr6CreationPriorityBudget(24, 24, 150_000, 4, "E"), result.Budget);
        Assert.IsNull(result.SumToTenTotal);
        Assert.AreEqual(0, result.Blockers.Count);

        var reordered = _provider.EvaluatePriorities(request with
        {
            Assignments = request.Assignments.Reverse().ToArray()
        });
        Assert.AreEqual(result.Budget, reordered.Budget);
    }

    [TestMethod]
    public void Sum_to_ten_allows_repeated_ranks_only_with_companion_enabled()
    {
        var request = Request("SumtoTen", "CCCCC");
        AssertRejected(_provider.EvaluatePriorities(request), Sr6CreationPriorityBlockers.CompanionRequired);
        var result = _provider.EvaluatePriorities(request, companionEnabled: true);
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(10, result.SumToTenTotal);
        Assert.AreEqual(new Sr6CreationPriorityBudget(12, 20, 150_000, 9, "C"), result.Budget);
        AssertRejected(_provider.EvaluatePriorities(request with { BuildMethod = "Priority" }),
            Sr6CreationPriorityBlockers.UniqueRanksRequired);
    }

    [TestMethod]
    [DataRow("AAAAA", 20)]
    [DataRow("EEEEE", 0)]
    [DataRow("ABBEE", 10)]
    [DataRow("AACEE", 10)]
    public void Sum_to_ten_never_substitutes_unique_rank_rules(string ranks, int total)
    {
        var result = _provider.EvaluatePriorities(Request("SumtoTen", ranks), companionEnabled: true);
        Assert.AreEqual(total, result.SumToTenTotal);
        Assert.AreEqual(total == 10, result.IsValid);
        if (total != 10)
            AssertRejected(result, Sr6CreationPriorityBlockers.SumToTenMismatch);
    }

    [TestMethod]
    [DataRow("PointBuy")]
    [DataRow("LifePath")]
    [DataRow("Karma")]
    [DataRow("LifeModule")]
    [DataRow("SumToTen")]
    [DataRow("priority")]
    [DataRow("")]
    public void Alternative_or_noncanonical_method_never_receives_a_priority_budget(string method)
        => AssertRejected(_provider.EvaluatePriorities(Request(method, "ABCDE"), companionEnabled: true),
            Sr6CreationPriorityBlockers.MethodUnsupported);

    [TestMethod]
    [DataRow("sr5")]
    [DataRow("sr4")]
    [DataRow("SR6")]
    [DataRow("")]
    public void Edition_mismatch_never_uses_sr6_tables(string ruleset)
        => AssertRejected(_provider.EvaluatePriorities(Request("Priority", "ABCDE") with { RulesetId = ruleset }),
            Sr6CreationPriorityBlockers.RulesetMismatch);

    [TestMethod]
    public void Missing_unknown_and_duplicate_categories_produce_no_partial_budget()
    {
        var request = Request("Priority", "ABCDE");
        AssertRejected(_provider.EvaluatePriorities(request with { Assignments = request.Assignments.Take(4).ToArray() }),
            Sr6CreationPriorityBlockers.CategoriesInvalid);
        AssertRejected(_provider.EvaluatePriorities(request with
        {
            Assignments = request.Assignments.Append(request.Assignments[0]).ToArray()
        }), Sr6CreationPriorityBlockers.CategoriesInvalid);
        foreach (string category in new[] { "unknown", "attributes", "Attributes", "" })
        {
            var choices = request.Assignments.ToArray();
            choices[4] = choices[4] with { CategoryId = category };
            AssertRejected(_provider.EvaluatePriorities(request with { Assignments = choices }),
                Sr6CreationPriorityBlockers.CategoriesInvalid);
        }
        AssertRejected(_provider.EvaluatePriorities(request with { Assignments = null! }),
            Sr6CreationPriorityBlockers.CategoriesInvalid);
        var nullChoice = request.Assignments.ToArray();
        nullChoice[0] = null!;
        AssertRejected(_provider.EvaluatePriorities(request with { Assignments = nullChoice }),
            Sr6CreationPriorityBlockers.CategoriesInvalid);
    }

    [TestMethod]
    [DataRow("F")]
    [DataRow("a")]
    [DataRow(" A")]
    [DataRow("AA")]
    [DataRow("")]
    [DataRow(null)]
    public void Malformed_rank_is_not_silently_changed_to_e(string? rank)
    {
        var request = Request("Priority", "ABCDE");
        var choices = request.Assignments.ToArray();
        choices[0] = choices[0] with { Rank = rank! };
        AssertRejected(_provider.EvaluatePriorities(request with { Assignments = choices }),
            Sr6CreationPriorityBlockers.RankInvalid);
    }

    private static Sr6CreationPriorityRequest Request(string method, string ranks)
        => new("sr6", method, new[] { "attributes", "skills", "resources", "heritage", "talent" }
            .Select((category, index) => new Sr6CreationPriorityChoice(category, ranks[index].ToString())).ToArray());

    private static void AssertRejected(Sr6CreationPriorityResult result, string blocker)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.Budget);
        CollectionAssert.Contains(result.Blockers.ToArray(), blocker);
    }
}
