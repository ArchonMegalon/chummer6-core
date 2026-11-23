using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private CharacterCreationMetatypeCatalogAuthority ProjectLifeModuleVariantCatalog8582(
            XDocument document, CharacterCreationMetatypeCatalogAuthority authority)
        {
            var projected = CharacterCreationMetatypeCatalogProjector.ProjectLifeModuleVariants(document, authority,
                _sourceInputs.CreationSourceExecutionSchema, UsesCareerCondition5497);
            if (!projected.IsAuthoritative || projected.MetavariantOptions is null) return projected;
            XElement[] qualities = [];
            bool found = TryComputeEffectiveInputDigest(_catalog, "qualities.xml", out string digest)
                && TryEnumerateTargets("qualities.xml", ["qualities"], "quality", out qualities);
            return projected with { MetavariantOptions = projected.MetavariantOptions.Select(option =>
            {
                if (!option.IsEnabled) return option;
                // The nested-source projector has already proved the exact pair,
                // child ranges/cost/bonuses and movement fallback. Admit its racial
                // grants only through the complete current compiler below, using
                // the selected quality XML and enabled books. Identity alone never
                // enables a child; unsupported effects/restrictions stay disabled.
                var blockers = new List<string>();
                var grants = found ? CharacterCreationMagicResonanceAuthorityProjector.ResolveQualityReferences(
                    option.GrantedQualities.Select(item => (Reference: item.Name, Selection: string.Empty)).ToArray(),
                    qualities, [], digest, string.Empty, projected.SourceContext.EnabledSourcebooks, blockers) : [];
                if (found && blockers.Count == 0 && grants.Length == option.GrantedQualities.Count
                    && grants.Where((grant, index) => !string.Equals(XElement.Parse(grant.CanonicalSourceXml).Element("category")?.Value,
                        option.GrantedQualities[index].Polarity, StringComparison.OrdinalIgnoreCase)).Any() == false
                    && grants.Length > 0 && CharacterCreationLifeModuleRacialGrants8590.TryProject(
                        option, grants, digest, out _, out var racialEffects)
                    && TryValidateLifeModuleRacialSkillTargets8590(racialEffects)) return option;
                return option with { IsEnabled = false, Blockers = ["current-metatype-racial-effects-unavailable"] };
            }).ToArray() };
        }

        private bool TryValidateLifeModuleRacialSkillTargets8590(IReadOnlyList<XElement> effects)
        {
            var names = effects.Where(row => row.Element("improvementttype")?.Value == "Skill")
                .Select(row => row.Element("improvedname")?.Value ?? string.Empty).Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length == 0) return true;
            if (!TryEnumerateTargets("skills.xml", ["skills"], "skill", out var skills)) return false;
            foreach (string name in names)
            {
                var matches = skills.Where(row => row.Element("name")?.Value == name).Take(2).ToArray();
                if (matches is not [var row] || !Guid.TryParseExact(row.Element("id")?.Value, "D", out Guid id)
                    || id == Guid.Empty || !TryResolveActiveSkillSource(id.ToString("D"), out var source)
                    || source.Name != name || source.IsExotic
                    || !XNode.DeepEquals(XElement.Parse(row.ToString(SaveOptions.DisableFormatting)), XElement.Parse(source.RawSourceXml))) return false;
            }
            return !_sourceInputs.HasSourceDrift;
        }

        private static CharacterCreationMetatypeOptionProjection CopyMetatypeOption8582(CharacterCreationMetatypeOptionProjection option)
            => option with
            {
                Attributes = option.Attributes.ToArray(),
                GrantedQualities = option.GrantedQualities.Select(row => row with { SourceAnchorIds = row.SourceAnchorIds.ToArray() }).ToArray(),
                ExcludedMetavariants = option.ExcludedMetavariants.Select(row => row with
                    { Blockers = row.Blockers.ToArray(), SourceAnchorIds = row.SourceAnchorIds.ToArray() }).ToArray(),
                Blockers = option.Blockers.ToArray(), SourceAnchorIds = option.SourceAnchorIds.ToArray()
            };
    }
}
