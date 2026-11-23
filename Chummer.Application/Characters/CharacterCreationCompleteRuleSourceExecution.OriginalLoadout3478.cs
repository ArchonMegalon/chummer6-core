using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Capability discovery precedes selected compilation. It must use the same
    // original/source proof, otherwise it refuses armor before the repaired
    // compiler can run. The actual proposal is previewed against typed inputs;
    // no empty replacement inventory or caller-supplied proof is introduced.
    private CharacterCreationNativeLoadoutProjection1432? EvaluateOriginalNativeLoadout3478(
        CharacterCreationPrerequisiteDraft draft)
    {
        var root = XElement.Parse(Inputs.OriginalCharacter!.RawCharacterXml, LoadOptions.PreserveWhitespace);
        CharacterCreationOriginalInventory3475? proof = null;
        CharacterCreationLoadoutCompletionProjection? proposal = null;
        if (root.Element("weapons")?.HasElements == true || root.Element("armors")?.HasElements == true)
        {
            using var scope = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var context = (scope ?? resolution.Resolver).TryCreateContext(Inputs.OriginalCharacter.RawCharacterXml);
            if (context is null || !TryLoadoutBasis1215(context, out _, out _, out proposal)
                || !CharacterCreationOriginalInventory3475.TryCapture(root, resolution.Resolver, out proof,
                    original.PriorityGrants3563(context))
                || !proof!.TryCombineLoadout3497(root, proposal!, requestDigest, out var combined)) return null;
            root = combined!;
        }
        try
        {
            return CharacterCreationNativeLoadout1432.Evaluate(root,
                Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, draft, proposal, proof, requestDigest);
        }
        catch (InvalidDataException) { return null; }
    }

    // Keep the existing selected-purchase evaluator untouched. Its original-
    // inventory guard remains the default; only this private live-proof path
    // can account for retained owners beside source-validated new purchases.
    private static bool EvaluateLoadoutPredicateWithOriginal3478(string predicate, XElement originalGraph, XElement graph,
        CharacterCreationLoadoutCompletionProjection projected, out object? result,
        CharacterCreationOriginalInventory3475? proof)
    {
        return EvaluateLoadoutPredicateWithOriginal3497(predicate, originalGraph, graph, projected, out result, proof, null);
    }

    private static bool EvaluateLoadoutPredicateWithOriginal3497(string predicate, XElement originalGraph, XElement graph,
        CharacterCreationLoadoutCompletionProjection projected, out object? result,
        CharacterCreationOriginalInventory3475? proof, string? instanceSeed)
    {
        result = null;
        if (NoObjects1215(originalGraph, "weapons", "weapon") && NoObjects1215(originalGraph, "armors", "armor"))
            return EvaluateLoadoutPredicate1215(predicate, originalGraph, graph, projected, out result);
        if (proof is null || !proof.MatchesLoadout3478(originalGraph)
            || !CharacterCreationOriginalLoadout3478.TryEvaluate(graph, projected, proof, out var stock, instanceSeed)) return false;
        bool Both(string container, string entry) => NoObjects1215(originalGraph, container, entry) && NoObjects1215(graph, container, entry);
        switch (predicate)
        {
            case "creation-base-price":
                // Only the projected purchases are charged. Original costs and IDs
                // stay on their original owners; the funding binder remains mandatory.
                result = new { Created = false, projected.PurchaseCost, NewAcquisitions = projected.Lines.Sum(line => line.Quantity),
                    RetainedWeaponOwners = originalGraph.Element("weapons")?.Elements().Count() ?? 0,
                    RetainedArmorOwners = originalGraph.Element("armors")?.Elements().Count() ?? 0,
                    CombinedWeaponOwners = stock!.WeaponOwners, CombinedArmorOwners = stock.ArmorOwners,
                    SourceAuthorityDigest = stock.SourceAuthorityDigest };
                return true;
            case "no-ware-target":
                if (!Both("cyberwares", "cyberware") || !Both("biowares", "bioware")) return false;
                result = new { WareTargets = 0, OriginalAndCompiledInventoryExecuted = true, SourceAuthorityDigest = stock!.SourceAuthorityDigest };
                return true;
            case "no-stacked-focus-target":
                if (!Both("stackedfoci", "stackedfocus")) return false;
                result = new { StackedFocusTargets = 0, OriginalAndCompiledInventoryExecuted = true, SourceAuthorityDigest = stock!.SourceAuthorityDigest };
                return true;
            case "plain-firearm-recoil":
            case "new-independent-weapon-acquisition":
                // StockLoadout's typed replay already proves every original
                // parentid empty, numeric base RC, and no attachments or clips.
                if (!Both("weaponaccessories", "accessory")) return false;
                result = new { RetainedOriginal = true, NewAcquisitions = projected.Lines.Sum(line => line.Quantity),
                    UnmodifiedIndependentWeapons = true, AccessoryGroups = 0, SourceAuthorityDigest = stock!.SourceAuthorityDigest,
                    BaseRecoilCompensation = (graph.Element("weapons")?.Elements() ?? []).Select(w => w.Element("rc")!.Value).ToArray() };
                return true;
            case "no-armor-modification-target":
            case "no-armor-accessory-target":
            case "undamaged-new-armor":
                if (!Both("armormods", "armormod")) return false;
                result = new { RetainedOriginal = true, NewAcquisitions = projected.Lines.Sum(line => line.Quantity),
                    ModificationTargets = 0, AccessoryTargets = 0, Damage = 0, SourceAuthorityDigest = stock!.SourceAuthorityDigest,
                    ArmorRatings = (graph.Element("armors")?.Elements() ?? []).Select(a => a.Element("armor")!.Value).ToArray() };
                return true;
            default: return false;
        }
    }
}
