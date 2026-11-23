using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCreationCompletionFinalizationProjector;

namespace Chummer.Application.Characters;

internal static class CharacterCreationAstralInitiative1249
{
    internal static CharacterCreationMysticAdeptPowerPointPolicy? Source(AwakenedGraphInputs inputs)
        => inputs switch
        {
            FullAwakenedGraphInputs full => full.Inputs.Authority.MysticAdeptPowerPointPolicy,
            SelectedAwakenedGraphInputs selected => selected.Authority.MysticAdeptPowerPointPolicy,
            _ => null
        };

    internal static CharacterAstralInitiativeDiceProjection? Evaluate(XElement graph,
        string? schema, AwakenedGraphInputs inputs, CharacterCreationPrerequisiteDraft draft)
        => schema is null ? null
            : schema == CharacterCreationCompletionReviewSchemas.LifecycleResultV1
                ? Evaluate(graph, Source(inputs), draft)
                    ?? throw new InvalidDataException("lifecycle-native-result-unresolved")
                : throw new InvalidDataException("lifecycle-result-version-unsupported");

    internal static CharacterAstralInitiativeDiceProjection? Evaluate(XElement graph,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationPrerequisiteDraft draft)
    {
        if (!CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(source)
            || source!.SettingsProfileId != draft.SettingsProfileId
            || graph.Name != "character" || PlainLeaf(graph, "created") is not { } saved
            || !bool.TryParse(saved.Value, out bool created)
            || draft.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)) return null;
        var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        if (!TryBounds(profile, out int minimum, out int maximum)) return null;
        // Retained Character.AstralInitiativeDice and its async equivalent read
        // precisely these two settings. No MAG gate, max clamp, or dice improvement
        // participates. In particular minimum > maximum is a valid calculation.
        // The magic policy carries raw settings inputs; source execution
        // binds those inputs to the selected profile.
        return new(Math.Min(minimum, maximum), minimum, maximum, created, source.SettingsProfileId,
            CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
            CharacterCreationFinalizationDigest.Compute(
                profile.ToString(SaveOptions.DisableFormatting)),
            CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting)));
    }

    internal static bool TryBounds(XElement profile, out int minimum, out int maximum)
    {
        minimum = maximum = 0;
        return PlainLeaf(profile, "minastralinitiativedice") is { } low
            && PlainLeaf(profile, "maxastralinitiativedice") is { } high
            && int.TryParse(low.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minimum) && minimum >= 0
            && int.TryParse(high.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out maximum) && maximum >= 0;
    }

    private static XElement? PlainLeaf(XElement parent, string field)
        => parent.Elements().Where(node => node.Name.LocalName == field).ToArray() is [var leaf]
            && leaf.Name == field && !leaf.HasAttributes
            && leaf.Nodes().ToArray() is [XText text] && text.GetType() == typeof(XText) ? leaf : null;
}

internal sealed partial class CharacterCreationSelectedEffectsConsumer
{
    // Produced by the native graph compiler, before any source report exists.
    private CharacterAstralInitiativeDiceProjection? astralInitiative1249;
    internal string? LifecycleResultSchema1272 { get; private init; }

    internal CharacterAstralInitiativeDiceProjection? AstralInitiative1249(CharacterCreationCompleteAssessment authority)
        => DigestFor(authority) is not null && astralInitiative1249 is { } result
            && result.GraphDigest == CharacterCreationFinalizationDigest.Compute(
                compiledGraph.ToString(SaveOptions.DisableFormatting)) ? result : null;
}
