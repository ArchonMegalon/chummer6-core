using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationCandidateEvaluatorTests
{
    [TestMethod]
    public void Native564_retained_execution_identity_matches_real_owner_source_read_without_nested_lease()
    {
        using ReadyContext context = ReadyContext.Create(true);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        var durable = CaptureFiles(context.Directory);
        var sources = new Native564ObservedSources(context.Resolver, () => AssertLease(owner));
        var reader = new WorkspaceContinuationCompletionReader(owner, sources, context.Queries, Catalog());
        var observed = reader.LoadCompletionInputsWithRuleSourceExecution(owner.Capture(), Encode(exported),
            MaximumBytes, out var execution);
        Assert.IsTrue(observed.Success, JsonSerializer.Serialize(observed));
        Assert.IsNotNull(execution);
        var actual = new OwnerBoundCharacterCreationCompletionReader(context.Store, owner, context.Resolver, context.Queries)
            .LoadCompletionInputsWithRuleSourceExecution(owner.Capture(), new(context.WorkspaceId), includeLoadout: true);
        Assert.IsNotNull(actual.Value);
        Assert.AreEqual(actual.Value.Inputs.SnapshotDigest, observed.Identity!.InputsDigest);
        Assert.AreEqual(actual.Value.Inputs.EffectivePolicy!.PolicyDigest, observed.Identity.PolicyDigest);
        Assert.AreEqual(exported.SnapshotDigest, observed.Identity.ContinuationDigest);
        Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            observed.Identity with { ObservationDigest = string.Empty }), observed.Identity.ObservationDigest);
        Assert.IsFalse(observed.HasPersistenceAuthority);
        Assert.IsFalse(observed.CompletePolicyCoverage);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Native577_retained_purchase_execution_identity_matches_real_owner_source_read_without_nested_lease()
    {
        using ReadyContext context = ReadyContext.Create(true);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        var durable = CaptureFiles(context.Directory);
        var sources = new Native564ObservedSources(context.Resolver, () => AssertLease(owner));
        var reader = new WorkspaceContinuationCompletionReader(owner, sources, context.Queries, Catalog());
        var observed = reader.LoadCompletionInputsWithPurchaseSourceExecution(owner.Capture(), Encode(exported),
            MaximumBytes, out var execution);
        Assert.IsTrue(observed.Success, JsonSerializer.Serialize(observed));
        Assert.IsNotNull(execution);
        var actual = new OwnerBoundCharacterCreationCompletionReader(context.Store, owner, context.Resolver, context.Queries)
            .LoadCompletionInputsWithPurchaseSourceExecution(owner.Capture(), new(context.WorkspaceId), includeLoadout: true);
        Assert.IsNotNull(actual.Value);
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2, observed.Inputs!.RuleSourceExecutionSchema);
        Assert.AreEqual(observed.Inputs.RuleSourceCaptureDigest, observed.Identity!.SourceDigest);
        var historical = reader.LoadCompletionInputsWithRuleSourceExecution(owner.Capture(), Encode(exported),
            MaximumBytes, out _);
        Assert.IsTrue(historical.Success);
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1, historical.Inputs!.RuleSourceExecutionSchema);
        Assert.AreNotEqual(historical.Identity!.InputsDigest, observed.Identity.InputsDigest);
        Assert.AreEqual(actual.Value.Inputs.SnapshotDigest, observed.Identity!.InputsDigest);
        Assert.AreEqual(actual.Value.Inputs.EffectivePolicy!.PolicyDigest, observed.Identity.PolicyDigest);
        Assert.AreEqual(exported.SnapshotDigest, observed.Identity.ContinuationDigest);
        Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            observed.Identity with { ObservationDigest = string.Empty }), observed.Identity.ObservationDigest);
        Assert.IsFalse(observed.HasPersistenceAuthority);
        Assert.IsFalse(observed.CompletePolicyCoverage);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    private sealed class Native564ObservedSources(ICharacterSourceDataResolver inner, Action beforeRead)
        : ICharacterSourceDataResolver, ICharacterCreationRuleSourceCapture, ICharacterSourceDataResolverOperationScopeFactory
    {
        public ICharacterSourceDataResolverOperationScope CreateOperationScope()
            => new Native577Scope(((ICharacterSourceDataResolverOperationScopeFactory)inner).CreateOperationScope(), beforeRead);
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            beforeRead();
            return inner.TryCreateContext(xml);
        }
        public bool TryCaptureRuleSources(string xml, out CharacterCreationRuleSourceCapture? capture)
        {
            beforeRead();
            capture = null;
            return inner is ICharacterCreationRuleSourceCapture source && source.TryCaptureRuleSources(xml, out capture);
        }
    }

    private sealed class Native577Scope(ICharacterSourceDataResolverOperationScope inner, Action beforeRead)
        : ICharacterSourceDataResolverOperationScope, ICharacterCreationRuleSourceCapture
    {
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            beforeRead();
            return inner.TryCreateContext(xml);
        }
        public bool TryCaptureRuleSources(string xml, out CharacterCreationRuleSourceCapture? capture)
        {
            beforeRead();
            capture = null;
            return inner is ICharacterCreationRuleSourceCapture sources && sources.TryCaptureRuleSources(xml, out capture);
        }
        public void Dispose() => inner.Dispose();
    }

    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly OwnerScope ForeignOwner = new("another-continuation-owner");

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Retained_completion_reader_matches_current_V3_typed_purchase_and_returns_detached_readonly_inputs(bool awakened)
    {
        using ReadyContext context = ReadyContext.Create(true, includeSkillPurchase: true,
            talentValue: awakened ? "Mystic Adept" : null, mysticPowerPoints: awakened ? 2 : 0);
        PersistTypedCompletionPurchase(context);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        byte[] bytes = Encode(exported);
        var durable = CaptureFiles(context.Directory);
        var sources = new ObservedResolver(context.Resolver, () => AssertLease(owner));
        var reader = new WorkspaceContinuationCompletionReader(owner, sources, context.Queries, Catalog());
        var retained = reader.LoadCompletionInputs(owner.Capture(), bytes, MaximumBytes);
        Assert.IsTrue(retained.Success, JsonSerializer.Serialize(retained));
        Assert.IsFalse(retained.HasPersistenceAuthority);
        Assert.IsFalse(retained.CompletePolicyCoverage);
        Assert.IsEmpty(retained.AuthorityBlockers);
        CollectionAssert.Contains(retained.ReadinessBlockers.ToArray(), CharacterCreationSkillsBlockers.PersistenceAuthorityRequired);
        var inputs = retained.Inputs!;
        var current = new OwnerBoundCharacterCreationCompletionReader(context.Store, owner, context.Resolver, context.Queries)
            .LoadCompletionInputsWithEffectivePolicy(owner.Capture(),
                new(context.WorkspaceId) { IncludeKarmaPurchasePolicy = true }, includeLoadout: true);
        Assert.IsNotNull(current.Value, string.Join(",", current.Blockers));
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.InputsV3, inputs.Schema);
        Assert.AreEqual(current.Value.SnapshotDigest, inputs.SnapshotDigest);
        Assert.AreEqual(JsonSerializer.Serialize(current.Value), JsonSerializer.Serialize(inputs));
        Assert.AreEqual(exported.SnapshotDigest, retained.Identity!.ContinuationDigest);
        Assert.AreEqual(inputs.SnapshotDigest, retained.Identity.InputsDigest);
        Assert.AreEqual(inputs.EffectivePolicy!.PolicyDigest, retained.Identity.PolicyDigest);
        Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            retained.Identity with { ObservationDigest = string.Empty }), retained.Identity.ObservationDigest);
        Assert.AreEqual(JsonSerializer.Serialize(exported.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationLoadoutDraft),
            JsonSerializer.Serialize(inputs.Loadout!.PendingDraft));
        Assert.IsTrue(inputs.Loadout.PendingDraft!.PurchaseCost > 0);
        Assert.IsFalse(inputs.Skills.Attributes.CanEdit);
        Assert.IsFalse(inputs.Skills.SavedSkillsState!.CanEdit);
        Assert.IsFalse(inputs.Magic.SavedMagicState?.CanEdit == true);
        if (awakened) Assert.IsNotNull(inputs.Magic.SavedMagicState);
        Assert.IsFalse(inputs.Lifestyles.Qualities.SavedQualitiesState!.CanEdit);
        Assert.IsFalse(inputs.Lifestyles.Gear.State.CanEdit);
        var attributes = CharacterCreationAttributesService.CreateDraftSession(inputs.Skills.Attributes);
        Assert.IsNotNull(attributes, "Read-only rule readiness must not require save authority.");
        Assert.IsNotNull(attributes.Preview(inputs.Skills.Attributes.PendingDraft!.Allocations).Value);
        Assert.IsNotNull(CharacterCreationLoadoutCompletionService.Preview(inputs.Loadout,
            inputs.Loadout.OriginalSelections).Value);

        string original = JsonSerializer.Serialize(inputs);
        ((IList<CharacterCreationPolicySettingCoverage>)inputs.EffectivePolicy.Settings)[0] =
            inputs.EffectivePolicy.Settings[0] with { IntegerValue = 99 };
        ((IList<CharacterCreationLoadoutCatalogOption>)inputs.Loadout.Authority.Options)[0] =
            inputs.Loadout.Authority.Options[0] with { UnitCost = 999999m };
        var again = reader.LoadCompletionInputs(owner.Capture(), bytes, MaximumBytes);
        Assert.IsTrue(again.Success, JsonSerializer.Serialize(again));
        Assert.AreEqual(original, JsonSerializer.Serialize(again.Inputs));
        Assert.AreEqual(original, JsonSerializer.Serialize(current.Value));
        Assert.AreEqual(retained.Identity, again.Identity);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Admission235_retained_request_scope_preserves_full_inputs_with_fewer_source_scans(bool awakened)
    {
        using ReadyContext context = ReadyContext.Create(true, includeSkillPurchase: true,
            talentValue: awakened ? "Mystic Adept" : null, mysticPowerPoints: awakened ? 2 : 0);
        PersistTypedCompletionPurchase(context);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        OwnerContextStamp stamp = owner.Capture();
        byte[] bytes = Encode(Export(context, owner));
        var durable = CaptureFiles(context.Directory);
        var resolver = (FileSystemCharacterSourceDataResolver)context.Resolver;
        var ordinaryReader = new WorkspaceContinuationCompletionReader(owner, resolver, context.Queries, Catalog());
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var baseline = ordinaryReader.LoadCompletionInputs(stamp, bytes, MaximumBytes);
        timer.Stop();
        double ordinaryMs = timer.Elapsed.TotalMilliseconds;
        Assert.IsTrue(baseline.Success, JsonSerializer.Serialize(baseline));
        string expected = JsonSerializer.Serialize(baseline);
        var ordinary = resolver.LastSourceInputSnapshotDiagnostics!;

        using var reads = resolver.CreateValidatedReadScope();
        var reader = new WorkspaceContinuationCompletionReader(owner, reads, context.Queries, Catalog());
        timer.Restart();
        var first = reader.LoadCompletionInputs(stamp, bytes, MaximumBytes);
        timer.Stop();
        double firstMs = timer.Elapsed.TotalMilliseconds;
        Assert.AreEqual(expected, JsonSerializer.Serialize(first));
        var initial = resolver.LastSourceInputSnapshotDiagnostics!;
        Assert.IsTrue(initial.ValidationReadCount < ordinary.ValidationReadCount,
            "The real completion reader must use the synchronous operation, not just small source fixtures.");
        Assert.IsFalse(first.HasPersistenceAuthority);
        Assert.AreEqual(0, owner.ActiveLeases);
        ((IList<CharacterCreationPolicySettingCoverage>)first.Inputs!.EffectivePolicy!.Settings)[0] =
            first.Inputs.EffectivePolicy.Settings[0] with { IntegerValue = 99 };

        await Task.Yield(); // Only the request cache survives; no operation or owner lease may do so.
        timer.Restart();
        var again = reader.LoadCompletionInputs(stamp, bytes, MaximumBytes);
        timer.Stop();
        var repeated = resolver.LastSourceInputSnapshotDiagnostics!;
        Assert.AreEqual(expected, JsonSerializer.Serialize(again));
        Assert.AreEqual(initial.PhysicalReadCount, repeated.PhysicalReadCount);
        Assert.AreEqual(initial.PhysicalXmlParseCount, repeated.PhysicalXmlParseCount);
        int repeatValidationReads = repeated.ValidationReadCount - initial.ValidationReadCount;
        Assert.IsTrue(repeatValidationReads > 0 && repeatValidationReads < ordinary.ValidationReadCount);
        Assert.AreEqual(0, owner.ActiveLeases);
        owner.Transition(ForeignOwner);
        owner.Transition(OwnerScope.LocalSingleUser);
        var revoked = reader.LoadCompletionInputs(stamp, bytes, MaximumBytes);
        Assert.IsFalse(revoked.Success);
        Assert.IsNull(revoked.Inputs);
        Assert.AreEqual(repeated.ValidationReadCount, resolver.LastSourceInputSnapshotDiagnostics!.ValidationReadCount,
            "Revoked ownership must reject before source-cache admission.");
        AssertFilesUnchanged(context.Directory, durable);
        Console.WriteLine($"completion-request-source-scans ordinary={ordinary.ValidationReadCount} "
            + $"first={initial.ValidationReadCount} repeat={repeatValidationReads} "
            + $"ordinaryMs={ordinaryMs:F3} firstMs={firstMs:F3} repeatMs={timer.Elapsed.TotalMilliseconds:F3}");
    }

    [TestMethod]
    public void Admission138_retained_recapture_reuses_only_the_live_synchronous_source_scope()
    {
        using ReadyContext context = ReadyContext.Create(true, includeSkillPurchase: true);
        PersistTypedCompletionPurchase(context);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        OwnerContextStamp stamp = owner.Capture();
        byte[] bytes = Encode(Export(context, owner));
        var durable = CaptureFiles(context.Directory);
        var resolver = (FileSystemCharacterSourceDataResolver)context.Resolver;
        int readsBeforeFinal = 0, parsesBeforeFinal = 0;
        long validationBeforeFinal = 0;
        var sources = new Admission138Resolver(resolver, read =>
        {
            AssertLease(owner);
            if (read != 2) return;
            var before = resolver.LastSourceInputSnapshotDiagnostics!;
            readsBeforeFinal = before.PhysicalReadCount;
            parsesBeforeFinal = before.PhysicalXmlParseCount;
            validationBeforeFinal = before.ValidationBytesRead;
        });
        var reader = new WorkspaceContinuationCompletionReader(owner, sources, context.Queries, Catalog());
        var observed = reader.LoadCompletionInputs(stamp, bytes, MaximumBytes);
        Assert.IsTrue(observed.Success, JsonSerializer.Serialize(observed));
        Assert.IsTrue(observed.Inputs!.Loadout!.PendingDraft!.PurchaseCost > 0);
        Assert.AreEqual(1, sources.ScopesCreated);
        Assert.AreEqual(1, sources.ScopesDisposed);
        Assert.AreEqual(0, sources.OutsideScopeReads);
        Assert.AreEqual(2, sources.Contexts.Count);
        Assert.AreSame(sources.Contexts[0], sources.Contexts[1]);
        var after = resolver.LastSourceInputSnapshotDiagnostics!;
        Assert.AreEqual(readsBeforeFinal, after.PhysicalReadCount);
        Assert.AreEqual(parsesBeforeFinal, after.PhysicalXmlParseCount);
        Assert.IsTrue(after.ValidationBytesRead > validationBeforeFinal,
            "Reusing projections must still read the actual current source bytes.");
        Assert.ThrowsExactly<ObjectDisposedException>(() => sources.LastScope!.TryCreateContext(
            Export(context, owner).Snapshot.Workspace.Document.Content));

        // The existing non-factory path remains a cold, independent oracle.
        var cold = new WorkspaceContinuationCompletionReader(owner,
            new ObservedResolver(context.Resolver, () => AssertLease(owner)), context.Queries, Catalog())
            .LoadCompletionInputs(stamp, bytes, MaximumBytes);
        Assert.IsTrue(cold.Success, JsonSerializer.Serialize(cold));
        Assert.AreEqual(JsonSerializer.Serialize(cold), JsonSerializer.Serialize(observed));

        // No scope/admission survives its lease, including same-owner ABA.
        owner.Transition(ForeignOwner);
        owner.Transition(OwnerScope.LocalSingleUser);
        var revoked = reader.LoadCompletionInputs(stamp, bytes, MaximumBytes);
        Assert.IsFalse(revoked.Success);
        Assert.IsNull(revoked.Inputs);
        CollectionAssert.Contains(revoked.AuthorityBlockers.ToArray(), "owner-authority-unavailable");
        Assert.AreEqual(1, sources.ScopesCreated);
        var next = reader.LoadCompletionInputs(owner.Capture(), bytes, MaximumBytes);
        Assert.IsTrue(next.Success, JsonSerializer.Serialize(next));
        Assert.AreEqual(2, sources.ScopesCreated);
        Assert.AreEqual(2, sources.ScopesDisposed);
        Assert.AreEqual(4, sources.Contexts.Count);
        Assert.AreNotSame(sources.Contexts[0], sources.Contexts[2]);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    [DataRow("source-bytes")]
    [DataRow("house-rule")]
    [DataRow("selected-file-deleted")]
    public void Admission138_final_scoped_recapture_rejects_current_source_drift(string change)
    {
        using ReadyContext context = ReadyContext.Create(true, prepareSource: _ => { });
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        byte[] bytes = Encode(Export(context, owner));
        string root = Path.Combine(context.Directory, "source");
        string name = change == "house-rule"
            ? CharacterCreationBootstrapProfiles.SettingsSourceFile(CharacterCreationBootstrapProfiles.PrioritySettingsProfileId)
            : "skills.xml";
        string path = Path.Combine(root, "data", name);
        byte[] original = File.ReadAllBytes(path);
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        // Force byte validation even when metadata identity is unavailable.
        var resolver = new FileSystemCharacterSourceDataResolver(
            new Chummer.Infrastructure.Files.FileSystemContentOverlayCatalogService(root, root, null),
            null, useStrongChangeIdentity: false);
        var sources = new Admission138Resolver(resolver, read =>
        {
            AssertLease(owner);
            if (read != 2) return;
            if (change == "selected-file-deleted")
            {
                File.Delete(path);
                return;
            }
            byte[] changed;
            if (change == "house-rule")
            {
                string text = Encoding.UTF8.GetString(original);
                string replacement = text.Replace("<karmaimproveactiveskill>2</karmaimproveactiveskill>",
                    "<karmaimproveactiveskill>3</karmaimproveactiveskill>", StringComparison.Ordinal);
                Assert.AreNotEqual(text, replacement);
                changed = Encoding.UTF8.GetBytes(replacement);
            }
            else
            {
                changed = original.ToArray();
                int newline = Array.IndexOf(changed, (byte)'\n');
                Assert.IsTrue(newline >= 0);
                changed[newline] = (byte)' ';
            }
            Assert.AreEqual(original.Length, changed.Length);
            File.WriteAllBytes(path, changed);
            File.SetLastWriteTimeUtc(path, timestamp);
        });
        var result = new WorkspaceContinuationCompletionReader(owner, sources, context.Queries, Catalog())
            .LoadCompletionInputs(owner.Capture(), bytes, MaximumBytes);
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Inputs);
        Assert.IsNull(result.Identity);
        CollectionAssert.Contains(result.AuthorityBlockers.ToArray(), "continuation-sources-changed");
        Assert.AreEqual(0, sources.OutsideScopeReads);
        Assert.AreEqual(2, sources.Contexts.Count);
        Assert.IsNotNull(sources.Contexts[0]);
        Assert.IsNull(sources.Contexts[1]);
        Assert.AreEqual(1, sources.ScopesDisposed);
        Assert.AreEqual(0, owner.ActiveLeases);
    }

    // Observes the real resolver's existing scope; supplies no source authority.
    private sealed class Admission138Resolver(FileSystemCharacterSourceDataResolver inner, Action<int> beforeRead)
        : ICharacterSourceDataResolver, ICharacterSourceDataResolverOperationScopeFactory
    {
        public int ScopesCreated { get; private set; }
        public int ScopesDisposed { get; private set; }
        public int OutsideScopeReads { get; private set; }
        public List<ICharacterSourceDataContext?> Contexts { get; } = [];
        public ICharacterSourceDataResolverOperationScope? LastScope { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            OutsideScopeReads++;
            return inner.TryCreateContext(xml);
        }
        public ICharacterSourceDataResolverOperationScope CreateOperationScope()
        {
            ScopesCreated++;
            return LastScope = new Scope(this, inner.CreateOperationScope(), beforeRead);
        }
        private sealed class Scope(Admission138Resolver observer,
            ICharacterSourceDataResolverOperationScope innerScope, Action<int> beforeRead)
            : ICharacterSourceDataResolverOperationScope
        {
            private int reads;
            private bool disposed;
            public ICharacterSourceDataContext? TryCreateContext(string xml)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                beforeRead(++reads);
                var context = innerScope.TryCreateContext(xml);
                observer.Contexts.Add(context);
                return context;
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                innerScope.Dispose();
                observer.ScopesDisposed++;
            }
        }
    }

    [TestMethod]
    public void Retained_completion_reader_rejects_foreign_owner_tampered_content_and_resealed_history_before_sources()
    {
        using ReadyContext context = ReadyContext.Create(true);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        byte[] bytes = Encode(exported);
        var durable = CaptureFiles(context.Directory);
        var sources = new ObservedResolver(context.Resolver, () => Assert.Fail("Rejected identity/history must precede sources."));
        var reader = new WorkspaceContinuationCompletionReader(owner, sources, context.Queries, Catalog());
        var foreign = reader.LoadCompletionInputs(owner.Capture(),
            Encode(WithDigest(exported.Snapshot with { OwnerId = ForeignOwner.NormalizedValue })), MaximumBytes);
        Assert.IsFalse(foreign.Success);
        Assert.IsNull(foreign.Inputs);
        Assert.IsNull(foreign.Identity);
        CollectionAssert.Contains(foreign.AuthorityBlockers.ToArray(), "continuation-owner-mismatch");
        JsonObject tampered = JsonNode.Parse(bytes)!.AsObject();
        tampered["Snapshot"]!["Workspace"]!["Document"]!["State"]!["Payload"] = "<character />";
        var content = reader.LoadCompletionInputs(owner.Capture(), Encoding.UTF8.GetBytes(tampered.ToJsonString()), MaximumBytes);
        Assert.IsFalse(content.Success);
        CollectionAssert.Contains(content.AuthorityBlockers.ToArray(), "continuation-wire-invalid");

        var workspace = exported.Snapshot.Workspace;
        var auxiliary = workspace.Document.AuxiliaryState;
        var altered = auxiliary.CharacterCreationSkillsDraft! with
        {
            DraftDigest = CharacterCreationFinalizationDigest.Compute("altered retained history")
        };
        var history = WithDigest(exported.Snapshot with { Workspace = workspace with
        {
            Document = workspace.Document with { State = workspace.Document.State with
            {
                AuxiliaryState = auxiliary with { CharacterCreationSkillsDraft = altered }
            } }
        } });
        var rejected = reader.LoadCompletionInputs(owner.Capture(), Encode(history), MaximumBytes);
        Assert.IsFalse(rejected.Success);
        Assert.IsNull(rejected.Inputs);
        Assert.IsNotEmpty(rejected.AuthorityBlockers);
        Assert.IsEmpty(rejected.ReadinessBlockers);
        Assert.AreEqual(0, sources.Reads);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Retained_completion_reader_rechecks_effective_sources_and_propagates_cancellation()
    {
        using ReadyContext context = ReadyContext.Create(true);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        byte[] bytes = Encode(Export(context, owner));
        var durable = CaptureFiles(context.Directory);
        int reads = 0;
        var changing = new CompletionPolicyResolver(context.Resolver, policy => ++reads == 1 ? policy
            : SealCompletionPolicy(policy with { BooksInputsDigest = CharacterCreationFinalizationDigest.Compute("new source") }));
        var changed = new WorkspaceContinuationCompletionReader(owner, changing, context.Queries, Catalog())
            .LoadCompletionInputs(owner.Capture(), bytes, MaximumBytes);
        Assert.IsFalse(changed.Success);
        Assert.IsNull(changed.Inputs);
        CollectionAssert.Contains(changed.AuthorityBlockers.ToArray(), "continuation-sources-changed");
        Assert.AreEqual(2, reads, "Completion must use the frozen policy, then independently recapture once.");

        using var cancellation = new CancellationTokenSource();
        var canceling = new CompletionPolicyResolver(context.Resolver, policy => { cancellation.Cancel(); return policy; });
        var reader = new WorkspaceContinuationCompletionReader(owner, canceling, context.Queries, Catalog());
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            reader.LoadCompletionInputs(owner.Capture(), bytes, MaximumBytes, cancellation.Token));
        int beforeCanceledRead = canceling.PolicyReads;
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            reader.LoadCompletionInputs(owner.Capture(), bytes, MaximumBytes, cancellation.Token));
        Assert.AreEqual(beforeCanceledRead, canceling.PolicyReads);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Retained_completion_reader_preserves_unsupported_policy_as_readiness_without_inventing_complete_coverage()
    {
        using ReadyContext context = ReadyContext.Create(true);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        byte[] bytes = Encode(Export(context, owner));
        var durable = CaptureFiles(context.Directory);
        var unsupported = new CompletionPolicyResolver(context.Resolver, policy =>
        {
            var settings = policy.Settings.ToArray();
            settings[0] = settings[0] with { IntegerValue = 5,
                Disposition = CharacterCreationPolicyDisposition.Unsupported,
                Consumer = CharacterCreationPolicyConsumer.None,
                Reason = CharacterCreationPolicyReason.ConfiguredLimitUnsupported };
            return SealCompletionPolicy(policy with { Settings = settings });
        });
        var result = new WorkspaceContinuationCompletionReader(owner, unsupported, context.Queries, Catalog())
            .LoadCompletionInputs(owner.Capture(), bytes, MaximumBytes);
        Assert.IsTrue(result.Success, JsonSerializer.Serialize(result));
        Assert.IsEmpty(result.AuthorityBlockers);
        CollectionAssert.Contains(result.ReadinessBlockers.ToArray(), CharacterCreationEffectivePolicyContext.SkillLimitsUnsupported);
        Assert.IsFalse(CharacterCreationEffectivePolicyValidation.Satisfies(result.Inputs!.EffectivePolicy,
            CharacterCreationPolicyRequirement.SkillLimits));
        Assert.IsFalse(CharacterCreationEffectivePolicyValidation.Satisfies(result.Inputs.EffectivePolicy,
            CharacterCreationPolicyRequirement.Complete));
        Assert.IsFalse(result.CompletePolicyCoverage);
        var missing = new WorkspaceContinuationCompletionReader(owner,
            new CompletionPolicyResolver(context.Resolver, _ => null), context.Queries, Catalog())
            .LoadCompletionInputs(owner.Capture(), bytes, MaximumBytes);
        Assert.IsFalse(missing.Success);
        CollectionAssert.Contains(missing.AuthorityBlockers.ToArray(), CharacterCreationEffectivePolicyContext.Unavailable);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Retained_completion_readiness_never_grants_editor_or_store_mutation_authority()
    {
        using ReadyContext context = ReadyContext.Create(true);
        PersistTypedCompletionPurchase(context);
        var workspace = context.Store.Get(context.WorkspaceId).Value!;
        var durable = CaptureFiles(context.Directory);
        IWorkspaceStore view = new WorkspaceContinuationReadView(OwnerScope.LocalSingleUser, workspace);
        Assert.IsFalse(view is IWorkspaceAuxiliaryStateAtomicCommitCapability);
        var attributes = new CharacterCreationAttributesService(view, context.Resolver);
        var state = attributes.Load(new(workspace.Id)).Value!;
        Assert.IsFalse(state.CanEdit);
        Assert.IsNotNull(CharacterCreationAttributesService.CreateDraftSession(state));
        var preview = attributes.Preview(new(state.Binding, state.PendingDraft!.Allocations));
        Assert.IsFalse(preview.Value?.CanConfirm == true);
        CollectionAssert.Contains(preview.Blockers.ToArray(), CharacterCreationAttributesBlockers.PersistenceAuthorityRequired);
        var loadout = new CharacterCreationLoadoutService(view, context.Resolver);
        var purchaseState = loadout.Prepare(workspace.Id).Value!;
        Assert.IsNotNull(purchaseState);
        var purchasePreview = loadout.Preview(new(purchaseState.Inputs.Binding, purchaseState.Inputs.SnapshotDigest,
            purchaseState.Inputs.OriginalSelections));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, purchasePreview.Outcome);
        Assert.IsNull(purchasePreview.Value);
        Assert.IsNotEmpty(purchasePreview.Blockers);
        var writableLoadout = new CharacterCreationLoadoutService(context.Store, context.Resolver);
        var writableState = writableLoadout.Prepare(workspace.Id).Value!;
        Assert.IsNotNull(writableState);
        var original = writableState.Inputs.OriginalSelections;
        var weapon = writableState.Inputs.Authority.Options
            .Where(option => option.Kind == CharacterCreationLoadoutKinds.Weapon && option.IsSelectable && option.UnitCost > 0
                && original.Weapons.All(selection => selection.OptionId != option.OptionId))
            .OrderBy(option => option.UnitCost).First();
        var writablePreview = writableLoadout.Preview(new(writableState.Inputs.Binding, writableState.Inputs.SnapshotDigest,
            new([.. original.Weapons, new(weapon.OptionId)], original.Armor)));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, writablePreview.Outcome, string.Join(",", writablePreview.Blockers));
        Assert.IsNotNull(writablePreview.Value);
        var rejected = loadout.Confirm(new(writablePreview.Value.Request, writablePreview.Value.PreviewDigest,
            "readonly-purchase-rejected", true));
        Assert.IsNull(rejected.Value);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationFinalizationBlockers.AtomicPersistenceRejected);
        Assert.IsFalse(new CharacterCreationGearService(view, context.Resolver).Load(new(workspace.Id)).Value!.CanEdit);
        Assert.IsFalse(new CharacterCreationResourcesService(view, context.Resolver).Load(new(workspace.Id)).Value!.CanEdit);
        Assert.IsFalse(view.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(workspace.Id,
            workspace.ContentRevision, workspace.Document.AuxiliaryStateDigest, workspace.Document).Success);
        Assert.IsFalse(view.SaveCheckpoint(workspace.Id, workspace.ContentRevision).Success);
        Assert.IsFalse(view.Delete(workspace.Id, workspace.ContentRevision).Success);
        Assert.AreEqual(JsonSerializer.Serialize(workspace), JsonSerializer.Serialize(context.Store.Get(workspace.Id).Value));
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Retained_completion_reader_reports_unselected_prerequisites_as_readiness_not_foreign_or_malformed_authority()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        byte[] bytes = Encode(Export(context, owner));
        var durable = CaptureFiles(context.Directory);
        var result = new WorkspaceContinuationCompletionReader(owner, context.Resolver, context.Queries, Catalog())
            .LoadCompletionInputs(owner.Capture(), bytes, MaximumBytes);
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Inputs);
        Assert.IsEmpty(result.AuthorityBlockers);
        CollectionAssert.Contains(result.ReadinessBlockers.ToArray(), "continuation-completion-prerequisite-required");
        AssertFilesUnchanged(context.Directory, durable);
    }

    private static void PersistTypedCompletionPurchase(ReadyContext context)
    {
        var service = new CharacterCreationLoadoutService(context.Store, context.Resolver);
        var state = service.Prepare(context.WorkspaceId).Value!;
        Assert.IsNotNull(state);
        var weapon = state.Inputs.Authority.Options
            .Where(option => option.Kind == CharacterCreationLoadoutKinds.Weapon && option.IsSelectable && option.UnitCost > 0)
            .OrderBy(option => option.UnitCost).First();
        var preview = service.Preview(new(state.Inputs.Binding, state.Inputs.SnapshotDigest, new([new(weapon.OptionId)], [])));
        Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
        var saved = service.Confirm(new(preview.Value.Request, preview.Value.PreviewDigest, "retained-completion-purchase", true));
        Assert.IsNotNull(saved.Value, string.Join(",", saved.Blockers));
    }

    [TestMethod]
    public void Retained_completion_sources_freeze_detached_V3_policy_loadout_and_Karma_inputs_without_writes()
    {
        using ReadyContext context = ReadyContext.Create(true, includeSkillPurchase: true, amendSettings: settings =>
        {
            settings.SetElementValue("multiplyrestrictedcost", true);
            settings.SetElementValue("multiplyforbiddencost", true);
            settings.SetElementValue("restrictedcostmultiplier", 2);
            settings.SetElementValue("forbiddencostmultiplier", 3);
        });
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var durable = CaptureFiles(context.Directory);
        var live = new OwnerBoundCharacterCreationCompletionReader(context.Store, owner, context.Resolver, context.Queries)
            .LoadCompletionInputsWithEffectivePolicy(owner.Capture(),
                new(context.WorkspaceId) { IncludeKarmaPurchasePolicy = true }, includeLoadout: true);
        Assert.IsNotNull(live.Value, string.Join(",", live.Blockers));
        var inputs = live.Value;
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.InputsV3, inputs.Schema);
        Assert.AreEqual(CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs), inputs.SnapshotDigest);

        var capture = CaptureCompletionSources(context);
        string xml = context.Store.Get(context.WorkspaceId).Value!.Document.Content;
        var frozen = capture.CreateResolver().TryCreateContext(xml)!;
        Assert.IsTrue(frozen.TryResolveCreationEffectivePolicy(out var policy));
        Assert.IsTrue(frozen.TryResolveCreationPrioritySkillKarmaPolicy(out var karma));
        Assert.IsTrue(frozen.TryResolveCreationLoadoutAuthority(out var loadout));
        Assert.IsTrue(frozen.TryResolveCreationLoadoutLegalityPolicy(out var legality));
        Assert.IsTrue(context.Resolver.TryCreateContext(xml)!.TryResolveCreationLoadoutLegalityPolicy(out var liveLegality));
        Assert.AreEqual(JsonSerializer.Serialize(liveLegality), JsonSerializer.Serialize(legality));
        Assert.IsTrue(legality!.HasCreationLifecycle(inputs.Skills.Binding.RawCharacterXmlDigest));
        Assert.IsTrue(frozen.TryResolveCreationSkillsCatalog(out var catalog));
        Assert.IsNotNull(catalog, "Compose the pending native skill source-order capture without replacing it.");
        Assert.IsTrue(frozen.TryResolveCreationContactsPolicy(out _), "Completion needs policy even before a contact draft exists.");
        Assert.IsTrue(frozen.TryResolveCreationCarryoverPolicy(out _));
        Assert.AreEqual(JsonSerializer.Serialize(inputs.EffectivePolicy), JsonSerializer.Serialize(policy));
        Assert.AreEqual(JsonSerializer.Serialize(inputs.Skills.KarmaPurchasePolicy), JsonSerializer.Serialize(karma));
        Assert.AreEqual(JsonSerializer.Serialize(inputs.Loadout!.Authority), JsonSerializer.Serialize(loadout));
        Assert.AreEqual(CharacterCreationEffectivePolicyContext.ComputeDigest(policy!), policy!.PolicyDigest);

        var settings = (IList<CharacterCreationPolicySettingCoverage>)policy.Settings;
        settings[0] = settings[0] with { IntegerValue = 99 };
        var options = (IList<CharacterCreationLoadoutCatalogOption>)loadout.Options;
        options[0] = options[0] with { UnitCost = 999999m };
        Assert.IsTrue(frozen.TryResolveCreationEffectivePolicy(out var secondPolicy));
        Assert.IsTrue(frozen.TryResolveCreationLoadoutAuthority(out var secondLoadout));
        Assert.AreEqual(JsonSerializer.Serialize(inputs.EffectivePolicy), JsonSerializer.Serialize(secondPolicy));
        Assert.AreEqual(JsonSerializer.Serialize(inputs.Loadout.Authority), JsonSerializer.Serialize(secondLoadout));
        Assert.AreEqual(capture.Digest, CaptureCompletionSources(context).Digest);
        Assert.IsNull(capture.CreateResolver().TryCreateContext(xml + " "));
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Retained_completion_sources_bind_effective_policy_changes_and_keep_prior_reads_frozen()
    {
        using ReadyContext context = ReadyContext.Create(true);
        var durable = CaptureFiles(context.Directory);
        bool changed = false;
        var resolver = new CompletionPolicyResolver(context.Resolver, policy => changed
            ? SealCompletionPolicy(policy with { BooksInputsDigest = CharacterCreationFinalizationDigest.Compute("changed books observation") })
            : policy);
        var before = CaptureCompletionSources(context, resolver);
        string xml = context.Store.Get(context.WorkspaceId).Value!.Document.Content;
        var frozen = before.CreateResolver().TryCreateContext(xml)!;
        Assert.IsTrue(frozen.TryResolveCreationEffectivePolicy(out var original));
        changed = true;
        Assert.IsTrue(frozen.TryResolveCreationEffectivePolicy(out var retained));
        Assert.AreEqual(original!.PolicyDigest, retained!.PolicyDigest);
        var after = CaptureCompletionSources(context, resolver);
        Assert.AreNotEqual(before.Digest, after.Digest);
        Assert.IsTrue(after.CreateResolver().TryCreateContext(xml)!.TryResolveCreationEffectivePolicy(out var current));
        Assert.AreNotEqual(original.PolicyDigest, current!.PolicyDigest);
        Assert.AreEqual(2, resolver.PolicyReads, "Frozen readers must never consult the producer again.");
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Retained_completion_sources_preserve_missing_and_unsupported_effective_policy_without_complete_coverage()
    {
        using ReadyContext context = ReadyContext.Create(true);
        var durable = CaptureFiles(context.Directory);
        string xml = context.Store.Get(context.WorkspaceId).Value!.Document.Content;
        var missing = CaptureCompletionSources(context, new CompletionPolicyResolver(context.Resolver, _ => null));
        Assert.IsFalse(missing.CreateResolver().TryCreateContext(xml)!.TryResolveCreationEffectivePolicy(out var absent));
        Assert.IsNull(absent);
        var unsupported = CaptureCompletionSources(context, new CompletionPolicyResolver(context.Resolver, policy =>
        {
            var settings = policy.Settings.ToArray();
            settings[0] = settings[0] with { IntegerValue = 5,
                Disposition = CharacterCreationPolicyDisposition.Unsupported,
                Consumer = CharacterCreationPolicyConsumer.None,
                Reason = CharacterCreationPolicyReason.ConfiguredLimitUnsupported };
            return SealCompletionPolicy(policy with { Settings = settings });
        }));
        Assert.IsTrue(unsupported.CreateResolver().TryCreateContext(xml)!.TryResolveCreationEffectivePolicy(out var policy));
        Assert.IsFalse(CharacterCreationEffectivePolicyValidation.Satisfies(policy, CharacterCreationPolicyRequirement.SkillLimits));
        Assert.IsFalse(CharacterCreationEffectivePolicyValidation.Satisfies(policy, CharacterCreationPolicyRequirement.Complete));
        Assert.AreEqual(CharacterCreationPolicyDisposition.Unsupported, policy!.Settings[0].Disposition);
        Assert.AreEqual(CharacterCreationEffectivePolicyContext.ComputeDigest(policy), policy.PolicyDigest);
        Assert.AreNotEqual(missing.Digest, unsupported.Digest);
        Assert.AreNotEqual(CaptureCompletionSources(context).Digest, unsupported.Digest);
        AssertFilesUnchanged(context.Directory, durable);
    }

    private static WorkspaceContinuationSourceCapture CaptureCompletionSources(
        ReadyContext context, ICharacterSourceDataResolver? resolver = null)
    {
        resolver ??= context.Resolver;
        var workspace = context.Store.Get(context.WorkspaceId).Value!;
        string xml = workspace.Document.Content;
        Assert.IsTrue(WorkspaceContinuationSourceCapture.TryCapture(resolver.TryCreateContext(xml)!, xml, Catalog(),
            out var capture, workspace.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft,
            workspace, resolver, includeCompletionInputs: true));
        return capture!;
    }

    private static CharacterCreationEffectivePolicyContext SealCompletionPolicy(CharacterCreationEffectivePolicyContext policy)
    {
        policy = policy with { Domains = CharacterCreationEffectivePolicyContext.DescribeDomains(
            policy.Settings, policy.Sources, policy.BooksInputsDigest) };
        return policy with { PolicyDigest = CharacterCreationEffectivePolicyContext.ComputeDigest(policy) };
    }

    private sealed class CompletionPolicyResolver(ICharacterSourceDataResolver inner,
        Func<CharacterCreationEffectivePolicyContext, CharacterCreationEffectivePolicyContext?> transform) : ICharacterSourceDataResolver
    {
        public int PolicyReads { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            var context = inner.TryCreateContext(characterXml);
            if (context is null) return null;
            var proxy = DispatchProxy.Create<ICharacterSourceDataContext, RetainedCompletionPolicyProbe>();
            var probe = (RetainedCompletionPolicyProbe)(object)proxy;
            probe.Inner = context;
            probe.Transform = policy => { PolicyReads++; return transform(policy); };
            return proxy;
        }
    }

    public class RetainedCompletionPolicyProbe : DispatchProxy
    {
        public ICharacterSourceDataContext Inner { get; set; } = null!;
        public Func<CharacterCreationEffectivePolicyContext, CharacterCreationEffectivePolicyContext?> Transform { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(Inner, args);
            if (targetMethod.Name == nameof(ICharacterSourceDataContext.TryResolveCreationEffectivePolicy))
            {
                Assert.IsTrue(result is true);
                Assert.IsNotNull(args![0]);
                args[0] = Transform((CharacterCreationEffectivePolicyContext)args[0]!);
                return args[0] is not null;
            }
            return result;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Pending_contacts_and_lifestyles_pass_cold_readonly_continuation(bool buyLifestyle)
    {
        using ReadyContext context = ReadyContext.Create(true);
        var contacts = new CharacterCreationContactsService(context.Store, context.Resolver);
        var contactState = contacts.Load(new(context.WorkspaceId)).Value!;
        Assert.IsNotNull(contactState.NewContactTemplate);
        var edit = new CharacterCreationContactEdit(Guid.Parse("7943b8ed-087b-43d9-81f8-043f7603cc97"),
            contactState.NewContactTemplate.Identity with { Name = "Fixer" }, Connection: 1, Loyalty: 1)
            { ChangeKind = CharacterCreationContactChangeKind.Add };
        var preview = contacts.Preview(new(contactState.Binding, edit));
        Assert.IsTrue(preview.Value?.CanConfirm == true, string.Join(",", preview.Value?.Blockers ?? preview.Blockers));
        var contactSaved = contacts.Confirm(new(contactState.Binding, edit, preview.Value!.PreviewDigest,
            "continuation-pending-contact", true));
        Assert.IsTrue(contactSaved.Success, string.Join(",", contactSaved.Blockers));
        if (buyLifestyle)
        {
            var lifestyles = new CharacterCreationLifestylesService(context.Store, context.Resolver);
            var state = lifestyles.Load(new(context.WorkspaceId)).Value!;
            var low = state.Authority.LifestyleOptions.Single(option => option.Name == "Low");
            Guid id = Guid.Parse("de7df355-0909-4bb3-9031-a6b8c55fc78b");
            var configuration = new CharacterCreationLifestyleConfiguration(id, low.OptionId, "Home",
                CharacterCreationLifestyleStyleIds.Standard, low.DefaultIncrementId, 1, 100m, 0,
                false, false, 0, 0, 0, 0, "Seattle", "", "", []);
            var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create, id, configuration);
            var review = lifestyles.Preview(new(state.Binding, mutation)).Value!;
            Assert.IsTrue(review.CanConfirm, string.Join(",", review.Blockers));
            var saved = lifestyles.Confirm(new(state.Binding, mutation, review.PreviewDigest,
                "continuation-pending-lifestyle", true));
            Assert.IsTrue(saved.Success, string.Join(",", saved.Blockers));
        }
        using ReadyContext cold = context.Restart();
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        byte[] bytes = Encode(Export(cold, owner));
        var durable = CaptureFiles(context.Directory);
        ObservedResolver sources = new(cold.Resolver, () => AssertLease(owner));
        var result = new WorkspaceContinuationCandidateEvaluator(owner, sources, cold.Queries, Catalog())
            .Evaluate(owner.Capture(), bytes, MaximumBytes);

        Assert.IsTrue(result.CurrentDraftChecksPassed, string.Join(";", result.BoundaryBlockers.Concat(
            result.DomainChecks.Select(check => check.Domain + ":" + string.Join(",", check.Blockers)))));
        Passed(result);
        Assert.IsTrue(result.DomainChecks.Any(check => check.Domain == "contacts"));
        Assert.AreEqual(buyLifestyle, result.DomainChecks.Any(check => check.Domain == "lifestyles"));
        Assert.AreEqual(buyLifestyle ? 4 : 2, sources.Reads,
            "Only raw and (when needed) projected XML capture plus independent recapture may consult live sources.");
        CollectionAssert.AreEqual(bytes, Encode(result.Candidate!));
        AssertFilesUnchanged(context.Directory, durable);
        if (buyLifestyle)
        {
            var disappearing = new ObservedResolver(cold.Resolver, () => AssertLease(owner)) { MissingAtRead = 4 };
            var changed = new WorkspaceContinuationCandidateEvaluator(owner, disappearing, cold.Queries, Catalog())
                .Evaluate(owner.Capture(), bytes, MaximumBytes);
            Denied(changed, "continuation-sources-changed");
            Assert.AreEqual(4, disappearing.Reads);
            Assert.AreEqual(0, owner.ActiveLeases);
            AssertFilesUnchanged(context.Directory, durable);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Real_complete_mundane_and_Mystic_Adept_drafts_pass_readonly_checks_without_restore_authority(bool awakened)
    {
        using ReadyContext context = ReadyContext.Create(true, includeNonEmptyPurchases: !awakened,
            talentValue: awakened ? "Mystic Adept" : null, mysticPowerPoints: awakened ? 2 : 0);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        WorkspaceContinuationExport exported = Export(context, owner);
        byte[] bytes = Encode(exported);
        byte[] originalBytes = bytes.ToArray();
        var durable = CaptureFiles(context.Directory);
        ObservedResolver sources = new(context.Resolver, () => AssertLease(owner));
        ObservedCatalog catalog = new(Catalog(), () => Assert.AreEqual(1, owner.ActiveLeases));

        var result = new WorkspaceContinuationCandidateEvaluator(owner, sources, context.Queries, catalog)
            .Evaluate(owner.Capture(), bytes, MaximumBytes);

        Passed(result);
        Assert.AreEqual(exported.SnapshotDigest, result.Candidate!.SnapshotDigest);
        Assert.AreEqual(JsonSerializer.Serialize(exported), JsonSerializer.Serialize(result.Candidate));
        Assert.AreEqual(2, sources.Reads, "Only capture and independent recapture may consult the live resolver.");
        Assert.AreEqual(2, catalog.StageReads);
        Assert.IsTrue(result.DomainChecks.SelectMany(check => check.ReadinessBlockers).Any(),
            "Read-only evaluation must expose unavailable persistence, not claim an atomic writer.");
        foreach (string domain in new[] { "prerequisite", "attributes", "skills", "qualities", "resources", "gear" })
            Assert.IsTrue(result.DomainChecks.Any(check => check.Domain == domain), domain);
        if (awakened)
        {
            Assert.IsTrue(result.DomainChecks.Any(check => check.Domain == "magic-resonance"));
            Assert.IsNotNull(result.Candidate.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft);
        }
        Assert.AreEqual(0, owner.ActiveLeases);
        CollectionAssert.AreEqual(originalBytes, bytes);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Original_arm_limited_quality_passes_readonly_continuation_without_rewriting_the_candidate(string method)
    {
        using ReadyContext context = ReadyContext.Create(true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", buildMethod: method);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        var selection = exported.Snapshot.Workspace.Document.AuxiliaryState
            .CharacterCreationQualitiesDraft!.Selections.Single();
        Assert.AreEqual(2, selection.ResolvedArmCount);
        Assert.AreEqual(4, selection.KarmaCost);
        byte[] bytes = Encode(exported);
        var durable = CaptureFiles(context.Directory);

        var result = Evaluator(context, owner).Evaluate(owner.Capture(), bytes, MaximumBytes);

        Passed(result);
        CollectionAssert.AreEqual(bytes, Encode(result.Candidate!));
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Real_bootstrap_only_wizard_does_not_require_absent_future_choices()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        var auxiliary = exported.Snapshot.Workspace.Document.AuxiliaryState;
        Assert.IsNotNull(auxiliary.CharacterCreationBootstrapBinding);
        Assert.IsNull(auxiliary.CharacterCreationPrerequisiteDraft);
        Assert.IsNull(auxiliary.CharacterCreationAttributesDraft);
        Assert.IsNull(auxiliary.CharacterCreationSkillsDraft);
        Assert.IsNull(auxiliary.CharacterCreationResourcesDraft);
        var durable = CaptureFiles(context.Directory);

        var result = Evaluator(context, owner).Evaluate(owner.Capture(), Encode(exported), MaximumBytes);

        Passed(result);
        CollectionAssert.AreEqual(new[] { "bootstrap" }, result.DomainChecks.Select(check => check.Domain).ToArray());
        Assert.IsNull(result.Candidate!.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Bootstrap_XML_marker_without_its_binding_is_never_an_empty_valid_continuation(bool created)
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        var workspace = exported.Snapshot.Workspace;
        var xml = System.Xml.Linq.XDocument.Parse(workspace.Document.Content);
        xml.Root!.SetElementValue("created", created ? "True" : "False");
        if (created)
            xml.Root.SetElementValue("metatype", "Human");
        var document = workspace.Document with { State = workspace.Document.State with
        {
            Payload = xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting),
            AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
        } };
        Assert.IsTrue(CharacterCreationBootstrapAuthority.HasBootstrapState(document));
        var forged = WithDigest(exported.Snapshot with
        {
            Workspace = workspace with { Document = document }
        });
        byte[] bytes = Encode(forged);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(bytes, MaximumBytes, out _));
        var durable = CaptureFiles(context.Directory);

        var result = Evaluator(context, owner).Evaluate(owner.Capture(), bytes, MaximumBytes);

        Assert.IsFalse(result.CurrentDraftChecksPassed, Describe(result));
        var bootstrap = result.DomainChecks.Single(check => check.Domain == "bootstrap");
        Assert.IsFalse(bootstrap.Evaluated);
        Assert.IsNotEmpty(bootstrap.Blockers);
        if (created)
            CollectionAssert.Contains(result.BoundaryBlockers.ToArray(), "active-creation-drafts-on-career-character");
        NoAuthority(result);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Continuation_readers_do_not_hide_invalid_attribute_math_behind_missing_writer_readiness()
    {
        using ReadyContext context = ReadyContext.Create(true, talentValue: "Mystic Adept", mysticPowerPoints: 2);
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        var view = new WorkspaceContinuationReadView(OwnerScope.LocalSingleUser, saved);
        var skills = new CharacterCreationSkillsService(view, context.Resolver).LoadForContinuation(new(saved.Id));
        var magic = new CharacterCreationMagicResonanceService(view, context.Resolver).LoadForContinuation(new(saved.Id));
        Assert.IsNotNull(skills.Value!.PendingDraft);
        Assert.IsNotNull(magic.Value!.PendingDraft);
        Assert.IsTrue(skills.Blockers.All(item => item == CharacterCreationSkillsBlockers.PersistenceAuthorityRequired));
        Assert.IsTrue(magic.Blockers.All(item => item == CharacterCreationMagicResonanceBlockers.PersistenceAuthorityRequired));

        var original = saved.Document.AuxiliaryState.CharacterCreationAttributesDraft!;
        var invalid = original with { Attributes = original.Attributes.Select(item =>
            item.AttributeId == "INT" ? item with { Current = item.Current + 1 } : item).ToArray() };
        invalid = invalid with { DraftDigest = CharacterCreationAttributesDraftIntegrity.ComputeDigest(invalid) };
        var document = saved.Document with { State = saved.Document.State with
        {
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCreationAttributesDraft = invalid }
        } };
        var malformedView = new WorkspaceContinuationReadView(OwnerScope.LocalSingleUser, saved with { Document = document });
        var attributes = new CharacterCreationAttributesService(malformedView, context.Resolver).Load(new(saved.Id));
        CollectionAssert.Contains(attributes.Blockers.ToArray(), CharacterCreationAttributesBlockers.DraftInvalid);
        skills = new CharacterCreationSkillsService(malformedView, context.Resolver).LoadForContinuation(new(saved.Id));
        magic = new CharacterCreationMagicResonanceService(malformedView, context.Resolver).LoadForContinuation(new(saved.Id));
        CollectionAssert.Contains(skills.Blockers.ToArray(), CharacterCreationSkillsBlockers.AttributesDraftRequired);
        CollectionAssert.Contains(magic.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.AttributesDraftRequired);
        Assert.AreEqual(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(context.Store.Get(saved.Id).Value));
    }

    [TestMethod]
    public void Stale_ABA_foreign_and_value_only_owner_authorities_are_rejected_before_source_reads()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        byte[] bytes = Encode(Export(context, owner));
        ObservedResolver sources = new(context.Resolver, () => Assert.Fail("An unadmitted owner must not resolve sources."));
        ObservedCatalog catalog = new(Catalog(), () => Assert.Fail("An unadmitted owner must not read catalog sources."));
        var evaluator = new WorkspaceContinuationCandidateEvaluator(owner, sources, context.Queries, catalog);
        OwnerContextStamp original = owner.Capture();
        owner.Transition(ForeignOwner);
        Denied(evaluator.Evaluate(original, bytes, MaximumBytes), "owner-authority-unavailable");
        owner.Transition(OwnerScope.LocalSingleUser);
        Denied(evaluator.Evaluate(original, bytes, MaximumBytes), "owner-authority-unavailable");
        foreach (OwnerContextStamp bad in new[]
                 {
                     default, owner.Capture() with { Owner = ForeignOwner },
                     owner.Capture() with { AuthorityInstanceId = "different-issuer" },
                     owner.Capture() with { TransitionRevision = -1 }
                 })
            Denied(evaluator.Evaluate(bad, bytes, MaximumBytes), "owner-authority-unavailable");
        var valueOnly = new WorkspaceContinuationCandidateEvaluator(
            new ValueOnlyOwner(OwnerScope.LocalSingleUser), sources, context.Queries, catalog);
        Denied(valueOnly.Evaluate(owner.Capture(), bytes, MaximumBytes), "owner-authority-unavailable");
        Assert.AreEqual(0, sources.Reads);
        Assert.AreEqual(0, catalog.Reads);
        Assert.AreEqual(0, owner.ActiveLeases);
    }

    [TestMethod]
    public void Codec_tampering_over_budget_and_foreign_payload_owner_fail_before_source_capture()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        byte[] bytes = Encode(exported);
        var durable = CaptureFiles(context.Directory);
        ObservedResolver sources = new(context.Resolver, () => Assert.Fail("Wire or owner failure must precede source capture."));
        ObservedCatalog catalog = new(Catalog(), () => Assert.Fail("Wire or owner failure must precede catalog capture."));
        var evaluator = new WorkspaceContinuationCandidateEvaluator(owner, sources, context.Queries, catalog);
        JsonObject tampered = JsonNode.Parse(bytes)!.AsObject();
        tampered["Snapshot"]!["Workspace"]!["Document"]!["State"]!["Payload"] = "<character><name>Forged</name></character>";
        Denied(evaluator.Evaluate(owner.Capture(), Encoding.UTF8.GetBytes(tampered.ToJsonString()), MaximumBytes),
            "continuation-wire-invalid");
        Denied(evaluator.Evaluate(owner.Capture(), bytes, bytes.Length - 1), "continuation-wire-invalid");
        Denied(evaluator.Evaluate(owner.Capture(), Encoding.UTF8.GetBytes("{"), MaximumBytes), "continuation-wire-invalid");

        // Recomputing a content digest is possible for any caller. It is not an
        // owner lease or authorization to evaluate another owner's workspace.
        var foreign = WithDigest(exported.Snapshot with { OwnerId = ForeignOwner.NormalizedValue });
        var mismatch = evaluator.Evaluate(owner.Capture(), Encode(foreign), MaximumBytes);
        Denied(mismatch, "continuation-owner-mismatch");
        Assert.IsNull(mismatch.Candidate, "A foreign candidate must not be returned through this owner-bound seam.");
        owner.Transition(new OwnerScope(OwnerScope.LocalSingleUser.Value));
        var forgedLocal = evaluator.Evaluate(owner.Capture(), bytes, MaximumBytes);
        Assert.IsFalse(forgedLocal.CurrentDraftChecksPassed);
        Assert.IsNotEmpty(forgedLocal.BoundaryBlockers);
        NoAuthority(forgedLocal);
        Assert.AreEqual(0, sources.Reads);
        Assert.AreEqual(0, catalog.Reads);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Loss_of_the_actual_source_resolver_during_independent_recapture_invalidates_the_review()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        byte[] bytes = Encode(Export(context, owner));
        var durable = CaptureFiles(context.Directory);
        ObservedResolver sources = new(context.Resolver, () => AssertLease(owner)) { MissingAfterFirstRead = true };

        var result = new WorkspaceContinuationCandidateEvaluator(owner, sources, context.Queries, Catalog())
            .Evaluate(owner.Capture(), bytes, MaximumBytes);

        Denied(result, "continuation-sources-changed");
        Assert.IsNotNull(result.SourceDigest, "The first genuine capture must have succeeded.");
        Assert.IsTrue(result.DomainChecks.All(check => check.Evaluated && check.Blockers.Count == 0), Describe(result));
        Assert.AreEqual(2, sources.Reads);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Actual_catalog_stage_metadata_drift_changes_the_full_capture_even_with_the_same_claimed_catalog_digest()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        byte[] bytes = Encode(Export(context, owner));
        var durable = CaptureFiles(context.Directory);
        ObservedCatalog catalog = new(Catalog(), () => Assert.AreEqual(1, owner.ActiveLeases))
        {
            ChangeStageOnRecapture = true
        };
        string claimedDigest = catalog.Inner.GetAuthority().RawXmlDigest;

        var result = new WorkspaceContinuationCandidateEvaluator(owner, context.Resolver, context.Queries, catalog)
            .Evaluate(owner.Capture(), bytes, MaximumBytes);

        Denied(result, "continuation-sources-changed");
        Assert.IsNotNull(result.SourceDigest);
        Assert.AreEqual(claimedDigest, catalog.Inner.GetAuthority().RawXmlDigest);
        Assert.AreEqual(2, catalog.StageReads);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Rehashed_Resources_history_still_requires_genuine_source_bound_recomputation()
    {
        using ReadyContext context = ReadyContext.Create(false, includeNonEmptyPurchases: true);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        var workspace = exported.Snapshot.Workspace;
        var auxiliary = workspace.Document.AuxiliaryState;
        var original = auxiliary.CharacterCreationResourcesDraft!;
        const decimal unearned = 1000m;
        var draft = original with
        {
            Budget = original.Budget with
            {
                PriorityNuyen = original.Budget.PriorityNuyen + unearned,
                TotalStartingNuyen = original.Budget.TotalStartingNuyen + unearned,
                RemainingNuyen = original.Budget.RemainingNuyen + unearned,
                CarryoverExcess = Math.Max(0m, original.Budget.RemainingNuyen + unearned - original.Budget.CarryoverLimit)
            },
            FinalizationContribution = original.FinalizationContribution with
            {
                StartingNuyen = original.FinalizationContribution.StartingNuyen + unearned
            }
        };
        draft = draft with { FinalizationContribution = draft.FinalizationContribution with
        {
            ContributionDigest = CharacterCreationResourcesRules.ComputeContributionDigest(draft.FinalizationContribution)
        } };
        draft = draft with { DraftDigest = CharacterCreationResourcesRules.ComputeDraftDigest(draft) };
        var entry = auxiliary.CharacterCreationResourcesReceipts!.Single();
        var receipt = entry.Receipt with
        {
            DraftDigest = draft.DraftDigest, TotalStartingNuyen = draft.Budget.TotalStartingNuyen,
            RemainingNuyen = draft.Budget.RemainingNuyen
        };
        receipt = receipt with { ReceiptDigest = CharacterCreationResourcesRules.ComputeReceiptDigest(receipt) };
        var forgedAuxiliary = auxiliary with
        {
            CharacterCreationResourcesDraft = draft,
            CharacterCreationResourcesReceipts = [entry with { Receipt = receipt }]
        };
        Assert.IsTrue(CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(
            workspace.Id, workspace.ContentRevision, draft, forgedAuxiliary.CharacterCreationResourcesReceipts));
        var forged = WithDigest(exported.Snapshot with { Workspace = workspace with
        {
            Document = workspace.Document with { State = workspace.Document.State with { AuxiliaryState = forgedAuxiliary } }
        } });
        byte[] bytes = Encode(forged);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(bytes, MaximumBytes, out _));
        var durable = CaptureFiles(context.Directory);

        var result = Evaluator(context, owner).Evaluate(owner.Capture(), bytes, MaximumBytes);

        Assert.IsTrue(result.HistoryConsistent, Describe(result));
        Assert.IsFalse(result.CurrentDraftChecksPassed);
        var resources = result.DomainChecks.Single(check => check.Domain == "resources");
        CollectionAssert.Contains(resources.Blockers.ToArray(), CharacterCreationResourcesBlockers.ReceiptLedgerCorrupt);
        NoAuthority(result);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Real_finalized_Career_archive_survives_dirty_candidate_review_without_normalizing_checkpoint_or_granting_mutation()
    {
        using ReadyContext context = ReadyContext.Create(true, includeNonEmptyPurchases: true);
        var loaded = context.Finalizer.Load(new(context.WorkspaceId));
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        Assert.IsNotNull(loaded.Value.StartingCashSource);
        var cash = new CharacterCreationStartingCashChoice(
            loaded.Value.StartingCashSource.AuthorityDigest, loaded.Value.StartingCashSource.Dice);
        var review = context.Finalizer.Review(new(loaded.Value.Binding) { StartingCash = cash });
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        Assert.IsNotNull(review.Value.Plan, string.Join(",", review.Value.Blockers));
        var finalized = context.Finalizer.Confirm(new(loaded.Value.Binding, review.Value.PreviewDigest,
            review.Value.Plan.PlanDigest, "continuation-candidate-finalize", ExplicitlyConfirmed: true)
            { StartingCash = cash });
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, finalized.Outcome, string.Join(",", finalized.Blockers));
        var clean = context.Store.Get(context.WorkspaceId).Value!;
        var edited = clean.Document with { State = clean.Document.State with
        {
            Payload = clean.Document.Content.Replace("Finalization Runner", "Career Runner", StringComparison.Ordinal)
        } };
        Assert.AreNotEqual(clean.Document.Content, edited.Content);
        Assert.IsTrue(context.Store.ReplaceWorkspaceDocument(context.WorkspaceId, clean.ContentRevision, edited).Success);
        var dirty = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(dirty.SavedRevision < dirty.ContentRevision);
        Assert.IsFalse(CharacterCareerReputationProjector.TryRead(dirty, context.Resolver, out _, out string error));
        Assert.AreEqual("reputation_workspace_not_clean", error);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        var durable = CaptureFiles(context.Directory);

        var result = Evaluator(context, owner).Evaluate(owner.Capture(), Encode(exported), MaximumBytes);

        Passed(result);
        CollectionAssert.AreEqual(new[] { "career-reputation" }, result.DomainChecks.Select(check => check.Domain).ToArray());
        Assert.AreEqual(dirty.ContentRevision, result.Candidate!.Snapshot.Workspace.ContentRevision);
        Assert.AreEqual(dirty.SavedRevision, result.Candidate.Snapshot.Workspace.SavedRevision);
        Assert.AreEqual(JsonSerializer.Serialize(exported), JsonSerializer.Serialize(result.Candidate));
        Assert.IsNotNull(result.Candidate.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        Assert.HasCount(1, result.Candidate.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!);
        Assert.IsFalse(CharacterCareerReputationProjector.TryRead(dirty, context.Resolver, out _, out error));
        Assert.AreEqual("reputation_workspace_not_clean", error);
        AssertFilesUnchanged(context.Directory, durable);
    }

    private static WorkspaceContinuationCandidateEvaluator Evaluator(ReadyContext context, IOwnerContextAccessor owner)
        => new(owner, context.Resolver, context.Queries, Catalog());

    private static XmlLifeModulesCatalogService Catalog() => new(Path.Combine(FindCoreRoot(), "Chummer", "data", "lifemodules.xml"));

    private static string FindCoreRoot()
    {
        for (DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate canonical Chummer source data.");
    }

    private static WorkspaceContinuationExport Export(ReadyContext context, TestOwner owner)
    {
        var result = new WorkspaceContinuationExportService(context.Store, owner).Export(owner.Capture(), context.WorkspaceId);
        Assert.IsTrue(result.Success, result.Error);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static WorkspaceContinuationExport WithDigest(WorkspaceContinuationSnapshot snapshot)
        => new(snapshot, WorkspaceContinuationSnapshotDigest.Compute(snapshot));

    private static byte[] Encode(WorkspaceContinuationExport exported) => WorkspaceContinuationCodec.Encode(exported, MaximumBytes);

    private static void Passed(WorkspaceContinuationCandidateEvaluation result)
    {
        Assert.IsTrue(result.CurrentDraftChecksPassed, Describe(result));
        Assert.IsTrue(result.HistoryConsistent);
        Assert.IsNotNull(result.Candidate);
        Assert.IsNotNull(result.SourceDigest);
        Assert.IsEmpty(result.BoundaryBlockers);
        Assert.IsTrue(result.DomainChecks.All(check => check.Evaluated && check.Blockers.Count == 0), Describe(result));
        NoAuthority(result);
    }

    private static void Denied(WorkspaceContinuationCandidateEvaluation result, string blocker)
    {
        Assert.IsFalse(result.CurrentDraftChecksPassed);
        CollectionAssert.Contains(result.BoundaryBlockers.ToArray(), blocker, Describe(result));
        NoAuthority(result);
    }

    private static void NoAuthority(WorkspaceContinuationCandidateEvaluation result)
    {
        Assert.IsFalse(result.RestoreAuthorized);
        Assert.IsFalse(result.HistoricalProvenanceVerified);
    }

    private static string Describe(WorkspaceContinuationCandidateEvaluation result) => JsonSerializer.Serialize(result);

    private static Dictionary<string, (byte[] Bytes, DateTime Timestamp)> CaptureFiles(string directory)
        => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => path, path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)), StringComparer.Ordinal);

    private static void AssertFilesUnchanged(string directory, Dictionary<string, (byte[] Bytes, DateTime Timestamp)> before)
    {
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        foreach (var (path, snapshot) in before)
        {
            CollectionAssert.AreEqual(snapshot.Bytes, File.ReadAllBytes(path), path);
            Assert.AreEqual(snapshot.Timestamp, File.GetLastWriteTimeUtc(path), path);
        }
    }

    private static void AssertLease(TestOwner owner)
    {
        Assert.AreEqual(1, owner.ActiveLeases);
        Assert.IsFalse(owner.CanTransitionFromAnotherThread(), "The actual owner lease must span both live-source observations.");
    }

    private sealed class ObservedResolver(ICharacterSourceDataResolver inner, Action beforeRead) : ICharacterSourceDataResolver
    {
        public int Reads { get; private set; }
        public bool MissingAfterFirstRead { get; init; }
        public int? MissingAtRead { get; init; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            Reads++;
            beforeRead();
            return MissingAfterFirstRead && Reads > 1 || Reads == MissingAtRead
                ? null : inner.TryCreateContext(characterXml);
        }
    }

    // Every value starts at the actual XML-backed catalog. The adversarial
    // change below alters only recaptured stage metadata, not rule success.
    private sealed class ObservedCatalog(ILifeModulesCatalogService inner, Action beforeRead) : ILifeModulesCatalogService
    {
        public ILifeModulesCatalogService Inner => inner;
        public int Reads { get; private set; }
        public int StageReads { get; private set; }
        public bool ChangeStageOnRecapture { get; init; }
        private void Read() { Reads++; beforeRead(); }
        public LifeModuleCatalogAuthorityDto GetAuthority() { Read(); return inner.GetAuthority(); }
        public IReadOnlyList<LifeModuleStageDto> GetStages()
        {
            Read();
            StageReads++;
            var stages = inner.GetStages().ToArray();
            if (ChangeStageOnRecapture && StageReads > 1)
            {
                Assert.IsTrue(stages.Length > 1);
                stages[^1] = stages[^1] with { Order = stages[^1].Order + 100 };
            }
            return stages;
        }
        public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null)
        { Read(); return inner.GetModules(stage); }
        public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(string? stage = null, IReadOnlyCollection<string>? enabledSources = null)
        { Read(); return inner.GetOptionProjections(stage, enabledSources); }
    }

    private sealed class TestOwner(OwnerScope initial) : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private readonly string _issuer = Guid.NewGuid().ToString("N");
        private OwnerScope _owner = initial;
        private long _revision;
        public int ActiveLeases { get; private set; }
        public OwnerScope Current { get { lock (_gate) return _owner; } }
        public OwnerContextStamp Capture() { lock (_gate) return new(_owner, _issuer, _revision); }
        public void Transition(OwnerScope owner)
        {
            lock (_gate)
            {
                Assert.AreEqual(0, ActiveLeases);
                _owner = owner;
                _revision++;
            }
        }
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            if (!expected.IsValid || expected != new OwnerContextStamp(_owner, _issuer, _revision))
            {
                Monitor.Exit(_gate);
                lease = null;
                return false;
            }
            ActiveLeases++;
            lease = new Lease(this, expected);
            return true;
        }
        public bool CanTransitionFromAnotherThread()
        {
            var task = Task.Run(() =>
            {
                if (!Monitor.TryEnter(_gate)) return false;
                Monitor.Exit(_gate);
                return true;
            });
            Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(10)));
            return task.Result;
        }
        private sealed class Lease(TestOwner owner, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp => !_disposed ? stamp : throw new ObjectDisposedException(nameof(Lease));
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                owner.ActiveLeases--;
                Monitor.Exit(owner._gate);
            }
        }
    }

    private sealed class ValueOnlyOwner(OwnerScope owner) : IOwnerContextAccessor
    {
        public OwnerScope Current => owner;
    }
}
