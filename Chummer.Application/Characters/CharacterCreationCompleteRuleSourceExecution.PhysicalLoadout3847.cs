using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Session;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    internal sealed record PhysicalCandidate3847(CharacterCreationCompletionPlanRequest Plan,
        CharacterCreationInitialAmmunitionLoad3847 Load);
    internal sealed record PhysicalLoadDelta3847(string Id, IReadOnlyList<string> Anchors);

    // Newly purchased physical kits need the same native stock/capacity execution
    // as retained kits. The compiler already proved prices and original ownership;
    // this method independently proves the CURRENT whole stock graph through the
    // retained resolver. It does not turn the selected DTO into inventory proof.
    internal CharacterCreationNativeLoadoutProjection1432? EvaluateSourceProvenPhysicalLoadout3847(
        XElement root, CharacterCreationPrerequisiteDraft draft, CharacterCreationOriginalInventory3475? loadedStock3861 = null)
    {
        if (request.Selections.InitialAmmunitionLoads3847 is not { Count: > 0 }) return null;
        var candidate = CompiledGraphResolver(root);
        if (candidate is null) return null;
        using var scope = (candidate as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        if (loadedStock3861 is null
            && !TryCapturePhysicalStock3861(root, scope ?? candidate, out loadedStock3861)) return null;
        return CharacterCreationNativeLoadout1432.Evaluate(root, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy,
            draft, EmptyPhysicalStockBasket3861(), loadedStock3861, requestDigest);
    }

    // All owners are already in this private stock proof. This empty consumer
    // basket neither reprices nor replaces the original purchase projection.
    internal static CharacterCreationLoadoutCompletionProjection EmptyPhysicalStockBasket3861()
    {
        var empty = new CharacterCreationLoadoutCompletionProjection(CharacterCreationLoadoutSelections.Empty, [], 0m, [], "");
        return empty with { ProjectionDigest = CharacterCreationLoadoutCompletionRules.ComputeProjectionDigest(empty) };
    }

    // The Career graph may change lifecycle, cash and reputation after loading,
    // but its whole inventory must still match the exact authorized transfer.
    // Reconstruct final stock only after that check, and recheck the transfer's
    // live source proof after capture. No failed proof can fall back to recapture.
    internal bool TryFinalizePhysicalStock3861(XElement root, CharacterCreationOriginalInventory3475 loaded,
        out CharacterCreationOriginalInventory3475? finalStock)
    {
        finalStock = null;
        var candidate = CompiledGraphResolver(root);
        if (candidate is null || !loaded.MatchesExactPhysicalInventory3861(root, candidate)) return false;
        var context = candidate.TryCreateContext(root.ToString(SaveOptions.DisableFormatting));
        if (context is null
            || !CharacterCreationOriginalInventory3475.TryCaptureFinalizedPhysical3847(root, candidate,
                out var stock, original.PriorityGrants3563(context))
            || !loaded.MatchesExactPhysicalInventory3861(root, candidate)) return false;
        finalStock = stock;
        return stock is not null;
    }

    private bool TryCapturePhysicalStock3861(XElement root, ICharacterSourceDataResolver resolver,
        out CharacterCreationOriginalInventory3475? stock)
    {
        stock = null;
        var context = resolver.TryCreateContext(root.ToString(SaveOptions.DisableFormatting));
        if (context is null) return false;
        var grants = original.PriorityGrants3563(context);
        return bool.TryParse(root.Element("created")?.Value, out bool created) && created
            ? CharacterCreationOriginalInventory3475.TryCaptureFinalizedPhysical3847(root, resolver, out stock, grants)
            : CharacterCreationOriginalInventory3475.TryCapture(root, resolver, out stock, grants);
    }

    internal IReadOnlyList<PhysicalCandidate3847> PhysicalCandidates3847(
        CharacterCreationRoleRoutineObjective routine, CharacterCreationCompletionBaseline basis,
        int maximum, out string blocker)
    {
        blocker = CharacterCreationRoleCompletionPlanner.PhysicalSourceUnavailable3847;
        if (routine.Action is not { AccessMode: "physical-single-shot", Target: "", ForceOrLevel: null,
                VehicleSourceId: null, PhysicalAttack: null } || routine.MagicIdentity is not null
            || routine.Conditions is not { Consumables: true, UseEdge: false, ExternalAssistance: false,
                SpriteAssistance: false, SustainedEffects: 0, Preparation: "", Situation: "ordinary" })
            return [];
        string xml = basis.Finalization.CharacterXml;
        var root = RoleXml(xml);
        var candidate = CompiledGraphResolver(root);
        if (candidate is null) return [];
        using var scope = (candidate as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var resolver = scope ?? candidate;
        xml = root.ToString(SaveOptions.DisableFormatting);
        var context = resolver.TryCreateContext(xml);
        if (context is not ICharacterSheetOwnerSourceContext sources
            || !context.TryResolveCreationLoadoutAuthority(out var catalog)
            || !CharacterCreationLoadoutRules.IsValidAuthority(catalog)
            || !sources.TryResolveSheetNamedSource("Gear", "Ammo: Regular Ammo", out var regular)
            || regular is null || !context.TryResolveActiveSkillSource(routine.SkillSourceId, out var skillSource)
            || skillSource.IsExotic) return [];
        var sheet = CharacterSheetDerivedQuery.ParseForRuleEffects2912(xml, resolver, out var owners,
            original.PriorityGrants3563(context));
        var learned = root.Element("newskills")?.Element("skills")?.Elements("skill")
            .Where(row => SameSource3847(row.Element("suid")?.Value, Guid.Parse(routine.SkillSourceId))).ToArray() ?? [];
        if (sheet.UnavailableReason is not null || owners is null || learned.Length != 1
            || !Guid.TryParse(learned[0].Element("guid")?.Value, out var skillId)
            || !sheet.Skills.Any(row => SameSource3847(row.Guid, skillId) && row.Name == skillSource.Name
                && row.Attribute == skillSource.DefaultAttribute && row.EffectiveRating > 0
                && row.DicePool >= routine.Targets.MinimumSkillDicePool && row.UnavailableReason is null))
        { blocker = CharacterCreationRoleCompletionPlanner.PhysicalSkillUnavailable3847; return []; }

        var held = root.Element("weapons")?.Elements("weapon").ToArray() ?? [];
        var gear = root.Element("gears")?.Elements("gear").ToArray() ?? [];
        var ammunition = gear.Where(row => SameSource3847(row.Element("sourceid")?.Value, regular.SourceId)).ToArray();
        // Preserve an existing line, including exhausted/limited ammunition. The
        // purchase planner does not silently increase locked original quantities.
        if (ammunition.Length > 1)
        { blocker = CharacterCreationRoleCompletionPlanner.PhysicalLocked3847; return []; }
        var ammoOption = Inputs.Lifestyles.Gear.State.Authority.Options
            .SingleOrDefault(row => row.SourceId == regular.SourceId);
        if (ammunition.Length == 0 && (ammoOption is null || !ammoOption.IsSelectable)) return [];
        if (ammunition.Length == 1 && (!int.TryParse(ammunition[0].Element("qty")?.Value,
                NumberStyles.None, CultureInfo.InvariantCulture, out int rounds) || rounds < 1))
        { blocker = CharacterCreationRoleCompletionPlanner.PhysicalAmmoUnavailable3847; return []; }

        var originalRoot = RoleXml(Inputs.OriginalCharacter!.RawCharacterXml);
        var candidates = new List<PhysicalCandidate3847>();
        foreach (var weapon in catalog.Options.Where(row => row.Kind == CharacterCreationLoadoutKinds.Weapon
                && CharacterCreationLoadoutRules.IsValidOption(row))
            .OrderBy(row => row.UnitCost).ThenBy(row => row.OptionId, StringComparer.Ordinal))
        {
            var source = RoleXml(weapon.SourceNodeXml);
            string skill = source.Element("useskill")?.Value ?? "";
            if (skill.Length == 0) skill = CharacterWeaponSkillRules3828.DictionaryKeyForCategory(weapon.Category, "");
            if (skill != skillSource.Name || source.Element("type")?.Value != "Ranged"
                || weapon.Category == "Special Weapons") continue;
            var selected = held.Where(row => SameSource3847(row.Element("sourceid")?.Value, weapon.SourceId)).ToArray();
            // Existing player equipment takes precedence. An incompatible or
            // ambiguous choice is never removed or replaced to obtain a green label.
            if (held.Length != 0 && selected.Length != 1) continue;
            if (selected.Length == 0 && (!weapon.IsSelectable || !weapon.PricingIsExact
                || !weapon.AvailabilityIsExact || weapon.Availability > catalog.MaximumAvailability)) continue;
            if (routine.RequiredEquipmentSourceIds.Any(id => !SameSource3847(id, weapon.SourceId)
                && !SameSource3847(id, regular.SourceId))) continue;
            if (!sources.TryResolveSheetOwnerSource("Weapon", weapon.SourceId, out var evidence) || evidence is null
                || !XNode.DeepEquals(source, RoleXml(evidence.SourceXml))) continue;
            var load = new CharacterCreationInitialAmmunitionLoad3847(weapon.SourceId,
                CharacterCreationFinalizationDigest.ComputeUtf8(evidence.SourceXml), regular.SourceId,
                CharacterCreationFinalizationDigest.ComputeUtf8(regular.SourceXml),
                OriginalId("weapons", "weapon", selected.SingleOrDefault()),
                OriginalId("gears", "gear", ammunition.SingleOrDefault()));
            var purchases = basis.Plan.Request.Purchases.ToList();
            if (selected.Length == 0) purchases.Add(new() { Weapon = new(weapon.OptionId) });
            if (ammunition.Length == 0)
                purchases.Add(new() { Ammunition = new(ammoOption!.OptionId, 1, ammoOption.SourceNodeDigest) });
            var loads = basis.Plan.Request.InitialAmmunitionLoads3847?.ToList() ?? [];
            if (!loads.Contains(load)) loads.Add(load);
            candidates.Add(new(basis.Plan.Request with { Purchases = purchases.ToArray(),
                InitialAmmunitionLoads3847 = loads.ToArray() }, load));
            if (candidates.Count >= maximum) break;
        }
        if (candidates.Count == 0 && held.Length != 0) blocker = CharacterCreationRoleCompletionPlanner.PhysicalLocked3847;
        return candidates;

        Guid? OriginalId(string container, string kind, XElement? row)
        {
            if (row is null || !Guid.TryParse(row.Element("guid")?.Value, out var id)) return null;
            return originalRoot.Element(container)?.Elements(kind)
                .Any(saved => SameSource3847(saved.Element("guid")?.Value, id)) == true ? id : null;
        }
    }

    internal CharacterCreationRoleRoutineObjective? BindPhysical3847(CharacterCreationRoleRoutineObjective routine,
        CharacterCreationInitialAmmunitionLoad3847 load, string xml)
    {
        var root = RoleXml(xml);
        var candidate = CompiledGraphResolver(root);
        if (candidate is null) return null;
        using var scope = (candidate as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var context = (scope ?? candidate).TryCreateContext(root.ToString(SaveOptions.DisableFormatting));
        if (!CurrentPhysicalSources3847(context, load)
            || SelectPhysicalOwner3847(root, "weapons", "weapon", load.WeaponSourceId, load.WeaponId) is not { } weapon
            || SelectPhysicalOwner3847(root, "gears", "gear", load.AmmunitionSourceId, load.AmmunitionId) is not { } ammo)
            return null;
        return routine with
        {
            RequiredEquipmentSourceIds = [load.WeaponSourceId.ToString("D"), load.AmmunitionSourceId.ToString("D")],
            Action = routine.Action! with { PhysicalAttack = new(Guid.Parse(weapon.Element("guid")!.Value),
                load.WeaponSourceId, load.WeaponSourceDigest, Guid.Parse(ammo.Element("guid")!.Value),
                load.AmmunitionSourceId, load.AmmunitionSourceDigest) }
        };
    }

    // Runs only inside canonical finalization after original-node preservation and
    // complete priced graph compilation. Apply replays this same method; no DTO
    // hash can create a resolver, source permission, inventory or a loaded round.
    private string? initialAmmunitionFailure3847;

    internal bool TryLoadInitialAmmunition3847(XElement root,
        IReadOnlyList<CharacterCreationInitialAmmunitionLoad3847> loads,
        out IReadOnlyList<PhysicalLoadDelta3847> deltas, out CharacterCreationOriginalInventory3475? loadedStock3861)
    {
        deltas = [];
        loadedStock3861 = null;
        initialAmmunitionFailure3847 = null;
        bool Refuse(string reason)
        { initialAmmunitionFailure3847 = "creation-completion.ammunition.load." + reason; return false; }
        if (loads.Count is < 1 or > 16 || loads.Any(load => load is null)) return Refuse("invalid-request");
        var projected = new XElement(root);
        var changes = new List<PhysicalLoadDelta3847>();
        var loadedIds = new HashSet<Guid>();
        try
        {
            foreach (var load in loads)
            {
                var candidate = CompiledGraphResolver(projected);
                if (candidate is null) return Refuse("compiled-source-view-unavailable");
                using var scope = (candidate as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
                var resolver = scope ?? candidate;
                string xml = projected.ToString(SaveOptions.DisableFormatting);
                var context = resolver.TryCreateContext(xml);
                if (PhysicalSourceFailure3847(context, load) is { } sourceFailure) return Refuse(sourceFailure);
                if (SelectPhysicalOwner3847(projected, "weapons", "weapon", load.WeaponSourceId, load.WeaponId) is not { } weapon)
                    return Refuse("weapon-owner-unavailable");
                if (SelectPhysicalOwner3847(projected, "gears", "gear", load.AmmunitionSourceId, load.AmmunitionId) is not { } ammo)
                    return Refuse("ammunition-owner-unavailable");
                Guid weaponId = Guid.Parse(weapon.Element("guid")!.Value), ammoId = Guid.Parse(ammo.Element("guid")!.Value);
                if (!loadedIds.Add(weaponId)) return Refuse("duplicate-weapon");
                var grants = original.PriorityGrants3563(context!);
                var diagnostics = new List<SessionPlayDiagnostic>();
                var states = SessionPlayWeaponAuthority.ResolveForPhysicalRoutine3828(projected, xml, resolver,
                    diagnostics, out var custody, grants);
                if (custody is null)
                {
                    string code = diagnostics.FirstOrDefault()?.Code ?? "unavailable";
                    if (code.Length is < 1 or > 128 || code.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
                        code = "unavailable";
                    return Refuse("custody-unavailable." + code);
                }
                var fire = states.SingleOrDefault(row => row.Identity.WeaponId == weaponId);
                var owned = custody.Weapons.SingleOrDefault(row => row.WeaponId == weaponId);
                if (fire is null || owned is null || !owned.CompatibleGearIds.Contains(ammoId)
                    || !fire.Modes.Any(mode => mode.Mode == CharacterWeaponFireMode.SingleShot && mode.Rounds == 1)) return Refuse("compatible-single-shot-unavailable");
                // Never switch ammunition or unload player choices. A ready owned
                // clip is reused byte-for-byte, even when it contains many rounds.
                if (owned.LoadedRounds > 0)
                { if (owned.LoadedGearId != ammoId) return Refuse("existing-loaded-choice-locked"); else continue; }
                if (owned.Feeds.Count != 1 || owned.Feeds[0].Capacity < 1) return Refuse("feed-unavailable");
                var transfer = new SessionPlayAmmoTransfer(SessionPlayAmmoTransferKind.Reload, weaponId,
                    ammoId, owned.Feeds[0].Id, 1, fire.Revision, custody.Revision);
                var transferred = SessionPlayAmmunitionReducer.Transfer(custody,
                    states.ToDictionary(row => row.Identity.WeaponId.ToString("D")), transfer,
                    CharacterCreationFinalizationDigest.Compute(new { requestDigest, transfer }));
                // Native Weapon.Save clip representation. Quantity remains total
                // owned cartridges; the reducer proves available + loaded = total.
                weapon.Element("clips")?.Remove();
                weapon.Add(new XElement("clips", new XElement("clip",
                    new XElement("id", ammoId.ToString("D")), new XElement("count", "1"))));
                string loadedXml = projected.ToString(SaveOptions.DisableFormatting);
                var loadedCandidate = CompiledGraphResolver(projected);
                if (loadedCandidate is null) return Refuse("loaded-source-view-unavailable");
                using var loadedScope = (loadedCandidate as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
                var loadedResolver = loadedScope ?? loadedCandidate;
                var after = SessionPlayWeaponAuthority.ResolveForPhysicalRoutine3828(projected, loadedXml, loadedResolver,
                    new List<SessionPlayDiagnostic>(), out var verified, grants);
                if (verified is null || after.Count != states.Count
                    || !SameRoundCustody3847(transferred, verified)
                    || !CurrentPhysicalSources3847(loadedResolver.TryCreateContext(loadedXml), load)) return Refuse("transfer-verification-failed");
                if (!context!.TryResolveCreationLoadoutAuthority(out var catalog)) return Refuse("catalog-unavailable");
                var anchors = catalog.Options.Single(row => row.SourceId == load.WeaponSourceId && row.Kind == "weapon")
                    .SourceAnchorIds.Concat(Inputs.Lifestyles.Gear.State.Authority.Options
                        .Where(row => row.SourceId == load.AmmunitionSourceId).SelectMany(row => row.SourceAnchorIds))
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                changes.Add(new($"weapon:{weaponId:D}:ammunition:{ammoId:D}", anchors));
            }
            // Capture the exact source-valid transfer result, before publishing it.
            // Bind to the durable resolver, never the operation scope disposed on
            // return: final consumers must still recheck live source freshness.
            var transferredResolver = CompiledGraphResolver(projected);
            if (transferredResolver is null
                || !TryCapturePhysicalStock3861(projected, transferredResolver, out var stock3861)
                || stock3861 is null
                || loads.Any(load => !CurrentPhysicalSources3847(transferredResolver.TryCreateContext(
                    projected.ToString(SaveOptions.DisableFormatting)), load))
                || !stock3861.MatchesCombinedLoadout3497(projected, EmptyPhysicalStockBasket3861(), requestDigest)) return Refuse("transferred-stock-unavailable");
            // Commit only the proven clip deltas to the disposable final graph.
            foreach (var id in loadedIds)
            {
                var source = projected.Element("weapons")!.Elements("weapon").Single(row => SameSource3847(row.Element("guid")?.Value, id));
                var target = root.Element("weapons")!.Elements("weapon").Single(row => SameSource3847(row.Element("guid")?.Value, id));
                if (XNode.DeepEquals(source.Element("clips"), target.Element("clips"))) continue;
                target.Element("clips")?.Remove();
                target.Add(new XElement(source.Element("clips")!));
            }
            if (!XNode.DeepEquals(root, projected)) return Refuse("projected-graph-mismatch");
            loadedStock3861 = stock3861;
            deltas = changes.ToArray();
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or FormatException or OverflowException or System.Xml.XmlException or SessionPlayException)
        { return Refuse("evaluation-failed"); }
    }

    private static bool SameRoundCustody3847(SessionPlayAmmunition expected, SessionPlayAmmunition actual)
        => expected.Inventory.OrderBy(row => row.GearId).SequenceEqual(actual.Inventory.OrderBy(row => row.GearId))
            && expected.Weapons.OrderBy(row => row.WeaponId).Select(row => (row.WeaponId, row.LoadedGearId, row.LoadedRounds, row.Feed))
                .SequenceEqual(actual.Weapons.OrderBy(row => row.WeaponId).Select(row => (row.WeaponId, row.LoadedGearId, row.LoadedRounds, row.Feed)));

    private static bool CurrentPhysicalSources3847(ICharacterSourceDataContext? context, CharacterCreationInitialAmmunitionLoad3847 load)
        => PhysicalSourceFailure3847(context, load) is null;

    private static string? PhysicalSourceFailure3847(ICharacterSourceDataContext? context, CharacterCreationInitialAmmunitionLoad3847 load)
    {
        if (load.WeaponSourceId == Guid.Empty || load.AmmunitionSourceId == Guid.Empty
            || load.WeaponId == Guid.Empty || load.AmmunitionId == Guid.Empty) return "invalid-identity";
        if (context is not ICharacterSheetOwnerSourceContext sources) return "owner-source-context-unavailable";
        if (!sources.TryResolveSheetOwnerSource("Weapon", load.WeaponSourceId, out var weapon) || weapon is null)
            return "weapon-source-unavailable";
        if (!sources.TryResolveSheetNamedSource("Gear", "Ammo: Regular Ammo", out var ammo) || ammo is null)
            return "ordinary-round-source-unavailable";
        if (ammo.SourceId != load.AmmunitionSourceId) return "ordinary-round-identity-mismatch";
        if (!CharacterCreationFinalizationDigest.IsCanonical(load.WeaponSourceDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(load.AmmunitionSourceDigest)) return "invalid-source-digest";
        if (CharacterCreationFinalizationDigest.ComputeUtf8(weapon.SourceXml) != load.WeaponSourceDigest)
            return "weapon-source-changed";
        if (CharacterCreationFinalizationDigest.ComputeUtf8(ammo.SourceXml) != load.AmmunitionSourceDigest)
            return "ammunition-source-changed";
        return null;
    }

    private static XElement? SelectPhysicalOwner3847(XElement root, string container, string kind, Guid sourceId, Guid? instance)
    {
        var rows = root.Element(container)?.Elements(kind).Where(row => SameSource3847(row.Element("sourceid")?.Value, sourceId)
            && (instance is null || SameSource3847(row.Element("guid")?.Value, instance.Value))).ToArray() ?? [];
        return rows.Length == 1 ? rows[0] : null;
    }
    private static bool SameSource3847(string? text, Guid id) => Guid.TryParse(text, out var parsed) && parsed == id;
}
