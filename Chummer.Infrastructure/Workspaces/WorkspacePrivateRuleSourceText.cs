using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

// Privacy classification only. These contracts carry individual effective source
// nodes, without their file ancestors. Digest checks bind the exact string; they
// do not authenticate it. Import must still replay retained history, and export
// must still observe the store under its real owner/source/campaign leases.
internal static class WorkspacePrivateRuleSourceText
{
    internal static bool TryValidate(object? model, string member, string value)
    {
        // Historical absent optional payloads retain their original strict path.
        if (value.Length == 0) return false;
        (string Xml, string Digest, string Expected, string Container, string Element)? node = model switch
        {
            CharacterCreationQualityCatalogOption source when member == nameof(source.SourceNodeXml) =>
                (source.SourceNodeXml, source.SourceNodeDigest,
                    CharacterCreationQualitiesRules.ComputeSourceNodeDigest(source.SourceNodeXml), "qualities", "quality"),
            CharacterCreationQualitySelection source when member == nameof(source.SourceNodeXml) =>
                (source.SourceNodeXml, source.SourceNodeDigest,
                    CharacterCreationQualitiesRules.ComputeSourceNodeDigest(source.SourceNodeXml), "qualities", "quality"),
            CharacterCreationGearCatalogOption source when member == nameof(source.SourceNodeXml) =>
                (source.SourceNodeXml, source.SourceNodeDigest,
                    CharacterCreationGearRules.ComputeSourceNodeDigest(source.SourceNodeXml), "gears", "gear"),
            CharacterCreationGearLine source when member == nameof(source.SourceNodeXml) =>
                (source.SourceNodeXml, source.SourceNodeDigest,
                    CharacterCreationGearRules.ComputeSourceNodeDigest(source.SourceNodeXml), "gears", "gear"),
            CharacterCreationLoadoutCatalogOption source when member == nameof(source.SourceNodeXml) =>
                Loadout(source.SourceNodeXml, source.SourceNodeDigest, source.Kind),
            CharacterCreationLoadoutLine source when member == nameof(source.SourceNodeXml) =>
                Loadout(source.SourceNodeXml, source.SourceNodeDigest, source.Kind),
            CharacterCreationTalentQualitySource source when member == nameof(source.CanonicalSourceXml) =>
                Canonical(source.CanonicalSourceXml, source.CanonicalSourceXmlDigest, "qualities", "quality"),
            CharacterCreationTalentGearSource source when member == nameof(source.CanonicalSourceXml) =>
                Canonical(source.CanonicalSourceXml, source.CanonicalSourceXmlDigest, "gears", "gear"),
            CharacterCreationMagicResonanceCatalogOption source when member == nameof(source.CanonicalSourceXml) =>
                Magic(source.CanonicalSourceXml, source.CanonicalSourceXmlDigest, source.Identity.Kind),
            CharacterCreationMagicResonanceOptionFinalizationSource source when member == nameof(source.CanonicalSourceXml) =>
                Magic(source.CanonicalSourceXml, source.CanonicalSourceXmlDigest, source.Identity.Kind),
            _ => null
        };
        if (node is not { } sourceNode) return false;
        if (value != sourceNode.Xml || sourceNode.Digest != sourceNode.Expected)
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        RetainedCharacterRuleSourceInputs.ValidatePortableRuleSourceNodeXml(value, sourceNode.Container, sourceNode.Element);
        return true;
    }

    private static (string, string, string, string, string) Loadout(string xml, string digest, string kind)
        => kind switch
        {
            CharacterCreationLoadoutKinds.Weapon =>
                (xml, digest, CharacterCreationLoadoutRules.ComputeSourceNodeDigest(xml), "weapons", "weapon"),
            CharacterCreationLoadoutKinds.Armor =>
                (xml, digest, CharacterCreationLoadoutRules.ComputeSourceNodeDigest(xml), "armors", "armor"),
            _ => throw WorkspacePrivateRuleSourceClosure.Rejected()
        };

    private static (string, string, string, string, string) Canonical(string xml, string digest, string container, string element)
        => (xml, digest, CharacterCreationMagicResonanceDigest.ComputeUtf8(xml), container, element);

    private static (string, string, string, string, string) Magic(string xml, string digest, string kind)
        => kind switch
        {
            CharacterCreationMagicResonanceKinds.Tradition or CharacterCreationMagicResonanceKinds.Stream =>
                Canonical(xml, digest, "traditions", "tradition"),
            CharacterCreationMagicResonanceKinds.AdeptPower => Canonical(xml, digest, "powers", "power"),
            CharacterCreationMagicResonanceKinds.Spell => Canonical(xml, digest, "spells", "spell"),
            CharacterCreationMagicResonanceKinds.ComplexForm => Canonical(xml, digest, "complexforms", "complexform"),
            _ => throw WorkspacePrivateRuleSourceClosure.Rejected()
        };
}
