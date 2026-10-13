using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    // Shared legacy serialization, not creation pricing or creation admission.
    // Requirements, prompted effects and other uncompiled source semantics are
    // deliberately not stripped to manufacture a usable Career definition.
    internal static bool TryBuildCareerQualityInstance(
        string sourceXml, Guid sourceId, Guid internalId, int? armCount,
        bool includeFirstLevelBonus, out CharacterCareerQualityDefinition definition,
        out XElement quality, out XElement[] improvements)
    {
        definition = null!;
        quality = new XElement("quality");
        improvements = [];
        if (sourceId == Guid.Empty || internalId == Guid.Empty
            || sourceXml is not { Length: > 0 and <= CharacterCreationQualitiesRules.MaximumSourceNodeLength })
            return false;
        try
        {
            using var input = new StringReader(sourceXml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = CharacterCreationQualitiesRules.MaximumSourceNodeLength
            });
            var original = XElement.Load(reader);
            var allowed = new HashSet<string>(s_QualitySourceChildren, StringComparer.Ordinal)
                { "chargenonly", "refundkarma" };
            if (original.Nodes().OfType<XText>().Any(node => !string.IsNullOrWhiteSpace(node.Value))
                || ParseBoundedSource(original.ToString(SaveOptions.DisableFormatting), "quality", allowed) is null)
                return false;
            foreach (string marker in new[] { "careeronly", "chargenonly", "onlyprioritygiven", "refundkarma", "nolevels" })
                if (original.Element(marker) is { } node && !IsEmptyMarker(node)) return false;

            // These flags affect Career admission/costs, not Quality.Save's shape.
            XElement metadata = new(original);
            metadata.Elements("chargenonly").Remove();
            metadata.Elements("refundkarma").Remove();
            if (!TryReadQualityDefinition(metadata, out var parsed, armCount)
                || parsed.SourceId != sourceId || parsed.Name.Length > CharacterCareerQualityRules.MaximumTextLength
                || parsed.Karma is < -CharacterCareerQualityRules.MaximumKarma or > CharacterCareerQualityRules.MaximumKarma)
                return false;
            bool noLevels = original.Element("nolevels") is not null;
            bool unlimited = !noLevels && string.Equals(ReadOptionalScalar(original, "limit"),
                "False", StringComparison.OrdinalIgnoreCase);
            definition = new(sourceId, parsed.Name,
                parsed.Type == CharacterCreationQualityType.Positive
                    ? CharacterCareerQualityType.Positive : CharacterCareerQualityType.Negative,
                parsed.Karma, parsed.Implemented, SourceEnabled: true,
                CareerOnly: original.Element("careeronly") is not null,
                ChargenOnly: original.Element("chargenonly") is not null,
                OnlyPriorityGiven: original.Element("onlyprioritygiven") is not null,
                DoubleCostCareer: parsed.DoubleCareer, StagedPurchase: parsed.StagedPurchase,
                RefundKarmaOnRemove: original.Element("refundkarma") is not null,
                NoLevels: noLevels, LimitIsUnlimited: unlimited, LevelLimit: unlimited ? 0 : parsed.MaximumRating,
                Metagenic: parsed.Metagenic, ContributeToBp: parsed.ContributeToBuildPoints,
                CostDiscountDefined: false, CostDiscountProjectionIsExact: true,
                CostDiscountRequirementsMet: false, CostDiscountValue: 0);
            string id = internalId.ToString("D", CultureInfo.InvariantCulture);
            quality = BuildSavedQuality(original, parsed, id);
            improvements = parsed.BonusEffects.Concat(includeFirstLevelBonus ? parsed.FirstLevelEffects : [])
                .Select(effect => BuildImprovement(effect, id, parsed.NotesColor)).ToArray();
            return true;
        }
        catch (Exception exception) when (exception is XmlException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
