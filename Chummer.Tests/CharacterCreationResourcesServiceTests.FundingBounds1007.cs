using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationResourcesServiceTests
{
    [TestMethod]
    [DataRow(10, "2000", 25, 25)]
    [DataRow(10, "2500.5", 0, 25)]
    [DataRow(0, "0.125", 0, 0)]
    [DataRow(int.MaxValue, "2500.5", 25, 25)]
    [DataRow(int.MaxValue, "0.125", 35, 25)]
    public void FundingBounds1007_catalog_preserves_prices_disabled_choices_and_quality_gains(
        int maximum, string rateText, int available, int initial)
    {
        decimal rate = decimal.Parse(rateText, CultureInfo.InvariantCulture);
        var authority = FundingAuthority1007(maximum, rate);
        var priority = authority.PriorityOptions.Single(row => row.Rank == "D");
        var options = Catalogue1007(authority, priority, available, initial);
        int end = Math.Min(maximum, Math.Max(available, initial));
        Assert.AreEqual(end + 1, options.Length);
        for (int investment = 0; investment <= end; investment++)
        {
            var expected = new CharacterCreationResourceAllocationOption(
                "karma:" + investment.ToString(CultureInfo.InvariantCulture), investment,
                investment * rate, priority.BasePriorityNuyen + investment * rate,
                investment <= available,
                investment <= available ? [] : [CharacterCreationResourcesBlockers.InsufficientCreationKarma],
                priority.SourceAnchorIds.Concat(CharacterCreationResourcesSourceAnchors.All)
                    .Distinct(StringComparer.Ordinal).OrderBy(row => row, StringComparer.Ordinal).ToArray(), string.Empty);
            expected = expected with { OptionDigest = CharacterCreationResourcesRules.ComputeAllocationOptionDigest(expected) };
            Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(options[investment]));
        }
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(6)]
    public void FundingBounds1007_actual_selected_pricer_refuses_range_with_same_fixture_positive_control(int investment)
    {
        var authority = FundingAuthority1007(5, 2500.5m);
        var priority = authority.PriorityOptions.Single(row => row.Rank == "D");
        var positive = PriceFunding1007(authority, priority, 25, 3, 1234.5m, 25);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, positive.GetProperty("Outcome").GetString());
        Assert.AreEqual(3, positive.GetProperty("Value").GetProperty("SelectedOption").GetProperty("KarmaInvestment").GetInt32());
        var refused = PriceFunding1007(authority, priority, 25, investment, 1234.5m, 25);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Invalid, refused.GetProperty("Outcome").GetString());
        Assert.AreEqual(JsonValueKind.Null, refused.GetProperty("Value").ValueKind);
        Assert.IsTrue(refused.GetProperty("Blockers").EnumerateArray()
            .Any(row => row.GetString() == CharacterCreationResourcesBlockers.InvalidOption));
    }

    [TestMethod]
    public void FundingBounds1007_single_lookup_reaches_Int32_endpoint_without_enumeration_and_refuses_unaffordable_choice()
    {
        var authority = FundingAuthority1007(int.MaxValue, 0.125m);
        var priority = authority.PriorityOptions.Single(row => row.Rank == "D");
        var endpoint = PriceFunding1007(authority, priority, int.MaxValue, int.MaxValue, 1234.5m, null, false);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, endpoint.GetProperty("Outcome").GetString());
        var value = endpoint.GetProperty("Value");
        Assert.AreEqual(1, value.GetProperty("Options").GetArrayLength());
        Assert.AreEqual(int.MaxValue * 0.125m, value.GetProperty("SelectedOption").GetProperty("NuyenFromKarma").GetDecimal());
        Assert.AreEqual(0, value.GetProperty("RemainingCreationKarma").GetInt32());
        // Same authority and choice, real limited wallet: this is a budget refusal,
        // not stale authority, a changed profile, or a catalogue allocation.
        var blocked = PriceFunding1007(authority, priority, 25, int.MaxValue, 0m, 25);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, blocked.GetProperty("Outcome").GetString());
        Assert.IsTrue(blocked.GetProperty("Blockers").EnumerateArray()
            .Any(row => row.GetString() == CharacterCreationResourcesBlockers.InsufficientCreationKarma));
        Assert.AreEqual(26, blocked.GetProperty("Value").GetProperty("Options").GetArrayLength());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FundingBounds1007_decimal_addition_and_multiplication_overflow_refuse_without_wrapping(bool multiplication)
    {
        var authority = FundingAuthority1007(int.MaxValue, decimal.MaxValue);
        var priority = authority.PriorityOptions.Single(row => row.Rank == "D");
        if (multiplication)
        {
            priority = priority with { BasePriorityNuyen = 0m };
            priority = priority with { OptionDigest = CharacterCreationResourcesRules.ComputePriorityOptionDigest(priority) };
            authority = authority with { PriorityOptions = authority.PriorityOptions.Select(row => row.Rank == "D" ? priority : row).ToArray() };
            authority = authority with { AuthorityDigest = CharacterCreationResourcesRules.ComputeAuthorityDigest(authority) };
        }
        // Overflow terminates after one or two options, even with both Int32 endpoints.
        var options = Catalogue1007(authority, priority, int.MaxValue, int.MaxValue);
        Assert.AreEqual(multiplication ? 2 : 1, options.Length);
        var positive = PriceFunding1007(authority, priority, int.MaxValue, multiplication ? 1 : 0, 0m, null, false);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, positive.GetProperty("Outcome").GetString());
        var refused = PriceFunding1007(authority, priority, int.MaxValue, multiplication ? 2 : 1, 0m, null, false);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Invalid, refused.GetProperty("Outcome").GetString());
        Assert.AreEqual(JsonValueKind.Null, refused.GetProperty("Value").ValueKind);
    }

    [TestMethod]
    [DataRow(-1, "2000", 25, "0")]
    [DataRow(10, "0", 25, "0")]
    [DataRow(10, "-0.5", 25, "0")]
    [DataRow(10, "2000", int.MinValue, "0")]
    [DataRow(10, "2000", 25, "-1")]
    public void FundingBounds1007_invalid_terms_wallet_and_purchase_cost_fail_closed(
        int maximum, string rate, int available, string purchaseCost)
    {
        var authority = FundingAuthority1007(maximum, decimal.Parse(rate, CultureInfo.InvariantCulture));
        var priority = authority.PriorityOptions.Single(row => row.Rank == "D");
        var result = PriceFunding1007(authority, priority, available, 0,
            decimal.Parse(purchaseCost, CultureInfo.InvariantCulture), 25);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, result.GetProperty("Outcome").GetString());
        Assert.AreEqual(JsonValueKind.Null, result.GetProperty("Value").ValueKind);
    }

    [TestMethod]
    [DataRow("karma:-1")]
    [DataRow("karma:01")]
    [DataRow("karma:+1")]
    [DataRow("karma:2147483648")]
    [DataRow("karma: 1")]
    [DataRow(null)]
    public void FundingBounds1007_direct_option_lookup_keeps_exact_option_identity(string? invalidId)
    {
        var authority = FundingAuthority1007(int.MaxValue, 2500.5m);
        var priority = authority.PriorityOptions.Single(row => row.Rank == "D");
        var method = typeof(CharacterCreationResourcesService).GetMethod("FindAllocationOption", BindingFlags.NonPublic | BindingFlags.Static)!;
        var positive = (CharacterCreationResourceAllocationOption)InvokeFunding1007(method, [authority, priority, 25, "karma:1"])!;
        Assert.AreEqual(2500.5m, positive.NuyenFromKarma);
        Assert.IsNull(InvokeFunding1007(method, [authority, priority, 25, invalidId]));
    }

    [TestMethod]
    public void FundingBounds1007_cancellation_is_observed_before_and_during_catalogue_generation()
    {
        using var cancellation = new CancellationTokenSource();
        var authority = FundingAuthority1007(100, 2500.5m);
        var priority = authority.PriorityOptions.Single(row => row.Rank == "D");
        var anchors = new CancelFundingAnchors1007(priority.SourceAnchorIds, cancellation);
        priority = priority with { SourceAnchorIds = anchors };
        authority = authority with { PriorityOptions = authority.PriorityOptions.Select(row => row.Rank == "D" ? priority : row).ToArray() };
        // Identical contents/digests; enumeration eight occurs in option construction,
        // after authority and priority validation. No clocks or huge loop are needed.
        anchors.Arm(8);
        bool observed = false;
        try { Catalogue1007(authority, priority, 100, 100, cancellation.Token); }
        catch (OperationCanceledException) { observed = true; }
        Assert.IsTrue(observed);
        Assert.AreEqual(8, anchors.Enumerations);
        observed = false;
        try { PriceFunding1007(authority, priority, 25, 0, 0m, 25, true, cancellation.Token); }
        catch (OperationCanceledException) { observed = true; }
        Assert.IsTrue(observed);
    }

    [TestMethod]
    public void FundingBounds1007_live_load_and_preview_bound_maximum_without_mutating_store()
    {
        WithService((store, _, id, _, _) =>
        {
            var before = store.Get(id).Value!;
            var authority = FundingAuthority1007(int.MaxValue, 2500.5m);
            var service = new CharacterCreationResourcesService(store, Resolver(PrerequisiteAuthority(), authority));
            var state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(',', state.Blockers));
            Assert.AreEqual(state.PrerequisiteDraft!.CreationKarmaTotal + 1, state.Options.Count);
            var preview = service.Preview(new(state.Binding, "karma:3"));
            Assert.IsTrue(preview.Value!.CanConfirm, string.Join(',', preview.Blockers));
            Assert.AreEqual(7501.5m, preview.Value.SelectedOption!.NuyenFromKarma);
            var refused = service.Preview(new(state.Binding, "karma:2147483647"));
            Assert.IsFalse(refused.Value!.CanConfirm);
            CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationResourcesBlockers.InsufficientCreationKarma);
            var after = store.Get(id).Value!;
            Assert.AreEqual(before.ContentRevision, after.ContentRevision);
            Assert.AreEqual(before.Document.Content, after.Document.Content);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FundingBounds1007_saved_history_lookups_price_one_real_choice_without_catalogue_enumeration(bool readSavedBudget)
    {
        WithService((store, _, id, _, _) =>
        {
            var service = new CharacterCreationResourcesService(store,
                Resolver(PrerequisiteAuthority(), FundingAuthority1007(int.MaxValue, 2500.5m)));
            var state = Load(service, id);
            var preview = service.Preview(new(state.Binding, "karma:3")).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(',', preview.Blockers));
            var saved = service.Confirm(new(preview.Binding, "karma:3", preview.PreviewDigest,
                "funding1014-saved-choice", true));
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied, saved.Outcome, string.Join(',', saved.Blockers));
            var before = store.Get(id).Value!;
            var loaded = service.LoadCompletionInputs(new(id));
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, loaded.Outcome, string.Join(',', loaded.Blockers));
            var inputs = loaded.Value!;
            Assert.AreEqual(3, inputs.PendingDraft!.KarmaInvestment);
            Assert.AreEqual(saved.Value!.ReceiptDigest, inputs.Receipts.Single().Receipt.ReceiptDigest);

            // Keep the actual saved inputs byte-equivalent. Instrument enumeration so
            // the old sentinel loop fails promptly instead of allocating billions of rows.
            var anchors = new FundingLookupAnchors1014(inputs.PriorityOption.SourceAnchorIds);
            var priority = inputs.PriorityOption with { SourceAnchorIds = anchors };
            inputs = inputs with
            {
                PriorityOption = priority,
                Authority = inputs.Authority with { PriorityOptions = inputs.Authority.PriorityOptions
                    .Select(row => row.SourceId == priority.SourceId ? priority : row).ToArray() }
            };
            Assert.AreEqual(inputs.SnapshotDigest, CharacterCreationResourcesCompletionRules.ComputeInputsDigest(inputs));
            anchors.Reset();
            var method = typeof(CharacterCreationResourcesService).GetMethod(
                readSavedBudget ? "ReadSavedPurchaseBudget" : "IsValidCompletionInputs",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = InvokeFunding1007(method, [inputs]);
            if (readSavedBudget)
            {
                Assert.IsNotNull(result);
                Assert.AreEqual(JsonSerializer.Serialize(preview.BudgetAfter), JsonSerializer.Serialize(result));
            }
            else Assert.AreEqual(true, result);
            Assert.IsTrue(anchors.Enumerations > 0);
            var after = store.Get(id).Value!;
            Assert.AreEqual(before.ContentRevision, after.ContentRevision);
            Assert.AreEqual(before.Document.Content, after.Document.Content);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        });
    }

    private sealed class FundingLookupAnchors1014(IReadOnlyList<string> source) : IReadOnlyList<string>
    {
        public int Enumerations { get; private set; }
        public int Count => source.Count;
        public string this[int index] => source[index];
        public void Reset() => Enumerations = 0;
        public IEnumerator<string> GetEnumerator()
        {
            if (++Enumerations > 64)
                throw new InvalidOperationException("Saved funding lookup enumerated a catalogue instead of one choice.");
            return source.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static CharacterCreationResourcesAuthority FundingAuthority1007(int maximum, decimal rate)
    {
        var authority = ResourcesAuthority() with { MaximumKarmaInvestment = maximum, KarmaToNuyenRate = rate };
        return authority with { AuthorityDigest = CharacterCreationResourcesRules.ComputeAuthorityDigest(authority) };
    }

    private static CharacterCreationResourceAllocationOption[] Catalogue1007(CharacterCreationResourcesAuthority authority,
        CharacterCreationResourcePriorityOption priority, int available, int initial, CancellationToken cancellationToken = default)
        => (CharacterCreationResourceAllocationOption[])InvokeFunding1007(
            typeof(CharacterCreationResourcesService).GetMethod("BuildOptions", BindingFlags.NonPublic | BindingFlags.Static)!,
            [authority, priority, available, initial, cancellationToken])!;

    internal static JsonElement PriceFunding1007(CharacterCreationResourcesAuthority authority,
        CharacterCreationResourcePriorityOption priority, int available, int investment, decimal purchaseCost,
        int? initial, bool includeOptions = true, CancellationToken cancellationToken = default)
    {
        var method = typeof(CharacterCreationResourcesService).GetMethod("PriceSelectedCompletion", BindingFlags.NonPublic | BindingFlags.Static)!;
        var input = Activator.CreateInstance(method.GetParameters()[0].ParameterType,
            [authority, priority, available, investment, true, purchaseCost, Array.Empty<string>()])!;
        return JsonSerializer.SerializeToElement(InvokeFunding1007(method, [input, cancellationToken, initial, includeOptions]));
    }

    private static object? InvokeFunding1007(MethodInfo method, object?[] args)
    {
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private sealed class CancelFundingAnchors1007(IReadOnlyList<string> source, CancellationTokenSource cancellation) : IReadOnlyList<string>
    {
        private int cancelAt = int.MaxValue;
        public int Enumerations { get; private set; }
        public int Count => source.Count;
        public string this[int index] => source[index];
        public void Arm(int count) { Enumerations = 0; cancelAt = count; }
        public IEnumerator<string> GetEnumerator()
        {
            if (++Enumerations == cancelAt) cancellation.Cancel();
            return source.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
