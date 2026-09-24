using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Life Modules skill contribution for whole-runner finalization. Replays
/// the method-owned quote; free module levels stay in the separate effect graph.
/// The finalizer still owns fresh source admission and atomic whole-runner storage.</summary>
internal static class CharacterCreationLifeModuleSkillsLegacyProjector
{
    internal static bool TryProject(string characterXml, CharacterCreationFoundationSequenceWritePlan effects,
        CharacterCreationLifeModuleMetatypeWritePlan racial, CharacterCreationLifeModuleTalentWritePlan talent,
        CharacterCreationLifeModuleAttributeQuote attributes, CharacterCreationSkillsCatalog catalog,
        CharacterCreationLifeModuleSkillsQuote quote, out XElement? skillsGraph,
        out CharacterCreationFinalizationDelta[] deltas)
    {
        skillsGraph = null;
        deltas = [];
        if (quote is not { Blockers.Count: 0, Policy: not null, Skills: not null, Groups: not null, Selection: not null })
            return false;
        var replay = CharacterCreationLifeModuleSkillsRules.Quote(characterXml, effects, racial, talent, attributes,
            quote.Selection, catalog, quote.Policy);
        if (replay.Quote is null || replay.Blockers.Count != 0
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(quote, replay.Quote))
            return false;
        return CharacterCreationPurchasedSkillsLegacyProjector.TryProject(catalog,
            quote.Skills.Select(row => (row.Allocation, row.Rating, row.KarmaCost)).ToArray(),
            quote.Groups.Select(row => (row.Allocation, row.IsBroken, row.KarmaCost)).ToArray(),
            quote.KarmaUsed, quote.QuoteDigest, "life-module", out skillsGraph, out deltas);
    }
}
