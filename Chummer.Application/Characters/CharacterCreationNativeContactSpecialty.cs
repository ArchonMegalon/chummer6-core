using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCreationCompletionFinalizationProjector;

namespace Chummer.Application.Characters;

internal static class CharacterCreationNativeContactSpecialty
{
    internal static CharacterCreationNativeContactSpecialtyPolicy? ReadPolicy(
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationPrerequisiteDraft draft)
    {
        if (!CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(source)
            || source!.SettingsProfileId != draft.SettingsProfileId
            || draft.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)) return null;
        try
        {
            var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
            if (profile.Name != "setting" || profile.HasAttributes) return null;
            var values = CharacterCreationNativeContactSpecialtyDefinitions.All
                .Select(row => OptionalBoolean(profile, row.Element)).ToArray();
            if (values.All(value => value is null)) return null;
            // These are dependencies, not extra discharged controls.
            if (values[0].HasValue != values[1].HasValue) return null;
            int? enemy = values[0] == true ? Integer(Container(profile, "karmacost"), "karmaenemy") : null;
            // CharacterSettings and CharacterCreationSkillGroupPolicyAuthority retain true
            // when this dependency is absent. Present malformed values still fail closed.
            bool? breaks = values[3].HasValue
                ? OptionalBoolean(profile, "specializationsbreakskillgroups") ?? true : null;
            return new(values[0], values[1], values[2], values[3], values[4], enemy, breaks,
                source.SettingsProfileId, CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(
                    source.SettingsInputsDigest, source.SettingsProfileId), Digest(profile));
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or FormatException or OverflowException or ArgumentException or InvalidOperationException) { return null; }
    }

    internal static CharacterCreationNativeContactSpecialtyProjection? Evaluate(XElement graph,
        string? schema, AwakenedGraphInputs awakened, CharacterCreationPrerequisiteDraft draft,
        CharacterCreationAttributeSkillsPreview skills)
    {
        if (schema is null) return null;
        if (schema != CharacterCreationCompletionReviewSchemas.LifecycleResultV1) throw Invalid();
        var source = CharacterCreationAstralInitiative1249.Source(awakened);
        if (source is null) throw Invalid();
        var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        if (!profile.Elements().Any(node => CharacterCreationNativeContactSpecialtyDefinitions.All.Any(row => row.Element == node.Name.LocalName))) return null;
        return Evaluate(graph, source, draft, skills) ?? throw Invalid();
    }

    internal static CharacterCreationNativeContactSpecialtyProjection? Evaluate(XElement graph,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationPrerequisiteDraft draft,
        CharacterCreationAttributeSkillsPreview skills)
    {
        try
        {
            if (ReadPolicy(source, draft) is not { } policy || graph.Name != "character" || graph.HasAttributes
                || Leaf(graph, "gameedition").Value != "SR5" || skills.Blockers.Count != 0) return null;
            bool created = Boolean(graph, "created");
            CharacterCreationEnemyTrackingResult? enemies = null;
            if (policy.EnableEnemyTracking is bool tracking && policy.EnemyKarmaQualityLimit is bool qualityLimit)
            {
                var rows = new List<(int Connection, int Loyalty)>();
                if (tracking && policy.KarmaEnemy > 0)
                    foreach (var contact in Rows(graph, "contacts", "contact"))
                    {
                        string type = Leaf(contact, "type").Value;
                        if (type != "Contact" && type != "Enemy") return null;
                        if (type != "Enemy") continue;
                        // Current creation selection emits Contact, never Enemy. Do not admit
                        // a nonzero rebate into the existing contacts/quality budget by projection.
                        // Such a candidate needs a source-backed enemy selection/budget consumer.
                        return null;
                    }
                enemies = CharacterCreationNativeContactSpecialtyRules.Enemies(tracking, qualityLimit, policy.KarmaEnemy, rows);
            }
            CharacterCareerReputationProjection? reputation = null;
            if (policy.UseCalculatedPublicAwareness is bool calculated)
            {
                if (!CharacterCareerReputationProjector.TryEvaluateGraph(graph, new(calculated),
                    created ? CharacterCareerReputationProjector.CharacterReputationPhase.Career
                            : CharacterCareerReputationProjector.CharacterReputationPhase.Creation,
                    out var result, out _)) return null;
                reputation = result!.Reputation;
            }
            CharacterCreationSpecializationFundingResult[]? funding = null;
            if (policy.AllowPointBuySpecializationsOnKarmaSkills is bool allow)
            {
                var saved = Container(graph, "newskills");
                var nodes = Rows(saved, "skills", "skill").Concat(Rows(saved, "knoskills", "skill")).ToArray();
                if (nodes.Length != skills.Skills.Count) return null;
                funding = skills.Skills.Select(skill =>
                {
                    var node = nodes.Single(row => Leaf(row, "suid").Value == skill.SourceSkillId
                        && Boolean(row, "isknowledge") == (skill.Kind == CharacterCreationSkillKinds.Knowledge));
                    int personal = skill.IsNativeLanguage ? 0 : checked(skill.EffectiveRating!.Value
                        - skill.GrantedRating - skill.GroupRating - skill.KarmaLevels);
                    if (!skill.IsEnabled || skill.Blockers.Count != 0 || Integer(node, "base") != personal
                        || Integer(node, "karma") != skill.KarmaLevels) throw Invalid();
                    var specs = OptionalRows(node, "specs", "spec");
                    bool has = skill.SpecializationName is not null;
                    if (specs.Length != (has ? 1 : 0) || has && (Leaf(specs[0], "name").Value != skill.SpecializationName
                        || Boolean(specs[0], "free"))) throw Invalid();
                    // BuildSkill omits false BuyWithKarma; the native field also initializes false.
                    bool paid = OptionalBoolean(node, "buywithkarma") ?? false;
                    if (paid != skill.KarmaSpecialization) throw Invalid();
                    bool forced = CharacterCreationNativeContactSpecialtyRules.ForcedBuyWithKarma(false, has,
                        !skill.IsNativeLanguage && skill.EffectiveRating > 0, skill.KarmaLevels, personal,
                        skill.GrantedRating, skill.GroupRating, allow, policy.SpecializationsBreakSkillGroups!.Value);
                    if (forced && !paid) throw Invalid();
                    return new CharacterCreationSpecializationFundingResult(skill.Kind, skill.SourceSkillId,
                        has, skill.KarmaLevels, personal, skill.GrantedRating, skill.GroupRating, forced, paid);
                }).ToArray();
            }
            CharacterCreationMartialSpecializationBranch? martial = null;
            if (policy.FreeMartialArtSpecialization is bool freeMartial)
            {
                var arts = OptionalRows(graph, "martialarts", "martialart");
                var improvements = Rows(graph, "improvements", "improvement");
                // addskillspecializationoption checks the producer's ImprovementSource.MartialArt,
                // not a skill name, option string, or guessed combat capability.
                var fromMartialArt = improvements.Where(row => Leaf(row, "improvementsource").Value == "MartialArt").ToArray();
                int options = fromMartialArt.Count(row => Leaf(row, "improvementttype").Value == "SkillSpecializationOption");
                // The source compiler has no admitted creation MartialArt option producer.
                // False takes the real native branch without adding a specialization.
                // True with an actual option needs that producer and its paired installation;
                // a retained option cannot become an executed effect through this bridge.
                if (freeMartial && options != 0) return null;
                martial = new(freeMartial, arts.Length, options, freeMartial, false, "SkillSpecialization");
            }
            return new(policy, created, Digest(graph), enemies, reputation, funding, martial);
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or FormatException or OverflowException or ArgumentException or InvalidOperationException) { return null; }
    }

    private static string Digest(XElement value) => CharacterCreationFinalizationDigest.Compute(value.ToString(SaveOptions.DisableFormatting));
    private static InvalidDataException Invalid() => new("Unresolved native contacts/specialization input.");
    private static XElement Leaf(XElement parent, string name)
    {
        if (parent.Elements().Where(node => node.Name.LocalName == name).ToArray() is not [var leaf]
            || leaf.Name != name || leaf.HasAttributes || leaf.Nodes().ToArray() is not [XText text]
            || text.GetType() != typeof(XText)) throw Invalid();
        return leaf;
    }
    private static XElement Container(XElement parent, string name)
    {
        if (parent.Elements().Where(node => node.Name.LocalName == name).ToArray() is not [var node]
            || node.Name != name || node.HasAttributes
            || node.Nodes().Any(child => child is not XElement && (child is not XText text || text.GetType() != typeof(XText) || !string.IsNullOrWhiteSpace(text.Value)))) throw Invalid();
        return node;
    }
    private static XElement[] Rows(XElement parent, string container, string row)
    {
        var nodes = Container(parent, container).Elements().ToArray();
        if (nodes.Any(node => node.Name != row || node.HasAttributes)) throw Invalid();
        return nodes;
    }
    private static XElement[] OptionalRows(XElement parent, string container, string row)
        => parent.Elements().Any(node => node.Name.LocalName == container) ? Rows(parent, container, row) : [];
    private static bool Boolean(XElement parent, string name)
        => bool.TryParse(Leaf(parent, name).Value, out bool value) ? value : throw Invalid();
    private static bool? OptionalBoolean(XElement parent, string name)
        => parent.Elements().Any(node => node.Name.LocalName == name) ? Boolean(parent, name) : null;
    private static int Integer(XElement parent, string name)
        => int.TryParse(Leaf(parent, name).Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : throw Invalid();
}

internal sealed partial class CharacterCreationSelectedEffectsConsumer
{
    private CharacterCreationNativeContactSpecialtyProjection? nativeContactSpecialty;
    internal CharacterCreationNativeContactSpecialtyProjection? NativeContactSpecialty(CharacterCreationCompleteAssessment authority)
        => DigestFor(authority) is not null && nativeContactSpecialty is { } result
            && result.GraphDigest == CharacterCreationFinalizationDigest.Compute(compiledGraph.ToString(SaveOptions.DisableFormatting)) ? result : null;
}
