using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using AuxiliarySnapshot7985 = Chummer.Application.Characters.CharacterCreationCompleteRuleSourceExecution.ProfileSettingInputValidation1203.FreshJsonComparisonStream;

// The engine implements an internal issuance seam; a public source/digest/report
// cannot opt into reuse. This grants access to the implementation assembly, not
// to caller-supplied implementations of the public resolver interfaces.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Chummer.Infrastructure")]

namespace Chummer.Application.Characters;

internal interface IRetainedCurrentRuleEvaluationIssuer2764
{
    IDisposable? BeginRetainedEvaluation2764(CharacterCurrentRuleEnvironmentContext current);
    ICharacterSourceDataResolverOperationScope? BeginImportedSheetOperation6371(CharacterCurrentRuleEnvironmentContext current);
}

// Implemented only by the concrete retained source context. This is a fresh
// byte-owner/catalog observation, not a caller digest or projection authority.
internal interface IRetainedCurrentSourceSnapshot8110
{
    bool IsCurrentRetainedSourceSnapshot8110();
}

// Only the concrete private source implements this query. Its retained byte
// owner and deeply immutable projection allow a fresh query without copying the
// whole loadout on every ammunition row. This does not issue evaluation authority.
internal interface IRetainedCurrentLoadoutSource7956
{
    bool TryResolveRetainedLoadoutAuthority7956(out CharacterCreationLoadoutAuthority authority);
}

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    // No ambient/global lookup, owner lease or cross-evaluation cache. The issuer
    // authenticates its concrete capture, immutable retained bytes, exact source,
    // query implementation and context before installing this disposable scope.
    // Current.IsUnchanged still binds character, auxiliary state and revisions on
    // every hit. An observed invalidation poisons the entire scope, including ABA.
    internal sealed class RetainedEvaluation2764 : IDisposable
    {
        private readonly CharacterCurrentRuleEnvironmentContext current;
        private readonly ICharacterSourceDataContext source;
        private readonly Func<bool> issuerIsCurrent;
        private Func<RetainedEvaluation2764, ICharacterSourceDataResolverOperationScope>? beginImportedSheet6371;
        private readonly int thread = Environment.CurrentManagedThreadId;
        private readonly HashSet<CharacterCurrentRuleCatalogValidation> skills = new(ReferenceEqualityComparer.Instance);
        // Evaluate validates then executes each domain in turn. One exact report
        // is sufficient; do not retain an unbounded set of catalogs or options.
        private CharacterCurrentRuleCatalogValidation? talents;
        private bool retired;
        private bool invalidated;
        private AuxiliaryDigestObservation7985? auxiliaryDigest7985;
        private const int MaximumMutableBindingBytes8102 = 8 * 1024 * 1024;
        private byte[]? mutableBinding8102;
        internal object? NativeMethod { get; set; }
        // Private selected-source calculation, valid only in this exact evaluation.
        // Store/write authority and subsequent evaluations never reuse it.
        internal object? CareerEffects { get; set; }
        internal CharacterSheetOwnerGraph? NativeOwners8048 { get; set; }
        internal object? ProfileQuality6223 { get; set; }
        internal CharacterCreationLoadoutRules.ValidatedLoadoutEvaluation6218? Loadout6218 { get; set; }
        internal CharacterCreationLegacySourceProjector.RetainedAmmunitionCategories7961? AmmunitionCategories7961 { get; set; }
        internal MagicAuthorityValidation6242? Magic6242 { get; set; }

        internal RetainedEvaluation2764(CharacterCurrentRuleEnvironmentContext current,
            ICharacterSourceDataContext source, Func<bool> issuerIsCurrent,
            Func<RetainedEvaluation2764, ICharacterSourceDataResolverOperationScope>? beginImportedSheet6371 = null)
        {
            if (current.RetainedEvaluation2764 is not null)
                throw new InvalidOperationException("A retained evaluation is already active.");
            this.current = current;
            this.source = source;
            this.issuerIsCurrent = issuerIsCurrent;
            this.beginImportedSheet6371 = beginImportedSheet6371;
            current.RetainedEvaluation2764 = this;
        }

        internal bool Allows(CharacterCurrentRuleEnvironmentContext requested, ICharacterSourceDataContext candidate)
        {
            // Foreign callers must retain their own source observation; reference
            // identity is a condition AFTER private issuance, never its substitute.
            if (retired || !ReferenceEquals(requested, current) || !ReferenceEquals(candidate, source)
                || Environment.CurrentManagedThreadId != thread) return false;
            try
            {
                if (!invalidated && current.IsUnchanged && issuerIsCurrent()) return true;
            }
            catch { Invalidate6371(); throw; }
            Invalidate6371();
            return false;
        }

        internal void Invalidate6371()
        {
            invalidated = true;
            auxiliaryDigest7985?.Dispose();
            auxiliaryDigest7985 = null;
            ClearMutableBinding8102();
            beginImportedSheet6371 = null;
            NativeMethod = null;
            CareerEffects = null;
            NativeOwners8048 = null;
            ProfileQuality6223 = null;
            Loadout6218 = null;
            AmmunitionCategories7961 = null;
            Magic6242 = null;
            skills.Clear();
            talents = null;
        }

        // Byte comparison only: every field is freshly serialized before reuse.
        // Do not call Allows here: its IsUnchanged check invokes these methods.
        private bool CanObserveMutableBinding8102(CharacterCurrentRuleEnvironmentContext requested)
            => !retired && !invalidated && ReferenceEquals(requested, current)
                && ReferenceEquals(current.RetainedEvaluation2764, this)
                && Environment.CurrentManagedThreadId == thread;

        internal byte[]? MutableBindingBaseline8102(CharacterCurrentRuleEnvironmentContext requested)
            => CanObserveMutableBinding8102(requested) ? mutableBinding8102 : null;

        internal void RememberMutableBinding8102(CharacterCurrentRuleEnvironmentContext requested,
            ReadOnlySpan<byte> originalBytes)
        {
            if (CanObserveMutableBinding8102(requested) && mutableBinding8102 is null
                && originalBytes.Length <= MaximumMutableBindingBytes8102)
                mutableBinding8102 = originalBytes.ToArray();
        }

        private void ClearMutableBinding8102()
        {
            if (mutableBinding8102 is not null)
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(mutableBinding8102);
            mutableBinding8102 = null;
        }

        // Digest-only observation inside the already installed scope. Calling
        // Allows here would recurse through IsUnchanged and the issuer callback.
        // These guards limit reuse; they grant no source, owner or write authority.
        internal string ObserveAuxiliaryDigest7985(CharacterCurrentRuleEnvironmentContext requested)
        {
            if (retired || invalidated || !ReferenceEquals(requested, current)
                || !ReferenceEquals(current.RetainedEvaluation2764, this)
                || Environment.CurrentManagedThreadId != thread)
                return requested.Current.Document.AuxiliaryStateDigest;
            try
            {
                return (auxiliaryDigest7985 ??= new()).Compute(current.Current.Document.AuxiliaryState);
            }
            catch { Invalidate6371(); throw; }
        }

        // Own only one successfully captured baseline for this synchronous
        // evaluation. The full mutable graph is still serialized on every call.
        private sealed class AuxiliaryDigestObservation7985 : IDisposable
        {
            private const int MaximumRetainedBytes = 8 * 1024 * 1024;
            private byte[]? serialization;
            private string? digest;
            private bool retentionDisabled;
            private bool disposed;

            internal string Compute(WorkspaceDocumentAuxiliaryState? state)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                // Oversized first observations keep the original behavior for
                // this evaluation, without retaining or refreshing a baseline.
                if (retentionDisabled) return WorkspaceDocumentAuxiliaryStateDigest.Compute(state);
                using var snapshot = new AuxiliarySnapshot7985(serialization);
                System.Text.Json.JsonSerializer.Serialize(snapshot,
                    state ?? WorkspaceDocumentAuxiliaryState.Empty,
                    WorkspaceDocumentAuxiliaryStateJsonConverter.TypeInfo);
                if (snapshot.IsExactMatch) return digest!;

                ReadOnlyMemory<byte> bytes = snapshot.GetCapturedJson();
                string observed = WorkspaceDocumentAuxiliaryStateDigest.ComputeCapturedJson7985(bytes);
                if (serialization is null)
                {
                    if (bytes.Length <= MaximumRetainedBytes)
                    {
                        serialization = bytes.ToArray();
                        digest = observed;
                    }
                    else retentionDisabled = true;
                }
                // Even canonically equivalent reordering must not replace the
                // admitted bytes. A mismatch uses THIS snapshot, never a reread.
                return observed;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (serialization is not null)
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(serialization);
                serialization = null;
                digest = null;
            }
        }

        internal ICharacterSourceDataResolverOperationScope? BorrowImportedSheetOperation6371(
            CharacterCurrentRuleEnvironmentContext requested, ICharacterSourceDataContext candidate)
        {
            if (!Allows(requested, candidate) || beginImportedSheet6371 is not { } begin) return null;
            try { return begin(this); }
            catch { Invalidate6371(); throw; }
        }

        internal void RememberSkills(CharacterCurrentRuleCatalogValidation catalog) => skills.Add(catalog);
        internal bool OwnsSkills(CharacterCurrentRuleCatalogValidation catalog) => skills.Contains(catalog);

        internal void RememberTalents3238(CharacterCurrentRuleCatalogValidation catalog)
            => talents = catalog.Input.IsDetachedSnapshot ? catalog : null;

        internal bool OwnsTalents3238(CharacterCurrentRuleCatalogValidation catalog)
            => ReferenceEquals(talents, catalog);

        internal void VerifyBoundary()
        {
            if (!Allows(current, source))
                throw new InvalidOperationException("Retained evaluation inputs changed.");
        }

        public void Dispose()
        {
            if (retired) return;
            retired = true;
            auxiliaryDigest7985?.Dispose();
            auxiliaryDigest7985 = null;
            ClearMutableBinding8102();
            beginImportedSheet6371 = null;
            NativeMethod = null;
            CareerEffects = null;
            NativeOwners8048 = null;
            ProfileQuality6223 = null;
            Loadout6218 = null;
            AmmunitionCategories7961 = null;
            Magic6242 = null;
            skills.Clear();
            talents = null;
            if (ReferenceEquals(current.RetainedEvaluation2764, this)) current.RetainedEvaluation2764 = null;
        }
    }

    private static RetainedEvaluation2764? RetainedEvaluationFor2764(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source)
        => current.RetainedEvaluation2764 is { } evaluation && evaluation.Allows(current, source) ? evaluation : null;

    // This proof retains no authority graph. The concrete private issuer binds
    // the immutable source inputs and exact SourceDataContext for this evaluation.
    // That source deep-copies its private magic snapshot on admission AND return
    // (including talent/quality/gear grants, catalog skill XML and policy anchors).
    // Each caller keeps its fresh graph; mutating one cannot affect a later query.
    // Digest equality is only a consistency check after that issuance, never an
    // admission mechanism for public providers or caller-supplied authorities.
    // CharacterRetainedSettingsFile is a sealed record containing only strings;
    // equality includes namespace, filename, source hash, author ID and name.
    internal sealed record MagicAuthorityValidation6242(
        string AuthorityDigest, CharacterRetainedSettingsFile? SettingsFile)
    {
        internal bool Matches(CharacterCreationMagicResonanceAuthority authority,
            CharacterRetainedSettingsFile? settingsFile)
            => AuthorityDigest == authority.AuthorityDigest && SettingsFile == settingsFile;
    }

    private static bool TryCurrentMagicAuthority6242(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, out CharacterCreationMagicResonanceAuthority authority)
    {
        // Preserve the original query, short circuit and selected-settings query
        // order, including IsSelectedBy validation, on every invocation.
        if (!source.TryResolveCreationMagicResonanceAuthority(out authority)) return false;
        var settingsFile = ImportedSettingsFile5691(current, source);
        var evaluation = RetainedEvaluationFor2764(current, source);
        if (evaluation?.Magic6242 is { } validated && validated.Matches(authority, settingsFile)) return true;
        if (!CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(authority, settingsFile)) return false;
        if (evaluation is not null && ReferenceEquals(evaluation, RetainedEvaluationFor2764(current, source)))
            evaluation.Magic6242 ??= new(authority.AuthorityDigest, settingsFile);
        return true;
    }

    internal static bool TryCurrentLoadoutAuthority6218(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, out CharacterCreationLoadoutAuthority authority)
    {
        var evaluation = RetainedEvaluationFor2764(current, source);
        // Retain a fresh source query and both current-binding checks. Only an
        // already issued evaluation of this exact private source may request its
        // immutable retained projection; public/live providers keep full replay.
        bool resolved = evaluation is not null && source is IRetainedCurrentLoadoutSource7956 retained
            ? retained.TryResolveRetainedLoadoutAuthority7956(out authority)
            : source.TryResolveCreationLoadoutAuthority(out authority);
        if (!resolved) return false;
        if (evaluation is null) return CharacterCreationLoadoutRules.IsValidAuthority(authority);
        if (!ReferenceEquals(evaluation, RetainedEvaluationFor2764(current, source))) return false;
        var validated = evaluation.Loadout6218;
        if (validated is null)
        {
            validated = CharacterCreationLoadoutRules.ValidatedLoadoutEvaluation6218.Create(authority);
            if (validated is null
                || !ReferenceEquals(evaluation, RetainedEvaluationFor2764(current, source))) return false;
            evaluation.Loadout6218 = validated;
        }
        else if (authority.AuthorityDigest != validated.Authority.AuthorityDigest) return false;
        // Digest equality is only a consistency check AFTER concrete immutable
        // source/query issuance and the current mutable-binding checks above.
        authority = validated.Authority;
        return true;
    }

    // Only a catalog lookup built from this exact owned authority may use this
    // proof for its options. The 6218 factory validated every immutable option;
    // equal digests and caller-owned/copied authorities cannot transfer it.
    private static RetainedEvaluation2764? CurrentLoadoutOptionProof6237(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source,
        CharacterCreationLoadoutAuthority authority)
    {
        var evaluation = RetainedEvaluationFor2764(current, source);
        return evaluation?.Loadout6218 is { } validated && ReferenceEquals(authority, validated.Authority)
            ? evaluation : null;
    }

    private static bool TryCurrentLoadoutBasket6218(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, CharacterCreationLoadoutSelections selections,
        CharacterCreationLoadoutAuthority authority, out CharacterCreationLoadoutLine[] lines,
        out decimal cost, out string[] blockers)
    {
        var validated = RetainedEvaluationFor2764(current, source)?.Loadout6218;
        return validated is not null && ReferenceEquals(authority, validated.Authority)
            ? validated.TryProjectBasket(selections, out lines, out cost, out blockers)
            : CharacterCreationLoadoutRules.TryProjectBasket(selections, authority, out lines, out cost, out blockers);
    }

    // The native observation is private and its consumers only read it. Metatype
    // quotes are the escaping graph: detach every nested collection there, not
    // merely the outer record. Scalar records and strings are immutable.
    private static CharacterCreationMetatypeOptionProjection DetachNativeMetatype2764(
        CharacterCreationMetatypeOptionProjection metatype)
        => metatype with
        {
            Attributes = Array.AsReadOnly(metatype.Attributes.ToArray()),
            GrantedQualities = Array.AsReadOnly(metatype.GrantedQualities.Select(quality => quality with
            {
                SourceAnchorIds = Array.AsReadOnly(quality.SourceAnchorIds.ToArray())
            }).ToArray()),
            ExcludedMetavariants = Array.AsReadOnly(metatype.ExcludedMetavariants.Select(variant => variant with
            {
                Blockers = Array.AsReadOnly(variant.Blockers.ToArray()),
                SourceAnchorIds = Array.AsReadOnly(variant.SourceAnchorIds.ToArray())
            }).ToArray()),
            Blockers = Array.AsReadOnly(metatype.Blockers.ToArray()),
            SourceAnchorIds = Array.AsReadOnly(metatype.SourceAnchorIds.ToArray())
        };
}
