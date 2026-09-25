using Chummer.Application.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

/// <summary>Deterministic review encoding, not a signature or permission to mutate.</summary>
public static class LifeModuleEffectReviewIntegrity
{
    public static LifeModuleEffectReview Seal(LifeModuleEffectReview review)
        => review with { ReviewDigest = Digest(review) };

    internal static bool IsValid(LifeModuleEffectReview review)
        => review.Request is not null && review.Contributions is not null
           && review.Contributions.All(item => item is not null && item.SourceAnchorIds is not null
               && item.DescriptiveMetadata is not null)
           && LifeModuleDecisionInputIntegrity.TryNormalize(review.Request.Values, out _)
           && review.ReviewDigest == Digest(review);

    private static string Digest(LifeModuleEffectReview review)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            Semantics = "chummer.life-module.compiler-contributions/v1;not-cumulative-character-ratings",
            Review = review with { ReviewDigest = string.Empty }
        });
}
