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
}
