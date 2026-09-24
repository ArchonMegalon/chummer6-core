using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Prepare from current sources; storage owns the owner/path lease and
/// atomic replacement. Archived components are reconstruction, never admission.</summary>
public static class CharacterCreationLifeModuleFinalizationTransaction
{
    public static bool IsConfirmed(CharacterCreationFoundationFinalizationConfirmRequest? request)
        => request is { ExplicitlyConfirmed: true, Binding: { } binding, DraftRevision: > 0 }
            && binding.ContentRevision is > 0 and < long.MaxValue
            && binding.SavedRevision == binding.ContentRevision
            && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
            && CharacterCreationFinalizationDigest.IsCanonical(binding.RawCharacterXmlDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(binding.SourceDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(request.DraftDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(request.PreviewDigest);

    // A workspace can finalize only once. The exact reviewed command is its
    // retry identity; changing any answer is not a retry of that command.
    public static string CommandDigest(CharacterCreationFoundationFinalizationConfirmRequest request)
        => Hash(new { Semantics = "life-module-finalization-command/v1", Request = ToPreviewRequest(request), request.PreviewDigest });

    public static CharacterCreationFoundationFinalizationPreviewRequest ToPreviewRequest(
        CharacterCreationFoundationFinalizationConfirmRequest request)
        => new(request.Binding, request.DraftRevision, request.DraftDigest)
        {
            QualityInstanceValues = request.QualityInstanceValues, AttributePurchases = request.AttributePurchases,
            TalentSelection = request.TalentSelection, SkillSelection = request.SkillSelection,
            KarmaResourceInvestment = request.KarmaResourceInvestment, GearSelection = request.GearSelection,
            LifestyleSelection = request.LifestyleSelection, StartingLifestyleId = request.StartingLifestyleId,
            ContactSelection = request.ContactSelection, StartingNuyenDiceTotal = request.StartingNuyenDiceTotal,
            MagicSelection = request.MagicSelection
        };

    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver resolver, ILifeModulesCatalogService catalog, ICharacterFileQueries characterFiles,
        CharacterCreationFoundationFinalizationConfirmRequest request,
        out WorkspaceDocument? replacement, out CharacterCreationFinalizationReceipt? receipt, out string[] blockers)
    {
        replacement = null; receipt = null;
        blockers = [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid];
        try
        {
            if (!IsConfirmed(request) || Lookup(workspace, request) is not null
                || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(workspace.Id, workspace.ContentRevision, workspace.Document.AuxiliaryState))
                return false;
            var view = new WorkspaceContinuationReadView(owner, workspace);
            var evaluator = new CharacterCreationFoundationService(view, characterFiles, resolver, catalog,
                new CharacterCreationFoundationDraftApplyAuthority(view));
            var evaluated = evaluator.PrepareFinalization(ToPreviewRequest(request), out var authority);
            if (evaluated.Value is { } inspected && inspected.PreviewDigest != request.PreviewDigest)
            { blockers = [CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch]; return false; }
            if (authority is null || evaluated.Value is not { CanApply: true, FinalizationPlan: { } plan } preview)
            { if (evaluated.Blockers.Count > 0) blockers = evaluated.Blockers.ToArray(); return false; }
            if (!Same(preview.Binding, request.Binding)
                || !CharacterCreationLifeModuleCharacterProjector.TryProject(authority.RawCharacterXml, authority.Parts, out var projected)
                || projected!.RawCharacterXmlDigest != plan.ExpectedResultRawCharacterXmlDigest)
                return false;
            if (!characterFiles.Validate(new(projected.CharacterXml)).IsValid) return false;
            string key = RetryIdentity(preview.PreviewDigest), command = CommandDigest(request);
            var result = new CharacterCreationFinalizationReceipt(CharacterCreationFinalizationSchemas.ReceiptV1,
                ReceiptId(workspace.Id, key, command), workspace.Id, key, command,
                workspace.ContentRevision, workspace.ContentRevision + 1, workspace.SavedRevision, workspace.ContentRevision + 1,
                plan.Binding.RawCharacterXmlDigest, projected.RawCharacterXmlDigest, workspace.Document.AuxiliaryStateDigest,
                plan.Binding.AuthorityDigest, preview.PreviewDigest, plan.PlanDigest, CharacterCreationBuildMethods.LifeModules,
                true, true, CharacterCreationFinalizationDigest.ReceiptLedgerRootDigest, string.Empty);
            result = result with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(result) };
            var auxiliary = new WorkspaceDocumentAuxiliaryState(
                CharacterCreationFinalizationReceipts: [new(key, command, result)],
                CharacterCreationFinalizationArchive: new(workspace.Document.AuxiliaryState) { LifeModuleAuthority = authority });
            if (!WorkspaceAuxiliaryStateIntegrity.IsValidShape(workspace.Id, result.ContentRevision, auxiliary)) return false;
            replacement = workspace.Document with { State = workspace.Document.State with
                { Payload = projected.CharacterXml, AuxiliaryState = auxiliary } };
            receipt = result; blockers = [];
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException
            or UnauthorizedAccessException or System.Xml.XmlException or OverflowException or FormatException)
        { return false; }
    }

    public static CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>? Lookup(
        WorkspaceStoredDocument workspace, CharacterCreationFoundationFinalizationConfirmRequest request)
    {
        if (workspace.Document.AuxiliaryState.CharacterCreationFinalizationReceipts is not { } ledger) return null;
        if (!IsConfirmed(request) || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(workspace.Id,
                workspace.ContentRevision, workspace.Document.AuxiliaryState))
            return Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        var entry = ledger.SingleOrDefault(item => item.IdempotencyKeyDigest == RetryIdentity(request.PreviewDigest));
        if (entry is null || entry.CommandDigest != CommandDigest(request)
            || workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive?.LifeModuleAuthority is not { } authority
            || !workspace.CanReplayReceipt(entry.Receipt.ContentRevision))
            return Blocked(CharacterCreationFinalizationBlockers.IdempotencyConflict);
        var receipt = entry.Receipt;
        return new(CharacterCreationFoundationOutcomes.Success,
            new(workspace.Id, receipt.PreviousContentRevision, receipt.ContentRevision, receipt.SavedRevision,
                receipt.RawCharacterXmlDigest, authority.Preview.Binding.SourceDigest,
                authority.Preview.Compilation.CompilerRuntimeDigest, authority.Request.DraftRevision, authority.Request.DraftDigest,
                authority.Preview.ModuleSequence!.CompilationDigest, receipt.PreviewDigest, true, true, true), []);
    }

    /// <summary>Validate the stored historical reconstruction and receipt pairing.
    /// This is not a substitute for live source admission or permission to restore.</summary>
    public static bool IsValidArchive(CharacterWorkspaceId id, CharacterCreationFinalizationArchive archive,
        CharacterCreationFinalizationReceipt receipt)
    {
        if (receipt.BuildMethod != CharacterCreationBuildMethods.LifeModules) return archive.LifeModuleAuthority is null;
        try
        {
            if (archive.KarmaAuthority is not null || archive.StartingCash is not null
                || archive.State.CharacterCreationFoundationDraft is not { ModuleSelectionFinished: true, CharacterEffectsApplied: false } draft
                || archive.LifeModuleAuthority is not { Preview: { CanApply: true, CanConfirm: true, FinalizationBlocked.Count: 0,
                    FinalizationPlan: { } plan, ModuleSequence: { SelectionFinished: true } sequence } preview } a
                || a.Request.DraftRevision != draft.DraftRevision || a.Request.DraftDigest != draft.DraftDigest
                || !Same(a.Request.Binding, preview.Binding) || preview.Binding.WorkspaceId != id
                || preview.Binding.ContentRevision != receipt.PreviousContentRevision
                || preview.Binding.SavedRevision != receipt.PreviousSavedRevision
                || preview.Binding.RawCharacterXmlDigest != receipt.PreviousRawCharacterXmlDigest
                || preview.Binding.SourceDigest != draft.SourceDigest
                || preview.PreviewDigest != Hash(preview with { PreviewDigest = string.Empty })
                || plan.PlanDigest != CharacterCreationFinalizationDigest.Compute(plan with { PlanDigest = string.Empty })
                || plan.Binding.WorkspaceId != id || plan.Binding.ContentRevision != receipt.PreviousContentRevision
                || plan.Binding.SavedRevision != receipt.PreviousSavedRevision
                || plan.Binding.AuxiliaryStateDigest != receipt.PreviousAuxiliaryStateDigest
                || plan.Binding.RawCharacterXmlDigest != receipt.PreviousRawCharacterXmlDigest
                || plan.Binding.BuildMethod != CharacterCreationBuildMethods.LifeModules
                || a.Parts.Effects.WorkspaceId != id || a.Parts.Effects.DraftDigest != draft.DraftDigest
                || a.Parts.Effects.DraftRevision != draft.DraftRevision || a.Parts.Effects.CompilationDigest != sequence.CompilationDigest
                || sequence.DraftDigest != draft.DraftDigest || sequence.Occurrences.Count != 1 + (draft.AdditionalModules?.Count ?? 0)
                || !CharacterCreationLifeModuleCharacterProjector.TryProject(a.RawCharacterXml, a.Parts, out var rebuilt)
                || rebuilt!.RawCharacterXmlDigest != plan.ExpectedResultRawCharacterXmlDigest
                || rebuilt.ComponentsDigest != plan.Binding.AuthorityDigest || !Same(rebuilt.Deltas, plan.OrderedDeltas)
                || plan.KarmaRemaining != a.Parts.Finances.KarmaCarried || plan.StartingNuyen != a.Parts.Resources.NuyenFromKarma
                || plan.NuyenRemaining != a.Parts.Finances.CareerNuyen) return false;
            string command = Hash(new { Semantics = "life-module-finalization-command/v1", Request = a.Request, preview.PreviewDigest });
            return receipt.PlanDigest == plan.PlanDigest && receipt.PreviewDigest == preview.PreviewDigest
                && receipt.RawCharacterXmlDigest == rebuilt.RawCharacterXmlDigest
                && receipt.AuthorityDigest == plan.Binding.AuthorityDigest && receipt.CommandDigest == command
                && receipt.IdempotencyKeyDigest == RetryIdentity(preview.PreviewDigest)
                && receipt.ReceiptId == ReceiptId(id, receipt.IdempotencyKeyDigest, command);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException
            or System.Xml.XmlException or FormatException or NullReferenceException)
        { return false; }
    }

    public static CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> Blocked(params string[] blockers)
        => new(blockers.Any(blocker => blocker is CharacterCreationFoundationBlockers.StaleWorkspaceRevision
                or CharacterCreationFoundationBlockers.StaleRawCharacterXmlDigest
                or CharacterCreationFoundationBlockers.SourceDigestConflict
                or CharacterCreationFoundationBlockers.FinalizationDraftRevisionConflict
                or CharacterCreationFoundationBlockers.FinalizationDraftDigestConflict
                or CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch
                or CharacterCreationFinalizationBlockers.IdempotencyConflict)
            ? CharacterCreationFoundationOutcomes.Conflict : CharacterCreationFoundationOutcomes.Blocked, null, blockers);
    private static string RetryIdentity(string preview) => Hash(new { Method = CharacterCreationBuildMethods.LifeModules, PreviewDigest = preview });
    private static string ReceiptId(CharacterWorkspaceId id, string key, string command)
        => Hash(new { Schema = CharacterCreationFinalizationSchemas.ReceiptV1, Id = id, idempotencyDigest = key, commandDigest = command });
    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    private static bool Same<T>(T left, T right) => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
}
