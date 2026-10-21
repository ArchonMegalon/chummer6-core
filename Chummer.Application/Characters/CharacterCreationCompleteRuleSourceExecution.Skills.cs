using Chummer.Contracts.Characters;
using System.Xml;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private IReadOnlyDictionary<string, string> ExecuteSkillsSource(
        CharacterCreationAttributeSkillsPreview actual,
        CharacterCreationQualitiesCompletionEvaluation? qualities)
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.RuleSourceExecutionSchema is not (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
            || Inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema || qualities is null)
            return empty;
        try
        {
            // The same canonical consumer is used for full and cold Finish Character.
            // It executes point/grant/group allocation and active/knowledge purchases,
            // including specialization prices and group compensation. It returns a
            // private trace only after the ENTIRE actual result equals that execution.
            // Reconstruct from retained full authority, never selected DTO assertions.
            var executed = CharacterCreationCompleteConsumerExecution.ExecuteSkills(
                Inputs.Skills, request.Selections, Inputs.EffectivePolicy!, actual, qualities,
                (Inputs.Magic.SelectedTalent.GrantedQualitySources ?? [])
                    .Select(row => row.CanonicalSourceXml),
                default, allowDiceOnlyGrants: true);
            if (executed is not { Completed: true }
                || request.Selections.SkillKarmaPurchases is not null && executed.PurchasesDigest is null)
                return empty;
            return resolution.ConsumeSkillsSource(Inputs.Skills.Authority, request.Selections, actual);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException
            or ArgumentException or OverflowException or XmlException)
        {
            return empty;
        }
    }
}
