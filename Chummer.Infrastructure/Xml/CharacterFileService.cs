using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class CharacterFileService : ICharacterFileService
{
    public CharacterFileSummary ParseSummaryFromXml(string xml)
    {
        XDocument document = LoadCharacterDocument(xml);
        XElement character = document.Root!;
        string alias = ReadValue(character, "alias");

        return new CharacterFileSummary(
            Name: ReadPrimaryDisplayName(character, alias),
            Alias: alias,
            Metatype: ReadValue(character, "metatype"),
            BuildMethod: ReadValue(character, "buildmethod"),
            CreatedVersion: ReadValue(character, "createdversion"),
            AppVersion: ReadValue(character, "appversion"),
            Karma: ParseDecimalOrDefault(ReadValue(character, "karma"), "karma", 0m),
            Nuyen: ParseDecimalOrDefault(ReadValue(character, "nuyen"), "nuyen", 0m),
            Created: ParseBoolOrDefault(ReadValue(character, "created"), "created", false));
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
        ValidateRequiredNode(character, "name", issues, fallbackNodeName: "alias");
        if (!IsPendingTypedCreationSetup(character))
        {
            ValidateRequiredNode(character, "metatype", issues);
        }
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

    private static bool IsPendingTypedCreationSetup(XElement character)
    {
        if (character.Element("metatype") is not null
            || !bool.TryParse(ReadValue(character, "created"), out bool created)
            || created)
        {
            return false;
        }

        // Explicit SR6 drafts use their own method identities. In particular,
        // Point Buy/Life Path are not aliases for SR5 Karma/Life Modules. Do not
        // infer an edition for those new identities from a missing/duplicate tag.
        XElement[] editions = character.Elements("gameedition").ToArray();
        if (editions.Length == 1 && editions[0].Value == "SR6")
        {
            return character.Elements("buildmethod").Count() == 1
                   && character.Elements("created").Count() == 1
                   && Sr6CharacterCreationBuildMethods.IsKnown(ReadValue(character, "buildmethod"));
        }

        // Every supported SR5 Creation method chooses its metatype after bootstrap.
        // This is shape validation only; the bootstrap binding/source authority
        // remains responsible for admitting an incomplete Creation workspace.
        return CharacterCreationBuildMethods.IsSupported(ReadValue(character, "buildmethod"));
    }

    public string ApplyMetadataUpdate(string xml, CharacterMetadataUpdate update)
    {
        XDocument document = LoadCharacterDocument(xml);
        XElement character = document.Root!;

        UpdateNode(character, "name", update.Name);
        UpdateNode(character, "alias", update.Alias);
        UpdateNode(character, "notes", update.Notes);
        UpdateNode(character, "gamenotes", update.GameNotes);
        UpdateNode(character, "groupnotes", update.GroupNotes);

        using StringWriter writer = new(CultureInfo.InvariantCulture);
        // Audit commitments bind the original metadata text, including CR/LF.
        // The default TextWriter overload normalizes carriage returns during
        // serialization; entitize them so a subsequent XML read is lossless.
        using (XmlWriter xmlWriter = XmlWriter.Create(writer, new XmlWriterSettings
        {
            Indent = false,
            NewLineHandling = NewLineHandling.Entitize
        }))
        {
            document.Save(xmlWriter);
        }
        return writer.ToString();
    }

    private static void ValidateRequiredNode(
        XElement character,
        string nodeName,
        ICollection<CharacterValidationIssue> issues,
        string? fallbackNodeName = null)
    {
        string value = ReadValue(character, nodeName);
        if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(fallbackNodeName))
        {
            value = ReadValue(character, fallbackNodeName);
        }

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

    private static string ReadPrimaryDisplayName(XElement character, string alias)
    {
        string name = ReadValue(character, "name");
        return string.IsNullOrWhiteSpace(name) ? alias : name;
    }

    private static decimal ParseDecimal(string value, string nodeName)
    {
        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed))
            throw new FormatException($"Node '{nodeName}' must be a decimal number.");
        return parsed;
    }

    private static decimal ParseDecimalOrDefault(string value, string nodeName, decimal fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return ParseDecimal(value, nodeName);
    }

    private static bool ParseBool(string value, string nodeName)
    {
        if (!bool.TryParse(value, out bool parsed))
            throw new FormatException($"Node '{nodeName}' must be 'True' or 'False'.");
        return parsed;
    }

    private static bool ParseBoolOrDefault(string value, string nodeName, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return ParseBool(value, nodeName);
    }
}
