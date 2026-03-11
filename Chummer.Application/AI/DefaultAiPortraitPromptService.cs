using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class DefaultAiPortraitPromptService : IAiPortraitPromptService
{
    private readonly IAiDigestService _aiDigestService;

    public DefaultAiPortraitPromptService(IAiDigestService aiDigestService)
    {
        _aiDigestService = aiDigestService;
    }

    public AiPortraitPromptProjection? CreatePortraitPrompt(OwnerScope owner, AiPortraitPromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? characterId = NormalizeOptional(request.CharacterId);
        if (characterId is null)
        {
            return null;
        }

        AiCharacterDigestProjection? characterDigest = _aiDigestService.GetCharacterDigest(owner, characterId);
        if (characterDigest is null)
        {
            return null;
        }

        string runtimeFingerprint = NormalizeOptional(request.RuntimeFingerprint) ?? characterDigest.RuntimeFingerprint;
        AiRuntimeSummaryProjection? runtimeSummary = _aiDigestService.GetRuntimeSummary(owner, runtimeFingerprint, characterDigest.RulesetId);
        if (runtimeSummary is null)
        {
            return null;
        }

        string stylePackId = NormalizeOptional(request.StylePackId) ?? "default-decker-contact";
        string flavor = NormalizeOptional(request.PromptFlavor) ?? "secure comm-channel dossier portrait";
        string alias = string.IsNullOrWhiteSpace(characterDigest.Summary.Alias)
            ? characterDigest.Summary.Name
            : $"{characterDigest.Summary.Name} ({characterDigest.Summary.Alias})";
        string basePrompt = $"Half-length cyberpunk portrait of {alias}, a {characterDigest.Summary.Metatype} Shadowrun {runtimeSummary.RulesetId.ToUpperInvariant()} runner. " +
            $"Lean into {flavor}, grounded by runtime '{runtimeSummary.Title}', {characterDigest.Summary.BuildMethod} build cues, and street-level neon atmosphere. " +
            $"Keep gear hints subtle, emphasize readable facial features, and avoid logo-heavy background clutter.";

        if (!string.Equals(stylePackId, "default-decker-contact", StringComparison.Ordinal))
        {
            basePrompt += $" Style pack: {stylePackId}.";
        }

        string[] tags =
        [
            runtimeSummary.RulesetId,
            characterDigest.Summary.Metatype.ToLowerInvariant(),
            characterDigest.Summary.BuildMethod.ToLowerInvariant(),
            stylePackId
        ];
        string[] notes =
        [
            $"Runtime: {runtimeSummary.Title}",
            $"Character: {characterDigest.DisplayName}",
            $"Content bundles: {runtimeSummary.ContentBundles.Count}",
            $"Rule packs: {runtimeSummary.RulePacks.Count}"
        ];
        AiPortraitPromptVariant[] variants =
        [
            new(AiPortraitPromptVariantKinds.Primary, "Primary Portrait", basePrompt),
            new(AiPortraitPromptVariantKinds.Mugshot, "Mugshot Variant", $"{basePrompt} Neutral lighting, intake-camera framing, plain background, official mugshot energy."),
            new(AiPortraitPromptVariantKinds.Undercover, "Undercover Variant", $"{basePrompt} Slightly cleaner wardrobe, low-key fake-SIN headshot presentation, restrained cyberware visibility."),
            new(AiPortraitPromptVariantKinds.Dossier, "Dossier Card Variant", $"{basePrompt} Leave space for dossier typography, mission-packet crop, and clean side-card composition.")
        ];

        return new AiPortraitPromptProjection(
            CharacterId: characterDigest.CharacterId,
            DisplayName: characterDigest.DisplayName,
            RuntimeFingerprint: runtimeSummary.RuntimeFingerprint,
            RulesetId: runtimeSummary.RulesetId,
            Prompt: basePrompt,
            Tags: tags,
            Notes: notes,
            Variants: variants,
            StylePackId: stylePackId);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
