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

/// <summary>Original effect facts, not caller-granted editing authority.</summary>
public sealed record CharacterContactRuleEffect(
    string ImprovementType,
    string ImprovedName,
    string Value,
    string Unique,
    bool Custom,
    int Enabled,
    string Condition);

public sealed record CharacterContactEditInput(
    string ContactId,
    bool CareerMode,
    bool ReadOnly,
    bool Linked,
    bool IsGroup,
    bool SavedFree,
    bool Family,
    bool Blackmail,
    int SavedConnection,
    int SavedLoyalty,
    bool IsEnemy);

public static class CharacterContactEditSemanticsResolver
{
    public static bool TryResolve(
        XElement character,
        XElement contact,
        out CharacterContactEditSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(contact);
        return TryResolve(ReadInput(character, contact), ReadEffects(character), out semantics);
    }

    public static CharacterContactEditInput ReadInput(XElement character, XElement contact) => new(
        ReadValue(contact, "guid"), ParseBool(ReadValue(character, "created")),
        contact.Element("readonly") is not null,
        !string.IsNullOrWhiteSpace(ReadValue(contact, "file"))
            || !string.IsNullOrWhiteSpace(ReadValue(contact, "relative")),
        ParseBool(ReadValue(contact, "group")), ParseBool(ReadValue(contact, "free")),
        ParseBool(ReadValue(contact, "family")), ParseBool(ReadValue(contact, "blackmail")),
        ParseInt(ReadValue(contact, "connection"), fallback: 1),
        ParseInt(ReadValue(contact, "loyalty"), fallback: 1),
        string.Equals(ReadValue(contact, "type"), "Enemy", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<CharacterContactRuleEffect> ReadEffects(XElement character) =>
        (character.Element("improvements")?.Elements("improvement") ?? []).Select(improvement =>
            new CharacterContactRuleEffect(ReadValue(improvement, "improvementttype"),
                ReadValue(improvement, "improvedname"), ReadValue(improvement, "val"),
                ReadValue(improvement, "unique"), ParseBool(ReadValue(improvement, "custom")),
                ParseInt(ReadValue(improvement, "enabled"), fallback: 1),
                ReadValue(improvement, "condition"))).ToArray();

    public static bool TryResolve(CharacterContactEditInput input,
        IReadOnlyList<CharacterContactRuleEffect> effects, out CharacterContactEditSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(effects);
        string contactId = input.ContactId;
        bool careerMode = input.CareerMode;
        bool readOnly = input.ReadOnly;
        bool linked = input.Linked;
        bool isGroup = input.IsGroup;
        bool savedFree = input.SavedFree;
        bool family = input.Family;
        bool blackmail = input.Blackmail;
        int savedConnection = input.SavedConnection;
        int savedLoyalty = input.SavedLoyalty;

        bool friendsInHighPlaces = HasApplicableImprovement(
            effects,
            "FriendsInHighPlaces",
            improvedName: null,
            careerMode);
        bool forceGroup = HasApplicableImprovement(
            effects,
            "ContactForceGroup",
            contactId,
            careerMode);
        bool freeFromImprovement = HasApplicableImprovement(
            effects,
            "ContactMakeFree",
            contactId,
            careerMode);
        if (!TryReadForcedLoyalty(
                effects,
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
        bool isEnemy = input.IsEnemy;

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
        IReadOnlyList<CharacterContactRuleEffect> effects,
        string improvementType,
        string? improvedName,
        bool careerMode)
        => effects
            .Any(improvement =>
                string.Equals(improvement.ImprovementType, improvementType, StringComparison.Ordinal)
                && (improvedName is null
                    || string.Equals(improvement.ImprovedName, improvedName, StringComparison.OrdinalIgnoreCase))
                && IsApplicable(improvement, careerMode));

    private static bool TryReadForcedLoyalty(
        IReadOnlyList<CharacterContactRuleEffect> effects,
        string contactId,
        bool careerMode,
        out int forcedLoyalty)
    {
        forcedLoyalty = 0;
        foreach (CharacterContactRuleEffect improvement in effects)
        {
            if (!string.Equals(
                    improvement.ImprovementType,
                    "ContactForcedLoyalty",
                    StringComparison.Ordinal)
                || !string.Equals(
                    improvement.ImprovedName,
                    contactId,
                    StringComparison.OrdinalIgnoreCase)
                || !IsApplicable(improvement, careerMode))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(improvement.Unique)
                || improvement.Custom
                || !decimal.TryParse(
                    improvement.Value,
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

    public static bool IsApplicable(CharacterContactRuleEffect improvement, bool careerMode)
    {
        if (improvement.Enabled <= 0)
        {
            return false;
        }

        string condition = improvement.Condition;
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
