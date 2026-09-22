using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Rulesets.Sr6;

/// <summary>Fresh edition-owned admission. Storage supplies the owner/workspace transaction lease.</summary>
public static class Sr6CreationFinalizationRules
{
    public static CharacterCreationFoundationResult<Sr6CreationFinalizationReview> Review(WorkspaceStoredDocument saved)
    {
        if (saved.Document.AuxiliaryState.Sr6CreationFinalizationArchive is not null)
            return Blocked<Sr6CreationFinalizationReview>(Sr6CreationFinalizationBlockers.AlreadyFinalized);
        if (saved.ContentRevision != saved.SavedRevision)
            return Blocked<Sr6CreationFinalizationReview>(Sr6CreationFoundationBlockers.StaleBinding);
        var state = Sr6CreationFoundationRules.Load(saved);
        var projected = Sr6CreationCharacterProjector.Project(saved);
        if (state.Value?.DraftSummary is not { Balances: { } balances } summary || projected.Value is not { } materialized)
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                state.Blockers.Concat(projected.Blockers).Distinct(StringComparer.Ordinal).ToArray());
        string[] blockers = materialized.IncompleteDomains.Where(id => id != "finalization-transaction").ToArray();
        var losses = summary.Steps.SelectMany(step => step.Remainders.Select(pool =>
            new Sr6CreationFinalizationLoss(step.Id, pool.Id, pool.Amount))).ToList();
        // Karma already appears as the above-cap remainder in its reviewed step.
        if (balances.NuyenAboveCarryOver > 0) losses.Add(new("resources", "nuyen-above-cap", balances.NuyenAboveCarryOver));
        XElement root = XDocument.Parse(materialized.Document.Content).Root!;
        if (blockers.Length == 0)
        {
            root.Element("created")!.Value = "true";
            root.Element("karma")!.Value = balances.ProjectedStartingKarma.ToString(System.Globalization.CultureInfo.InvariantCulture);
            root.Element("nuyen")!.Value = balances.ProjectedStartingNuyen.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var projectedState = root.Element("sr6creationprojection")!;
            projectedState.Name = "sr6creation";
            projectedState.SetAttributeValue("stage", "finalized");
            projectedState.Element("incomplete")!.Remove();
            projectedState.Element("unspent")!.Name = "discardedcreationpools";
            // No implants are supported by this creation catalogue. Essence starts
            // at six (SR6 core p40); no SR5 reputation/initiative baseline is reused.
            root.Add(new XElement("totaless", 6), new XElement("physicalcmfilled", 0), new XElement("stuncmfilled", 0),
                new XElement("physicalcmoverflow", 0));
        }
        string xml = root.ToString(SaveOptions.DisableFormatting);
        var review = new Sr6CreationFinalizationReview(materialized.Binding, new(xml),
            Sr6CreationFinalizationIntegrity.DocumentDigest(xml), balances, losses.ToArray(), blockers,
            materialized.SourceAnchorIds.Concat(["sr6_core_de_2024:p40", "sr6_core_de_2024:p69-70"])
                .Distinct(StringComparer.Ordinal).ToArray(), string.Empty);
        review = review with { ReviewDigest = Sr6CreationFinalizationIntegrity.ReviewDigest(review) };
        return new(CharacterCreationFoundationOutcomes.Success, review, []);
    }

    public static CharacterCreationFoundationResult<Sr6CreationFinalizationCommit>? Lookup(WorkspaceStoredDocument saved,
        Sr6CreationFinalizationRequest request)
    {
        if (saved.Document.AuxiliaryState.Sr6CreationFinalizationArchive is not { } archive) return null;
        if (!Sr6CreationFinalizationIntegrity.IsValidArchive(saved.Id, saved.ContentRevision, saved.Document.AuxiliaryState)
            || !Sr6CreationFinalizationIntegrity.IsValidCurrentDocument(saved.Document, saved.ContentRevision))
            return Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFinalizationBlockers.InvalidArchive);
        if (archive.Receipt.Command.OperationId != request.OperationId)
            return Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFinalizationBlockers.AlreadyFinalized);
        return archive.Receipt.Command == request && saved.CanReplayReceipt(archive.Receipt.ContentRevision)
            ? new(CharacterCreationFoundationOutcomes.Success, new(archive.Receipt, true), [])
            : Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFinalizationBlockers.OperationConflict);
    }

    public static CharacterCreationFoundationResult<Sr6CreationFinalizationCommit> Prepare(WorkspaceStoredDocument saved,
        Sr6CreationFinalizationRequest request, out WorkspaceDocument? replacement)
    {
        replacement = null;
        if (!Sr6CreationFinalizationIntegrity.IsConfirmed(request))
            return Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFinalizationBlockers.ConfirmationRequired);
        if (Lookup(saved, request) is { } prior) return prior;
        var current = Review(saved);
        if (current.Value is not { } review) return new(current.Outcome, null, current.Blockers);
        if (review.Binding != request.Binding || review.ReviewDigest != request.ReviewDigest)
            return Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFoundationBlockers.StaleBinding);
        if (!review.CanFinalize) return new(CharacterCreationFoundationOutcomes.Blocked, null, review.Blockers);
        if (review.Losses.Count > 0 && !request.AcceptUnspentLoss)
            return Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFinalizationBlockers.LossAcknowledgementRequired);
        var receipt = new Sr6CreationFinalizationReceipt(request, saved.ContentRevision + 1, saved.ContentRevision + 1,
            review.DocumentDigest, string.Empty);
        receipt = receipt with { ReceiptDigest = Sr6CreationFinalizationIntegrity.ReceiptDigest(receipt) };
        var archive = new Sr6CreationFinalizationArchive(new(saved.Document.Content), saved.Document.AuxiliaryState, review, receipt);
        var state = new WorkspaceDocumentAuxiliaryState(Sr6CreationFinalizationArchive: archive);
        if (!WorkspaceAuxiliaryStateIntegrity.IsValidShape(saved.Id, receipt.ContentRevision, state))
            return Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFinalizationBlockers.InvalidArchive);
        replacement = saved.Document with { State = saved.Document.State with { Payload = review.Document.Content, AuxiliaryState = state } };
        return new(CharacterCreationFoundationOutcomes.Success, new(receipt, false), []);
    }

    private static CharacterCreationFoundationResult<T> Blocked<T>(string reason) where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [reason]);
}
