using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCreationCompletionFinalizationProjector;

namespace Chummer.Application.Characters;

internal static class CharacterCreationNativeSettings1420
{
    // Reviewed consumers are pinned by NativeSettings1420.targets. This producer
    // retains the source policy for the two native operations; it executes neither.
    internal static CharacterCreationNativeSettingsProjection? Evaluate(XElement graph, string? schema,
        AwakenedGraphInputs inputs, CharacterCreationPrerequisiteDraft draft)
        => schema == CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            ? Evaluate(graph, CharacterCreationAstralInitiative1249.Source(inputs), draft) : null;

    internal static CharacterCreationNativeSettingsProjection? Evaluate(XElement graph,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationPrerequisiteDraft draft)
    {
        if (!CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(source)
            || source!.SettingsProfileId != draft.SettingsProfileId || graph.Name != "character"
            || PlainLeaf(graph, "gameedition")?.Value != "SR5"
            || PlainLeaf(graph, "created") is not { } saved || !bool.TryParse(saved.Value, out bool created)
            || draft.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen))
            return null;
        var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        if (profile.HasAttributes
            || !Read(CharacterCreationObservedSetting.LicenseRestricted, out var licenses)
            || !Read(CharacterCreationObservedSetting.MoreLethalGameplay, out var lethal)
            || licenses is null && lethal is null) return null;
        return new(licenses, lethal, created, source.SettingsProfileId,
            CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
            CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
            CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting)));

        bool Read(CharacterCreationObservedSetting key, out bool? value)
        {
            value = null;
            var definition = CharacterCreationSettingDefinitions.NativeLifecycle1420.Single(row => row.Key == key);
            if (!profile.Elements().Any(node => node.Name.LocalName == definition.Element)) return true;
            if (PlainLeaf(profile, definition.Element) is not { } leaf || !bool.TryParse(leaf.Value, out bool parsed)) return false;
            value = parsed;
            return true;
        }
    }

    internal static bool? Value(CharacterCreationNativeSettingsProjection result, string field) => field switch
    {
        "licenserestricted" => result.LicenseRestricted,
        "morelethalgameplay" => result.MoreLethalGameplay,
        _ => null
    };

    private static XElement? PlainLeaf(XElement parent, string field)
        => parent.Elements().Where(node => node.Name.LocalName == field).ToArray() is [var leaf]
            && leaf.Name == field && !leaf.HasAttributes && leaf.Nodes().ToArray() is [XText text]
            && text.GetType() == typeof(XText) ? leaf : null;
}

internal sealed partial class CharacterCreationSelectedEffectsConsumer
{
    private CharacterCreationNativeSettingsProjection? nativeSettings1420;
    internal CharacterCreationNativeSettingsProjection? NativeSettings1420(CharacterCreationCompleteAssessment authority)
        => DigestFor(authority) is not null && nativeSettings1420 is { } result
            && result.GraphDigest == CharacterCreationFinalizationDigest.Compute(
                compiledGraph.ToString(SaveOptions.DisableFormatting)) ? result : null;
}
