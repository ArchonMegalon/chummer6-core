using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

public sealed record CharacterContactEditSemantics(
    int Connection,
    int ConnectionMaximum,
    int Loyalty,
    bool IsGroup,
    bool Free,
    bool Family,
    bool Blackmail,
    bool IdentityEditable,
    bool ConnectionEditable,
    bool LoyaltyEditable,
    bool GroupEditable,
    bool FreeEditable,
    bool FamilyEditable,
    bool BlackmailEditable,
    bool CanDelete);

public static class CharacterContactEditSemanticsResolver
{
    public static bool TryResolve(
        XElement character,
        XElement contact,
        out CharacterContactEditSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(contact);

        string contactId = ReadValue(contact, "guid");
        bool careerMode = ParseBool(ReadValue(character, "created"));
        bool readOnly = contact.Element("readonly") is not null;
        bool linked = !string.IsNullOrWhiteSpace(ReadValue(contact, "file"))
            || !string.IsNullOrWhiteSpace(ReadValue(contact, "relative"));
        bool isGroup = ParseBool(ReadValue(contact, "group"));
        bool savedFree = ParseBool(ReadValue(contact, "free"));
        bool family = ParseBool(ReadValue(contact, "family"));
        bool blackmail = ParseBool(ReadValue(contact, "blackmail"));
        int savedConnection = ParseInt(ReadValue(contact, "connection"), fallback: 1);
        int savedLoyalty = ParseInt(ReadValue(contact, "loyalty"), fallback: 1);

        bool friendsInHighPlaces = HasApplicableImprovement(
            character,
            "FriendsInHighPlaces",
            improvedName: null,
            careerMode);
        bool forceGroup = HasApplicableImprovement(
            character,
            "ContactForceGroup",
            contactId,
            careerMode);
        bool freeFromImprovement = HasApplicableImprovement(
            character,
            "ContactMakeFree",
            contactId,
            careerMode);
        if (!TryReadForcedLoyalty(
                character,
                contactId,
                careerMode,
                out int forcedLoyalty))
        {
            semantics = null!;
            return false;
        }

        int connectionMaximum = careerMode || friendsInHighPlaces ? 12 : 6;
        int connection = Math.Clamp(savedConnection, 1, connectionMaximum);
        int loyalty = forcedLoyalty > 0
            ? forcedLoyalty
            : isGroup ? 1 : Math.Clamp(savedLoyalty, 1, 6);
        bool isEnemy = string.Equals(ReadValue(contact, "type"), "Enemy", StringComparison.OrdinalIgnoreCase);

        semantics = new CharacterContactEditSemantics(
            Connection: connection,
            ConnectionMaximum: connectionMaximum,
            Loyalty: loyalty,
            IsGroup: isGroup,
            Free: savedFree || freeFromImprovement,
            Family: family,
            Blackmail: blackmail,
            IdentityEditable: !linked,
            ConnectionEditable: !readOnly,
            LoyaltyEditable: !readOnly && !isGroup && forcedLoyalty <= 0,
            GroupEditable: !readOnly && !forceGroup,
            FreeEditable: !freeFromImprovement && !careerMode,
            FamilyEditable: !isEnemy,
            BlackmailEditable: !isEnemy,
            CanDelete: !readOnly);
        return true;
    }

    private static bool HasApplicableImprovement(
        XElement character,
        string improvementType,
        string? improvedName,
        bool careerMode)
        => (character.Element("improvements")?.Elements("improvement") ?? [])
            .Any(improvement =>
                string.Equals(ReadValue(improvement, "improvementttype"), improvementType, StringComparison.Ordinal)
                && (improvedName is null
                    || string.Equals(ReadValue(improvement, "improvedname"), improvedName, StringComparison.OrdinalIgnoreCase))
                && IsApplicable(improvement, careerMode));

    private static bool TryReadForcedLoyalty(
        XElement character,
        string contactId,
        bool careerMode,
        out int forcedLoyalty)
    {
        forcedLoyalty = 0;
        foreach (XElement improvement in character.Element("improvements")?.Elements("improvement") ?? [])
        {
            if (!string.Equals(
                    ReadValue(improvement, "improvementttype"),
                    "ContactForcedLoyalty",
                    StringComparison.Ordinal)
                || !string.Equals(
                    ReadValue(improvement, "improvedname"),
                    contactId,
                    StringComparison.OrdinalIgnoreCase)
                || !IsApplicable(improvement, careerMode))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(ReadValue(improvement, "unique"))
                || ParseBool(ReadValue(improvement, "custom"))
                || !decimal.TryParse(
                    ReadValue(improvement, "val"),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal value)
                || value is < int.MinValue or > int.MaxValue)
            {
                forcedLoyalty = 0;
                return false;
            }

            int rounded = decimal.ToInt32(decimal.Round(value, 0, MidpointRounding.AwayFromZero));
            if (rounded > 6)
            {
                forcedLoyalty = 0;
                return false;
            }
            forcedLoyalty = Math.Max(forcedLoyalty, rounded);
        }
        return true;
    }

    private static bool IsApplicable(XElement improvement, bool careerMode)
    {
        if (ParseInt(ReadValue(improvement, "enabled"), fallback: 1) <= 0)
        {
            return false;
        }

        string condition = ReadValue(improvement, "condition");
        return string.IsNullOrEmpty(condition)
            || string.Equals(condition, careerMode ? "career" : "create", StringComparison.Ordinal);
    }

    private static bool ParseBool(string value)
        => bool.TryParse(value, out bool parsed) && parsed
            || string.Equals(value, "1", StringComparison.Ordinal);

    private static int ParseInt(string value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;

    private static string ReadValue(XElement element, string name)
        => element.Element(name)?.Value.Trim() ?? string.Empty;
}
