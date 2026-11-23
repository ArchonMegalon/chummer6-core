using Chummer.Application.Workspaces;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerSkillGroupCurrentCapability
{
    private const string SkillGroupLegacy = "skill_group_legacy_workspace";
    private T WithSkillGroupCurrent<T>(IOwnerContextAccessor owners, OwnerContextStamp stamp, IOwnerContextLease lease,
        CharacterWorkspaceId id, Func<WorkspaceStoredDocument, GearCurrentEnvironment?, string,
            IReadOnlyList<DelegatedGmCharacterEditLedgerEntry>, T> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateLifestyleOwner(owners, stamp, lease);
        if (TryGetPath(stamp.Owner, id) is not { } path) throw new InvalidOperationException("skill_group_owner_unavailable");
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out var ledger, out var environment);
        using var current = environment;
        if (!read.Success || read.Value is not { } saved || saved.ContentRevision <= 0
            || saved.SavedRevision != saved.ContentRevision || saved.Document.Format != WorkspaceDocumentFormat.NativeXml
            || saved.Document.RulesetId != "sr5") throw new InvalidOperationException("skill_group_current_unavailable");
        var result = action(saved, current, path, ledger);
        if (result is not CharacterCareerSkillGroupWorkspaceCommitResult { Outcome: CharacterCareerSkillGroupWorkspaceOutcome.Applied })
            current?.ValidateReadObservation();
        ValidateLifestyleOwner(owners, stamp, lease);
        token.ThrowIfCancellationRequested();
        return result;
    }

    private static string SkillGroupAuthority(WorkspaceStoredDocument saved, GearCurrentEnvironment current,
        OwnerContextStamp stamp, string path) => CharacterCreationFinalizationDigest.Compute(new
        { stamp, saved.LocalHistory, Slot = ReadContinuationSlotUnderLease(path), Capture = current.Capture.IntegrityDigest });

    private CharacterCareerSkillGroupAdvanceInput GroupInput(WorkspaceStoredDocument saved, GearCurrentEnvironment current,
        Guid id, string authority, CancellationToken token)
    {
        var input = current.Execution.ReadCareerSkillGroupInput6494(saved, current.Capture, _retainedCharacterQueries!, id, token)
            ?? throw new InvalidOperationException("skill_group_current_projection_unavailable");
        return input with { RawRuleState = input.RawRuleState + "\0skill-group-current6494\0" + authority };
    }

    public CharacterCareerSkillGroupWorkspaceReadResult ReadCurrentSkillGroup(IOwnerContextAccessor owners,
        OwnerContextStamp stamp, IOwnerContextLease lease, CharacterWorkspaceId id,
        CharacterCareerSkillGroupIdentity identity, CancellationToken cancellationToken = default)
    {
        if (identity is null || identity.InternalId == Guid.Empty) return new(CharacterCareerSkillGroupWorkspaceOutcome.Corrupt);
        try
        {
            return WithSkillGroupCurrent<CharacterCareerSkillGroupWorkspaceReadResult>(owners, stamp, lease, id, (saved, current, path, _) =>
            {
                if (current is null) return new CharacterCareerSkillGroupWorkspaceReadResult(CharacterCareerSkillGroupWorkspaceOutcome.Unavailable, Error: SkillGroupLegacy);
                if (!CareerLifestyleCurrentWriteGuardsInstalled) throw new InvalidOperationException("skill_group_current_write_admission_unavailable");
                // Validate the established immutable ledger even on preview.
                CharacterCareerSkillGroupAdvancePersistence.ValidateHistory(saved);
                return new CharacterCareerSkillGroupWorkspaceReadResult(CharacterCareerSkillGroupWorkspaceOutcome.Available, saved.ContentRevision,
                    GroupInput(saved, current, identity.InternalId, SkillGroupAuthority(saved, current, stamp, path), cancellationToken));
            }, cancellationToken);
        }
        catch (Exception e) when (SkillGroupFailure(e)) { return new(CharacterCareerSkillGroupWorkspaceOutcome.Unavailable, Error: "skill_group_current_unavailable"); }
    }

    public CharacterCareerSkillGroupWorkspaceLookupResult LookupCurrentSkillGroup(IOwnerContextAccessor owners,
        OwnerContextStamp stamp, IOwnerContextLease lease, CharacterWorkspaceId id, Guid operationId,
        string commandDigest, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || !CharacterCareerSkillGroupAdvanceServiceIntegrity.IsCanonicalDigest(commandDigest))
            return new(CharacterCareerSkillGroupWorkspaceOutcome.Corrupt);
        try
        {
            return WithSkillGroupCurrent<CharacterCareerSkillGroupWorkspaceLookupResult>(owners, stamp, lease, id, (saved, current, _, _) => current is null
                ? new CharacterCareerSkillGroupWorkspaceLookupResult(CharacterCareerSkillGroupWorkspaceOutcome.Unavailable, Error: SkillGroupLegacy)
                : CharacterCareerSkillGroupAdvancePersistence.Lookup(saved, operationId, commandDigest), cancellationToken);
        }
        catch (Exception e) when (SkillGroupFailure(e)) { return new(CharacterCareerSkillGroupWorkspaceOutcome.Unavailable, Error: "skill_group_current_lookup_unavailable"); }
    }

    public CharacterCareerSkillGroupWorkspaceCommitResult CommitCurrentSkillGroup(IOwnerContextAccessor owners,
        OwnerContextStamp stamp, IOwnerContextLease lease, CharacterCareerSkillGroupWorkspaceCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        // Detach mutable member/prerequisite collections before any collaborator runs.
        request = JsonSerializer.Deserialize<CharacterCareerSkillGroupWorkspaceCommitRequest>(JsonSerializer.Serialize(request))!;
        if (request is null || request.Plan is null || request.ReviewedQuote is null
            || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(request.Plan)
            || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(request.ReviewedQuote)
            || !CharacterCareerSkillGroupAdvanceServiceIntegrity.IsCanonicalDigest(request.CommandDigest))
            return new(CharacterCareerSkillGroupWorkspaceOutcome.Corrupt);
        bool published = false;
        try
        {
            var result = WithSkillGroupCurrent<CharacterCareerSkillGroupWorkspaceCommitResult>(owners, stamp, lease, request.WorkspaceId, (saved, current, path, ledger) =>
            {
                if (current is null) return new CharacterCareerSkillGroupWorkspaceCommitResult(CharacterCareerSkillGroupWorkspaceOutcome.Unavailable, Error: SkillGroupLegacy);
                var prior = CharacterCareerSkillGroupAdvancePersistence.Lookup(saved, request.Plan.TransactionId, request.CommandDigest);
                if (prior.Outcome != CharacterCareerSkillGroupWorkspaceOutcome.NotFound) return GroupCommit(prior);
                cancellationToken.ThrowIfCancellationRequested();
                if (!CareerLifestyleCurrentWriteGuardsInstalled || saved.ContentRevision != request.ExpectedWorkspaceRevision)
                    return new(CharacterCareerSkillGroupWorkspaceOutcome.Conflict, saved.ContentRevision, Error: "stale_workspace_revision");
                string authority = SkillGroupAuthority(saved, current, stamp, path);
                var input = GroupInput(saved, current, request.Plan.Identity.InternalId, authority, cancellationToken);
                if (!CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(input, out var quote)
                    || !GroupSame(quote, request.ReviewedQuote)
                    || !CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance(quote, quote.LogicalRevision, quote.SourceRevision, quote.RuleDigest,
                        true, false, request.Plan.TransactionId, request.Plan.ExpenseDateLocal, out var plan) || !GroupSame(plan, request.Plan))
                    return new(CharacterCareerSkillGroupWorkspaceOutcome.Conflict, saved.ContentRevision, Error: "stale_skill_group_binding");
                // Verify the digest names this exact confirmed command, not just a caller-supplied hash.
                CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeBindingDigest(saved.Id, saved.ContentRevision, quote, out var binding);
                var command = new CharacterCareerSkillGroupAdvanceCommand(CharacterCareerSkillGroupAdvanceServiceSchemas.CommandV1,
                    saved.Id, saved.ContentRevision, quote.Identity, quote.LogicalRevision, quote.SourceRevision, quote.RuleDigest,
                    binding, plan.TransactionId, plan.ExpenseDateLocal, true);
                if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeCommandDigest(command, out var digest) || digest != request.CommandDigest)
                    return new(CharacterCareerSkillGroupWorkspaceOutcome.Conflict, saved.ContentRevision, Error: "skill_group_command_digest_invalid");
                var xml = CharacterCareerSkillGroupAdvancePersistence.ParseDocument(saved.Document.Content);
                CharacterCareerSkillGroupAdvancePersistence.ApplyPlan(xml, plan);
                long revision = checked(saved.ContentRevision + 1);
                WorkspaceDocument Replacement() => saved.Document with { State = saved.Document.State with
                    { Payload = CharacterCareerSkillGroupAdvancePersistence.Serialize(xml) } };
                var proposed = saved with { Document = Replacement(), ContentRevision = revision, SavedRevision = revision };
                var admitted = PrepareLifestyleReadmission(current, proposed, cancellationToken);
                var afterInput = admitted.AfterExecution!.ReadCareerSkillGroupInput6494(proposed, admitted.AfterCapture!, _retainedCharacterQueries!,
                    plan.Identity.InternalId, cancellationToken) ?? throw new InvalidOperationException("skill_group_after_unavailable");
                afterInput = afterInput with { RawRuleState = afterInput.RawRuleState + "\0skill-group-current6494\0" + authority };
                if (!CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(afterInput, out var after)
                    || !CharacterCareerSkillGroupAdvanceRules.TryCreateReceipt(plan.TransactionId, quote, plan, after,
                        CharacterCareerSkillGroupAdvancePersistence.ObserveExpense(xml, plan.ExpenseId), out var receipt))
                    throw new InvalidOperationException("skill_group_receipt_invalid");
                CharacterCareerSkillGroupAdvancePersistence.PersistLedgerEntry(xml, saved.Id, saved.ContentRevision, revision, digest, quote, receipt);
                WriteCurrentLifestyle(saved, Replacement(), revision, current, path, ledger,
                    () => SkillGroupAuthority(saved, current, stamp, path) == authority,
                    () => ValidateLifestyleOwner(owners, stamp, lease), cancellationToken);
                published = true;
                // The outer observation describes the predecessor and cannot validate after publication.
                return new CharacterCareerSkillGroupWorkspaceCommitResult(CharacterCareerSkillGroupWorkspaceOutcome.Applied, revision, digest, quote, receipt);
            }, cancellationToken);
            if (!published) return result;
        }
        catch (Exception e) when (SkillGroupFailure(e)) { }
        // Always release the workspace operation before fresh read-only recovery. Never re-dispatch.
        var recovered = LookupCurrentSkillGroup(owners, stamp, lease, request.WorkspaceId, request.Plan.TransactionId, request.CommandDigest);
        return recovered.Outcome == CharacterCareerSkillGroupWorkspaceOutcome.Replayed
            ? GroupCommit(recovered) with { Outcome = published ? CharacterCareerSkillGroupWorkspaceOutcome.Applied : CharacterCareerSkillGroupWorkspaceOutcome.Replayed }
            : new(CharacterCareerSkillGroupWorkspaceOutcome.Unavailable, recovered.CurrentWorkspaceRevision, Error: "skill_group_commit_not_observed");
    }
    private static CharacterCareerSkillGroupWorkspaceCommitResult GroupCommit(CharacterCareerSkillGroupWorkspaceLookupResult r)
        => new(r.Outcome, r.CurrentWorkspaceRevision, r.ExistingCommandDigest, r.ReviewedQuote, r.Receipt, r.Error);
    private static bool GroupSame<T>(T a, T b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
    private static bool SkillGroupFailure(Exception e) => e is IOException or UnauthorizedAccessException or InvalidOperationException
        or ArgumentException or OperationCanceledException or System.Xml.XmlException or JsonException or OverflowException;
}
