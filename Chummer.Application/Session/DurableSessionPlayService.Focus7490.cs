using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public sealed partial class DurableSessionPlayService
{
    public CharacterCareerFocusBondingResult CommitFocus7490(OwnerScope owner, string characterId,
        CharacterCareerFocusBondingCommand command, bool retire,
        Func<CharacterCareerFocusBondingResult> lookup,
        Func<SessionFocusCanonicalAdmission7490?, CharacterCareerFocusBondingResult> commit)
    {
        try { return store.Execute(owner, characterId, tx =>
        {
            var rows = tx.ReadFocusContinuations7490();
            var original = rows.SingleOrDefault(x => x.Intent.ExpectedReceipt.Command.Request.OperationId == command.Request.OperationId);
            string digest = CharacterCareerFocusBondingTransaction.CommandDigest(command);
            if (original is not null)
                Conflict(original.Intent.ExpectedReceipt.CommandDigest == digest, "focus-continuation-command-mismatch");
            var observed = lookup();
            if (observed.Outcome != CharacterCareerFocusBondingOutcome.NotFound)
                return ObserveFocusResult7490(tx, original, observed);
            if (original?.Receipt is not null) throw new SessionPlayException(SessionPlayFailures.Conflict("focus-continuation-original-missing"));
            if (original is not null && !retire)
                return new CharacterCareerFocusBondingResult(CharacterCareerFocusBondingOutcome.UnknownOutcome);
            var origin = tx.ReadOrigin();
            if (origin is null) return commit(null); // No run exists to continue.
            Conflict(rows.All(x => x.Receipt is not null || x == original), "focus-continuation-requires-original-recovery");
            var operations = tx.ReadOperations();
            Conflict(operations.All(x => x.Result is not null), "pending-operation-requires-recovery");
            Conflict(tx.ReadConditionTransfers().All(x => x.Receipt is not null)
                && tx.ReadNaturalRecoveries().All(x => x.Receipt is not null), "original-operation-needs-inspection");
            var history = History(operations);
            var snapshot = Replay(tx, origin, history, operations.LastOrDefault(x => x.Result?.Snapshot is not null)?.Intent.OperationId ?? "");
            CheckReplay(tx, operations, snapshot);
            SessionFocusContinuationStored7490? prepared = original;
            var token = new SessionFocusCanonicalAdmission7490(authority, (before, after, auxiliary, sourceEnvironment, environment, expected) =>
            {
                ValidateAuthority(before.Authority, owner, characterId); ValidateAuthority(after, owner, characterId);
                // A retirement resolves the SAME preimage. It never legitimizes intervening canonical edits.
                if (original is null)
                    Conflict(MatchesOrigin5082(origin.Authority, ValidateContinuation(tx, before.Authority, before)),
                        "canonical-base-or-runtime-changed");
                else Conflict(SessionFocusContinuationRules7490.SameCanonicalPreimage(before, original.Intent.Before), "focus-continuation-retirement-divergence");
                var intent = new SessionFocusContinuationIntent7490(Digest(origin), snapshot.Play!.ReplicaCursor,
                    history.Length, operations.LastOrDefault()?.Ordinal ?? 0, before, after, auxiliary, sourceEnvironment, environment, expected, "");
                intent = intent with { Digest = Digest(intent) };
                if (original is null) prepared = tx.PrepareFocusContinuation7490(intent);
                else
                {
                    var priorRetirement = original.Retirement
                        ?? (original.Intent.ExpectedReceipt.NeverApplied ? original.Intent : null);
                    if (priorRetirement is not null)
                    {
                        // An interrupted terminal non-application write can finish under a fresh
                        // lease for the same owner. Reuse its immutable plan, never replay the focus
                        // mutation or accept a changed canonical preimage/expected receipt.
                        SessionFocusContinuationRules7490.Validate(priorRetirement);
                        SessionFocusContinuationRules7490.Validate(intent);
                        Conflict(intent.ExpectedReceipt.NeverApplied
                            && Digest(priorRetirement with { Before = priorRetirement.Before with {
                                OwnerAuthorityDigest = intent.Before.OwnerAuthorityDigest }, Digest = "" })
                                == Digest(intent with { Digest = "" }), "focus-continuation-retirement-divergence");
                        prepared = original;
                    }
                    else
                    {
                        tx.PrepareFocusRetirement7490(original, intent);
                        prepared = original with { Retirement = intent };
                    }
                }
            });
            CharacterCareerFocusBondingResult result;
            try { result = commit(token); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            { result = new(CharacterCareerFocusBondingOutcome.UnknownOutcome); }
            var recovered = lookup(); // Never invoke another canonical commit to reconcile a lost reply.
            if (recovered.Receipt is not null)
            {
                Conflict(prepared is not null, "focus-continuation-intent-not-published");
                var reconciled = ObserveFocusResult7490(tx, prepared, recovered);
                return reconciled with { Outcome = result.Outcome == CharacterCareerFocusBondingOutcome.Applied
                    ? CharacterCareerFocusBondingOutcome.Applied : reconciled.Outcome };
            }
            return prepared is null ? result : new(CharacterCareerFocusBondingOutcome.UnknownOutcome);
        }); }
        catch (SessionPlayException e) { return new(CharacterCareerFocusBondingOutcome.Conflict, Error: e.Failure.Code); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerFocusBondingOutcome.UnknownOutcome); }
    }

    public CharacterCareerFocusBondingResult RecoverFocus7490(OwnerScope owner, string characterId, Guid operationId,
        string digest, Func<CharacterCareerFocusBondingResult> lookup)
    {
        try { return store.Execute(owner, characterId, tx =>
        {
            var original = tx.ReadFocusContinuations7490().SingleOrDefault(x =>
                x.Intent.ExpectedReceipt.Command.Request.OperationId == operationId);
            if (original is not null) Conflict(original.Intent.ExpectedReceipt.CommandDigest == digest, "focus-continuation-command-mismatch");
            var result = lookup();
            try { return ObserveFocusResult7490(tx, original, result); }
            catch (SessionPlayException e) when (result.Receipt is not null)
            {
                // Historical canonical inspection remains available even when current source admission fails.
                // No acknowledgement is written; LIVE Play stays fenced under the original operation.
                return result with { Error = e.Failure.Code };
            }
        }); }
        catch (SessionPlayException e) { return new(CharacterCareerFocusBondingOutcome.Conflict, Error: e.Failure.Code); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerFocusBondingOutcome.UnknownOutcome); }
    }

    private CharacterCareerFocusBondingResult ObserveFocusResult7490(ISessionPlayTransaction tx,
        SessionFocusContinuationStored7490? original, CharacterCareerFocusBondingResult result)
    {
        if (original is null || result.Receipt is null) return result;
        if (original.Receipt is not null)
        {
            Conflict(Digest(original.Receipt.Canonical) == Digest(result.Receipt), "focus-continuation-original-receipt-divergence");
            return result;
        }
        var intent = result.Receipt.NeverApplied ? original.Retirement ?? original.Intent : original.Intent;
        Conflict(Digest(intent.ExpectedReceipt) == Digest(result.Receipt), "focus-continuation-original-receipt-divergence");
        var c = result.Receipt.Command;
        var owner = c.Binding.LocalSingleUser ? OwnerScope.LocalSingleUser : new OwnerScope(c.Binding.OwnerId);
        var observed = authority.ResolveFocusCurrent7490(owner, c.Request.WorkspaceId.Value);
        var binding = observed.Binding;
        Conflict(binding.ContentRevision == result.Receipt.CommittedWorkspaceRevision && binding.SavedRevision == binding.ContentRevision
            && binding.IncarnationId == intent.Before.IncarnationId && binding.SlotGeneration == intent.Before.SlotGeneration
            && binding.AuxiliaryDigest == intent.AfterAuxiliaryDigest && observed.SourceEnvironment == intent.AfterSourceEnvironment,
            "focus-continuation-committed-source-changed");
        ValidateAuthority(binding.Authority, owner, c.Request.WorkspaceId.Value);
        var acknowledgement = new SessionFocusContinuationReceipt7490(result.Receipt, binding.Authority, observed.SourceEnvironment, "");
        acknowledgement = acknowledgement with { Digest = Digest(acknowledgement) };
        _ = SessionFocusContinuationRules7490.Admitted(original with { Receipt = acknowledgement });
        tx.AcknowledgeFocusContinuation7490(original, acknowledgement);
        return result;
    }

    private static SessionPlayException FocusPending7490(SessionFocusContinuationStored7490 value) => new(
        SessionPlayFailures.Unavailable("focus-continuation-requires-original-recovery") with {
            OperationId = value.Intent.ExpectedReceipt.Command.Request.OperationId.ToString("D") });

    private static IReadOnlyList<SessionFocusCastingCheckpoint7490> FocusCheckpoints7490(ISessionPlayReader reader, long? beforeOrdinal = null)
        => reader.ReadFocusContinuations7490().Where(x => x.Receipt is not null)
            .Select(x => (Intent: SessionFocusContinuationRules7490.Admitted(x), Receipt: x.Receipt!))
            .Where(x => beforeOrdinal is null || x.Intent.PlayOrdinal < beforeOrdinal.Value)
            .OrderBy(x => x.Intent.ExpectedReceipt.CommittedWorkspaceRevision)
            .Select(x => new SessionFocusCastingCheckpoint7490(x.Intent.EventCount,
                x.Intent.ExpectedReceipt.CommittedWorkspaceRevision, x.Receipt.ContinuedAuthority)).ToArray();

    private static SessionPlayAuthority CastingAuthority7490(ISessionPlayReader reader, SessionPlayOrigin origin,
        SessionPlayAuthority current, IReadOnlyList<SessionEventEnvelope> history)
    {
        var checkpoint = FocusCheckpoints7490(reader).LastOrDefault();
        var original = SessionPlayMagicUpgrade5311.CurrentCapabilities(origin, current, history);
        return checkpoint is null ? original : original with { CastingAuthority =
            SessionPlayMagicUpgrade5311.CurrentCapabilities(origin, checkpoint.Authority, history).CastingAuthority };
    }
}
