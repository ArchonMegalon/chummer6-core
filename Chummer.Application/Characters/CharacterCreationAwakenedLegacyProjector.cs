using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Interprets the confirmed source payload into legacy saved instances. It runs
/// only inside whole-build projection, never against a durable character directly.
/// Unknown bonuses and unprovided choices reject the entire projection.
/// </summary>
internal static class CharacterCreationAwakenedLegacyProjector
{
    internal static bool TryResolvePowerPointPurchase(CharacterCreationMagicResonanceDraft? magic,
        CharacterCreationAttributesDraft attributes, out CharacterCreationMysticAdeptPowerPointAllocation? allocation)
    {
        allocation = null;
        if (magic is null) return true;
        var contribution = magic.FinalizationContribution;
        if (contribution is null || magic.Selections is null) return false;
        if (contribution.MysticAdeptPowerPoints is not { } recorded)
            return magic.Selections.MysticAdeptPowerPoints == 0;
        try
        {
            Require(magic.TalentKind == CharacterCreationMagicResonanceKinds.MysticAdept);
            XElement source = Parse(contribution.Talent.CanonicalSourceXml, "talent");
            Require(int.TryParse(Scalar(source, "spells"), NumberStyles.None, CultureInfo.InvariantCulture, out int spells));
            var mag = attributes.Attributes.Single(item => item.AttributeId == "MAG");
            Require(CharacterCreationMysticAdeptPowerPointRules.TryEvaluate(recorded.Policy, magic.TalentKind,
                mag.Current, spells, magic.Selections.MysticAdeptPowerPoints, out allocation));
            Require(allocation is not null && Equal(CharacterCreationMagicResonanceDigest.Compute(allocation),
                    CharacterCreationMagicResonanceDigest.Compute(recorded))
                && magic.AdeptPowerPointBudget.Total == allocation!.PowerPoints
                && magic.SpellBudget.Total == allocation.SpellBudget
                && magic.Selections.Spells.Count == allocation.SpellBudget);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
            or XmlException or ArgumentException) { allocation = null; return false; }
    }

    internal static bool TryApply(XElement root, CharacterCreationPrerequisiteDraft prerequisite,
        CharacterCreationAttributesDraft attributes, CharacterCreationSkillsDraft skills,
        CharacterCreationMagicResonanceDraft? magic, ICollection<CharacterCreationFinalizationDelta> deltas,
        ref int order)
    {
        try
        {
            ApplySkillGrants(root, prerequisite, skills);
            if (magic is null) return CharacterCreationFinalizationProjector.IsMundaneTalent(prerequisite);
            var contribution = magic.FinalizationContribution;
            Require(contribution is not null && !magic.CharacterEffectsApplied
                && contribution.Spells is not null && contribution.AdeptPowers is not null && contribution.ComplexForms is not null
                && magic.Selections is not null && magic.Selections.Spells is not null
                && magic.Selections.AdeptPowers is not null && magic.Selections.ComplexForms is not null
                && Equal(magic.DraftDigest, CharacterCreationMagicResonanceDraftIntegrity.ComputeDigest(magic))
                && Equal(contribution!.ContributionDigest,
                    CharacterCreationMagicResonanceFinalizationRules.ComputeContributionDigest(contribution))
                && contribution.PrerequisiteDraftRevision == prerequisite.DraftRevision
                && Equal(contribution.PrerequisiteDraftDigest, prerequisite.DraftDigest)
                && contribution.AttributesDraftRevision == attributes.DraftRevision
                && Equal(contribution.AttributesDraftDigest, attributes.DraftDigest)
                && Equal(contribution.ExpectedRawCharacterXmlDigest, magic.BaseRawCharacterXmlDigest)
                && contribution.Talent.Identity == magic.TalentIdentity
                && contribution.Talent.Kind == magic.TalentKind
                && Equal(contribution.Talent.ProjectionDigest,
                    CharacterCreationMagicResonanceFinalizationRules.ComputeTalentProjectionDigest(contribution.Talent))
                && Equal(contribution.Talent.CanonicalSourceXmlDigest,
                    CharacterCreationMagicResonanceDigest.ComputeUtf8(contribution.Talent.CanonicalSourceXml))
                && CharacterCreationTalentQualitySourceRules.MatchesTalent(
                    contribution.Talent.CanonicalSourceXml, contribution.Talent.GrantedQualitySources));
            var source = contribution!;
            Require(TryResolvePowerPointPurchase(magic, attributes, out var purchasedPowerPoints));
            if (purchasedPowerPoints is not null)
            {
                Set(root, "magsplitadept", purchasedPowerPoints.PowerPoints.ToString(CultureInfo.InvariantCulture));
                Set(root, "magsplitmagician", "0");
                CharacterCreationFinalizationProjector.AddDelta(deltas, ref order, "mystic-adept:power-points",
                    CharacterCreationFinalizationDeltaKinds.MagicResonance, "magsplitadept", "0",
                    purchasedPowerPoints.PowerPoints.ToString(CultureInfo.InvariantCulture),
                    purchasedPowerPoints.KarmaCost, 0, purchasedPowerPoints.Policy.SourceAnchorIds);
            }
            Require(source.Talent.Identity.TalentSelectionId == prerequisite.TalentSelection!.SelectionId
                && source.Talent.Identity.TalentValue == prerequisite.TalentSelection.Value
                && Equal(source.Talent.SourceNodeDigest, prerequisite.TalentSelection.PriorityChildNodeDigest)
                && source.Talent.GrantedQualitySources!.Select(item => item.Reference)
                    .SequenceEqual(prerequisite.TalentSelection.GrantedQualities, StringComparer.Ordinal));
            var flags = new HashSet<string>(StringComparer.Ordinal);
            var projectedQualities = new List<XElement>();
            var improvements = new List<XElement>();
            var projectedGear = new List<(XElement Saved, CharacterCreationTalentGearSource Source)>();
            foreach (var quality in source.Talent.GrantedQualitySources!)
            {
                XElement definition = Parse(quality.CanonicalSourceXml, "quality");
                string id = CharacterCreationFinalizationProjector.StableGuid(
                    $"heritage-quality:{quality.SourceId}:{quality.ForcedSelection}:{quality.SourceNodeDigest}:{magic.DraftDigest}").ToString("D");
                string extra = CompileBonus(definition.Element("bonus"), quality.ForcedSelection,
                    prerequisite, quality, id, flags, improvements, projectedGear);
                Require(CharacterCreationLegacySourceProjector.TryBuildHeritageQualityInstance(quality, id, extra, out var saved));
                projectedQualities.Add(saved);
                CharacterCreationFinalizationProjector.AddDelta(deltas, ref order, "talent-quality:" + id,
                    CharacterCreationFinalizationDeltaKinds.Quality, quality.SourceId, null, quality.Name,
                    0, 0, quality.SourceAnchorIds);
            }
            foreach (var quality in source.Talent.GrantedQualitySources!)
                CheckRestrictions(Parse(quality.CanonicalSourceXml, "quality"), root,
                    projectedQualities, flags);
            foreach (var (attribute, flag) in new[] { ("MAG", "magenabled"), ("RES", "resenabled"), ("DEP", "depenabled") })
            {
                var actual = attributes.Attributes.Where(item => item.AttributeId == attribute).ToArray();
                Require(actual.Length == 1 && actual[0].IsEnabled == flags.Contains(flag));
            }
            root.Element("qualities")!.Add(projectedQualities);
            Container(root, "improvements").Add(improvements);
            XElement gears = Container(root, "gears");
            foreach (var (saved, gearSource) in projectedGear)
            {
                bool hasActiveCommlink = gears.Elements("gear").Any(item => Boolean(item, "active", false));
                if (!hasActiveCommlink && Scalar(saved, "canformpersona").Contains("Self", StringComparison.Ordinal))
                    saved.Element("active")!.Value = "True";
                gears.Add(saved);
                CharacterCreationFinalizationProjector.AddDelta(deltas, ref order,
                    "talent-gear:" + saved.Element("guid")!.Value,
                    CharacterCreationFinalizationDeltaKinds.Gear, gearSource.SourceId, null, gearSource.Name,
                    0, 0, gearSource.SourceAnchorIds);
            }
            foreach (string flag in flags)
            {
                Set(root, flag, "True");
                CharacterCreationFinalizationProjector.AddDelta(deltas, ref order, "talent-flag:" + flag,
                    CharacterCreationFinalizationDeltaKinds.MagicResonance, flag, "False", "True", 0, 0, source.Talent.SourceAnchorIds);
            }
            Require(source.Tradition?.Identity == magic.Selections.Tradition
                && source.Stream?.Identity == magic.Selections.Stream
                && !(source.Tradition is not null && source.Stream is not null));
            foreach (var tradition in new[] { source.Tradition, source.Stream }.OfType<CharacterCreationMagicResonanceOptionFinalizationSource>())
            {
                Require(!root.Elements("tradition").Any());
                XElement node = OptionSource(tradition, "tradition");
                Require(Scalar(node, "drain").Length > 0);
                string kind = tradition.Identity.Kind == CharacterCreationMagicResonanceKinds.Stream ? "RES" : "MAG";
                var spirits = node.Element("spirits");
                Require(spirits is null || !spirits.HasAttributes && spirits.Elements().All(item =>
                    item.Name.LocalName is "spirit" or "spiritcombat" or "spiritdetection" or "spirithealth" or "spiritillusion" or "spiritmanipulation"
                    && !item.HasElements && !item.HasAttributes));
                XElement saved = Identity("tradition", tradition, magic.DraftDigest);
                saved.Add(new XElement("traditiontype", kind), new XElement("extra"),
                    new XElement("spiritform", "Materialization"), new XElement("drain", Scalar(node, "drain")),
                    new XElement("source", tradition.SourceBook), new XElement("page", tradition.Page));
                foreach (string field in new[] { "spiritcombat", "spiritdetection", "spirithealth", "spiritillusion", "spiritmanipulation" })
                    saved.Add(new XElement(field, spirits is null ? string.Empty : Scalar(spirits, field)));
                saved.Add(new XElement("spirits", spirits?.Elements("spirit").Select(item => item.Value)
                    .Distinct(StringComparer.Ordinal).Select(value => new XElement("spirit", value))), new XElement("bonus"));
                root.Add(saved);
                SelectionDelta(deltas, ref order, tradition);
            }
            ApplyOptions(root, "spells", "spell", source.Spells, magic.Selections.Spells, magic.DraftDigest, deltas, ref order);
            ApplyOptions(root, "complexforms", "complexform", source.ComplexForms, magic.Selections.ComplexForms, magic.DraftDigest, deltas, ref order);
            Require(source.AdeptPowers.Count == magic.Selections.AdeptPowers.Count
                && source.AdeptPowers.All(item => magic.Selections.AdeptPowers.Count(choice => choice.Identity == item.Identity
                    && choice.Levels == item.Levels) == 1));
            ApplyOptions(root, "powers", "power", source.AdeptPowers,
                magic.Selections.AdeptPowers.Select(item => item.Identity).ToArray(), magic.DraftDigest, deltas, ref order);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
            or XmlException or OverflowException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static void ApplySkillGrants(XElement root, CharacterCreationPrerequisiteDraft prerequisite, CharacterCreationSkillsDraft skills)
    {
        var plan = prerequisite.TalentSelection!.GrantPlan;
        var active = plan?.ActiveSkills ?? [];
        Require(plan?.SkillGroups.All(item => item.BaseRating >= 0) ?? true);
        IReadOnlyList<CharacterCreationTalentSkillGroupGrantPlanEntry> groups =
            plan?.SkillGroups.Where(item => item.BaseRating > 0).ToArray() ?? [];
        Require(Equal(skills.PrerequisiteDraftDigest, prerequisite.DraftDigest)
            && skills.PrerequisiteDraftRevision == prerequisite.DraftRevision
            && (plan is null || Equal(plan.PlanDigest,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(plan with { PlanDigest = string.Empty })))
            && active.Count == skills.Skills.Count(item => item.GrantedRating > 0)
            && groups.Count == skills.SkillGroups.Count(item => item.GrantedRating > 0));
        foreach (var skill in skills.Skills)
        {
            var grants = active.Where(item => item.SourceId == skill.SourceSkillId).ToArray();
            Require(grants.Length <= 1 && skill.GrantedRating == (grants.SingleOrDefault()?.BaseRating ?? 0));
            if (grants.Length == 0) continue;
            var grant = grants[0];
            Require(grant.ImprovementKind == CharacterCreationTalentGrantImprovementKinds.SkillBase
                && grant.CanonicalName == skill.Name && grant.TargetKind == "active-skill"
                && !skill.IsNativeLanguage && skill.Kind == CharacterCreationSkillKinds.Active
                && skill.Rating >= grant.BaseRating && skill.EffectiveRating == skill.Rating
                && skill.PointCost == skill.Rating - grant.BaseRating + (skill.SpecializationName is null ? 0 : 1));
            Container(root, "improvements").Add(Improvement("SkillBase", skill.Name, "", "Heritage", grant.BaseRating));
        }
        foreach (var group in skills.SkillGroups)
        {
            var grants = groups.Where(item => item.CanonicalName == group.Name).ToArray();
            Require(grants.Length <= 1 && group.GrantedRating == (grants.SingleOrDefault()?.BaseRating ?? 0));
            if (grants.Length == 0) continue;
            var grant = grants[0];
            Require(grant.ImprovementKind == CharacterCreationTalentGrantImprovementKinds.SkillGroupBase
                && grant.TargetKind == "skill-group" && group.Rating >= grant.BaseRating
                && group.PointCost == group.Rating - grant.BaseRating
                && group.MemberSkillSourceIds.Order(StringComparer.Ordinal).SequenceEqual(grant.MemberSkillSourceIds.Order(StringComparer.Ordinal)));
            Container(root, "improvements").Add(Improvement("SkillGroupBase", group.Name, "", "Heritage", grant.BaseRating));
        }
    }

    private static string CompileBonus(XElement? bonus, string forced, CharacterCreationPrerequisiteDraft prerequisite,
        CharacterCreationTalentQualitySource quality, string id, HashSet<string> flags, List<XElement> improvements,
        List<(XElement Saved, CharacterCreationTalentGearSource Source)> gears)
    {
        string extra = string.Empty;
        if (bonus is null) { Require(forced.Length == 0); return extra; }
        Require(bonus.Attributes().All(item => item.Name == "useselected" && bool.TryParse(item.Value, out _)));
        Require(!bonus.Nodes().OfType<XText>().Any(item => !string.IsNullOrWhiteSpace(item.Value)));
        bool useSelected = bonus.Attribute("useselected") is not { } useSelectedAttribute || bool.Parse(useSelectedAttribute.Value);
        bool usedForced = false;
        int gearIndex = 0;
        foreach (XElement effect in bonus.Elements())
        {
            Require(!effect.HasAttributes);
            switch (effect.Name.LocalName)
            {
                case "addgear":
                    Require(forced.Length == 0 && quality.GrantedGearSources is not null
                        && gearIndex < quality.GrantedGearSources.Count);
                    var gearSource = quality.GrantedGearSources[gearIndex++];
                    Require(Scalar(effect, "name") == gearSource.Name && Scalar(effect, "category") == gearSource.Category);
                    string gearId = CharacterCreationFinalizationProjector.StableGuid(
                        $"talent-gear:{id}:{gearSource.SourceId}:{gearSource.SourceNodeDigest}:{gearIndex}").ToString("D");
                    Require(CharacterCreationLegacySourceProjector.TryBuildGrantedGearInstance(gearSource, gearId, id, out var gear));
                    gears.Add((gear, gearSource));
                    improvements.Add(Improvement("Gear", gearId, id, "Quality"));
                    break;
                case "specificskill":
                    Require(effect.Elements().All(item => item.Name.LocalName is "name" or "bonus" or "condition" or "applytorating")
                        && !effect.Nodes().OfType<XText>().Any(item => !string.IsNullOrWhiteSpace(item.Value)));
                    string skillName = Scalar(effect, "name");
                    string condition = Scalar(effect, "condition");
                    Require(!string.IsNullOrWhiteSpace(skillName)
                        && int.TryParse(Scalar(effect, "bonus"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _));
                    int skillBonusValue = int.Parse(Scalar(effect, "bonus"), CultureInfo.InvariantCulture);
                    bool addsToRating = Boolean(effect, "applytorating", false);
                    XElement skillBonus = Improvement("Skill", skillName, id, "Quality", skillBonusValue);
                    skillBonus.Element("condition")!.Value = condition;
                    skillBonus.Element("addtorating")!.Value = addsToRating ? "1" : "0";
                    improvements.Add(skillBonus);
                    break;
                case "enableattribute":
                    Require(effect.Elements().Count() == 1 && effect.Element("name") is not null);
                    string attribute = Scalar(effect, "name").ToUpperInvariant();
                    string flag = attribute switch { "MAG" => "magenabled", "RES" => "resenabled", "DEP" => "depenabled", _ => "" };
                    Require(flag.Length > 0 && flags.Add(flag));
                    improvements.Add(Improvement("Attribute", attribute, id, "Quality", 0, "enableattribute", 0));
                    break;
                case "enabletab":
                    Require(effect.Elements().Any() && effect.Elements().All(item => item.Name == "name" && !item.HasAttributes && !item.HasElements));
                    foreach (XElement name in effect.Elements())
                    {
                        string tab = name.Value.ToLowerInvariant();
                        string value = tab switch { "magician" => "Magician", "adept" => "Adept", "technomancer" => "Technomancer", _ => "" };
                        Require(value.Length > 0 && flags.Add(tab));
                        improvements.Add(Improvement("SpecialTab", value, id, "Quality", 0, "enabletab", 0));
                    }
                    break;
                case "unlockskills":
                    Require(!effect.HasElements);
                    string[] choices = effect.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    Require(choices.Length > 0 && choices.Distinct(StringComparer.Ordinal).Count() == choices.Length
                        && choices.All(choice => choice is "Magician" or "Adept" or "Technomancer" or "Sorcery" or "Conjuring" or "Enchanting"));
                    string[] chosenGroups = prerequisite.TalentSelection!.GrantPlan?.SkillGroups.Select(item => item.CanonicalName).ToArray() ?? [];
                    string chosen = choices.Length == 1 ? choices[0] : chosenGroups.Length == 1 ? chosenGroups[0] : string.Empty;
                    // Aspected Priority B/C/D pushes the explicit group choice into
                    // the legacy unlock prompt, including D's selection-only rating zero.
                    Require(choices.Contains(chosen, StringComparer.Ordinal));
                    if (forced.Length > 0) { Require(forced == chosen); usedForced = true; }
                    improvements.Add(Improvement("SpecialSkills", chosen, id, "Quality"));
                    break;
                case "blockspelldescriptor":
                case "limitspellcategory":
                    Require(!effect.HasElements && !string.IsNullOrWhiteSpace(effect.Value));
                    improvements.Add(Improvement(effect.Name.LocalName == "blockspelldescriptor" ? "BlockSpellDescriptor" : "LimitSpellCategory",
                        effect.Value, id, "Quality"));
                    if (useSelected) extra = effect.Value;
                    break;
                default:
                    throw new InvalidDataException("Uncompiled Talent bonus.");
            }
        }
        Require((forced.Length == 0 || usedForced) && gearIndex == (quality.GrantedGearSources?.Count ?? 0));
        return extra;
    }

    private static void CheckRestrictions(XElement definition, XElement root, IReadOnlyList<XElement> granted, IReadOnlySet<string> flags)
    {
        Require(!definition.Elements("required").Any());
        XElement? forbidden = definition.Element("forbidden");
        if (forbidden is null) return;
        Require(!forbidden.HasAttributes && forbidden.Elements().Count() == 1 && forbidden.Element("oneof") is not null);
        XElement oneOf = forbidden.Element("oneof")!;
        Require(!oneOf.HasAttributes && oneOf.HasElements);
        foreach (XElement condition in oneOf.Elements())
        {
            Require(!condition.HasAttributes && !condition.HasElements);
            if (condition.Name == "quality")
            {
                Require(!string.IsNullOrWhiteSpace(condition.Value));
                Require(!root.Element("qualities")!.Elements("quality").Concat(granted).Any(item =>
                    item.Element("name")?.Value == condition.Value || item.Element("sourceid")?.Value == condition.Value));
            }
            else
            {
                Require(condition.Name.LocalName is "magenabled" or "resenabled" or "depenabled"
                    && string.IsNullOrWhiteSpace(condition.Value) && !flags.Contains(condition.Name.LocalName));
            }
        }
    }

    private static void ApplyOptions(XElement root, string container, string itemName,
        IReadOnlyList<CharacterCreationMagicResonanceOptionFinalizationSource> sources,
        IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> selections, string digest,
        ICollection<CharacterCreationFinalizationDelta> deltas, ref int order)
    {
        Require(sources.Count == selections.Count && selections.Distinct().Count() == selections.Count
            && sources.Select(item => item.Identity).OrderBy(item => item.SourceId, StringComparer.Ordinal)
                .SequenceEqual(selections.OrderBy(item => item.SourceId, StringComparer.Ordinal)));
        XElement collection = Container(root, container);
        Require(!collection.HasElements && !collection.HasAttributes && string.IsNullOrWhiteSpace(collection.Value));
        foreach (var source in sources)
        {
            XElement node = OptionSource(source, itemName);
            XElement saved = Identity(itemName, source, digest);
            if (itemName == "spell")
            {
                foreach (string field in new[] { "category", "type", "range", "damage", "duration", "dv", "useskill" })
                    saved.Add(new XElement(field, Scalar(node, field)));
                saved.Add(new XElement("descriptors", Scalar(node, "descriptor")));
                foreach (string field in new[] { "limited", "extended", "customextended", "alchemical", "freebonus", "barehandedadept" })
                    saved.Add(new XElement(field, "False"));
                saved.Add(new XElement("improvementsource", "Spell"), new XElement("grade", 0));
            }
            else if (itemName == "power")
            {
                Require(CharacterCreationAdeptPowerSourceRules.IsUndiscountedPayloadSupported(node)
                    && CharacterCreationAdeptPowerSourceRules.TryReadMaximumLevels(node, out _, out _));
                CharacterCreationAdeptPowerSourceRules.TryReadMaximumLevels(node, out _, out int savedMaximum);
                saved.Add(new XElement("pointsperlevel", Scalar(node, "points")),
                    new XElement("adeptway", Scalar(node, "adeptway", "0")), new XElement("action", Scalar(node, "action")),
                    new XElement("rating", source.Levels), new XElement("extrapointcost", 0),
                    new XElement("levels", Boolean(node, "levels", false) ? "True" : "False"),
                    // Chummer5 Power.Create reads maxlevel(s), not source limit.
                    new XElement("maxlevels", savedMaximum), new XElement("discounted", "False"),
                    new XElement("discountedgeas", "False"), new XElement("bonussource"), new XElement("freepoints", 0),
                    new XElement("bonus"), node.Element("adeptwayrequires") is { } requirements
                        ? new XElement(requirements) : new XElement("adeptwayrequires"), new XElement("enhancements"));
            }
            else
            {
                foreach (string field in new[] { "useskill", "target", "duration", "fv" }) saved.Add(new XElement(field, Scalar(node, field)));
                saved.Add(new XElement("grade", 0));
            }
            saved.Add(new XElement("extra"), new XElement("source", source.SourceBook), new XElement("page", source.Page),
                new XElement("notes"), new XElement("notesColor", "#000000"));
            collection.Add(saved);
            SelectionDelta(deltas, ref order, source);
        }
    }

    private static XElement OptionSource(CharacterCreationMagicResonanceOptionFinalizationSource source, string name)
    {
        Require(Equal(source.ProjectionDigest, CharacterCreationMagicResonanceFinalizationRules.ComputeOptionProjectionDigest(source))
            && Equal(source.CanonicalSourceXmlDigest, CharacterCreationMagicResonanceDigest.ComputeUtf8(source.CanonicalSourceXml)));
        XElement node = Parse(source.CanonicalSourceXml, name);
        Require(Guid.TryParseExact(Scalar(node, "id"), "D", out Guid id) && id.ToString("D") == source.Identity.SourceId
            && Scalar(node, "name") == source.Name && Scalar(node, "source") == source.SourceBook && Scalar(node, "page") == source.Page);
        string[] allowed = name switch
        {
            "tradition" => ["id", "name", "drain", "source", "page", "spirits", "bonus"],
            "power" => ["id", "name", "points", "levels", "limit", "source", "page", "action", "adeptway", "adeptwayrequires", "bonus", "maxlevel", "maxlevels"],
            "spell" => ["id", "name", "page", "source", "category", "damage", "descriptor", "duration", "dv", "range", "type", "useskill", "bonus"],
            "complexform" => ["id", "name", "target", "duration", "fv", "source", "page", "bonus"],
            _ => []
        };
        Require(node.Elements().All(item => allowed.Contains(item.Name.LocalName, StringComparer.Ordinal)
            && !item.HasAttributes && (item.Name == "spirits" || name == "power" && item.Name == "adeptwayrequires" || !item.HasElements)));
        Require(Scalar(node, "bonus").Length == 0);
        if (name == "power") Require(CharacterCreationAdeptPowerSourceRules.IsUndiscountedPayloadSupported(node));
        return node;
    }

    private static XElement Parse(string xml, string name)
    {
        using var input = new StringReader(xml);
        using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_048_576 });
        XElement root = XElement.Load(reader);
        Require(root.Name == name && !root.HasAttributes
            && !root.DescendantNodes().OfType<XProcessingInstruction>().Any()
            && root.DescendantsAndSelf().All(item => item.Name.Namespace == XNamespace.None)
            && root.Elements().GroupBy(item => item.Name).All(group => group.Count() == 1));
        return root;
    }

    private static XElement Identity(string name, CharacterCreationMagicResonanceOptionFinalizationSource source, string digest) =>
        new(name, new XElement("sourceid", source.Identity.SourceId),
            new XElement("guid", CharacterCreationFinalizationProjector.StableGuid($"{name}:{source.Identity.SourceId}:{source.SourceNodeDigest}:{digest}")),
            new XElement("name", source.Name));

    private static void SelectionDelta(ICollection<CharacterCreationFinalizationDelta> deltas, ref int order,
        CharacterCreationMagicResonanceOptionFinalizationSource source) =>
        CharacterCreationFinalizationProjector.AddDelta(deltas, ref order, source.Identity.Kind + ":" + source.Identity.SourceId,
            CharacterCreationFinalizationDeltaKinds.MagicResonance, source.Identity.SourceId, null,
            source.Name + (source.Levels > 1 ? " × " + source.Levels.ToString(CultureInfo.InvariantCulture) : string.Empty),
            0, 0, source.SourceAnchorIds);

    private static XElement Improvement(string type, string name, string sourceName, string source,
        int value = 0, string unique = "", int rating = 1) => new("improvement",
        new XElement("target"), unique.Length == 0 ? null : new XElement("unique", unique),
        new XElement("improvedname", name), new XElement("sourcename", sourceName),
        new XElement("min", 0), new XElement("max", 0), new XElement("aug", 0), new XElement("augmax", 0),
        new XElement("val", value), new XElement("rating", rating), new XElement("exclude"), new XElement("condition"),
        new XElement("improvementttype", type), new XElement("improvementsource", source),
        new XElement("custom", "False"), new XElement("customname"), new XElement("customid"), new XElement("customgroup"),
        new XElement("addtorating", "0"), new XElement("enabled", "1"), new XElement("order", "0"),
        new XElement("notes"), new XElement("notesColor", "#000000"));

    private static XElement Container(XElement root, string name)
    {
        XElement[] matches = root.Elements(name).ToArray();
        Require(matches.Length <= 1);
        if (matches.Length == 1) return matches[0];
        XElement created = new(name);
        root.Add(created);
        return created;
    }

    private static void Set(XElement root, string name, string value) => Container(root, name).Value = value;
    private static string Scalar(XElement root, string name, string fallback = "")
    {
        XElement[] matches = root.Elements(name).ToArray();
        Require(matches.Length <= 1 && matches.All(item => !item.HasElements && !item.HasAttributes));
        return matches.SingleOrDefault()?.Value ?? fallback;
    }
    private static bool Boolean(XElement root, string name, bool fallback)
    {
        string text = Scalar(root, name, fallback ? "True" : "False");
        Require(bool.TryParse(text, out bool value));
        return value;
    }
    private static bool Equal(string a, string b) => CharacterCreationMagicResonanceDigest.EqualsFixedTime(a, b);
    private static void Require([DoesNotReturnIf(false)] bool valid)
    {
        if (!valid) throw new InvalidDataException("Awakened saved graph is unresolved or inconsistent.");
    }
}
