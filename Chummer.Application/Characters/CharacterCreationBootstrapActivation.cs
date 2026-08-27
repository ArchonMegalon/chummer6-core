using System.Security.Cryptography;
using System.Text;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static class CharacterCreationBootstrapActivationSchemas
{
    public const string BundleV1 = "chummer.character-creation-bootstrap-activation.v1";
    public const string RecoveryBindingV1 =
        "chummer.character-creation-bootstrap-recovery-binding.v1";
    public const string InitialProjectionV1 =
        "chummer.character-creation-bootstrap-initial-projection.v1";
}

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
    IReadOnlyList<string> Blockers);

public interface ICharacterCreationBootstrapActivationService
{
    CharacterCreationBootstrapActivationAttempt CreateActivation(
        CharacterCreationBootstrapRequest request);
}

public interface ICharacterCreationBootstrapActivationProjector
{
    CharacterCreationInitialProjection Project(
        WorkspaceStoredDocument workspace,
        ICharacterSourceDataContext sourceContext);
}

public static class CharacterCreationBootstrapActivationIntegrity
{
    public static string ComputeDocumentDigest(WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(document);
    }

    public static string ComputeBundleDigest(CharacterCreationBootstrapActivationBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            bundle with { BundleDigest = string.Empty });
    }

    public static bool IsValid(CharacterCreationBootstrapActivationBundle? bundle)
    {
        if (bundle is null
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
        if (!string.Equals(
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

        return ResultsAreInternallyBound(bundle.InitialCreation, snapshot);
    }

    private static bool ResultsAreInternallyBound(
        CharacterCreationInitialProjection projection,
        WorkspaceDocumentSnapshot snapshot)
    {
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
        return foundation.Binding.WorkspaceId == id
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
            && BlockersMatch(projection.Foundation.Blockers, foundation.AuthorityBlockers)
            && BlockersMatch(projection.Prerequisite.Blockers, prerequisite.Blockers)
            && BlockersMatch(projection.Attributes.Blockers, attributes.Blockers)
            && BlockersMatch(projection.Contacts.Blockers, contacts.Blockers)
            && BlockersMatch(projection.Qualities.Blockers, qualities.Blockers)
            && BlockersMatch(projection.MagicResonance.Blockers, magic.Blockers);
    }

    private static bool BlockersMatch(
        IReadOnlyList<string> resultBlockers,
        IReadOnlyList<string> stateBlockers)
        => resultBlockers
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
