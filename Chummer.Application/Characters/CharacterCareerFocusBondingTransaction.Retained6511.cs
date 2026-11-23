using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerFocusBondingTransaction
{
    // Read capability only. The source and saved effect grammar are the same as
    // the bonding transaction; no caller-supplied bonus becomes an authority.
    internal static bool TryReadRetainedFocusSource6511(CharacterCareerGearPurchaseSource authority,
        out XElement? source)
    {
        source = null;
        try
        {
            Need(authority.SourceXml.Length <= CharacterCreationGearRules.MaximumSourceNodeLength
                && IsSourceDigest(authority.CatalogDigest) && IsSourceDigest(authority.ProfileDigest)
                && IsSourceDigest(authority.CustomDataDigest), "focus_bonding_source_binding_invalid");
            var node = Parse(authority.SourceXml, "gear");
            const string fields = "id name category rating minrating ratinglabel source page avail cost bonus translate altpage notes altnotes notesColor";
            Need(node.Nodes().All(n => n is XElement || n is XText t && string.IsNullOrWhiteSpace(t.Value))
                && node.Elements().All(n => n.Name.Namespace == XNamespace.None && fields.Split(' ').Contains(n.Name.LocalName))
                && node.Elements().GroupBy(n => n.Name).All(g => g.Count() == 1)
                && node.Elements().Where(n => n.Name != "bonus").All(n => !n.HasAttributes && !n.HasElements)
                && GuidValue(node, "id") == authority.SourceId && Scalar(node, "name") == authority.Name
                && Scalar(node, "source") == authority.SourceBook && Scalar(node, "category") == "Foci"
                && SupportedName(authority.Name), "focus_bonding_source_semantics_unsupported");
            // The allowlist and uniqueness checks above bind this element. Its
            // canonical unique attribute is consumed by exact bonus equality.
            Need(node.Element("bonus") is { } bonus && XmlKey(bonus) == XmlKey(ExpectedBonus(authority.Name)),
                "focus_bonding_source_effect_unsupported");
            int minimum = Integer(node, "minrating", 1), maximum = Integer(node, "rating");
            Need(minimum > 0 && maximum >= minimum
                && maximum <= CharacterCreationLegacySourceProjector.MaximumCareerGearCatalogRatingChoices,
                "focus_bonding_rating_invalid");
            source = node;
            return true;
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }

    internal static bool TryCompileRetainedFocus6511(XElement character, XElement gear, XElement source,
        out XElement[] effects)
    {
        effects = [];
        try
        {
            Need(gear.Parent == Node(character, "gears") && Flag(character, "created"), "focus_bonding_nested_owner_unsupported");
            _ = ReadInventory(character); // Includes bindings, orphan flags and unique identities.
            var compiled = CompileFocus(gear, source);
            var owned = OwnedEffects(character, Identity(gear));
            bool bonded = Flag(gear, "bonded");
            Need(bonded ? EquivalentEffects(owned, compiled) : owned.Length == 0, "focus_bonding_owned_effect_drift");
            effects = bonded ? compiled : [];
            return true;
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }
}
