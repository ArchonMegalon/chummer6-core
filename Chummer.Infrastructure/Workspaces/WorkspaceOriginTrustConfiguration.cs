namespace Chummer.Infrastructure.Workspaces;

/// <summary>
/// Host-trusted public configuration, never imported from a runner or HTTP response.
/// Linux/Android supply their approved directory durability barrier. Windows
/// uses the shared immutable write-through publication path, not directory fsync.
/// </summary>
public sealed record WorkspaceOriginTrustConfiguration(string IssuerId, string IssuerPublicKey,
    Action<string>? SyncDirectory = null, TimeProvider? Clock = null)
{
    public FileWorkspaceStore.OriginTrustDomain Create(string privateDirectory)
        => new(privateDirectory, IssuerId, IssuerPublicKey, SyncDirectory, Clock);
}
