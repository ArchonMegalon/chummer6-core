using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Characters;

internal static partial class CharacterCreationFoundationEffectCompiler
{
    // Use the very same compiled instructions and numeric parsers as the write
    // planner. Never interpret raw catalog nulls as missing character ratings.
    internal static IReadOnlyList<LifeModuleEffectContribution> ReviewContributions(
        CharacterCreationFoundationEffectCompilation compilation)
    {
        var result = new List<LifeModuleEffectContribution>();
        foreach (var effect in compilation.Effects)
        {
            bool supported = effect.CompilationStatus == CharacterCreationFoundationEffectCompilationStatuses.Supported;
            string kind = effect.EffectKind;
            string targetId = effect.TargetBinding?.SourceId ?? effect.TargetId;
            string targetName = effect.TargetBinding?.CanonicalName ?? effect.TargetId;
            decimal? amount = null;
            var metadata = new Dictionary<string, string>(effect.IgnoredSourceMetadata, StringComparer.Ordinal);
            if (supported)
            {
                string? raw = effect.Parameters.GetValueOrDefault("val");
                switch (kind)
                {
                    case "attributelevel":
                        amount = ParseLegacyAttributeLevelValue(raw);
                        break;
                    case "skilllevel":
                    case "skillgrouplevel":
                        amount = ParseLegacySkillLevelValue(raw);
                        break;
                    case "knowledgeskilllevel":
                        // The oracle grants a pool, not the named language or
                        // knowledge skill. Preserve those labels as metadata.
                        amount = ParseLegacyKnowledgeSkillLevelValue(raw);
                        break;
                    case "qualitylevel":
                        amount = decimal.Parse(effect.TargetId, CultureInfo.InvariantCulture);
                        metadata["group"] = effect.Parameters["@group"];
                        break;
                    case "freepositivequalities":
                    case "freenegativequalities":
                        amount = decimal.Parse(effect.TargetId, NumberStyles.Any, CultureInfo.InvariantCulture);
                        break;
                    case "addqualities":
                        foreach (var quality in compilation.DependentQualities.Where(item => item.EffectId == effect.EffectId))
                        {
                            string selection = compilation.SelectionBindings
                                .FirstOrDefault(item => item.ConsumerId == quality.SelectionConsumerId)?.Literal ?? string.Empty;
                            result.Add(new(effect.EffectId, kind, quality.TargetBinding.SourceId,
                                quality.TargetBinding.CanonicalName, null, selection, metadata,
                                [.. effect.SourceAnchorIds, $"qualities.xml#quality:{quality.TargetBinding.SourceId}"],
                                quality.CompilationStatus, quality.Blocker));
                        }
                        continue;
                }
            }
            result.Add(new(effect.EffectId, kind, targetId, targetName, amount,
                kind == "pushtext"
                    ? compilation.SelectionPushes.FirstOrDefault(item => item.EffectId == effect.EffectId)?.Literal ?? string.Empty
                    : string.Empty,
                metadata, effect.SourceAnchorIds, effect.CompilationStatus, effect.Blocker));
        }
        return result;
    }
}
