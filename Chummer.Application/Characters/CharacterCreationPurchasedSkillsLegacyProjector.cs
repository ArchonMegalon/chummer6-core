using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Legacy serialization shared by independently admitted purchase methods.
/// This internal helper neither prices allocations nor authorizes persistence.</summary>
internal static class CharacterCreationPurchasedSkillsLegacyProjector
{
    internal static bool TryProject(CharacterCreationSkillsCatalog catalog,
        IReadOnlyList<(CharacterCreationKarmaSkillAllocation Allocation, int? Rating, int KarmaCost)> skills,
        IReadOnlyList<(CharacterCreationKarmaSkillGroupAllocation Allocation, bool IsBroken, int KarmaCost)> skillGroups,
        decimal karmaUsed, string quoteDigest, string identityPrefix,
        out XElement? skillsGraph, out CharacterCreationFinalizationDelta[] deltas)
    {
        skillsGraph = null;
        deltas = [];
        try
        {
            var sources = catalog.ActiveSkills.Concat(catalog.KnowledgeSkills)
                .ToDictionary(source => (source.Kind, source.SourceSkillId));
            var active = new XElement("skills");
            var knowledge = new XElement("knoskills");
            var groups = new XElement("groups");
            var changes = new List<CharacterCreationFinalizationDelta>();
            foreach (var skill in skills.OrderBy(item => item.Allocation.Kind, StringComparer.Ordinal)
                         .ThenBy(item => item.Allocation.SourceSkillId, StringComparer.Ordinal)
                         .ThenBy(item => item.Allocation.SpecializationOptionId, StringComparer.Ordinal))
            {
                var allocation = skill.Allocation;
                var source = sources[(allocation.Kind, allocation.SourceSkillId)];
                if (!Guid.TryParseExact(source.SourceSkillId, "D", out var sourceId) || sourceId == Guid.Empty)
                    return false;
                bool isKnowledge = source.Kind == CharacterCreationSkillKinds.Knowledge;
                var specialization = allocation.SpecializationOptionId is { } specializationId
                    ? source.Specializations.Single(item => item.OptionId == specializationId) : null;
                string identity = $"{identityPrefix}-skill:{source.Kind}:{source.SourceSkillId}"
                    + (source.IsExotic ? $":{allocation.SpecializationOptionId}" : string.Empty);
                var node = new XElement("skill",
                    new XElement("guid", StableId(identity)),
                    new XElement("suid", sourceId),
                    new XElement("isknowledge", Boolean(isKnowledge)),
                    new XElement("skillcategory", source.Category),
                    new XElement("requiresgroundmovement", Boolean(source.RequiresGroundMovement)),
                    new XElement("requiresswimmovement", Boolean(source.RequiresSwimMovement)),
                    new XElement("requiresflymovement", Boolean(source.RequiresFlyMovement)),
                    // Only paid personal levels belong here. Group purchases live
                    // in groups/group/karma; Life Module grants stay Improvements.
                    new XElement("karma", allocation.KarmaLevels),
                    new XElement("base", allocation.KnowledgePointLevels),
                    new XElement("notes"),
                    new XElement("name", source.Name),
                    new XElement("buywithkarma", Boolean(
                        allocation.SpecializationPayment == CharacterCreationKarmaSpecializationPayments.Karma)));
                if (source.IsExotic)
                {
                    // A weapon identity is not a +2 specialization. Distinct
                    // identities sharing a source skill require distinct GUIDs.
                    if (specialization is null) return false;
                    node.Add(new XElement("specific", specialization.Name));
                }
                else if (specialization is not null)
                {
                    node.Add(new XElement("specs", new XElement("spec",
                        new XElement("guid", StableId($"{identity}:spec:{specialization.OptionId}")),
                        new XElement("name", specialization.Name),
                        new XElement("free", "False"),
                        new XElement("expertise", "False"))));
                }
                if (isKnowledge)
                    node.Add(new XElement("type", source.Category),
                        new XElement("isnativelanguage", Boolean(allocation.IsNativeLanguage)));
                (isKnowledge ? knowledge : active).Add(node);
                var anchors = source.SourceAnchorIds.Concat(specialization is null
                    ? [] : new[] { specialization.SourceAnchorId });
                changes.Add(new(changes.Count, identity, CharacterCreationFinalizationDeltaKinds.Skill,
                    identity, null, allocation.IsNativeLanguage ? "native"
                        : skill.Rating?.ToString(CultureInfo.InvariantCulture),
                    skill.KarmaCost, 0, Anchors(anchors)));
            }
            foreach (var group in skillGroups.OrderBy(item => item.Allocation.GroupId, StringComparer.Ordinal))
            {
                var source = catalog.SkillGroups.Single(item => item.GroupId == group.Allocation.GroupId);
                string identity = $"{identityPrefix}-skill-group:{source.GroupId}";
                groups.Add(new XElement("group",
                    new XElement("karma", group.Allocation.KarmaLevels),
                    new XElement("base", 0),
                    new XElement("isbroken", Boolean(group.IsBroken)),
                    new XElement("id", StableId(identity)),
                    new XElement("name", source.Name)));
                changes.Add(new(changes.Count, identity, CharacterCreationFinalizationDeltaKinds.SkillGroup,
                    source.GroupId, null, group.Allocation.KarmaLevels.ToString(CultureInfo.InvariantCulture),
                    group.KarmaCost, 0, Anchors(source.SourceAnchorIds)));
            }
            if (changes.Sum(change => change.KarmaCost) != karmaUsed
                || changes.Select(change => change.DeltaId).Distinct(StringComparer.Ordinal).Count() != changes.Count)
                return false;
            var result = new XElement("newskills", new XElement("skillptsmax", 0),
                new XElement("skillgrpsmax", 0), active, knowledge,
                new XElement("skilljackknowledgeskills"), groups);
            // Reject illegal XML characters now, not after a finalizer commits.
            _ = result.ToString(SaveOptions.DisableFormatting);
            skillsGraph = result;
            deltas = changes.ToArray();
            return true;

            Guid StableId(string identity) => CharacterCreationFinalizationProjector.StableGuid(
                $"{identity}:{quoteDigest}");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
                                      or KeyNotFoundException or OverflowException or XmlException)
        {
            return false;
        }
    }

    private static string Boolean(bool value) => value ? "True" : "False";
    private static string[] Anchors(IEnumerable<string> anchors) => anchors
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
