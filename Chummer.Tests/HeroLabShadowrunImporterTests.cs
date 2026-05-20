#nullable enable annotations

using Chummer.Application.Workspaces;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class HeroLabShadowrunImporterTests
{
    [TestMethod]
    public void Detect_ruleset_from_online_json_accepts_snake_case_metadata_aliases()
    {
        const string json = """
{
  "metadata": {
    "game_code": "SR6",
    "game_name": "Shadowrun Sixth World"
  }
}
""";

        string? rulesetId = HeroLabShadowrunImporter.DetectRulesetFromOnlineJson(json);

        Assert.AreEqual(RulesetDefaults.Sr6, rulesetId);
    }

    [TestMethod]
    public void Import_online_json_accepts_case_and_separator_drift_for_actor_and_game_values_keys()
    {
        const string json = """
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

        HeroLabImportSnapshot snapshot = HeroLabShadowrunImporter.ImportOnlineJson(json, "alias-drift.json");

        Assert.AreEqual(RulesetDefaults.Sr6, snapshot.RulesetId);
        Assert.AreEqual("Case Runner", snapshot.Profile.Name);
        Assert.AreEqual("Ghost", snapshot.Profile.Alias);
        Assert.AreEqual("Priority", snapshot.Profile.BuildMethod);
        Assert.AreEqual(11m, snapshot.Progress.Karma);
        Assert.AreEqual(2500m, snapshot.Progress.Nuyen);
        Assert.AreEqual(5.7m, snapshot.Progress.TotalEssence);
        Assert.AreEqual("Shadowrun Sixth World", snapshot.Rules.GameplayOption);
        Assert.AreEqual(1, snapshot.Attributes.Count);
        Assert.AreEqual("BOD", snapshot.Attributes.Attributes[0].Name);
        Assert.AreEqual(4, snapshot.Attributes.Attributes[0].TotalValue);
    }

    [TestMethod]
    public void Import_online_json_accepts_actor_arrays_and_nested_items()
    {
        const string json = """
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

        HeroLabImportSnapshot snapshot = HeroLabShadowrunImporter.ImportOnlineJson(json, "array-shape.json");

        Assert.AreEqual(RulesetDefaults.Sr6, snapshot.RulesetId);
        Assert.AreEqual("Array Runner", snapshot.Profile.Name);
        Assert.AreEqual("Array Ghost", snapshot.Profile.Alias);
        Assert.AreEqual("Karma", snapshot.Profile.BuildMethod);
        Assert.AreEqual(9m, snapshot.Progress.Karma);
        Assert.AreEqual(1200m, snapshot.Progress.Nuyen);
        Assert.AreEqual(5.3m, snapshot.Progress.TotalEssence);
        Assert.AreEqual(1, snapshot.Weapons.Count);
        Assert.AreEqual(1, snapshot.Armors.Count);
        Assert.AreEqual(5, snapshot.Attributes.Attributes[0].TotalValue);
    }

    [TestMethod]
    public void Import_online_json_projects_root_level_metadata_aliases()
    {
        const string json = """
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

        HeroLabImportSnapshot snapshot = HeroLabShadowrunImporter.ImportOnlineJson(json, "root-level-metadata.json");

        Assert.AreEqual("Shadowrun Sixth World", snapshot.Rules.GameplayOption);
        Assert.AreEqual("6.3.1", snapshot.Profile.AppVersion);
        Assert.AreEqual("2026.05", snapshot.Profile.CreatedVersion);
        Assert.AreEqual("Root Ghost", snapshot.Profile.Alias);
    }

    [TestMethod]
    public void Import_online_json_projects_nested_game_metadata_aliases()
    {
        const string json = """
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

        HeroLabImportSnapshot snapshot = HeroLabShadowrunImporter.ImportOnlineJson(json, "nested-metadata.json");

        Assert.AreEqual("Shadowrun Sixth World", snapshot.Rules.GameplayOption);
        Assert.AreEqual("6.4.0", snapshot.Profile.AppVersion);
        Assert.AreEqual("2026.06", snapshot.Profile.CreatedVersion);
        Assert.AreEqual("Nested Ghost", snapshot.Profile.Alias);
    }

    [TestMethod]
    public void Import_online_json_rejects_payloads_without_actors_collection()
    {
        const string json = """
{
  "metadata": {
    "gameCode": "SR6",
    "gameName": "Shadowrun Sixth World"
  }
}
""";

        try
        {
            HeroLabShadowrunImporter.ImportOnlineJson(json, "missing-actors.json");
            Assert.Fail("Expected import without actors collection to throw.");
        }
        catch (System.InvalidOperationException exception)
        {
            StringAssert.Contains(exception.Message, "must keep a Hero Lab Online actors collection");
        }
    }

    [TestMethod]
    public void Convert_online_json_to_native_xml_projects_expected_core_fields()
    {
        const string json = """
{
  "metadata": {
    "gameCode": "SR6",
    "gameName": "Shadowrun Sixth World",
    "hloVersion": "6.5.0",
    "exportVersion": "2026.07"
  },
  "actors": {
    "actor_1": {
      "name": "Xml Runner",
      "player": "Foxtrot",
      "gameValues": {
        "alias": "Xml Ghost",
        "buildMethod": "Priority",
        "karma": "12",
        "nuyen": "3400",
        "walk": "10",
        "run": "15",
        "sprint": "20",
        "adept": true,
        "tradition": "Hermetic"
      },
      "items": {}
    }
  }
}
""";

        string xml = HeroLabShadowrunImporter.ConvertOnlineJsonToNativeXml(json, "native-xml.json");

        StringAssert.Contains(xml, "<name>Xml Runner</name>");
        StringAssert.Contains(xml, "<alias>Xml Ghost</alias>");
        StringAssert.Contains(xml, "<buildmethod>Priority</buildmethod>");
        StringAssert.Contains(xml, "<createdversion>2026.07</createdversion>");
        StringAssert.Contains(xml, "<appversion>6.5.0</appversion>");
        StringAssert.Contains(xml, "<karma>12</karma>");
        StringAssert.Contains(xml, "<nuyen>3400</nuyen>");
        StringAssert.Contains(xml, "<adept>True</adept>");
        StringAssert.Contains(xml, "<tradition>Hermetic</tradition>");
    }
}
