using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using System.Text.Json;

internal static class HeroLabRulesParityAudit
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    public static void AssertHeroLabImportsAndParity(string heroLabFixtureRoot)
    {
        AssertOnlineAliasAndMetadataDriftHandling();

        AssertClassicFixtures(
            Path.Combine(heroLabFixtureRoot, "Sr5"),
            expectedFixtureNames:
            [
                "Cascade Orc Prirates.por",
                "Glitched Character 1.por",
                "Two Banshees.por"
            ]);
        AssertClassicFixtures(
            Path.Combine(heroLabFixtureRoot, "Sr4"),
            expectedFixtureNames:
            [
                "sr4-street-samurai.por"
            ]);
        AssertOnlineFixtures(
            Path.Combine(heroLabFixtureRoot, "Sr6"),
            expectedFixtureNames:
            [
                "sr6-starter.hlo.json"
            ]);
    }

    private static void AssertClassicFixtures(string directory, string[] expectedFixtureNames)
    {
        AssertFixtureNames(directory, "*.por", expectedFixtureNames);

        foreach (string fileName in expectedFixtureNames)
        {
            string filePath = Path.Combine(directory, fileName);
            HeroLabImportSnapshot snapshot = HeroLabShadowrunImporter.ImportClassicPortfolio(File.ReadAllBytes(filePath), fileName);
            HeroLabParityContract parity = LoadParityContract(filePath);

            AssertSnapshotCoverage(snapshot, parity, fileName);
            AssertAttributeParity(snapshot, parity, fileName);
            AssertSkillParity(snapshot, parity, fileName);
        }
    }

    private static void AssertOnlineAliasAndMetadataDriftHandling()
    {
        const string metadataAliasJson = """
{
  "metadata": {
    "game_code": "SR6",
    "game_name": "Shadowrun Sixth World"
  }
}
""";

        string? detectedRulesetId = HeroLabShadowrunImporter.DetectRulesetFromOnlineJson(metadataAliasJson);
        AssertEx.Equal(RulesetDefaults.Sr6, detectedRulesetId, "Hero Lab online ruleset detection should accept snake_case metadata aliases.");

        const string rootLevelMetadataJson = """
{
  "gameCode": "SR5",
  "gameName": "Shadowrun Fifth Edition"
}
""";
        string? rootLevelDetectedRuleset = HeroLabShadowrunImporter.DetectRulesetFromOnlineJson(rootLevelMetadataJson);
        AssertEx.Equal(RulesetDefaults.Sr5, rootLevelDetectedRuleset, "Hero Lab online ruleset detection should accept root-level game metadata.");

        const string nestedGameMetadataJson = """
{
  "game": {
    "code": "SR4",
    "name": "Shadowrun 4th Edition"
  }
}
""";
        string? nestedGameDetectedRuleset = HeroLabShadowrunImporter.DetectRulesetFromOnlineJson(nestedGameMetadataJson);
        AssertEx.Equal(RulesetDefaults.Sr4, nestedGameDetectedRuleset, "Hero Lab online ruleset detection should accept nested game metadata aliases.");

        const string aliasDriftJson = """
{
  "Metadata": {
    "game_code": "SR6",
    "game_name": "Shadowrun Sixth World",
    "hlo_version": "6.2.0",
    "export_version": "2026.04"
  },
  "Actors": {
    "actor_1": {
      "Name": "Case Runner",
      "Player": "Alice",
      "Game_Values": {
        "Alias": "Ghost",
        "Metatype": "Human",
        "build_method": "Priority",
        "karma": "11",
        "nuyen": "2500",
        "essence": "5.7",
        "walk": "10",
        "run": "15",
        "sprint": "20",
        "adept": true,
        "magician": false
      },
      "Items": {
        "as_body": {
          "name": "Body",
          "compset": "ability",
          "base_value": 2,
          "total_value": 4
        }
      }
    }
  }
}
""";

        HeroLabImportSnapshot snapshot = HeroLabShadowrunImporter.ImportOnlineJson(aliasDriftJson, "alias-drift.json");

        AssertEx.Equal(RulesetDefaults.Sr6, snapshot.RulesetId, "Hero Lab online import should map alias drift payloads onto SR6.");
        AssertEx.Equal("Case Runner", snapshot.Profile.Name, "Hero Lab online import should preserve actor name aliases.");
        AssertEx.Equal("Ghost", snapshot.Profile.Alias, "Hero Lab online import should preserve nested alias payloads.");
        AssertEx.Equal("Priority", snapshot.Profile.BuildMethod, "Hero Lab online import should preserve build-method aliases.");
        AssertEx.Equal(11m, snapshot.Progress.Karma, "Hero Lab online import should preserve karma values from alias payloads.");
        AssertEx.Equal(2500m, snapshot.Progress.Nuyen, "Hero Lab online import should preserve nuyen values from alias payloads.");
        AssertEx.Equal(5.7m, snapshot.Progress.TotalEssence, "Hero Lab online import should preserve essence values from alias payloads.");
        AssertEx.Equal("Shadowrun Sixth World", snapshot.Rules.GameplayOption, "Hero Lab online import should preserve metadata game-name aliases.");
        AssertEx.True(snapshot.Attributes.Attributes.Count > 0, "Hero Lab online import should keep attribute projections in alias payloads.");
        CharacterAttributeSummary body = snapshot.Attributes.Attributes.FirstOrDefault(static entry => string.Equals(entry.Name, "BOD", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Hero Lab online alias payload should project a Body attribute.");
        AssertEx.Equal(4, body.TotalValue, "Hero Lab online import should preserve total attribute values from alias payloads.");

        const string arrayShapeJson = """
{
  "metadata": {
    "gameCode": "SR6",
    "gameName": "Shadowrun Sixth World"
  },
  "actors": [
    {
      "isLead": true,
      "name": "Array Runner",
      "player": "Bob",
      "gameValues": {
        "alias": "Array Ghost",
        "buildMethod": "Karma",
        "karma": 9,
        "nuyen": 1200,
        "essence": 5.3
      },
      "items": [
        {
          "id": "as_body",
          "name": "Body",
          "compset": "ability",
          "baseValue": 3,
          "totalValue": 5
        },
        {
          "id": "wp_katana",
          "name": "Katana",
          "compset": "weapon",
          "items": [
            {
              "id": "ar_linedcoat",
              "name": "Lined Coat",
              "compset": "armor"
            }
          ]
        }
      ]
    }
  ]
}
""";

        HeroLabImportSnapshot arrayShapeSnapshot = HeroLabShadowrunImporter.ImportOnlineJson(arrayShapeJson, "array-shape.json");
        AssertEx.Equal(RulesetDefaults.Sr6, arrayShapeSnapshot.RulesetId, "Hero Lab online import should accept actors arrays.");
        AssertEx.Equal("Array Runner", arrayShapeSnapshot.Profile.Name, "Hero Lab online import should preserve lead actor from arrays.");
        AssertEx.Equal("Array Ghost", arrayShapeSnapshot.Profile.Alias, "Hero Lab online import should preserve profile aliases in array payloads.");
        AssertEx.Equal(9m, arrayShapeSnapshot.Progress.Karma, "Hero Lab online import should preserve progress values in array payloads.");
        AssertEx.Equal(1, arrayShapeSnapshot.Weapons.Count, "Hero Lab online import should parse weapon items from arrays.");
        AssertEx.Equal(1, arrayShapeSnapshot.Armors.Count, "Hero Lab online import should parse nested armor items from arrays.");
        CharacterAttributeSummary arrayBody = arrayShapeSnapshot.Attributes.Attributes.FirstOrDefault(static entry => string.Equals(entry.Name, "BOD", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Hero Lab online array payload should project a Body attribute.");
        AssertEx.Equal(5, arrayBody.TotalValue, "Hero Lab online import should preserve total attribute values from array payloads.");

        const string rootLevelImportMetadataJson = """
{
  "gameCode": "SR6",
  "gameName": "Shadowrun Sixth World",
  "hloVersion": "6.3.1",
  "exportVersion": "2026.05",
  "actors": {
    "actor_1": {
      "name": "Root Metadata Runner",
      "player": "Delta",
      "gameValues": {
        "alias": "Root Ghost",
        "buildMethod": "Priority"
      },
      "items": {}
    }
  }
}
""";

        HeroLabImportSnapshot rootLevelMetadataSnapshot = HeroLabShadowrunImporter.ImportOnlineJson(rootLevelImportMetadataJson, "root-level-metadata.json");
        AssertEx.Equal("Shadowrun Sixth World", rootLevelMetadataSnapshot.Rules.GameplayOption, "Hero Lab online import should project root-level game metadata into gameplay option.");
        AssertEx.Equal("6.3.1", rootLevelMetadataSnapshot.Profile.AppVersion, "Hero Lab online import should project root-level HLO version into app version.");
        AssertEx.Equal("2026.05", rootLevelMetadataSnapshot.Profile.CreatedVersion, "Hero Lab online import should project root-level export version into created-version provenance.");

        const string nestedImportMetadataJson = """
{
  "game": {
    "code": "SR6",
    "name": "Shadowrun Sixth World",
    "hloVersion": "6.4.0",
    "exportVersion": "2026.06"
  },
  "actors": {
    "actor_1": {
      "name": "Nested Metadata Runner",
      "player": "Echo",
      "gameValues": {
        "alias": "Nested Ghost",
        "buildMethod": "Karma"
      },
      "items": {}
    }
  }
}
""";

        HeroLabImportSnapshot nestedMetadataSnapshot = HeroLabShadowrunImporter.ImportOnlineJson(nestedImportMetadataJson, "nested-metadata.json");
        AssertEx.Equal("Shadowrun Sixth World", nestedMetadataSnapshot.Rules.GameplayOption, "Hero Lab online import should project nested game metadata into gameplay option.");
        AssertEx.Equal("6.4.0", nestedMetadataSnapshot.Profile.AppVersion, "Hero Lab online import should project nested HLO version into app version.");
        AssertEx.Equal("2026.06", nestedMetadataSnapshot.Profile.CreatedVersion, "Hero Lab online import should project nested export version into created-version provenance.");
    }

    private static void AssertOnlineFixtures(string directory, string[] expectedFixtureNames)
    {
        AssertFixtureNames(directory, "*.hlo.json", expectedFixtureNames);

        foreach (string fileName in expectedFixtureNames)
        {
            string filePath = Path.Combine(directory, fileName);
            HeroLabImportSnapshot snapshot = HeroLabShadowrunImporter.ImportOnlineJson(File.ReadAllText(filePath), fileName);
            HeroLabParityContract parity = LoadParityContract(filePath);

            AssertSnapshotCoverage(snapshot, parity, fileName);
            AssertAttributeParity(snapshot, parity, fileName);
            AssertSkillParity(snapshot, parity, fileName);
        }
    }

    private static void AssertFixtureNames(string directory, string searchPattern, string[] expectedFixtureNames)
    {
        string[] actualFixtureNames = Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .OrderBy(static fileName => fileName, StringComparer.Ordinal)
            .ToArray();

        AssertEx.SequenceEqual(
            expectedFixtureNames.OrderBy(static fileName => fileName, StringComparer.Ordinal).ToArray(),
            actualFixtureNames,
            $"{directory} should keep the governed Hero Lab fixture corpus in lockstep.");
    }

    private static HeroLabParityContract LoadParityContract(string fixturePath)
    {
        string parityPath = Path.Combine(
            Path.GetDirectoryName(fixturePath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(fixturePath)}.parity.json");

        if (!File.Exists(parityPath))
        {
            throw new InvalidOperationException($"{Path.GetFileName(fixturePath)} must keep a parity sidecar at {Path.GetFileName(parityPath)}.");
        }

        HeroLabParityContract? parity = JsonSerializer.Deserialize<HeroLabParityContract>(File.ReadAllText(parityPath));
        return parity ?? throw new InvalidOperationException($"{Path.GetFileName(parityPath)} must deserialize into a Hero Lab parity contract.");
    }

    private static void AssertSnapshotCoverage(HeroLabImportSnapshot snapshot, HeroLabParityContract parity, string fileName)
    {
        AssertEx.Equal(parity.RulesetId, snapshot.RulesetId, $"{fileName} imported onto the wrong Shadowrun ruleset lane.");
        AssertEx.True(!string.IsNullOrWhiteSpace(snapshot.Profile.Name), $"{fileName} should import a usable profile name.");
        AssertEx.True(snapshot.Attributes.Count >= parity.MinAttributes, $"{fileName} should preserve enough imported attributes.");
        AssertEx.True(snapshot.Skills.Count >= parity.MinSkills, $"{fileName} should preserve enough imported skills.");
        AssertEx.True(snapshot.Qualities.Count >= parity.MinQualities, $"{fileName} should preserve enough imported qualities.");
        AssertEx.True(snapshot.Contacts.Count >= parity.MinContacts, $"{fileName} should preserve enough imported contacts.");
        AssertEx.True(snapshot.Inventory.GearCount >= parity.MinGear, $"{fileName} should preserve enough imported gear items.");
        AssertEx.True(snapshot.Inventory.WeaponCount >= parity.MinWeapons, $"{fileName} should preserve enough imported weapons.");
        AssertEx.True(snapshot.Inventory.ArmorCount >= parity.MinArmor, $"{fileName} should preserve enough imported armor items.");

        switch (snapshot.RulesetId)
        {
            case RulesetDefaults.Sr4:
                AssertEx.Equal("SR4", snapshot.Rules.GameEdition, $"{fileName} should preserve the SR4 game edition marker.");
                break;
            case RulesetDefaults.Sr5:
                AssertEx.Equal("SR5", snapshot.Rules.GameEdition, $"{fileName} should preserve the SR5 game edition marker.");
                break;
            case RulesetDefaults.Sr6:
                AssertEx.Equal("SR6", snapshot.Rules.GameEdition, $"{fileName} should preserve the SR6 game edition marker.");
                break;
        }
    }

    private static void AssertAttributeParity(HeroLabImportSnapshot snapshot, HeroLabParityContract parity, string fileName)
    {
        foreach ((string attributeName, int expectedDelta) in parity.AttributeDeltas ?? new Dictionary<string, int>(StringComparer.Ordinal))
        {
            HeroLabAttributeOracle oracle = snapshot.OracleAttributes.FirstOrDefault(attribute => NameComparer.Equals(attribute.Name, attributeName))
                ?? throw new InvalidOperationException($"{fileName} must expose Hero Lab attribute '{attributeName}' for parity audit.");
            int expectedTotal = oracle.BaseValue + expectedDelta;
            AssertEx.Equal(expectedTotal, oracle.TotalValue, $"{fileName} Hero Lab attribute '{attributeName}' drifted from the governed parity delta.");

            CharacterAttributeSummary imported = snapshot.Attributes.Attributes.FirstOrDefault(attribute => NameComparer.Equals(attribute.Name, attributeName))
                ?? throw new InvalidOperationException($"{fileName} must expose imported attribute '{attributeName}' for parity audit.");
            AssertEx.Equal(oracle.TotalValue, imported.TotalValue, $"{fileName} imported attribute '{attributeName}' should match the Hero Lab oracle total.");
        }
    }

    private static void AssertSkillParity(HeroLabImportSnapshot snapshot, HeroLabParityContract parity, string fileName)
    {
        foreach ((string skillName, int expectedDelta) in parity.SkillDeltas ?? new Dictionary<string, int>(StringComparer.Ordinal))
        {
            HeroLabSkillOracle oracle = snapshot.OracleSkills.FirstOrDefault(skill => NameComparer.Equals(skill.Name, skillName))
                ?? throw new InvalidOperationException($"{fileName} must expose Hero Lab skill '{skillName}' for parity audit.");
            int expectedTotal = oracle.BaseValue + expectedDelta;
            AssertEx.Equal(expectedTotal, oracle.TotalValue, $"{fileName} Hero Lab skill '{skillName}' drifted from the governed parity delta.");

            CharacterSkillSummary imported = snapshot.Skills.Skills.FirstOrDefault(skill => NameComparer.Equals(skill.Suid, skillName))
                ?? throw new InvalidOperationException($"{fileName} must expose imported skill '{skillName}' for parity audit.");
            AssertEx.Equal(expectedDelta, imported.KarmaValue, $"{fileName} imported skill '{skillName}' should preserve the Hero Lab delta.");
        }
    }

    private sealed record HeroLabParityContract(
        string RulesetId,
        IReadOnlyDictionary<string, int>? AttributeDeltas = null,
        IReadOnlyDictionary<string, int>? SkillDeltas = null,
        int MinAttributes = 0,
        int MinSkills = 0,
        int MinQualities = 0,
        int MinContacts = 0,
        int MinGear = 0,
        int MinWeapons = 0,
        int MinArmor = 0);
}
