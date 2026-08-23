using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Deterministic, read-only SR5 authority used on both sides of the mutation
/// boundary. Receipt fields are projections of this evaluator, never caller facts.
/// </summary>
public static class CharacterCreationContactsAuthorityEvaluator
{
    private const int MinimumConnection = 1;
    private const int MinimumLoyalty = 1;
    private const int MaximumLoyalty = 6;

    private static readonly string s_RulesDigest =
        CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            CharacterCreationContactsSchemas.RulesV1,
            StepId = CharacterCreationWizardStepIds.ContactsLifestyles,
            ConnectionMinimum = MinimumConnection,
            CreationConnectionMaximum = 6,
            LoyaltyMinimum = MinimumLoyalty,
            LoyaltyMaximum = MaximumLoyalty,
            Cost = "free?0:round-away(max(connection+loyalty+(family?1:0)+(blackmail?2:0)+discount,2+minimum))",
            Budget = "contacts excluding groups; FIH connection>=8 uses CHA*4 pool",
            Career = "rejected",
            SourceAnchors = CharacterCreationContactSourceAnchors.All
        });

    private static readonly string s_RuntimeDigest =
        CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            CharacterCreationContactsSchemas.RuntimeV1,
            Service = typeof(CharacterCreationContactsService).FullName,
            Semantics = typeof(CharacterContactEditSemanticsResolver).FullName,
            Assembly = typeof(CharacterCreationContactsService).Assembly.GetName().Name,
            Version = typeof(CharacterCreationContactsService).Assembly.GetName().Version?.ToString() ?? string.Empty
        });

    public static CharacterCreationContactsAuthoritySnapshot Evaluate(WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var blockers = new List<string>();
        if (!string.Equals(
                RulesetDefaults.NormalizeOptional(document.RulesetId),
                RulesetDefaults.Sr5,
                StringComparison.Ordinal))
        {
            blockers.Add(CharacterCreationContactsBlockers.RulesetSr5Required);
        }

        XDocument parsed;
        XElement root;
        try
        {
            parsed = XDocument.Parse(document.Content, LoadOptions.PreserveWhitespace);
            root = parsed.Root ?? throw new XmlException();
            if (!string.Equals(root.Name.LocalName, "character", StringComparison.Ordinal))
                throw new XmlException();
        }
        catch (XmlException)
        {
            parsed = new XDocument(new XElement("character"));
            root = parsed.Root!;
            blockers.Add(CharacterCreationContactsBlockers.CharacterDocumentInvalid);
        }

        bool created = ParseBool(ReadValue(root, "created"));
        if (created)
            blockers.Add(CharacterCreationContactsBlockers.CareerModeRejected);

        bool friendsInHighPlaces = HasApplicableImprovement(
            root,
            "FriendsInHighPlaces",
            improvedName: null,
            careerMode: false);
        var contacts = new List<CharacterCreationContactAuthorityCost>();
        var identities = new HashSet<Guid>();
        int editableContactCount = 0;
        foreach (XElement contact in root.Element("contacts")?.Elements("contact") ?? [])
        {
            string type = ReadValue(contact, "type");
            if (type.Length != 0
                && !string.Equals(type, "Contact", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            editableContactCount++;
            if (!Guid.TryParseExact(ReadValue(contact, "guid"), "D", out Guid id)
                || id == Guid.Empty)
            {
                blockers.Add(CharacterCreationContactsBlockers.ContactInvalid);
                continue;
            }
            if (!identities.Add(id))
            {
                blockers.Add(CharacterCreationContactsBlockers.ContactAmbiguous);
                continue;
            }
            if (!CharacterContactEditSemanticsResolver.TryResolve(
                    root,
                    contact,
                    out CharacterContactEditSemantics semantics)
                || !TryComputeContactCost(root, semantics, out int cost))
            {
                blockers.Add(CharacterCreationContactsBlockers.AuthorityUnavailable);
                continue;
            }

            bool highPlaces = !semantics.IsGroup
                              && !semantics.Free
                              && semantics.Connection >= 8
                              && friendsInHighPlaces;
            contacts.Add(new CharacterCreationContactAuthorityCost(
                id,
                cost,
                CountsAgainstContactBudget: !semantics.IsGroup && !semantics.Free && !highPlaces,
                CountsAgainstHighPlacesBudget: highPlaces));
        }
        if (contacts.Count != editableContactCount)
            blockers.Add(CharacterCreationContactsBlockers.ContactInvalid);

        bool contactTotalExact = TryParseNonNegativeInt(ReadValue(root, "contactpoints"), out int contactTotal);
        if (!contactTotalExact)
            blockers.Add(CharacterCreationContactsBlockers.BudgetAuthorityRequired);
        bool contactUsedExact = TrySumContactCosts(
            contacts,
            contact => contact.CountsAgainstContactBudget,
            out int contactUsed);
        if (!contactUsedExact)
            blockers.Add(CharacterCreationContactsBlockers.AuthorityUnavailable);

        int highPlacesTotal = 0;
        bool highPlacesExact = true;
        int charisma = 0;
        if (friendsInHighPlaces && !TryReadCharismaValue(root, out charisma))
        {
            highPlacesExact = false;
            blockers.Add(CharacterCreationContactsBlockers.FriendsInHighPlacesAuthorityRequired);
        }
        else if (friendsInHighPlaces)
        {
            try
            {
                highPlacesTotal = checked(charisma * 4);
            }
            catch (OverflowException)
            {
                highPlacesExact = false;
                blockers.Add(CharacterCreationContactsBlockers.FriendsInHighPlacesAuthorityRequired);
            }
        }
        bool highPlacesUsedExact = TrySumContactCosts(
            contacts,
            contact => contact.CountsAgainstHighPlacesBudget,
            out int highPlacesUsed);
        if (!highPlacesUsedExact)
            blockers.Add(CharacterCreationContactsBlockers.AuthorityUnavailable);

        return new CharacterCreationContactsAuthoritySnapshot(
            created,
            ComputeSourceDigest(root),
            s_RulesDigest,
            s_RuntimeDigest,
            contacts.OrderBy(contact => contact.ContactId).ToArray(),
            BuildBudget(
                CharacterCreationContactBudgetIds.Contacts,
                contactTotal,
                contactUsed,
                contactTotalExact && contactUsedExact,
                CharacterCreationContactsBlockers.BudgetExceeded),
            BuildBudget(
                CharacterCreationContactBudgetIds.FriendsInHighPlaces,
                highPlacesTotal,
                highPlacesUsed,
                highPlacesExact && highPlacesUsedExact,
                CharacterCreationContactsBlockers.HighPlacesBudgetExceeded),
            blockers.Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    private static bool TryComputeContactCost(
        XElement root,
        CharacterContactEditSemantics semantics,
        out int cost)
    {
        cost = 0;
        if (semantics.Free)
            return true;
        if (!TrySumApplicableImprovement(root, "ContactKarmaDiscount", out decimal discount)
            || !TrySumApplicableImprovement(root, "ContactKarmaMinimum", out decimal minimum))
        {
            return false;
        }
        try
        {
            decimal raw = checked((decimal)semantics.Connection + semantics.Loyalty
                                  + (semantics.Family ? 1 : 0)
                                  + (semantics.Blackmail ? 2 : 0)
                                  + discount);
            decimal floor = checked(2m + minimum);
            decimal rounded = decimal.Round(Math.Max(raw, floor), 0, MidpointRounding.AwayFromZero);
            if (rounded is < 0 or > int.MaxValue)
                return false;
            cost = decimal.ToInt32(rounded);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TrySumApplicableImprovement(XElement root, string type, out decimal total)
    {
        total = 0;
        foreach (XElement improvement in root.Element("improvements")?.Elements("improvement") ?? [])
        {
            if (!string.Equals(ReadValue(improvement, "improvementttype"), type, StringComparison.Ordinal)
                || !IsApplicable(improvement, careerMode: false))
            {
                continue;
            }
            if (!decimal.TryParse(
                    ReadValue(improvement, "val"),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal value))
            {
                return false;
            }
            try
            {
                total = checked(total + value);
            }
            catch (OverflowException)
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasApplicableImprovement(
        XElement root,
        string type,
        string? improvedName,
        bool careerMode) =>
        (root.Element("improvements")?.Elements("improvement") ?? []).Any(improvement =>
            string.Equals(ReadValue(improvement, "improvementttype"), type, StringComparison.Ordinal)
            && (improvedName is null
                || string.Equals(ReadValue(improvement, "improvedname"), improvedName, StringComparison.OrdinalIgnoreCase))
            && IsApplicable(improvement, careerMode));

    private static bool IsApplicable(XElement improvement, bool careerMode)
    {
        if (!int.TryParse(
                ReadValue(improvement, "enabled"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int enabled))
        {
            enabled = 1;
        }
        if (enabled <= 0)
            return false;
        string condition = ReadValue(improvement, "condition");
        return condition.Length == 0
               || string.Equals(condition, careerMode ? "career" : "create", StringComparison.Ordinal);
    }

    private static bool TryReadCharismaValue(XElement root, out int value)
    {
        value = 0;
        XElement[] candidates = (root.Element("attributes")?.Elements("attribute") ?? [])
            .Where(attribute => string.Equals(ReadValue(attribute, "name"), "CHA", StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
            return false;
        XElement charisma = candidates[0];
        if (TryParseNonNegativeInt(ReadValue(charisma, "totalvalue"), out value)
            || TryParseNonNegativeInt(ReadValue(charisma, "value"), out value))
        {
            return true;
        }
        if (!TryParseNonNegativeInt(ReadValue(charisma, "base"), out int basis)
            || !TryParseNonNegativeInt(ReadValue(charisma, "karma"), out int karma)
            || !TryParseNonNegativeInt(ReadValue(charisma, "metatypemin"), out int minimum))
        {
            return false;
        }
        try
        {
            value = checked(basis + karma + minimum);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TrySumContactCosts(
        IEnumerable<CharacterCreationContactAuthorityCost> contacts,
        Func<CharacterCreationContactAuthorityCost, bool> applies,
        out int total)
    {
        total = 0;
        try
        {
            foreach (CharacterCreationContactAuthorityCost contact in contacts)
            {
                if (applies(contact))
                    total = checked(total + contact.ContactPointCost);
            }
            return true;
        }
        catch (OverflowException)
        {
            total = 0;
            return false;
        }
    }

    private static CharacterCreationContactBudget BuildBudget(
        string id,
        int total,
        int used,
        bool exact,
        string exceededBlocker)
    {
        int normalizedTotal = Math.Max(0, total);
        int normalizedUsed = Math.Max(0, used);
        int remaining = Math.Max(0, normalizedTotal - normalizedUsed);
        int overspend = Math.Max(0, normalizedUsed - normalizedTotal);
        return new CharacterCreationContactBudget(
            id,
            normalizedTotal,
            normalizedUsed,
            remaining,
            overspend,
            exact,
            !exact
                ? [CharacterCreationContactsBlockers.BudgetAuthorityRequired]
                : overspend > 0 ? [exceededBlocker] : [],
            CharacterCreationContactSourceAnchors.All);
    }

    private static string ComputeSourceDigest(XElement root)
    {
        string[] improvements = (root.Element("improvements")?.Elements("improvement") ?? [])
            .Where(improvement => ReadValue(improvement, "improvementttype") is
                "FriendsInHighPlaces" or "ContactForceGroup" or "ContactMakeFree"
                or "ContactForcedLoyalty" or "ContactKarmaDiscount" or "ContactKarmaMinimum")
            .Select(improvement => improvement.ToString(SaveOptions.DisableFormatting))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            Schema = "chummer.character_creation_contacts.source.v1",
            Settings = ReadValue(root, "settings"),
            BuildMethod = ReadValue(root, "buildmethod"),
            ContactPoints = ReadValue(root, "contactpoints"),
            Charisma = (root.Element("attributes")?.Elements("attribute") ?? [])
                .FirstOrDefault(attribute => string.Equals(ReadValue(attribute, "name"), "CHA", StringComparison.Ordinal))
                ?.ToString(SaveOptions.DisableFormatting) ?? string.Empty,
            Improvements = improvements,
            SourceAnchors = CharacterCreationContactSourceAnchors.All
        });
    }

    private static bool TryParseNonNegativeInt(string value, out int parsed)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
           && parsed >= 0;

    private static bool ParseBool(string value)
        => bool.TryParse(value, out bool parsed) && parsed
           || string.Equals(value, "1", StringComparison.Ordinal);

    private static string ReadValue(XElement parent, string name)
        => parent.Element(name)?.Value.Trim() ?? string.Empty;
}

public sealed record CharacterCreationContactAuthorityCost(
    Guid ContactId,
    int ContactPointCost,
    bool CountsAgainstContactBudget,
    bool CountsAgainstHighPlacesBudget);

public sealed record CharacterCreationContactsAuthoritySnapshot(
    bool CharacterCreated,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest,
    IReadOnlyList<CharacterCreationContactAuthorityCost> Contacts,
    CharacterCreationContactBudget ContactBudget,
    CharacterCreationContactBudget HighPlacesBudget,
    IReadOnlyList<string> AuthorityBlockers);
