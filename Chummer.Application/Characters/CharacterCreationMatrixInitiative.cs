using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCreationCompletionFinalizationProjector;

namespace Chummer.Application.Characters;

internal static partial class CharacterCreationMatrixInitiative
{
    internal const string Blocker = "matrix-initiative-graph-unresolved";

    internal static CharacterMatrixInitiativeDiceProjection? Evaluate(XElement graph, string? schema,
        AwakenedGraphInputs inputs, CharacterCreationPrerequisiteDraft draft)
        => schema is null ? null : schema == CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            ? Evaluate(graph, CharacterCreationAstralInitiative1249.Source(inputs), draft)
            : throw new InvalidDataException("lifecycle-result-version-unsupported");

    internal static CharacterMatrixInitiativeDiceProjection? Evaluate(XElement graph,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationPrerequisiteDraft draft)
    {
        if (!CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(source)
            || source!.SettingsProfileId != draft.SettingsProfileId
            || draft.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)) return null;
        var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        // Missing/invalid grouped policy earns no capability or source receipt.
        if (!TryPolicy(profile, out var policy)) return null;
        if (graph.Name != "character" || graph.HasAttributes || Scalar(graph, "gameedition") != "SR5") throw Invalid();
        bool created = Boolean(graph, "created");
        bool depth = Boolean(graph, "depenabled");
        var improvements = Improvements(graph);
        int? bodyMaximum = depth ? BodyMaximum(graph, improvements) : null;
        bool ai = depth && bodyMaximum == 0;
        XElement? device = NeedsGearFilter(graph) ? ActiveCommlink(graph) : null;
        var channels = MatrixChannels(graph, created, device);
        int dice = Round(channels.Dice);
        int? generalMaximum = ai ? Nonnegative(profile, "maxinitiativedice") : null;
        decimal? addValue = ai ? channels.Additive : null;
        int? additive = ai ? Round(channels.Additive) : null;
        int cold, hot;
        if (ai)
            cold = hot = Math.Min(checked(policy!.MinimumHotDice + dice + additive!.Value), generalMaximum!.Value);
        else
        {
            cold = Math.Min(checked(policy!.MinimumColdDice + dice), policy.MaximumColdDice);
            hot = Math.Min(checked(policy.MinimumHotDice + dice), policy.MaximumHotDice);
        }
        // No lower clamp: the native formula permits a negative improved total.
        return new(cold, hot, ai, channels.Dice, dice, addValue, additive, policy!,
            new(depth, bodyMaximum, generalMaximum, improvements.ToString(SaveOptions.DisableFormatting),
                device?.ToString(SaveOptions.DisableFormatting)), created, source.SettingsProfileId,
            CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
            CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
            CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting)));
    }

    internal static bool TryPolicy(XElement profile, out CharacterMatrixInitiativePolicy? policy)
    {
        policy = null;
        if (profile.Name != "setting" || profile.HasAttributes) return false;
        var values = new List<int>();
        foreach (var definition in CharacterCreationSettingDefinitions.MatrixLifecycle)
        {
            var nodes = profile.Elements().Where(node => node.Name.LocalName == definition.Element).ToArray();
            if (nodes is not [var leaf] || leaf.Name != definition.Element || leaf.HasAttributes
                || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                || !int.TryParse(text.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                || value < 0) return false;
            values.Add(value);
        }
        // Native getters/loaders do not enforce min <= max: Math.Min is the rule.
        policy = new(values[0], values[1], values[2], values[3]);
        return true;
    }

    private static int BodyMaximum(XElement graph, XElement improvements)
    {
        var attributes = One(graph, "attributes") ?? throw Invalid();
        if (attributes.HasAttributes || attributes.Elements().Any(node => node.Name != "attribute")) throw Invalid();
        var matches = attributes.Elements().Where(row => Scalar(row, "name") == "BOD").ToArray();
        if (matches is not [var body] || body.HasAttributes) throw Invalid();
        // Attribute.MetatypeMaximum reads the last enabled ReplaceAttribute.
        // Shapeshifter alternate attribute selection is owned by that graph compiler.
        if (One(body, "metatypecategory") is { } category && category.Value != "Standard") throw Invalid();
        int maximum = Nonnegative(body, "metatypemax");
        foreach (var row in improvements.Elements())
            if (Scalar(row, "improvementttype") == "ReplaceAttribute" && Scalar(row, "improvedname", "") == "BOD"
                && Integer(row, "enabled", 1) > 0 && Integer(row, "max", 0) is var replacement && replacement != 0)
                maximum = replacement;
        return maximum;
    }

    private static int Round(decimal value) => decimal.ToInt32(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value));
    private static int Nonnegative(XElement root, string name)
    {
        int value = Integer(root, name);
        return value >= 0 ? value : throw Invalid();
    }
    private static int Integer(XElement root, string name, int? absent = null)
        => int.TryParse(Scalar(root, name, absent?.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value) ? value : throw Invalid();
    private static bool Boolean(XElement root, string name, bool? absent = null)
        => bool.TryParse(Scalar(root, name, absent?.ToString()), out bool value) ? value : throw Invalid();
    private static XElement? One(XElement root, string name)
    {
        var nodes = root.Elements().Where(node => node.Name.LocalName == name).ToArray();
        if (nodes.Length > 1 || nodes.Length == 1 && nodes[0].Name != name) throw Invalid();
        return nodes.SingleOrDefault();
    }
    private static string Scalar(XElement root, string name, string? absent = null)
    {
        var node = One(root, name);
        if (node is null) return absent ?? throw Invalid();
        if (node.HasAttributes || node.Nodes().Any(child => child.GetType() != typeof(XText))) throw Invalid();
        return node.Value;
    }
    private static InvalidDataException Invalid() => new(Blocker);
}

internal sealed partial class CharacterCreationSelectedEffectsConsumer
{
    private CharacterMatrixInitiativeDiceProjection? matrixInitiative;
    internal CharacterMatrixInitiativeDiceProjection? MatrixInitiative(CharacterCreationCompleteAssessment authority)
        => DigestFor(authority) is not null && matrixInitiative is { } result
            && result.GraphDigest == CharacterCreationFinalizationDigest.Compute(
                compiledGraph.ToString(SaveOptions.DisableFormatting)) ? result : null;
}
