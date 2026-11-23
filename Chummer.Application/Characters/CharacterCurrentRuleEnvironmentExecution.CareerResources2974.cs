using Chummer.Contracts.Characters;
using V = Chummer.Application.Characters.CharacterRuleEffectValues2912;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCurrentRuleEnvironmentExecution
{
    // Display only, called on the freshly readmitted execution. Keep this outside
    // domain outcome hashes and never turn a saved balance into a source price.
    private CharacterRuleEffectSectionProjection WithCareerResources2974(
        CharacterRuleEffectSectionProjection section, CancellationToken cancellationToken)
    {
        if (context.Stage != CharacterCurrentRuleStage.Career || section.Section != CharacterRuleEffectSection.Profile
            || section.Coverage is CharacterRuleEffectCoverage.NotSelected or CharacterRuleEffectCoverage.NotApplicable)
            return section;
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.IsUnchanged) throw new InvalidOperationException("Career resource observation changed.");

        CharacterFileSummary? balance = null;
        try
        {
            var document = new CharacterDocument(context.CharacterXml);
            // ParseSummary has a missing-field default. The existing validator
            // must succeed first so an absent/invalid balance never becomes zero.
            if (context.Queries.Validate(document) is { IsValid: true }
                && context.Queries.ParseSummary(document) is { Created: true } summary)
                balance = summary;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or FormatException or OverflowException or System.Xml.XmlException or System.Text.Json.JsonException)
        {
            // An unavailable canonical query is an unknown display value, never
            // a computed fallback, an expense sum or a change to admission.
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.IsUnchanged) throw new InvalidOperationException("Career resource observation changed.");

        const string unavailable = "Saved Career balances could not be read from a valid current character.";
        var values = section.Values.Concat(new[]
        {
            V.Number("career-resources", "Current Nuyen", CharacterRuleEffectMetric.RemainingNuyen,
                balance?.Nuyen, "saved-balance", reason: unavailable),
            V.Number("career-resources", "Current Karma", CharacterRuleEffectMetric.KarmaRemaining,
                balance?.Karma, "saved-balance", reason: unavailable)
        }).ToArray();
        // Saved balances do not complete any other Profile calculations.
        // Retain domain coverage; an unavailable balance makes a previously
        // calculated section partial. Reasons are owned by their producer.
        return new(section.Section, balance is null && section.Coverage == CharacterRuleEffectCoverage.Calculated
                ? CharacterRuleEffectCoverage.Partial : section.Coverage,
            values, section.Reasons.Append(balance is null ? unavailable
                : "Current Nuyen and Karma are saved balances. Changing rule sources does not reprice cash or past expenses; other Profile values retain their reported coverage.").ToArray());
    }
}
