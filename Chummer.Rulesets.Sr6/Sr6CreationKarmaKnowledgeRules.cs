using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Additional knowledge and language ranks; never spends the free Logic pool.</summary>
public static class Sr6CreationKarmaKnowledgeRules
{
    public static Sr6CreationKarmaKnowledgeOptions? Options(Sr6CreationFoundationPreview foundation)
        => foundation is { Attributes: not null, Skills: not null, Selection.Knowledge: { } pool }
            ? new(3, 3, pool.NativeLanguage, pool.Languages.ToArray(), Sr6CreationLanguageLevels.Ordered,
                [Sr6CreationKarmaRules.SourceAnchor, Sr6CreationKnowledgeRules.SourceAnchor]) : null;

    public static CharacterCreationFoundationResult<Sr6CreationKarmaKnowledgePreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationKarmaKnowledgeSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeKarma(new([], [], 0) { Knowledge = selection }, out var frozen)
            || frozen!.Knowledge is not { } choices)
            return Fail(Sr6CreationKarmaBlockers.InvalidSelection);
        if (Options(foundation) is not { } options || foundation.Selection.Knowledge is not { } pool)
            return Fail(Sr6CreationKarmaBlockers.KnowledgeRequired);
        var existingIds = pool.KnowledgeSkills.Select(row => row.Id).Concat(pool.Languages.Select(row => row.Id)).ToHashSet();
        var knowledge = new List<Sr6CreationKarmaKnowledgeValue>();
        var languages = new List<Sr6CreationKarmaLanguageValue>();
        foreach (var row in choices.KnowledgeSkills)
        {
            if (existingIds.Contains(row.Id) || pool.KnowledgeSkills.Any(value =>
                string.Equals(value.Name, row.Name, StringComparison.OrdinalIgnoreCase)))
                return Fail(Sr6CreationKarmaBlockers.KnowledgeConflict);
            knowledge.Add(new(row.Id, row.Name, options.KnowledgeKarmaCost, true));
        }
        foreach (var row in choices.Languages)
        {
            var baseline = pool.Languages.SingleOrDefault(value => value.Id == row.Id);
            if (string.Equals(row.Name, pool.NativeLanguage, StringComparison.OrdinalIgnoreCase)
                || baseline is null && (existingIds.Contains(row.Id) || pool.Languages.Any(value =>
                    string.Equals(value.Name, row.Name, StringComparison.OrdinalIgnoreCase)))
                || baseline is not null && baseline.Name != row.Name)
                return Fail(Sr6CreationKarmaBlockers.KnowledgeConflict);
            int rank = Rank(row.Level), purchased = rank - Rank(baseline?.Level);
            if (purchased <= 0) return Fail(Sr6CreationKarmaBlockers.KnowledgeConflict);
            languages.Add(new(row.Id, row.Name, baseline?.Level, row.Level, purchased,
                purchased * options.LanguageLevelKarmaCost, rank == 1 ? 0 : rank));
        }
        int cost = knowledge.Sum(row => row.KarmaCost) + languages.Sum(row => row.KarmaCost);
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-karma-knowledge.v1", Sr6CreationFoundationRules.CoreSourceSha256,
            foundation.Binding.AuthorityDigest, Pool = pool, options, knowledge, languages, cost
        });
        return new(CharacterCreationFoundationOutcomes.Success,
            new(knowledge.ToArray(), languages.ToArray(), cost, authority, options.SourceAnchorIds), []);
    }

    private static int Rank(string? level) => level switch
    {
        Sr6CreationLanguageLevels.Basic => 1, Sr6CreationLanguageLevels.Specialist => 2,
        Sr6CreationLanguageLevels.Expert => 3, _ => 0
    };

    private static CharacterCreationFoundationResult<Sr6CreationKarmaKnowledgePreview> Fail(string reason)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [reason]);
}
