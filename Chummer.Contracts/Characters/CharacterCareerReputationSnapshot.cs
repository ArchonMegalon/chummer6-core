using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

/// <summary>Indexes are zero-based saved XML row identities within SourceDigest, not global IDs.</summary>
public sealed record CharacterCareerReputationExpenseContribution(
    int ExpenseIndex, decimal Amount, bool Included, int CareerKarmaContribution);

/// <summary>
/// Applicable is the enabled/Career/non-rating filter. Selected additionally
/// accounts for unique and precedence competition (and the pinned Erased
/// contributor-list behavior). A selected zero-valued Erased improvement is
/// meaningful; presence must not be inferred from its sum.
/// </summary>
public sealed record CharacterCareerReputationImprovementContribution(
    int ImprovementIndex, string ImprovementType, string ImprovedName, string UniqueName,
    decimal Value, bool Custom, bool Applicable, bool Selected);

/// <summary>
/// Read-only projection of a clean saved workspace and its captured profile.
/// Digests are content bindings, not signatures, approvals or write permission.
/// A future persistence owner must reload all authority and perform atomic CAS.
/// </summary>
public sealed record CharacterCareerReputationSnapshot(
    CharacterWorkspaceId WorkspaceId, long ContentRevision, long SavedRevision,
    string SourceDigest, string AuxiliaryStateDigest, string RuleStateDigest,
    CharacterCareerReputationProjection Reputation,
    IReadOnlyList<CharacterCareerReputationExpenseContribution> Expenses,
    IReadOnlyList<CharacterCareerReputationImprovementContribution> Improvements);
