using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

// Privacy classification only. These contracts carry individual effective source
// nodes or logical selectors, without their file ancestors. XML digest checks
// bind the exact XML; selector digests retain their canonical provenance shape.
// Neither authenticates a source. Import must still replay retained history, and export
// must still observe the store under its real owner/source/campaign leases.
internal static class WorkspacePrivateRuleSourceText
{
    internal static bool TryValidate(object? model, string member, string value)
    {
        // Location SourcePath is a source-data selector, never a filesystem path
        // or execution capability. Only these exact typed members carry it.
        switch (model)
        {
            case WorkspaceDocumentState document when member == nameof(document.Payload)
                && document.RulesetId == RulesetDefaults.Sr5 && document.SchemaVersion == 1
                && document.PayloadKind is "sr5/chum5-xml" or "workspace":
            {
                // Classification only: capture, history replay and current admission
                // still bind the original payload and its selected source owners.
                if (value != document.Payload) throw WorkspacePrivateRuleSourceClosure.Rejected();
                RetainedCharacterRuleSourceInputs.ValidatePortableCharacterDocumentXml(value);
                return true;
            }
            case CharacterCreationLifestyleTextPrompt prompt when member == nameof(prompt.XPath):
            {
                // This typed V7 member carries the compiler's bounded source selector,
                // not a filesystem path. Classification grants no source authority:
                // retained-source replay still binds its digest, choices and exact text.
                if (value != prompt.XPath || prompt.SourceFile != "skills.xml"
                    || !CharacterCreationLifestylesRules.IsCanonicalDigest(prompt.SourceDigest))
                    throw WorkspacePrivateRuleSourceClosure.Rejected();
                string[] paths = value.Split('|').Select(path => path.Trim()).ToArray();
                if (paths.Length is < 1 or > 2
                    || paths.Distinct(StringComparer.Ordinal).Count() != paths.Length
                    || paths.Any(path => path is not ("/chummer/skills/skill/name"
                        or "/chummer/knowledgeskills/skill/name")))
                    throw WorkspacePrivateRuleSourceClosure.Rejected();
                // All other prompt members, including choice text, keep the ordinary
                // portable-text checks; a field name or caller digest is insufficient.
                return true;
            }
            case CharacterCreationLifestyleLocationNode location when member == nameof(location.SourcePath):
            {
                int depth = ValidateLifestyleLocationPath(value, location.SourcePath, location.SourceNodeDigest, location.SourceId);
                if (location.Name is null || location.Children is null
                    || depth == 3 && location.Children.Count != 0
                    || location.Name.Length == 0 && (location.SourceId is not null
                        || location.Children.Any(child => child is null || child.Name != string.Empty))
                    || location.SecurityRating is not null && (depth != 3 || location.Name.Length == 0
                        || string.IsNullOrWhiteSpace(location.SecurityRating)))
                    throw WorkspacePrivateRuleSourceClosure.Rejected();
                return true;
            }
            case CharacterCreationLifestyleLocationComponent location when member == nameof(location.SourcePath):
            {
                int depth = ValidateLifestyleLocationPath(value, location.SourcePath, location.SourceNodeDigest, location.SourceId);
                bool selected = location.Kind switch
                {
                    CharacterCreationLifestyleLocationKind.Catalogue => !string.IsNullOrEmpty(location.Text),
                    CharacterCreationLifestyleLocationKind.Placeholder => location.Text == string.Empty && location.SourceId is null,
                    _ => false
                };
                if (!selected || location.SecurityRating is not null && (depth != 3
                    || location.Kind != CharacterCreationLifestyleLocationKind.Catalogue
                    || string.IsNullOrWhiteSpace(location.SecurityRating)))
                    throw WorkspacePrivateRuleSourceClosure.Rejected();
                return true;
            }
        }
        // Historical absent optional XML payloads retain their original strict path.
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

    private static int ValidateLifestyleLocationPath(string value, string? path, string? digest, Guid? sourceId)
    {
        const string root = "lifestyles.xml#/cities/";
        if (value != path || !CharacterCreationLifestylesRules.IsCanonicalDigest(digest)
            || sourceId == Guid.Empty || !value.StartsWith(root, StringComparison.Ordinal))
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        string[] rows = value[root.Length..].Split('/');
        if (rows.Length is < 1 or > 3) throw WorkspacePrivateRuleSourceClosure.Rejected();
        for (int depth = 0; depth < rows.Length; depth++)
        {
            string kind = depth == 0 ? "city" : depth == 1 ? "district" : "borough";
            string row = rows[depth];
            if (!row.StartsWith(kind + "[", StringComparison.Ordinal) || !row.EndsWith("]", StringComparison.Ordinal)
                || !int.TryParse(row.AsSpan(kind.Length + 1, row.Length - kind.Length - 2),
                    System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
                || index is <= 0 or > 65_536 || row != kind + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]")
                throw WorkspacePrivateRuleSourceClosure.Rejected();
        }
        // This is the grammar used by CharacterCreationLifestyleLocationRules.ValidRows.
        // Full catalogue consistency and retained-history replay remain separate checks.
        return rows.Length;
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
