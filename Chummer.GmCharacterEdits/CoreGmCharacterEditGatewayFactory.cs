using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;

namespace Chummer.Engine.GmCharacterEdits;

/// <summary>
/// Composes the canonical delegated-edit application over Core's durable
/// owner-scoped workspace store. The state root is explicit: this boundary
/// never falls back to a temporary directory or an in-memory store.
/// </summary>
public static class CoreGmCharacterEditGatewayFactory
{
    public static ICoreGmCharacterEditGateway CreateStoreBacked(
        string workspaceStorePath,
        ICampaignGmCharacterEditAuthorizer authorizer,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(authorizer);
        if (string.IsNullOrWhiteSpace(workspaceStorePath)
            || !Path.IsPathFullyQualified(workspaceStorePath))
        {
            throw new ArgumentException(
                "An absolute, explicitly provisioned Core workspace-store path is required.",
                nameof(workspaceStorePath));
        }

        string stateRoot = Path.GetFullPath(workspaceStorePath);
        if (!Directory.Exists(stateRoot))
        {
            throw new DirectoryNotFoundException(
                "The configured Core workspace-store path must already exist.");
        }

        var rootInfo = new DirectoryInfo(stateRoot);
        rootInfo.Refresh();
        if (rootInfo.LinkTarget is not null
            || (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "The configured Core workspace-store path cannot be a link or reparse point.");
        }

        var characterFileService = new CharacterFileService();
        var characterSectionService = new CharacterSectionService();
        var fileQueries = new XmlCharacterFileQueries(characterFileService);
        var sectionQueries = new XmlCharacterSectionQueries(characterSectionService);
        var metadataCommands = new XmlCharacterMetadataCommands(characterFileService);
        var resolver = new RulesetWorkspaceCodecResolver(
        [
            new Sr5WorkspaceCodec(fileQueries, sectionQueries, metadataCommands),
            new Sr6WorkspaceCodec(fileQueries, sectionQueries, metadataCommands)
        ]);
        var store = new FileWorkspaceStore(stateRoot);
        return new DelegatedGmCharacterEditService(
            store,
            resolver,
            authorizer,
            timeProvider);
    }
}
