using System.Xml.Linq;
using Chummer.Application.Session;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private CharacterCreationAmmunitionEvaluation? ammunitionEvaluation;
    private string? ammunitionAuthorityDigest;

    internal CharacterCreationAmmunitionEvaluation? AmmunitionFor(CharacterCreationCompleteAssessment authority)
        => ammunitionAuthorityDigest == CharacterCreationFinalizationDigest.Compute(authority)
            ? ammunitionEvaluation : null;

    // Runs on a disposable copy after the actual complete source/owner compiler.
    // The existing finalizer performs the same typed allocation again at Apply.
    // Neither the original graph nor its private stock proof is changed here.
    private CharacterCreationAmmunitionEvaluation? EvaluateAmmunition(
        CharacterCreationCompleteAssessment authority, CharacterCreationSelectedEffectsConsumer effects)
    {
        if (request.LifecycleResultSchema != CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            || request.Selections.InitialAmmunitionLoads3847 is not { Count: > 0 } loads
            || effects.DigestFor(authority) is not { } compilation
            || original.CreationLifecycleDigestFor(authority) is null) return null;
        var graph = effects.CopyCompiledGraph();
        string selectedDigest = CharacterCreationOwnedGearGraph1233.GraphDigest(graph);
        CharacterCreationAmmunitionEvaluation Refuse(string blocker) => new(
            CharacterCreationAmmunitionEvaluation.SchemaV1, authority.InputsSnapshotDigest, authority.RequestDigest,
            authority.RawCharacterXmlDigest, compilation, selectedDigest, "", "", [], [], [blocker]);
        CharacterCreationAmmunitionEvaluation Unavailable(string detail)
            => Refuse(CharacterCreationAmmunitionEvaluation.Unavailable + "." + detail);
        string stage = "initial-load";
        try
        {
            if (!TryLoadInitialAmmunition3847(graph, loads, out _, out var stock) || stock is null)
                return Refuse(initialAmmunitionFailure3847 ?? CharacterCreationAmmunitionEvaluation.NotReady);
            var candidate = CompiledGraphResolver(graph);
            if (candidate is null) return Unavailable("compiled-source-view");
            stage = "source-context";
            using var scope = (candidate as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var resolver = scope ?? candidate;
            string xml = graph.ToString(SaveOptions.DisableFormatting);
            var context = resolver.TryCreateContext(xml);
            if (context is not ICharacterSheetOwnerSourceContext sources
                || !sources.TryResolveSheetNamedSource("Gear", "Ammo: Regular Ammo", out var regular)
                || regular is null || !IsOrdinaryAmmunitionSource(regular))
                return Refuse(CharacterCreationAmmunitionEvaluation.EffectsUnavailable);

            var diagnostics = new List<SessionPlayDiagnostic>();
            stage = "custody";
            var states = SessionPlayWeaponAuthority.ResolveForPhysicalRoutine3828(graph, xml, resolver,
                diagnostics, out var custody, original.PriorityGrants3563(context));
            var weapons = graph.Element("weapons")?.Elements("weapon").ToArray() ?? [];
            var ammunition = graph.Descendants("gear")
                .Where(row => row.Element("category")?.Value == "Ammunition").ToArray();
            // A supported subset cannot prove the whole ammunition category.
            // Diagnostics include skipped owners AND the successful Play-ledger
            // notice. Only that non-authorizing notice is informational; require
            // exact complete custody and reject every other diagnostic below.
            if (HasBlockingAmmunitionDiagnostics(diagnostics)) return Unavailable("custody-diagnostics");
            if (custody is null) return Unavailable("custody-missing");
            if (weapons.Length == 0 || ammunition.Length == 0) return Unavailable("empty-weapon-or-ammunition");
            if (custody.Weapons.Count != weapons.Length || states.Count != weapons.Length)
                return Unavailable("weapon-count");
            if (custody.Inventory.Count != ammunition.Length) return Unavailable("ammunition-count");
            if (custody.Inventory.Any(row => row.SourceId != regular.SourceId)) return Unavailable("ammunition-source");
            string regularDigest = CharacterCreationFinalizationDigest.ComputeUtf8(regular.SourceXml);
            var ready = new List<CharacterCreationAmmunitionReadyWeapon>();
            var witnesses = new List<CharacterSheetOwnerSource>();
            stage = "weapon-witnesses";
            foreach (var weapon in weapons)
            {
                if (!Guid.TryParse(weapon.Element("guid")?.Value, out var id)
                    || !Guid.TryParse(weapon.Element("sourceid")?.Value, out var sourceId)
                    || !sources.TryResolveSheetOwnerSource("Weapon", sourceId, out var witness) || witness is null)
                    return Unavailable("weapon-source");
                var owned = custody.Weapons.SingleOrDefault(row => row.WeaponId == id);
                var state = states.SingleOrDefault(row => row.Identity.WeaponId == id);
                if (owned is null || state is null || owned.LoadedRounds < 1
                    || !owned.CompatibleGearIds.Contains(owned.LoadedGearId)
                    || !custody.Inventory.Any(row => row.GearId == owned.LoadedGearId)
                    || state.Identity.AmmoGearId != owned.LoadedGearId)
                    return Refuse(CharacterCreationAmmunitionEvaluation.NotReady);
                witnesses.Add(witness);
                ready.Add(new(id, sourceId, owned.LoadedGearId, owned.LoadedRounds, custody.Revision, state.Revision));
            }
            // Recheck the exact source objects and private transferred stock after
            // the dependent evaluations. Digests supplied in a wish are not proof.
            stage = "source-rechecks";
            if (!sources.TryResolveSheetNamedSource("Gear", "Ammo: Regular Ammo", out var currentRegular)
                || currentRegular != regular) return Unavailable("ammunition-source-changed");
            if (witnesses.Any(prior => !sources.TryResolveSheetOwnerSource("Weapon", prior.SourceId, out var current)
                    || current != prior)) return Unavailable("weapon-source-changed");
            if (loads.Any(load => !CurrentPhysicalSources3847(context, load))) return Unavailable("load-source-changed");
            stage = "stock-recheck";
            if (!stock.MatchesCombinedLoadout3497(graph, EmptyPhysicalStockBasket3861(), requestDigest))
                return Unavailable("stock-changed");
            stage = "result";
            return new(CharacterCreationAmmunitionEvaluation.SchemaV1,
                authority.InputsSnapshotDigest, authority.RequestDigest, authority.RawCharacterXmlDigest,
                compilation, selectedDigest, CharacterCreationOwnedGearGraph1233.GraphDigest(graph),
                CharacterCreationFinalizationDigest.Compute(new { Regular = regular, Weapons = witnesses }),
                ready.ToArray(), custody.Inventory.Select(row => new CharacterCreationAmmunitionEffects(
                    row.GearId, row.SourceId, regularDigest, row.TotalRounds, row.AvailableRounds, 0, 0)).ToArray(), []);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or FormatException or OverflowException or System.Xml.XmlException or SessionPlayException)
        { return Unavailable(stage + "." + error.GetType().Name); }
    }

    internal static bool HasBlockingAmmunitionDiagnostics(IReadOnlyList<SessionPlayDiagnostic> diagnostics)
        => diagnostics.Any(row => row.Code != "ammunition-local-custody"
            || row.Capability != "ammo-reload" || row.AuthorizesMutation);

    // Canonical regular-round identity is resolved by the effective source context,
    // not pinned to a GUID or inferred from an absent weaponbonus. An overlay that
    // adds unknown effects must get its own evaluator, never these zero modifiers.
    internal static bool IsOrdinaryAmmunitionSource(CharacterSheetOwnerSource source)
    {
        if (source.Kind != "Gear" || source.SourceId == Guid.Empty || source.Name != "Ammo: Regular Ammo"
            || source.SourceXml.Length > CharacterCreationGearRules.MaximumSourceNodeLength) return false;
        try
        {
            var node = RoleXml(source.SourceXml);
            string[] allowed = ["id", "name", "category", "rating", "source", "page", "avail", "cost", "costfor", "ammoforweapontype"];
            return node.Name == "gear" && !node.HasAttributes
                && node.Elements().All(row => row.Name.NamespaceName.Length == 0
                    && allowed.Contains(row.Name.LocalName, StringComparer.Ordinal) && !row.HasAttributes && !row.HasElements)
                && node.Elements().GroupBy(row => row.Name).All(group => group.Count() == 1)
                && Guid.TryParse(node.Element("id")?.Value, out var id) && id == source.SourceId
                && node.Element("name")?.Value == source.Name && node.Element("category")?.Value == "Ammunition"
                && node.Element("rating")?.Value == "0" && node.Element("ammoforweapontype")?.Value == "gun";
        }
        catch (Exception error) when (error is System.Xml.XmlException or InvalidOperationException or ArgumentException)
        { return false; }
    }
}
