using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerQualityCurrentCapability
{
    private T WithQualityEnvironment<T>(IOwnerContextAccessor owners, OwnerContextStamp stamp, IOwnerContextLease lease,
        CharacterWorkspaceId id, ICharacterCareerQualityAuthorityResolver authority,
        Func<WorkspaceStoredDocument, ICharacterCareerQualityAuthorityResolver, string, GearCurrentEnvironment?, string,
            IReadOnlyList<DelegatedGmCharacterEditLedgerEntry>, T> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateQualityOwner(owners, stamp, lease);
        if (!CharacterCareerQualityTransaction.IsValidOwner(stamp.Owner) || TryGetPath(stamp.Owner, id) is not { } path)
            throw new InvalidOperationException("quality_owner_changed");
        // Preview and receipt recovery share this path. Neither may delete an
        // interrupted writer's temporary record while observing its outcome.
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out var ledger, out var environment);
        using var current = environment;
        if (!read.Success || read.Value is not { } saved)
            throw new InvalidOperationException("quality_current_workspace_unavailable");
        var exact = authority;
        if (current is not null)
        {
            // Only the composed owner-managed policy is supported here. A custom
            // authority is never silently replaced with stock or more permissive policy.
            if (authority is not OwnerManagedCareerQualityAuthorityResolver || !CareerLifestyleCurrentWriteGuardsInstalled
                || !current.Capture.MatchesOriginalCharacter(saved.Document.Content))
                throw new InvalidOperationException("quality_current_policy_unavailable");
            var resolution = current.Capture.CreateCurrentEnvironmentResolution(current.Execution.ExecutionProfile,
                current.Execution.ExecutionProfileFingerprint);
            if (resolution is null) throw new InvalidOperationException("quality_current_sources_unavailable");
            exact = new OwnerManagedCareerQualityAuthorityResolver(resolution.Resolver);
        }
        string runtime = QualityRuntime(current, path, stamp);
        var result = action(saved, exact, runtime, current, path, ledger);
        ValidateQualityOwner(owners, stamp, lease);
        return result;
    }

    private static void ValidateQualityOwner(IOwnerContextAccessor owners, OwnerContextStamp stamp, IOwnerContextLease lease)
    {
        if (!stamp.IsValid || owners is not IOwnerContextLeaseAccessor actual || actual.Capture() != stamp || lease.Stamp != stamp)
            throw new InvalidOperationException("quality_owner_changed");
    }

    private string QualityRuntime(GearCurrentEnvironment? current, string path, OwnerContextStamp stamp)
        => current is null ? CareerQualityRuntimeFingerprint : CharacterCareerReputationTransaction.Hash(JsonSerializer.Serialize(new
        {
            // The caller's full transition stamp is checked under its live lease,
            // never serialized into durable receipt semantics. This keeps exact
            // inverse/history projections valid when a process is reconstructed.
            Runtime = CareerQualityRuntimeFingerprint, OwnerId = stamp.Owner.NormalizedValue, Slot = ReadContinuationSlotUnderLease(path),
            current.Stored.LocalHistory!.IncarnationId, current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint,
            current.Capture.EffectivePolicyDigest, current.Capture.RequiredPackageSetDigest,
            current.Capture.RequiredRulePacks, current.Capture.RequiredAssets, current.Capture.OrderedCustomSources
        }));

    private static bool QualityCurrentFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OperationCanceledException;

    private void ValidateQualityObservation(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        GearCurrentEnvironment? current, string path)
    {
        if (current is not null)
        {
            // This existing fence rechecks storage after the query collaborator,
            // including timestamp, owner transition and retained source bytes.
            current.ValidateFreeSpriteReadObservation();
            return;
        }
        var after = ReadWorkspaceUnderLease(owner.Owner, saved.Id, path, out _);
        if (!after.Success || JsonSerializer.Serialize(after.Value) != JsonSerializer.Serialize(saved))
            throw new IOException("quality_workspace_changed_during_read");
    }

    public CharacterCareerQualityWorkspaceSnapshot? ReadCurrentCareerQuality(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterWorkspaceId workspace,
        ICharacterCareerQualityAuthorityResolver authority, CancellationToken cancellationToken = default)
    {
        try
        {
            return WithQualityEnvironment(owners, expectedOwner, ownerLease, workspace, authority, (saved, exact, runtime, current, path, _) =>
            {
                var result = OwnerBoundCharacterCareerQualityService.ReadSaved(expectedOwner.Owner, saved, runtime, exact, cancellationToken);
                ValidateQualityObservation(expectedOwner, saved, current, path);
                return result;
            }, cancellationToken);
        }
        catch (Exception ex) when (QualityCurrentFailure(ex)) { return null; }
    }

    public CharacterCareerQualityCommitResult RecoverCurrentCareerQuality(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterCareerQualityPlan original,
        ICharacterCareerQualityAuthorityResolver authority, CancellationToken cancellationToken = default)
    {
        if (!CharacterCareerQualityTransaction.IsValidPlan(original) || original.OwnerId != expectedOwner.Owner.NormalizedValue)
            return new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_recovery_owner_rejected");
        try
        {
            return WithQualityEnvironment(owners, expectedOwner, ownerLease, new(original.WorkspaceId), authority,
                (saved, exact, runtime, current, path, _) =>
                {
                    var result = OwnerBoundCharacterCareerQualityService.RecoverSavedReceipt(expectedOwner.Owner, saved, original,
                        runtime, exact, cancellationToken);
                    ValidateQualityObservation(expectedOwner, saved, current, path);
                    return result;
                }, cancellationToken);
        }
        catch (Exception ex) when (QualityCurrentFailure(ex))
        { return new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_current_recovery_unavailable"); }
    }

    public CharacterCareerQualityCommitResult CommitCurrentCareerQuality(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterCareerQualityPlan plan,
        ICharacterCareerQualityAuthorityResolver authority, CancellationToken cancellationToken = default)
    {
        if (!CharacterCareerQualityTransaction.IsValidPlan(plan) || plan.OwnerId != expectedOwner.Owner.NormalizedValue)
            return new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_command_invalid");
        bool completed = false;
        try
        {
            var result = WithQualityEnvironment<CharacterCareerQualityCommitResult>(owners, expectedOwner, ownerLease,
                new(plan.WorkspaceId), authority, (saved, exact, runtime, current, path, ledger) =>
                {
                    var prior = CharacterCareerQualityTransaction.Lookup(expectedOwner.Owner, saved, plan);
                    if (prior.Outcome != CharacterCareerQualityCommitOutcome.NotFound)
                    {
                        if (prior.Outcome == CharacterCareerQualityCommitOutcome.Replayed && current is not null)
                            prior = OwnerBoundCharacterCareerQualityService.RecoverSavedReceipt(expectedOwner.Owner, saved, plan, runtime, exact);
                        current?.ValidateFreeSpriteReadObservation();
                        return prior;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    using var admission = exact.Open(expectedOwner.Owner, saved, plan.Identity, plan.Operation, runtime);
                    if (admission is null || !CharacterCareerQualityTransaction.TryBuild(expectedOwner.Owner, saved, plan,
                            admission, runtime, out var replacement, out var entry))
                        return new(CharacterCareerQualityCommitOutcome.Conflict, CurrentRevision: saved.ContentRevision,
                            Error: "quality_current_binding_conflict");
                    // Reuse the existing private before/after issuer and its installed
                    // retained-write hooks. Only the typed quality transaction above can
                    // provide this replacement; no public document-write permit is added.
                    WriteCurrentLifestyle(saved, replacement!, plan.TargetWorkspaceRevision, current, path, ledger,
                        admission.IsCurrent, () => ValidateQualityOwner(owners, expectedOwner, ownerLease), cancellationToken);
                    completed = true;
                    return new(CharacterCareerQualityCommitOutcome.Applied, entry, plan.TargetWorkspaceRevision);
                }, CancellationToken.None);
            if (!completed) return result;
        }
        catch (Exception ex) when (QualityCurrentFailure(ex)) { }
        // The original plan survives cancellation or a lost response. This opens
        // a new leased observation and never dispatches a second mutation.
        var recovered = RecoverCurrentCareerQuality(owners, expectedOwner, ownerLease, plan, authority);
        return recovered.Outcome == CharacterCareerQualityCommitOutcome.Replayed
            ? recovered with { Outcome = completed ? CharacterCareerQualityCommitOutcome.Applied : CharacterCareerQualityCommitOutcome.Replayed }
            : new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_current_commit_not_observed");
    }

    public CharacterCareerQualityCorrectionResult CorrectCurrentCareerQuality(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterCareerQualityCorrectionPlan correction,
        ICharacterCareerQualityAuthorityResolver authority, CancellationToken cancellationToken = default)
    {
        if (!CharacterCareerQualityTransaction.IsValidCorrection(correction) || correction.OwnerId != expectedOwner.Owner.NormalizedValue)
            return new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_correction_invalid");
        bool completed = false;
        try
        {
            var result = WithQualityEnvironment<CharacterCareerQualityCorrectionResult>(owners, expectedOwner, ownerLease,
                new(correction.WorkspaceId), authority, (saved, exact, runtime, current, path, ledger) =>
                {
                    var prior = LookupQualityCorrection(saved, expectedOwner, correction, exact, runtime);
                    if (prior.Outcome != CharacterCareerQualityCommitOutcome.NotFound)
                    {
                        current?.ValidateFreeSpriteReadObservation();
                        return prior;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    using var catalog = exact.Read(expectedOwner.Owner, saved, runtime);
                    if (catalog is null || catalog.Binding.ContentDigest != correction.ExpectedContentDigest
                        || !CharacterCareerQualityTransaction.TryBuildCorrection(expectedOwner.Owner, saved, correction, runtime,
                            out var replacement, out var entry))
                        return new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_current_correction_conflict");
                    WriteCurrentLifestyle(saved, replacement!, correction.TargetWorkspaceRevision, current, path, ledger,
                        catalog.IsCurrent, () => ValidateQualityOwner(owners, expectedOwner, ownerLease), cancellationToken);
                    completed = true;
                    return new(CharacterCareerQualityCommitOutcome.Applied, entry, correction.TargetWorkspaceRevision);
                }, CancellationToken.None);
            if (!completed) return result;
        }
        catch (Exception ex) when (QualityCurrentFailure(ex)) { }
        // Correction reconciliation is read-only, including after a process restart.
        try
        {
            return WithQualityEnvironment(owners, expectedOwner, ownerLease, new(correction.WorkspaceId), authority,
                (saved, exact, runtime, current, _, _) =>
                {
                    var result = LookupQualityCorrection(saved, expectedOwner, correction, exact, runtime);
                    current?.ValidateFreeSpriteReadObservation();
                    return result.Outcome == CharacterCareerQualityCommitOutcome.Replayed
                        ? result with { Outcome = completed ? CharacterCareerQualityCommitOutcome.Applied : CharacterCareerQualityCommitOutcome.Replayed }
                        : new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_current_correction_not_observed");
                }, CancellationToken.None);
        }
        catch (Exception ex) when (QualityCurrentFailure(ex))
        { return new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_current_correction_not_observed"); }
    }

    private static CharacterCareerQualityCorrectionResult LookupQualityCorrection(WorkspaceStoredDocument saved,
        OwnerContextStamp owner, CharacterCareerQualityCorrectionPlan correction, ICharacterCareerQualityAuthorityResolver authority,
        string runtime)
    {
        var result = CharacterCareerQualityTransaction.LookupCorrection(owner.Owner, saved, correction);
        if (result.Outcome != CharacterCareerQualityCommitOutcome.Replayed) return result;
        using var current = authority.Read(owner.Owner, saved, runtime);
        return current is not null && current.Binding.ContentDigest == correction.ExpectedContentDigest && current.IsCurrent()
            ? result : new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_correction_source_changed");
    }
}
