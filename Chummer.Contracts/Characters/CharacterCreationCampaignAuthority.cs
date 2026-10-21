using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

/// <summary>
/// Comparison data only. Neither this binding, its digest, nor a serialized
/// assessment supplies the live execution required by Complete.
/// </summary>
public sealed record CharacterCreationCampaignBinding(
    string Schema,
    WorkspaceContinuationCompletionIdentity RetainedIdentity,
    string TransitionDigest)
{
    public const string SchemaV1 = "chummer.creation-campaign-authority.v1";
    public override string ToString() => nameof(CharacterCreationCampaignBinding);
}

public enum CharacterCreationCampaignState
{
    Unavailable,
    Denied,
    AuthenticatedAbsence,
    AdmittedUnchangedPolicy
}

// Bounded diagnostic categories; no provider messages, principal IDs or tokens.
public enum CharacterCreationCampaignFailure
{
    None,
    Unavailable,
    Denied,
    BindingChanged,
    RetainedIdentityChanged,
    UnsupportedRequirement,
    WrongPhase
}

public enum CharacterCreationCampaignPhase { Preparation, Review, Replay, Apply }
