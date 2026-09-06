using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Deterministically composes the projectable SR5 Priority draft graph.  It is
/// intentionally conservative: source nodes whose complete legacy bonus/effect
/// payload is not present in the typed draft fail closed rather than producing a
/// superficially valid but mechanically incomplete Chummer5 document.
/// </summary>
public static class CharacterCreationFinalizationProjector
{
    public static bool TryProject(
        WorkspaceStoredDocument workspace,
        out string characterXml,
        out CharacterCreationFinalizationDelta[] deltas,
        out string[] sourceAnchorIds,
        out decimal karmaRemaining,
        out decimal startingNuyen,
        out decimal nuyenRemaining,
        out string[] blockers)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        characterXml = string.Empty;
        deltas = [];
        sourceAnchorIds = [];
        karmaRemaining = 0;
        startingNuyen = 0;
        nuyenRemaining = 0;
        var failures = new List<string>();
        WorkspaceDocumentAuxiliaryState auxiliary = workspace.Document.AuxiliaryState;
        CharacterCreationPrerequisiteDraft? prerequisite = auxiliary.CharacterCreationPrerequisiteDraft;
        CharacterCreationAttributesDraft? attributes = auxiliary.CharacterCreationAttributesDraft;
        CharacterCreationSkillsDraft? skills = auxiliary.CharacterCreationSkillsDraft;
        CharacterCreationMagicResonanceDraft? magic = auxiliary.CharacterCreationMagicResonanceDraft;
        CharacterCreationQualitiesDraft? qualities = auxiliary.CharacterCreationQualitiesDraft;
        CharacterCreationResourcesDraft? resources = auxiliary.CharacterCreationResourcesDraft;
        CharacterCreationGearDraft? gear = auxiliary.CharacterCreationGearDraft;

        // Foundation is a Life Modules rules draft, not Priority biography.
        // It cannot arise through normal Priority confirmation, but a stale or
        // foreign auxiliary draft must never be silently cleared by this
        // whole-build transaction, nor interpreted as extra Priority bonuses.
        if (auxiliary.CharacterCreationFoundationDraft is not null)
            failures.Add(CharacterCreationFinalizationBlockers.FoundationDraftNotApplicable);

        if (prerequisite is null)
            failures.Add(CharacterCreationFinalizationBlockers.PrerequisiteDraftRequired);
        if (attributes is null)
            failures.Add(CharacterCreationFinalizationBlockers.AttributesDraftRequired);
        if (skills is null)
            failures.Add(CharacterCreationFinalizationBlockers.SkillsDraftRequired);
        bool magicRequired = !IsMundaneTalent(prerequisite);
        if (magic is null && magicRequired)
            failures.Add(CharacterCreationFinalizationBlockers.MagicResonanceDraftRequired);
        if (qualities is null)
            failures.Add(CharacterCreationFinalizationBlockers.QualitiesDraftRequired);
        if (resources is null)
            failures.Add(CharacterCreationFinalizationBlockers.ResourcesDraftRequired);
        if (gear is null)
            failures.Add(CharacterCreationFinalizationBlockers.GearDraftRequired);
        if (failures.Count != 0)
        {
            blockers = Normalize(failures);
            return false;
        }

        if (!string.Equals(prerequisite!.BuildMethod, CharacterCreationBuildMethods.Priority,
                StringComparison.Ordinal))
            failures.Add(CharacterCreationFinalizationBlockers.BuildMethodNotReady);
        if (prerequisite.HeritageSelection is null || prerequisite.TalentSelection is null)
            failures.Add(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        if (magic is not null
            && (!string.Equals(magic.TalentKind, CharacterCreationMagicResonanceKinds.Mundane,
                    StringComparison.Ordinal)
                || magic.Selections.Tradition is not null
                || magic.Selections.Stream is not null
                || magic.Selections.AdeptPowers.Count != 0
                || magic.Selections.Spells.Count != 0
                || magic.Selections.ComplexForms.Count != 0))
            failures.Add(CharacterCreationFinalizationBlockers.AwakenedEffectsNotProjectable);
        if (prerequisite.TalentSelection?.GrantPlan is
            { ActiveSkills: { Count: > 0 } } or { SkillGroups: { Count: > 0 } })
            failures.Add(CharacterCreationFinalizationBlockers.TalentGrantsNotProjectable);

        karmaRemaining = checked(qualities!.KarmaRemaining - resources!.KarmaInvestment);
        if (karmaRemaining < 0)
            failures.Add(CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);
        startingNuyen = resources.FinalizationContribution.StartingNuyen;
        nuyenRemaining = gear!.Budget.RemainingNuyen;
        if (failures.Count != 0)
        {
            blockers = Normalize(failures);
            return false;
        }

        try
        {
            XDocument document = XDocument.Parse(workspace.Document.Content, LoadOptions.None);
            XElement root = document.Root
                ?? throw new InvalidDataException("Character root is missing.");
            if (root.Name != "character"
                || ReadDirect(root, "created") is not string createdText
                || !bool.TryParse(createdText, out bool created)
                || created)
            {
                throw new InvalidDataException("Character is not an uncreated Chummer document.");
            }
            EnsureEmptyOwnedContainer(root, "qualities");
            EnsureEmptyOwnedContainer(root, "gears");

            CharacterCreationPriorityHeritageSelection heritage = prerequisite.HeritageSelection!;
            var projected = new List<CharacterCreationFinalizationDelta>();
            int order = 0;
            AddDelta(projected, ref order, "lifecycle:created",
                CharacterCreationFinalizationDeltaKinds.Lifecycle, "created", "False", "True", 0, 0,
                prerequisite.SourceAnchorIds);
            AddDelta(projected, ref order, "metatype:selected",
                CharacterCreationFinalizationDeltaKinds.Metatype, heritage.MetatypeSourceId,
                ReadDirect(root, "metatype"), heritage.MetatypeName, heritage.KarmaCost, 0,
                heritage.SourceAnchorIds);

            SetDirect(root, "metatype", heritage.MetatypeName);
            SetDirect(root, "metavariant", heritage.MetavariantName ?? string.Empty);
            SetDirect(root, "metatypebp", heritage.KarmaCost.ToString(CultureInfo.InvariantCulture));
            SetDirect(root, "buildmethod", prerequisite.BuildMethod);
            foreach (CharacterCreationPriorityAssignment assignment in prerequisite.Assignments
                         .OrderBy(static assignment => assignment.Order))
            {
                string legacyElement = assignment.CategoryId switch
                {
                    CharacterCreationPriorityCategoryIds.Heritage => "prioritymetatype",
                    CharacterCreationPriorityCategoryIds.Talent => "priorityspecial",
                    CharacterCreationPriorityCategoryIds.Attributes => "priorityattributes",
                    CharacterCreationPriorityCategoryIds.Skills => "priorityskills",
                    CharacterCreationPriorityCategoryIds.Resources => "priorityresources",
                    _ => throw new InvalidDataException("Unknown Priority category.")
                };
                string value = string.Create(CultureInfo.InvariantCulture,
                    $"{assignment.Rank},{assignment.SumToTenValue}");
                SetDirect(root, legacyElement, value);
                AddDelta(projected, ref order, $"priority:{assignment.CategoryId}",
                    CharacterCreationFinalizationDeltaKinds.Build, assignment.CategoryId,
                    null, value, 0, 0, assignment.SourceAnchorIds);
            }
            SetDirect(root, "prioritytalent", prerequisite.TalentSelection!.Value);
            SetDirect(root, "sumtoten", prerequisite.SumToTenTarget.GetValueOrDefault()
                .ToString(CultureInfo.InvariantCulture));

            ReplaceDirect(root, BuildAttributes(attributes!, projected, ref order));
            ReplaceDirect(root, BuildSkills(skills!, projected, ref order));
            SetDirect(root, "magenabled", "False");
            SetDirect(root, "resenabled", "False");
            SetDirect(root, "depenabled", "False");
            SetDirect(root, "adept", "False");
            SetDirect(root, "magician", "False");
            SetDirect(root, "technomancer", "False");
            SetDirect(root, "ai", "False");
            BuildQualityAndEffectGraph(
                root,
                qualities,
                projected,
                ref order);
            ReplaceDirect(root, BuildGearGraph(gear, projected, ref order));
            SetDirect(root, "karma", karmaRemaining.ToString(CultureInfo.InvariantCulture));
            SetDirect(root, "nuyen", nuyenRemaining.ToString(CultureInfo.InvariantCulture));
            SetDirect(root, "startingnuyen", startingNuyen.ToString(CultureInfo.InvariantCulture));
            SetDirect(root, "nuyenbp", resources.KarmaInvestment.ToString(CultureInfo.InvariantCulture));
            SetDirect(root, "created", "True");
            root.Elements(CharacterCreationBootstrapXml.MarkerElement).Remove();

            AddDelta(projected, ref order, "resources:starting-nuyen",
                CharacterCreationFinalizationDeltaKinds.Resources, "startingnuyen", null,
                startingNuyen.ToString(CultureInfo.InvariantCulture),
                resources.KarmaInvestment, 0, resources.SourceAnchorIds);
            AddDelta(projected, ref order, "resources:remaining-nuyen",
                CharacterCreationFinalizationDeltaKinds.Resources, "nuyen", null,
                nuyenRemaining.ToString(CultureInfo.InvariantCulture),
                0, gear.Budget.BasketCost, gear.FinalizationContribution.SourceAnchorIds);

            characterXml = document.ToString(SaveOptions.DisableFormatting);
            deltas = projected.OrderBy(static delta => delta.Order).ToArray();
            sourceAnchorIds = prerequisite.SourceAnchorIds
                .Concat(attributes!.SourceAnchorIds)
                .Concat(skills!.SourceAnchorIds)
                .Concat(magic?.SourceAnchorIds ?? [])
                .Concat(qualities.SourceAnchorIds)
                .Concat(resources.SourceAnchorIds)
                .Concat(gear.FinalizationContribution.SourceAnchorIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static anchor => anchor, StringComparer.Ordinal)
                .ToArray();
            blockers = [];
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          or InvalidOperationException
                                          or XmlException)
        {
            blockers = [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid];
            characterXml = string.Empty;
            deltas = [];
            sourceAnchorIds = [];
            return false;
        }
    }

    public static string ComputeRawCharacterXmlDigest(string characterXml) =>
        "sha256:" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(characterXml ?? string.Empty)));

    public static bool IsMundaneTalent(CharacterCreationPrerequisiteDraft? prerequisite) =>
        prerequisite?.TalentSelection is { } talent
        && string.Equals(talent.Value, CharacterCreationMagicResonanceKinds.Mundane,
            StringComparison.OrdinalIgnoreCase)
        && talent.Magic is null
        && talent.Resonance is null
        && talent.Depth is null;

    private static XElement BuildAttributes(
        CharacterCreationAttributesDraft draft,
        ICollection<CharacterCreationFinalizationDelta> deltas,
        ref int order)
    {
        var container = new XElement("attributes");
        foreach (CharacterCreationAttributeProjection attribute in draft.Attributes
                     .OrderBy(static item => AttributeOrder(item.AttributeId))
                     .ThenBy(static item => item.AttributeId, StringComparer.Ordinal))
        {
            int baseValue = Math.Max(0, attribute.PriorityPointsSpent);
            var node = new XElement("attribute",
                new XElement("name", attribute.AttributeId),
                new XElement("metatypemin", attribute.Minimum),
                new XElement("metatypemax", attribute.Maximum),
                new XElement("metatypeaugmax", attribute.AugmentedMaximum),
                new XElement("base", baseValue),
                new XElement("karma", Math.Max(0, attribute.KarmaLevels)),
                new XElement("metatypecategory", "Standard"),
                new XElement("totalvalue", attribute.Current));
            container.Add(node);
            AddDelta(deltas, ref order, $"attribute:{attribute.AttributeId}",
                CharacterCreationFinalizationDeltaKinds.Attribute, attribute.AttributeId,
                null, attribute.Current.ToString(CultureInfo.InvariantCulture),
                attribute.KarmaCost, 0, attribute.SourceAnchorIds);
        }
        return container;
    }

    private static XElement BuildSkills(
        CharacterCreationSkillsDraft draft,
        ICollection<CharacterCreationFinalizationDelta> deltas,
        ref int order)
    {
        var active = new XElement("skills");
        var knowledge = new XElement("knoskills");
        foreach (CharacterCreationSkillProjection skill in draft.Skills
                     .OrderBy(static item => item.Kind, StringComparer.Ordinal)
                     .ThenBy(static item => item.SourceSkillId, StringComparer.Ordinal))
        {
            XElement node = BuildSkill(skill, draft.DraftDigest);
            (skill.Kind == CharacterCreationSkillKinds.Knowledge ? knowledge : active).Add(node);
            AddDelta(deltas, ref order, $"skill:{skill.Kind}:{skill.SourceSkillId}",
                CharacterCreationFinalizationDeltaKinds.Skill, skill.SourceSkillId,
                null, skill.IsNativeLanguage ? "native" : skill.EffectiveRating?.ToString(CultureInfo.InvariantCulture),
                0, 0, skill.SourceAnchorIds);
        }

        var groups = new XElement("groups");
        foreach (CharacterCreationSkillGroupProjection group in draft.SkillGroups
                     .OrderBy(static item => item.GroupId, StringComparer.Ordinal))
        {
            groups.Add(new XElement("group",
                new XElement("karma", 0),
                new XElement("base", group.Rating),
                new XElement("id", StableGuid($"group:{group.GroupId}:{draft.DraftDigest}")),
                new XElement("name", group.Name)));
            AddDelta(deltas, ref order, $"skill-group:{group.GroupId}",
                CharacterCreationFinalizationDeltaKinds.SkillGroup, group.GroupId,
                null, group.Rating.ToString(CultureInfo.InvariantCulture),
                0, 0, group.SourceAnchorIds);
        }
        return new XElement("newskills",
            new XElement("skillptsmax", draft.ActivePointTotal),
            new XElement("skillgrpsmax", draft.SkillGroupPointTotal),
            active,
            knowledge,
            new XElement("skilljackknowledgeskills"),
            groups);
    }

    private static void BuildQualityAndEffectGraph(
        XElement root,
        CharacterCreationQualitiesDraft draft,
        ICollection<CharacterCreationFinalizationDelta> deltas,
        ref int order)
    {
        var qualityContainer = new XElement("qualities");
        var improvements = new List<XElement>();
        foreach (CharacterCreationQualitySelection selection in draft.Selections
                     .OrderBy(static item => item.OptionId, StringComparer.Ordinal))
        {
            if (!CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
                    selection,
                    draft.DraftDigest,
                    out XElement[] projectedQualities,
                    out XElement[] projectedImprovements))
                throw new InvalidDataException("Selected Quality source cannot be projected exactly.");
            qualityContainer.Add(projectedQualities);
            improvements.AddRange(projectedImprovements);
            AddDelta(
                deltas,
                ref order,
                $"quality:{selection.OptionId}",
                CharacterCreationFinalizationDeltaKinds.Quality,
                selection.SourceId.ToString("D", CultureInfo.InvariantCulture),
                null,
                selection.Rating.ToString(CultureInfo.InvariantCulture),
                selection.KarmaCost,
                0,
                selection.SourceAnchorIds);
        }
        ReplaceDirect(root, qualityContainer);
        if (improvements.Count != 0)
        {
            XElement improvementContainer = GetOrCreateDirectContainer(root, "improvements");
            improvementContainer.Add(improvements);
        }
    }

    private static XElement BuildGearGraph(
        CharacterCreationGearDraft draft,
        ICollection<CharacterCreationFinalizationDelta> deltas,
        ref int order)
    {
        var container = new XElement("gears");
        foreach (CharacterCreationGearLine line in draft.Lines
                     .OrderBy(static item => item.OptionId, StringComparer.Ordinal))
        {
            if (!CharacterCreationLegacySourceProjector.TryBuildGear(
                    line,
                    draft.DraftDigest,
                    out XElement projected))
                throw new InvalidDataException("Selected Gear source cannot be projected exactly.");
            container.Add(projected);
            AddDelta(
                deltas,
                ref order,
                $"gear:{line.OptionId}",
                CharacterCreationFinalizationDeltaKinds.Gear,
                line.SourceId.ToString("D", CultureInfo.InvariantCulture),
                null,
                line.Quantity.ToString(CultureInfo.InvariantCulture),
                0,
                line.TotalCost,
                line.SourceAnchorIds);
        }
        return container;
    }

    private static XElement BuildSkill(CharacterCreationSkillProjection skill, string draftDigest)
    {
        var node = new XElement("skill",
            new XElement("guid", StableGuid($"skill:{skill.Kind}:{skill.SourceSkillId}:{draftDigest}")),
            new XElement("suid", Guid.TryParse(skill.SourceSkillId, out Guid sourceId)
                ? sourceId
                : Guid.Empty),
            new XElement("isknowledge", skill.Kind == CharacterCreationSkillKinds.Knowledge),
            new XElement("skillcategory", skill.Category),
            new XElement("karma", 0),
            new XElement("base", skill.IsNativeLanguage ? 0 : skill.EffectiveRating.GetValueOrDefault()),
            new XElement("notes"));
        if (skill.SpecializationName is not null)
        {
            node.Add(new XElement("specs",
                new XElement("spec",
                    new XElement("guid", StableGuid($"spec:{skill.SourceSkillId}:{skill.SpecializationOptionId}:{draftDigest}")),
                    new XElement("name", skill.SpecializationName),
                    new XElement("free", "False"))));
        }
        if (skill.Kind == CharacterCreationSkillKinds.Knowledge)
        {
            node.Add(new XElement("name", skill.Name));
            node.Add(new XElement("type", skill.Category));
            if (skill.IsNativeLanguage)
                node.Add(new XElement("isnativelanguage", "True"));
        }
        return node;
    }

    private static int AttributeOrder(string attributeId) => attributeId switch
    {
        "BOD" => 0, "AGI" => 1, "REA" => 2, "STR" => 3,
        "CHA" => 4, "INT" => 5, "LOG" => 6, "WIL" => 7,
        "EDG" => 8, "MAG" => 9, "MAGAdept" => 10, "RES" => 11,
        "DEP" => 12, "ESS" => 13, _ => 100
    };

    private static Guid StableGuid(string seed)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> guid = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guid);
        guid[7] = (byte)((guid[7] & 0x0f) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3f) | 0x80);
        return new Guid(guid);
    }

    private static string? ReadDirect(XElement root, string name)
    {
        XElement[] nodes = root.Elements(name).Take(2).ToArray();
        if (nodes.Length > 1)
            throw new InvalidDataException($"Duplicate {name} node.");
        return nodes.SingleOrDefault()?.Value.Trim();
    }

    private static void SetDirect(XElement root, string name, string value)
    {
        XElement[] nodes = root.Elements(name).Take(2).ToArray();
        if (nodes.Length > 1)
            throw new InvalidDataException($"Duplicate {name} node.");
        if (nodes.Length == 1)
            nodes[0].Value = value;
        else
            root.Add(new XElement(name, value));
    }

    private static void ReplaceDirect(XElement root, XElement replacement)
    {
        XElement[] nodes = root.Elements(replacement.Name).Take(2).ToArray();
        if (nodes.Length > 1)
            throw new InvalidDataException($"Duplicate {replacement.Name} node.");
        if (nodes.Length == 1)
            nodes[0].ReplaceWith(replacement);
        else
            root.Add(replacement);
    }

    private static XElement GetOrCreateDirectContainer(XElement root, string name)
    {
        XElement[] nodes = root.Elements(name).Take(2).ToArray();
        if (nodes.Length > 1)
            throw new InvalidDataException($"Duplicate {name} node.");
        if (nodes.Length == 1)
            return nodes[0];
        var created = new XElement(name);
        root.Add(created);
        return created;
    }

    private static void EnsureEmptyOwnedContainer(XElement root, string name)
    {
        XElement[] nodes = root.Elements(name).Take(2).ToArray();
        if (nodes.Length > 1 || nodes.SingleOrDefault()?.Elements().Any() == true)
            throw new InvalidDataException($"Existing {name} graph is not draft-owned.");
    }

    private static void AddDelta(
        ICollection<CharacterCreationFinalizationDelta> deltas,
        ref int order,
        string deltaId,
        string kind,
        string targetId,
        string? before,
        string? after,
        decimal karmaCost,
        decimal nuyenCost,
        IReadOnlyList<string> anchors)
    {
        deltas.Add(new CharacterCreationFinalizationDelta(
            ++order,
            deltaId,
            kind,
            targetId,
            before,
            after,
            karmaCost,
            nuyenCost,
            anchors.Distinct(StringComparer.Ordinal).OrderBy(static item => item, StringComparer.Ordinal).ToArray()));
    }

    private static string[] Normalize(IEnumerable<string> blockers) => blockers
        .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static blocker => blocker, StringComparer.Ordinal)
        .ToArray();
}
