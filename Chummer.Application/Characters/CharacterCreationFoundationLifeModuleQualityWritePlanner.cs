using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// A deterministic, headless serialization plan for the exact Chummer5
/// LifeModule Quality graph produced by supported non-prompt level effects.
/// Producing a plan is not creation-finalization authority and never writes a
/// workspace. The caller must supply the exact effective source node produced
/// by the authoritative VERSION-over-MODULE resolver and its raw-input digest.
/// </summary>
internal static class CharacterCreationFoundationLifeModuleQualityWritePlanner
{
    private const string PlanSchema =
        "chummer.character_creation_foundation_lifemodule_quality_write_plan.v2";
    private const string WriterSemantics =
        "chummer5-quality-create-save-5.225.0;attributelevel-and-digest-bound-skilllevel-int32-any-default1-create-save;one-quality-ordered-distinct-improvements;deterministic-quality-uuidv8;no-partial-apply";

    private static readonly IReadOnlySet<string> s_AllowedSourceChildren =
        new HashSet<string>(
            [
                "id", "name", "karma", "category", "metagenic", "metagenetic",
                "altnotes", "notes", "notesColor", "doublecareer",
                "canbuywithspellpoints", "print", "implemented", "contributetobp",
                "contributetolimit", "stagedpurchase", "source", "page", "mutant",
                "stage", "bonus", "story", "required", "selectable"
            ],
            StringComparer.Ordinal);

    public static CharacterCreationFoundationEffectWritePlanResult Build(
        CharacterWorkspaceId workspaceId,
        string rulesetId,
        CharacterCreationFoundationDraftLedger ledger,
        LifeModuleLegalOptionDto module,
        LifeModuleVersionProjectionDto? version,
        string effectiveSourceXml,
        string sourceAuthorityDigest,
        string defaultNotesColor,
        string? skillsSourceXml = null,
        string? skillsSourceDigest = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(module);

        bool hasSkillSourceInput = skillsSourceXml is not null || skillsSourceDigest is not null;
        CharacterCreationFoundationSkillSourceAuthority? skillSourceAuthority = null;
        bool skillSourceValid = !hasSkillSourceInput
                                || CharacterCreationFoundationSkillSourceAuthority.TryCreate(
                                    skillsSourceXml,
                                    skillsSourceDigest,
                                    out skillSourceAuthority);
        CharacterCreationFoundationEffectCompilation compilation =
            CharacterCreationFoundationEffectCompiler.Compile(
                rulesetId,
                ledger,
                module,
                version,
                skillSourceAuthority);
        var blockers = new List<string>();
        if (workspaceId != ledger.WorkspaceId
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                sourceAuthorityDigest)
            || !FixedTimeEquals(sourceAuthorityDigest, ledger.SourceDigest)
            || string.IsNullOrWhiteSpace(defaultNotesColor)
            || !string.Equals(defaultNotesColor, defaultNotesColor.Trim(), StringComparison.Ordinal))
        {
            blockers.Add(
                CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
        }
        if (!skillSourceValid)
        {
            blockers.Add(
                CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
        }

        blockers.AddRange(compilation.Blockers.Where(blocker => !string.Equals(
            blocker,
            CharacterCreationFoundationBlockers.FinalizationRequiredStagesIncomplete,
            StringComparison.Ordinal)));
        if (compilation.Requirements.Any(requirement => !string.Equals(
                requirement.CompilationStatus,
                CharacterCreationFoundationEffectCompilationStatuses.Supported,
                StringComparison.Ordinal))
            || compilation.Effects.Count == 0
            || compilation.Effects.Any(effect => !string.Equals(
                effect.CompilationStatus,
                CharacterCreationFoundationEffectCompilationStatuses.Supported,
                StringComparison.Ordinal)))
        {
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
        }

        XElement? source = ParseSource(effectiveSourceXml, blockers);
        SourceDefinition? definition = source is null
            ? null
            : ReadDefinition(source, module, version, defaultNotesColor, blockers);
        if (source is not null
            && !SourceEffectsMatch(source, ledger, compilation))
        {
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
        }

        string[] normalizedBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(blocker => blocker, StringComparer.Ordinal)
            .ToArray();
        if (normalizedBlockers.Length > 0 || definition is null)
            return new CharacterCreationFoundationEffectWritePlanResult(null, normalizedBlockers);

        string qualityId = CreateDeterministicQualityId(
            workspaceId,
            ledger,
            compilation,
            definition.SourceId,
            sourceAuthorityDigest);
        XElement[] improvementElements = compilation.Effects
            .Select(instruction => CreateImprovement(instruction, qualityId, defaultNotesColor))
            .ToArray();
        XElement qualityElement = CreateQuality(
            source!,
            definition,
            qualityId,
            defaultNotesColor);
        string writerRuntimeDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeCanonicalDigest(new
            {
                Schema = PlanSchema,
                RulesetId = rulesetId,
                WriterSemantics,
                SkillSourceDigest = skillSourceAuthority?.SourceDigest ?? string.Empty,
                DefaultNotesColor = defaultNotesColor
            });
        var plan = new CharacterCreationFoundationEffectWritePlan(
            Schema: PlanSchema,
            WriterRuntimeDigest: writerRuntimeDigest,
            WorkspaceId: workspaceId,
            DraftRevision: ledger.DraftRevision,
            DraftDigest: ledger.DraftDigest,
            CompilationDigest: compilation.CompilationDigest,
            SourceAuthorityDigest: sourceAuthorityDigest,
            SkillSourceAuthorityDigest: skillSourceAuthority?.SourceDigest,
            SourceId: definition.SourceId,
            QualityId: qualityId,
            QualityXml: qualityElement.ToString(SaveOptions.DisableFormatting),
            ImprovementXml: improvementElements
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToArray(),
            InstructionDigests: compilation.Effects
                .Select(instruction => instruction.InstructionDigest)
                .ToArray(),
            EffectProvenance: compilation.Effects.Select(instruction =>
                new CharacterCreationFoundationEffectWriteProvenance(
                    instruction.Order,
                    instruction.EffectId,
                    instruction.SourcePhase,
                    instruction.InstructionDigest,
                    instruction.SourceAnchorIds.ToArray(),
                    instruction.TargetBinding,
                    instruction.IgnoredSourceMetadata
                        .OrderBy(item => item.Key, StringComparer.Ordinal)
                        .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)))
                .ToArray(),
            PlanDigest: string.Empty);
        plan = plan with
        {
            PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeCanonicalDigest(plan with { PlanDigest = string.Empty })
        };
        return new CharacterCreationFoundationEffectWritePlanResult(plan, []);
    }

    private static XElement? ParseSource(string? effectiveSourceXml, ICollection<string> blockers)
    {
        try
        {
            XElement source = XElement.Parse(effectiveSourceXml ?? string.Empty, LoadOptions.None);
            if (source.Name.NamespaceName.Length != 0
                || source.Name.LocalName is not ("module" or "version")
                || source.HasAttributes
                || source.Elements().Any(child => child.Name.NamespaceName.Length != 0)
                || source.Elements().Any(child => !s_AllowedSourceChildren.Contains(
                    child.Name.LocalName)))
            {
                blockers.Add(
                    CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
                return null;
            }

            return source;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or System.Xml.XmlException)
        {
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
            return null;
        }
    }

    private static SourceDefinition? ReadDefinition(
        XElement source,
        LifeModuleLegalOptionDto module,
        LifeModuleVersionProjectionDto? version,
        string defaultNotesColor,
        ICollection<string> blockers)
    {
        string[] singletonNames =
        [
            "id", "name", "karma", "category", "metagenic", "metagenetic",
            "altnotes", "notes", "notesColor", "doublecareer",
            "canbuywithspellpoints", "print", "implemented", "contributetobp",
            "contributetolimit", "stagedpurchase", "source", "page", "mutant",
            "stage", "bonus"
        ];
        if (singletonNames.Any(name => source.Elements(name).Take(2).Count() > 1)
            || (source.Elements("metagenic").Any()
                && source.Elements("metagenetic").Any())
            || (source.Elements("altnotes").Any()
                && source.Elements("notes").Any())
            || singletonNames
                .Where(name => name != "bonus")
                .SelectMany(name => source.Elements(name))
                .Any(element => element.HasElements || element.HasAttributes))
        {
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
            return null;
        }

        string expectedId = version?.VersionId ?? module.ModuleId;
        string expectedName = version?.Label ?? module.Name;
        decimal expectedKarma = version?.KarmaCost ?? module.KarmaCost;
        string expectedKarmaRaw = version?.KarmaRaw ?? module.KarmaRaw;
        string expectedSource = version?.Source ?? module.Source;
        string expectedPage = version?.PageReference ?? module.PageReference;
        string sourceId = ReadRequired(source, "id");
        string name = ReadRequired(source, "name");
        string karmaRaw = ReadRequired(source, "karma");
        string sourceBook = ReadRequired(source, "source");
        string page = ReadRequired(source, "page");
        string stage = ReadRequired(source, "stage");
        string[] normalizedScalarNames =
            ["id", "name", "karma", "category", "source", "page", "stage"];
        bool sourceIdValid = Guid.TryParseExact(sourceId, "D", out Guid parsedSourceId)
                             && parsedSourceId != Guid.Empty
                             && string.Equals(sourceId, parsedSourceId.ToString("D"), StringComparison.Ordinal);
        if (normalizedScalarNames.Any(name => source.Element(name) is not XElement element
                || !string.Equals(element.Value, element.Value.Trim(), StringComparison.Ordinal))
            || !sourceIdValid
            || !string.Equals(sourceId, expectedId, StringComparison.Ordinal)
            || !string.Equals(name, expectedName, StringComparison.Ordinal)
            || !int.TryParse(
                karmaRaw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int karma)
            || !string.Equals(karmaRaw, expectedKarmaRaw, StringComparison.Ordinal)
            || karma != expectedKarma
            || !string.Equals(sourceBook, expectedSource, StringComparison.Ordinal)
            || !string.Equals(page, expectedPage, StringComparison.Ordinal)
            || !string.Equals(stage, module.StageId, StringComparison.Ordinal)
            || !string.Equals(ReadRequired(source, "category"), "LifeModule", StringComparison.Ordinal)
            || source.Element("bonus") is null
            || source.Element("bonus")!.HasAttributes
            || string.IsNullOrWhiteSpace(defaultNotesColor))
        {
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
            return null;
        }

        if (!TryReadBoolean(source, "implemented", defaultValue: true, out bool implemented)
            || !TryReadBoolean(source, "contributetobp", defaultValue: true, out bool contributeToBp)
            || !TryReadBoolean(source, "contributetolimit", defaultValue: true, out bool contributeToLimit)
            || !TryReadBoolean(source, "stagedpurchase", defaultValue: false, out bool stagedPurchase)
            || !TryReadBoolean(source, "doublecareer", defaultValue: true, out bool doubleCareer)
            || !TryReadBoolean(source, "canbuywithspellpoints", defaultValue: false, out bool spellPoints)
            || !TryReadBooleanAlias(source, "metagenic", "metagenetic", out bool metagenic)
            || !TryReadBoolean(source, "print", defaultValue: true, out bool print))
        {
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
            return null;
        }

        string notesColor = ReadOptional(source, "notesColor") ?? defaultNotesColor;
        if (!string.Equals(notesColor, defaultNotesColor, StringComparison.Ordinal))
        {
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
            return null;
        }

        return new SourceDefinition(
            sourceId,
            name,
            karma,
            implemented,
            contributeToBp,
            contributeToLimit,
            stagedPurchase,
            doubleCareer,
            spellPoints,
            metagenic,
            print,
            source.Element("mutant") is not null,
            sourceBook,
            page,
            ReadOptional(source, "altnotes") ?? ReadOptional(source, "notes") ?? string.Empty,
            notesColor,
            stage);
    }

    private static bool SourceEffectsMatch(
        XElement source,
        CharacterCreationFoundationDraftLedger ledger,
        CharacterCreationFoundationEffectCompilation compilation)
    {
        XElement[] sourceEffects = source.Element("bonus")?.Elements().ToArray() ?? [];
        if (sourceEffects.Length != ledger.ProjectedEffects.Count
            || sourceEffects.Length != compilation.Effects.Count)
        {
            return false;
        }

        for (int index = 0; index < sourceEffects.Length; index++)
        {
            XElement projected;
            try
            {
                projected = XElement.Parse(ledger.ProjectedEffects[index].RawXml, LoadOptions.None);
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }

            if (!XNode.DeepEquals(sourceEffects[index], projected)
                || compilation.Effects[index].Order != index + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static XElement CreateQuality(
        XElement source,
        SourceDefinition definition,
        string qualityId,
        string defaultNotesColor)
    {
        return new XElement(
            "quality",
            new XElement("sourceid", definition.SourceId),
            new XElement("guid", qualityId),
            new XElement("name", definition.Name),
            new XElement("extra", string.Empty),
            new XElement("bp", definition.Karma.ToString(CultureInfo.InvariantCulture)),
            new XElement("implemented", LegacyBoolean(definition.Implemented)),
            new XElement("contributetobp", LegacyBoolean(definition.ContributeToBp)),
            new XElement("contributetolimit", LegacyBoolean(definition.ContributeToLimit)),
            new XElement("stagedpurchase", LegacyBoolean(definition.StagedPurchase)),
            new XElement("doublecareer", LegacyBoolean(definition.DoubleCareer)),
            new XElement("canbuywithspellpoints", LegacyBoolean(definition.CanBuyWithSpellPoints)),
            new XElement("metagenic", LegacyBoolean(definition.Metagenic)),
            new XElement("print", LegacyBoolean(definition.Print)),
            new XElement("qualitytype", "LifeModule"),
            new XElement("qualitysource", "LifeModule"),
            new XElement("mutant", LegacyBoolean(definition.Mutant)),
            new XElement("source", definition.Source),
            new XElement("page", definition.Page),
            new XElement("sourcename", string.Empty),
            new XElement("bonus", source.Element("bonus")!.Nodes()),
            new XElement("firstlevelbonus", string.Empty),
            new XElement("naturalweapons", string.Empty),
            new XElement("notes", definition.Notes),
            new XElement("notesColor", definition.NotesColor.Length == 0
                ? defaultNotesColor
                : definition.NotesColor),
            new XElement("stage", definition.Stage));
    }

    private static XElement CreateImprovement(
        CharacterCreationFoundationEffectInstruction instruction,
        string qualityId,
        string defaultNotesColor)
    {
        string? rawValue = instruction.Parameters.TryGetValue("val", out string? value)
            ? value
            : null;
        bool isSkillLevel = string.Equals(
            instruction.EffectKind,
            "skilllevel",
            StringComparison.Ordinal);
        int parsedValue = isSkillLevel
            ? CharacterCreationFoundationEffectCompiler.ParseLegacySkillLevelValue(rawValue)
            : CharacterCreationFoundationEffectCompiler.ParseLegacyAttributeLevelValue(rawValue);
        string improvedName = isSkillLevel
            ? instruction.TargetBinding!.CanonicalName
            : instruction.TargetId;
        return new XElement(
            "improvement",
            new XElement("target", string.Empty),
            new XElement("improvedname", improvedName),
            new XElement("sourcename", qualityId),
            new XElement("min", "0"),
            new XElement("max", "0"),
            new XElement("aug", "0"),
            new XElement("augmax", "0"),
            new XElement("val", parsedValue.ToString(CultureInfo.InvariantCulture)),
            new XElement("rating", "1"),
            new XElement("exclude", string.Empty),
            new XElement("condition", string.Empty),
            new XElement("improvementttype", isSkillLevel ? "SkillLevel" : "Attributelevel"),
            new XElement("improvementsource", "Quality"),
            new XElement("custom", "False"),
            new XElement("customname", string.Empty),
            new XElement("customid", string.Empty),
            new XElement("customgroup", string.Empty),
            new XElement("addtorating", "0"),
            new XElement("enabled", "1"),
            new XElement("order", "0"),
            new XElement("notes", string.Empty),
            new XElement("notesColor", defaultNotesColor));
    }

    private static string CreateDeterministicQualityId(
        CharacterWorkspaceId workspaceId,
        CharacterCreationFoundationDraftLedger ledger,
        CharacterCreationFoundationEffectCompilation compilation,
        string sourceId,
        string sourceAuthorityDigest)
    {
        string seed = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            Schema = PlanSchema,
            WorkspaceId = workspaceId.Value,
            ledger.DraftRevision,
            ledger.DraftDigest,
            compilation.CompilationDigest,
            SourceId = sourceId,
            SourceAuthorityDigest = sourceAuthorityDigest
        });
        char[] hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))
            .AsSpan(0, 32)
            .ToArray();
        hex[12] = '8';
        hex[16] = '8';
        return Guid.ParseExact(new string(hex), "N").ToString("D");
    }

    private static string ReadRequired(XElement source, string name) =>
        (source.Element(name)?.Value ?? string.Empty).Trim();

    private static string? ReadOptional(XElement source, string name) =>
        source.Element(name) is XElement element ? element.Value : null;

    private static bool TryReadBoolean(
        XElement source,
        string name,
        bool defaultValue,
        out bool value)
    {
        XElement? element = source.Element(name);
        if (element is null)
        {
            value = defaultValue;
            return true;
        }

        return bool.TryParse(element.Value.Trim(), out value);
    }

    private static bool TryReadBooleanAlias(
        XElement source,
        string currentName,
        string legacyName,
        out bool value)
    {
        XElement? element = source.Element(currentName) ?? source.Element(legacyName);
        if (element is null)
        {
            value = false;
            return true;
        }

        return bool.TryParse(element.Value.Trim(), out value);
    }

    private static string LegacyBoolean(bool value) => value ? "True" : "False";

    private static bool FixedTimeEquals(string? left, string? right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record SourceDefinition(
        string SourceId,
        string Name,
        int Karma,
        bool Implemented,
        bool ContributeToBp,
        bool ContributeToLimit,
        bool StagedPurchase,
        bool DoubleCareer,
        bool CanBuyWithSpellPoints,
        bool Metagenic,
        bool Print,
        bool Mutant,
        string Source,
        string Page,
        string Notes,
        string NotesColor,
        string Stage);
}

internal sealed record CharacterCreationFoundationEffectWritePlan(
    string Schema,
    string WriterRuntimeDigest,
    CharacterWorkspaceId WorkspaceId,
    long DraftRevision,
    string DraftDigest,
    string CompilationDigest,
    string SourceAuthorityDigest,
    string? SkillSourceAuthorityDigest,
    string SourceId,
    string QualityId,
    string QualityXml,
    IReadOnlyList<string> ImprovementXml,
    IReadOnlyList<string> InstructionDigests,
    IReadOnlyList<CharacterCreationFoundationEffectWriteProvenance> EffectProvenance,
    string PlanDigest);

internal sealed record CharacterCreationFoundationEffectWriteProvenance(
    int Order,
    string EffectId,
    string SourcePhase,
    string InstructionDigest,
    IReadOnlyList<string> SourceAnchorIds,
    CharacterCreationFoundationEffectTargetBinding? TargetBinding,
    IReadOnlyDictionary<string, string> IgnoredSourceMetadata);

internal sealed record CharacterCreationFoundationEffectWritePlanResult(
    CharacterCreationFoundationEffectWritePlan? Plan,
    IReadOnlyList<string> Blockers)
{
    public bool IsReady => Plan is not null && Blockers.Count == 0;
}
