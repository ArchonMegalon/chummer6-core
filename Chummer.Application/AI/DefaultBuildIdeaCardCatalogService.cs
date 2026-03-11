using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.AI;

public sealed class DefaultBuildIdeaCardCatalogService : IBuildIdeaCardCatalogService
{
    private static readonly IReadOnlyList<BuildIdeaCard> Cards =
    [
        new(
            IdeaId: "sr5.street-samurai-ladder",
            RulesetId: RulesetDefaults.Sr5,
            Title: "Street Samurai Ladder",
            Summary: "Weapon accuracy, initiative, and survivability upgrades paced for 25/50/100 Karma checkpoints.",
            RoleTags: ["combat", "street-samurai", "initiative"],
            CompatibleProfileIds: ["official.sr5.core"],
            CoreLoop: "Hit first, stay standing, and convert each spend into cleaner attack and soak math.",
            EarlyPriorities: ["Reaction boosts", "initiative boosters", "weapon specialization"],
            KarmaMilestones: ["25 Karma: tighten attack pools", "50 Karma: improve survivability", "100 Karma: branch into utility edges"],
            Strengths: ["fast combat payoff", "clear upgrade path"],
            Weaknesses: ["resource hungry", "can neglect utility skills"],
            TrapChoices: ["overspending on niche weapons before initiative", "ignoring non-combat coverage"],
            LinkedContentIds: ["wired-reflexes", "reaction", "automatics"],
            CommunityScore: 0.92),
        new(
            IdeaId: "sr5.decker-support-operator",
            RulesetId: RulesetDefaults.Sr5,
            Title: "Decker Support Operator",
            Summary: "Balances matrix presence, initiative, and legwork tools without collapsing into one-trick combat debt.",
            RoleTags: ["matrix", "decker", "support"],
            CompatibleProfileIds: ["official.sr5.core"],
            CoreLoop: "Leverage matrix control and intel while preserving enough action economy to stay alive at the table.",
            EarlyPriorities: ["core matrix actions", "initiative support", "perception coverage"],
            KarmaMilestones: ["25 Karma: stabilize hacking pools", "50 Karma: add table utility", "100 Karma: deepen action-economy options"],
            Strengths: ["strong legwork", "broad table contribution"],
            Weaknesses: ["gear intensive", "fragile if over-specialized"],
            TrapChoices: ["buying every matrix toy before core pools", "ignoring physical survivability"],
            LinkedContentIds: ["hacking", "cyberdeck", "electronic-warfare"],
            CommunityScore: 0.88),
        new(
            IdeaId: "sr5.face-legwork-hybrid",
            RulesetId: RulesetDefaults.Sr5,
            Title: "Face Legwork Hybrid",
            Summary: "Turns social dominance into safer runs by pairing negotiation with perception, contacts, and fallback utility.",
            RoleTags: ["social", "face", "legwork"],
            CompatibleProfileIds: ["official.sr5.core"],
            CoreLoop: "Open doors with social leverage, then cash it out through contacts, intel, and light backup utility.",
            EarlyPriorities: ["social core skills", "contact network", "perception and etiquette"],
            KarmaMilestones: ["25 Karma: secure negotiation floor", "50 Karma: broaden intel coverage", "100 Karma: reinforce fallback combat or magic"],
            Strengths: ["campaign glue", "works outside combat"],
            Weaknesses: ["can stall in combat-heavy tables"],
            TrapChoices: ["dumping perception", "treating contacts as flavor only"],
            LinkedContentIds: ["negotiation", "con", "contacts"],
            CommunityScore: 0.85)
    ];

    public IReadOnlyList<BuildIdeaCard> SearchBuildIdeas(OwnerScope owner, string routeType, string queryText, string? rulesetId = null, int maxCount = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeType);
        maxCount = Math.Max(1, maxCount);
        string normalizedQuery = queryText?.Trim() ?? string.Empty;
        string? normalizedRuleset = RulesetDefaults.NormalizeOptional(rulesetId);

        IEnumerable<BuildIdeaCard> query = Cards;
        if (!string.IsNullOrWhiteSpace(normalizedRuleset))
        {
            query = query.Where(card => string.Equals(RulesetDefaults.NormalizeRequired(card.RulesetId), normalizedRuleset, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            BuildIdeaCard[] matched = query
                .Select(card => (Card: card, Score: Score(card, normalizedQuery)))
                .Where(static item => item.Score > 0)
                .OrderByDescending(static item => item.Score)
                .ThenByDescending(static item => item.Card.CommunityScore)
                .Select(static item => item.Card)
                .ToArray();
            query = matched.Length > 0
                ? matched
                : query.OrderByDescending(static card => card.CommunityScore);
        }
        else
        {
            query = query.OrderByDescending(static card => card.CommunityScore);
        }

        return query
            .Take(maxCount)
            .ToArray();
    }

    public BuildIdeaCard? GetBuildIdea(OwnerScope owner, string ideaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ideaId);

        return Cards.FirstOrDefault(card => string.Equals(card.IdeaId, ideaId, StringComparison.Ordinal));
    }

    private static int Score(BuildIdeaCard card, string queryText)
    {
        int score = 0;
        if (card.Title.Contains(queryText, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (card.Summary.Contains(queryText, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }

        if (card.RoleTags.Any(tag => tag.Contains(queryText, StringComparison.OrdinalIgnoreCase)))
        {
            score += 4;
        }

        if (card.CoreLoop.Contains(queryText, StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        return score;
    }
}
