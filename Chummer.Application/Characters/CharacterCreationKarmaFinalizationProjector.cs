using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Composes the confirmed Karma build into a complete legacy-shaped candidate.
/// It does not write it. A finalization transaction must separately admit current
/// sources, bind explicit review, fence the write, and archive the consumed draft.
/// Awakened purchases still require their own typed draft before completion.
/// </summary>
public static class CharacterCreationKarmaFinalizationProjector
{
    public static bool TryProject(WorkspaceStoredDocument workspace, CharacterCreationKarmaMetatypeQuote foundation,
        CharacterCreationKarmaFinalizationBudgetQuote finances,
        IReadOnlyList<CharacterCreationTalentQualitySource> racialSources,
        CharacterCreationLifestylesAuthority lifestyles,
        out string characterXml, out CharacterCreationFinalizationDelta[] deltas, out string[] blockers)
    {
        characterXml = string.Empty;
        deltas = [];
        blockers = [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid];
        try
        {
            if (workspace is null || foundation is null || foundation.Talent is null
                || !CharacterCreationKarmaMetatypeTransaction.IsValidHistory(workspace)
                || workspace.Document.AuxiliaryState.CharacterCreationBootstrapBinding is not { BuildMethod: CharacterCreationBuildMethods.Karma } bootstrap
                || !Equals(workspace.Document.AuxiliaryState with { CharacterCreationKarmaMetatypeDecisions = null },
                    new WorkspaceDocumentAuxiliaryState(CharacterCreationBootstrapBinding: bootstrap))
                || workspace.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions?.LastOrDefault() is not { } saved
                || foundation.Binding.WorkspaceId != workspace.Id || foundation.Binding.ContentRevision != workspace.ContentRevision
                || foundation.Binding.SavedRevision != workspace.SavedRevision
                || foundation.Binding.AuxiliaryStateDigest != workspace.Document.AuxiliaryStateDigest
                || foundation.Binding.RawCharacterXmlDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(workspace.Document.Content)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.Quote with
                    { Binding = foundation.Binding, SnapshotDigest = foundation.SnapshotDigest, QuoteDigest = foundation.QuoteDigest }, foundation)
                || !CharacterCreationKarmaFinalizationBudgetRules.IsValid(finances, foundation)) return false;
            if (foundation.Talent.OptionId != CharacterCreationKarmaTalentCatalog.MundaneOptionId)
            {
                blockers = [CharacterCreationFinalizationBlockers.MagicResonanceDraftRequired];
                return false;
            }
            if (!CharacterCreationKarmaSkillsLegacyProjector.TryProject(foundation.Metatype, foundation.Talent,
                    foundation.Attributes!, foundation.Skills!, out var skills, out var skillDeltas)
                || !CharacterCreationKarmaGrantsLegacyProjector.TryProject(foundation, racialSources, null,
                    out var grants, out var grantDeltas)
                || !TryDefaultLifestyle(lifestyles, finances, foundation.QuoteDigest, out var lifestyle)) return false;

            var document = XDocument.Parse(workspace.Document.Content, LoadOptions.None);
            var root = document.Root;
            if (root?.Name != "character" || root.Elements("created").Count() != 1
                || !bool.TryParse(root.Element("created")!.Value, out bool created) || created) return false;
            foreach (string container in new[] { "attributes", "newskills", "qualities", "gears", "improvements", "lifestyles", "contacts" })
            {
                var present = root.Elements(container).Take(2).ToArray();
                if (present.Length > 1 || present.Length == 1 && (present[0].HasAttributes || present[0].HasElements
                    || !string.IsNullOrWhiteSpace(present[0].Value))) return false;
            }
            var changes = new List<CharacterCreationFinalizationDelta>();
            foreach (string field in CharacterCreationCareerBaseline.InitializeMissing(root))
                Change("career-initialization:" + field, CharacterCreationFinalizationDeltaKinds.Lifecycle,
                    field, null, root.Element(field)!.Value, 0, 0, []);
            var metatype = foundation.Metatype;
            if (!Guid.TryParseExact(metatype.OptionId, "D", out var metatypeId) || metatypeId == Guid.Empty
                || metatype.Movement.IsSpecial) return false;
            Set("metatype", metatype.Label);
            Set("metatypeid", metatypeId.ToString("D"));
            Set("metatypebp", Number(metatype.KarmaCost));
            Set("metatypecategory", metatype.Category);
            Set("metavariant", string.Empty);
            Set("metavariantid", Guid.Empty.ToString("D"));
            Set("buildmethod", CharacterCreationBuildMethods.Karma);
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
            foreach (var attribute in foundation.Attributes!.Attributes)
            {
                attributes.Add(new XElement("attribute", new XElement("name", attribute.AttributeId),
                    new XElement("metatypemin", attribute.Minimum), new XElement("metatypemax", attribute.Maximum),
                    new XElement("metatypeaugmax", attribute.AugmentedMaximum), new XElement("base", 0),
                    new XElement("karma", attribute.KarmaLevels), new XElement("metatypecategory", "Standard"),
                    new XElement("totalvalue", attribute.Current)));
                Change("attribute:" + attribute.AttributeId, CharacterCreationFinalizationDeltaKinds.Attribute,
                    attribute.AttributeId, null, Number(attribute.Current), attribute.KarmaCost, 0, attribute.SourceAnchorIds);
            }
            Replace(attributes);
            Replace(skills!);
            Add(skillDeltas);
            foreach (var element in grants) Replace(new XElement(element));
            Add(grantDeltas);
            decimal qualitySourceCost = 0;
            foreach (var option in foundation.Qualities!.Selections.OrderBy(item => item.OptionId, StringComparer.Ordinal))
            {
                var selection = new CharacterCreationQualitySelection(option.OptionId, option.SourceId, option.SelectionKey,
                    option.Name, option.Type, option.Rating, option.KarmaCost, option.IsMetagenic, option.CountsAgainstQualityLimit,
                    option.CountsAgainstKarma, false, null, null, option.SourceAnchorIds, option.SourceNodeXml,
                    option.SourceNodeDigest, option.OptionDigest);
                if (!CharacterCreationLegacySourceProjector.TryBuildQualityGraph(selection, foundation.QuoteDigest,
                        out var qualities, out var improvements)) return false;
                root.Element("qualities")!.Add(qualities);
                root.Element("improvements")!.Add(improvements);
                decimal cost = option.CountsAgainstKarma ? option.KarmaCost : 0;
                qualitySourceCost += cost;
                Change("quality:" + option.OptionId, CharacterCreationFinalizationDeltaKinds.Quality,
                    option.SourceId.ToString("D"), null, Number(option.Rating), cost, 0, option.SourceAnchorIds);
            }
            decimal qualityAdjustment = foundation.Qualities.Costs.NetKarmaSpent - qualitySourceCost;
            if (qualityAdjustment != 0)
                Change("qualities:karma-adjustment", CharacterCreationFinalizationDeltaKinds.Build, "qualities-karma-adjustment",
                    Number(qualitySourceCost), Number(foundation.Qualities.Costs.NetKarmaSpent), qualityAdjustment, 0,
                    foundation.Qualities.Policy.SourceAnchorIds);
            if (foundation.Contacts is { } contacts)
            {
                Replace(new XElement("contacts", contacts.Lines.Select(line =>
                    CharacterCreationKarmaContactsRules.BuildContactElement(line.Selection))));
                Set("contactpoints", Number(contacts.ContactPoints));
                foreach (var line in contacts.Lines)
                    Change("contact:" + line.Selection.ContactId.ToString("D"), CharacterCreationFinalizationDeltaKinds.Build,
                        line.Selection.ContactId.ToString("D"), null, line.Selection.Identity.Name, 0, 0,
                        contacts.Policy.SourceAnchorIds);
                Change("contacts:karma", CharacterCreationFinalizationDeltaKinds.Build, "contacts-karma",
                    "0", Number(contacts.KarmaUsed), contacts.KarmaUsed, 0, contacts.Policy.SourceAnchorIds);
            }
            foreach (var line in foundation.Gear!.Lines)
            {
                if (!CharacterCreationLegacySourceProjector.TryBuildGear(line, foundation.QuoteDigest, out var gear)) return false;
                root.Element("gears")!.Add(gear);
                Change("gear:" + line.OptionId, CharacterCreationFinalizationDeltaKinds.Gear, line.SourceId.ToString("D"),
                    null, Number(line.Quantity), 0, line.TotalCost, line.SourceAnchorIds);
            }
            Replace(new XElement("lifestyles", lifestyle));
            Change("lifestyle:default", CharacterCreationFinalizationDeltaKinds.Resources, finances.StartingCashSource.SourceId,
                null, finances.StartingCashSource.Name, 0, 0, finances.StartingCashSource.SourceAnchorIds);
            Set("karma", Number(finances.KarmaCarried));
            Set("nuyen", Number(finances.CareerNuyen));
            Set("startingnuyen", Number(foundation.Resources!.NuyenFromKarma));
            Set("nuyenbp", Number(foundation.Resources.KarmaInvestment));
            Change("resources:funding", CharacterCreationFinalizationDeltaKinds.Resources, "startingnuyen", null,
                Number(foundation.Resources.NuyenFromKarma), foundation.Resources.KarmaInvestment, 0, foundation.Resources.Policy.SourceAnchorIds);
            if (finances.ResourceKarmaRoundingAdjustment != 0)
                Change("resources:karma-rounding", CharacterCreationFinalizationDeltaKinds.Resources, "resource-karma-rounding",
                    Number(foundation.Resources.KarmaInvestment), Number(decimal.Ceiling(foundation.Resources.KarmaInvestment)),
                    finances.ResourceKarmaRoundingAdjustment, 0, [CharacterCreationKarmaFinalizationBudgetRules.ResourceRoundingAnchor]);
            Change("carryover:karma", CharacterCreationFinalizationDeltaKinds.Resources, "karma",
                Number(finances.KarmaBeforeCarryover), Number(finances.KarmaCarried), 0, 0, finances.Policy.SourceAnchorIds);
            Change("carryover:nuyen", CharacterCreationFinalizationDeltaKinds.Resources, "nuyen-carried",
                Number(finances.NuyenBeforeCarryover), Number(finances.NuyenCarried), 0, 0, finances.Policy.SourceAnchorIds);
            Change("lifestyle:starting-nuyen", CharacterCreationFinalizationDeltaKinds.Resources, "lifestyle-starting-nuyen",
                "0", Number(finances.LifestyleStartingNuyen), 0, 0, finances.StartingCashSource.SourceAnchorIds);
            Change("lifecycle:created", CharacterCreationFinalizationDeltaKinds.Lifecycle, "created", "False", "True", 0, 0,
                foundation.SourceAnchorIds);
            if (changes.Sum(item => item.KarmaCost) != foundation.KarmaBudget.Used + finances.ResourceKarmaRoundingAdjustment
                || changes.Sum(item => item.NuyenCost) != foundation.Gear.Budget.BasketCost
                || changes.Select(item => item.DeltaId).Distinct(StringComparer.Ordinal).Count() != changes.Count) return false;
            Set("created", "True");
            root.Elements(CharacterCreationBootstrapXml.MarkerElement).Remove();
            CharacterCareerReputationProjector.ValidateSavedInputShape(root);
            characterXml = document.ToString(SaveOptions.DisableFormatting);
            deltas = changes.ToArray();
            blockers = [];
            return true;

            void Replace(XElement element)
            {
                var matches = root.Elements(element.Name).Take(2).ToArray();
                if (matches.Length > 1) throw new InvalidDataException("Duplicate finalization field.");
                if (matches.Length == 1) matches[0].ReplaceWith(element); else root.Add(element);
            }
            void Set(string name, string value) => Replace(new XElement(name, value));
            void Add(IEnumerable<CharacterCreationFinalizationDelta> source)
            { foreach (var delta in source) changes.Add(delta with { Order = changes.Count + 1 }); }
            void Change(string id, string kind, string target, string? before, string? after, decimal karma, decimal nuyen,
                IReadOnlyList<string> anchors) => changes.Add(new(changes.Count + 1, id, kind, target, before, after, karma, nuyen, anchors));
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or InvalidDataException or OverflowException)
        {
            return false;
        }
    }

    private static bool TryDefaultLifestyle(CharacterCreationLifestylesAuthority authority,
        CharacterCreationKarmaFinalizationBudgetQuote finances, string seed, out XElement? lifestyle)
    {
        lifestyle = null;
        var source = finances.StartingCashSource;
        if (!CharacterCreationLifestylesRules.IsValidAuthority(authority) || authority.ProfileDigest != source.RawProfileInputsDigest
            || authority.SettingsProfileId != source.SettingsProfileId || authority.SourceDigest != source.SourceInputsDigest
            || source.Name != "Street") return false;
        var option = authority.LifestyleOptions.SingleOrDefault(item => item.SourceId.ToString("D") == source.SourceId);
        if (option is not { IsSelectable: true, EligibilityIsExact: true, BaseCost: 0 }
            || option.Name != source.Name || option.StartingNuyenDice != source.Dice
            || option.StartingNuyenMultiplier != source.Multiplier || option.SourceBook != source.SourceBook || option.Page != source.Page) return false;
        var configuration = new CharacterCreationLifestyleConfiguration(
            CharacterCreationFinalizationProjector.StableGuid("karma-default-lifestyle:" + seed), option.OptionId,
            option.Name, CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId, 1, 100m, 0,
            false, false, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, []);
        if (!CharacterCreationLifestylesRules.TryProject(configuration, authority, out var projection, out _)
            || projection.Economics.TotalCost != 0) return false;
        lifestyle = CharacterCreationLifestylesService.BuildLifestyleElement(projection, null, authority);
        return true;
    }

    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
