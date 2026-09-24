using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Persisted identity/shape only. Fresh SR6 rule admission belongs to the SR6 transaction.</summary>
public static class Sr6CreationFinalizationIntegrity
{
    public static string DocumentDigest(string xml) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
    public static string ReviewDigest(Sr6CreationFinalizationReview review)
        => Sr6CreationFoundationIntegrity.Digest(review with { ReviewDigest = string.Empty });
    public static string ReceiptDigest(Sr6CreationFinalizationReceipt receipt)
        => Sr6CreationFoundationIntegrity.Digest(receipt with { ReceiptDigest = string.Empty });

    public static bool IsConfirmed(Sr6CreationFinalizationRequest? request)
        => request is { ExplicitlyConfirmed: true } && request.OperationId != Guid.Empty
           && Sr6CreationFoundationIntegrity.ValidBinding(request.Binding)
           && request.Binding.ContentRevision == request.Binding.SavedRevision
           && CharacterCreationBootstrapBindingDigest.IsCanonical(request.ReviewDigest);

    public static bool IsValidArchive(CharacterWorkspaceId id, long revision, WorkspaceDocumentAuxiliaryState state)
    {
        if (state.Sr6CreationFinalizationArchive is not { } archive) return true;
        var review = archive.Review;
        var receipt = archive.Receipt;
        var before = archive.OriginalState;
        // Reject nested archives before any recursive shape validation or hashing.
        if (review?.Binding is not { } binding || receipt is null || before is null
            || before.CharacterCreationBootstrapBinding is not { RulesetId: RulesetDefaults.Sr6 } bootstrap
            || before.Sr6CreationFoundationDecisions is not { Count: > 0 }
            || !Equals(before, new WorkspaceDocumentAuxiliaryState(CharacterCreationBootstrapBinding: bootstrap,
                Sr6CreationFoundationDecisions: before.Sr6CreationFoundationDecisions))
            || !(state with { Sr6CreationFinalizationArchive = null }).IsEmpty
            || !IsConfirmed(receipt.Command) || receipt.Command.Binding != binding || binding.WorkspaceId != id
            || receipt.Command.ReviewDigest != review.ReviewDigest
            || receipt.ContentRevision != binding.ContentRevision + 1 || receipt.SavedRevision != receipt.ContentRevision
            || receipt.ContentRevision > revision || receipt.DocumentDigest != review.DocumentDigest
            || review.Blockers is not { Count: 0 } || review.Losses is null || review.Balances is null
            || review.Losses.Any(row => row is null || row.Amount <= 0 || string.IsNullOrWhiteSpace(row.DomainId) || string.IsNullOrWhiteSpace(row.PoolId))
            || review.Losses.Count > 0 && !receipt.Command.AcceptUnspentLoss
            || review.SourceAnchorIds is not { Count: > 0 }
            || archive.OriginalDocument is not { Content.Length: > 0 } || review.Document is not { Content.Length: > 0 }
            || binding.AuxiliaryStateDigest != WorkspaceDocumentAuxiliaryStateDigest.Compute(before)
            || binding.BootstrapBindingDigest != bootstrap.BindingDigest
            || DocumentDigest(archive.OriginalDocument.Content) != bootstrap.RawCharacterXmlDigest
            || DocumentDigest(review.Document.Content) != review.DocumentDigest
            || ReviewDigest(review) != review.ReviewDigest || ReceiptDigest(receipt) != receipt.ReceiptDigest
            || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(id, binding.ContentRevision, before)) return false;
        var historical = new WorkspaceDocument(archive.OriginalDocument.Content, RulesetDefaults.Sr6);
        historical = historical with { State = historical.State with
            { AuxiliaryState = before with { Sr6CreationFoundationDecisions = null } } };
        return CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(id, historical)
            && CreatedSr6(review.Document.Content);
    }

    public static bool IsValidCurrentDocument(WorkspaceDocument document, long revision)
    {
        var archive = document.AuxiliaryState.Sr6CreationFinalizationArchive;
        return archive is null || document.RulesetId == RulesetDefaults.Sr6 && archive.Receipt is { } receipt
            && revision >= receipt.ContentRevision && CreatedSr6(document.Content)
            && (revision != receipt.ContentRevision || DocumentDigest(document.Content) == receipt.DocumentDigest);
    }

    private static bool CreatedSr6(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            return root is not null && root.Name == "character" && !root.Attributes().Any()
                && root.Elements("gameedition").Count() == 1 && root.Element("gameedition")!.Value == "SR6"
                && root.Elements("created").Count() == 1 && bool.TryParse(root.Element("created")!.Value, out bool created) && created
                && !root.Elements(CharacterCreationBootstrapXml.MarkerElement).Any();
        }
        catch (System.Xml.XmlException) { return false; }
    }
}
