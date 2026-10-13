using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

/// <summary>
/// Executable Core-composed authority, never supplied by request/model data.
/// Implementations resolve complete source, eligibility and campaign policy
/// against this saved owner/workspace and keep live drift fencing until disposal.
/// Null means unavailable authority, not permission to substitute a UI quote.
/// </summary>
public interface ICharacterCareerQualityAuthorityResolver
{
    ICharacterCareerQualityAdmission? Open(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerQualityIdentity identity, CharacterCareerQualityOperation operation,
        string runtimeFingerprint);
}

public interface ICharacterCareerQualityAdmission : IDisposable
{
    CharacterCareerQualityInput Input { get; }
    CharacterCareerQualitySource Source { get; }
    int? ResolvedArmCount { get; }
    bool IsCurrent();
}

/// <summary>
/// Dedicated atomic seam. Caller must hold the actual owner transition lease;
/// the store uses the same workspace lease as every other write, resolves fresh
/// authority itself and persists the character, expense and receipt together.
/// </summary>
public interface ICharacterCareerQualityAtomicCommitCapability
{
    string CareerQualityRuntimeFingerprint { get; }
    CharacterCareerQualityCommitResult CommitCareerQuality(OwnerScope owner,
        CharacterCareerQualityPlan plan, ICharacterCareerQualityAuthorityResolver authority,
        CancellationToken cancellationToken = default);

    CharacterCareerQualityCorrectionResult CorrectCareerQuality(OwnerScope owner,
        CharacterCareerQualityCorrectionPlan correction, CancellationToken cancellationToken = default)
        => new(CharacterCareerQualityCommitOutcome.Unavailable);
}
