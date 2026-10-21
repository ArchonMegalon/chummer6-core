using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed record CharacterCareerInitiationSourceRow(string Xml, bool Enabled);
public sealed record CharacterCareerInitiationSources(
    string SettingsXml, string AuthorityState, IReadOnlyList<CharacterCareerInitiationSourceRow> Metamagics);

/// <summary>Implemented by a source context; not a permission supplied by a client.</summary>
public interface ICharacterCareerInitiationSourceContext
{
    bool TryResolveCareerInitiationSources(out CharacterCareerInitiationSources? sources);
}

public interface ICharacterCareerInitiationAuthorityResolver
{
    ICharacterCareerInitiationAdmission? Open(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerInitiationSelection selection, string runtimeFingerprint);
}

public interface ICharacterCareerInitiationAdmission : IDisposable
{
    CharacterCareerInitiationQuote Quote { get; }
    bool IsCurrent();
}

/// <summary>Caller holds the complete owner transition lease. Store holds its workspace lease.</summary>
public interface ICharacterCareerInitiationAtomicCommitCapability
{
    string CareerInitiationRuntimeFingerprint { get; }
    WorkspaceStoreReadResult ReadCareerInitiationWorkspace(OwnerContextStamp owner,
        CharacterWorkspaceId workspace);
    CharacterCareerInitiationResult CommitCareerInitiation(OwnerContextStamp owner,
        CharacterCareerInitiationPlan plan, ICharacterCareerInitiationAuthorityResolver authority,
        CancellationToken cancellationToken = default);
}
