using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class Mechanics695Execution
{
    // This is private execution state. The outer retained-source consumer binds
    // its graph, selections, policy, owner observation and full inputs separately.
    internal sealed class Extension711
    {
        private readonly IReadOnlyDictionary<int, EffectResult> effects;
        internal string SourceState { get; }
        internal object Results => effects.OrderBy(row => row.Key).Select(row => row.Value).ToArray();
        internal Extension711(Dictionary<int, EffectResult> effects, string sourceState)
        { this.effects = new Dictionary<int, EffectResult>(effects); SourceState = sourceState; }
        internal bool TryGet(int index, XElement node, out object? result)
        {
            result = null;
            if (!effects.TryGetValue(index, out var effect)
                || effect.NodeDigest != NodeDigest711(node)
                || effect.Type != Scalar(node, "improvementttype")) return false;
            result = effect.Result;
            return true;
        }
    }

    internal static bool IsExtensionType711(string type) => type is "Overclocker"
        or "LimitSpellDescriptor" or "BlockSpellDescriptor" or "LimitSpellCategory" or "LimitSpiritCategory";

    internal static bool TryExecute711(XElement graph, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationAttributeSkillsPreview skills, CharacterCreationMagicResonanceCompletionProjection magic,
        CharacterCreationQualitiesCompletionEvaluation qualities, string seed, ICharacterSourceDataContext context,
        out Extension711? execution)
    {
        execution = null;
        try
        {
            var effects = Rows(graph, "improvements", "improvement");
            var selected = effects.Select((node, index) => (node, index))
                .Where(row => IsExtensionType711(Scalar(row.node, "improvementttype"))).ToArray();
            if (selected.Length == 0)
            { execution = new(new(), CharacterCreationFinalizationDigest.Compute(Array.Empty<string>())); return true; }
            if (selected.Length > 4096) return false;
            foreach (var (node, _) in selected)
            {
                ValidateImprovement(node);
                if (Scalar(node, "improvementsource") != "Quality") return false;
                if (Scalar(node, "improvementttype") == "Overclocker") Marker(node);
                else MarkerTarget(node);
            }

            // Recompile paid occurrences, preserving their generated owners and order.
            var expected = new List<XElement>();
            foreach (var quality in qualities.Selections.OrderBy(row => row.OptionId, StringComparer.Ordinal))
            {
                if (!CharacterCreationLegacySourceProjector.TryBuildQualityGraph(quality, seed, out _, out var compiled))
                    return false;
                expected.AddRange(compiled.Where(node => IsExtensionType711(Scalar(node, "improvementttype"))));
            }
            var sources = new List<object>();
            foreach (var source in inputs.Magic.SelectedTalent.GrantedQualitySources ?? [])
            {
                if (!CharacterCreationTalentQualitySourceRules.IsValidSource(source)) return false;
                var definition = XElement.Parse(source.CanonicalSourceXml);
                if (definition.Element("bonus")?.Elements().Any(node =>
                        node.Name.LocalName is "limitspellcategory" or "limitspiritcategory"
                            or "blockspelldescriptor" or "limitspelldescriptor") != true) continue;
                var owners = Rows(graph, "qualities", "quality").Where(node => Scalar(node, "sourceid") == source.SourceId).ToArray();
                if (owners.Length != 1) return false;
                string owner = Scalar(owners[0], "guid");
                CharacterCreationTalentRestrictionPlan? plan = null;
                if (CharacterCreationTalentRestrictionAuthority.HasPairedPrompts(source))
                {
                    if (!context.TryResolveCreationKarmaTalents(out var catalog) || catalog is null) return false;
                    var restrictions = catalog.Options.Select(row => row.Restrictions)
                        .SingleOrDefault(row => row?.QualitySourceId == source.SourceId);
                    var owned = selected.Where(row => Scalar(row.node, "sourcename") == owner).Select(row => row.node).ToArray();
                    var category = owned.SingleOrDefault(row => Scalar(row, "improvementttype") == "LimitSpellCategory");
                    var spirit = owned.SingleOrDefault(row => Scalar(row, "improvementttype") == "LimitSpiritCategory");
                    var spiritSource = restrictions?.SpiritCategories.SingleOrDefault(row =>
                        spirit is not null && row.Name == Scalar(spirit, "improvedname"));
                    if (restrictions is null || category is null || spiritSource is null
                        || !CharacterCreationTalentRestrictionAuthority.TryChoose(source, restrictions,
                            new(Scalar(category, "improvedname"), spiritSource.SourceId), out plan)) return false;
                    sources.Add(new { catalog.AuthorityDigest, Restrictions = restrictions, Plan = plan });
                }
                var compiled = new List<XElement>();
                CharacterCreationAwakenedLegacyProjector.CompileBonus(definition.Element("bonus"),
                    source.ForcedSelection,
                    inputs.Magic.PrerequisiteDraft.TalentSelection?.GrantPlan?.SkillGroups.Select(row => row.CanonicalName).ToArray() ?? [],
                    source, owner, [], compiled, [], plan);
                expected.AddRange(compiled.Where(node => IsExtensionType711(Scalar(node, "improvementttype"))));
                sources.Add(source);
            }
            // An arbitrary improvement in an admitted original must not borrow
            // another quality's execution, including one with an identical target.
            var remaining = expected.ToList();
            foreach (var (node, _) in selected)
            {
                int match = remaining.FindIndex(candidate => XNode.DeepEquals(candidate, node));
                if (match < 0) return false;
                remaining.RemoveAt(match);
            }
            if (remaining.Count != 0) return false;

            var result = new Dictionary<int, EffectResult>();
            var active = selected.Where(row => Integer(row.node, "enabled") == 1).ToArray();
            if (selected.Any(row => Scalar(row.node, "improvementttype") == "Overclocker"))
            {
                if (!TryMatrix711(graph, skills.Attributes.Attributes.ToDictionary(row => row.AttributeId, row => row.Current,
                        StringComparer.Ordinal), active.Any(row => Scalar(row.node, "improvementttype") == "Overclocker"),
                        out var matrix)) return false;
                foreach (var (node, index) in selected.Where(row => Scalar(row.node, "improvementttype") == "Overclocker"))
                    Add(index, node, new { Enabled = Integer(node, "enabled") == 1, Matrix = matrix });
            }
            if (selected.Any(row => Scalar(row.node, "improvementttype") != "Overclocker"))
            {
                if (!context.TryResolveCreationMagicResonanceAuthority(out var current)
                    || !Same711(current, inputs.Magic.Authority)) return false;
                sources.Add(current);
                if (!TrySpellChoices711(current.Spells, magic.Selections.Spells, active.Select(row => row.node).ToArray(),
                        out var spells)) return false;
                // The selected graph may not smuggle an original spell outside
                // the selections just checked against retained effective sources.
                if (!Rows(graph, "spells", "spell").Select(row => Scalar(row, "sourceid")).Order(StringComparer.Ordinal)
                    .SequenceEqual(magic.Selections.Spells.Select(row => row.SourceId).Order(StringComparer.Ordinal))) return false;
                object? spirits = null;
                if (selected.Any(row => Scalar(row.node, "improvementttype") == "LimitSpiritCategory"))
                {
                    var savedChoices = new List<CharacterSpiritNameChoiceState>();
                    if (magic.Selections.Tradition is not { } tradition
                        || !context.TryResolveSpiritCatalogNames("Spirit", out var catalog)
                        || !context.TryResolveTraditionSpiritNames("Spirit", tradition.SourceId, out var available)
                        || !TrySpiritChoices711(catalog, available, active.Select(row => row.node).ToArray(),
                            Rows(graph, "spirits", "spirit"), out var decisions, savedChoices.Add)) return false;
                    spirits = new { Tradition = tradition, Catalog = catalog, TraditionNames = available,
                        Choices = decisions, SavedChoices = savedChoices };
                    sources.Add(spirits);
                }
                foreach (var (node, index) in selected.Where(row => Scalar(row.node, "improvementttype") != "Overclocker"))
                    Add(index, node, new { Enabled = Integer(node, "enabled") == 1, Spells = spells, Spirits = spirits });
            }
            execution = new(result, CharacterCreationFinalizationDigest.Compute(sources));
            return true;

            void Add(int index, XElement node, object value)
                => result.Add(index, new(index, Scalar(node, "improvementttype"), NodeDigest711(node), "mechanics711", value));
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException
            or OverflowException or FormatException or System.Xml.XmlException) { return false; }
    }

    internal sealed record SpellChoice711(CharacterCreationMagicResonanceOptionIdentity Identity,
        string SourceNodeDigest, string Category, string Descriptor, bool Selected, bool Allowed,
        IReadOnlyList<string> RejectedBy);

    internal static bool TrySpellChoices711(IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> catalog,
        IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> selected, IReadOnlyList<XElement> effects,
        out SpellChoice711[] choices)
    {
        choices = [];
        if (!ValidateActiveEffects711(effects) || catalog.Count is 0 or > 4096
            || (long)catalog.Count * Math.Max(1, effects.Count) > 65_536
            || catalog.Sum(row => (long)row.CanonicalSourceXml.Length) > 8 * 1024 * 1024
            || catalog.Select(row => row.Identity).Distinct().Count() != catalog.Count
            || selected.Distinct().Count() != selected.Count
            || selected.Any(id => !catalog.Any(row => row.Identity == id))) return false;
        var limits = effects.Where(row => Scalar(row, "improvementttype") == "LimitSpellDescriptor").ToArray();
        var blocks = effects.Where(row => Scalar(row, "improvementttype") == "BlockSpellDescriptor").ToArray();
        var categories = effects.Where(row => Scalar(row, "improvementttype") == "LimitSpellCategory").ToArray();
        var results = new List<SpellChoice711>();
        foreach (var option in catalog)
        {
            if (!CharacterCreationMagicResonanceFinalizationRules.HasValidOptionPayload(option)) return false;
            var source = XElement.Parse(option.CanonicalSourceXml);
            string category = Scalar(source, "category");
            string descriptor = Optional711(source, "descriptor");
            if (category != option.Category) return false;
            var rejected = new List<string>();
            // SelectSpell: descriptor limits are OR, blocks are OR, category
            // restrictions are AND. Do not silently implement allow overrides.
            if (limits.Length != 0 && !limits.Any(row => descriptor.Contains(Scalar(row, "improvedname"), StringComparison.Ordinal)))
                rejected.AddRange(limits.Select(NodeDigest711));
            rejected.AddRange(blocks.Where(row => descriptor.Contains(Scalar(row, "improvedname"), StringComparison.Ordinal)).Select(NodeDigest711));
            rejected.AddRange(categories.Where(row => Scalar(row, "improvedname") != category).Select(NodeDigest711));
            bool allowed = option.IsEnabled && option.Blockers.Count == 0 && rejected.Count == 0;
            bool chosen = selected.Contains(option.Identity);
            results.Add(new(option.Identity, option.SourceNodeDigest, category, descriptor, chosen, allowed, rejected));
            if (chosen && !allowed) return false;
        }
        choices = results.ToArray();
        return true;
    }

    internal sealed record SpiritChoice711(string Name, bool InTradition, bool Allowed);
    internal static bool TrySpiritChoices711(IReadOnlyList<string> catalog, IReadOnlyList<string> tradition,
        IReadOnlyList<XElement> effects, IReadOnlyList<XElement> saved, out SpiritChoice711[] choices,
        Action<CharacterSpiritNameChoiceState>? observe = null)
    {
        choices = [];
        if (!ValidateActiveEffects711(effects) || catalog.Count is 0 or > 512
            || tradition.Count is 0 or > 512 || catalog.Any(string.IsNullOrWhiteSpace)
            || catalog.Distinct(StringComparer.Ordinal).Count() != catalog.Count
            || tradition.Distinct(StringComparer.Ordinal).Count() != tradition.Count
            || tradition.Contains("All", StringComparer.Ordinal) && tradition.Count != 1) return false;
        IReadOnlyList<string> baseNames = tradition.Count == 1 && tradition[0] == "All" ? catalog : tradition;
        if (baseNames.Any(name => !catalog.Contains(name, StringComparer.Ordinal))) return false;
        var limits = effects.Where(row => Scalar(row, "improvementttype") == "LimitSpiritCategory")
            .Select(row => Scalar(row, "improvedname")).ToHashSet(StringComparer.Ordinal);
        if (limits.Any(name => !catalog.Contains(name, StringComparer.Ordinal))) return false;
        choices = catalog.Select(name => new SpiritChoice711(name, baseNames.Contains(name, StringComparer.Ordinal),
            baseNames.Contains(name, StringComparer.Ordinal) && (limits.Count == 0 || limits.Contains(name)))).ToArray();
        var ids = new List<Guid>();
        foreach (var spirit in saved)
            if (!Guid.TryParseExact(Optional711(spirit, "guid"), "D", out var id) || id == Guid.Empty || ids.Contains(id))
                return false;
            else ids.Add(id);
        var canonical = new List<CharacterSpiritNameChoiceState>();
        foreach (var spirit in saved)
        {
            if (Scalar(spirit, "type") != "Spirit"
                || !CharacterSpiritNameChoiceRules.TryProject(Guid.Parse(Scalar(spirit, "guid")), ids, "Spirit",
                    Scalar(spirit, "name"), baseNames, limits.ToArray(), false, false, [], [], out var state)
                || !CharacterSpiritNameChoiceRules.CanSet(state!, Scalar(spirit, "name"))
                || !choices.Where(row => row.Allowed).Select(row => row.Name).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(state!.AllowedNames))
                return false;
            canonical.Add(state!);
        }
        foreach (var state in canonical) observe?.Invoke(state);
        return true;
    }

    private static string Optional711(XElement node, string name)
        => node.Element(name) is null ? string.Empty : Scalar(node, name);
    private static bool ValidateActiveEffects711(IReadOnlyList<XElement> effects)
    {
        try
        {
            if (effects.Count > 4096) return false;
            foreach (var effect in effects)
            {
                ValidateImprovement(effect);
                if (!IsExtensionType711(Scalar(effect, "improvementttype")) || Integer(effect, "enabled") != 1)
                    return false;
                if (Scalar(effect, "improvementttype") == "Overclocker") Marker(effect);
                else MarkerTarget(effect);
            }
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException
            or InvalidOperationException or ArgumentException) { return false; }
    }
    private static string NodeDigest711(XElement node)
        => CharacterCreationFinalizationDigest.ComputeUtf8(node.ToString(SaveOptions.DisableFormatting));
    private static bool Same711<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
}
