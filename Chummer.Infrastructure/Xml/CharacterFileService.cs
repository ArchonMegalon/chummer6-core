using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class CharacterFileService : ICharacterFileService
{
    public CharacterFileSummary ParseSummaryFromXml(string xml)
    {
        XDocument document = LoadCharacterDocument(xml);
        XElement character = document.Root!;

        return new CharacterFileSummary(
            Name: ReadValue(character, "name"),
            Alias: ReadValue(character, "alias"),
            Metatype: ReadValue(character, "metatype"),
            BuildMethod: ReadValue(character, "buildmethod"),
            CreatedVersion: ReadValue(character, "createdversion"),
            AppVersion: ReadValue(character, "appversion"),
            Karma: ParseDecimal(ReadValue(character, "karma"), "karma"),
            Nuyen: ParseDecimal(ReadValue(character, "nuyen"), "nuyen"),
            Created: ParseBool(ReadValue(character, "created"), "created"));
    }

    public CharacterValidationResult ValidateXml(string xml)
    {
        List<CharacterValidationIssue> issues = new();
        XDocument? document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            issues.Add(new CharacterValidationIssue(
                Severity: "Error",
                Code: "InvalidXml",
                Message: ex.Message,
                Path: "/"));
            return new CharacterValidationResult(false, issues);
        }

        if (document.Root == null || !string.Equals(document.Root.Name.LocalName, "character", StringComparison.Ordinal))
        {
            issues.Add(new CharacterValidationIssue(
                Severity: "Error",
                Code: "InvalidRoot",
                Message: "Root node must be <character>.",
                Path: "/"));
            return new CharacterValidationResult(false, issues);
        }

        XElement character = document.Root;
        ValidateRequiredNode(character, "name", issues);
        ValidateRequiredNode(character, "metatype", issues);
        ValidateRequiredNode(character, "buildmethod", issues);
        ValidateRequiredNode(character, "createdversion", issues);
        ValidateRequiredNode(character, "appversion", issues);
        ValidateDecimalNode(character, "karma", issues);
        ValidateDecimalNode(character, "nuyen", issues);
        ValidateBoolNode(character, "created", issues);

        return new CharacterValidationResult(
            IsValid: issues.All(x => !string.Equals(x.Severity, "Error", StringComparison.Ordinal)),
            Issues: issues);
    }

    public string ApplyMetadataUpdate(string xml, CharacterMetadataUpdate update)
    {
        XDocument document = LoadCharacterDocument(xml);
        XElement character = document.Root!;

        UpdateNode(character, "name", update.Name);
        UpdateNode(character, "alias", update.Alias);
        UpdateNode(character, "notes", update.Notes);

        using StringWriter writer = new(CultureInfo.InvariantCulture);
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }

    private static void ValidateRequiredNode(
        XElement character,
        string nodeName,
        ICollection<CharacterValidationIssue> issues)
    {
        string value = ReadValue(character, nodeName);
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new CharacterValidationIssue(
                Severity: "Error",
                Code: "MissingRequiredNode",
                Message: $"Required node '{nodeName}' is missing or empty.",
                Path: $"/character/{nodeName}"));
        }
    }

    private static void ValidateDecimalNode(
        XElement character,
        string nodeName,
        ICollection<CharacterValidationIssue> issues)
    {
        string value = ReadValue(character, nodeName);
        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            issues.Add(new CharacterValidationIssue(
                Severity: "Error",
                Code: "InvalidDecimal",
                Message: $"Node '{nodeName}' must be a decimal number.",
                Path: $"/character/{nodeName}"));
        }
    }

    private static void ValidateBoolNode(
        XElement character,
        string nodeName,
        ICollection<CharacterValidationIssue> issues)
    {
        string value = ReadValue(character, nodeName);
        if (!bool.TryParse(value, out _))
        {
            issues.Add(new CharacterValidationIssue(
                Severity: "Error",
                Code: "InvalidBoolean",
                Message: $"Node '{nodeName}' must be 'True' or 'False'.",
                Path: $"/character/{nodeName}"));
        }
    }

    private static void UpdateNode(XElement character, string nodeName, string? value)
    {
        if (value == null)
            return;

        XElement? node = character.Element(nodeName);
        if (node == null)
        {
            node = new XElement(nodeName);
            character.Add(node);
        }

        node.Value = value;
    }

    private static XDocument LoadCharacterDocument(string xml)
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        if (document.Root == null || !string.Equals(document.Root.Name.LocalName, "character", StringComparison.Ordinal))
            throw new InvalidOperationException("Root node must be <character>.");
        return document;
    }

    private static string ReadValue(XElement character, string nodeName)
    {
        return (character.Element(nodeName)?.Value ?? string.Empty).Trim();
    }

    private static decimal ParseDecimal(string value, string nodeName)
    {
        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed))
            throw new FormatException($"Node '{nodeName}' must be a decimal number.");
        return parsed;
    }

    private static bool ParseBool(string value, string nodeName)
    {
        if (!bool.TryParse(value, out bool parsed))
            throw new FormatException($"Node '{nodeName}' must be 'True' or 'False'.");
        return parsed;
    }
}
