using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>A read compiler, not a creation/commerce permission or a saved-total adapter.</summary>
internal sealed partial class CharacterSheetOwnerGraph
{
    internal const string Semantics = "chummer.core.sr5-equipped-sheet/v9";
    private readonly XElement root;
    private readonly ICharacterSourceDataContext context;
    private readonly ICharacterSheetOwnerSourceContext? sources;
    private readonly List<Func<bool>> checks;
    private readonly CharacterFinalizedLifeModuleOwnership? finalizedModules;
    private readonly HashSet<Guid> instances = [];
    private readonly Dictionary<Guid, int> qualityLevels = [];
    private readonly List<Owner> owners = [];
    private readonly List<XElement> expected = [];
    private readonly List<string> sourceEvidence = [];
    private readonly List<(Owner Owner, XElement Grade)> ware = [];
    private readonly List<(string Name, int Minimum)> selectedSkillMinimums = [];
    private readonly HashSet<Guid> limitIds = [];
    private readonly List<(Guid Id, int Position)> ownedLimitPositions = [];
    private readonly HashSet<Guid> boundChoices = [];
    private readonly Dictionary<XElement, List<Guid>> qualityGrants = [];
    private readonly Dictionary<Guid, Guid> qualityGrantParents = [];
    private readonly List<(Owner Owner, decimal Value)> unresolvedDefense = [];
    private readonly HashSet<Guid> suppressedOwners = [];
    internal string? DefenseUnavailableReason => unresolvedDefense.Any(e => e.Owner.Enabled && !suppressedOwners.Contains(e.Owner.Id))
        ? "The selected defensetest is outside the supported portable source penalty shape; defense action authority is unresolved."
        : null;
    internal List<CharacterSheetGrantedTrait> GrantedTraits { get; } = [];
    internal List<CharacterSheetSkillUnlock> UnlockedSkills { get; } = [];
    internal Dictionary<string, CharacterActiveSkillSource> NamedSkillSources { get; } = new(StringComparer.Ordinal);
    internal List<CharacterSheetConditionalLimitValue> ConditionalLimits { get; } = [];
    internal IReadOnlyList<CharacterSheetAttributeValue> SpecialAttributes { get; private set; } = [];
    internal XElement[] Effects { get; private set; } = [];
    internal string AuthorityDigest { get; private set; } = "";
    // Read only after complete reconciliation. These are actual selected owners,
    // including generated children; catalog membership never grants an instance.
    internal IReadOnlyList<(Guid OwnerId, Guid SourceId, string SourceXml)> NonPurchasedQualities5688()
        => owners.Where(o => o.Kind == "Quality" && (qualityGrantParents.ContainsKey(o.Id)
            || Text(o.Saved, "qualitysource", "") is "Metatype" or "MetatypeRemovable"
            || ImportedPriority5699 && Text(o.Saved, "qualitysource", "") == "Heritage"
            || nativePriorityQualityOwners9569.Contains(o.Id)))
            .Select(o => (o.Id, o.SourceId, o.Source.ToString(SaveOptions.DisableFormatting))).ToArray();

    private sealed record Owner(string Kind, Guid Id, Guid SourceId, XElement Saved, XElement Source,
        int Rating, string Extra, bool Enabled, string Path);

    private CharacterSheetOwnerGraph(XElement root, ICharacterSourceDataContext context, List<Func<bool>> checks,
        CharacterFinalizedLifeModuleOwnership? finalizedModules)
    { this.root = root; this.context = context; this.checks = checks; this.finalizedModules = finalizedModules;
        sources = context as ICharacterSheetOwnerSourceContext; }

    internal static CharacterSheetOwnerGraph Resolve(XElement root, ICharacterSourceDataContext context,
        List<Func<bool>> checks, XElement metatype, XElement settings, Guid metatypeOwnerId,
        out CharacterSheetOwnerGraph? traitEvidence, CharacterCreationPriorityGrantAuthority3563? priorityGrants = null,
        CharacterFinalizedLifeModuleOwnership? finalizedModules = null)
    {
        traitEvidence = null;
        var graph = new CharacterSheetOwnerGraph(root, context, checks, finalizedModules);
        if (finalizedModules is not null) graph.sourceEvidence.Add(finalizedModules.Digest);
        graph.ObserveSprite5751(metatype);
        graph.ReadImportedGrades5719(priorityGrants, settings);
        graph.BindImportedPowerRead5715(priorityGrants);
        foreach (var (container, kind, entry) in new[] {
            ("qualities", "Quality", "quality"), ("cyberwares", "Cyberware", "cyberware"),
            ("biowares", "Bioware", "bioware"), ("powers", "Power", "power"),
            ("armors", "Armor", "armor"), ("gear", "Gear", "gear"), ("gears", "Gear", "gear"),
            ("weapons", "Weapon", "weapon"), ("spells", "Spell", "spell"),
            ("vehicles", "Vehicle", "vehicle"), ("complexforms", "ComplexForm", "complexform"),
            ("critterpowers", "CritterPower", "critterpower") })
            graph.ReadContainer(root, container, kind, entry, true, container, 0);
        graph.ReadImportedMentors5715();
        graph.ReadNativeTradition3751();
        if (priorityGrants?.IsImported5693 == true) graph.ReadNativeSpirits5724();
        graph.ReadNativeLifestyles3751();
        graph.ReadRetainedFocusLinks6511();
        foreach (string container in new[] { "drugs", "martialarts", "sustainedspells",
            "stackedfoci", "mentors" })
            Need(Node(root, container)?.HasElements != true, "owner-domain-unconsumed", container);
        Need(Node(root, "metamagics")?.HasElements != true || graph.RetainedGrades5719 is not null,
            "owner-domain-unconsumed", "metamagics");
        foreach (var node in root.Descendants().Where(n => Node(n, "sourceid") is not null
            && !n.Ancestors().Any(a => a.Name.LocalName is "bonus" or "wirelessbonus" or "pairbonus" or "wirelesspairbonus")))
            Need(graph.owners.Any(o => ReferenceEquals(o.Saved, node)), "unvisited-owner", node.Name.LocalName);
        graph.BindTraitWeapons();
        graph.BindMetatypeGrants(metatype, metatypeOwnerId);
        graph.BindQualityGrants();
        graph.BindMatrixOwners3474();
        traitEvidence = graph;
        graph.Compile(metatype, settings, priorityGrants);
        graph.ReadNativeSustaining5064(settings);
        return graph;
    }

    private void ReadNativeLifestyles3751()
    {
        var container = Node(root, "lifestyles");
        if (container is null) return;
        Need(!container.HasAttributes && container.Nodes().All(n => n is XElement
            || n is XText text && string.IsNullOrWhiteSpace(text.Value)), "owner-container-invalid", "lifestyles");
        if (!container.HasElements) return;
        Need(context.TryResolveCreationLifestylesAuthority(out var authority)
            && CharacterCreationLifestylesRules.IsValidAuthority(authority)
            && authority.SettingsProfileId == Text(root, "settings"), "lifestyle-authority-unavailable", "lifestyles");
        string captured = authority.AuthorityDigest;
        sourceEvidence.Add(captured);
        checks.Add(() => context.TryResolveCreationLifestylesAuthority(out var final)
            && CharacterCreationLifestylesRules.IsValidAuthority(final) && final.AuthorityDigest == captured);
        int position = 0;
        foreach (var saved in container.Elements())
        {
            string path = "lifestyles/" + (position++).ToString(CultureInfo.InvariantCulture);
            Need(saved.Name == "lifestyle" && !saved.HasAttributes && owners.Count < 4096, "owner-shape", path);
            Guid id = Id(saved, "guid"), sourceId = Id(saved, "sourceid");
            Need(instances.Add(id), "duplicate-owner-instance", path);
            var source = ResolveSource("Lifestyle", sourceId, saved, path);
            // Economic catalog projection alone does not consume arbitrary bonuses.
            // Free grids may be enabled by the sourcebook even when the setting is false.
            // Resolve generated children through the same current authority and writer.
            string[] scalars = ["id", "name", "translate", "cost", "dice", "lp", "multiplier",
                "costforarea", "costforcomforts", "costforsecurity", "allowbonuslp", "increment", "source", "page", "altpage"];
            Need(source.Name == "lifestyle" && !source.HasAttributes
                && source.Nodes().All(n => n is XElement || n is XText text && string.IsNullOrWhiteSpace(text.Value))
                && source.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1)
                && source.Elements().All(e => e.Name.Namespace == XNamespace.None && !e.HasAttributes
                    && (e.Name == "freegrids" || scalars.Contains(e.Name.LocalName) && !e.HasElements)),
                "lifestyle-source-mechanic-unconsumed", path);
            var grids = Node(source, "freegrids");
            Need(grids is null || grids.Nodes().All(n => n is XText text && string.IsNullOrWhiteSpace(text.Value)
                || n is XElement grid && grid.Name == "freegrid" && !grid.HasElements
                    && grid.Attributes().All(a => a.Name == "select")), "lifestyle-source-mechanic-unconsumed", path);
            Need(Text(source, "increment", "").Trim().ToLowerInvariant() is "" or "day" or "week" or "month"
                && (!Flag(source, "allowbonuslp", false) || authority.LifestyleOptions.Single(o => o.SourceId == sourceId).AllowsBonusLifestylePoints),
                "lifestyle-source-mechanic-unconsumed", path);
            Need(CharacterCreationLifestylesService.TryReadConfiguration(saved, id, authority, out var configuration, out _),
                "lifestyle-saved-configuration-invalid", path);
            CharacterCreationLifestyleProjection projection;
            bool career = Flag(root, "created");
            if (career)
            {
                // Career's source projector preserves signed saved intervals and checks
                // aggregate arithmetic. The complete saved row is still compared below.
                try
                {
                    Need(CharacterCareerLifestyleMaintenanceProjection.TryProject(configuration, authority,
                        out var careerProjection, out _) && careerProjection is not null,
                        "lifestyle-projection-unavailable", path);
                    projection = careerProjection!;
                }
                catch (OverflowException) { throw Error("lifestyle-projection-unavailable", path); }
            }
            else
                Need(CharacterCreationLifestylesRules.TryProject(configuration, authority, out projection, out _),
                    "lifestyle-projection-unavailable", path);
            var native = CharacterCreationLifestylesService.BuildLifestyleElement(projection, null, authority);
            // Career maintenance writes these optional caches after the shared writer.
            // Re-derive their exact values; neither saved cache supplies price authority.
            if (career)
            {
                if (Node(saved, "totalmonthlycost") is not null)
                    native.Add(new XElement("totalmonthlycost", projection.Economics.CostPerIncrement.ToString(CultureInfo.InvariantCulture)));
                if (Node(saved, "totalcost") is not null)
                    native.Add(new XElement("totalcost", projection.Economics.TotalCost.ToString(CultureInfo.InvariantCulture)));
            }
            var normalized = Parse(saved.ToString(SaveOptions.DisableFormatting));
            // Compare mechanics after validating only the writer-retained scalar display
            // fields on these copies. Original saved nodes still enter traversal/digests.
            RemoveNativeLifestyleDisplay3768(normalized, path);
            RemoveNativeLifestyleDisplay3768(native, path);
            Need(saved.Nodes().All(n => n is XElement || n is XText text && string.IsNullOrWhiteSpace(text.Value))
                && normalized.Elements().Count() == native.Elements().Count()
                && native.Elements().All(field => XNode.DeepEquals(field, Node(normalized, field.Name.LocalName))),
                "lifestyle-saved-source-mismatch", path);
            // Include both complete rows in the owner digest and normal effect graph.
            owners.Add(new Owner("Lifestyle", id, sourceId, saved, source, 1, "", true, path));
            // The complete native comparison above requires every deterministic child,
            // including its GUID, flags, choice and all economic/source fields. Register
            // the actual saved nodes so exhaustive traversal also covers nested owners.
            int qualityPosition = 0;
            foreach (var quality in Node(saved, "lifestylequalities")!.Elements())
            {
                string qualityPath = path + "/lifestylequalities/" + (qualityPosition++).ToString(CultureInfo.InvariantCulture);
                Guid qualityId = Id(quality, "guid"), qualitySourceId = Id(quality, "sourceid");
                Need(owners.Count < 4096 && instances.Add(qualityId), "duplicate-owner-instance", qualityPath);
                var selected = projection.Configuration.Qualities.Single(q => q.InstanceId == qualityId);
                var option = authority.QualityOptions.Single(q => q.OptionId == selected.OptionId);
                Need(option.SourceId == qualitySourceId && option.IsSelectable && option.EligibilityIsExact,
                    "lifestyle-quality-source-unavailable", qualityPath);
                var qualitySource = ResolveSource("LifestyleQuality", qualitySourceId, quality, qualityPath);
                string[] qualityScalars = ["id", "name", "translate", "category", "lp", "cost", "multiplier",
                    "multiplierbaseonly", "area", "comforts", "security", "areamaximum", "comfortsmaximum",
                    "securitymaximum", "allowed", "source", "page", "altpage"];
                Need(qualitySource.Name == "quality" && !qualitySource.HasAttributes
                    && qualitySource.Nodes().All(n => n is XElement || n is XText text && string.IsNullOrWhiteSpace(text.Value))
                    && qualitySource.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1)
                    && qualitySource.Elements().All(e => e.Name.Namespace == XNamespace.None && !e.HasAttributes
                        && (e.Name == "bonus" || qualityScalars.Contains(e.Name.LocalName) && !e.HasElements
                            || e.Name == "allowmultiple" && !e.HasElements && string.IsNullOrWhiteSpace(e.Value))),
                    "lifestyle-quality-source-mechanic-unconsumed", qualityPath);
                if (!selected.IsBuiltIn)
                    BindSelectedLifestyleQuality3775(qualitySource, option, authority, qualityPath);
                owners.Add(new Owner("LifestyleQuality", qualityId, qualitySourceId, quality, qualitySource,
                    1, selected.Extra, true, qualityPath));
            }
        }
    }

    private static void BindSelectedLifestyleQuality3775(XElement source,
        CharacterCreationLifestyleQualityCatalogOption option, CharacterCreationLifestylesAuthority authority, string path)
    {
        // The source whitelist above bounds mechanics; the current catalogue and
        // projector own eligibility, LP, costs and choices. Bind their consumed
        // values to this independently resolved owner source, never saved caches.
        Need(Text(source, "name").Trim() == option.Name && Text(source, "category").Trim() == option.Category
            && Text(source, "source").Trim() == option.SourceBook && Text(source, "page").Trim() == option.Page,
            "lifestyle-quality-projection-source-mismatch", path);
        Need(CharacterCurrentRuleEnvironmentConsumers.TryLifestyleScalar2678(Text(source, "cost", ""), out decimal cost)
            && cost == option.FlatCost, "lifestyle-quality-projection-source-mismatch", path + "/cost");
        foreach (var (field, value) in new (string, decimal)[]
        {
            ("lp", option.LifestylePointCost), ("multiplier", option.CostMultiplierPercent),
            ("multiplierbaseonly", option.BaseCostMultiplierPercent), ("area", option.Area),
            ("comforts", option.Comforts), ("security", option.Security),
            ("areamaximum", option.AreaMaximumModifier), ("comfortsmaximum", option.ComfortsMaximumModifier),
            ("securitymaximum", option.SecurityMaximumModifier)
        })
        {
            string raw = Text(source, field, "").Trim();
            Need(decimal.TryParse(raw.Length == 0 ? "0" : raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
                && parsed == value, "lifestyle-quality-projection-source-mismatch", path + "/" + field);
        }
        var allowed = Text(source, "allowed", "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Need(allowed.ToHashSet(StringComparer.Ordinal).SetEquals(option.AllowedFreeLifestyleNames),
            "lifestyle-quality-projection-source-mismatch", path + "/allowed");
        Need((option.SelectionSemantics?.AllowMultiple ?? false) == (Node(source, "allowmultiple") is not null)
            && (option.SelectionSemantics?.RequiresText ?? false) == (Node(Node(source, "bonus") ?? new XElement("bonus"), "selecttext") is not null),
            "lifestyle-quality-projection-source-mismatch", path + "/selection");
        // Older projections consume aspect maxima but not total-aspect deltas.
        Need(authority.Mechanics is not null || option.Area == 0 && option.Comforts == 0 && option.Security == 0,
            "lifestyle-quality-source-mechanic-unconsumed", path + "/aspects");
        // CompileNativeLifestyleQuality3761 separately consumes a plain selecttext
        // into Extra. Other bonuses never become inferred character improvements.
    }

    private static void RemoveNativeLifestyleDisplay3768(XElement lifestyle, string path)
    {
        // These five fields alone are passive in the existing maintenance reader and
        // retained by its writer. In particular, an absent child print is retained too.
        foreach (var row in (Node(lifestyle, "lifestylequalities")?.Elements("lifestylequality") ?? []).Prepend(lifestyle))
        foreach (string name in new[] { "notes", "notesColor", "customname", "sortorder", "print" })
        {
            var field = Node(row, name); // Enforce a single, unnamespaced field.
            if (field is null) continue;
            Need(!field.HasAttributes && field.Nodes().All(n => n is XText),
                "lifestyle-saved-source-mismatch", path);
            field.Remove();
        }
    }

    private void CompileNativeLifestyleQuality3761(Owner owner)
    {
        // Lifestyle selection consumes this prompt into the canonical saved extra;
        // BuildLifestyleElement does not emit a Text improvement. This is not the
        // ordinary quality dispatcher. No other bonus/effect is admitted here.
        var bonus = Node(owner.Source, "bonus");
        Need(bonus is null || !bonus.HasAttributes && bonus.Nodes().All(n => n is XElement
            || n is XText text && string.IsNullOrWhiteSpace(text.Value)),
            "lifestyle-quality-source-mechanic-unconsumed", owner.Path);
        if (bonus?.HasElements != true)
        {
            Need(owner.Extra.Length == 0, "lifestyle-quality-choice-unconsumed", owner.Path);
            return;
        }
        Need(bonus.Elements().ToArray() is [var prompt] && prompt.Name == "selecttext"
            && !prompt.HasAttributes && !prompt.HasElements && prompt.Nodes().All(n => n is XText)
            && string.IsNullOrWhiteSpace(prompt.Value) && !string.IsNullOrWhiteSpace(owner.Extra),
            "lifestyle-quality-source-mechanic-unconsumed", owner.Path);
        boundChoices.Add(owner.Id);
    }

    private void ReadContainer(XElement parent, string container, string kind, string entry,
        bool parentEnabled, string path, int depth)
    {
        var node = Node(parent, container);
        if (node is null) return;
        Need(depth < 16 && !node.HasAttributes && !node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)),
            "owner-container-invalid", path);
        int position = 0;
        foreach (var row in node.Elements())
        {
            string at = path + "/" + (position++).ToString(CultureInfo.InvariantCulture);
            Need(row.Name == entry && !row.HasAttributes && owners.Count < 4096, "owner-shape", at);
            Guid id = Id(row, "guid"), sourceId = Id(row, "sourceid");
            Need(instances.Add(id), "duplicate-owner-instance", at);
            // Legacy saves all ware as cyberware; improvement source selects the catalog.
            string actualKind = kind == "Cyberware" ? Text(row, "improvementsource", "Cyberware") : kind;
            Need(actualKind == kind || kind == "Cyberware" && actualKind == "Bioware", "ware-source-kind", at);
            XElement source;
            if (actualKind == "Quality" && (Text(row, "qualitytype", "") == "LifeModule"
                || Text(row, "qualitysource", "") == "LifeModule"))
            {
                XElement? module = null;
                Need(finalizedModules is not null && finalizedModules.TryGetModuleDefinition(row, out module)
                    && module is not null, "finalized-module-owner-unavailable", at);
                actualKind = "LifeModule";
                source = module!;
            }
            else source = ResolveSource(actualKind, sourceId, row, at);
            int rating = Int(row, "rating", actualKind is "Quality" or "LifeModule" or "Spell" ? 1 : 0);
            Need(rating >= 0, "negative-owner-rating", at);
            if (actualKind is "Cyberware" or "Bioware" or "Gear")
            {
                int min = ExactInt(Expression(Text(source, "minrating", "0"), rating));
                int max = ExactInt(Expression(Text(source, "rating", "0"), rating));
                Need(rating >= min && rating <= max, "owner-rating-outside-source", at);
            }
            if (actualKind == "Power")
            {
                Need(Flag(root, "adept", false), "power-without-adept", at);
                rating = PowerTotalRating5715(row, source, rating, at);
                Need(CharacterCreationAdeptPowerSourceRules.TryReadMaximumLevels(source, out int max, out _),
                    "power-source-rating-cap-invalid", at);
                Need(rating <= max, "power-rating-outside-source", at);
            }
            if (actualKind == "Quality")
            {
                rating = 1; // Native qualities are one instance per level, not a cached rating.
                qualityLevels.TryGetValue(sourceId, out int level);
                // SelectionShared.RequirementsMet: literal False means no copy limit.
                // It is a source-owned cardinality flag, not a malformed numeric rating.
                string limit = Text(source, "limit", "1");
                Need(limit == "False" || level < Int(source, "limit", 1), "quality-level-outside-source", at);
                qualityLevels[sourceId] = level + 1;
            }
            bool enabled = parentEnabled;
            if (actualKind is "Gear" or "Armor" or "Weapon" or "WeaponAccessory") enabled &= Flag(row, "equipped", true);
            if (actualKind == "Power") enabled &= rating > 0;
            var owner = new Owner(actualKind, id, sourceId, row, source, rating, Text(row, "extra", ""), enabled, at);
            owners.Add(owner);
            ValidateSavedMechanics(owner);
            ValidateSourceShape(owner);
            if (actualKind is "Cyberware" or "Bioware") ReadWare(owner);
            ReadContainer(row, "gears", "Gear", "gear", enabled, at + "/gears", depth + 1);
            ReadContainer(row, "gear", "Gear", "gear", enabled, at + "/gear", depth + 1);
            if (actualKind == "Gear") ReadContainer(row, "children", "Gear", "gear", enabled, at + "/children", depth + 1);
            if (actualKind == "Weapon") ReadContainer(row, "accessories", "WeaponAccessory", "accessory", enabled, at + "/accessories", depth + 1);
            // Child ware Essence/capacity and limb aggregation require their own typed projection.
            foreach (string children in new[] { "children", "cyberwares", "biowares", "armormods", "weapons", "mods", "enhancements" })
            {
                if (actualKind == "Gear" && children == "children") continue;
                Need(Node(row, children)?.HasElements != true, "owner-child-semantics-unconsumed", at + "/" + children);
            }
        }
    }

    private XElement ResolveSource(string kind, Guid sourceId, XElement saved, string path)
    {
        CharacterSheetOwnerSource? receipt = null;
        // Preserve old resolvers for the original quality-only API. Equipped authority
        // requires the production extension, not a caller-supplied source XML fallback.
        if (sources is null && kind == "Quality")
        {
            Need(context.TryResolveCareerQualitySource(sourceId, out var quality) && quality is not null,
                "quality-source-unavailable", path);
            var q = quality!;
            sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(q));
            checks.Add(() => context.TryResolveCareerQualitySource(sourceId, out var final) && final == q);
            var xml = Parse(q.RawSourceXml);
            Need(q.SourceId == sourceId && q.Name == Text(saved, "name") && q.SourceBook == Text(saved, "source"),
                "owner-source-identity", path);
            return xml;
        }
        Need(sources is not null && sources.TryResolveSheetOwnerSource(kind, sourceId, out receipt)
            && receipt is not null, "owner-source-disabled-missing-or-drifted", path);
        var bound = receipt!;
        Need(new[] { bound.CatalogDigest, bound.CustomDataDigest, bound.ProfileDigest, bound.PolicyDigest }
            .All(CharacterCreationFinalizationDigest.IsCanonical), "source-receipt-digest", path);
        sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(bound));
        // The source ID owns this equipped item; its saved label can predate a
        // catalog rename. Keep scalar/name shape checks without treating that
        // presentation change as an invalid owner or substituting saved effects.
        string savedName = Text(saved, "name");
        Need(bound.Kind == kind && bound.SourceId == sourceId
            && !string.IsNullOrWhiteSpace(bound.Name) && !string.IsNullOrWhiteSpace(savedName)
            && savedName == savedName.Trim()
            && bound.SourceBook == Text(saved, "source"), "owner-source-identity", path);
        checks.Add(() => sources!.TryResolveSheetOwnerSource(kind, sourceId, out var final) && final == bound);
        var source = Parse(bound.SourceXml);
        Need(Id(source, "id") == sourceId && Text(source, "name") == bound.Name && Text(source, "source") == bound.SourceBook,
            "source-receipt-identity", path);
        return source;
    }

    private void ValidateSavedMechanics(Owner owner)
    {
        if (owner.Kind == "Vehicle") ValidateVehicle3474(owner);
        if (owner.Kind == "ComplexForm") ValidateComplexForm3474(owner);
        if (owner.Kind == "Gear")
        {
            string category = Text(owner.Source, "category", "");
            if (Node(owner.Source, "requireparent") is not null)
                Need(Empty3474(owner.Source, "requireparent") && owner.Saved.Parent?.Parent is { } parent
                    && !ReferenceEquals(parent, root) && owners.Any(o => ReferenceEquals(o.Saved, parent)),
                    "gear-required-parent-missing", owner.Path);
            if (category == "Foci")
            {
                BindRetainedFocusOwner6511(owner);
                return; // The shared focus compiler checked every saved field and effect.
            }
            Need(category != "Stacked Focus" && !category.EndsWith("Foci", StringComparison.Ordinal),
                "focus-bonding-graph-unconsumed", owner.Path);
            Need(category is not ("Drugs" or "Drug Components"), "drug-active-state-unconsumed", owner.Path);
        }
        foreach (string field in new[] { "bonus", "wirelessbonus", "pairbonus", "wirelesspairbonus", "firstlevelbonus" })
        {
            var saved = Node(owner.Saved, field);
            var source = Node(owner.Source, field);
            if (saved is not null && (saved.HasElements || saved.HasAttributes || !string.IsNullOrWhiteSpace(saved.Value)))
            {
                // Quality.Save and BuildSavedQuality/CloneContainer write only the
                // source container's inner XML. Accept that exact native form when
                // all container attributes are absent; retained attributes still
                // have to match. Keep descendant nodes and attributes unchanged.
                // Compile continues to consume owner.Source, including its attributes.
                if (owner.Kind == "Quality" && (field is "bonus" or "firstlevelbonus")
                    && !saved.HasAttributes && source is not null)
                    source = new XElement(source.Name, source.Nodes());
                // Typed grants use XElement.Parse (insignificant whitespace ignored),
                // whereas callers can supply an XmlReader-preserved saved tree. Reparse
                // only the compared containers with that same XML whitespace policy.
                // DeepEquals still binds every attribute, comment, element and text value.
                Need(source is not null && XNode.DeepEquals(
                    Parse(saved.ToString(SaveOptions.DisableFormatting)),
                    Parse(source.ToString(SaveOptions.DisableFormatting))),
                    "saved-bonus-source-mismatch", owner.Path + "/" + field);
            }
        }
        foreach (string field in new[] { "pairbonus", "modularmount", "plugsintomodularmount",
            "limbslot", "limbslotcount", "addweapon", "addvehicle", "subsystems" })
        {
            if (owner.Kind == "Quality" && field == "addweapon") continue; // Bound after all owners are read.
            Need(Node(owner.Source, field) is not { } node || !node.HasElements && string.IsNullOrWhiteSpace(node.Value),
                "owner-source-mechanic-unconsumed", owner.Path + "/" + field);
        }
        foreach (var (container, entry) in new[] { ("gears", "gear"), ("accessories", "accessory") })
        {
            if (owner.Kind == "Vehicle" && container == "gears") continue; // Typed factory projection below owns every child.
            if (owner.Kind == "Gear" && container == "gears") { ValidateGearChildren3474(owner); continue; }
            foreach (var child in Node(owner.Source, container)?.Elements() ?? [])
            {
                Need(child.Name == entry, "source-generated-child-shape", owner.Path);
                string name = Text(child, "name");
                Need((Node(owner.Saved, container)?.Elements() ?? []).Count(c => Text(c, "name", "") == name) == 1,
                    "source-generated-child-missing-or-duplicate", owner.Path + "/" + container);
            }
        }
        if (Node(owner.Source, "wirelesspairbonus")?.HasElements == true)
            Need(!Flag(owner.Saved, "wirelesson", false), "wireless-pair-graph-unconsumed", owner.Path);
        if (owner.Kind == "Armor")
        {
            string armor = Text(owner.Source, "armor");
            Need(int.TryParse(armor, NumberStyles.None, CultureInfo.InvariantCulture, out _), "stacking-armor-unconsumed", owner.Path);
            Need(Text(owner.Saved, "armor") == armor && Text(owner.Saved, "armoroverride", "") is "" or "0"
                && Text(owner.Source, "armoroverride", "") is "" or "0", "armor-source-override", owner.Path);
            Need(Flag(owner.Saved, "encumbrance", true) == Flag(owner.Source, "encumbrance", true), "armor-encumbrance-mismatch", owner.Path);
        }
        if (owner.Kind == "Spell")
            Need(Node(owner.Source, "bonus")?.HasElements != true, "spell-bonus-unconsumed", owner.Path);
    }

    private void ReadWare(Owner owner)
    {
        Need(!Flag(owner.Saved, "isgeneware", false) && Text(owner.Source, "isgeneware", "False") != "True"
            && !Flag(owner.Source, "geneware", false) && !Flag(owner.Saved, "prototypetranshuman", false),
            "geneware-essence-unconsumed", owner.Path);
        Need(Dec(owner.Saved, "essdiscount", 0) == 0 && Dec(owner.Saved, "extraessadditivemultiplier", 0) == 0
            && Dec(owner.Saved, "extraessmultiplicativemultiplier", 1) == 1,
            "ware-acquisition-essence-history-unconsumed", owner.Path);
        string grade = Text(owner.Saved, "grade");
        Need(!(Node(owner.Source, "bannedgrades")?.Elements("grade") ?? []).Any(g => g.Value == grade),
            "ware-banned-grade", owner.Path);
        Need(Text(owner.Source, "forcegrade", "") is "" || Text(owner.Source, "forcegrade") == grade,
            "ware-forced-grade", owner.Path);
        Need(sources!.TryResolveSheetWareGrade(owner.Kind, grade, out var receipt) && receipt is not null,
            "ware-grade-source-unavailable", owner.Path);
        var captured = receipt!;
        Need(captured.Kind == owner.Kind + "Grade" && captured.Name == grade,
            "grade-receipt-identity", owner.Path);
        sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(captured));
        checks.Add(() => sources.TryResolveSheetWareGrade(owner.Kind, grade, out var final) && final == captured);
        var gradeSource = Parse(captured.SourceXml);
        Need(Id(gradeSource, "id") == captured.SourceId && Text(gradeSource, "name") == grade,
            "grade-source-identity", owner.Path);
        Need(gradeSource.Elements().All(e => new[] { "id", "name", "translate", "ess", "cost", "devicerating", "avail", "source", "page", "altpage" }.Contains(e.Name.LocalName)),
            "ware-grade-mechanic-unconsumed", owner.Path);
        Need(Dec(gradeSource, "ess") >= 0, "ware-grade-essence", owner.Path);
        ware.Add((owner, gradeSource));
    }

    private void Compile(XElement metatype, XElement settings, CharacterCreationPriorityGrantAuthority3563? priorityGrants)
    {
        BindSkillUnlockChoices3778(priorityGrants);
        var first = new HashSet<Guid>();
        foreach (var owner in owners)
        {
            if (finalizedModules?.OwnsQuality(owner.Saved) == true)
            {
                // The native sequence planner owns module instructions, dependent
                // qualities and level winners as one graph. Keep every saved owner;
                // do not dispatch that graph again through ordinary quality bonuses.
                // The module planner owns legacy rows, but the retained source-only
                // defense penalty has no legacy ImprovementType or saved row.
                CompileFinalizedSourceDefense8587(owner);
                expected.AddRange(finalizedModules.EffectsForOwner(owner.Id));
                boundChoices.Add(owner.Id);
                if (owner.Kind == "Quality") first.Add(owner.SourceId);
                continue;
            }
            if (CompileRetainedFocusOwner6511(owner)) continue;
            if (CompileImportedGradeOwner5719(owner)) continue;
            if (owner.Kind == "LifestyleQuality") CompileNativeLifestyleQuality3761(owner);
            else CompilePowerGrantContainer5715(owner, Node(owner.Source, "bonus"), owner.Enabled, "");
            if (owner.Kind == "MentorSpirit") CompileMentorChoices5715(owner);
            if (owner.Kind == "Quality" && first.Add(owner.SourceId))
                CompileContainer(owner, Node(owner.Source, "firstlevelbonus"), owner.Enabled, "");
            var wireless = Node(owner.Source, "wirelessbonus");
            if (wireless?.HasElements == true)
            {
                // Replacement and pair wireless effects have distinct source-name/disable graphs.
                Need(wireless.Attribute("mode") is null, "wireless-replacement-unconsumed", owner.Path);
                if (Flag(owner.Saved, "wirelesson", false) && owner.Enabled)
                    CompileContainer(owner, wireless, true, "Wireless");
            }
        }
        Need(CareerPowers5715 is null || consumedPowerGrants5715.Count == CareerPowers5715.ProvenGrants.Count,
            "power-grant-origin-unconsumed", "powers");
        Need(owners.Where(o => o.Kind == "MentorSpirit").All(o => selectedMentors5715.Contains(o.Id)),
            "mentor-selection-origin-unconsumed", "mentorspirits");
        foreach (var owner in owners)
            Need(boundChoices.Contains(owner.Id) || owner.Extra.Length == 0 || owner.Kind is "Gear" or "Weapon" or "Armor" or "Spell" or "WeaponAccessory",
                "unbound-owner-choice", owner.Path);
        ValidateGrantedGear3474();
        foreach (var limit in Node(root, "limitmodifiers")?.Elements() ?? [])
            Need(Flag(limit, "candelete", true) || limitIds.Contains(Id(limit, "guid")), "unbound-owned-limit", "limitmodifiers");
        var disabled = expected.Where(e => Text(e, "improvementttype") == "DisableQuality" && Flag(e, "enabled"))
            .Select(e => Text(e, "improvedname")).ToHashSet(StringComparer.Ordinal);
        foreach (var quality in owners.Where(o => o.Kind is ("Quality" or "LifeModule") && (disabled.Contains(o.SourceId.ToString("D"))
            || disabled.Contains(Text(o.Source, "name")))))
        {
            var rows = expected.Where(e => Text(e, "sourcename") == quality.Id.ToString("D")).ToArray();
            Need(rows.All(e => Text(e, "improvementttype") != "DisableQuality"), "quality-suppression-cycle-unconsumed", quality.Path);
            suppressedOwners.Add(quality.Id);
            foreach (var row in rows) row.SetElementValue("enabled", "0");
        }
        // Disabling a SpecificQuality grant disables the child's effects in legacy.
        // Closure was checked before compilation, so this cannot introduce a cycle.
        bool changed;
        do
        {
            changed = false;
            foreach (var (child, parent) in qualityGrantParents)
                if (suppressedOwners.Contains(parent) && suppressedOwners.Add(child))
                {
                    var rows = expected.Where(e => Text(e, "sourcename") == child.ToString("D")).ToArray();
                    Need(rows.All(e => Text(e, "improvementttype") != "DisableQuality"), "quality-suppression-cycle-unconsumed", "qualities");
                    foreach (var row in rows) row.SetElementValue("enabled", "0");
                    changed = true;
                }
        } while (changed);
        foreach (var (id, position) in ownedLimitPositions)
            ConditionalLimits[position] = ConditionalLimits[position] with { Enabled = expected.Any(e =>
                Text(e, "improvementttype") == "LimitModifier" && Text(e, "improvedname") == id.ToString("D") && Flag(e, "enabled")) };
        CompileSpiritFettering5724(settings);
        Effects = expected.ToArray();
        UnlockedSkills.RemoveAll(s => !Effects.Any(e => Active(e) && Text(e, "improvementttype") == "SpecialSkills"
            && Text(e, "improvedname") == "Name" && Text(e, "target") == s.Name
            && Text(e, "sourcename") == s.OwnerId.ToString("D")));
        foreach (string id in NamedSkillSources.Keys.Where(id => !UnlockedSkills.Any(s => s.SourceId.ToString("D") == id)).ToArray())
            NamedSkillSources.Remove(id);
        ValidateAwakenedFlags();
        CompileSpecialAttributes(metatype, settings, priorityGrants);
        CompilePrioritySkillGrants3563(priorityGrants);
        ValidateSpriteValues5751(settings);
        var saved = Node(root, "improvements")?.Elements().ToArray() ?? [];
        Need(saved.All(e => e.Name == "improvement"), "improvement-node", "improvements");
        var savedKeys = saved.Select(Key).Order(StringComparer.Ordinal).ToArray();
        var expectedKeys = expected.Select(Key).Order(StringComparer.Ordinal).ToArray();
        if (!savedKeys.SequenceEqual(expectedKeys))
        {
            string detail = saved.Length != expected.Count ? "count" : "content-or-state";
            throw Error("owner-effect-graph-mismatch", "improvements/" + detail);
        }
        // Use source-compiled rows only after exact graph equality, including disabled rows.
        Effects = expected.ToArray();
        AuthorityDigest = CharacterCreationFinalizationDigest.Compute(new XElement("owners",
            new XAttribute("semantics", Semantics),
            owners.Select(o => new XElement("owner", new XAttribute("kind", o.Kind), new XAttribute("id", o.Id),
                o.Source, o.Saved)), new XElement("sources", sourceEvidence.Order(StringComparer.Ordinal).Select(d => new XElement("digest", d))),
            new XElement("effects", Effects)).ToString(SaveOptions.DisableFormatting));
    }

    private void ValidateAwakenedFlags()
    {
        bool Has(string type, string name) => Effects.Any(e => Text(e, "improvementttype") == type
            && Text(e, "improvedname") == name && Active(e));
        Need(Flag(root, "magenabled", false) == Has("Attribute", "MAG"), "magic-enable-graph-mismatch", "magenabled");
        Need(Flag(root, "adept", false) == Has("SpecialTab", "Adept")
            && Flag(root, "magician", false) == Has("SpecialTab", "Magician"), "magic-tab-graph-mismatch", "talent");
        Need(Flag(root, "critter", false) == Has("SpecialTab", "Critter"), "critter-tab-graph-mismatch", "critter");
        Need(Flag(root, "resenabled", false) == Has("Attribute", "RES")
            && Flag(root, "technomancer", false) == Has("SpecialTab", "Technomancer"),
            "resonance-enable-graph-mismatch", "resenabled");
        Need(!Flag(root, "resenabled", false) || Flag(root, "technomancer", false)
            && !Flag(root, "magenabled", false) && !Flag(root, "adept", false) && !Flag(root, "magician", false),
            "resonance-talent-conflict", "resenabled");
    }

    internal decimal Essence(XElement metatype, XElement settings)
    {
        int decimals = Int(settings, "essencedecimals", 2);
        Need(decimals is >= 2 and <= 28, "essence-rounding-profile", "settings");
        bool unrounded = Flag(settings, "donotroundessenceinternally", false);
        Need(Dec(root, "prototypetranshuman", 0) == 0, "prototype-allowance-unconsumed", "essence");
        decimal total = 0;
        foreach (var (owner, grade) in ware)
        {
            string expression = Text(settings, "essencemodifierpostexpression", "{Modifier}");
            if (expression.Length == 0) expression = "{Modifier}";
            decimal modifier = CharacterCreationEssenceExpression1222.Evaluate(expression,
                new Dictionary<string, decimal> { ["Modifier"] = Dec(grade, "ess"), ["Rating"] = owner.Rating,
                    ["MinRating"] = Expression(Text(owner.Source, "minrating", "0"), owner.Rating) });
            Need(modifier >= 0, "essence-post-modifier-range", "settings");
            total = checked(total + CharacterCreationEssenceRules1222.Ware(
                Expression(Text(owner.Source, "ess"), owner.Rating), modifier, 1, decimals, unrounded));
        }
        Need(Dec(root, "essencehole", 0) == 0 && Dec(root, "essenceanti-hole", 0) == 0,
            "essence-hole-history-unconsumed", "essence");
        return checked(Dec(metatype, "essmax") - total);
    }

    internal bool Active(XElement effect) => Flag(effect, "enabled") && Text(effect, "condition", "") is var c
        && (c == "" || c == (Flag(root, "created") ? "career" : "create"));

    internal void ValidateSkillSelections(IReadOnlyList<CharacterSheetSkillValue> skills, XElement settings)
    {
        foreach (string name in spriteSkills5751)
            Need(skills.Count(s => s.Name == name && s.LearnedRating is not null && s.UnavailableReason is null) == 1,
                "sprite-generated-skill-unconsumed", "skills");
        foreach (var (name, minimum) in selectedSkillMinimums)
            Need(skills.Count(s => s.Name == name && s.LearnedRating >= minimum && s.UnavailableReason is null) == 1,
                "skill-choice-minimum-unresolved", "skills");
        // Power.TotalMaximumLevels has this native BoostedSkill rule for the
        // source-named power. It is a rule semantic, never an item admission list.
        foreach (var power in owners.Where(o => o.Kind == "Power" && Text(o.Source, "name") == "Improved Ability (skill)"))
        {
            var skill = skills.SingleOrDefault(s => s.Name == power.Extra);
            Need(skill?.LearnedRating is not null && skill.UnavailableReason is null, "power-boosted-skill-unresolved", power.Path);
            int learned = skill!.LearnedRating!.Value;
            int maximum = checked((learned + 1) / 2 + (Flag(settings, "increasedimprovedabilitymultiplier", false) ? learned : 0));
            Need(power.Rating <= maximum, "power-boosted-skill-rating-cap", power.Path);
        }
    }

    internal static XElement Parse(string xml)
    {
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, IgnoreWhitespace = true, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XElement.Load(reader);
    }
    internal static XElement? Node(XElement p, string n)
    {
        var nodes = p.Elements().Where(e => e.Name.LocalName == n).ToArray();
        Need(nodes.Length <= 1 && nodes.All(e => e.Name == n), "duplicate-or-namespaced-field", n);
        return nodes.SingleOrDefault();
    }
    internal static string Text(XElement p, string n, string? fallback = null)
    {
        var node = Node(p, n);
        Need(node is not null || fallback is not null, "missing-field", n);
        Need(node is null || !node.HasAttributes && !node.HasElements, "non-scalar-field", n);
        return node?.Value ?? fallback!;
    }
    internal static int Int(XElement p, string n, int? fallback = null) => ExactInt(Dec(p, n, fallback));
    internal static decimal Dec(XElement p, string n, decimal? fallback = null)
    {
        Need(decimal.TryParse(Text(p, n, fallback?.ToString(CultureInfo.InvariantCulture)), NumberStyles.AllowLeadingSign
            | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal v), "invalid-number", n);
        return v;
    }
    internal static bool Flag(XElement p, string n, bool? fallback = null) => Text(p, n, fallback?.ToString()) switch
    { "True" or "true" or "1" => true, "False" or "false" or "0" => false, _ => throw Error("invalid-boolean", n) };
    internal static Guid Id(XElement p, string n)
    { Need(Guid.TryParse(Text(p, n), out var id) && id != Guid.Empty, "invalid-identity", n); return id; }
    internal static int ExactInt(decimal v)
    { Need(v == decimal.Truncate(v), "non-integer", "expression"); return checked((int)v); }
    internal static decimal Expression(string text, int rating) => CharacterCreationEssenceExpression1222.Evaluate(text,
        new Dictionary<string, decimal>(StringComparer.Ordinal) { ["Rating"] = rating });
    internal static void Need(bool value, string code, string path) { if (!value) throw Error(code, path); }
    internal static InvalidDataException Error(string code, string path) => new("sheet:" + code + ":" + path[..Math.Min(path.Length, 160)]);
}
