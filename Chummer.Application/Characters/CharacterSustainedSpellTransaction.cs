using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Gear = Chummer.Application.Characters.CharacterCareerGearEquipmentTransaction;

namespace Chummer.Application.Characters;

public static class CharacterSustainedSpellTransaction
{
    public const int MaximumReceipts = 1024;
    public static string CommandDigest(CharacterSustainedSpellCommand command)
        => Gear.Hash("chummer.sustained-spell.command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterSustainedSpellReceipt receipt)
        => Gear.Hash("chummer.sustained-spell.receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));
    public static string CancellationDigest(CharacterSustainedSpellCancellationReceipt receipt)
        => Gear.Hash("chummer.sustained-spell.cancellation/v1\0"
            + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));
    public static bool ValidCancellation(CharacterSustainedSpellCancellationReceipt? receipt)
        => receipt is not null && ValidCommand(receipt.Command)
            && receipt.CommandDigest == CommandDigest(receipt.Command)
            && receipt.ObservedRevision >= receipt.Command.Binding.Preparation.ContentRevision
            && receipt.ObservedRevision < long.MaxValue
            && receipt.ReceiptDigest == CancellationDigest(receipt);
    public static string SourceDigest(WorkspaceDocument document) => Gear.Hash(JsonSerializer.Serialize(new
    {
        document.Format, document.RulesetId, document.SchemaVersion, document.PayloadKind,
        PayloadDigest = Gear.Hash(document.Content)
    }));
    public static bool ValidOwner(OwnerScope owner) => !string.IsNullOrWhiteSpace(owner.NormalizedValue)
        && owner.NormalizedValue.Length <= 512 && (!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser);
    public static bool MatchesOwner(OwnerScope owner, CharacterSustainedSpellPreparationBinding binding)
        => ValidOwner(owner) && binding is not null && owner.NormalizedValue == binding.OwnerId
            && owner.IsLocalSingleUser == binding.TrustedLocalOwner;
    public static bool ValidCommand(CharacterSustainedSpellCommand? command)
    {
        if (command is not { OperationId: var id, ExplicitlyConfirmed: true, Binding: { Preparation: { } p } b,
                Review: { Request: { } request, AddedIdentity: { } added } review }
            || id == Guid.Empty || !Gear.Same(p, review.Binding)
            || string.IsNullOrWhiteSpace(p.OwnerId) || p.OwnerId.Length > 512
            || p.OwnerId != p.OwnerId.Trim().ToLowerInvariant()
            || (p.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue) != p.TrustedLocalOwner
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(p.WorkspaceId)
            || p.ContentRevision is <= 0 or long.MaxValue || p.SavedRevision != p.ContentRevision
            || !Gear.IsDigest(p.SourceDigest) || !Gear.IsDigest(p.AuxiliaryDigest) || !Gear.IsDigest(p.EnvironmentDigest)
            || !Gear.IsDigest(b.RuntimeDigest) || !Gear.IsGeneration(b.IncarnationId) || !Gear.IsGeneration(b.SlotGeneration)
            || string.IsNullOrWhiteSpace(b.OwnerAuthorityInstanceId) || b.OwnerAuthorityInstanceId.Length > 512
            || b.OwnerTransitionRevision < 0 || request.SpellId == Guid.Empty
            || request.Force is < CharacterSustainedObjectRules.MinimumForce or > CharacterSustainedObjectRules.MaximumForce
            || request.NetHits is < CharacterSustainedObjectRules.MinimumNetHits or > CharacterSustainedObjectRules.MaximumNetHits
            || added.LinkedObjectType != "Spell" || added.LinkedObjectId != request.SpellId || added.Occurrence is < 0 or >= 1024
            || string.IsNullOrWhiteSpace(review.DisplayName)
            || review.DisplayName.Length > 16384 || review.DicePoolPenaltyBefore > 0 || review.DicePoolPenaltyAfter > 0
            || string.IsNullOrWhiteSpace(review.SheetAuthorityBefore) || string.IsNullOrWhiteSpace(review.SheetAuthorityAfter)) return false;
        return true;
    }
    public static CharacterSustainedSpellLookup Original(CharacterSustainedSpellCommand command) => new(
        command.Binding.Preparation.OwnerId, command.Binding.Preparation.TrustedLocalOwner,
        command.Binding.Preparation.WorkspaceId, command.OperationId, CommandDigest(command),
        command.Binding.IncarnationId, command.Binding.SlotGeneration);
    public static bool ValidLookup(CharacterSustainedSpellLookup? original) => original is not null
        && original.OperationId != Guid.Empty && Gear.IsDigest(original.CommandDigest)
        && Gear.IsGeneration(original.IncarnationId) && Gear.IsGeneration(original.SlotGeneration)
        && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(original.WorkspaceId)
        && !string.IsNullOrWhiteSpace(original.OriginalOwnerId) && original.OriginalOwnerId.Length <= 512;

    public static bool IsValidLedger(CharacterWorkspaceId workspace, long revision,
        IReadOnlyList<CharacterSustainedSpellReceipt>? receipts)
    {
        if (receipts is null) return true;
        if (receipts.Count is 0 or > MaximumReceipts
            || JsonSerializer.SerializeToUtf8Bytes(receipts).Length > Gear.MaximumLedgerBytes) return false;
        var ids = new HashSet<Guid>(); long previous = 0;
        foreach (var receipt in receipts)
        {
            if (receipt is null || !ValidCommand(receipt.Command)
                || receipt.Command.Binding.Preparation.WorkspaceId != workspace
                || !ids.Add(receipt.Command.OperationId) || receipt.CommandDigest != CommandDigest(receipt.Command)
                || receipt.CommittedRevision != receipt.Command.Binding.Preparation.ContentRevision + 1
                || receipt.CommittedRevision <= previous || receipt.CommittedRevision > revision
                || !Gear.IsDigest(receipt.PayloadDigestAfter) || receipt.ReceiptDigest != ReceiptDigest(receipt)) return false;
            previous = receipt.CommittedRevision;
        }
        return true;
    }

    public static CharacterSustainedSpellResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        string? slot, CharacterSustainedSpellLookup original)
    {
        if (!ValidLookup(original) || !ValidOwner(owner) || owner.NormalizedValue != original.OriginalOwnerId
            || owner.IsLocalSingleUser != original.TrustedLocalOwner || saved.Id != original.WorkspaceId
            || saved.LocalHistory is not { } history || !history.IsValid(saved.ContentRevision)
            || history.IncarnationId != original.IncarnationId || slot != original.SlotGeneration
            || !IsValidLedger(saved.Id, saved.ContentRevision, saved.Document.AuxiliaryState.CharacterSustainedSpellReceipts))
            return new(CharacterSustainedSpellOutcome.Conflict);
        var receipt = saved.Document.AuxiliaryState.CharacterSustainedSpellReceipts?
            .SingleOrDefault(r => r.Command.OperationId == original.OperationId);
        if (receipt is null) return new(CharacterSustainedSpellOutcome.NotFound, CurrentRevision: saved.ContentRevision);
        return receipt.CommandDigest == original.CommandDigest && Gear.Same(Original(receipt.Command), original)
            && saved.CanReplayReceipt(receipt.CommittedRevision)
            ? new(CharacterSustainedSpellOutcome.Recorded, Receipt: receipt, CurrentRevision: saved.ContentRevision)
            : new(CharacterSustainedSpellOutcome.Conflict);
    }

    public static bool TryRead(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        ICharacterSourceDataResolver sources, string runtime, string? slot, out CharacterSustainedSpellCatalog? catalog, CharacterCreationRuleSourceCapture? captured = null)
    {
        catalog = null;
        if (!owner.IsValid || !ValidOwner(owner.Owner) || !Gear.IsDigest(runtime) || !Gear.IsGeneration(slot)
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(saved.Id)
            || saved.ContentRevision is <= 0 or long.MaxValue || saved.SavedRevision != saved.ContentRevision
            || saved.LocalHistory is not { } history || !history.IsValid(saved.ContentRevision)
            || saved.Document.Format != WorkspaceDocumentFormat.NativeXml || saved.Document.RulesetId != "sr5"
            || saved.Document.SchemaVersion != 1 || saved.Document.PayloadKind is not ("workspace" or "sr5/chum5-xml")
            || !IsValidLedger(saved.Id, saved.ContentRevision, saved.Document.AuxiliaryState.CharacterSustainedSpellReceipts)
            || !WorkspaceDocumentAuxiliaryState.HasSafeExtensionData(saved.Document.AuxiliaryState)) return false;
        using var scope = (sources as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var resolver = scope ?? sources;
        bool Environment(out string digest) => captured is null
            ? Gear.TryEnvironment(resolver, saved.Document.Content, out digest)
            : Gear.TryCapturedEnvironment(captured, resolver, saved.Document.Content, out digest);
        if (!Environment(out string environment)) return false;
        var grants = CharacterCreationPriorityGrantAuthority3563.Capture(saved, resolver);
        var sheet = CharacterSheetDerivedQuery.ParseWithPriorityGrants3563(saved.Document.Content, resolver, grants);
        if (sheet.UnavailableReason is not null || sheet.SustainedSpellPreparation is not { } preparation
            || !Environment(out string after) || environment != after) return false;
        var binding = new CharacterSustainedSpellPreparationBinding(owner.Owner.NormalizedValue, owner.Owner.IsLocalSingleUser,
            saved.Id, saved.ContentRevision, saved.SavedRevision, SourceDigest(saved.Document),
            saved.Document.AuxiliaryStateDigest, environment);
        catalog = new(new(binding, runtime, history.IncarnationId, slot!, owner.AuthorityInstanceId, owner.TransitionRevision), preparation);
        return true;
    }

    public static bool TryBuild(OwnerContextStamp owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        CharacterSustainedSpellCatalog current, CharacterSustainedSpellCommand command,
        out WorkspaceDocument? replacement, out CharacterSustainedSpellReceipt? receipt, CharacterCreationRuleSourceCapture? captured = null,
        CharacterCurrentRuleEnvironmentExecution? currentExecution = null)
    {
        replacement = null; receipt = null;
        if (!ValidCommand(command) || !Gear.Same(command.Binding, current.Binding)
            || !MatchesOwner(owner.Owner, command.Binding.Preparation)
            || command.Binding.OwnerAuthorityInstanceId != owner.AuthorityInstanceId
            || command.Binding.OwnerTransitionRevision != owner.TransitionRevision
            || saved.Document.AuxiliaryState.CharacterSustainedSpellReceipts is { Count: >= MaximumReceipts }
            || saved.Document.AuxiliaryState.CharacterSustainedSpellReceipts?.Any(r => r.Command.OperationId == command.OperationId) == true
            || !CharacterSustainedSpellPreparationPlanner.TryBuildConfirmedReplacement(owner.Owner, saved, sources,
                command.Review, command.ExplicitlyConfirmed, out var proposed, captured, currentExecution) || proposed is null) return false;
        receipt = new(command, CommandDigest(command), saved.ContentRevision + 1, Gear.Hash(proposed.Content), "");
        receipt = receipt with { ReceiptDigest = ReceiptDigest(receipt) };
        var entries = (saved.Document.AuxiliaryState.CharacterSustainedSpellReceipts ?? []).Append(receipt).ToArray();
        if (!IsValidLedger(saved.Id, receipt.CommittedRevision, entries)) { receipt = null; return false; }
        replacement = proposed with { State = proposed.State with
            { AuxiliaryState = proposed.AuxiliaryState with { CharacterSustainedSpellReceipts = entries } } };
        return true;
    }
}
