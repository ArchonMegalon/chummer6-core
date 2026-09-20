using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Read-only contacts contribution to an admitted Karma foundation. Source
/// resolution, owner/revision admission and persistence remain service duties.
/// Reuses the creation contact evaluator and quality-cap arithmetic; it never
/// interprets an overdrawn free-point allowance as a second Karma budget.
/// </summary>
public static class CharacterCreationKarmaContactsRules
{
    public const int MaximumSelections = 64;
    public const string ContactLimitExceeded = "creation-karma-contact-limit-exceeded";

    public static string PolicyDigest(CharacterCreationKarmaContactsPolicy policy)
        => Hash(policy with { AuthorityDigest = string.Empty });

    public static bool IsValidPolicy(CharacterCreationKarmaContactsPolicy? policy)
        => policy is { Schema: CharacterCreationKarmaContactsPolicy.SchemaV1,
            ContactPointsExpression.Length: > 0 and <= 2048, GroupContactKarmaMultiplier: >= 0,
            SourceAnchorIds.Count: > 0 }
            && !string.IsNullOrWhiteSpace(policy.SettingsProfileId)
            && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(policy.RawProfileInputsDigest)
            && policy.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
            && policy.AuthorityDigest == PolicyDigest(policy);

    public static bool TryFreeze(IReadOnlyList<CharacterCreationKarmaContactSelection>? selections,
        out CharacterCreationKarmaContactSelection[] frozen)
    {
        frozen = [];
        if (selections is null || selections.Count > MaximumSelections) return false;
        var copy = selections.Take(MaximumSelections + 1).ToArray();
        if (copy.Length > MaximumSelections || copy.Any(item => item is null || item.ContactId == Guid.Empty
            || item.Identity is null || item.Connection is < 1 or > 12 || item.Loyalty is < 1 or > 6
            || item.IsGroup && item.Loyalty != 1)
            || copy.Select(item => item.ContactId).Distinct().Count() != copy.Length) return false;
        try
        {
            foreach (var item in copy)
                foreach (string value in IdentityValues(item.Identity))
                {
                    if (value is null || value.Length > 32_767 || value != value.Trim()
                        || value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
                        return false;
                    XmlConvert.VerifyXmlChars(value);
                }
        }
        catch (XmlException) { return false; }
        frozen = copy.OrderBy(item => item.ContactId).ToArray();
        return true;
    }

    public static bool TryContactPoints(CharacterCreationKarmaContactsPolicy policy,
        CharacterCreationKarmaAttributesQuote attributes, out int points)
    {
        points = 0;
        if (!IsValidPolicy(policy) || attributes is not { CanSelect: true, Attributes: not null, Policy: not null }
            || attributes.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest
            || attributes.Policy.SettingsProfileId != policy.SettingsProfileId
            || attributes.QuoteDigest != Hash(attributes with { QuoteDigest = string.Empty })) return false;
        string expression = policy.ContactPointsExpression;
        foreach (var attribute in attributes.Attributes)
        {
            if (attribute is null) return false;
            string value = attribute.Current.ToString(CultureInfo.InvariantCulture);
            expression = expression.Replace("{" + attribute.AttributeId + "Unaug}", value, StringComparison.Ordinal)
                .Replace("{" + attribute.AttributeId + "}", value, StringComparison.Ordinal);
        }
        expression = expression.Replace(" div ", " / ", StringComparison.Ordinal);
        int depth = 0;
        foreach (char character in expression)
        {
            if (character == '(' && ++depth > 32 || character == ')' && --depth < 0) return false;
            if (!char.IsWhiteSpace(character) && character is not (>= '0' and <= '9')
                && character is not ('(' or ')' or '+' or '-' or '*' or '/' or '.')) return false;
        }
        if (depth != 0 || !CharacterGearQuantityRules.TryEvaluateCostExpression(expression, 0, out decimal total)
            || total is < 0 or > int.MaxValue) return false;
        points = (int)decimal.Ceiling(total);
        return true;
    }

    public static CharacterCreationKarmaContactsQuote? Evaluate(
        CharacterCreationKarmaContactsPolicy policy, CharacterCreationKarmaMetatypeQuote foundation,
        IReadOnlyList<CharacterCreationTalentQualitySource> racialSources,
        CharacterCreationTalentQualitySource? talentSource,
        IReadOnlyList<CharacterCreationKarmaContactSelection> selections)
    {
        try
        {
            if (foundation is not { Contacts: null, Binding: not null, Metatype: not null, Attributes: { } attributes,
                    Qualities: { Policy: not null, Costs: not null } qualities }
                || foundation.KarmaBudget is not { IsExact: true, Total: >= 0, Remaining: >= 0, Blockers.Count: 0 }
                || foundation.KarmaBudget.Used != (decimal)foundation.Metatype.KarmaCost + (foundation.Talent?.KarmaCost ?? 0)
                    + attributes.KarmaUsed + qualities.Costs.NetKarmaSpent
                    + (foundation.Skills?.KarmaUsed ?? 0) + (foundation.Resources?.KarmaInvestment ?? 0)
                || foundation.KarmaBudget.Remaining != foundation.KarmaBudget.Total - foundation.KarmaBudget.Used
                || !TryContactPoints(policy, attributes, out int points)
                || policy.RawProfileInputsDigest != foundation.Binding.SourceProfileDigest
                || qualities.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest
                || qualities.Policy.SettingsProfileId != policy.SettingsProfileId
                || !TryFreeze(selections, out var frozen)
                || !CharacterCreationKarmaGrantsLegacyProjector.TryProject(foundation, racialSources, talentSource,
                    out var grants, out _)) return null;

            var root = new XElement("character", new XElement("created", false), new XElement("contactpoints", points),
                new XElement("attributes", attributes.Attributes.Select(item => new XElement("attribute",
                    new XElement("name", item.AttributeId), new XElement("totalvalue", item.Current),
                    new XElement("metatypemin", item.Minimum), new XElement("base", 0),
                    new XElement("karma", item.KarmaLevels)))),
                new XElement(grants.Single(item => item.Name == "improvements")),
                new XElement("contacts", frozen.Select(BuildContactElement)));
            foreach (var option in qualities.Selections)
            {
                var selection = new CharacterCreationQualitySelection(option.OptionId, option.SourceId, option.SelectionKey,
                    option.Name, option.Type, option.Rating, option.KarmaCost, option.IsMetagenic, option.CountsAgainstQualityLimit,
                    option.CountsAgainstKarma, false, null, null, option.SourceAnchorIds, option.SourceNodeXml,
                    option.SourceNodeDigest, option.OptionDigest);
                if (!CharacterCreationLegacySourceProjector.TryBuildQualityGraph(selection, foundation.QuoteDigest,
                    out _, out var improvements)) return null;
                root.Element("improvements")!.Add(improvements);
            }
            var document = new WorkspaceDocument(root.ToString(SaveOptions.DisableFormatting), RulesetDefaults.Sr5);
            var authority = CharacterCreationContactsAuthorityEvaluator.Evaluate(document);
            if (authority.AuthorityBlockers.Count != 0 || !authority.ContactBudget.IsExact || !authority.HighPlacesBudget.IsExact)
                return null;
            var lines = new List<CharacterCreationKarmaContactLine>();
            bool friendsInHighPlaces = root.Element("improvements")!.Elements("improvement")
                .Any(item => item.Element("improvementttype")?.Value == "FriendsInHighPlaces");
            foreach (var selection in frozen)
            {
                // The legacy display resolver clamps imported values. A typed
                // creation choice must instead be rejected if it would change.
                if (!CharacterContactEditSemanticsResolver.TryResolve(root, BuildContactElement(selection), out var semantics)
                    || semantics.Connection != selection.Connection || semantics.Loyalty != selection.Loyalty
                    || semantics.IsGroup != selection.IsGroup || semantics.Free != selection.Free) return null;
                var cost = authority.Contacts.Single(item => item.ContactId == selection.ContactId);
                lines.Add(new(selection, cost.ContactPointCost, cost.CountsAgainstHighPlacesBudget));
            }
            int groupKarma = checked(lines.Where(line => line.Selection.IsGroup && !line.Selection.Free)
                .Sum(line => line.PointCost) * policy.GroupContactKarmaMultiplier);
            if (!CharacterCreationQualityCostRules.TryCalculate(qualities.Policy.Costs, qualities.Policy.QualityKarmaLimit,
                qualities.Selections.Select(item => new CharacterCreationQualityCostItem(item.KarmaCost,
                    item.CountsAgainstQualityLimit, item.CountsAgainstKarma, item.IsMetagenic)).ToArray(), groupKarma,
                out var combined)) return null;
            var blockers = new List<string>();
            if (combined.PositiveLimitKarma > qualities.Policy.QualityKarmaLimit && !qualities.Policy.MayExceedPositiveLimit)
                blockers.Add(CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
            if (lines.Any(line => (!friendsInHighPlaces || line.Selection.Connection < 8) && line.PointCost > 7)
                || friendsInHighPlaces && lines.Where(line => line.Selection.Connection >= 8 && line.PointCost > 7).Sum(line => line.PointCost)
                    > authority.HighPlacesBudget.Total)
                blockers.Add(ContactLimitExceeded);
            int karma = checked(authority.ContactBudget.Overspend + authority.HighPlacesBudget.Overspend
                + combined.NetKarmaSpent - qualities.Costs.NetKarmaSpent);
            if (karma < 0) return null;
            if (karma > foundation.KarmaBudget.Remaining)
                blockers.Add(CharacterCreationKarmaMetatypeBlockers.BudgetExceeded);
            var result = new CharacterCreationKarmaContactsQuote(CharacterCreationKarmaContactsQuote.SchemaV1,
                policy, attributes.QuoteDigest, qualities.QuoteDigest, racialSources.ToArray(), talentSource, lines.ToArray(), points,
                authority.ContactBudget.Used, authority.HighPlacesBudget.Total, authority.HighPlacesBudget.Used,
                groupKarma, combined, karma, blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), string.Empty);
            return result with { QuoteDigest = Hash(result) };
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    public static CharacterCreationKarmaMetatypeQuote WithoutContacts(CharacterCreationKarmaMetatypeQuote foundation)
    {
        if (foundation.Contacts is not { } contacts) return foundation;
        var result = foundation with
        {
            Contacts = null,
            KarmaBudget = foundation.KarmaBudget with
            {
                Used = foundation.KarmaBudget.Used - contacts.KarmaUsed,
                Remaining = foundation.KarmaBudget.Remaining + contacts.KarmaUsed
            },
            QuoteDigest = string.Empty
        };
        return result with { QuoteDigest = Hash(result) };
    }

    public static bool IsValid(CharacterCreationKarmaMetatypeQuote foundation,
        IReadOnlyList<CharacterCreationKarmaContactSelection>? selections)
    {
        if (foundation is not { Binding: not null, KarmaBudget: not null }) return false;
        if (foundation.Contacts is not { } contacts) return selections is null;
        return contacts is { Schema: CharacterCreationKarmaContactsQuote.SchemaV1, Policy: not null,
                Blockers.Count: 0, RacialSources: not null, Lines.Count: <= MaximumSelections }
            && contacts.Lines.All(line => line is not null && line.Selection is not null)
            && TryFreeze(selections, out var frozen)
            && foundation.Binding.ContactsPolicyDigest == contacts.Policy.AuthorityDigest
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(contacts,
                Evaluate(contacts.Policy, WithoutContacts(foundation), contacts.RacialSources, contacts.TalentSource, frozen));
    }

    internal static XElement BuildContactElement(CharacterCreationKarmaContactSelection selection)
    {
        string[] names = ["name", "role", "location", "notes", "extra", "metatype", "gender", "age", "contacttype",
            "preferredpayment", "hobbiesvice", "personallife", "groupname"];
        return new("contact", new XElement("guid", selection.ContactId.ToString("D")),
            names.Zip(IdentityValues(selection.Identity), (name, value) => new XElement(name, value)),
            new XElement("type", "Contact"), new XElement("connection", selection.Connection),
            new XElement("loyalty", selection.Loyalty), new XElement("group", selection.IsGroup),
            new XElement("free", selection.Free), new XElement("family", selection.Family),
            new XElement("blackmail", selection.Blackmail));
    }

    private static string[] IdentityValues(CharacterCreationContactIdentity identity) =>
        [identity.Name, identity.Role, identity.Location, identity.Notes, identity.CustomName, identity.Metatype,
            identity.Gender, identity.Age, identity.ContactType, identity.PreferredPayment, identity.HobbiesVice,
            identity.PersonalLife, identity.GroupName];

    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
}
