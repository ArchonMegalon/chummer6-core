using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerInitiationCurrentEnvironmentCapability
{
    public WorkspaceStoreReadResult ReadCareerInitiationWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId workspace)
    {
        var stamp = lease.Stamp;
        if (!LearningOwnerCurrent5703(owners, lease, stamp)) return UnavailableRead();
        try
        {
            string? path = TryGetPath(stamp.Owner, workspace);
            if (path is null) return new(WorkspaceOperationOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, workspace, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            current?.ValidateReadObservation();
            return LearningOwnerCurrent5703(owners, lease, stamp) ? read : UnavailableRead();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return UnavailableRead(); }
    }

    private ICharacterCareerInitiationAdmission? OpenInitiation5722(OwnerContextStamp stamp,
        WorkspaceStoredDocument saved, CharacterCareerInitiationSelection selection, string runtime,
        ICharacterCareerInitiationAuthorityResolver authority, GearCurrentEnvironment? current)
    {
        if (current is null) return authority.Open(stamp, saved, selection, runtime);
        if (authority is not CharacterCareerInitiationAuthorityResolver builtIn) return null;
        current.ValidateReadObservation();
        var resolver = current.Capture.CreateCurrentEnvironmentResolution(current.Execution.ExecutionProfile,
            current.Execution.ExecutionProfileFingerprint)?.Resolver;
        return resolver is null ? null : builtIn.Open(stamp, saved, selection, runtime,
            resolver, LearningContext5703(current, saved));
    }

    public CharacterCareerInitiationQuote? QuoteCareerInitiation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId workspace, CharacterCareerInitiationSelection selection,
        ICharacterCareerInitiationAuthorityResolver authority)
    {
        var stamp = lease.Stamp;
        if (!LearningOwnerCurrent5703(owners, lease, stamp)) return null;
        try
        {
            string? path = TryGetPath(stamp.Owner, workspace);
            if (path is null) return null;
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, workspace, path, owners, lease, stamp,
                out var ledger, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved || HasCareerInitiationCampaignAuthority(saved, ledger)
                || !CharacterCareerInitiationTransaction.ObserveHistory(saved)) return null;
            string? slot = ReadContinuationSlotUnderLease(path);
            string beforeDigest = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
            string runtime = LearningRuntime5703(CareerInitiationRuntimeFingerprint, current, slot, stamp);
            using var admission = OpenInitiation5722(stamp, saved, selection, runtime, authority, current);
            if (admission is null || !admission.IsCurrent()) return null;
            var quote = admission.Quote;
            if (current is not null && quote.CanApply)
            {
                // Private feasibility only: no receipt or operation is published by a quote.
                // The caller retains its own issued plan/ID for confirmation and recovery.
                var preview = new CharacterCareerInitiationPlan(Guid.NewGuid(), quote,
                    DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified));
                if (!CharacterCareerInitiationTransaction.TryBuild(stamp, saved, preview, admission, runtime,
                    out var replacement, out _)) return null;
                var proposed = saved with { Document = replacement!, ContentRevision = preview.TargetWorkspaceRevision,
                    SavedRevision = preview.TargetSavedRevision };
                var after = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, proposed);
                if (after.AfterCapture is null || after.AfterExecution is null || after.Blockers.Count != 0)
                    quote = quote with { Blockers = quote.Blockers.Append("initiation_proposed_environment_unavailable").ToArray() };
            }
            current?.ValidateReadObservation();
            return LearningOwnerCurrent5703(owners, lease, stamp) && slot == ReadContinuationSlotUnderLease(path)
                && beforeDigest == CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) && admission.IsCurrent()
                ? quote : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return null; }
    }

    public bool TryReadCurrentCareerMagic5722(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterWorkspaceId workspace, ICharacterSourceDataResolver sources, out CharacterCareerMagicRead? result)
    {
        result = null;
        var stamp = lease.Stamp;
        if (!LearningOwnerCurrent5703(owners, lease, stamp)) return true;
        try
        {
            string? path = TryGetPath(stamp.Owner, workspace);
            if (path is null) return true;
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, workspace, path, owners, lease, stamp,
                out var ledger, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return true;
            if (current is null) return false;
            var effective = LearningSources5703(current, sources);
            using var scope = (effective as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var resolver = scope ?? effective;
            var context = resolver.TryCreateContext(saved.Document.Content);
            var imported = LearningContext5703(current, saved);
            if (context is null) return true;
            var candidate = OwnerBoundCharacterCareerMagicReader.ReadCurrent5722(stamp, saved, context, imported);
            if (candidate is null || resolver.TryCreateContext(saved.Document.Content) is not { } fresh
                || !CharacterCareerInitiationRules.Same(candidate,
                    OwnerBoundCharacterCareerMagicReader.ReadCurrent5722(stamp, saved, fresh, imported))
                || !CharacterCareerInitiationRules.Same(candidate,
                    OwnerBoundCharacterCareerMagicReader.ReadCurrent5722(stamp, saved, context, imported))) return true;
            current.ValidateReadObservation();
            if (LearningOwnerCurrent5703(owners, lease, stamp))
                result = HasCareerInitiationCampaignAuthority(saved, ledger)
                    ? candidate with { AllowsOwnerManagedTransactions = false } : candidate;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return true; }
    }

    [ThreadStatic] private static InitiationWriteAdmission5722? _initiationAdmission5722;
    private sealed class InitiationWriteAdmission5722 : IDisposable
    {
        private readonly InitiationWriteAdmission5722? previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string recordDigest;
        private readonly WorkspaceStoredDocument after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? readmission;
        internal InitiationWriteAdmission5722(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult? readmission)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current;
            this.after = after; this.readmission = readmission;
            recordDigest = CharacterCreationFinalizationDigest.Compute(record);
            previous = _initiationAdmission5722; _initiationAdmission5722 = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != recordDigest)
                throw new IOException("initiation_exact_record_changed");
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (readmission?.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution
                || !execution.Revalidate(after, capture, Store._retainedCharacterQueries!))
                throw new IOException("initiation_proposed_environment_changed");
        }
        public void Dispose() => _initiationAdmission5722 = previous;
    }
    private InitiationWriteAdmission5722? InitiationAdmissionFor5722(string path)
        => _initiationAdmission5722 is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;
}
