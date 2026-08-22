using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Characters;

/// <summary>
/// Deterministically compiles the persisted foundation draft into reviewable
/// instructions. Supported effects are written as source-owned LifeModule
/// Quality/Improvement graphs, never as direct edits to attributes or skills.
/// Classifying one effect does not authorize partial application of a ledger.
/// </summary>
internal static class CharacterCreationFoundationEffectCompiler
{
    private const string CompilerSemantics =
        "chummer5-life-module-improvement-oracle-5.225.0;attributelevel-v1-int32-any-default1;version-before-module;no-partial-apply";

    private static readonly IReadOnlySet<string> s_AttributeIds = new HashSet<string>(
        [
            "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL",
            "EDG", "MAG", "MAGAdept", "RES", "ESS", "DEP"
        ],
        StringComparer.Ordinal);

    public static CharacterCreationFoundationEffectCompilation Compile(
        string rulesetId,
        CharacterCreationFoundationDraftLedger ledger,
        LifeModuleLegalOptionDto module,
        LifeModuleVersionProjectionDto? version)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(module);

        string compilerRuntimeDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeCanonicalDigest(new
            {
                Schema = CharacterCreationFoundationSchemas.EffectCompilationV1,
                RulesetId = rulesetId,
                CompilerSemantics,
                SupportedEffectKinds = new[] { "attributelevel:v1" },
                SupportedRequirementKinds = new[] { "oneof:metatype" }
            });

        LifeModuleEffectProjectionDto[] authoritativeEffects =
        [
            .. version?.Effects ?? [],
            .. module.Effects
        ];
        LifeModuleRequirementProjectionDto[] authoritativeRequirements =
        [
            .. module.Requirements,
            .. version?.Requirements ?? []
        ];
        LifeModuleFollowUpPromptDto[] authoritativePrompts =
        [
            .. version?.FollowUps ?? [],
            .. module.FollowUps
        ];

        var blockers = new List<string>
        {
            // This ledger schema contains Nationality only.  Creation cannot be
            // finalized before Formative Years, Teen Years and Further Education.
            CharacterCreationFoundationBlockers.FinalizationRequiredStagesIncomplete
        };

        bool effectLedgerMatches = CharacterCreationFoundationDraftLedgerIntegrity
            .CanonicallyEquals(authoritativeEffects, ledger.ProjectedEffects);
        if (!effectLedgerMatches)
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);

        CharacterCreationFoundationRequirementInstruction[] requirements = ledger
            .RequirementEvaluations
            .Select((requirement, index) => CompileRequirement(
                index,
                requirement,
                index < authoritativeRequirements.Length
                    ? authoritativeRequirements[index]
                    : null,
                ledger.RequestedMetatype))
            .ToArray();
        if (requirements.Length != authoritativeRequirements.Length
            || requirements.Any(item => !string.Equals(
                item.CompilationStatus,
                CharacterCreationFoundationEffectCompilationStatuses.Supported,
                StringComparison.Ordinal)))
        {
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationRequirementUnsupported);
        }

        int versionEffectCount = version?.Effects.Count ?? 0;
        CharacterCreationFoundationEffectInstruction[] effects = ledger.ProjectedEffects
            .Select((effect, index) => CompileEffect(
                index,
                effect,
                index < versionEffectCount
                    ? CharacterCreationFoundationEffectSourcePhases.Version
                    : CharacterCreationFoundationEffectSourcePhases.Module,
                authoritativePrompts
                    .Where(prompt => string.Equals(
                        prompt.EffectId,
                        effect.EffectId,
                        StringComparison.Ordinal))
                    .ToArray()))
            .ToArray();
        if (effects.Any(item => string.Equals(
                item.CompilationStatus,
                CharacterCreationFoundationEffectCompilationStatuses.PromptRequired,
                StringComparison.Ordinal)))
        {
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationPromptRequired);
        }
        if (effects.Any(item => string.Equals(
                item.CompilationStatus,
                CharacterCreationFoundationEffectCompilationStatuses.Unsupported,
                StringComparison.Ordinal)))
        {
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
        }

        string[] normalizedBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        bool requirementsSupported = requirements.Length == authoritativeRequirements.Length
                                     && requirements.All(item => string.Equals(
                                         item.CompilationStatus,
                                         CharacterCreationFoundationEffectCompilationStatuses.Supported,
                                         StringComparison.Ordinal));
        bool isCompleteLedgerSupported = normalizedBlockers.Length == 0
                                         && effectLedgerMatches
                                         && requirementsSupported
                                         && effects.Length > 0
                                         && effects.All(item => string.Equals(
                                             item.CompilationStatus,
                                             CharacterCreationFoundationEffectCompilationStatuses.Supported,
                                             StringComparison.Ordinal));
        var compilation = new CharacterCreationFoundationEffectCompilation(
            Schema: CharacterCreationFoundationSchemas.EffectCompilationV1,
            CompilerRuntimeDigest: compilerRuntimeDigest,
            DraftRevision: ledger.DraftRevision,
            DraftDigest: ledger.DraftDigest,
            Requirements: requirements,
            Effects: effects,
            Blockers: normalizedBlockers,
            IsCompleteLedgerSupported: isCompleteLedgerSupported,
            CompilationDigest: string.Empty);
        return compilation with
        {
            CompilationDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeCanonicalDigest(compilation with { CompilationDigest = string.Empty })
        };
    }

    private static CharacterCreationFoundationRequirementInstruction CompileRequirement(
        int index,
        LifeModuleRequirementProjectionDto requirement,
        LifeModuleRequirementProjectionDto? authoritative,
        string requestedMetatype)
    {
        bool sourceMatches = authoritative is not null
                             && string.Equals(
                                 requirement.RequirementId,
                                 authoritative.RequirementId,
                                 StringComparison.Ordinal)
                             && string.Equals(
                                 requirement.Operator,
                                 authoritative.Operator,
                                 StringComparison.Ordinal)
                             && string.Equals(
                                 requirement.SubjectKind,
                                 authoritative.SubjectKind,
                                 StringComparison.Ordinal)
                             && requirement.AcceptedValues.SequenceEqual(
                                 authoritative.AcceptedValues,
                                 StringComparer.Ordinal)
                             && string.Equals(
                                 requirement.RawXml,
                                 authoritative.RawXml,
                                 StringComparison.Ordinal)
                             && requirement.SourceAnchorIds.SequenceEqual(
                                 authoritative.SourceAnchorIds,
                                 StringComparer.Ordinal);
        bool supported = sourceMatches
                         && IsExactMetatypeOneOf(requirement, requestedMetatype)
                         && requirement.IsMet
                         && requirement.DisableReasonKey is null
                         && !requirement.RequiresCharacterAuthority;
        string status = supported
            ? CharacterCreationFoundationEffectCompilationStatuses.Supported
            : CharacterCreationFoundationEffectCompilationStatuses.Unsupported;
        string? blocker = supported
            ? null
            : CharacterCreationFoundationBlockers.FinalizationRequirementUnsupported;
        var instruction = new CharacterCreationFoundationRequirementInstruction(
            Order: index + 1,
            RequirementId: requirement.RequirementId,
            Operator: requirement.Operator,
            SubjectKind: requirement.SubjectKind,
            AcceptedValues: requirement.AcceptedValues.ToArray(),
            SourceAnchorIds: requirement.SourceAnchorIds.ToArray(),
            CompilationStatus: status,
            Blocker: blocker,
            InstructionDigest: string.Empty);
        return instruction with
        {
            InstructionDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeCanonicalDigest(instruction with { InstructionDigest = string.Empty })
        };
    }

    private static CharacterCreationFoundationEffectInstruction CompileEffect(
        int index,
        LifeModuleEffectProjectionDto effect,
        string sourcePhase,
        IReadOnlyList<LifeModuleFollowUpPromptDto> prompts)
    {
        string effectKind = ReadEffectKind(effect.RawXml);
        string[] promptIds = prompts
            .Select(prompt => prompt.PromptId)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        bool requiresPrompt = promptIds.Length > 0;
        bool supported = !requiresPrompt && IsSupportedAttributeLevel(effect);
        string status = requiresPrompt
            ? CharacterCreationFoundationEffectCompilationStatuses.PromptRequired
            : supported
                ? CharacterCreationFoundationEffectCompilationStatuses.Supported
                : CharacterCreationFoundationEffectCompilationStatuses.Unsupported;
        string? blocker = requiresPrompt
            ? CharacterCreationFoundationBlockers.FinalizationPromptRequired
            : supported
                ? null
                : CharacterCreationFoundationBlockers.FinalizationEffectUnsupported;
        var instruction = new CharacterCreationFoundationEffectInstruction(
            Order: index + 1,
            EffectId: effect.EffectId,
            SourcePhase: sourcePhase,
            EffectKind: effectKind,
            Domain: effect.Domain,
            TargetId: effect.TargetId,
            Parameters: effect.Parameters
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            PromptIds: promptIds,
            SourceAnchorIds: effect.SourceAnchorIds.ToArray(),
            CompilationStatus: status,
            Blocker: blocker,
            InstructionDigest: string.Empty);
        return instruction with
        {
            InstructionDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeCanonicalDigest(instruction with { InstructionDigest = string.Empty })
        };
    }

    private static bool IsSupportedAttributeLevel(LifeModuleEffectProjectionDto effect)
    {
        if (!effect.IsFullyTyped
            || !string.Equals(effect.Domain, "attribute", StringComparison.Ordinal)
            || effect.BudgetId is not null
            || effect.BudgetDelta != 0
            || effect.SourceAnchorIds.Count == 0
            || effect.SourceAnchorIds.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        try
        {
            XElement element = XElement.Parse(effect.RawXml, LoadOptions.None);
            if (element.Name.NamespaceName.Length != 0
                || !string.Equals(
                    element.Name.LocalName,
                    "attributelevel",
                    StringComparison.OrdinalIgnoreCase)
                || element.HasAttributes)
            {
                return false;
            }

            XElement[] children = element.Elements().ToArray();
            XElement[] names = children.Where(child => string.Equals(
                    child.Name.LocalName,
                    "name",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            XElement[] values = children.Where(child => string.Equals(
                    child.Name.LocalName,
                    "val",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (children.Any(child => child.Name.NamespaceName.Length != 0)
                || children.Length != names.Length + values.Length
                || names.Length != 1
                || values.Length > 1
                || names[0].HasElements
                || names[0].HasAttributes
                || values.Any(value => value.HasElements || value.HasAttributes))
            {
                return false;
            }

            string name = names[0].Value.Trim();
            string rawValue = values.Length == 0 ? "1" : values[0].Value.Trim();
            if (!s_AttributeIds.Contains(name)
                || !string.Equals(effect.TargetId, name, StringComparison.Ordinal)
                || effect.Parameters.Count != (values.Length == 0 ? 1 : 2)
                || !effect.Parameters.TryGetValue("name", out string? projectedName)
                || !string.Equals(projectedName, name, StringComparison.Ordinal)
                || (values.Length > 0
                    && (!effect.Parameters.TryGetValue("val", out string? projectedValue)
                        || !string.Equals(projectedValue, rawValue, StringComparison.Ordinal))))
            {
                return false;
            }

            return values.Length == 0
                ? effect.AfterValue is null
                : string.Equals(effect.AfterValue, rawValue, StringComparison.Ordinal);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    internal static int ParseLegacyAttributeLevelValue(string? rawValue)
    {
        return rawValue is not null
               && int.TryParse(
                   rawValue,
                   System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out int value)
            ? value
            : 1;
    }

    private static bool IsExactMetatypeOneOf(
        LifeModuleRequirementProjectionDto requirement,
        string requestedMetatype)
    {
        if (!string.Equals(requirement.Operator, "oneof", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(requirement.SubjectKind, "metatype", StringComparison.OrdinalIgnoreCase)
            || !requirement.AcceptedValues.Contains(
                requestedMetatype,
                StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            XElement root = XElement.Parse(requirement.RawXml, LoadOptions.None);
            return root.Name.NamespaceName.Length == 0
                   && string.Equals(root.Name.LocalName, "oneof", StringComparison.OrdinalIgnoreCase)
                   && root.Attributes().All(attribute => attribute.IsNamespaceDeclaration)
                   && root.Elements().All(element =>
                       element.Name.NamespaceName.Length == 0
                       && string.Equals(
                           element.Name.LocalName,
                           "metatype",
                           StringComparison.OrdinalIgnoreCase)
                       && !element.HasElements
                       && !element.HasAttributes)
                   && root.Elements().Select(element => element.Value.Trim()).SequenceEqual(
                       requirement.AcceptedValues,
                       StringComparer.Ordinal);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static string ReadEffectKind(string rawXml)
    {
        try
        {
            XElement element = XElement.Parse(rawXml, LoadOptions.None);
            return element.Name.NamespaceName.Length == 0
                ? element.Name.LocalName
                : "unsupported-namespace";
        }
        catch (System.Xml.XmlException)
        {
            return "invalid-xml";
        }
    }
}
