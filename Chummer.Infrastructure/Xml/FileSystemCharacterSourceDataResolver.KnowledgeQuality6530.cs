using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceInputSnapshot
    {
        public string? RetainedPolicyDigest6530 => _retained?.PolicyDigest;
    }

    private sealed partial class SourceDataContext
    {
        // Policy/capture APIs are synchronous. This scope replays the former
        // conservative classification only for an exact archived policy digest;
        // it never supplies source bytes or authorizes an effect or a write.
        [ThreadStatic] private static SourceDataContext? _legacyQualityKnowledgePolicy6530;
        private bool UsesLegacyQualityKnowledgePolicy6530
            => ReferenceEquals(_legacyQualityKnowledgePolicy6530, this);

        private CharacterCreationEffectivePolicyContext PreserveRetainedKnowledgePolicy6530(
            CharacterCreationEffectivePolicyContext observed)
        {
            if (UsesLegacyQualityKnowledgePolicy6530
                || _sourceInputs.RetainedPolicyDigest6530 is not { } expected
                || observed.PolicyDigest == expected) return observed;
            var previous = _legacyQualityKnowledgePolicy6530;
            try
            {
                _legacyQualityKnowledgePolicy6530 = this;
                // Old captures may have recorded unavailable Skills solely because
                // native Quality rows were not recognized. Preserve that exact
                // policy on replay; never upgrade the archived authority claim.
                if (TryResolveCreationEffectivePolicy(out var legacy)
                    && legacy is not null && legacy.PolicyDigest == expected) return legacy;
                return observed; // The ordinary exact capture fence still refuses.
            }
            finally { _legacyQualityKnowledgePolicy6530 = previous; }
        }

        private bool IsKnowledgeNeutralQualityImprovement6530(XElement row)
        {
            if (UsesLegacyQualityKnowledgePolicy6530 || row.Name != "improvement"
                || (string?)row.Element("improvementsource") != "Quality"
                || (string?)row.Element("improvementttype") is null or "FreeKnowledgeSkills"
                || !Guid.TryParseExact((string?)row.Element("sourcename"), "D", out var ownerId)
                || ownerId == Guid.Empty) return false;
            var owners = _character.Elements("qualities").Elements("quality")
                .Where(quality => (string?)quality.Element("guid") == ownerId.ToString("D")).Take(2).ToArray();
            if (owners is not [var owner]
                || !Guid.TryParseExact((string?)owner.Element("sourceid"), "D", out var sourceId)
                || sourceId == Guid.Empty
                || !TryResolveCareerQualitySource(sourceId, out var source) || source is null) return false;

            // Reuse the real Career compiler/graph comparison, including the
            // complete saved Quality and all its owned effects. A type name or
            // owner GUID alone cannot make forged/unknown fields neutral. No XML
            // is removed or rewritten, and no arm count or source is invented.
            string characterXml = _character.ToString(SaveOptions.DisableFormatting);
            if (CharacterCareerQualityGraph.TryProject(characterXml,
                source, new(ownerId, sourceId), CharacterCareerQualityOperation.RemoveAllLevels,
                resolvedArmCount: null, out _)) return true;
            // Some otherwise supported definitions bind their level limit to
            // anatomy. Use the same complete source-backed proof as Career;
            // unknown contributors remain unresolved.
            return TryResolveCareerQualityAnatomySources(out var anatomy) && anatomy is not null
                && CharacterCreationLegacySourceProjector.TryResolveCareerArmCount(characterXml, anatomy, out var arms)
                && CharacterCareerQualityGraph.TryProject(characterXml, source, new(ownerId, sourceId),
                    CharacterCareerQualityOperation.RemoveAllLevels, arms, out _);
        }
    }
}
