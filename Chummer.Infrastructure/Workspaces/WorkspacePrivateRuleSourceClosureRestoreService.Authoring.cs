using System.Security.Cryptography;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

// Only Core constructs this fence from its own admitted detached closure. The
// caller cannot supply a digest, callback or replacement document as authority.
internal sealed class PrivateAuthoredRuleSourceFence(
    ICharacterSourceDataResolver sources, string characterXml, string profile, string digest)
{
    internal bool IsCurrent(CancellationToken cancellation)
    {
        try { Validate(cancellation); return true; }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (WorkspacePrivateRuleSourceClosure.IsFailure(error)) { return false; }
    }

    internal void Validate(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var export = sources is ICharacterRetainedSettingsExportSource producer
            ? producer.ForCompleteSourceExport() : sources;
        using var scope = (export as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var resolver = scope ?? export;
        if (resolver is not ICharacterCreationRuleSourceCapture collector
            || !collector.TryCaptureChummer5aRuleSources(characterXml, profile, out var capture, out _)
            || capture is null || capture.CompatibilityProfile != profile)
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        // Reimport actual bytes. A custom collector's claimed IntegrityDigest is
        // insufficient; the built-in archive parser must agree on exact identity.
        byte[] bytes = capture.ExportPrivateInputs();
        try
        {
            if (!FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(bytes, digest, out var restored)
                || restored is null || restored.CompatibilityProfile != profile
                || !restored.MatchesOriginalCharacter(characterXml))
                throw WorkspacePrivateRuleSourceClosure.Rejected();
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        cancellation.ThrowIfCancellationRequested();
    }
}

public sealed partial class WorkspacePrivateRuleSourceClosureRestoreService
{
    /// <summary>
    /// Author and review a complete staged source update of an exact saved runner.
    /// Keep stagedSources alive and unchanged until Confirm returns. No mutation
    /// occurs until an explicit Confirm; the current branch is preserved first.
    /// </summary>
    public WorkspacePrivateRuleSourceClosureRestoreReview ReviewAuthoredUpdate(OwnerContextStamp owner,
        WorkspacePrivateRuleSourceClosureBinding baseline, ICharacterSourceDataResolver stagedSources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(stagedSources);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_store.CanUseRuleUpdateDurability()) return Denied("rule-update-durability-unavailable");
            var candidate = _store.AuthorCurrentRuleEnvironmentUpdate(_owners, owner, baseline,
                stagedSources, cancellationToken: cancellationToken);
            var environment = candidate.InspectCurrentEnvironment() ?? throw WorkspacePrivateRuleSourceClosure.Rejected();
            var continuation = candidate.ExportContinuation();
            var sourceFence = new PrivateAuthoredRuleSourceFence(stagedSources,
                continuation.Snapshot.Workspace.Document.Content, environment.CompatibilityProfile, environment.CaptureDigest);
            byte[] bytes = candidate.ExportPrivateBytes();
            try
            {
                return ReviewUpdateCore(owner, bytes, candidate.Binding, stagedSources, null,
                    cancellationToken, baseline, sourceFence);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (OperationCanceledException) { return Denied("rule-update-canceled"); }
        catch (Exception error) when (IsFailure(error)) { return Denied("rule-update-authoring-unavailable"); }

        WorkspacePrivateRuleSourceClosureRestoreReview Denied(string blocker)
            => new(_issuer, owner, null, baseline, _clock.GetUtcNow() + _lifetime,
                new(WorkspaceContinuationRestoreOutcome.Conflict, Blockers: [blocker]));
    }
}
