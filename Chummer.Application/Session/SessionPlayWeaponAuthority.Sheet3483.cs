using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;
using static Chummer.Application.Session.WorkspaceSessionPlayAuthorityResolver;

namespace Chummer.Application.Session;

internal static partial class SessionPlayWeaponAuthority
{
    internal static bool IsPhysicalRoundType3483(string type) => RoundTypes.Contains(type);

    // An internal, current-context proof for the sheet's ammo-only field admission.
    // Does not return a fire/reload authority or bypass the sheet improvement graph.
    internal static string BindSheetConfiguration3483(XElement weapon, XElement sheetSource,
        ICharacterSourceDataContext context, List<Func<bool>> checks)
    {
        Require(context.TryResolveCreationLoadoutAuthority(out var authority)
            && CharacterCreationLoadoutRules.IsValidAuthority(authority), "weapon-source-unavailable");
        var ownerSources = context as ICharacterSheetOwnerSourceContext;
        Require(ownerSources is not null, "accessory-source-unavailable");
        Guid sourceId = Guid.Parse(Text(weapon, "sourceid"));
        var options = authority.Options.Where(o => o.Kind == "weapon" && o.SourceId == sourceId).ToArray();
        Require(options.Length == 1 && CharacterCreationLoadoutRules.IsValidOption(options[0]),
            "source-identity-unavailable");
        var option = options[0];
        var canonical = Parse(option.SourceNodeXml);
        Require(XNode.DeepEquals(XElement.Parse(option.SourceNodeXml),
                XElement.Parse(sheetSource.ToString(SaveOptions.DisableFormatting)))
            && Text(weapon, "name") == Text(canonical, "name")
            && Text(weapon, "category") == Text(canonical, "category")
            && Text(weapon, "type") == "Ranged" && Text(canonical, "type") == "Ranged"
            && Text(weapon, "ammo") == Text(canonical, "ammo")
            && Text(weapon, "mode") == Text(canonical, "mode")
            && Text(weapon, "parentid", "").Length == 0 && !Boolean(weapon, "cyberware", false)
            && !weapon.Descendants("weapon").Any(),
            "weapon-saved-source-divergence");
        var witnesses = new Dictionary<Guid, CharacterSheetOwnerSource>();
        CharacterSheetOwnerSource Resolve(Guid id)
        {
            Require(ownerSources!.TryResolveSheetOwnerSource("WeaponAccessory", id, out var evidence)
                && evidence is not null, "accessory-source-unavailable");
            var bound = evidence!;
            Require(bound.SourceXml.Length <= CharacterCreationLoadoutRules.MaximumSourceNodeLength,
                "accessory-source-unavailable");
            var source = Parse(bound.SourceXml);
            Require(bound.Kind == "WeaponAccessory" && bound.SourceId == id && id != Guid.Empty
                && source.Name == "accessory" && Guid.Parse(Text(source, "id")) == id
                && bound.Name == Text(source, "name") && bound.SourceBook == Text(source, "source")
                && new[] { bound.CatalogDigest, bound.ProfileDigest, bound.CustomDataDigest, bound.PolicyDigest }
                    .All(CharacterCreationFinalizationDigest.IsCanonical), "accessory-source-unavailable");
            if (witnesses.TryGetValue(id, out var prior))
                Require(prior == bound, "accessory-source-changed");
            witnesses[id] = bound;
            return bound;
        }
        var attachments = ResolveAttachments(weapon, canonical, Resolve, identity =>
        {
            Require(ownerSources!.TryResolveSheetNamedSource("WeaponAccessory", identity, out var named)
                && named is not null && Resolve(named.SourceId) == named, "accessory-intrinsic-divergence");
            return named!.SourceId;
        }, (_, _) => throw new SessionPlayException(SessionPlayFailures.Invalid("accessory-child-source-unavailable")));
        // This bounded bridge admits no new generated-child domain. Existing sheet
        // rules still inspect every owner and every source effect after this proof.
        var feeds = EffectiveFeeds(Text(canonical, "ammo"), attachments);
        checks.Add(() => context.TryResolveCreationLoadoutAuthority(out var current)
            && CharacterCreationLoadoutRules.IsValidAuthority(current)
            && current.AuthorityDigest == authority.AuthorityDigest);
        foreach (var witness in witnesses.Values)
            checks.Add(() => ownerSources!.TryResolveSheetOwnerSource("WeaponAccessory", witness.SourceId, out var current)
                && current == witness);
        return Digest(new
        {
            Schema = "sr5-sheet-ammo-configuration/v1", option, authority.AuthorityDigest,
            Weapon = weapon.ToString(SaveOptions.DisableFormatting),
            Attachments = attachments.Select(a => new { a.Evidence, a.Selection, a.ChildDigest }).ToArray(), feeds
        });
    }
}
