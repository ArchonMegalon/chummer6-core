using Chummer.Application.Session;
using Chummer.Contracts.Session;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// In-process read evidence only. Never serialized, accepted from a proposal, or
// used to price an acquisition. Every replay must resolve the original again.
internal sealed class CharacterCreationOriginalInventory3475
{
    private readonly XElement original;
    private readonly ICharacterSourceDataResolver resolver;
    private readonly string sourceDigest;
    private readonly CharacterCreationPriorityGrantAuthority3563? priorityGrants;
    private readonly LoadoutValidation8553 loadoutValidation;
    internal string AuthorityDigest => sourceDigest;
    private static readonly string[] containers = ["gears", "armors", "weapons", "vehicles"];

    private CharacterCreationOriginalInventory3475(XElement original,
        ICharacterSourceDataResolver resolver, string sourceDigest, CharacterCreationPriorityGrantAuthority3563? priorityGrants,
        LoadoutValidation8553 loadoutValidation)
    { this.original = new(original); this.resolver = resolver; this.sourceDigest = sourceDigest; this.priorityGrants = priorityGrants;
        this.loadoutValidation = loadoutValidation; }

    internal static bool HasInventory(XElement root) => containers.Any(name =>
        root.Elements(name).Any(container => container.HasElements));

    internal static bool TryCapture(XElement original, ICharacterSourceDataResolver? resolver,
        out CharacterCreationOriginalInventory3475? evidence, CharacterCreationPriorityGrantAuthority3563? priorityGrants = null)
        => TryCaptureInventory3847(original, resolver, out evidence, priorityGrants, finalizedPhysical: false);

    // Only the finalization execution calls this entry after its real Career
    // transition. The old Creation-only capture remains unchanged for all callers.
    // No false-created clone or caller digest stands in for the finalized graph.
    internal static bool TryCaptureFinalizedPhysical3847(XElement final, ICharacterSourceDataResolver resolver,
        out CharacterCreationOriginalInventory3475? evidence, CharacterCreationPriorityGrantAuthority3563? priorityGrants)
        => TryCaptureInventory3847(final, resolver, out evidence, priorityGrants, finalizedPhysical: true);

    private static bool TryCaptureInventory3847(XElement original, ICharacterSourceDataResolver? resolver,
        out CharacterCreationOriginalInventory3475? evidence, CharacterCreationPriorityGrantAuthority3563? priorityGrants,
        bool finalizedPhysical)
    {
        evidence = null;
        var createdFields3847 = original.Elements().Where(row => row.Name.LocalName == "created").ToArray();
        bool lifecycle3847 = finalizedPhysical
            ? original.Name == "character" && createdFields3847 is [var field3847] && field3847.Name == "created"
                && !field3847.HasAttributes && !field3847.HasElements && bool.TryParse(field3847.Value, out bool created3847) && created3847
            : CharacterCreationOriginalCharacterSource.HasSingleCreationField(new XDocument(new XElement(original)));
        if (resolver is null || !HasInventory(original)
            || !lifecycle3847
            || !CharacterCreationOwnedGearGraph1233.Empty(original, "cyberwares")
            || !CharacterCreationOwnedGearGraph1233.Empty(original, "biowares")
            || !CharacterCreationOwnedGearGraph1233.Empty(original, "gear")
            || !CharacterCreationOwnedGearGraph1233.Empty(original, "foci")
            || !CharacterCreationOwnedGearGraph1233.Empty(original, "stackedfoci")) return false;
        try
        {
            var loadoutValidation = new LoadoutValidation8553();
            if (!ReadCurrent(original, resolver, priorityGrants, loadoutValidation, out string? digest)) return false;
            evidence = new(original, resolver, digest!, priorityGrants, loadoutValidation);
            return true;
        }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    // Both consumers must receive this exact private proof. Recheck its whole
    // graph/source contract and require exactly the original loadout containers.
    internal bool MatchesLoadout3478(XElement compiled)
        => HasBoundInventoryTraversal3518(compiled) && Matches(compiled) && new[] { "weapons", "armors" }.All(name =>
            XNode.DeepEquals(original.Element(name), compiled.Element(name)));

    // A completed typed transfer has already compiled every purchase. Its final
    // handoff may change lifecycle/cash/reputation, but cannot acquire another
    // owner or change any inventory field while recapturing the Career proof.
    // Keep ordinary Matches unchanged: pre-transfer compilation still adds buys.
    internal bool MatchesExactPhysicalInventory3861(XElement graph, ICharacterSourceDataResolver compiledResolver)
        => compiledResolver is not null && HasBoundInventoryTraversal3518(graph)
            && containers.All(name => XNode.DeepEquals(original.Element(name), graph.Element(name)))
            && MatchesWithCompiledResolver3861(graph, compiledResolver);

    // Only the private captured original can supply retained owners. New owners
    // are replayed from the CURRENT typed catalog, never accepted by DTO digest.
    // The seed is the actual compiler seed; missing seeds refuse nonempty baskets.
    internal bool TryCombineLoadout3497(XElement graph, CharacterCreationLoadoutCompletionProjection proposal,
        string? seed, out XElement? combined)
    {
        combined = null;
        if (!MatchesLoadout3478(graph)
            || !TryLoadoutContainers3497(proposal, seed, out var weapons, out var armor)) return false;
        var candidate = new XElement(graph);
        Install(candidate, "weapons", weapons);
        Install(candidate, "armors", armor);
        if (!Matches(candidate)) return false;
        combined = candidate;
        return true;
    }

    internal bool MatchesCombinedLoadout3497(XElement graph,
        CharacterCreationLoadoutCompletionProjection? proposal, string? seed)
    {
        try
        {
            if (proposal is null || !HasBoundInventoryTraversal3518(graph)) return false;
            // One comparison owns both original fences and the priced basket
            // replay. Reuse source parsing within that synchronous lifetime;
            // every current context and every domain comparison still executes.
            using var operation = (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var reader = operation ?? resolver;
            using var sheetOperation = priorityGrants?.IsImported5693 == true ? null
                : (reader as ICharacterCreationRetainedSheetScopeFactory8573)?.CreateRetainedCreationSheetScope8573();
            reader = sheetOperation ?? reader;
            using var sheets = reader is ICharacterCreationRetainedSheetScope8573 retained
                && priorityGrants?.IsImported5693 != true && retained.IsRetainedCreationSheetScope8573()
                    ? new CharacterCreationInventorySheetRead8573(reader, priorityGrants) : null;
            return MatchesWithResolvers8556(graph, reader, reader, sheets)
                && TryLoadoutContainers3497(proposal, seed, out var weapons, out var armor, reader, sheets)
                && CharacterCreationOwnedGearGraph1233.SameStructure1277(graph.Element("weapons"), weapons)
                && CharacterCreationOwnedGearGraph1233.SameStructure1277(graph.Element("armors"), armor)
                && MatchesWithResolvers8556(graph, reader, reader, sheets);
        }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    // The sheet reader visits canonical inventory roots, and its unvisited-owner
    // guard finds sourceid-bearing owners. A hidden owner without sourceid must
    // not disappear from this consumer's traversal. This is only a boundary
    // check: Matches still replays the complete owner/effect sources, and the
    // combined weapon/armor containers still require the exact priced basket.
    private static bool HasBoundInventoryTraversal3518(XElement graph)
    {
        string[] rootNames = ["gears", "gear", "armors", "weapons", "vehicles",
            "cyberwares", "biowares", "foci", "stackedfoci"];
        var roots = graph.Elements().Where(node => rootNames.Any(name => node.Name == name)).ToHashSet();
        var inventoryNames = new HashSet<string>(rootNames, StringComparer.OrdinalIgnoreCase)
            { "armor", "weapon", "vehicle", "cyberware", "bioware", "focus", "stackedfocus",
              "accessories", "accessory", "armormods", "armormod", "weaponmounts", "weaponmount" };
        return graph.Descendants().Where(node => inventoryNames.Contains(node.Name.LocalName))
            .All(node => node.AncestorsAndSelf().Any(roots.Contains));
    }

    private bool TryLoadoutContainers3497(CharacterCreationLoadoutCompletionProjection proposal, string? seed,
        out XElement? weapons, out XElement? armor, ICharacterSourceDataResolver? sourceReader = null,
        CharacterCreationInventorySheetRead8573? sheets = null)
    {
        weapons = null; armor = null;
        try
        {
            var reader = sourceReader ?? resolver;
            using var operation = (reader as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            reader = operation ?? reader;
            if (proposal is not { Lines: not null, Blockers.Count: 0 }
                || !CharacterCreationLoadoutCompletionService.IsValidSelections(proposal.Selections)
                || proposal.ProjectionDigest != CharacterCreationLoadoutCompletionRules.ComputeProjectionDigest(proposal)
                || proposal.Lines.Count != 0 && !CharacterCreationFinalizationDigest.IsCanonical(seed)
                || !ReadCurrent(original, reader, priorityGrants, loadoutValidation, out var before, out var currentLoadout, sheets) || before != sourceDigest) return false;
            var context = reader.TryCreateContext(original.ToString(SaveOptions.DisableFormatting));
            if (context is null || !context.TryResolveCreationLoadoutAuthority(out var authority)
                || !loadoutValidation.TryRead(authority, out var validated, out var currentAuthority)
                || authority.SettingsProfileId != original.Element("settings")?.Value
                || currentAuthority != currentLoadout
                || !validated!.TryProjectBasket(proposal.Selections,
                    out var lines, out decimal cost, out var blockers) || blockers.Length != 0
                || proposal.ProjectionDigest != CharacterCreationLoadoutCompletionService.BuildProjection(
                    proposal.Selections, lines, cost, proposal.RetainedInstances).ProjectionDigest
                || !CharacterCreationLoadoutSourceProjector.TryBuildGraph(lines, seed ?? sourceDigest,
                    out var addedWeapons, out var addedArmor, proposal.RetainedInstances)) return false;
            weapons = Combine("weapons", addedWeapons);
            armor = Combine("armors", addedArmor);
            return context.TryResolveCreationLoadoutAuthority(out var after)
                && loadoutValidation.TryRead(after, out _, out var afterDigest) && currentAuthority == afterDigest
                && ReadCurrent(original, reader, priorityGrants, loadoutValidation, out var final, sheets) && final == sourceDigest;
        }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    private XElement? Combine(string container, XElement[] added)
    {
        var saved = original.Element(container);
        if (saved is null && added.Length == 0) return null;
        var result = saved is null ? new XElement(container) : new XElement(saved);
        result.Add(added);
        return result;
    }

    private static void Install(XElement graph, string name, XElement? value)
    {
        if (value is null) return; // preserve actual absence for empty proposals
        if (graph.Element(name) is { } prior) prior.ReplaceWith(value);
        else graph.Add(value);
    }

    internal bool Matches(XElement compiled)
        => MatchesWithCompiledResolver3861(compiled, resolver);

    private bool MatchesWithCompiledResolver3861(XElement compiled, ICharacterSourceDataResolver compiledResolver)
    {
        try
        {
            using var operation = (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var reader = operation ?? resolver;
            return MatchesWithResolvers8556(compiled,
                ReferenceEquals(compiledResolver, resolver) ? reader : compiledResolver, reader);
        }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    private bool MatchesWithResolvers8556(XElement compiled, ICharacterSourceDataResolver compiledResolver,
        ICharacterSourceDataResolver originalResolver, CharacterCreationInventorySheetRead8573? sheets = null)
    {
        try
        {
            // A second owner or changed child cannot hide behind an unchanged top
            // owner ID. Compare the complete original subtrees, without filtering.
            var ids = new HashSet<Guid>();
            foreach (var guid in compiled.Descendants("guid"))
                if (!Guid.TryParse(guid.Value, out var id) || id == Guid.Empty || !ids.Add(id)) return false;
            foreach (string name in containers)
            {
                var target = compiled.Elements(name).ToArray();
                if (target.Length > 1) return false;
                foreach (var retained in original.Element(name)?.Elements() ?? [])
                    if ((target.SingleOrDefault()?.Elements() ?? []).Count(candidate =>
                        CharacterCreationOwnedGearGraph1233.GraphDigest(candidate)
                            == CharacterCreationOwnedGearGraph1233.GraphDigest(retained)) != 1) return false;
            }
            // The current compiler replaces improvement containers. It may not
            // obtain success by losing an original effect. Such originals remain
            // unsupported unless the compiled graph really retains every row.
            foreach (var group in (original.Element("improvements")?.Elements() ?? [])
                .GroupBy(CharacterCreationOwnedGearGraph1233.GraphDigest))
                if ((compiled.Element("improvements")?.Elements() ?? []).Count(row =>
                    CharacterCreationOwnedGearGraph1233.GraphDigest(row) == group.Key) < group.Count()) return false;
            if (!ReadCurrent(original, originalResolver, priorityGrants, loadoutValidation, out var before, sheets) || before != sourceDigest) return false;
            // Only the candidate sheet uses its separately issued exact reader.
            // Both original-source rechecks still use this proof's original
            // resolver and digest; a new reader cannot replace failed evidence.
            return ReadSheet8573(compiled.ToString(SaveOptions.DisableFormatting), compiledResolver, priorityGrants, sheets, out _)
                && ReadCurrent(original, originalResolver, priorityGrants, loadoutValidation, out var after, sheets) && after == sourceDigest;
        }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    private static bool ReadCurrent(XElement root, ICharacterSourceDataResolver resolver,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants, LoadoutValidation8553 loadoutValidation, out string? digest,
        CharacterCreationInventorySheetRead8573? sheets = null)
        => ReadCurrent(root, resolver, priorityGrants, loadoutValidation, out digest, out _, sheets);

    private static bool ReadCurrent(XElement root, ICharacterSourceDataResolver resolver,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants, LoadoutValidation8553 loadoutValidation,
        out string? digest, out string? loadoutDigest, CharacterCreationInventorySheetRead8573? sheets = null)
    {
        loadoutDigest = null;
        digest = null;
        // One synchronous inventory read already owns the before/after checks.
        // Keep its source parsing/projections in the resolver's existing bounded
        // scope instead of opening separate scopes for each sheet/round reader.
        // Every context still freshly admits its source bytes and membership.
        // An enclosing combined comparison may retain only its exact private
        // sheet digest; no result crosses an operation or await.
        using var operation = (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        resolver = operation ?? resolver;
        string xml = root.ToString(SaveOptions.DisableFormatting);
        if (!ReadSheet8573(xml, resolver, priorityGrants, sheets, out var sheetDigest)) return false;
        if (root.Element("weapons")?.HasElements == true || root.Element("armors")?.HasElements == true)
        {
            var context = resolver.TryCreateContext(xml);
            if (context is null || !context.TryResolveCreationLoadoutAuthority(out var authority)
                || !loadoutValidation.TryRead(authority, out var validated, out var currentAuthority)
                || authority.SettingsProfileId != root.Element("settings")?.Value) return false;
            var roundOwners3847 = new HashSet<Guid>();
            if (root.Descendants("clips").Any(clips => clips.HasElements))
            {
                // Loaded originals require the same live source/whole-sheet and
                // conserved inventory authority as physical readiness. Their clips
                // remain exact original nodes; no hash or public flag approves them.
                var fire = SessionPlayWeaponAuthority.ResolveForPhysicalRoutine3828(root, xml, resolver,
                    new List<SessionPlayDiagnostic>(), out var custody, priorityGrants);
                if (custody is null) return false;
                roundOwners3847.UnionWith(fire.Select(state => state.Identity.WeaponId));
            }
            foreach (var row in (root.Element("weapons")?.Elements() ?? []).Concat(root.Element("armors")?.Elements() ?? []))
                if (!StockLoadout(row, validated!.Authority, Guid.TryParse(row.Element("guid")?.Value, out var owner3847)
                    && roundOwners3847.Contains(owner3847))) return false;
            if (!context.TryResolveCreationLoadoutAuthority(out var final)
                || !loadoutValidation.TryRead(final, out _, out var finalAuthority) || currentAuthority != finalAuthority) return false;
            loadoutDigest = currentAuthority;
        }
        // Re-read after the independent typed loadout reader as well.
        if (!ReadSheet8573(xml, resolver, priorityGrants, sheets, out var finalDigest) || finalDigest != sheetDigest) return false;
        digest = CharacterCreationFinalizationDigest.Compute(new { AuthorityDigest = sheetDigest, Loadout = loadoutDigest });
        return true;
    }

    private static bool ReadSheet8573(string xml, ICharacterSourceDataResolver resolver,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants, CharacterCreationInventorySheetRead8573? sheets,
        out string? digest)
        => sheets is null ? CharacterCreationInventorySheetRead8573.TryReadCold(xml, resolver, priorityGrants, out digest)
            : sheets.TryRead(xml, resolver, priorityGrants, out digest);

    // Repeated inventory checks still resolve and hash the entire CURRENT catalog,
    // and still re-read both sheets and all stock owners. Only pure grammar/cost
    // validation of an exact, privately detached catalog is retained. A supplied
    // AuthorityDigest or reference identity cannot pass this comparison. Drift
    // invalidates this inventory proof permanently; it never refreshes the cache.
    private sealed class LoadoutValidation8553
    {
        private CharacterCreationLoadoutRules.ValidatedLoadoutEvaluation6218? catalog;
        private string? payloadDigest;
        private bool invalidated;

        internal bool TryRead(CharacterCreationLoadoutAuthority? current,
            out CharacterCreationLoadoutRules.ValidatedLoadoutEvaluation6218? validated, out string? digest)
        {
            validated = null;
            digest = null;
            if (invalidated) return false;
            invalidated = true; // Exceptions and failed reads poison this proof too.
            if (current is null) return false;
            string actual = CharacterCreationFinalizationDigest.Compute(current);
            var owned = catalog ?? CharacterCreationLoadoutRules.ValidatedLoadoutEvaluation6218.Create(current);
            if (owned is null) return false;
            string expected = payloadDigest ?? CharacterCreationFinalizationDigest.Compute(owned.Authority);
            if (!string.Equals(actual, expected, StringComparison.Ordinal)) return false;
            catalog = owned;
            payloadDigest = expected;
            validated = owned;
            digest = actual;
            invalidated = false;
            return true;
        }
    }

    // The imported Career consumer has already reconciled every sheet owner and
    // effect. Reuse this exact stock-source comparison, admitting only the item
    // damage counter additionally owned by the existing armor-damage kernel.
    internal static bool IsCurrentStockOwner5696(XElement actual, CharacterCreationLoadoutAuthority authority,
        bool roundCustodyProven5701 = false)
        => StockLoadout(actual, authority, roundCustodyProven5701, currentCareer5696: true);

    private static bool StockLoadout(XElement actual, CharacterCreationLoadoutAuthority authority, bool roundCustodyProven3847,
        bool currentCareer5696 = false)
    {
        if (actual.Nodes().Any(node => node is not XElement
                && (node.GetType() != typeof(XText) || !string.IsNullOrWhiteSpace(((XText)node).Value)))
            || !Guid.TryParse(actual.Element("sourceid")?.Value, out var sourceId)
            || !Guid.TryParse(actual.Element("guid")?.Value, out var id) || id == Guid.Empty) return false;
        var options = authority.Options.Where(o => o.Kind == actual.Name.LocalName && o.SourceId == sourceId).ToArray();
        if (options is not [var option] || !option.IsSelectable) return false;
        var line = new CharacterCreationLoadoutLine(option.OptionId, option.Kind, option.SourceId,
            option.Name, option.Category, 1, option.UnitCost, option.UnitCost, option.Availability,
            option.Legality, option.SourceBook, option.Page, option.SourceAnchorIds.ToArray(),
            option.SourceNodeXml, option.SourceNodeDigest, option.SourceCategoryXml, option.SourceCategoryDigest, string.Empty);
        line = line with { LineDigest = CharacterCreationLoadoutRules.ComputeLineDigest(line) };
        if (!CharacterCreationLoadoutSourceProjector.TryBuildGraph([line], CharacterCreationOwnedGearGraph1233.GraphDigest(actual),
            out var weapons, out var armors)) return false;
        var expected = weapons.Concat(armors).Single();
        expected.SetElementValue("guid", actual.Element("guid")!.Value);
        if (currentCareer5696 && actual.Name == "armor")
        {
            // Global racial Armor belongs to Character armor, not this item's
            // degradation limit. StockLoadout still excludes every armor mod.
            var source = XElement.Parse(option.SourceNodeXml);
            if (!CharacterArmorDamageRules.TryCalculateMaximum(source.Element("armor")?.Value,
                    source.Element("armoroverride")?.Value, 0, [], out int maximum)
                || actual.Elements("damage").ToArray() is not [var damage] || damage.HasAttributes || damage.HasElements
                || !int.TryParse(damage.Value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int value) || value > maximum) return false;
            expected.SetElementValue("damage", damage.Value);
        }
        // Only player presentation and validated equip switches can differ from
        // the native typed projection. This is comparison, never a payload edit.
        foreach (string field in new[] { "notes", "notesColor", "location", "sortorder", "weaponname", "armorname", "equipped", "wirelesson" })
        {
            if (expected.Element(field) is null) continue;
            if (actual.Elements(field).ToArray() is not [var value] || value.HasAttributes || value.HasElements) return false;
            if (field is "equipped" or "wirelesson" && !bool.TryParse(value.Value, out _)) return false;
            if (field == "sortorder" && !int.TryParse(value.Value, out _)) return false;
            expected.SetElementValue(field, value.Value);
        }
        // Native Save may emit these empty collections. Nonempty attachments,
        if (actual.Element("clips") is { HasElements: true } clips3847)
        {
            if (!roundCustodyProven3847 || actual.Name != "weapon" || clips3847.HasAttributes
                || clips3847.Elements().ToArray() is not [var clip3847] || clip3847.Name != "clip"
                || clip3847.HasAttributes || clip3847.Elements().Count() != 2
                || clip3847.Elements().Any(row => row.Name != "id" && row.Name != "count"
                    || row.HasElements || row.HasAttributes)
                || clip3847.Elements("id").Count() != 1 || clip3847.Elements("count").Count() != 1) return false;
            expected.Add(new XElement(clips3847));
        }
        else if (actual.Element("clips") is { } emptyClips3847) expected.Add(new XElement(emptyClips3847));
        // Attachments and modifications still require their own authority.
        foreach (string name in new[] { "accessories", "gears", "weapons", "children" })
        {
            if (!CharacterCreationOwnedGearGraph1233.Empty(actual, name)) return false;
            if (actual.Element(name) is { } empty && expected.Element(name) is null) expected.Add(new XElement(empty));
        }
        // Native element order is presentation; names/cardinality and all values
        // remain exact. The returned runner is never reordered or normalized.
        static XElement Sorted(XElement node) => new(node.Name, node.Attributes(),
            node.Elements().OrderBy(e => e.Name.ToString(), StringComparer.Ordinal).Select(e => new XElement(e)));
        return CharacterCreationOwnedGearGraph1233.GraphDigest(Sorted(actual))
            == CharacterCreationOwnedGearGraph1233.GraphDigest(Sorted(expected));
    }

    // Native capacity traversal still visits ammunition Gear. Ordinary cartridge
    // sources have no capacity and no child allocation. Prove those precise source
    // and saved fields through this private live inventory capture, not a category
    // label, purchase receipt, or caller digest.
    internal bool IsCapacityFreeOrdinaryAmmunition3847(XElement node)
    {
        if (node.Name != "gear" || node.Parent?.Name != "gears" || node.Parent.Parent?.Name != "character"
            || !(original.Element("gears")?.Elements("gear") ?? []).Any(saved =>
                CharacterCreationOwnedGearGraph1233.GraphDigest(saved) == CharacterCreationOwnedGearGraph1233.GraphDigest(node))) return false;
        var context = resolver.TryCreateContext(original.ToString(SaveOptions.DisableFormatting));
        return context is not null && IsCapacityFreeOrdinaryAmmunition5701(node, context);
    }

    internal static bool IsCapacityFreeOrdinaryAmmunition5701(XElement node, ICharacterSourceDataContext context)
    {
        if (context is not ICharacterSheetOwnerSourceContext sources
            || !sources.TryResolveSheetNamedSource("Gear", "Ammo: Regular Ammo", out var source) || source is null
            || !Guid.TryParse(node.Element("sourceid")?.Value, out var sourceId) || sourceId != source.SourceId
            || !context.TryResolveCreationGearAuthority(out var authority)) return false;
        var option = authority.Options.SingleOrDefault(row => row.SourceId == sourceId);
        if (option is null || !XNode.DeepEquals(XElement.Parse(option.SourceNodeXml), XElement.Parse(source.SourceXml))
            || !CharacterCreationAmmunitionCompletionConsumer.TrySelect(authority,
                new(option.OptionId, 1, option.SourceNodeDigest), out var selection, out _)
            || !CharacterCreationGearRules.TryProjectBasket([selection!], authority, option.PackageCost, out var lines, out _, out _)
            || !CharacterCreationLegacySourceProjector.TryBuildGear(lines.Single(), option.SourceNodeDigest, out var canonical)) return false;
        foreach (string field in new[] { "capacity", "armorcapacity" })
            if (canonical.Element(field)?.Value is not (null or "" or "0")
                || !XNode.DeepEquals(node.Element(field), canonical.Element(field))) return false;
        return new[] { "children", "gears", "weapons", "bonus", "wirelessbonus" }
            .All(name => CharacterCreationOwnedGearGraph1233.Empty(node, name));
    }

    private static bool Malformed(Exception error) => error is IOException or XmlException
        or ArgumentException or InvalidOperationException or FormatException or OverflowException;
}
