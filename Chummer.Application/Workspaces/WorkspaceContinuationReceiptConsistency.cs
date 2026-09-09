using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Intrinsic commitments shared across receipt families. This neither proves
/// historical execution nor grants permission to replay any recorded command.
/// Call only after complete per-family shape and ledger validation.
/// </summary>
internal static class WorkspaceContinuationReceiptConsistency
{
    internal static bool IsValid(WorkspaceContinuationSnapshot candidate)
    {
        var revisions = new Dictionary<long, string>();
        if (!AddState(candidate.Workspace.Document.AuxiliaryState, revisions))
            return false;
        foreach (var receipt in candidate.DelegatedGmCharacterEdits)
            if (!Add(revisions, receipt.NewRevision, "delegated-gm"))
                return false;

        var latest = candidate.DelegatedGmCharacterEdits.LastOrDefault();
        if (latest is null || latest.NewRevision != candidate.Workspace.ContentRevision)
            return true;

        // GM mutations deliberately do not advance SavedRevision. Compare the
        // exact profile value, not display helpers that trim whitespace, and do
        // not reinterpret a later owner's edit as a corrupt historical receipt.
        if (candidate.Workspace.Document.Format != WorkspaceDocumentFormat.NativeXml)
            return false;
        using var reader = XmlReader.Create(new StringReader(candidate.Workspace.Document.Content),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        XElement? root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
        if (root?.Name.LocalName != "character")
            return false;
        foreach (var operation in latest.Operations)
        {
            string? name = operation.Path switch
            {
                DelegatedGmCharacterEditContract.ProfileNamePath => "name",
                DelegatedGmCharacterEditContract.ProfileAliasPath => "alias",
                DelegatedGmCharacterEditContract.ProfileNotesPath => "notes",
                _ => null
            };
            if (name is null)
                return false;
            XElement[] nodes = root.Elements(name).ToArray();
            if (nodes.Length != 1 || nodes[0].HasElements)
                return false;
            string value = nodes[0].Value;
            if (value.Length != operation.ValueLength
                || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value))),
                    operation.ValueSha256, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static bool AddState(WorkspaceDocumentAuxiliaryState state, Dictionary<long, string> revisions)
    {
        // A Foundation update may also append a Life Module acceptance in the
        // SAME atomic lane. Draft and receipt therefore share a lane identifier.
        // Other typed commits each own a distinct content revision, including
        // transactions preserved in the pre-finalization archive.
        if (state.CharacterCreationFoundationDraft is { } foundation
            && !Add(revisions, checked(foundation.BaseContentRevision + 1), "foundation")) return false;
        if (state.CharacterCreationPrerequisiteDraft is { } prerequisite
            && !Add(revisions, checked(prerequisite.BaseContentRevision + 1), "prerequisite")) return false;
        if (state.CharacterCreationAttributesDraft is { } attributes
            && !Add(revisions, checked(attributes.BaseContentRevision + 1), "attributes")) return false;
        if (!AddAll(revisions, state.LifeModuleDecisionAcceptances?.Select(item => item.Receipt.WorkspaceRevision), "foundation")
            || !AddAll(revisions, state.CharacterCreationSkillsReceipts?.Select(item => item.ContentRevision), "skills")
            || !AddAll(revisions, state.CharacterCreationMagicResonanceReceipts?.Select(item => item.ContentRevision), "magic-resonance")
            || !AddAll(revisions, state.CharacterCreationQualitiesReceipts?.Select(item => item.ContentRevision), "qualities")
            || !AddAll(revisions, state.CharacterCreationContactReceipts?.Select(item => item.Receipt.ContentRevision), "contacts")
            || !AddAll(revisions, state.CharacterCreationLifestyleReceipts?.Select(item => item.Receipt.ContentRevision), "lifestyles")
            || !AddAll(revisions, state.CharacterCreationResourcesReceipts?.Select(item => item.Receipt.WorkspaceRevision), "resources")
            || !AddAll(revisions, state.CharacterCreationGearReceipts?.Select(item => item.Receipt.WorkspaceRevision), "gear")
            || !AddAll(revisions, state.CharacterCreationFinalizationReceipts?.Select(item => item.Receipt.ContentRevision), "finalization")
            || !AddAll(revisions, state.CharacterAfterRunRewardReceipts?.Select(item => item.CommittedWorkspaceRevision), "after-run-reward")
            || !AddAll(revisions, state.CharacterAfterRunSettlementReceipts?.Select(item => item.CommittedWorkspaceRevision), "after-run-settlement")
            || !AddAll(revisions, state.CharacterCareerReputationReceipts?.Select(item => item.CommittedWorkspaceRevision), "career-reputation"))
            return false;
        return state.CharacterCreationFinalizationArchive is not { } archive
            || AddState(archive.State, revisions);
    }

    private static bool AddAll(Dictionary<long, string> revisions, IEnumerable<long>? values, string lane)
        => values is null || values.All(revision => Add(revisions, revision, lane));

    private static bool Add(Dictionary<long, string> revisions, long revision, string lane)
        => revision > 0 && (revisions.TryAdd(revision, lane)
            || string.Equals(revisions[revision], lane, StringComparison.Ordinal));
}
