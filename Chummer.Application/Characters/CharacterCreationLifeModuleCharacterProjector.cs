using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Prepares the whole Life Modules runner without writing it. The caller
/// must admit the ordered module sequence and current source data, then perform
/// one explicitly confirmed, owner-bound atomic write including Origin history.</summary>
internal static class CharacterCreationLifeModuleCharacterProjector
{
    internal static bool TryProject(string rawXml, CharacterCreationLifeModuleCharacterParts parts,
        out CharacterCreationLifeModuleCharacterProjection? projection)
    {
        projection = null;
        try
        {
            var p = parts;
            if (p.Effects.RawCharacterXmlDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(rawXml)
                || !TryReplay(rawXml, p, out var skillGraph, out var skillDeltas)) return false;
            var document = XDocument.Parse(rawXml);
            var root = document.Root;
            if (root?.Name != "character" || root.Elements("created").Count() != 1
                || !bool.TryParse(root.Element("created")!.Value, out bool created) || created
                || root.Elements("buildmethod").Count() != 1
                || root.Element("buildmethod")!.Value != CharacterCreationBuildMethods.LifeModules) return false;
            // Imported or partially applied inventory needs reconciliation, never
            // a blanket replacement with a newly prepared character.
            foreach (string field in new[] { "attributes", "newskills", "qualities", "improvements", "gears", "contacts", "lifestyles",
                         "tradition", "stream", "spells", "powers", "complexforms" })
            {
                var nodes = root.Elements(field).ToArray();
                if (nodes.Length > 1 || nodes.Any(node => node.HasAttributes || node.HasElements
                    || !string.IsNullOrWhiteSpace(node.Value))) return false;
            }
            var changes = new List<CharacterCreationFinalizationDelta>();
            string seed = Hash(new { p.Effects.PlanDigest, Racial = p.Racial.PlanDigest, Talent = p.Talent.PlanDigest,
                Attributes = p.Attributes.QuoteDigest, Skills = p.Skills.QuoteDigest, Resources = p.Resources.QuoteDigest,
                Gear = p.Gear.QuoteDigest, Lifestyles = p.Lifestyles.QuoteDigest, Contacts = p.Contacts.QuoteDigest,
                Magic = p.Magic.QuoteDigest, Finances = p.Finances.QuoteDigest });
            foreach (string field in CharacterCreationCareerBaseline.InitializeMissing(root))
                Change("career-initialization:" + field, CharacterCreationFinalizationDeltaKinds.Lifecycle,
                    field, null, root.Element(field)!.Value, 0, 0, []);
            var metatype = p.Racial.Metatype;
            if (!Guid.TryParseExact(metatype.OptionId, "D", out var metatypeId) || metatypeId == Guid.Empty
                || metatype.Movement.IsSpecial) return false;
            Set("metatype", metatype.Label);
            Set("metatypeid", metatypeId.ToString("D"));
            Set("metatypebp", Number(metatype.KarmaCost));
            Set("metatypecategory", metatype.Category);
            Set("metavariant", string.Empty);
            Set("metavariantid", Guid.Empty.ToString("D"));
            Set("movement", string.Empty);
            foreach (var (name, rate) in new[] { ("walk", metatype.Movement.Walk), ("run", metatype.Movement.Run),
                         ("sprint", metatype.Movement.Sprint) })
            {
                string value = string.Join('/', Number(rate.Ground), Number(rate.Swim), Number(rate.Fly));
                Set(name, value);
                Set(name + "alt", value);
            }
            Set("initiativedice", Number(metatype.Initiative.MinimumDiceFallback));
            Change("metatype:selected", CharacterCreationFinalizationDeltaKinds.Metatype, metatype.OptionId,
                null, metatype.Label, metatype.KarmaCost, 0, metatype.SourceAnchorIds);

            var attributes = new XElement("attributes");
            foreach (var range in metatype.Attributes)
            {
                var value = p.Attributes.Attributes.SingleOrDefault(row => row.AttributeId == range.AttributeId);
                bool active = value is not null || range.AttributeId == "ESS";
                int current = value?.Current ?? (range.AttributeId == "ESS" ? range.Maximum : 0);
                attributes.Add(new XElement("attribute", new XElement("name", range.AttributeId),
                    new XElement("metatypemin", active ? range.Minimum : 0),
                    new XElement("metatypemax", active ? range.Maximum : 0),
                    new XElement("metatypeaugmax", active ? range.AugmentedMaximum : 0),
                    new XElement("base", 0), new XElement("karma", value?.KarmaLevels ?? 0),
                    new XElement("metatypecategory", "Standard"), new XElement("totalvalue", current)));
                Change("attribute:" + range.AttributeId, CharacterCreationFinalizationDeltaKinds.Attribute,
                    range.AttributeId, null, Number(current), value?.KarmaCost ?? 0, 0,
                    value?.SourceAnchorIds ?? metatype.SourceAnchorIds);
            }
            Replace(attributes);
            Replace(skillGraph!);
            Add(skillDeltas);
            var qualities = new XElement("qualities", p.Effects.QualityXml.Concat(p.Racial.QualityXml)
                .Concat(p.Talent.QualityXml).Select(XElement.Parse));
            var qualityIds = qualities.Elements("quality").Select(row => row.Element("guid")?.Value).ToArray();
            if (qualityIds.Any(id => !Guid.TryParseExact(id, "D", out var value) || value == Guid.Empty)
                || qualityIds.Distinct(StringComparer.Ordinal).Count() != qualityIds.Length) return false;
            Replace(qualities);
            Replace(new XElement("improvements", p.Effects.ImprovementXml.Concat(p.Racial.ImprovementXml)
                .Concat(p.Talent.ImprovementXml).Select(XElement.Parse)));
            foreach (string flag in new[] { "magenabled", "resenabled", "depenabled", "magician", "adept", "technomancer", "ai" })
                Set(flag, p.Racial.Flags.Concat(p.Talent.Flags).Contains(flag, StringComparer.Ordinal) ? "True" : "False");
            foreach (var owner in p.Effects.ModuleOwners)
            {
                var quality = qualities.Elements("quality").Single(row => row.Element("guid")?.Value == owner.QualityId);
                Change("module:" + owner.OccurrenceId, CharacterCreationFinalizationDeltaKinds.Quality,
                    owner.SourceId, null, quality.Element("name")!.Value, owner.KarmaCost, 0,
                    [$"{quality.Element("source")!.Value}:{quality.Element("page")!.Value}"]);
            }
            Change("talent:selected", CharacterCreationFinalizationDeltaKinds.MagicResonance, p.Talent.Talent.OptionId,
                null, p.Talent.Talent.Name, p.Talent.Talent.KarmaCost, 0, p.Talent.Summary.SourceAnchorIds);
            Change("qualities:adjustment", CharacterCreationFinalizationDeltaKinds.Quality, "qualities-karma-adjustment",
                "0", Number(p.Resources.QualityCosts.KarmaAdjustmentAfterTalent),
                p.Resources.QualityCosts.KarmaAdjustmentAfterTalent, 0, p.Resources.QualityCosts.Policy.SourceAnchorIds);

            var magicChanges = new List<CharacterCreationFinalizationDelta>();
            int order = 0;
            // Empty imported placeholders carry no choice. The shared serializer
            // writes both magic traditions and resonance streams as <tradition>.
            root.Elements("tradition").Remove();
            root.Elements("stream").Remove();
            foreach (var tradition in p.Magic.Sources.Where(source => source.Identity.Kind is "tradition" or "stream"))
                CharacterCreationAwakenedLegacyProjector.ApplyTradition(root, tradition, seed, magicChanges, ref order);
            foreach (var (kind, container, item) in new[] { ("spell", "spells", "spell"),
                         ("complex-form", "complexforms", "complexform"), ("adept-power", "powers", "power") })
            {
                var sources = p.Magic.Sources.Where(source => source.Identity.Kind == kind).ToArray();
                CharacterCreationAwakenedLegacyProjector.ApplyOptions(root, container, item, sources,
                    sources.Select(source => source.Identity).ToArray(), seed, magicChanges, ref order);
            }
            Add(magicChanges);
            if (p.Magic.Cost.MysticPowerPoints is { } points)
            {
                Set("magsplitadept", Number(points.PowerPoints));
                Set("magsplitmagician", "0");
            }
            Change("magic:purchases", CharacterCreationFinalizationDeltaKinds.MagicResonance, "magic-karma",
                "0", Number(p.Magic.Cost.TotalKarma), p.Magic.Cost.TotalKarma, 0, p.Magic.ProjectionCatalog.Policy.SourceAnchorIds);
            Replace(new XElement("contacts", p.Contacts.Lines.Select(line => CharacterCreationKarmaContactsRules.BuildContactElement(line.Selection))));
            Set("contactpoints", Number(p.Contacts.ContactBudget.Total));
            foreach (var line in p.Contacts.Lines)
                Change("contact:" + line.Selection.ContactId.ToString("D"), CharacterCreationFinalizationDeltaKinds.Build,
                    line.Selection.ContactId.ToString("D"), null, line.Selection.Identity.Name, 0, 0, p.Contacts.Policy.SourceAnchorIds);
            Change("contacts:karma", CharacterCreationFinalizationDeltaKinds.Build, "contacts-karma", "0",
                Number(p.Contacts.KarmaUsed), p.Contacts.KarmaUsed, 0, p.Contacts.Policy.SourceAnchorIds);
            var gear = new XElement("gears", p.Racial.GearXml.Concat(p.Talent.GearXml).Select(XElement.Parse));
            foreach (var line in p.Gear.Lines)
            {
                if (!CharacterCreationLegacySourceProjector.TryBuildGear(line, seed, out var saved)) return false;
                gear.Add(saved);
                Change("gear:" + line.OptionId, CharacterCreationFinalizationDeltaKinds.Gear, line.SourceId.ToString("D"),
                    null, Number(line.Quantity), 0, line.TotalCost, line.SourceAnchorIds);
            }
            if (!gear.Elements("gear").Any(row => string.Equals(row.Element("active")?.Value, "True", StringComparison.OrdinalIgnoreCase))
                && gear.Elements("gear").FirstOrDefault(row => row.Element("canformpersona")?.Value.Contains("Self", StringComparison.Ordinal) == true) is { } persona)
                persona.SetElementValue("active", "True");
            Replace(gear);
            if (!TryLifestyles(p, seed, out var lifestyleNodes)) return false;
            Replace(new XElement("lifestyles", lifestyleNodes));
            foreach (var line in p.Lifestyles.Lines)
                Change("lifestyle:" + line.Configuration.LifestyleId.ToString("D"), CharacterCreationFinalizationDeltaKinds.Resources,
                    line.SourceId.ToString("D"), null, line.Configuration.Name, 0, line.Economics.TotalCost, line.SourceAnchorIds);
            if (p.Lifestyles.Lines.Count == 0)
                Change("lifestyle:default", CharacterCreationFinalizationDeltaKinds.Resources, p.Finances.StartingCashSource.SourceId,
                    null, p.Finances.StartingCashSource.Name, 0, 0, p.Finances.StartingCashSource.SourceAnchorIds);
            Set("startingnuyen", Number(p.Resources.NuyenFromKarma));
            Set("nuyenbp", Number(p.Resources.KarmaInvestment));
            Set("karma", Number(p.Finances.KarmaCarried));
            Set("nuyen", Number(p.Finances.CareerNuyen));
            Change("resources:funding", CharacterCreationFinalizationDeltaKinds.Resources, "startingnuyen", null,
                Number(p.Resources.NuyenFromKarma), p.Resources.KarmaInvestment, 0, p.Resources.Policy.SourceAnchorIds);
            Change("resources:karma-rounding", CharacterCreationFinalizationDeltaKinds.Resources, "resource-karma-rounding",
                Number(p.Resources.KarmaInvestment), Number(decimal.Ceiling(p.Resources.KarmaInvestment)),
                p.Finances.ResourceKarmaRoundingAdjustment, 0, [CharacterCreationKarmaFinalizationBudgetRules.ResourceRoundingAnchor]);
            Change("carryover:karma", CharacterCreationFinalizationDeltaKinds.Resources, "karma", Number(p.Finances.KarmaBeforeCarryover),
                Number(p.Finances.KarmaCarried), 0, 0, p.Finances.Policy.SourceAnchorIds);
            Change("carryover:nuyen", CharacterCreationFinalizationDeltaKinds.Resources, "nuyen-carried", Number(p.Finances.NuyenBeforeCarryover),
                Number(p.Finances.NuyenCarried), 0, 0, p.Finances.Policy.SourceAnchorIds);
            Change("lifestyle:starting-nuyen", CharacterCreationFinalizationDeltaKinds.Resources, "lifestyle-starting-nuyen", "0",
                Number(p.Finances.LifestyleStartingNuyen), 0, 0, p.Finances.StartingCashSource.SourceAnchorIds);
            Change("lifecycle:created", CharacterCreationFinalizationDeltaKinds.Lifecycle, "created", "False", "True", 0, 0, p.Finances.SourceAnchorIds);
            if (changes.Sum(row => row.KarmaCost) != p.Resources.TotalKarma - p.Finances.KarmaBeforeCarryover
                || changes.Sum(row => row.NuyenCost) != p.Lifestyles.Budget.Used
                || changes.Select(row => row.DeltaId).Distinct(StringComparer.Ordinal).Count() != changes.Count) return false;
            Set("created", "True");
            root.Elements(CharacterCreationBootstrapXml.MarkerElement).Remove();
            CharacterCareerReputationProjector.ValidateSavedInputShape(root);
            string result = document.ToString(SaveOptions.DisableFormatting);
            projection = new(result, CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(result),
                seed, changes.ToArray());
            return true;

            void Replace(XElement element)
            {
                var matches = root.Elements(element.Name).Take(2).ToArray();
                if (matches.Length > 1) throw new InvalidDataException("Duplicate finalization field.");
                if (matches.Length == 1) matches[0].ReplaceWith(element); else root.Add(element);
            }
            void Set(string name, string value) => Replace(new XElement(name, value));
            void Add(IEnumerable<CharacterCreationFinalizationDelta> values)
            { foreach (var delta in values) changes.Add(delta with { Order = changes.Count + 1 }); }
            void Change(string id, string kind, string target, string? before, string? after, decimal karma, decimal nuyen,
                IReadOnlyList<string> anchors) => changes.Add(new(changes.Count + 1, id, kind, target, before, after, karma, nuyen, anchors));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException
            or XmlException or OverflowException or KeyNotFoundException)
        { return false; }
    }

    private static bool TryReplay(string rawXml, CharacterCreationLifeModuleCharacterParts p,
        out XElement? skillGraph, out CharacterCreationFinalizationDelta[] skillDeltas)
    {
        skillGraph = null; skillDeltas = [];
        if (p.Skills.Blockers.Count != 0 || p.Resources.Blockers.Count != 0 || p.Gear.Blockers.Count != 0
            || p.Lifestyles.Blockers.Count != 0 || p.Contacts.Blockers.Count != 0 || p.Magic.Blockers.Count != 0
            || !CharacterCreationLifeModuleSkillsLegacyProjector.TryProject(rawXml, p.Effects, p.Racial, p.Talent,
                p.Attributes, p.SkillsCatalog, p.Skills, out skillGraph, out skillDeltas)
            || !Same(p.Resources, CharacterCreationLifeModuleResourcesRules.Quote(p.Effects, p.Racial, p.Talent,
                p.Attributes, p.Skills, p.Resources.Policy, p.Resources.QualityCosts.Policy,
                p.Resources.TotalKarma, p.Resources.KarmaInvestment).Quote)
            || !CharacterCreationGearRules.TryProjectBasket(p.Gear.Selection, p.GearAuthority, p.Resources.NuyenFromKarma,
                out var gearLines, out var gearBudget, out _)
            || !Same(p.Gear.Lines, gearLines) || !Same(p.Gear.Budget, gearBudget)
            || p.Gear.ResourcesQuoteDigest != p.Resources.QuoteDigest
            || p.Gear.QuoteDigest != Hash(p.Gear with { QuoteDigest = string.Empty })
            || p.Gear.Basis != new CharacterCreationKarmaGearBasis(p.GearAuthority.SettingsProfileId,
                p.GearAuthority.ProfileDigest, p.GearAuthority.SourceDigest, p.GearAuthority.RulesDigest,
                p.GearAuthority.RuntimeDigest, p.GearAuthority.AuthorityDigest, p.GearAuthority.MaximumAvailability,
                p.GearAuthority.MaximumBasketLines, p.GearAuthority.MaximumQuantityPerLine)
            || !Same(p.Lifestyles, CharacterCreationLifeModuleLifestylesRules.Quote(p.LifestylesAuthority,
                p.Lifestyles.SourceAuthorityDigest, p.Resources, p.Gear, p.Lifestyles.Selection, p.Lifestyles.StartingLifestyleId).Quote)
            || !Same(p.Contacts, CharacterCreationLifeModuleContactsRules.Quote(p.Effects, p.Racial, p.Talent,
                p.Attributes, p.Resources, p.Contacts.Policy, p.Contacts.Selection).Quote)
            || !Same(p.Magic, CharacterCreationLifeModuleMagicRules.Quote(p.MagicCatalog, p.Effects, p.Racial, p.Talent,
                p.Attributes, p.Skills, p.Resources, p.Contacts, p.Magic.Selections).Quote)
            || !Same(p.Finances, CharacterCreationLifeModuleFinalizationBudgetRules.Quote(p.Finances.Policy,
                p.Finances.StartingCashSource, p.Resources, p.Lifestyles, p.Contacts, p.Magic, p.Finances.DiceTotal).Quote)) return false;
        return true;
    }

    private static bool TryLifestyles(CharacterCreationLifeModuleCharacterParts p, string seed, out XElement[] nodes)
    {
        nodes = [];
        if (p.Lifestyles.Lines.Count > 0)
        {
            nodes = p.Lifestyles.Lines.Select(line => CharacterCreationLifestylesService.BuildLifestyleElement(line, null, p.LifestylesAuthority)).ToArray();
            return true;
        }
        var source = p.Finances.StartingCashSource;
        var authority = p.LifestylesAuthority;
        if (authority.ProfileDigest != source.RawProfileInputsDigest || authority.SettingsProfileId != source.SettingsProfileId
            || authority.SourceDigest != source.SourceInputsDigest || source.Name != "Street") return false;
        var option = authority.LifestyleOptions.SingleOrDefault(row => row.SourceId.ToString("D") == source.SourceId);
        if (option is not { IsSelectable: true, EligibilityIsExact: true, BaseCost: 0 }
            || option.Name != source.Name || option.StartingNuyenDice != source.Dice || option.StartingNuyenMultiplier != source.Multiplier
            || option.SourceBook != source.SourceBook || option.Page != source.Page) return false;
        var config = new CharacterCreationLifestyleConfiguration(CharacterCreationFinalizationProjector.StableGuid("life-module-default-lifestyle:" + seed),
            option.OptionId, option.Name, CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId,
            1, 100m, 0, false, false, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, []);
        if (!CharacterCreationLifestylesRules.TryProject(config, authority, out var line, out _)
            || line.Economics.TotalCost != 0) return false;
        nodes = [CharacterCreationLifestylesService.BuildLifestyleElement(line, null, authority)];
        return true;
    }

    private static bool Same<T>(T left, T right) => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record CharacterCreationLifeModuleCharacterProjection(string CharacterXml, string RawCharacterXmlDigest,
    string ComponentsDigest, IReadOnlyList<CharacterCreationFinalizationDelta> Deltas);
