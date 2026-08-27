using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

/// <summary>
/// Builds and validates the immutable, source-bound awakened input consumed by
/// the whole-character finalizer. It never creates character GUIDs and never
/// writes character XML.
/// </summary>
public static class CharacterCreationMagicResonanceFinalizationRules
{
    public static string ComputeTalentProjectionDigest(
        CharacterCreationMagicResonanceTalentFinalizationSource value) =>
        CharacterCreationMagicResonanceDigest.Compute(value with { ProjectionDigest = string.Empty });

    public static string ComputeOptionProjectionDigest(
        CharacterCreationMagicResonanceOptionFinalizationSource value) =>
        CharacterCreationMagicResonanceDigest.Compute(value with { ProjectionDigest = string.Empty });

    public static string ComputeContributionDigest(
        CharacterCreationMagicResonanceFinalizationContribution value) =>
        CharacterCreationMagicResonanceDigest.Compute(value with { ContributionDigest = string.Empty });

    public static bool TryCreate(
        string expectedRawCharacterXmlDigest,
        long prerequisiteDraftRevision,
        string prerequisiteDraftDigest,
        long attributesDraftRevision,
        string attributesDraftDigest,
        CharacterCreationMagicResonanceAuthority authority,
        CharacterCreationMagicResonanceTalentOption talent,
        CharacterCreationMagicResonanceSelections selections,
        out CharacterCreationMagicResonanceFinalizationContribution contribution,
        out string[] blockers)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(talent);
        ArgumentNullException.ThrowIfNull(selections);
        contribution = EmptyContribution();
        var failures = new List<string>();
        if (authority.Talents is null
            || authority.Traditions is null
            || authority.Streams is null
            || authority.AdeptPowers is null
            || authority.Spells is null
            || authority.ComplexForms is null
            || selections.AdeptPowers is null
            || selections.Spells is null
            || selections.ComplexForms is null)
        {
            blockers = [CharacterCreationMagicResonanceBlockers.FinalizationContributionInvalid];
            return false;
        }
        if (!CharacterCreationMagicResonanceDigest.IsCanonical(expectedRawCharacterXmlDigest)
            || prerequisiteDraftRevision <= 0
            || !CharacterCreationMagicResonanceDigest.IsCanonical(prerequisiteDraftDigest)
            || attributesDraftRevision <= 0
            || !CharacterCreationMagicResonanceDigest.IsCanonical(attributesDraftDigest)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(authority.AuthorityDigest)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(authority.SourceInputsDigest)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(authority.CustomDataInputsDigest)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(authority.GmPolicyDigest)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(authority.RuntimeDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                authority.AuthorityDigest,
                CharacterCreationMagicResonanceDigest.Compute(
                    authority with { AuthorityDigest = string.Empty })))
            failures.Add(CharacterCreationMagicResonanceBlockers.FinalizationContributionInvalid);

        CharacterCreationMagicResonanceTalentOption[] matchingTalents = authority.Talents
            .Where(candidate => candidate.Identity == talent.Identity
                                && string.Equals(candidate.Kind, talent.Kind, StringComparison.Ordinal)
                                && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                                    candidate.SourceNodeDigest, talent.SourceNodeDigest))
            .Take(2)
            .ToArray();
        if (matchingTalents.Length != 1
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                CharacterCreationMagicResonanceDigest.Compute(matchingTalents[0]),
                CharacterCreationMagicResonanceDigest.Compute(talent))
            || !talent.IsEnabled
            || talent.Blockers.Count != 0
            || !HasSupportedTalentPayload(talent))
            failures.Add(CharacterCreationMagicResonanceBlockers.FinalizationPayloadInvalid);

        CharacterCreationMagicResonanceTalentFinalizationSource? talentSource =
            HasSupportedTalentPayload(talent) ? ProjectTalent(talent) : null;
        CharacterCreationMagicResonanceOptionFinalizationSource? tradition = Resolve(
            selections.Tradition,
            authority.Traditions,
            CharacterCreationMagicResonanceKinds.Tradition,
            1,
            failures);
        CharacterCreationMagicResonanceOptionFinalizationSource? stream = Resolve(
            selections.Stream,
            authority.Streams,
            CharacterCreationMagicResonanceKinds.Stream,
            1,
            failures);

        if (talent.RequiresTradition != (tradition is not null)
            || talent.RequiresStream != (stream is not null))
            failures.Add(CharacterCreationMagicResonanceBlockers.FinalizationContributionInvalid);

        CharacterCreationMagicResonanceOptionFinalizationSource[] powers = selections.AdeptPowers
            .Select(allocation => Resolve(
                allocation.Identity,
                authority.AdeptPowers,
                CharacterCreationMagicResonanceKinds.AdeptPower,
                allocation.Levels,
                failures))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Identity.SourceId, StringComparer.Ordinal)
            .ToArray();
        CharacterCreationMagicResonanceOptionFinalizationSource[] spells = selections.Spells
            .Select(identity => Resolve(
                identity,
                authority.Spells,
                CharacterCreationMagicResonanceKinds.Spell,
                1,
                failures))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Identity.SourceId, StringComparer.Ordinal)
            .ToArray();
        CharacterCreationMagicResonanceOptionFinalizationSource[] forms = selections.ComplexForms
            .Select(identity => Resolve(
                identity,
                authority.ComplexForms,
                CharacterCreationMagicResonanceKinds.ComplexForm,
                1,
                failures))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Identity.SourceId, StringComparer.Ordinal)
            .ToArray();

        bool hasValidPowerCost = TryComputePowerCost(powers, out decimal powerCost);
        if (powers.Length != selections.AdeptPowers.Count
            || spells.Length != selections.Spells.Count
            || forms.Length != selections.ComplexForms.Count
            || powers.Select(item => item.Identity).Distinct().Count() != powers.Length
            || spells.Select(item => item.Identity).Distinct().Count() != spells.Length
            || forms.Select(item => item.Identity).Distinct().Count() != forms.Length
            || (!talent.AllowsAdeptPowers && powers.Length != 0)
            || (!talent.AllowsSpells && spells.Length != 0)
            || (!talent.AllowsComplexForms && forms.Length != 0)
            || !hasValidPowerCost
            || powerCost != talent.AdeptPowerPointBudget
            || spells.Length != talent.SpellBudget
            || forms.Length != talent.ComplexFormBudget)
            failures.Add(CharacterCreationMagicResonanceBlockers.FinalizationContributionInvalid);

        string[] normalized = Normalize(failures);
        if (normalized.Length != 0 || talentSource is null)
        {
            blockers = normalized;
            return false;
        }

        string[] anchors = talentSource.SourceAnchorIds
            .Concat(tradition?.SourceAnchorIds ?? [])
            .Concat(stream?.SourceAnchorIds ?? [])
            .Concat(powers.SelectMany(item => item.SourceAnchorIds))
            .Concat(spells.SelectMany(item => item.SourceAnchorIds))
            .Concat(forms.SelectMany(item => item.SourceAnchorIds))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var candidate = new CharacterCreationMagicResonanceFinalizationContribution(
            CharacterCreationMagicResonanceSchemas.FinalizationContributionV1,
            expectedRawCharacterXmlDigest,
            prerequisiteDraftRevision,
            prerequisiteDraftDigest,
            attributesDraftRevision,
            attributesDraftDigest,
            authority.AuthorityDigest,
            authority.SourceInputsDigest,
            authority.CustomDataInputsDigest,
            authority.GmPolicyDigest,
            authority.RuntimeDigest,
            talentSource,
            tradition,
            stream,
            powers,
            spells,
            forms,
            anchors,
            string.Empty);
        contribution = candidate with
        {
            ContributionDigest = ComputeContributionDigest(candidate)
        };
        blockers = [];
        return IsStructurallyValid(contribution);
    }

    public static bool IsValidContribution(
        CharacterCreationMagicResonanceFinalizationContribution? contribution,
        CharacterCreationMagicResonanceDraft draft,
        CharacterCreationMagicResonanceAuthority authority)
    {
        if (contribution is null
            || !IsStructurallyValid(contribution)
            || !TryCreate(
                draft.BaseRawCharacterXmlDigest,
                draft.PrerequisiteDraftRevision,
                draft.PrerequisiteDraftDigest,
                draft.AttributesDraftRevision,
                draft.AttributesDraftDigest,
                authority,
                FindUniqueTalent(authority, draft) ?? DisabledTalent(),
                draft.Selections,
                out CharacterCreationMagicResonanceFinalizationContribution expected,
                out _))
            return false;
        return CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   contribution.ContributionDigest, expected.ContributionDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   CharacterCreationMagicResonanceDigest.Compute(contribution),
                   CharacterCreationMagicResonanceDigest.Compute(expected));
    }

    public static bool HasValidTalentPayload(CharacterCreationMagicResonanceTalentOption talent)
    {
        if (!TryParseCanonicalPayload(
                talent.CanonicalSourceXml,
                talent.CanonicalSourceXmlDigest,
                "talent",
                out XElement? source)
            || source is null
            || !string.Equals(Read(source, "name"), talent.Name, StringComparison.Ordinal)
            || !string.Equals(Read(source, "value"), talent.Identity.TalentValue, StringComparison.Ordinal)
            || ReadInt(source, "magic") != talent.Magic
            || ReadInt(source, "resonance") != talent.Resonance
            || ReadInt(source, "depth") != talent.Depth
            || ReadInt(source, "spells") != talent.SpellBudget
            || ReadInt(source, "cfp") != talent.ComplexFormBudget
            || !TryReadRestriction(source, "required", allowMetatypeCategory: true,
                out string[] requiredNames, out string[] requiredCategories)
            || !TryReadRestriction(source, "forbidden", allowMetatypeCategory: false,
                out string[] forbiddenNames, out _)
            || !requiredNames.SequenceEqual(talent.RequiredMetatypeNames, StringComparer.Ordinal)
            || !requiredCategories.SequenceEqual(talent.RequiredMetatypeCategories, StringComparer.Ordinal)
            || !forbiddenNames.SequenceEqual(talent.ForbiddenMetatypeNames, StringComparer.Ordinal))
            return false;
        string kind = ResolveTalentKind(talent.Identity.TalentValue);
        bool requiresTradition = kind is CharacterCreationMagicResonanceKinds.Magician
            or CharacterCreationMagicResonanceKinds.MysticAdept
            or CharacterCreationMagicResonanceKinds.AspectedMagician;
        bool requiresStream = kind == CharacterCreationMagicResonanceKinds.Technomancer;
        bool allowsAdeptPowers = kind == CharacterCreationMagicResonanceKinds.Adept;
        bool allowsSpells = kind is CharacterCreationMagicResonanceKinds.Magician
            or CharacterCreationMagicResonanceKinds.MysticAdept;
        bool allowsComplexForms = kind == CharacterCreationMagicResonanceKinds.Technomancer;
        if (!string.Equals(talent.Kind, kind, StringComparison.Ordinal)
            || talent.RequiresTradition != requiresTradition
            || talent.RequiresStream != requiresStream
            || talent.AllowsAdeptPowers != allowsAdeptPowers
            || talent.AllowsSpells != allowsSpells
            || talent.AllowsComplexForms != allowsComplexForms
            || talent.AdeptPowerPointBudget != (allowsAdeptPowers ? talent.Magic : 0m))
            return false;
        return CharacterCreationMagicResonanceDigest.IsCanonical(talent.SourceNodeDigest);
    }

    public static bool HasValidOptionPayload(CharacterCreationMagicResonanceCatalogOption option)
    {
        string expectedRoot = option.Identity.Kind switch
        {
            CharacterCreationMagicResonanceKinds.Tradition => "tradition",
            CharacterCreationMagicResonanceKinds.Stream => "tradition",
            CharacterCreationMagicResonanceKinds.AdeptPower => "power",
            CharacterCreationMagicResonanceKinds.Spell => "spell",
            CharacterCreationMagicResonanceKinds.ComplexForm => "complexform",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(expectedRoot)
            || !TryParseCanonicalPayload(
                option.CanonicalSourceXml,
                option.CanonicalSourceXmlDigest,
                expectedRoot,
                out XElement? source)
            || source is null
            || !string.Equals(Read(source, "id"), option.Identity.SourceId, StringComparison.Ordinal)
            || !string.Equals(Read(source, "name"), option.Name, StringComparison.Ordinal)
            || !string.Equals(Read(source, "source"), option.SourceBook, StringComparison.Ordinal)
            || !string.Equals(Read(source, "page"), option.Page, StringComparison.Ordinal)
            || !HasExactTypedOptionProjection(option, source))
            return false;
        return CharacterCreationMagicResonanceDigest.IsCanonical(option.SourceNodeDigest);
    }

    private static CharacterCreationMagicResonanceTalentFinalizationSource ProjectTalent(
        CharacterCreationMagicResonanceTalentOption talent)
    {
        var candidate = new CharacterCreationMagicResonanceTalentFinalizationSource(
            CharacterCreationMagicResonanceSchemas.FinalizationSourceV1,
            talent.Identity,
            talent.Kind,
            talent.Magic,
            talent.Resonance,
            talent.Depth,
            talent.SourceNodeDigest,
            talent.CanonicalSourceXml,
            talent.CanonicalSourceXmlDigest,
            talent.SourceAnchorIds,
            string.Empty);
        return candidate with { ProjectionDigest = ComputeTalentProjectionDigest(candidate) };
    }

    private static CharacterCreationMagicResonanceOptionFinalizationSource? Resolve(
        CharacterCreationMagicResonanceOptionIdentity? identity,
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> catalog,
        string kind,
        int levels,
        List<string> blockers)
    {
        if (identity is null)
            return null;
        CharacterCreationMagicResonanceCatalogOption[] matches = catalog
            .Where(item => item.Identity == identity
                           && string.Equals(item.Identity.Kind, kind, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1
            || levels < 1
            || levels > (matches.SingleOrDefault()?.MaximumLevels ?? 0)
            || !matches[0].IsEnabled
            || matches[0].Blockers.Count != 0
            || !HasSupportedOptionPayload(matches[0]))
        {
            blockers.Add(CharacterCreationMagicResonanceBlockers.FinalizationPayloadInvalid);
            return null;
        }
        CharacterCreationMagicResonanceCatalogOption option = matches[0];
        var candidate = new CharacterCreationMagicResonanceOptionFinalizationSource(
            CharacterCreationMagicResonanceSchemas.FinalizationSourceV1,
            option.Identity,
            option.Name,
            option.Category,
            levels,
            option.PointCost,
            option.SourceBook,
            option.Page,
            option.SourceNodeDigest,
            option.CanonicalSourceXml,
            option.CanonicalSourceXmlDigest,
            option.SourceAnchorIds,
            string.Empty);
        return candidate with { ProjectionDigest = ComputeOptionProjectionDigest(candidate) };
    }

    private static bool IsStructurallyValid(
        CharacterCreationMagicResonanceFinalizationContribution contribution) =>
        string.Equals(contribution.Schema,
            CharacterCreationMagicResonanceSchemas.FinalizationContributionV1,
            StringComparison.Ordinal)
        && CharacterCreationMagicResonanceDigest.IsCanonical(
            contribution.ExpectedRawCharacterXmlDigest)
        && contribution.PrerequisiteDraftRevision > 0
        && CharacterCreationMagicResonanceDigest.IsCanonical(contribution.PrerequisiteDraftDigest)
        && contribution.AttributesDraftRevision > 0
        && CharacterCreationMagicResonanceDigest.IsCanonical(contribution.AttributesDraftDigest)
        && CharacterCreationMagicResonanceDigest.IsCanonical(contribution.AuthorityDigest)
        && CharacterCreationMagicResonanceDigest.IsCanonical(contribution.SourceInputsDigest)
        && CharacterCreationMagicResonanceDigest.IsCanonical(contribution.CustomDataInputsDigest)
        && CharacterCreationMagicResonanceDigest.IsCanonical(contribution.GmPolicyDigest)
        && CharacterCreationMagicResonanceDigest.IsCanonical(contribution.RuntimeDigest)
        && IsValidTalentProjection(contribution.Talent)
        && (contribution.Tradition is null || IsValidOptionProjection(contribution.Tradition))
        && (contribution.Stream is null || IsValidOptionProjection(contribution.Stream))
        && IsCanonicalProjectionList(contribution.AdeptPowers)
        && IsCanonicalProjectionList(contribution.Spells)
        && IsCanonicalProjectionList(contribution.ComplexForms)
        && IsCanonicalSet(contribution.SourceAnchorIds)
        && CharacterCreationMagicResonanceDigest.IsCanonical(contribution.ContributionDigest)
        && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
            contribution.ContributionDigest,
            ComputeContributionDigest(contribution));

    private static bool HasSupportedTalentPayload(
        CharacterCreationMagicResonanceTalentOption talent) =>
        HasValidTalentPayload(talent)
        && TryParseCanonicalPayload(
            talent.CanonicalSourceXml,
            talent.CanonicalSourceXmlDigest,
            "talent",
            out XElement? source)
        && source is not null
        && source.Elements().All(element => TalentElements.Contains(
            element.Name.LocalName, StringComparer.Ordinal));

    private static bool HasSupportedOptionPayload(
        CharacterCreationMagicResonanceCatalogOption option)
    {
        string expectedRoot = option.Identity.Kind switch
        {
            CharacterCreationMagicResonanceKinds.Tradition => "tradition",
            CharacterCreationMagicResonanceKinds.Stream => "tradition",
            CharacterCreationMagicResonanceKinds.AdeptPower => "power",
            CharacterCreationMagicResonanceKinds.Spell => "spell",
            CharacterCreationMagicResonanceKinds.ComplexForm => "complexform",
            _ => string.Empty
        };
        return HasValidOptionPayload(option)
               && TryParseCanonicalPayload(
                   option.CanonicalSourceXml,
                   option.CanonicalSourceXmlDigest,
                   expectedRoot,
                   out XElement? source)
               && source is not null
               && source.Elements().All(element => AllowedElements(option.Identity.Kind).Contains(
                   element.Name.LocalName, StringComparer.Ordinal));
    }

    private static bool IsValidTalentProjection(
        CharacterCreationMagicResonanceTalentFinalizationSource source) =>
        source is not null
        && string.Equals(source.Schema,
            CharacterCreationMagicResonanceSchemas.FinalizationSourceV1,
            StringComparison.Ordinal)
        && CharacterCreationMagicResonanceDigest.IsCanonical(source.SourceNodeDigest)
        && CharacterCreationMagicResonanceDigest.IsCanonical(source.CanonicalSourceXmlDigest)
        && IsCanonicalSet(source.SourceAnchorIds)
        && CharacterCreationMagicResonanceDigest.IsCanonical(source.ProjectionDigest)
        && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
            source.ProjectionDigest, ComputeTalentProjectionDigest(source));

    private static bool IsValidOptionProjection(
        CharacterCreationMagicResonanceOptionFinalizationSource source) =>
        source is not null
        && string.Equals(source.Schema,
            CharacterCreationMagicResonanceSchemas.FinalizationSourceV1,
            StringComparison.Ordinal)
        && source.Levels > 0
        && source.PointCost >= 0m
        && CharacterCreationMagicResonanceDigest.IsCanonical(source.SourceNodeDigest)
        && CharacterCreationMagicResonanceDigest.IsCanonical(source.CanonicalSourceXmlDigest)
        && IsCanonicalSet(source.SourceAnchorIds)
        && CharacterCreationMagicResonanceDigest.IsCanonical(source.ProjectionDigest)
        && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
            source.ProjectionDigest, ComputeOptionProjectionDigest(source));

    private static bool IsCanonicalProjectionList(
        IReadOnlyList<CharacterCreationMagicResonanceOptionFinalizationSource> sources) =>
        sources is not null
        && sources.All(IsValidOptionProjection)
        && sources.Select(item => item.Identity).Distinct().Count() == sources.Count
        && sources.SequenceEqual(sources.OrderBy(item => item.Identity.SourceId, StringComparer.Ordinal));

    private static bool TryParseCanonicalPayload(
        string canonicalXml,
        string expectedDigest,
        string expectedRoot,
        out XElement? source)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(canonicalXml)
            || canonicalXml.Length > 1_048_576
            || !CharacterCreationMagicResonanceDigest.IsCanonical(expectedDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                expectedDigest,
                CharacterCreationMagicResonanceDigest.ComputeUtf8(canonicalXml)))
            return false;
        try
        {
            source = XElement.Parse(canonicalXml, LoadOptions.None);
            return string.Equals(source.Name.LocalName, expectedRoot, StringComparison.Ordinal)
                   && string.Equals(
                       source.ToString(SaveOptions.DisableFormatting),
                       canonicalXml,
                       StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            source = null;
            return false;
        }
    }

    private static string Read(XElement source, string name)
    {
        XElement[] matches = source.Elements().Where(element =>
                string.Equals(element.Name.LocalName, name, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0].Value : string.Empty;
    }

    private static int ReadInt(XElement source, string name) =>
        int.TryParse(Read(source, name), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out int parsed) && parsed >= 0
            ? parsed
            : 0;

    private static bool HasExactTypedOptionProjection(
        CharacterCreationMagicResonanceCatalogOption option,
        XElement source)
    {
        switch (option.Identity.Kind)
        {
            case CharacterCreationMagicResonanceKinds.Tradition:
                return string.Equals(option.Category, "magic-tradition", StringComparison.Ordinal)
                       && option.PointCost == 1m
                       && option.MaximumLevels == 1
                       && string.Equals(option.DrainExpression, Read(source, "drain"),
                           StringComparison.Ordinal);
            case CharacterCreationMagicResonanceKinds.Stream:
                return string.Equals(option.Category, "resonance-stream", StringComparison.Ordinal)
                       && option.PointCost == 1m
                       && option.MaximumLevels == 1
                       && string.Equals(option.DrainExpression, Read(source, "drain"),
                           StringComparison.Ordinal);
            case CharacterCreationMagicResonanceKinds.AdeptPower:
                if (!decimal.TryParse(Read(source, "points"), NumberStyles.Number,
                        CultureInfo.InvariantCulture, out decimal points)
                    || points <= 0m)
                    return false;
                XElement[] levelsElements = source.Elements().Where(element =>
                        string.Equals(element.Name.LocalName, "levels", StringComparison.Ordinal))
                    .Take(2)
                    .ToArray();
                if (levelsElements.Length > 1)
                    return false;
                bool hasLevels = levelsElements.Length == 1;
                bool parsedLevels = false;
                if (hasLevels && !bool.TryParse(levelsElements[0].Value, out parsedLevels))
                    return false;
                bool usesLevels = hasLevels && parsedLevels;
                int maximumLevels = 1;
                if (usesLevels
                    && (!int.TryParse(Read(source, "limit"), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out maximumLevels)
                        || maximumLevels <= 0))
                    return false;
                return string.Equals(option.Category, "adept-power", StringComparison.Ordinal)
                       && option.PointCost == points
                       && option.MaximumLevels == maximumLevels
                       && string.IsNullOrEmpty(option.DrainExpression);
            case CharacterCreationMagicResonanceKinds.Spell:
                string category = Read(source, "category");
                if (string.IsNullOrEmpty(category))
                    category = "uncategorized";
                return string.Equals(option.Category, category, StringComparison.Ordinal)
                       && option.PointCost == 1m
                       && option.MaximumLevels == 1
                       && string.IsNullOrEmpty(option.DrainExpression);
            case CharacterCreationMagicResonanceKinds.ComplexForm:
                return string.Equals(option.Category, "complex-form", StringComparison.Ordinal)
                       && option.PointCost == 1m
                       && option.MaximumLevels == 1
                       && string.IsNullOrEmpty(option.DrainExpression);
            default:
                return false;
        }
    }

    private static bool TryReadRestriction(
        XElement source,
        string container,
        bool allowMetatypeCategory,
        out string[] metatypeNames,
        out string[] metatypeCategories)
    {
        metatypeNames = [];
        metatypeCategories = [];
        XElement[] restrictions = source.Elements().Where(element =>
                string.Equals(element.Name.LocalName, container, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (restrictions.Length == 0)
            return true;
        if (restrictions.Length != 1 || restrictions[0].HasAttributes)
            return false;
        XElement[] oneOf = restrictions[0].Elements("oneof").Take(2).ToArray();
        if (oneOf.Length != 1
            || oneOf[0].HasAttributes
            || !oneOf[0].Elements().Any()
            || oneOf[0].Elements().Any(element =>
                element.HasAttributes
                || element.HasElements
                || string.IsNullOrWhiteSpace(element.Value)
                || element.Name.LocalName != "metatype"
                && (!allowMetatypeCategory || element.Name.LocalName != "metatypecategory")))
            return false;
        metatypeNames = oneOf[0].Elements("metatype")
            .Select(element => element.Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        metatypeCategories = oneOf[0].Elements("metatypecategory")
            .Select(element => element.Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return true;
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

    private static string[] AllowedElements(string kind) => kind switch
    {
        CharacterCreationMagicResonanceKinds.Tradition or
            CharacterCreationMagicResonanceKinds.Stream => TraditionElements,
        CharacterCreationMagicResonanceKinds.AdeptPower => PowerElements,
        CharacterCreationMagicResonanceKinds.Spell => SpellElements,
        CharacterCreationMagicResonanceKinds.ComplexForm => ComplexFormElements,
        _ => []
    };

    private static bool IsCanonicalSet(IReadOnlyList<string> values) =>
        values is { Count: > 0 }
        && values.All(value => !string.IsNullOrWhiteSpace(value)
                               && string.Equals(value, value.Trim(), StringComparison.Ordinal))
        && values.SequenceEqual(values.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal);

    private static string[] Normalize(IEnumerable<string> blockers) => blockers
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(item => item, StringComparer.Ordinal)
        .ToArray();

    private static CharacterCreationMagicResonanceTalentOption? FindUniqueTalent(
        CharacterCreationMagicResonanceAuthority authority,
        CharacterCreationMagicResonanceDraft draft)
    {
        CharacterCreationMagicResonanceTalentOption[] matches = authority.Talents
            .Where(item => item.Identity == draft.TalentIdentity
                           && string.Equals(item.Kind, draft.TalentKind, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool TryComputePowerCost(
        IReadOnlyList<CharacterCreationMagicResonanceOptionFinalizationSource> powers,
        out decimal cost)
    {
        cost = 0m;
        try
        {
            foreach (CharacterCreationMagicResonanceOptionFinalizationSource power in powers)
                cost = checked(cost + checked(power.PointCost * power.Levels));
            return true;
        }
        catch (OverflowException)
        {
            cost = 0m;
            return false;
        }
    }

    private static CharacterCreationMagicResonanceFinalizationContribution EmptyContribution()
    {
        var emptyTalent = new CharacterCreationMagicResonanceTalentFinalizationSource(
            string.Empty,
            new(string.Empty, string.Empty, string.Empty),
            string.Empty,
            0,
            0,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            string.Empty);
        return new(
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            emptyTalent,
            null,
            null,
            [],
            [],
            [],
            [],
            string.Empty);
    }

    private static CharacterCreationMagicResonanceTalentOption DisabledTalent() => new(
        new(string.Empty, string.Empty, string.Empty),
        string.Empty,
        string.Empty,
        CharacterCreationMagicResonanceKinds.Unsupported,
        0,
        0,
        0,
        0,
        0,
        0m,
        false,
        false,
        false,
        false,
        false,
        [],
        [],
        [],
        string.Empty,
        [],
        [CharacterCreationMagicResonanceBlockers.FinalizationPayloadInvalid],
        false);

    private static readonly string[] TalentElements =
    [
        "name", "value", "qualities", "specialattribpoints", "magic", "resonance", "depth",
        "spells", "cfp", "skillqty", "skillval", "skilltype", "skillchoices",
        "skillgroupqty", "skillgroupval", "skillgrouptype", "skillgroupchoices",
        "required", "forbidden"
    ];

    private static readonly string[] TraditionElements =
        ["id", "name", "drain", "source", "page", "spirits", "bonus", "required", "forbidden"];

    private static readonly string[] PowerElements =
    [
        "id", "name", "points", "levels", "limit", "source", "page", "action", "adeptway",
        "adeptwayrequires", "bonus", "required", "forbidden"
    ];

    private static readonly string[] SpellElements =
    [
        "id", "name", "page", "source", "category", "damage", "descriptor", "duration", "dv",
        "range", "type", "useskill", "bonus", "required", "forbidden"
    ];

    private static readonly string[] ComplexFormElements =
        ["id", "name", "target", "duration", "fv", "source", "page", "bonus", "required", "forbidden"];
}
