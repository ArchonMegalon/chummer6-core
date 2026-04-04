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
