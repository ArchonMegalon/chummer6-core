namespace Chummer.Contracts.Characters;

/// <summary>Canonical resource bound shared by purchase planning and advice decoding.
/// This limit grants no purchase permission, source authority or policy coverage.</summary>
public static class CharacterCreationPurchaseCompletionLimits
{
    public const int MaximumTargets = 64;
}
