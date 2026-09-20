using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Fresh rule admission and deterministic preparation; storage owns the lease and write.</summary>
public static class CharacterCreationKarmaFinalizationTransaction
{
    public static string AuthorityDigest(CharacterCreationKarmaFinalizationAuthority authority)
        => CharacterCreationFinalizationDigest.Compute(authority with { AuthorityDigest = string.Empty });

    public static bool IsConfirmed(CharacterCreationKarmaFinalizationConfirmRequest? request)
        => request is { DiceTotal: >= 0, Confirmation: { ExplicitlyConfirmed: true, Binding: { } binding } command }
            && binding.BuildMethod == CharacterCreationBuildMethods.Karma
            && binding.ContentRevision is > 0 and < long.MaxValue && binding.SavedRevision == binding.ContentRevision
            && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
            && CharacterCreationFinalizationDigest.IsCanonical(binding.RawCharacterXmlDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(binding.AuthorityDigest)
            && CharacterCareerReputationTransaction.IsDigest(binding.AuxiliaryStateDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(command.PreviewDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(command.PlanDigest)
            && command.IdempotencyKey is { Length: >= 8 and <= 200 }
            && command.IdempotencyKey == command.IdempotencyKey.Trim()
            && !command.IdempotencyKey.Any(char.IsControl);

    public static string CommandDigest(CharacterCreationKarmaFinalizationConfirmRequest request)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.character_creation_karma_finalization_command.v1",
            request.Confirmation.Binding, request.Confirmation.PreviewDigest, request.Confirmation.PlanDigest,
            request.Confirmation.ExplicitlyConfirmed, request.DiceTotal
        });

    public static bool TryAdmit(OwnerScope owner, WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver resolver, int diceTotal,
        out CharacterCreationKarmaFinalizationAuthority? authority,
        out CharacterCreationFinalizationReview? review, out string xml, out string[] blockers)
    {
        authority = null; review = null; xml = string.Empty;
        blockers = [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid];
        try
        {
            var context = resolver.TryCreateContext(workspace.Document.Content);
            if (context is null) return false;
            var view = new WorkspaceContinuationReadView(owner, workspace);
            var captured = new CapturedResolver(workspace.Document.Content, context);
            var opened = new CharacterCreationKarmaMetatypeService(view, captured).Open(workspace.Id, true, true, true);
            if (opened.Value?.Quote is not { } foundation)
            {
                blockers = opened.Blockers.Count > 0 ? opened.Blockers.ToArray()
                    : [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid];
                return false;
            }
            if (!context.TryResolveCreationKarmaCarryoverPolicy(out var policy)
                || !context.TryResolveCreationKarmaDefaultStartingNuyen(out var starting)
                || policy is null || starting is null) return false;
            var finances = CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy, starting, foundation, diceTotal);
            if (finances is null) { blockers = [CharacterCreationKarmaFinalizationBudgetBlockers.BudgetInvalid]; return false; }
            if (!context.TryResolveCreationKarmaGrantSources(foundation.Metatype.OptionId,
                    foundation.Talent!.OptionId, out var racial, out _)
                || !context.TryResolveCreationLifestylesAuthority(out var lifestyles)) return false;
            var candidate = new CharacterCreationKarmaFinalizationAuthority(
                CharacterCreationKarmaFinalizationAuthority.SchemaV1, workspace.Document.Content,
                foundation, finances, racial, lifestyles, string.Empty);
            candidate = candidate with { AuthorityDigest = AuthorityDigest(candidate) };
            if (!TryReview(workspace, candidate, out var preparedReview, out var preparedXml, out blockers)) return false;
            // One captured context must remain valid through the entire admission.
            if (!context.TryResolveCreationLifestylesAuthority(out var finalLifestyles)
                || finalLifestyles.AuthorityDigest != lifestyles.AuthorityDigest
                || !context.TryResolveCreationKarmaCarryoverPolicy(out var finalPolicy)
                || finalPolicy?.AuthorityDigest != policy.AuthorityDigest
                || !context.TryResolveCreationKarmaDefaultStartingNuyen(out var finalStarting)
                || finalStarting?.AuthorityDigest != starting.AuthorityDigest
                || !CharacterCreationBootstrapAuthority.TryPrepareBinding(workspace.Id, workspace.Document,
                    context, out var bootstrap, out _, out _)
                || bootstrap.BindingDigest != foundation.Binding.BootstrapBindingDigest)
            { blockers = [CharacterCreationKarmaMetatypeBlockers.StaleBinding]; return false; }
            authority = candidate; review = preparedReview; xml = preparedXml;
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException
            or UnauthorizedAccessException or System.Xml.XmlException or OverflowException or FormatException)
        { return false; }
    }

    public static bool TryReview(WorkspaceStoredDocument workspace, CharacterCreationKarmaFinalizationAuthority authority,
        out CharacterCreationFinalizationReview? review, out string xml, out string[] blockers)
    {
        review = null; xml = string.Empty; blockers = [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid];
        if (authority is not { Schema: CharacterCreationKarmaFinalizationAuthority.SchemaV1 }
            || authority.RawCharacterXml != workspace.Document.Content
            || authority.AuthorityDigest != AuthorityDigest(authority)
            || !CharacterCreationKarmaFinalizationProjector.TryProject(workspace, authority.Foundation,
                authority.Finances, authority.RacialSources, authority.Lifestyles, out xml, out var deltas, out blockers)) return false;
        var binding = new CharacterCreationFinalizationBinding(workspace.Id, workspace.ContentRevision,
            workspace.SavedRevision, authority.Foundation.Binding.RawCharacterXmlDigest, workspace.Document.AuxiliaryStateDigest,
            CharacterCreationBuildMethods.Karma, authority.AuthorityDigest);
        var anchors = deltas.SelectMany(item => item.SourceAnchorIds).Concat(authority.Finances.SourceAnchorIds)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var plan = new CharacterCreationFinalizationPlan(CharacterCreationFinalizationSchemas.PlanV1,
            binding, deltas, authority.Finances.KarmaCarried, authority.Foundation.Resources!.NuyenFromKarma,
            authority.Finances.CareerNuyen, anchors, CharacterCreationFinalizationDigest.ComputeUtf8(xml), string.Empty);
        plan = plan with { PlanDigest = CharacterCreationFinalizationDigest.Compute(plan) };
        review = new(CharacterCreationFinalizationSchemas.ReviewV1, binding, plan, deltas, [], anchors, true, true, string.Empty);
        review = review with { PreviewDigest = CharacterCreationFinalizationDigest.Compute(review) };
        return true;
    }

    public static CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>? Lookup(
        WorkspaceStoredDocument workspace, CharacterCreationKarmaFinalizationConfirmRequest request)
    {
        var ledger = workspace.Document.AuxiliaryState.CharacterCreationFinalizationReceipts;
        if (ledger is null) return null;
        if (!IsConfirmed(request) || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(workspace.Id,
                workspace.ContentRevision, workspace.Document.AuxiliaryState))
            return Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        string key = CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(request.Confirmation.IdempotencyKey);
        var entry = ledger.SingleOrDefault(item => item.IdempotencyKeyDigest == key);
        if (entry is null) return Blocked(CharacterCreationFinalizationBlockers.CharacterAlreadyCreated);
        return entry.CommandDigest == CommandDigest(request)
            && workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive?.KarmaAuthority?.Finances.DiceTotal == request.DiceTotal
            && workspace.CanReplayReceipt(entry.Receipt.ContentRevision)
            ? new(CharacterCreationFoundationOutcomes.Success, entry.Receipt, [])
            : Blocked(CharacterCreationFinalizationBlockers.IdempotencyConflict);
    }

    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument workspace, ICharacterSourceDataResolver resolver,
        CharacterCreationKarmaFinalizationConfirmRequest request,
        out WorkspaceDocument? replacement, out CharacterCreationFinalizationReceipt? receipt)
    {
        replacement = null; receipt = null;
        if (!IsConfirmed(request) || Lookup(workspace, request) is not null
            || !TryAdmit(owner, workspace, resolver, request.DiceTotal, out var authority, out var review, out var xml, out _)
            || review!.Binding != request.Confirmation.Binding || review.PreviewDigest != request.Confirmation.PreviewDigest
            || review.Plan!.PlanDigest != request.Confirmation.PlanDigest) return false;
        string key = CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(request.Confirmation.IdempotencyKey);
        string command = CommandDigest(request);
        var result = new CharacterCreationFinalizationReceipt(CharacterCreationFinalizationSchemas.ReceiptV1,
            CharacterCreationFinalizationDigest.Compute(new
                { Schema = CharacterCreationFinalizationSchemas.ReceiptV1, Id = workspace.Id, idempotencyDigest = key, commandDigest = command }),
            workspace.Id, key, command, workspace.ContentRevision, workspace.ContentRevision + 1,
            workspace.SavedRevision, workspace.ContentRevision + 1, review.Binding.RawCharacterXmlDigest,
            review.Plan.ExpectedResultRawCharacterXmlDigest, workspace.Document.AuxiliaryStateDigest,
            authority!.AuthorityDigest, review.PreviewDigest, review.Plan.PlanDigest, CharacterCreationBuildMethods.Karma,
            true, true, CharacterCreationFinalizationDigest.ReceiptLedgerRootDigest, string.Empty);
        result = result with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(result) };
        var auxiliary = new WorkspaceDocumentAuxiliaryState(
            CharacterCreationFinalizationReceipts: [new(key, command, result)],
            CharacterCreationFinalizationArchive: new(workspace.Document.AuxiliaryState, authority));
        if (!WorkspaceAuxiliaryStateIntegrity.IsValidShape(workspace.Id, result.ContentRevision, auxiliary)) return false;
        replacement = workspace.Document with { State = workspace.Document.State with { Payload = xml, AuxiliaryState = auxiliary } };
        receipt = result;
        return true;
    }

    public static bool IsValidArchive(CharacterWorkspaceId id, CharacterCreationFinalizationArchive archive,
        CharacterCreationFinalizationReceipt receipt)
    {
        if (receipt.BuildMethod != CharacterCreationBuildMethods.Karma) return archive.KarmaAuthority is null;
        try
        {
            var authority = archive.KarmaAuthority;
            var foundation = authority?.Foundation;
            var saved = archive.State.CharacterCreationKarmaMetatypeDecisions?.LastOrDefault();
            if (authority is not { Schema: CharacterCreationKarmaFinalizationAuthority.SchemaV1, RawCharacterXml: { Length: > 0 } }
                || foundation?.Binding is null || saved is null || receipt.AuthorityDigest != authority.AuthorityDigest
                || foundation.Binding.WorkspaceId != id || foundation.Binding.ContentRevision != receipt.PreviousContentRevision
                || foundation.Binding.SavedRevision != receipt.PreviousSavedRevision
                || foundation.Binding.AuxiliaryStateDigest != receipt.PreviousAuxiliaryStateDigest
                || foundation.Binding.RawCharacterXmlDigest != receipt.PreviousRawCharacterXmlDigest) return false;
            var document = new WorkspaceDocument(authority.RawCharacterXml, "sr5");
            document = document with { State = document.State with { AuxiliaryState = archive.State } };
            var historical = new WorkspaceStoredDocument(id, document, receipt.PreviousContentRevision,
                receipt.PreviousSavedRevision, DateTimeOffset.UnixEpoch);
            if (!TryReview(historical, authority, out var review, out _, out _)) return false;
            var command = new CharacterCreationKarmaFinalizationConfirmRequest(
                new(review!.Binding, review.PreviewDigest, review.Plan!.PlanDigest, "historical-validation", true), authority.Finances.DiceTotal);
            string commandDigest = CommandDigest(command);
            return receipt.PlanDigest == review.Plan.PlanDigest && receipt.PreviewDigest == review.PreviewDigest
                && receipt.RawCharacterXmlDigest == review.Plan.ExpectedResultRawCharacterXmlDigest
                && receipt.CommandDigest == commandDigest
                && receipt.ReceiptId == CharacterCreationFinalizationDigest.Compute(new
                    { Schema = CharacterCreationFinalizationSchemas.ReceiptV1, Id = id,
                        idempotencyDigest = receipt.IdempotencyKeyDigest, commandDigest });
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { return false; }
    }

    public static CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> Blocked(string blocker)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);

    private sealed class CapturedResolver(string xml, ICharacterSourceDataContext context) : ICharacterSourceDataResolver
    { public ICharacterSourceDataContext? TryCreateContext(string candidate) => candidate == xml ? context : null; }
}
