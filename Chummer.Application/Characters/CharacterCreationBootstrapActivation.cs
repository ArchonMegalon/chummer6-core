using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static class CharacterCreationBootstrapActivationSchemas
{
    public const string BundleV1 = "chummer.character-creation-bootstrap-activation.v1";
    public const string RecoveryBindingV1 =
        "chummer.character-creation-bootstrap-recovery-binding.v1";
    public const string InitialProjectionV1 =
        "chummer.character-creation-bootstrap-initial-projection.v1";
    public const string SourceAuthorityV1 =
        "chummer.character-creation-bootstrap-source-authority.v1";
}

public sealed record CharacterCreationBootstrapSourceAuthorityBinding(
    string Schema,
    string RawCharacterXmlDigest,
    string SourceSnapshotDigest,
    string SourceProfileDigest,
    string MetatypeAuthorityDigest,
    string PrerequisiteAuthorityDigest,
    string QualitiesAuthorityDigest,
    string MagicResonanceAuthorityDigest,
    string LifeModulesAuthorityDigest,
    string AggregateDigest);

/// <summary>
/// Exact recovery and source authority carried from the atomic creation commit.
/// The document digest covers the complete workspace envelope, including its
/// auxiliary creation binding; the raw XML and auxiliary digests remain explicit
/// so a consumer cannot accidentally compare only display payload bytes.
/// </summary>
public sealed record CharacterCreationBootstrapRecoveryBinding(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string WorkspaceDocumentDigest,
    string WorkspaceOverviewDigest,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    string BootstrapBindingDigest,
    string ReceiptDigest,
    string RawProfileInputsDigest,
    string MetatypeAuthorityDigest,
    string PrerequisiteAuthorityDigest,
    IReadOnlyList<string> SourceAnchorIds);

/// <summary>
/// Complete initial Creation state projected from one frozen workspace snapshot
/// and one source-data context. Results, not only successful values, are retained
/// so every domain keeps its exact outcome and blocker semantics.
/// </summary>
public sealed record CharacterCreationInitialProjection(
    string Schema,
    CharacterCreationBootstrapSourceAuthorityBinding SourceAuthority,
    CharacterCreationFoundationResult<CharacterCreationFoundationState> Foundation,
    CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> Prerequisite,
    CharacterCreationFoundationResult<CharacterCreationAttributesState> Attributes,
    CharacterCreationContactResult<CharacterCreationContactsState> Contacts,
    CharacterCreationFoundationResult<CharacterCreationQualitiesState> Qualities,
    CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> MagicResonance);

/// <summary>
/// Atomic creation output that can activate the newly-created workspace without
/// immediately rebuilding it through the generic multi-read loader. Consumers
/// must still perform one independent store read and exact-match it before use.
/// </summary>
public sealed record CharacterCreationBootstrapActivationBundle(
    string Schema,
    CharacterCreationBootstrapReceipt Receipt,
    WorkspaceOverviewProjection WorkspaceProjection,
    CharacterCreationBootstrapRecoveryBinding RecoveryBinding,
    CharacterCreationInitialProjection InitialCreation,
    string BundleDigest);

public sealed record CharacterCreationBootstrapActivationAttempt(
    string Outcome,
    CharacterCreationBootstrapReceipt? Receipt,
    CharacterCreationBootstrapActivationBundle? Bundle,
    IReadOnlyList<string> Blockers)
{
    public bool CreatedRequiresReload =>
        string.Equals(Outcome, CharacterCreationBootstrapOutcomes.Success, StringComparison.Ordinal)
        && Receipt is not null
        && Bundle is null;
}

public interface ICharacterCreationBootstrapActivationService
{
    CharacterCreationBootstrapActivationAttempt CreateActivation(
        CharacterCreationBootstrapRequest request);

    bool TryValidateCurrent(
        CharacterCreationBootstrapActivationBundle activation,
        out IReadOnlyList<string> blockers);
}

public interface ICharacterCreationBootstrapActivationProjector
{
    CharacterCreationInitialProjection Project(
        WorkspaceStoredDocument workspace,
        CharacterCreationBootstrapSourceSnapshot sourceSnapshot);

    bool IsCurrent(
        CharacterCreationInitialProjection projection,
        ICharacterSourceDataContext sourceContext,
        string characterXml);
}

public static class CharacterCreationBootstrapActivationIntegrity
{
    public static string ComputeDocumentDigest(WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(document);
    }

    public static string ComputeOverviewDigest(CharacterOverviewProjection overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(overview);
    }

    public static string ComputeBundleDigest(CharacterCreationBootstrapActivationBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            bundle with { BundleDigest = string.Empty });
    }

    public static bool IsValid(CharacterCreationBootstrapActivationBundle? bundle)
    {
        try
        {
            return IsValidCore(bundle);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidCore(CharacterCreationBootstrapActivationBundle? bundle)
    {
        if (bundle is null
            || bundle.Receipt is null
            || bundle.WorkspaceProjection is null
            || bundle.RecoveryBinding is null
            || bundle.InitialCreation is null
            || bundle.InitialCreation.SourceAuthority is null
            || !string.Equals(
                bundle.Schema,
                CharacterCreationBootstrapActivationSchemas.BundleV1,
                StringComparison.Ordinal)
            || !CharacterCreationBootstrapReceiptDigest.IsValid(bundle.Receipt)
            || !IsCanonicalDigest(bundle.BundleDigest)
            || !FixedTimeEquals(bundle.BundleDigest, ComputeBundleDigest(bundle)))
        {
            return false;
        }

        WorkspaceDocumentSnapshot snapshot = bundle.WorkspaceProjection.Workspace;
        CharacterCreationBootstrapRecoveryBinding recovery = bundle.RecoveryBinding;
        CharacterCreationBootstrapReceipt receipt = bundle.Receipt;
        CharacterCreationBootstrapBinding? documentBootstrapBinding =
            snapshot?.Document?.AuxiliaryState?.CharacterCreationBootstrapBinding;
        if (snapshot is null
            || snapshot.Document is null
            || bundle.WorkspaceProjection.Overview is null
            || bundle.WorkspaceProjection.Validation is null
            || recovery.SourceAnchorIds is null
            || receipt.SourceAnchorIds is null
            || receipt.Binding is null
            || documentBootstrapBinding is null
            || !CharacterCreationBootstrapBindingDigest.IsValid(documentBootstrapBinding)
            || !FixedTimeEquals(
                documentBootstrapBinding.BindingDigest,
                receipt.Binding.BindingDigest)
            || !string.Equals(
                recovery.Schema,
                CharacterCreationBootstrapActivationSchemas.RecoveryBindingV1,
                StringComparison.Ordinal)
            || !string.Equals(
                bundle.InitialCreation.Schema,
                CharacterCreationBootstrapActivationSchemas.InitialProjectionV1,
                StringComparison.Ordinal)
            || snapshot.Id != receipt.WorkspaceId
            || snapshot.ContentRevision != receipt.ContentRevision
            || snapshot.SavedRevision != receipt.SavedRevision
            || recovery.WorkspaceId != snapshot.Id
            || recovery.ContentRevision != snapshot.ContentRevision
            || recovery.SavedRevision != snapshot.SavedRevision
            || !bundle.WorkspaceProjection.Validation.IsValid
            || !FixedTimeEquals(
                recovery.WorkspaceDocumentDigest,
                ComputeDocumentDigest(snapshot.Document))
            || !FixedTimeEquals(
                recovery.WorkspaceOverviewDigest,
                ComputeOverviewDigest(bundle.WorkspaceProjection.Overview))
            || !FixedTimeEquals(
                recovery.RawCharacterXmlDigest,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                    snapshot.Document.Content))
            || !FixedTimeEquals(
                recovery.RawCharacterXmlDigest,
                receipt.Binding.RawCharacterXmlDigest)
            || !FixedTimeEquals(
                recovery.AuxiliaryStateDigest,
                snapshot.Document.AuxiliaryStateDigest)
            || !FixedTimeEquals(
                recovery.BootstrapBindingDigest,
                receipt.Binding.BindingDigest)
            || !FixedTimeEquals(recovery.ReceiptDigest, receipt.ReceiptDigest)
            || !FixedTimeEquals(
                recovery.RawProfileInputsDigest,
                receipt.Binding.RawProfileInputsDigest)
            || !FixedTimeEquals(
                recovery.MetatypeAuthorityDigest,
                receipt.Binding.MetatypeAuthorityDigest)
            || !FixedTimeEquals(
                recovery.PrerequisiteAuthorityDigest,
                receipt.Binding.PrerequisiteAuthorityDigest)
            || !recovery.SourceAnchorIds.SequenceEqual(
                receipt.SourceAnchorIds,
                StringComparer.Ordinal))
        {
            return false;
        }

        return SourceAuthorityIsValid(bundle.InitialCreation, receipt)
               && ResultsAreInternallyBound(bundle.InitialCreation, snapshot);
    }

    private static bool SourceAuthorityIsValid(
        CharacterCreationInitialProjection projection,
        CharacterCreationBootstrapReceipt receipt)
    {
        CharacterCreationBootstrapSourceAuthorityBinding source = projection.SourceAuthority;
        if (!string.Equals(
                source.Schema,
                CharacterCreationBootstrapActivationSchemas.SourceAuthorityV1,
                StringComparison.Ordinal)
            || !IsCanonicalDigest(source.RawCharacterXmlDigest)
            || !IsCanonicalDigest(source.SourceSnapshotDigest)
            || !IsCanonicalDigest(source.SourceProfileDigest)
            || !IsCanonicalDigest(source.MetatypeAuthorityDigest)
            || !IsCanonicalDigest(source.PrerequisiteAuthorityDigest)
            || !IsCanonicalDigest(source.QualitiesAuthorityDigest)
            || !IsCanonicalDigest(source.MagicResonanceAuthorityDigest)
            || !IsCanonicalDigest(source.LifeModulesAuthorityDigest)
            || !IsCanonicalDigest(source.AggregateDigest)
            || !FixedTimeEquals(
                source.AggregateDigest,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                    source with { AggregateDigest = string.Empty }))
            || !FixedTimeEquals(source.RawCharacterXmlDigest, receipt.Binding.RawCharacterXmlDigest)
            || !FixedTimeEquals(source.MetatypeAuthorityDigest, receipt.Binding.MetatypeAuthorityDigest)
            || !FixedTimeEquals(
                source.PrerequisiteAuthorityDigest,
                receipt.Binding.PrerequisiteAuthorityDigest))
        {
            return false;
        }

        CharacterCreationQualitiesAuthority? qualities = projection.Qualities?.Value?.Authority;
        CharacterCreationMagicResonanceAuthority? magic = projection.MagicResonance?.Value?.Authority;
        return qualities is not null
               && magic is not null
               && FixedTimeEquals(source.QualitiesAuthorityDigest, qualities.AuthorityDigest)
               && FixedTimeEquals(source.MagicResonanceAuthorityDigest, magic.AuthorityDigest);
    }

    private static bool ResultsAreInternallyBound(
        CharacterCreationInitialProjection projection,
        WorkspaceDocumentSnapshot snapshot)
    {
        if (projection.Foundation is null
            || projection.Prerequisite is null
            || projection.Attributes is null
            || projection.Contacts is null
            || projection.Qualities is null
            || projection.MagicResonance is null
            || projection.Foundation.Blockers is null
            || projection.Prerequisite.Blockers is null
            || projection.Attributes.Blockers is null
            || projection.Contacts.Blockers is null
            || projection.Qualities.Blockers is null
            || projection.MagicResonance.Blockers is null
            || !string.Equals(
                projection.Foundation.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || !string.Equals(
                projection.Prerequisite.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || !string.Equals(
                projection.Attributes.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || !string.Equals(
                projection.Contacts.Outcome,
                CharacterCreationContactOutcomes.Available,
                StringComparison.Ordinal)
            || !string.Equals(
                projection.Qualities.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || !string.Equals(
                projection.MagicResonance.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal))
        {
            return false;
        }

        CharacterCreationFoundationState? foundation = projection.Foundation.Value;
        CharacterCreationPrerequisiteState? prerequisite = projection.Prerequisite.Value;
        CharacterCreationAttributesState? attributes = projection.Attributes.Value;
        CharacterCreationContactsState? contacts = projection.Contacts.Value;
        CharacterCreationQualitiesState? qualities = projection.Qualities.Value;
        CharacterCreationMagicResonanceState? magic = projection.MagicResonance.Value;
        if (foundation is null
            || prerequisite is null
            || attributes is null
            || contacts is null
            || qualities is null
            || magic is null)
        {
            return false;
        }

        CharacterWorkspaceId id = snapshot.Id;
        long contentRevision = snapshot.ContentRevision;
        long savedRevision = snapshot.SavedRevision;
        string rawDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(snapshot.Document.Content);
        string auxiliaryDigest = snapshot.Document.AuxiliaryStateDigest;
        return foundation.Binding is not null
            && prerequisite.Binding is not null
            && attributes.Binding is not null
            && contacts.Binding is not null
            && qualities.Binding is not null
            && qualities.Authority is not null
            && qualities.Authority.Options is not null
            && qualities.Authority.GrantedQualities is not null
            && magic.Binding is not null
            && magic.Authority is not null
            && prerequisite.Authority is not null
            && foundation.AuthorityBlockers is not null
            && prerequisite.Blockers is not null
            && attributes.Blockers is not null
            && contacts.Blockers is not null
            && qualities.Blockers is not null
            && magic.Blockers is not null
            && foundation.Binding.WorkspaceId == id
            && foundation.Binding.ContentRevision == contentRevision
            && foundation.Binding.SavedRevision == savedRevision
            && FixedTimeEquals(foundation.Binding.RawCharacterXmlDigest, rawDigest)
            && prerequisite.Binding.WorkspaceId == id
            && prerequisite.Binding.ContentRevision == contentRevision
            && prerequisite.Binding.SavedRevision == savedRevision
            && FixedTimeEquals(prerequisite.Binding.RawCharacterXmlDigest, rawDigest)
            && FixedTimeEquals(prerequisite.Binding.AuxiliaryStateDigest, auxiliaryDigest)
            && attributes.Binding.WorkspaceId == id
            && attributes.Binding.ContentRevision == contentRevision
            && attributes.Binding.SavedRevision == savedRevision
            && FixedTimeEquals(attributes.Binding.RawCharacterXmlDigest, rawDigest)
            && FixedTimeEquals(attributes.Binding.AuxiliaryStateDigest, auxiliaryDigest)
            && contacts.Binding.WorkspaceId == id
            && contacts.Binding.ContentRevision == contentRevision
            && contacts.Binding.SavedRevision == savedRevision
            && FixedTimeEquals(contacts.Binding.ContentDigest, rawDigest)
            && FixedTimeEquals(contacts.Binding.AuxiliaryStateDigest, auxiliaryDigest)
            && qualities.Binding.WorkspaceId == id
            && qualities.Binding.ContentRevision == contentRevision
            && qualities.Binding.SavedRevision == savedRevision
            && FixedTimeEquals(qualities.Binding.RawCharacterXmlDigest, rawDigest)
            && FixedTimeEquals(qualities.Binding.AuxiliaryStateDigest, auxiliaryDigest)
            && magic.Binding.WorkspaceId == id
            && magic.Binding.ContentRevision == contentRevision
            && magic.Binding.SavedRevision == savedRevision
            && FixedTimeEquals(magic.Binding.RawCharacterXmlDigest, rawDigest)
            && FixedTimeEquals(magic.Binding.AuxiliaryStateDigest, auxiliaryDigest)
            && FixedTimeEquals(
                foundation.SnapshotDigest,
                ComputeFoundationStateDigest(foundation))
            && FixedTimeEquals(
                prerequisite.SnapshotDigest,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                    prerequisite with { SnapshotDigest = string.Empty }))
            && FixedTimeEquals(
                attributes.SnapshotDigest,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                    attributes with { SnapshotDigest = string.Empty }))
            && FixedTimeEquals(
                contacts.SnapshotDigest,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                    contacts with { SnapshotDigest = string.Empty }))
            && FixedTimeEquals(
                qualities.Authority.AuthorityDigest,
                CharacterCreationQualitiesRules.ComputeAuthorityDigest(qualities.Authority))
            && qualities.Authority.Options.All(option =>
                option is not null
                && FixedTimeEquals(
                    option.OptionDigest,
                    CharacterCreationQualitiesRules.ComputeOptionDigest(option)))
            && qualities.Authority.GrantedQualities.All(grant =>
                grant is not null
                && FixedTimeEquals(
                    grant.GrantDigest,
                    CharacterCreationQualitiesRules.ComputeGrantDigest(grant)))
            && FixedTimeEquals(
                qualities.SnapshotDigest,
                CharacterCreationQualitiesRules.ComputeStateDigest(qualities))
            && FixedTimeEquals(
                magic.Authority.AuthorityDigest,
                CharacterCreationMagicResonanceDigest.Compute(
                    magic.Authority with { AuthorityDigest = string.Empty }))
            && FixedTimeEquals(
                magic.SnapshotDigest,
                CharacterCreationMagicResonanceDigest.Compute(
                    magic with { SnapshotDigest = string.Empty }))
            && FixedTimeEquals(
                prerequisite.Authority.AuthorityDigest,
                CharacterCreationPrerequisiteAuthorityDigest.Compute(
                    prerequisite.Authority))
            && BlockersMatch(projection.Foundation.Blockers, foundation.AuthorityBlockers)
            && BlockersMatch(projection.Prerequisite.Blockers, prerequisite.Blockers)
            && BlockersMatch(projection.Attributes.Blockers, attributes.Blockers)
            && BlockersMatch(projection.Contacts.Blockers, contacts.Blockers)
            && BlockersMatch(projection.Qualities.Blockers, qualities.Blockers)
            && BlockersMatch(projection.MagicResonance.Blockers, magic.Blockers);
    }

    private static string ComputeFoundationStateDigest(CharacterCreationFoundationState state)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            state.Schema,
            state.Binding,
            state.RulesetId,
            Metatype = state.CurrentMetatype,
            state.BuildMethod,
            Created = state.CharacterCreated,
            Metatypes = state.MetatypeOptions,
            Nationalities = state.NationalityOptions,
            state.LifeModuleBudget,
            state.PendingDraft,
            Blockers = state.AuthorityBlockers
        });
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static bool BlockersMatch(
        IReadOnlyList<string> resultBlockers,
        IReadOnlyList<string> stateBlockers)
        => resultBlockers is not null
           && stateBlockers is not null
           && resultBlockers
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .ToHashSet(StringComparer.Ordinal)
            .SetEquals(stateBlockers.Where(static blocker =>
                !string.IsNullOrWhiteSpace(blocker)));

    private static bool IsCanonicalDigest(string? value)
        => CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(value);

    private static bool FixedTimeEquals(string? left, string? right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        try
        {
            return leftBytes.Length == rightBytes.Length
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }
}
