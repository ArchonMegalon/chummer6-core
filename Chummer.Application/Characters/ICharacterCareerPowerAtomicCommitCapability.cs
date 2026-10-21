using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

public interface ICharacterCareerPowerAuthorityResolver
{
    ICharacterCareerPowerAdmission? Open(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerPowerCommand command, string runtimeFingerprint);
    ICharacterCareerPowerAdmission? OpenDetailed(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerPowerCommand command, string runtimeFingerprint, out string? blocker)
    {
        var admission = Open(owner, saved, command, runtimeFingerprint);
        blocker = admission is null ? "power_authority_unavailable" : null;
        return admission;
    }
}

public interface ICharacterCareerPowerAdmission : IDisposable
{
    CharacterCareerPowerPlan Plan { get; }
    string CharacterXml { get; }
    IReadOnlyList<CharacterCareerPowerGrant> Grants { get; }
    bool IsCurrent();
}

/// <summary>Caller holds owner-transition admission. Store holds its ordinary workspace lease.</summary>
public interface ICharacterCareerPowerAtomicCommitCapability
{
    string CareerPowerRuntimeFingerprint { get; }
    CharacterCareerPowerResult CommitCareerPower(OwnerScope owner, CharacterCareerPowerPlan plan,
        ICharacterCareerPowerAuthorityResolver authority, CancellationToken cancellationToken = default);
}
