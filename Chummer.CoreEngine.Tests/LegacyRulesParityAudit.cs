using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr4;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;
using System.Xml.Linq;

internal static class LegacyRulesParityAudit
{
    private static readonly StringComparer PathComparer = StringComparer.Ordinal;
    private static readonly string[] KnownSr5AttributeHardeningGapIds =
    [
        "BLUE.chum5|AGI|cyberlimb-aggregation",
        "BLUE.chum5|STR|cyberlimb-aggregation",
        "Barrett.chum5|AGI|cyberlimb-aggregation",
        "Barrett.chum5|STR|cyberlimb-aggregation",
        "Bastion.chum5|AGI|cyberlimb-aggregation",
        "Bastion.chum5|STR|cyberlimb-aggregation",
        "Blindfire.chum5|AGI|cyberlimb-aggregation",
        "Blindfire.chum5|STR|cyberlimb-aggregation",
        "Ghile Mear.chum5|AGI|cyberlimb-aggregation",
        "Ghile Mear.chum5|STR|cyberlimb-aggregation",
        "Monomax (approved) 3.chum5|AGI|cyberlimb-aggregation",
        "Monomax (approved) 3.chum5|STR|cyberlimb-aggregation",
        "SCSi.chum5|AGI|legacy-bonus-without-parsed-driver",
        "SCSi.chum5|STR|legacy-bonus-without-parsed-driver"
    ];

    private static readonly HashSet<string> Sr5AuditedAttributeNames =
    [
        "BOD",
        "AGI",
        "REA",
        "STR",
        "CHA",
        "INT",
        "LOG",
        "WIL",
        "EDG"
    ];

    public static void AssertLegacyRulesParity(string legacyFixtureDirectory, string sr4FixtureDirectory)
    {
        AssertChummer5AttributeParity(legacyFixtureDirectory);
        AssertChummer4SkillParity(sr4FixtureDirectory);
    }

    private static void AssertChummer5AttributeParity(string legacyFixtureDirectory)
    {
        List<string> actualGapIds = [];

        foreach (string filePath in Directory.EnumerateFiles(legacyFixtureDirectory, "*.chum5", SearchOption.TopDirectoryOnly).OrderBy(static path => Path.GetFileName(path), PathComparer))
        {
            string fileName = Path.GetFileName(filePath);
            string xml = File.ReadAllText(filePath);
            WorkspaceService workspaceService = CreateWorkspaceService();
            WorkspaceImportResult imported = workspaceService.Import(new WorkspaceImportDocument(
                xml,
                string.Empty,
                WorkspaceDocumentFormat.NativeXml));

            AssertEx.Equal(RulesetDefaults.Sr5, imported.RulesetId, $"{fileName} should import onto the SR5 ruleset lane before legacy rules parity audit.");

            CharacterAttributesSection? importedAttributes = workspaceService.GetSection(imported.Id, "attributes") as CharacterAttributesSection;
            AssertEx.NotNull(importedAttributes, $"{fileName} should expose imported attribute data for parity audit.");

            XElement root = XDocument.Parse(xml, LoadOptions.PreserveWhitespace).Root
                ?? throw new InvalidOperationException($"{fileName} must keep a <character> root.");
            Sr5AttributeBaselineMode baselineMode = DetermineSr5BaselineMode(root, importedAttributes!);

            foreach (CharacterAttributeSummary importedAttribute in importedAttributes!.Attributes.Where(static attribute => Sr5AuditedAttributeNames.Contains(attribute.Name)))
            {
                XElement? legacyAttribute = FindAttribute(root, importedAttribute.Name);
                AssertEx.NotNull(legacyAttribute, $"{fileName} should preserve legacy XML for attribute '{importedAttribute.Name}'.");

                AttributeParityResult parity = EvaluateSr5AttributeParity(root, legacyAttribute!, importedAttribute, baselineMode);
                if (parity.IsKnownHardeningGap)
                {
                    actualGapIds.Add($"{fileName}|{importedAttribute.Name}|{parity.GapReason}");
                    continue;
                }

                AssertEx.Equal(
                    parity.ExpectedTotal,
                    importedAttribute.TotalValue,
                    $"{fileName} imported {importedAttribute.Name} total drifted from the current SR5 legacy rules parity model ({baselineMode}).");
            }

            AssertEx.True(workspaceService.Close(imported.Id), $"{fileName} should close cleanly after SR5 legacy rules parity audit.");
        }

        string[] orderedGapIds = actualGapIds.OrderBy(static gapId => gapId, PathComparer).ToArray();
        AssertEx.SequenceEqual(
            KnownSr5AttributeHardeningGapIds,
            orderedGapIds,
            "SR5 legacy attribute parity hardening gaps drifted. Burn down resolved gaps or harden the rules model before accepting new ones.");
    }

    private static void AssertChummer4SkillParity(string sr4FixtureDirectory)
    {
        foreach (string filePath in Directory.EnumerateFiles(sr4FixtureDirectory, "*.chum4", SearchOption.TopDirectoryOnly).OrderBy(static path => Path.GetFileName(path), PathComparer))
        {
            string fileName = Path.GetFileName(filePath);
            string xml = File.ReadAllText(filePath);
            WorkspaceService workspaceService = CreateWorkspaceService();
            WorkspaceImportResult imported = workspaceService.Import(new WorkspaceImportDocument(
                xml,
                string.Empty,
                WorkspaceDocumentFormat.NativeXml));

            AssertEx.Equal(RulesetDefaults.Sr4, imported.RulesetId, $"{fileName} should import onto the SR4 ruleset lane before legacy rules parity audit.");

            CharacterSkillsSection? importedSkills = workspaceService.GetSkills(imported.Id);
            AssertEx.NotNull(importedSkills, $"{fileName} should expose imported SR4 skill data for parity audit.");

            XElement root = XDocument.Parse(xml, LoadOptions.PreserveWhitespace).Root
                ?? throw new InvalidOperationException($"{fileName} must keep a <character> root.");
            Dictionary<string, XElement> rawSkillsByName = root.Element("skills")?
                .Elements("skill")
                .Select(skill => new KeyValuePair<string, XElement>(ReadValue(skill, "name"), skill))
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, PathComparer)
                ?? new Dictionary<string, XElement>(PathComparer);

            foreach (CharacterSkillSummary importedSkill in importedSkills!.Skills)
            {
                if (!rawSkillsByName.TryGetValue(importedSkill.Suid, out XElement? rawSkill))
                {
                    throw new InvalidOperationException($"{fileName} should preserve raw SR4 skill '{importedSkill.Suid}' for parity audit.");
                }

                int expectedTotal = importedSkill.BaseValue + ComputeSkillImprovementDelta(root, importedSkill.Suid);
                int actualTotal = ParseInt(ReadValue(rawSkill, "totalvalue"));

                AssertEx.Equal(
                    expectedTotal,
                    actualTotal,
                    $"{fileName} imported SR4 skill '{importedSkill.Suid}' drifted from the legacy saved total.");
            }

            AssertEx.True(workspaceService.Close(imported.Id), $"{fileName} should close cleanly after SR4 legacy skill parity audit.");
        }
    }

    private static WorkspaceService CreateWorkspaceService()
    {
        ICharacterFileQueries fileQueries = new XmlCharacterFileQueries(new CharacterFileService());
        ICharacterSectionQueries sectionQueries = new XmlCharacterSectionQueries(new CharacterSectionService());
        ICharacterMetadataCommands metadataCommands = new XmlCharacterMetadataCommands(new CharacterFileService());
        return new WorkspaceService(
            new InMemoryWorkspaceStore(),
            new RulesetWorkspaceCodecResolver(
            [
                new Sr4WorkspaceCodec(fileQueries, sectionQueries, metadataCommands),
                new Sr5WorkspaceCodec(fileQueries, sectionQueries, metadataCommands),
                new Sr6WorkspaceCodec(fileQueries, sectionQueries, metadataCommands)
            ]),
            new WorkspaceImportRulesetDetector());
    }

    private static AttributeParityResult EvaluateSr5AttributeParity(
        XElement root,
        XElement legacyAttribute,
        CharacterAttributeSummary importedAttribute,
        Sr5AttributeBaselineMode baselineMode)
    {
        string attributeName = importedAttribute.Name;
        int baseline = ComputeSr5AttributeBaseline(legacyAttribute, baselineMode);
        int improvementDelta = ComputeAttributeImprovementDelta(root, attributeName);
        int expectedTotal = baseline + improvementDelta;

        if (expectedTotal == importedAttribute.TotalValue)
        {
            return new AttributeParityResult(expectedTotal, null);
        }

        if (HasCyberlimbAggregationGap(root, attributeName))
        {
            return new AttributeParityResult(expectedTotal, "cyberlimb-aggregation");
        }

        if (!HasAnyParsedAttributeDriver(root, attributeName))
        {
            return new AttributeParityResult(expectedTotal, "legacy-bonus-without-parsed-driver");
        }

        return new AttributeParityResult(expectedTotal, null);
    }

    private static Sr5AttributeBaselineMode DetermineSr5BaselineMode(XElement root, CharacterAttributesSection importedAttributes)
    {
        int absoluteMismatches = 0;
        int metatypeMismatches = 0;

        foreach (CharacterAttributeSummary importedAttribute in importedAttributes.Attributes.Where(static attribute => Sr5AuditedAttributeNames.Contains(attribute.Name)))
        {
            XElement? legacyAttribute = FindAttribute(root, importedAttribute.Name);
            if (legacyAttribute is null || HasCyberlimbAggregationGap(root, importedAttribute.Name))
            {
                continue;
            }

            int improvementDelta = ComputeAttributeImprovementDelta(root, importedAttribute.Name);
            int absoluteExpected = ComputeSr5AttributeBaseline(legacyAttribute, Sr5AttributeBaselineMode.AbsoluteBase) + improvementDelta;
            int metatypeExpected = ComputeSr5AttributeBaseline(legacyAttribute, Sr5AttributeBaselineMode.MetatypeOffset) + improvementDelta;

            if (absoluteExpected != importedAttribute.TotalValue)
            {
                absoluteMismatches++;
            }

            if (metatypeExpected != importedAttribute.TotalValue)
            {
                metatypeMismatches++;
            }
        }

        return absoluteMismatches < metatypeMismatches
            ? Sr5AttributeBaselineMode.AbsoluteBase
            : Sr5AttributeBaselineMode.MetatypeOffset;
    }

    private static int ComputeSr5AttributeBaseline(XElement legacyAttribute, Sr5AttributeBaselineMode baselineMode)
    {
        int baseValue = ParseInt(ReadValue(legacyAttribute, "base"));
        int karmaValue = ParseInt(ReadValue(legacyAttribute, "karma"));
        return baselineMode switch
        {
            Sr5AttributeBaselineMode.AbsoluteBase => baseValue + karmaValue,
            _ => ParseInt(ReadValue(legacyAttribute, "metatypemin")) + baseValue + karmaValue
        };
    }

    private static int ComputeAttributeImprovementDelta(XElement root, string attributeName)
    {
        Dictionary<string, int> uniqueContributions = new(PathComparer);
        int total = 0;

        foreach (XElement improvement in EnumerateAttributeImprovements(root, attributeName))
        {
            int contribution = ComputeImprovementContribution(improvement);
            string unique = ReadValue(improvement, "unique");
            if (string.IsNullOrWhiteSpace(unique))
            {
                total += contribution;
                continue;
            }

            if (!uniqueContributions.TryGetValue(unique, out int existing) || Math.Abs(contribution) > Math.Abs(existing))
            {
                uniqueContributions[unique] = contribution;
            }
        }

        return total + uniqueContributions.Values.Sum();
    }

    private static int ComputeSkillImprovementDelta(XElement root, string skillName)
    {
        Dictionary<string, int> uniqueContributions = new(PathComparer);
        int total = 0;

        foreach (XElement improvement in root.Element("improvements")?.Elements("improvement") ?? [])
        {
            if (!ParseBool(ReadValue(improvement, "enabled")))
            {
                continue;
            }

            string improvementType = ReadValue(improvement, "improvementttype");
            if (!PathComparer.Equals(improvementType, "Skill") && !PathComparer.Equals(improvementType, "SkillBase"))
            {
                continue;
            }

            if (!PathComparer.Equals(ReadValue(improvement, "improvedname"), skillName))
            {
                continue;
            }

            int contribution = ComputeImprovementContribution(improvement);
            string unique = ReadValue(improvement, "unique");
            if (string.IsNullOrWhiteSpace(unique))
            {
                total += contribution;
                continue;
            }

            if (!uniqueContributions.TryGetValue(unique, out int existing) || Math.Abs(contribution) > Math.Abs(existing))
            {
                uniqueContributions[unique] = contribution;
            }
        }

        return total + uniqueContributions.Values.Sum();
    }

    private static IEnumerable<XElement> EnumerateAttributeImprovements(XElement root, string attributeName)
    {
        foreach (XElement improvement in root.Element("improvements")?.Elements("improvement") ?? [])
        {
            if (!ParseBool(ReadValue(improvement, "enabled")))
            {
                continue;
            }

            string improvementType = ReadValue(improvement, "improvementttype");
            if (!PathComparer.Equals(improvementType, "Attribute") && !PathComparer.Equals(improvementType, "Attributelevel"))
            {
                continue;
            }

            if (PathComparer.Equals(ReadValue(improvement, "improvedname"), attributeName))
            {
                yield return improvement;
            }
        }
    }

    private static bool HasAnyParsedAttributeDriver(XElement root, string attributeName)
        => EnumerateAttributeImprovements(root, attributeName).Any();

    private static bool HasCyberlimbAggregationGap(XElement root, string attributeName)
    {
        if (!PathComparer.Equals(attributeName, "AGI") && !PathComparer.Equals(attributeName, "STR"))
        {
            return false;
        }

        foreach (XElement cyberware in root.Element("cyberwares")?.Elements("cyberware") ?? [])
        {
            string cyberwareName = ReadValue(cyberware, "name");
            bool isArmOrLegLimb = PathComparer.Equals(ReadValue(cyberware, "category"), "Cyberlimb")
                && (PathComparer.Equals(ReadValue(cyberware, "limbslot"), "arm")
                    || PathComparer.Equals(ReadValue(cyberware, "limbslot"), "leg"));
            if (!isArmOrLegLimb)
            {
                continue;
            }

            if (PathComparer.Equals(attributeName, "STR")
                && (cyberwareName.Contains("Full Arm", StringComparison.Ordinal)
                    || cyberwareName.Contains("Full Leg", StringComparison.Ordinal)))
            {
                return true;
            }

            if (cyberware.Element("children")?.Elements("cyberware").Any(child => PathComparer.Equals(ReadValue(child, "name"), "Customized Agility")) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static int ComputeImprovementContribution(XElement improvement)
    {
        int value = ParseInt(ReadValue(improvement, "val"));
        int augmentation = ParseInt(ReadValue(improvement, "aug"));
        if (value != 0 || augmentation != 0)
        {
            if (value < 0 && augmentation >= 0)
            {
                return value;
            }

            if (augmentation < 0 && value >= 0)
            {
                return augmentation;
            }

            if (value == 0)
            {
                return augmentation;
            }

            if (augmentation == 0)
            {
                return value;
            }

            return Math.Abs(value) >= Math.Abs(augmentation) ? value : augmentation;
        }

        int minimum = ParseInt(ReadValue(improvement, "min"));
        int maximum = ParseInt(ReadValue(improvement, "max"));
        return minimum == maximum && minimum > 0
            ? minimum
            : 0;
    }

    private static XElement? FindAttribute(XElement root, string attributeName)
    {
        return root.Element("attributes")?
            .Elements("attribute")
            .FirstOrDefault(attribute => PathComparer.Equals(ReadValue(attribute, "name"), attributeName));
    }

    private static string ReadValue(XElement parent, string nodeName)
        => parent.Element(nodeName)?.Value.Trim() ?? string.Empty;

    private static int ParseInt(string value)
        => int.TryParse(value, out int parsed) ? parsed : 0;

    private static bool ParseBool(string value)
        => bool.TryParse(value, out bool parsed) && parsed;

    private readonly record struct AttributeParityResult(int ExpectedTotal, string? GapReason)
    {
        public bool IsKnownHardeningGap => !string.IsNullOrWhiteSpace(GapReason);
    }

    private enum Sr5AttributeBaselineMode
    {
        AbsoluteBase,
        MetatypeOffset
    }
}
