using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using MutableBindingSnapshot = Chummer.Application.Characters.CharacterCreationCompleteRuleSourceExecution.ProfileSettingInputValidation1203.FreshJsonComparisonStream;

namespace Chummer.Application.Characters;

public enum CharacterCurrentRuleDomain
{
    Profile, Metatypes, Priorities, Skills, Qualities, Traditions, Streams,
    Powers, Spells, ComplexForms, Gear, Lifestyles, Weapons, Armor
}

public enum CharacterCurrentRuleStage { Creation, Career }
public enum CharacterCurrentRuleEntryState { AvailableNotSelected, Unavailable, Tombstone }
public enum CharacterCurrentRuleConsumptionState { Evaluated, NotConfigured }

/// <summary>An Application-issued invocation, never a completion request or an activation permit.</summary>
public sealed class CharacterCurrentRuleEnvironmentContext
{
    private readonly string retainedArchiveDigest;
    private readonly string retainedMutableBinding;
    private readonly byte[] retainedMutableSerializationDigest;
    // Installed only by the concrete retained issuer for one synchronous Evaluate.
    // This is private derived-data lifetime, never owner or commit authority.
    internal CharacterCurrentRuleEnvironmentConsumers.RetainedEvaluation2764? RetainedEvaluation2764 { get; set; }

    internal CharacterCurrentRuleEnvironmentContext(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture capture, ICharacterFileQueries queries,
        CharacterCurrentRuleStage stage, string semanticsProfile, string semanticsFingerprint)
    {
        Current = current;
        Capture = capture;
        Queries = queries;
        Stage = stage;
        SemanticsProfile = semanticsProfile;
        SemanticsFingerprint = semanticsFingerprint;
        // One detached archive at this explicit observation/admission boundary. The
        // concrete retained resolver owns immutable bytes throughout domain execution.
        retainedArchiveDigest = capture.ComputePrivateInputsDigest();
        // Observe mutable inputs BEFORE computing the durable binding. A mutation
        // during that computation must not become a new accepted baseline.
        using (var snapshot = SerializeMutableBinding())
        {
            ReadOnlyMemory<byte> mutableBytes = snapshot.GetCapturedJson();
            retainedMutableSerializationDigest = System.Security.Cryptography.SHA256.HashData(mutableBytes.Span);
            retainedMutableBinding = CanonicalMutableBinding(mutableBytes);
        }
        BindingDigest = ComputeBinding();
    }

    [JsonIgnore] public WorkspaceStoredDocument Current { get; }
    [JsonIgnore] public CharacterCreationRuleSourceCapture Capture { get; }
    [JsonIgnore] public ICharacterFileQueries Queries { get; }
    public string CharacterXml => Current.Document.Content;
    public CharacterCurrentRuleStage Stage { get; }
    public string SemanticsProfile { get; }
    public string SemanticsFingerprint { get; }
    public string BindingDigest { get; }
    public string CaptureDigest => Capture.IntegrityDigest;
    public string EffectivePolicyDigest => Capture.EffectivePolicyDigest;
    public string? RequiredPackageSetDigest => Capture.RequiredPackageSetDigest;

    public bool IsUnchanged => retainedArchiveDigest == Capture.IntegrityDigest
        && HasUnchangedMutableBinding() && Capture.MatchesOriginalCharacter(CharacterXml);

    // Recheck external capture bytes at issuance/quote boundaries, never cache a
    // mutable external export as authority. Inner checks still freeze all current
    // XML/aux/revisions and source metadata; the engine freezes actual source bytes.
    internal bool VerifyRetainedArchive() => IsUnchanged && BindingDigest == ComputeBinding()
        && retainedArchiveDigest == Capture.ComputePrivateInputsDigest();

    // Current, Document and State are sealed records held by get-only context
    // properties. Observe the entire reachable mutable projection on EVERY check,
    // including the generated auxiliary digest and virtual source metadata. Only
    // allocation, hashing and canonical sorting of identical bytes are reused; no mutable
    // graph, source witness or mutation-admission check is skipped.
    private bool HasUnchangedMutableBinding()
    {
        var evaluation = RetainedEvaluation2764;
        using var snapshot = SerializeMutableBinding(evaluation?.MutableBindingBaseline8102(this));
        if (snapshot.IsExactMatch) return true;
        ReadOnlyMemory<byte> bytes = snapshot.GetCapturedJson();
        if (retainedMutableSerializationDigest.AsSpan().SequenceEqual(
            System.Security.Cryptography.SHA256.HashData(bytes.Span)))
        {
            // Retain only bytes proven equal to the context's original snapshot.
            // The issuer owns and clears this bounded, evaluation-local baseline.
            evaluation?.RememberMutableBinding8102(this, bytes.Span);
            return true;
        }
        // Dictionary/JSON object order is not part of the old canonical
        // equality contract. Compare that SAME snapshot on a byte mismatch;
        // re-enumerating caller lists here could hide an observed ABA change.
        // Neither baseline is refreshed, even after an equivalent snapshot.
        return retainedMutableBinding == CanonicalMutableBinding(bytes);
    }

    private static string CanonicalMutableBinding(ReadOnlyMemory<byte> bytes)
    {
        using var document = System.Text.Json.JsonDocument.Parse(bytes);
        return CharacterCreationFinalizationDigest.Compute(document.RootElement);
    }

    private MutableBindingSnapshot SerializeMutableBinding(byte[]? baseline = null) => CaptureMutableBinding(new
    {
        Current.Document.AuxiliaryState,
        AuxiliaryStateDigest = RetainedEvaluation2764 is { } evaluation
            ? evaluation.ObserveAuxiliaryDigest7985(this) : Current.Document.AuxiliaryStateDigest,
        Current.LocalHistory, Current.DelegatedGmHistorySegmentStarts,
        Capture.IntegrityDigest, Capture.EffectivePolicyDigest, Capture.CompatibilityProfile,
        Capture.RequiredPackageSetDigest, Capture.RequiredRulePacks, Capture.RequiredAssets,
        Capture.OrderedCustomSources, Capture.SourcebookSelection
    }, baseline);

    // The shared auxiliary converter delegates to the existing generated wire
    // contract. Reuse serializer metadata only; every getter, collection and
    // extension value is still observed afresh on each mutable-binding check.
    private static readonly System.Text.Json.JsonSerializerOptions mutableBindingOptions8129 = CreateMutableBindingOptions8129();

    private static System.Text.Json.JsonSerializerOptions CreateMutableBindingOptions8129()
    {
        var options = new System.Text.Json.JsonSerializerOptions();
        options.Converters.Add(new Chummer.Contracts.Workspaces.WorkspaceDocumentAuxiliaryStateJsonConverter());
        return options;
    }

    private static MutableBindingSnapshot CaptureMutableBinding<T>(T value, byte[]? baseline)
    {
        var snapshot = new MutableBindingSnapshot(baseline);
        try
        {
            System.Text.Json.JsonSerializer.Serialize(snapshot, value, mutableBindingOptions8129);
            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    private string ComputeBinding()
    {
        // Observe the same complete binding through the shared auxiliary wire
        // contract. This document belongs only to this call; the existing v1
        // canonical digest still determines the binding, with no value reuse.
        using var document = System.Text.Json.JsonSerializer.SerializeToDocument(new
        {
            Current, Stage, SemanticsProfile, SemanticsFingerprint,
            Capture.IntegrityDigest, Capture.EffectivePolicyDigest, Capture.CompatibilityProfile,
            Capture.RequiredPackageSetDigest, Capture.RequiredRulePacks, Capture.RequiredAssets,
            Capture.OrderedCustomSources, Capture.SourcebookSelection,
            PrivateInputs = retainedArchiveDigest
        }, mutableBindingOptions8129);
        return CharacterCreationFinalizationDigest.Compute(document.RootElement);
    }
}

/// <summary>Exact merger observations. These records are diagnostic data, never success capabilities.</summary>
public sealed record CharacterCurrentRuleMergeOperation(
    int Order, string Query, string SourceIdentity, string SourceDigest, string Phase,
    string Operation, string Selector, string? BeforeXml, string? AfterXml,
    bool IsTombstone, IReadOnlyList<string> Dependencies);

public sealed record CharacterCurrentRuleCatalogEntry(
    string Key, string Name, string Xml, CharacterCurrentRuleEntryState State,
    string? UnavailableReason = null);

/// <summary>Unvalidated, retained-only catalog input supplied by the shared merger.</summary>
public sealed record CharacterCurrentRuleCatalogInput(
    CharacterCurrentRuleDomain Domain, string Query, string ContextDigest,
    IReadOnlyList<CharacterCurrentRuleCatalogEntry> Entries,
    IReadOnlyList<CharacterCurrentRuleMergeOperation> Operations,
    IReadOnlyList<string> Dependencies, IReadOnlyList<string> Blockers)
{
    public string ContentDigest => Entries is SnapshotEntries snapshot
        && ReferenceEquals(snapshot.Input, this) && snapshot.Digest is { } digest
            ? digest : ComputeContentDigest();

    // A report may retain this digest only for the exact detached input instance.
    internal bool IsDetachedSnapshot => Entries is SnapshotEntries snapshot
        && ReferenceEquals(snapshot.Input, this) && snapshot.Digest is not null;

    // Public construction and with-expressions remain mutation-sensitive. Only
    // this factory detaches every collection before retaining a digest.
    public static CharacterCurrentRuleCatalogInput CreateSnapshot(
        CharacterCurrentRuleDomain domain, string query, string contextDigest,
        IReadOnlyList<CharacterCurrentRuleCatalogEntry> entries,
        IReadOnlyList<CharacterCurrentRuleMergeOperation> operations,
        IReadOnlyList<string> dependencies, IReadOnlyList<string> blockers)
    {
        var snapshotEntries = new SnapshotEntries(entries.ToImmutableArray());
        var input = new CharacterCurrentRuleCatalogInput(domain, query, contextDigest,
            snapshotEntries, operations.Select(operation => operation with
            {
                Dependencies = operation.Dependencies.ToImmutableArray()
            }).ToImmutableArray(), dependencies.ToImmutableArray(), blockers.ToImmutableArray());
        snapshotEntries.Bind(input);
        return input;
    }

    private string ComputeContentDigest() => CharacterCreationFinalizationDigest.Compute(new
        { Domain, Query, ContextDigest, Entries, Operations, Dependencies, Blockers });

    // Keep the memo with this privately owned collection, not in a record field
    // (which would affect equality/hash codes) or a shared cache. Copies can share
    // the collection, but only its exact issuing input may reuse the digest.
    private sealed class SnapshotEntries(ImmutableArray<CharacterCurrentRuleCatalogEntry> entries)
        : System.Collections.ObjectModel.ReadOnlyCollection<CharacterCurrentRuleCatalogEntry>(entries)
    {
        public CharacterCurrentRuleCatalogInput? Input { get; private set; }
        public string? Digest { get; private set; }

        public void Bind(CharacterCurrentRuleCatalogInput input)
        {
            Digest = input.ComputeContentDigest();
            Input = input;
        }
    }
}

/// <summary>A catalog observation. Only its issuing retained resolution can attest its identity.</summary>
public sealed record CharacterCurrentRuleCatalogValidation(
    CharacterCurrentRuleCatalogInput Input,
    IReadOnlyList<CharacterCurrentRuleCatalogEntry> Entries,
    IReadOnlyList<string> Blockers, string GrammarProfile)
{
    public CharacterCurrentRuleDomain Domain => Input.Domain;
    public bool IsComplete => Input.Blockers.Count == 0 && Blockers.Count == 0
        && !string.IsNullOrWhiteSpace(GrammarProfile);
    public string ContentDigest => Entries is SnapshotEntries snapshot
        && ReferenceEquals(snapshot.Report, this) && snapshot.Digest is { } digest
            ? digest : ComputeContentDigest();

    // Only the exact factory-issued report has a deeply detached input and
    // output graph. A public constructor or with-copy cannot inherit this proof.
    internal bool IsDetachedSnapshot8005 => Entries is SnapshotEntries snapshot
        && ReferenceEquals(snapshot.Report, this) && Input.IsDetachedSnapshot;

    // Preserve the exact input used by the issuer. Public/copy inputs cannot
    // justify a memo, even if their collections currently look read-only.
    public static CharacterCurrentRuleCatalogValidation CreateSnapshot(
        CharacterCurrentRuleCatalogInput input,
        IReadOnlyList<CharacterCurrentRuleCatalogEntry> entries,
        IReadOnlyList<string> blockers, string grammarProfile)
    {
        var snapshotEntries = new SnapshotEntries(entries.ToImmutableArray());
        var report = new CharacterCurrentRuleCatalogValidation(input, snapshotEntries,
            blockers.ToImmutableArray(), grammarProfile);
        if (input.IsDetachedSnapshot) snapshotEntries.Bind(report);
        return report;
    }

    private string ComputeContentDigest() => CharacterCreationFinalizationDigest.Compute(new
        { Input = Input.ContentDigest, Entries, Blockers, GrammarProfile });

    // No record memo field or shared cache: copies retain record equality but
    // cannot reuse the issuing report's digest or gain its observation identity.
    private sealed class SnapshotEntries(ImmutableArray<CharacterCurrentRuleCatalogEntry> entries)
        : System.Collections.ObjectModel.ReadOnlyCollection<CharacterCurrentRuleCatalogEntry>(entries)
    {
        public CharacterCurrentRuleCatalogValidation? Report { get; private set; }
        public string? Digest { get; private set; }

        public void Bind(CharacterCurrentRuleCatalogValidation report)
        {
            Digest = report.ComputeContentDigest();
            Report = report;
        }
    }
}

/// <summary>Actual Application kernel output. No public constructor or deserialization success path.</summary>
public sealed class CharacterCurrentRuleDomainExecution
{
    private readonly object contentDigestLock = new();
    private string? contentDigest;

    internal CharacterCurrentRuleDomainExecution(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, CharacterCurrentRuleConsumptionState state,
        IReadOnlyList<string> selectedKeys, string canonicalOutcomeDigest, IReadOnlyList<string> blockers)
    {
        Domain = catalog.Domain;
        ContextDigest = current.BindingDigest;
        CatalogDigest = catalog.ContentDigest;
        State = state;
        SelectedKeys = Array.AsReadOnly(selectedKeys.ToArray());
        CanonicalOutcomeDigest = canonicalOutcomeDigest;
        Blockers = Array.AsReadOnly(blockers.ToArray());
    }

    public CharacterCurrentRuleDomain Domain { get; }
    public string ContextDigest { get; }
    public string CatalogDigest { get; }
    public CharacterCurrentRuleConsumptionState State { get; }
    public IReadOnlyList<string> SelectedKeys { get; }
    public string CanonicalOutcomeDigest { get; }
    public IReadOnlyList<string> Blockers { get; }
    public bool IsComplete => Blockers.Count == 0 && !string.IsNullOrWhiteSpace(CanonicalOutcomeDigest);
    public string ContentDigest
    {
        get
        {
            lock (contentDigestLock)
            {
                // Cache only a successful digest of this execution's immutable snapshot.
                return contentDigest ??= CharacterCreationFinalizationDigest.Compute(new
                    { Domain, ContextDigest, CatalogDigest, State, SelectedKeys, CanonicalOutcomeDigest, Blockers });
            }
        }
    }
}

/// <summary>Domain implementations run existing kernels against original current state.</summary>
public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    // Deliberately no public success factory or callback-based execution adapter.
}
