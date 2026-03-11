#nullable enable annotations

using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class BuildIdeaCardCatalogServiceTests
{
    [TestMethod]
    public void Build_idea_catalog_returns_top_cards_when_query_has_no_direct_match()
    {
        DefaultBuildIdeaCardCatalogService service = new();

        var cards = service.SearchBuildIdeas(OwnerScope.LocalSingleUser, "build", "next upgrade", RulesetDefaults.Sr5, maxCount: 2);

        Assert.HasCount(2, cards);
        Assert.IsTrue(cards.All(card => card.RulesetId == RulesetDefaults.Sr5));
        Assert.IsTrue(cards.All(card => card.Provenance == "build-idea-card"));
    }

    [TestMethod]
    public void Build_idea_catalog_prefers_matching_role_tags_and_titles()
    {
        DefaultBuildIdeaCardCatalogService service = new();

        var cards = service.SearchBuildIdeas(OwnerScope.LocalSingleUser, "coach", "social", RulesetDefaults.Sr5, maxCount: 3);

        Assert.IsGreaterThan(0, cards.Count);
        Assert.AreEqual("sr5.face-legwork-hybrid", cards[0].IdeaId);
    }

    [TestMethod]
    public void Build_idea_catalog_returns_detail_projection_for_known_id()
    {
        DefaultBuildIdeaCardCatalogService service = new();

        var card = service.GetBuildIdea(OwnerScope.LocalSingleUser, "sr5.face-legwork-hybrid");

        Assert.IsNotNull(card);
        Assert.AreEqual(RulesetDefaults.Sr5, card.RulesetId);
        Assert.AreEqual("Face Legwork Hybrid", card.Title);
    }
}
