using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

internal sealed record CharacterCreationMagicResonanceProjectionContext(
    string SettingsProfileId,
    CharacterCreationPrerequisiteAuthority PrerequisiteAuthority,
    string PrioritiesInputsDigest,
    string MetatypesInputsDigest,
    string TraditionsInputsDigest,
    string StreamsInputsDigest,
    string PowersInputsDigest,
    string SpellsInputsDigest,
    string ComplexFormsInputsDigest,
    string CustomDataInputsDigest,
    IReadOnlyList<string> EnabledSourcebooks,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Strict projection of the source catalogs used by the SR5 Standard Priority
/// Magic/Resonance step. A row with semantics not explicitly understood remains
/// visible but disabled; the projector never infers hidden bonus/prerequisite logic.
/// </summary>
internal static class CharacterCreationMagicResonanceAuthorityProjector
{
    public static CharacterCreationMagicResonanceAuthority Project(
        IReadOnlyList<XElement> metatypes,
        IReadOnlyList<XElement> traditions,
        IReadOnlyList<XElement> streams,
        IReadOnlyList<XElement> powers,
        IReadOnlyList<XElement> spells,
        IReadOnlyList<XElement> complexForms,
        CharacterCreationMagicResonanceProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(metatypes);
        ArgumentNullException.ThrowIfNull(traditions);
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(powers);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(complexForms);
        ArgumentNullException.ThrowIfNull(context);

        var blockers = new List<string>(context.Blockers);
        CharacterCreationMagicResonanceTalentOption[] talentOptions = ProjectTalents(
            context.PrerequisiteAuthority, blockers);
        CharacterCreationMagicResonanceMetatypeCapability[] metatypeOptions = ProjectMetatypes(
            metatypes, context.MetatypesInputsDigest, blockers);
        CharacterCreationMagicResonanceCatalogOption[] traditionOptions = ProjectCatalog(
            traditions, CharacterCreationMagicResonanceKinds.Tradition,
            context.TraditionsInputsDigest, context.EnabledSourcebooks, blockers);
        CharacterCreationMagicResonanceCatalogOption[] streamOptions = ProjectCatalog(
            streams, CharacterCreationMagicResonanceKinds.Stream,
            context.StreamsInputsDigest, context.EnabledSourcebooks, blockers);
        CharacterCreationMagicResonanceCatalogOption[] powerOptions = ProjectCatalog(
            powers, CharacterCreationMagicResonanceKinds.AdeptPower,
            context.PowersInputsDigest, context.EnabledSourcebooks, blockers);
        CharacterCreationMagicResonanceCatalogOption[] spellOptions = ProjectCatalog(
            spells, CharacterCreationMagicResonanceKinds.Spell,
            context.SpellsInputsDigest, context.EnabledSourcebooks, blockers);
        CharacterCreationMagicResonanceCatalogOption[] formOptions = ProjectCatalog(
            complexForms, CharacterCreationMagicResonanceKinds.ComplexForm,
            context.ComplexFormsInputsDigest, context.EnabledSourcebooks, blockers);

        string sourceInputsDigest = CharacterCreationMagicResonanceDigest.Compute(new
        {
            Schema = "chummer.sr5.standard_priority_magic_resonance_source_inputs.v1",
            context.PrioritiesInputsDigest,
            context.MetatypesInputsDigest,
            context.TraditionsInputsDigest,
            context.StreamsInputsDigest,
            context.PowersInputsDigest,
            context.SpellsInputsDigest,
            context.ComplexFormsInputsDigest
        });
        string gmPolicyDigest = CharacterCreationMagicResonanceDigest.Compute(new
        {
            Schema = "chummer.sr5.standard_priority_magic_resonance_gm_policy.v1",
            Policy = "local-source-profile-no-external-gm-policy",
            context.SettingsProfileId,
            EnabledSourcebooks = context.EnabledSourcebooks
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        });
        string runtimeDigest = CharacterCreationMagicResonanceDigest.Compute(new
        {
            Schema = CharacterCreationMagicResonanceSchemas.RuntimeV1,
            Ruleset = "sr5",
            BuildMethod = CharacterCreationBuildMethods.Priority,
            PriorityTable = "Standard",
            CharacterDocumentMutation = false,
            AdeptPowerPointBudget = "assigned-magic",
            MysticAdeptPowerPointPurchase = "unsupported-fail-closed",
            Confirmation = "explicit-atomic-auxiliary-cas"
        });
        string[] normalizedBlockers = Normalize(blockers);
        string[] anchors = context.SourceAnchorIds
            .Concat(talentOptions.SelectMany(item => item.SourceAnchorIds))
            .Concat(metatypeOptions.SelectMany(item => item.SourceAnchorIds))
            .Concat(traditionOptions.SelectMany(item => item.SourceAnchorIds))
            .Concat(streamOptions.SelectMany(item => item.SourceAnchorIds))
            .Concat(powerOptions.SelectMany(item => item.SourceAnchorIds))
            .Concat(spellOptions.SelectMany(item => item.SourceAnchorIds))
            .Concat(formOptions.SelectMany(item => item.SourceAnchorIds))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var authority = new CharacterCreationMagicResonanceAuthority(
            CharacterCreationMagicResonanceSchemas.AuthorityV1,
            context.SettingsProfileId,
            context.PrerequisiteAuthority.AuthorityDigest,
            sourceInputsDigest,
            context.CustomDataInputsDigest,
            gmPolicyDigest,
            runtimeDigest,
            talentOptions,
            metatypeOptions,
            traditionOptions,
            streamOptions,
            powerOptions,
            spellOptions,
            formOptions,
            anchors,
            normalizedBlockers,
            IsAuthoritative: normalizedBlockers.Length == 0,
            AuthorityDigest: string.Empty);
        return authority with
        {
            AuthorityDigest = CharacterCreationMagicResonanceDigest.Compute(
                authority with { AuthorityDigest = string.Empty })
        };
    }

    private static CharacterCreationMagicResonanceTalentOption[] ProjectTalents(
        CharacterCreationPrerequisiteAuthority authority,
        ICollection<string> blockers)
    {
        var result = new List<CharacterCreationMagicResonanceTalentOption>();
        foreach (CharacterCreationPriorityOptionProjection priority in authority.Options
                     .Where(item => string.Equals(
                         item.CategoryId, CharacterCreationPriorityCategoryIds.Talent, StringComparison.Ordinal))
                     .OrderBy(item => item.Rank, StringComparer.Ordinal)
                     .ThenBy(item => item.SourceId, StringComparer.Ordinal))
        {
            foreach (CharacterCreationPriorityTalentOptionProjection talent in priority.TalentOptions
                         .OrderBy(item => item.SelectionId, StringComparer.Ordinal))
            {
                var local = new List<string>();
                XElement? raw = null;
                try { raw = XElement.Parse(talent.RawTalentNode, LoadOptions.None); }
                catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException)
                {
                    local.Add(CharacterCreationMagicResonanceBlockers.OptionSemanticsUnsupported);
                }
                string kind = ResolveTalentKind(talent.Value);
                int spells = ReadOptionalNonNegative(raw, "spells", local);
                int forms = ReadOptionalNonNegative(raw, "cfp", local);
                string[] requiredNames = ReadRestriction(raw, "required", "metatype", local);
                string[] requiredCategories = ReadRestriction(raw, "required", "metatypecategory", local);
                string[] forbiddenNames = ReadRestriction(raw, "forbidden", "metatype", local);
                if (raw is null
                    || kind == CharacterCreationMagicResonanceKinds.Unsupported
                    || HasUnsupportedTalentRestriction(raw))
                    local.Add(CharacterCreationMagicResonanceBlockers.TalentUnsupported);

                int magic = talent.Magic.GetValueOrDefault();
                int resonance = talent.Resonance.GetValueOrDefault();
                int depth = talent.Depth.GetValueOrDefault();
                bool tradition = kind is CharacterCreationMagicResonanceKinds.Magician
                    or CharacterCreationMagicResonanceKinds.MysticAdept
                    or CharacterCreationMagicResonanceKinds.AspectedMagician;
                bool stream = kind == CharacterCreationMagicResonanceKinds.Technomancer;
                bool adeptPowers = kind == CharacterCreationMagicResonanceKinds.Adept;
                bool allowsSpells = kind is CharacterCreationMagicResonanceKinds.Magician
                    or CharacterCreationMagicResonanceKinds.MysticAdept;
                bool allowsForms = kind == CharacterCreationMagicResonanceKinds.Technomancer;
                if ((magic == 0 && kind is (CharacterCreationMagicResonanceKinds.Adept
                        or CharacterCreationMagicResonanceKinds.Magician
                        or CharacterCreationMagicResonanceKinds.MysticAdept
                        or CharacterCreationMagicResonanceKinds.AspectedMagician))
                    || (resonance == 0 && kind == CharacterCreationMagicResonanceKinds.Technomancer)
                    || (depth == 0 && kind == CharacterCreationMagicResonanceKinds.ArtificialIntelligence)
                    || (spells > 0 && !allowsSpells)
                    || (forms > 0 && !allowsForms))
                    local.Add(CharacterCreationMagicResonanceBlockers.TalentUnsupported);
                string[] normalized = Normalize(local);
                result.Add(new(
                    new(priority.SourceId, talent.SelectionId, talent.Value),
                    priority.Rank,
                    talent.Name,
                    kind,
                    magic,
                    resonance,
                    depth,
                    spells,
                    forms,
                    adeptPowers ? magic : 0m,
                    tradition,
                    stream,
                    adeptPowers,
                    allowsSpells,
                    allowsForms,
                    requiredNames,
                    requiredCategories,
                    forbiddenNames,
                    talent.PriorityChildNodeDigest,
                    talent.SourceAnchorIds.Distinct(StringComparer.Ordinal)
                        .OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                    normalized,
                    IsEnabled: normalized.Length == 0));
            }
        }
        if (result.Count == 0)
            blockers.Add(CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);
        return result.OrderBy(item => item.Rank, StringComparer.Ordinal)
            .ThenBy(item => item.Identity.TalentSelectionId, StringComparer.Ordinal)
            .ToArray();
    }

    private static CharacterCreationMagicResonanceMetatypeCapability[] ProjectMetatypes(
        IReadOnlyList<XElement> rows,
        string effectiveDigest,
        ICollection<string> blockers)
    {
        var result = new List<CharacterCreationMagicResonanceMetatypeCapability>();
        foreach (XElement row in rows)
        {
            if (!TryReadGuid(row, "id", out string id)
                || !TryReadScalar(row, "name", out string name)
                || !TryReadScalar(row, "category", out string category))
            {
                blockers.Add(CharacterCreationMagicResonanceBlockers.MetatypePrerequisiteUnresolved);
                continue;
            }
            string anchor = $"metatypes.xml#metatype:{id}";
            result.Add(new(
                id,
                name,
                category,
                [anchor],
                ComputeNodeDigest("metatype", effectiveDigest, id, row)));
        }
        if (result.Select(item => item.MetatypeSourceId)
            .Distinct(StringComparer.Ordinal).Count() != result.Count)
            blockers.Add(CharacterCreationMagicResonanceBlockers.MetatypePrerequisiteUnresolved);
        return result.OrderBy(item => item.MetatypeName, StringComparer.Ordinal)
            .ThenBy(item => item.MetatypeSourceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static CharacterCreationMagicResonanceCatalogOption[] ProjectCatalog(
        IReadOnlyList<XElement> rows,
        string kind,
        string effectiveDigest,
        IReadOnlyList<string> enabledSourcebooks,
        ICollection<string> blockers)
    {
        var result = new List<CharacterCreationMagicResonanceCatalogOption>();
        foreach (XElement row in rows)
        {
            if (!TryReadGuid(row, "id", out string id)
                || !TryReadScalar(row, "name", out string name)
                || !TryReadScalar(row, "source", out string source)
                || !TryReadScalar(row, "page", out string page))
            {
                blockers.Add(CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);
                continue;
            }
            var local = new List<string>();
            if (!enabledSourcebooks.Contains(source, StringComparer.OrdinalIgnoreCase))
                local.Add(CharacterCreationMagicResonanceBlockers.OptionDisabled);
            if (row.Elements().Any(element => element.Name.LocalName is "required" or "forbidden")
                || (kind is (CharacterCreationMagicResonanceKinds.Tradition
                        or CharacterCreationMagicResonanceKinds.Stream)
                    && row.Element("bonus") is not null))
                local.Add(CharacterCreationMagicResonanceBlockers.OptionSemanticsUnsupported);

            string category = kind switch
            {
                CharacterCreationMagicResonanceKinds.Tradition => "magic-tradition",
                CharacterCreationMagicResonanceKinds.Stream => "resonance-stream",
                CharacterCreationMagicResonanceKinds.AdeptPower => "adept-power",
                CharacterCreationMagicResonanceKinds.Spell => ReadOptionalScalar(row, "category", "uncategorized"),
                CharacterCreationMagicResonanceKinds.ComplexForm => "complex-form",
                _ => "unsupported"
            };
            decimal pointCost = 1m;
            int maximumLevels = 1;
            if (kind == CharacterCreationMagicResonanceKinds.AdeptPower)
            {
                if (!TryReadNonNegativeDecimal(row, "points", out pointCost) || pointCost <= 0m)
                    local.Add(CharacterCreationMagicResonanceBlockers.OptionSemanticsUnsupported);
                bool levels = TryReadBoolean(row, "levels", out bool parsedLevels) && parsedLevels;
                if (levels)
                {
                    if (!TryReadPositiveInt(row, "limit", out maximumLevels))
                        local.Add(CharacterCreationMagicResonanceBlockers.OptionSemanticsUnsupported);
                }
                if (row.Element("bonus") is not null || row.Element("adeptwayrequires") is not null)
                    local.Add(CharacterCreationMagicResonanceBlockers.OptionSemanticsUnsupported);
            }
            else if ((kind is CharacterCreationMagicResonanceKinds.Spell
                      or CharacterCreationMagicResonanceKinds.ComplexForm)
                     && row.Element("bonus") is not null)
            {
                local.Add(CharacterCreationMagicResonanceBlockers.OptionSemanticsUnsupported);
            }

            string anchor = $"{FileName(kind)}#{kind}:{id}";
            string[] normalized = Normalize(local);
            result.Add(new(
                CharacterCreationMagicResonanceSchemas.CatalogOptionV1,
                new(kind, id),
                name,
                category,
                pointCost,
                maximumLevels,
                source,
                page,
                ComputeNodeDigest(kind, effectiveDigest, id, row),
                [anchor],
                normalized,
                IsEnabled: normalized.Length == 0)
            {
                DrainExpression = ReadOptionalScalar(row, "drain", string.Empty)
            });
        }
        if (result.Select(item => item.Identity.SourceId)
            .Distinct(StringComparer.Ordinal).Count() != result.Count)
            blockers.Add(CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);
        return result.OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Identity.SourceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveTalentKind(string value) => value switch
    {
        "Mundane" => CharacterCreationMagicResonanceKinds.Mundane,
        "Adept" => CharacterCreationMagicResonanceKinds.Adept,
        "Magician" => CharacterCreationMagicResonanceKinds.Magician,
        "Mystic Adept" => CharacterCreationMagicResonanceKinds.MysticAdept,
        "Aspected Magician" => CharacterCreationMagicResonanceKinds.AspectedMagician,
        "Technomancer" => CharacterCreationMagicResonanceKinds.Technomancer,
        "A.I." => CharacterCreationMagicResonanceKinds.ArtificialIntelligence,
        _ => CharacterCreationMagicResonanceKinds.Unsupported
    };

    private static bool HasUnsupportedTalentRestriction(XElement talent)
    {
        string[] known =
        [
            "name", "value", "qualities", "specialattribpoints", "magic", "resonance", "depth",
            "spells", "cfp", "skillqty", "skillval", "skilltype", "skillchoices",
            "skillgroupqty", "skillgroupval", "skillgrouptype", "skillgroupchoices",
            "required", "forbidden"
        ];
        if (talent.Elements().Any(element => !known.Contains(element.Name.LocalName, StringComparer.Ordinal)))
            return true;
        return !HasExactRestrictionShape(talent.Element("required"), allowMetatypeCategory: true)
               || !HasExactRestrictionShape(talent.Element("forbidden"), allowMetatypeCategory: false);
    }

    private static bool HasExactRestrictionShape(XElement? restriction, bool allowMetatypeCategory)
    {
        if (restriction is null)
            return true;
        XElement[] oneOf = restriction.Elements("oneof").Take(2).ToArray();
        return !restriction.HasAttributes
               && oneOf.Length == 1
               && !oneOf[0].HasAttributes
               && oneOf[0].Elements().Any()
               && oneOf[0].Elements().All(element =>
                   !element.HasAttributes
                   && !element.HasElements
                   && !string.IsNullOrWhiteSpace(element.Value)
                   && (element.Name.LocalName == "metatype"
                       || allowMetatypeCategory && element.Name.LocalName == "metatypecategory"));
    }

    private static string[] ReadRestriction(
        XElement? talent,
        string container,
        string field,
        ICollection<string> blockers)
    {
        XElement? restriction = talent?.Element(container);
        if (restriction is null)
            return [];
        XElement[] oneOf = restriction.Elements("oneof").Take(2).ToArray();
        if (oneOf.Length != 1)
        {
            blockers.Add(CharacterCreationMagicResonanceBlockers.OptionSemanticsUnsupported);
            return [];
        }
        return oneOf[0].Elements(field).Select(item => item.Value.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static int ReadOptionalNonNegative(XElement? row, string field, ICollection<string> blockers)
    {
        XElement[] values = row?.Elements(field).Take(2).ToArray() ?? [];
        if (values.Length == 0)
            return 0;
        if (values.Length == 1
            && !values[0].HasAttributes
            && !values[0].HasElements
            && int.TryParse(values[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            && value >= 0)
            return value;
        blockers.Add(CharacterCreationMagicResonanceBlockers.OptionSemanticsUnsupported);
        return 0;
    }

    private static bool TryReadGuid(XElement row, string field, out string value)
    {
        value = ReadOptionalScalar(row, field, string.Empty);
        return Guid.TryParseExact(value, "D", out Guid id)
               && id != Guid.Empty
               && string.Equals(value, id.ToString("D"), StringComparison.Ordinal);
    }

    private static bool TryReadScalar(XElement row, string field, out string value)
    {
        XElement[] values = row.Elements(field).Take(2).ToArray();
        value = values.Length == 1 ? values[0].Value : string.Empty;
        return values.Length == 1
               && !values[0].HasAttributes
               && !values[0].HasElements
               && !string.IsNullOrWhiteSpace(value)
               && string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }

    private static string ReadOptionalScalar(XElement row, string field, string fallback) =>
        TryReadScalar(row, field, out string value) ? value : fallback;

    private static bool TryReadNonNegativeDecimal(XElement row, string field, out decimal value)
    {
        value = 0m;
        return TryReadScalar(row, field, out string raw)
               && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
               && value >= 0m;
    }

    private static bool TryReadPositiveInt(XElement row, string field, out int value)
    {
        value = 0;
        return TryReadScalar(row, field, out string raw)
               && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
               && value > 0;
    }

    private static bool TryReadBoolean(XElement row, string field, out bool value)
    {
        value = false;
        return TryReadScalar(row, field, out string raw) && bool.TryParse(raw, out value);
    }

    private static string ComputeNodeDigest(string kind, string inputsDigest, string id, XElement row) =>
        CharacterCreationMagicResonanceDigest.Compute(new
        {
            Schema = $"chummer.sr5.standard_priority_magic_resonance_{kind}_source.v1",
            EffectiveInputsDigest = inputsDigest,
            SourceId = id,
            RawNode = row.ToString(SaveOptions.DisableFormatting)
        });

    private static string FileName(string kind) => kind switch
    {
        CharacterCreationMagicResonanceKinds.Tradition => "traditions.xml",
        CharacterCreationMagicResonanceKinds.Stream => "streams.xml",
        CharacterCreationMagicResonanceKinds.AdeptPower => "powers.xml",
        CharacterCreationMagicResonanceKinds.Spell => "spells.xml",
        CharacterCreationMagicResonanceKinds.ComplexForm => "complexforms.xml",
        _ => "unknown.xml"
    };

    private static string[] Normalize(IEnumerable<string> blockers) => blockers
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(item => item, StringComparer.Ordinal)
        .ToArray();
}
