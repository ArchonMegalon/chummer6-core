using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private sealed record LoadoutLeaf1215(string Field, string Path, string XmlDigest, string SettingId,
        object Value, string Predicate);
    private sealed record BoundLoadout1215(string InvocationDigest, string RawCharacterDigest,
        string ProfileXmlDigest, string AuthorityDigest, string LegalityDigest,
        IReadOnlyDictionary<string, string> Results, ProfileSettingInputValidation1203 Digests);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundLoadout1215>
        loadoutInvocations1215 = new();

    // Exact leaves with a bounded consumer below. Other loadout settings are deliberately
    // not capabilities: in particular anatomy, vehicle rules and armor encumbrance.
    private static string? LoadoutPredicate1215(string field, string? container) => (container, field) switch
    {
        (null, "multiplyrestrictedcost" or "multiplyforbiddencost" or "restrictedcostmultiplier"
            or "forbiddencostmultiplier") => "creation-base-price",
        (null, "cyberlegmovement" or "allowcyberwareessdiscounts" or "dontusecyberlimbcalculation"
            or "allowbiowaresuites" or "cyberlimbattributebonuscap" or "excludelimbslot") => "no-ware-target",
        ("bannedwaregrades", "grade") => "no-ware-target",
        ("redlinerexclusion", "limb") => "no-ware-target",
        (null, "allowhigherstackedfoci") => "no-stacked-focus-target",
        (null, "restrictrecoil") => "plain-firearm-recoil",
        (null, "alloweditpartofbaseweapon") => "new-independent-weapon-acquisition",
        (null, "maximumarmormodifications") => "no-armor-modification-target",
        (null, "uncappedarmoraccessorybonuses") => "no-armor-accessory-target",
        (null, "armordegredation") => "undamaged-new-armor",
        _ => null
    };

    private static IReadOnlyList<LoadoutLeaf1215> LoadoutLeaves1215(XElement profile,
        CharacterCreationSettingsObservation observations)
    {
        var result = new List<LoadoutLeaf1215>();
        string xml = profile.ToString(SaveOptions.DisableFormatting);
        // Re-run the frozen observation algorithm, including order, namespace and every
        // repeated occurrence. Digest equality alone cannot select a repeated item.
        if (profile.Name != "setting" || profile.HasAttributes
            || !CharacterCreationSettingsObservation.TryCapture(xml, observations.ProfileInputsDigest,
                observations.Schema, out var fresh)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(observations, fresh)) return result;
        string[] represented = ["id", "name", "buildmethod", "books", "customdatadirectorynames",
            "maxskillratingcreate", "maxskillrating", "maxknowledgeskillratingcreate", "maxknowledgeskillrating"];
        var nodes = profile.Elements().Where(node => node.Name.NamespaceName.Length != 0
            || !represented.Contains(node.Name.LocalName, StringComparer.Ordinal))
            .SelectMany(node => node.DescendantsAndSelf().Where(child => !child.HasElements)).ToArray();
        for (int order = 0; order < nodes.Length; order++)
        {
            var leaf = nodes[order];
            string? container = leaf.Parent == profile ? null : leaf.Parent?.Name.LocalName;
            string? predicate = LoadoutPredicate1215(leaf.Name.LocalName, container);
            if (predicate is null || leaf.Name.NamespaceName.Length != 0 || leaf.HasAttributes
                || leaf.Nodes().Any(node => node.GetType() != typeof(XText))) continue;
            if (container is null)
            {
                if (profile.Elements().Count(node => node.Name.LocalName == leaf.Name.LocalName) != 1) continue;
            }
            else
            {
                var parent = leaf.Parent!;
                if (parent.Parent != profile || parent.Name != container || parent.HasAttributes
                    || profile.Elements().Count(node => node.Name.LocalName == container) != 1
                    || parent.Nodes().Any(node => node is not XElement
                        && (node.GetType() != typeof(XText) || !string.IsNullOrWhiteSpace(((XText)node).Value)))
                    || parent.Elements().Any(node => node.Name != leaf.Name || node.HasAttributes || node.HasElements
                        || node.Nodes().ToArray() is not [XText] || string.IsNullOrWhiteSpace(node.Value))) continue;
                // Repetition is an ordered source list, not a duplicate scalar. Never Distinct().
            }
            object value;
            if (container is not null || leaf.Name == "excludelimbslot")
            {
                if (leaf.Value.Length > 256 || leaf.Value.Any(char.IsControl)) continue;
                value = leaf.Value;
            }
            else if (leaf.Name.LocalName is "restrictedcostmultiplier" or "forbiddencostmultiplier"
                or "cyberlimbattributebonuscap")
            {
                if (leaf.Nodes().ToArray() is not [XText]
                    || !int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                    || number < 0) continue;
                value = number;
            }
            else
            {
                if (leaf.Nodes().ToArray() is not [XText] || !bool.TryParse(leaf.Value, out bool boolean)) continue;
                value = boolean;
            }
            var observation = fresh!.Settings[order];
            if (observation.Key != CharacterCreationObservedSetting.Opaque
                || observation.State != CharacterCreationSettingObservationState.Opaque) continue;
            result.Add(new(leaf.Name.LocalName, RelativePath1203(profile, leaf), observation.SourceValueDigest!,
                observation.SettingId, value, predicate));
        }
        return result;
    }

    private void AddProfileLoadoutCapabilities1215(Dictionary<string, ProfileSettingCapability1203> capabilities,
        ICharacterSourceDataContext context, XElement profile)
    {
        if (!TryLoadoutBasis1215(context, out var loadout, out var legality, out var projected)) return;
        string digest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
        foreach (var leaf in LoadoutLeaves1215(profile, Inputs.EffectivePolicy!.SettingObservations!))
            capabilities.Add(leaf.SettingId, new(leaf.Field, leaf.Path, leaf.XmlDigest, digest,
                CharacterCreationFinalizationDigest.Compute(new { loadout, legality, projected, leaf.Predicate })));
    }

    private bool TryLoadoutBasis1215(ICharacterSourceDataContext context,
        out CharacterCreationLoadoutAuthority? loadout, out CharacterCreationLoadoutLegalityPolicy? legality,
        out CharacterCreationLoadoutCompletionProjection? projected)
    {
        loadout = null; legality = null; projected = null;
        if (!MatchesProfileSettingInputs1203() || Inputs.Loadout is not { } captured
            || request.Selections.Loadout is not { } selected
            || !context.TryResolveCreationLoadoutAuthority(out var fresh)
            || !context.TryResolveCreationLoadoutLegalityPolicy(out legality) || legality is null
            || fresh.SettingsProfileId != Inputs.EffectivePolicy!.SettingsProfileId
            || fresh.ProfileDigest != Inputs.EffectivePolicy.ProfileInputsDigest
            || legality.SettingsProfileId != fresh.SettingsProfileId || legality.RawProfileInputsDigest != fresh.ProfileDigest
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(fresh, captured.Authority)
            || captured.Binding.RawCharacterXmlDigest != Inputs.Skills.Binding.RawCharacterXmlDigest
            || captured.Binding.WorkspaceId != Inputs.OriginalCharacter!.WorkspaceId
            || captured.Binding.ContentRevision != Inputs.OriginalCharacter.ContentRevision
            || captured.Binding.SavedRevision != Inputs.OriginalCharacter.SavedRevision) return false;
        var preview = CharacterCreationLoadoutCompletionService.Preview(captured, selected);
        if (preview.Outcome != CharacterCreationFoundationOutcomes.Success || preview.Blockers.Count != 0
            || preview.Value is null) return false;
        loadout = fresh; projected = preview.Value;
        return true;
    }

    private void BindProfileLoadout1215(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, XElement graph,
        CharacterCreationResourcesService.CompleteFundingExecution? funding)
    {
        loadoutInvocations1215.Remove(invocation);
        if (!MatchesProfileSettingInputs1203() || Inputs.OriginalCharacter is null
            || funding?.LoadoutEvaluationDigest is not { } funded
            || !funding.TryGetCarryoverBudget995(authority, out _, out _, out _)
            || authority.RequestDigest != requestDigest || authority.InputsSnapshotDigest != Inputs.SnapshotDigest
            || invocation.GraphDigest != CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting))) return;
        using var scope = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var context = (scope ?? resolution.Resolver).TryCreateContext(Inputs.OriginalCharacter!.RawCharacterXml);
        if (context is null || !TryLoadoutBasis1215(context, out var loadout, out var legality, out var projected)) return;
        var originalGraph = XElement.Parse(Inputs.OriginalCharacter.RawCharacterXml, LoadOptions.PreserveWhitespace);
        if (!CreationLifecycle1215(originalGraph) || !CreationLifecycle1215(graph)) return;
        CharacterCreationOriginalInventory3475? originalLoadout3478 = null;
        if ((!NoObjects1215(originalGraph, "weapons", "weapon") || !NoObjects1215(originalGraph, "armors", "armor"))
            && !CharacterCreationOriginalInventory3475.TryCapture(originalGraph, resolution.Resolver, out originalLoadout3478,
                original.PriorityGrants3563(context))) return;
        var profile = XElement.Parse(Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var leaf in LoadoutLeaves1215(profile, Inputs.EffectivePolicy!.SettingObservations!))
        {
            if (!EvaluateLoadoutPredicateWithOriginal3497(leaf.Predicate, originalGraph, graph, projected!, out var mechanical, originalLoadout3478,
                CharacterCreationSelectedEffectsConsumer.CompilationSeed(authority))) continue;
            results.Add(leaf.Path, JsonSerializer.Serialize(new
            {
                Operation = "selected-profile-loadout1215", leaf.Field, leaf.SettingId, leaf.XmlDigest, leaf.Value,
                leaf.Predicate, Mechanical = mechanical, Profile = profileDigest, Inputs.SnapshotDigest,
                Original = Inputs.Skills.Binding.RawCharacterXmlDigest, invocation.GraphDigest,
                invocation.RequestDigest, invocation.PolicyDigest, invocation.CaptureDigest,
                Inputs.OriginalCharacter.WorkspaceId, Inputs.OriginalCharacter.ContentRevision,
                Inputs.OriginalCharacter.SavedRevision, Funded = funded, funding.EvaluationDigest,
                Projection = projected!.ProjectionDigest
            }));
        }
        BindProfileLoadoutContainers3212(profile, originalGraph, graph, projected!, results, originalLoadout3478,
            CharacterCreationSelectedEffectsConsumer.CompilationSeed(authority));
        // This private invocation owns bounded JSON/hash comparisons, not mutable
        // authority aliases. Every later read serializes the complete current
        // invocation, catalog and legality again before reusing a canonical hash.
        var digests8572 = new ProfileSettingInputValidation1203();
        loadoutInvocations1215.Add(invocation, new(LoadoutInvocationDigest1215(invocation, digests8572),
            Inputs.Skills.Binding.RawCharacterXmlDigest, profileDigest,
            digests8572.CanonicalDigest8544(loadout!), digests8572.CanonicalDigest8544(legality!), results, digests8572));
    }

    private static bool CreationLifecycle1215(XElement graph)
        => graph.Name == "character" && graph.Elements().Where(node => node.Name.LocalName == "created").ToArray() is [var created]
            && created.Name == "created" && !created.HasAttributes && created.Nodes().ToArray() is [XText]
            && bool.TryParse(created.Value, out bool value) && !value;

    // Inspect the original AND the actual complete compiled graph. No empty request,
    // Mundane talent, default switch, or source inventory stands in for this execution.
    private static bool NoObjects1215(XElement graph, string container, string entry)
    {
        var roots = graph.Descendants().Where(node => string.Equals(node.Name.LocalName, container, StringComparison.OrdinalIgnoreCase)).ToArray();
        return graph.Elements().Count(node => string.Equals(node.Name.LocalName, container, StringComparison.OrdinalIgnoreCase)) <= 1
            && roots.All(node => node.Name == container && !node.HasAttributes
            && !node.HasElements && node.Nodes().All(child => child.GetType() == typeof(XText)
                && string.IsNullOrWhiteSpace(((XText)child).Value)))
            && !graph.Descendants().Any(node => string.Equals(node.Name.LocalName, entry, StringComparison.OrdinalIgnoreCase)
                // A plain firearm's persisted cyberware=False is a flag, not an implant.
                && !(entry == "cyberware" && node.Parent?.Name == "weapon" && node.Name == "cyberware"
                    && !node.HasAttributes && node.Nodes().ToArray() is [XText]
                    && bool.TryParse(node.Value, out bool installed) && !installed));
    }

    private static bool EvaluateLoadoutPredicate1215(string predicate, XElement originalGraph, XElement graph,
        CharacterCreationLoadoutCompletionProjection projected, out object? result)
    {
        result = null;
        bool Both(string container, string entry) => NoObjects1215(originalGraph, container, entry)
            && NoObjects1215(graph, container, entry);
        // No existing loadout is silently replaced by the purchase projector.
        if (!NoObjects1215(originalGraph, "weapons", "weapon") || !NoObjects1215(originalGraph, "armors", "armor")) return false;
        switch (predicate)
        {
            case "creation-base-price":
                result = new { Created = false, projected.PurchaseCost, Lines = projected.Lines.Select(line =>
                    new { line.OptionId, line.Legality, line.UnitCost, line.Quantity, line.TotalCost, line.SourceNodeDigest }).ToArray() };
                return true;
            case "no-ware-target":
                if (!Both("cyberwares", "cyberware") || !Both("biowares", "bioware")) return false;
                result = new { WareTargets = 0, OriginalAndCompiledInventoryExecuted = true };
                return true;
            case "no-stacked-focus-target":
                if (!Both("stackedfoci", "stackedfocus")) return false;
                result = new { StackedFocusTargets = 0, OriginalAndCompiledInventoryExecuted = true };
                return true;
            case "plain-firearm-recoil":
            case "new-independent-weapon-acquisition":
                if (!Both("weaponaccessories", "accessory")) return false;
                var weapons = graph.Element("weapons")?.Elements("weapon").ToArray() ?? [];
                if (weapons.Length != projected.Lines.Where(line => line.Kind == "weapon").Sum(line => line.Quantity)
                    || weapons.Any(node => node.Elements("parentid").ToArray() is not [var parent]
                        || parent.HasAttributes || parent.HasElements || parent.Value.Length != 0
                        || !int.TryParse(node.Element("rc")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _))) return false;
                result = new { IndependentAcquisition = true, AccessoryGroups = 0,
                    BaseRecoilCompensation = weapons.Select(node => node.Element("rc")!.Value).ToArray() };
                return true;
            case "no-armor-modification-target":
            case "no-armor-accessory-target":
            case "undamaged-new-armor":
                if (!Both("armormods", "armormod")) return false;
                var armors = graph.Element("armors")?.Elements("armor").ToArray() ?? [];
                if (armors.Length != projected.Lines.Count(line => line.Kind == "armor")
                    || armors.Any(node => node.Element("damage")?.Value != "0"
                        || !int.TryParse(node.Element("armor")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _))) return false;
                result = new { ModificationTargets = 0, AccessoryTargets = 0, Damage = 0,
                    ArmorRatings = armors.Select(node => node.Element("armor")!.Value).ToArray() };
                return true;
            default: return false;
        }
    }

    private static string LoadoutInvocationDigest1215(CharacterCreationRuleSourceScopeInvocation invocation,
        ProfileSettingInputValidation1203? digests8572 = null)
    {
        var current = new
        {
            invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
            invocation.CompiledEffects, invocation.CreationDraft718, invocation.WorkspaceId718,
            invocation.Attributes718, invocation.AttributePolicy718, invocation.Skills718,
            invocation.ResourcesExecution718, invocation.PhysicalAuthority886, invocation.PhysicalInitiative886,
            Consumer = invocation.TryGetCompletedConsumer("profile", out var consumer) ? consumer : null
        };
        return digests8572 is null ? CharacterCreationFinalizationDigest.Compute(current)
            : digests8572.CanonicalDigest8544(current);
    }

    internal static bool TryGetExecutedProfileLoadout1215(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationLoadoutAuthority authority, CharacterCreationLoadoutLegalityPolicy legality,
        string rawCharacterDigest, string profileXmlDigest, string relativePath, string leafXmlDigest,
        out string? result)
    {
        result = null;
        if (!loadoutInvocations1215.TryGetValue(invocation, out var bound)) return false;
        // Serialize access to this invocation's bounded comparison buffers. No
        // comparison result or caller-advertised digest grants a later read.
        lock (bound)
        {
            if (bound.InvocationDigest != LoadoutInvocationDigest1215(invocation, bound.Digests)
                || bound.RawCharacterDigest != rawCharacterDigest || bound.ProfileXmlDigest != profileXmlDigest
                || bound.AuthorityDigest != bound.Digests.CanonicalDigest8544(authority)
                || bound.LegalityDigest != bound.Digests.CanonicalDigest8544(legality)
                || !bound.Results.TryGetValue(relativePath, out var diagnostic)) return false;
            using var json = JsonDocument.Parse(diagnostic);
            if (json.RootElement.GetProperty("XmlDigest").GetString() != leafXmlDigest) return false;
            result = diagnostic;
            return true;
        }
    }
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Read-only access to a private bound execution. No caller can register or restore it.
    public bool TryGetExecutedProfileLoadout1215(CharacterCreationLoadoutAuthority authority,
        CharacterCreationLoadoutLegalityPolicy legality, string rawCharacterDigest, string profileXmlDigest,
        string relativePath, string leafXmlDigest, out string? result)
        => CharacterCreationCompleteRuleSourceExecution.TryGetExecutedProfileLoadout1215(this,
            authority, legality, rawCharacterDigest, profileXmlDigest, relativePath, leafXmlDigest, out result);
}
