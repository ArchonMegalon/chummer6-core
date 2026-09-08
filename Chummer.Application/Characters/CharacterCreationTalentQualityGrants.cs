using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Joins independently resolved Talent quality definitions into the Quality budget.
/// Heritage origin, not a zeroed source cost or a name allowlist, makes these free.
/// This is a budget projection only; it does not authorize saved bonus effects.
/// </summary>
internal static class CharacterCreationTalentQualityGrants
{
    internal static bool TryBind(CharacterCreationPrerequisiteDraft prerequisite,
        ICharacterSourceDataContext context, CharacterCreationQualitiesAuthority original,
        out CharacterCreationQualitiesAuthority authority)
    {
        authority = original;
        if (prerequisite.TalentSelection is not { } selected
            || selected.GrantedQualities is null)
            return false;
        if (selected.GrantedQualities.Count == 0)
            return true;
        if (!TryResolveTalent(prerequisite, context, out var magic, out var talent)) return false;
        var grants = new List<CharacterCreationGrantedQuality>();
        foreach (var source in talent.GrantedQualitySources!)
        {
            if (!TryProject(source, prerequisite.DraftDigest, out var grant)) return false;
            grants.Add(grant);
        }
        if (grants.Any(grant => original.GrantedQualities.Any(existing =>
                existing.GrantId == grant.GrantId || existing.SelectionKey == grant.SelectionKey)))
            return false;
        authority = original with
        {
            GrantedQualities = original.GrantedQualities.Concat(grants)
                .OrderBy(item => item.GrantId, StringComparer.Ordinal).ToArray(),
            SourceAnchorIds = original.SourceAnchorIds.Concat(grants.SelectMany(item => item.SourceAnchorIds))
                .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            RuntimeDigest = CharacterCreationMagicResonanceDigest.Compute(new
            {
                Schema = "chummer.sr5.creation_quality_talent_budget/v1",
                BaseRuntimeDigest = original.RuntimeDigest,
                TalentRuntimeDigest = magic.RuntimeDigest,
                TalentSourceDigest = talent.SourceNodeDigest,
                GrantDigests = grants.Select(item => item.GrantDigest).ToArray()
            }),
            AuthorityDigest = string.Empty
        };
        authority = authority with { AuthorityDigest = CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority) };
        return true;
    }

    // Shared source join for Quality budgets and Skills access. Neither caller
    // may infer permissions from a Talent's display name or cached UI state.
    internal static bool TryResolveTalent(CharacterCreationPrerequisiteDraft prerequisite,
        ICharacterSourceDataContext context, out CharacterCreationMagicResonanceAuthority magic,
        out CharacterCreationMagicResonanceTalentOption talent)
    {
        magic = null!;
        talent = null!;
        if (prerequisite.TalentSelection is not { GrantedQualities: not null } selected
            || !context.TryResolveCreationMagicResonanceAuthority(out magic)
            || !CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(magic)
            || !magic.IsAuthoritative || magic.Blockers.Count != 0)
            return false;
        var assignments = prerequisite.Assignments.Where(item =>
            item.CategoryId == CharacterCreationPriorityCategoryIds.Talent).Take(2).ToArray();
        if (assignments.Length != 1) return false;
        var matches = magic.Talents.Where(item => item.Identity.PrioritySourceId == assignments[0].SourceId
            && item.Rank == assignments[0].Rank
            && item.Identity.TalentSelectionId == selected.SelectionId
            && item.Identity.TalentValue == selected.Value
            && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                item.SourceNodeDigest, selected.PriorityChildNodeDigest)).Take(2).ToArray();
        if (matches.Length != 1 || !matches[0].IsEnabled || matches[0].Blockers.Count != 0
            || !CharacterCreationTalentQualitySourceRules.MatchesTalent(
                matches[0].CanonicalSourceXml, matches[0].GrantedQualitySources)
            || !selected.GrantedQualities.SequenceEqual(
                matches[0].GrantedQualitySources!.Select(item => item.Reference), StringComparer.Ordinal))
            return false;
        talent = matches[0];
        return true;
    }

    internal static bool TryProject(CharacterCreationTalentQualitySource source, string prerequisiteDigest,
        out CharacterCreationGrantedQuality grant)
    {
        grant = null!;
        if (!CharacterCreationTalentQualitySourceRules.IsValidSource(source)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(prerequisiteDigest)) return false;
        try
        {
            XElement node = XElement.Parse(source.CanonicalSourceXml);
            if (node.Elements().GroupBy(item => item.Name).Any(group => group.Count() != 1)
                || !int.TryParse(node.Element("karma")?.Value, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out int karma)
                || node.Element("category")?.Value is not ("Positive" or "Negative")
                || node.Element("category")!.Value == "Positive" && karma < 0
                || node.Element("category")!.Value == "Negative" && karma > 0
                || node.Elements("metagenic").Any() && node.Elements("metagenetic").Any()) return false;
            XElement? metagenicNode = node.Element("metagenic") ?? node.Element("metagenetic");
            bool metagenic = false;
            if (metagenicNode is not null && (metagenicNode.HasAttributes || metagenicNode.HasElements
                || !bool.TryParse(metagenicNode.Value, out metagenic))) return false;
            // Retain every source flag in the source packet. Quality.ContributeToBP,
            // ContributeToLimit and ContributeToMetagenicLimit all exclude Heritage.
            string selectionKey = source.SourceId + (source.ForcedSelection.Length == 0 ? string.Empty : ":" + source.ForcedSelection);
            string grantId = "priority-talent:" + CharacterCreationMagicResonanceDigest.Compute(new
            { prerequisiteDigest, source.SourceNodeDigest, selectionKey })[7..];
            grant = new(grantId, Guid.Parse(source.SourceId), selectionKey, source.Name,
                node.Element("category")!.Value == "Positive" ? CharacterCreationQualityType.Positive : CharacterCreationQualityType.Negative,
                1, karma, metagenic, false, false, "Heritage", source.SourceAnchorIds, string.Empty);
            grant = grant with { GrantDigest = CharacterCreationQualitiesRules.ComputeGrantDigest(grant) };
            return true;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }
}
