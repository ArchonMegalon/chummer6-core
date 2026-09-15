using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using Chummer.Application;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Explain;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>
/// Composes an actual SR5 continuation restore and rule-query runtime in newly
/// owned private scratch storage. The host must already authorize the supplied
/// exact nonlocal owner and full continuation. This factory is not authentication,
/// consent, a user-store restore endpoint, or a provider admission service.
/// </summary>
public sealed class PrivateWorkspaceRuleRuntimeFactory
{
    // This runtime's bounded admission policy, not a new wire-schema limit.
    internal const int MaximumContinuationBytes = 512 * 1024;
    private readonly OwnedWorkspaceScratchDirectory.Root _scratchRoot;
    private readonly string _baseDirectory;
    private readonly string _currentDirectory;
    private readonly string? _configuredAmendsPath;
    private readonly TimeProvider _clock;

    public PrivateWorkspaceRuleRuntimeFactory(string privateScratchRoot,
        string baseDirectory, string currentDirectory, string? configuredAmendsPath,
        TimeProvider? clock = null)
    {
        _scratchRoot = OwnedWorkspaceScratchDirectory.ValidateRoot(privateScratchRoot);
        _baseDirectory = RequireSourceDirectory(baseDirectory, nameof(baseDirectory));
        _currentDirectory = RequireSourceDirectory(currentDirectory, nameof(currentDirectory));
        _configuredAmendsPath = RequireAmendsPaths(configuredAmendsPath);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Reviews and explicitly confirms the exact complete Core wire envelope
    /// before publishing a runtime. All failures close the private issuer and
    /// clean owned scratch where its identity can still be safely established.
    /// There is no import into a caller/user store and no automatic write retry.
    /// </summary>
    public PrivateWorkspaceRuleRuntime Create(OwnerScope authorizedOwner,
        ReadOnlyMemory<byte> completeContinuation, bool explicitlyConfirmed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owners = new RequestOwnerContextAccessor(authorizedOwner);
        OwnedWorkspaceScratchDirectory? scratch = null;
        byte[]? captured = null;
        bool published = false;
        try
        {
            if (!explicitlyConfirmed || completeContinuation.IsEmpty
                || completeContinuation.Length > MaximumContinuationBytes)
                throw RestoreDenied();
            captured = completeContinuation.ToArray();
            if (!WorkspaceContinuationCodec.TryDecodeCandidate(captured, MaximumContinuationBytes, out var candidate)
                || !string.Equals(candidate.Snapshot.OwnerId, authorizedOwner.Value, StringComparison.Ordinal))
                throw RestoreDenied();

            OwnerContextStamp stamp = owners.Capture();
            cancellationToken.ThrowIfCancellationRequested();
            scratch = OwnedWorkspaceScratchDirectory.Allocate(_scratchRoot);
            var store = new FileWorkspaceStore(scratch.DirectoryPath);
            var overlays = new FileSystemContentOverlayCatalogService(
                _baseDirectory, _currentDirectory, _configuredAmendsPath);
            var sources = new FileSystemCharacterSourceDataResolver(overlays);
            var files = new CharacterFileService();
            var fileQueries = new XmlCharacterFileQueries(files);
            var sections = new XmlCharacterSectionQueries(new CharacterSectionService(sources));
            var metadata = new XmlCharacterMetadataCommands(files);
            var codec = new Sr5WorkspaceCodec(fileQueries, sections, metadata);
            var codecs = new RulesetWorkspaceCodecResolver([codec]);
            var lifeModules = new XmlLifeModulesCatalogService(LifeModulesCatalogPathResolver.Resolve(overlays));
            var restore = new WorkspaceContinuationRestoreService(store, owners, sources,
                fileQueries, lifeModules, MaximumContinuationBytes, _clock);

            using WorkspaceContinuationRestoreReview review = restore.Review(stamp, captured);
            if (review.Result.Outcome != WorkspaceContinuationRestoreOutcome.Available)
                throw RestoreDenied();
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceContinuationRestoreResult result = restore.Confirm(review, explicitlyConfirmed, cancellationToken);
            if (result.Outcome == WorkspaceContinuationRestoreOutcome.Canceled)
                throw new OperationCanceledException(cancellationToken);
            if (result.Outcome != WorkspaceContinuationRestoreOutcome.Applied
                || result.ReopenRequired || result.Receipt is not { } receipt
                || receipt.ContentRevision != candidate.Snapshot.Workspace.ContentRevision
                || receipt.SavedRevision != candidate.Snapshot.Workspace.SavedRevision
                || !string.Equals(receipt.SnapshotDigest, candidate.SnapshotDigest, StringComparison.Ordinal))
                throw RestoreDenied();

            // The actual read capability must reopen the whole restored carrier,
            // not merely report a projected document or a caller-supplied hash.
            var export = new WorkspaceContinuationExportService(store, owners)
                .Export(stamp, candidate.Snapshot.Workspace.Id);
            if (!export.Success || export.Outcome != WorkspaceOperationOutcome.Success
                || export.Value is not { } restored
                || !string.Equals(restored.SnapshotDigest, candidate.SnapshotDigest, StringComparison.Ordinal))
                throw RestoreDenied();

            cancellationToken.ThrowIfCancellationRequested();
            var runtime = new PrivateWorkspaceRuleRuntime(owners, scratch,
                new WorkspaceRuleQuestionService(owners, store, codecs, sources), stamp,
                restored.Snapshot.Workspace.Id, restored.Snapshot.Workspace.ContentRevision,
                restored.Snapshot.Workspace.SavedRevision, receipt);
            published = true;
            return runtime;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                          or InvalidOperationException or JsonException or XmlException)
        {
            // Fixed local status only: never copy carrier contents or source
            // paths into an exception or attach the underlying exception.
            throw RestoreDenied();
        }
        finally
        {
            if (captured is not null)
                CryptographicOperations.ZeroMemory(captured);
            if (!published)
            {
                // End first. Do not delete in an unconditional finally after an
                // unsuccessful authority termination. A private commit may have
                // completed before cancellation; cleaning it is not rollback.
                owners.Dispose();
                scratch?.Dispose();
            }
        }
    }

    private static string RequireSourceDirectory(string path, string argument)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.Any(char.IsControl) || !Directory.Exists(path))
            throw new ArgumentException("An explicit existing source directory is required.", argument);
        return Path.GetFullPath(path);
    }

    private static string? RequireAmendsPaths(string? configured)
    {
        if (configured is null)
            return null;
        if (string.IsNullOrWhiteSpace(configured))
            throw new ArgumentException("Explicit amend directories or null are required.", nameof(configured));
        string[] paths = configured.Split([Path.PathSeparator, ';'], StringSplitOptions.TrimEntries);
        return string.Join(Path.PathSeparator,
            paths.Select(path => RequireSourceDirectory(path, nameof(configured))));
    }

    private static InvalidOperationException RestoreDenied()
        => new("The private workspace continuation could not be admitted.");
}
