using System.Xml.Linq;
using Chummer.Application.Session;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    private readonly HashSet<Guid> ammoConfigurationOwners3483 = [];

    private void BindAmmunitionMetadata3483(Owner owner)
    {
        // These fields describe a sale unit and compatible physical round types.
        // They do not modify a skill/attribute or authorize round custody.
        string types = Text(owner.Source, "ammoforweapontype");
        Need(Text(owner.Source, "category") == "Ammunition"
            && types.Length > 0 && types.Split(',').All(SessionPlayWeaponAuthority.IsPhysicalRoundType3483)
            && Text(owner.Saved, "ammoforweapontype", types) == types
            && Text(owner.Saved, "category", "Ammunition") == "Ammunition",
            "ammunition-source-mismatch", owner.Path);
        int unit = Int(owner.Source, "costfor", 1);
        Need(unit > 0 && Int(owner.Saved, "costfor", unit) == unit,
            "ammunition-source-mismatch", owner.Path + "/costfor");
        foreach (string field in new[] { "weaponbonus", "flechetteweaponbonus", "wirelessweaponbonus", "ammobonus" })
        {
            var saved = Node(owner.Saved, field);
            if (saved is null || !saved.HasAttributes && !saved.HasElements && string.IsNullOrWhiteSpace(saved.Value))
                continue;
            var source = Node(owner.Source, field);
            Need(source is not null && XNode.DeepEquals(
                XElement.Parse(saved.ToString(SaveOptions.DisableFormatting)),
                XElement.Parse(source!.ToString(SaveOptions.DisableFormatting))),
                "saved-ammunition-effect-mismatch", owner.Path + "/" + field);
        }
        // The existing source-field and effect compiler still rejects unknown
        // source mechanics, and still reconciles every saved improvement.
    }

    private void BindAmmoAccessory3483(Owner owner)
    {
        var parent = owners.SingleOrDefault(o => o.Kind == "Weapon"
            && ReferenceEquals(owner.Saved.Parent?.Parent, o.Saved));
        Need(parent is not null && ReferenceEquals(parent.Saved.Parent, Node(root, "weapons")),
            "ammo-accessory-parent-unavailable", owner.Path);
        var boundParent = parent!;
        if (ammoConfigurationOwners3483.Contains(boundParent.Id)) return;
        try
        {
            // Reuse the typed validator's source selection, mounts, requirements,
            // instance/parent links, saved scalar binding and capacity expression.
            // No detached copy with removed improvements or substituted resolver.
            sourceEvidence.Add(SessionPlayWeaponAuthority.BindSheetConfiguration3483(
                boundParent.Saved, boundParent.Source, context, checks));
            ammoConfigurationOwners3483.Add(boundParent.Id);
        }
        catch (Exception error) when (error is SessionPlayException or System.Xml.XmlException
            or System.Xml.XPath.XPathException or InvalidOperationException or FormatException
            or OverflowException or ArgumentException)
        {
            string code = error is SessionPlayException session ? session.Failure.Code : "invalid-configuration";
            throw Error("ammo-accessory-configuration-unavailable", owner.Path + "/" + code);
        }
    }
}
