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
        "chummer5-life-module-improvement-oracle-5.225.0;attributelevel-v1-int32-any-default1;skilllevel-v1-digest-bound-active-skill-int32-any-default1;knowledgeskilllevel-v1-digest-bound-free-knowledge-pool-decimal-any-default1;pushtext-addqualities-v1-digest-bound-lifo-dependent-quality-graph;version-before-module;no-partial-apply";

    private const string FreeKnowledgeSkillsIdentity = "FreeKnowledgeSkills";

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
        LifeModuleVersionProjectionDto? version,
        CharacterCreationFoundationSkillSourceAuthority? skillSourceAuthority = null,
        CharacterCreationFoundationQualitySourceAuthority? qualitySourceAuthority = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(module);

        string compilerRuntimeDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeCanonicalDigest(new
            {
                Schema = CharacterCreationFoundationSchemas.EffectCompilationV1,
                RulesetId = rulesetId,
                CompilerSemantics,
                SupportedEffectKinds = new[]
                {
                    "attributelevel:v1",
                    "skilllevel:v1",
                    "knowledgeskilllevel:v1-free-knowledge-pool",
                    "pushtext:v1-selection-stack",
                    "addqualities:v1-dependent-quality-graph"
                },
                SkillSourceDigest = skillSourceAuthority?.SourceDigest ?? string.Empty,
                QualitySourceDigest = qualitySourceAuthority?.SourceDigest ?? string.Empty,
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
                    .ToArray(),
                skillSourceAuthority,
                ledger.SourceDigest))
            .ToArray();
        CompositeSelectionCompilation composite = CompileCompositeSelections(
            ledger.ProjectedEffects,
            effects,
            qualitySourceAuthority,
            ledger.SourceDigest);
        effects = composite.Effects;
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
            CompilationDigest: string.Empty)
        {
            SelectionPushes = composite.Pushes,
            SelectionConsumers = composite.Consumers,
            SelectionBindings = composite.Bindings,
            DependentQualities = composite.DependentQualities
        };
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
        IReadOnlyList<LifeModuleFollowUpPromptDto> prompts,
        CharacterCreationFoundationSkillSourceAuthority? skillSourceAuthority,
        string lifeModulesSourceDigest)
    {
        string effectKind = ReadEffectKind(effect.RawXml);
        string[] promptIds = prompts
            .Select(prompt => prompt.PromptId)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        bool requiresPrompt = promptIds.Length > 0
                              || HasUnprojectedKnowledgeSkillPrompt(effect);
        CharacterCreationFoundationEffectTargetBinding? targetBinding = null;
        IReadOnlyDictionary<string, string> ignoredSourceMetadata =
            new Dictionary<string, string>(StringComparer.Ordinal);
        bool supported = false;
        if (!requiresPrompt)
        {
            supported = IsSupportedAttributeLevel(effect)
                        || IsSupportedSkillLevel(
                            effect,
                            skillSourceAuthority,
                            out targetBinding,
                            out ignoredSourceMetadata)
                        || IsSupportedKnowledgeSkillLevel(
                            effect,
                            lifeModulesSourceDigest,
                            out targetBinding,
                            out ignoredSourceMetadata);
        }
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
            InstructionDigest: string.Empty)
        {
            TargetBinding = targetBinding,
            IgnoredSourceMetadata = ignoredSourceMetadata
        };
        return instruction with
        {
            InstructionDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeCanonicalDigest(instruction with { InstructionDigest = string.Empty })
        };
    }

    private static CompositeSelectionCompilation CompileCompositeSelections(
        IReadOnlyList<LifeModuleEffectProjectionDto> projectedEffects,
        IReadOnlyList<CharacterCreationFoundationEffectInstruction> compiledEffects,
        CharacterCreationFoundationQualitySourceAuthority? qualitySourceAuthority,
        string lifeModulesSourceDigest)
    {
        CharacterCreationFoundationEffectInstruction[] effects = compiledEffects.ToArray();
        var pushes = new List<CharacterCreationFoundationSelectionPushInstruction>();
        var consumers = new List<CharacterCreationFoundationSelectionConsumerInstruction>();
        var bindings = new List<CharacterCreationFoundationSelectionBinding>();
        var dependentQualities =
            new List<CharacterCreationFoundationDependentQualityInstruction>();
        var stack = new Stack<(int EffectIndex,
            CharacterCreationFoundationSelectionPushInstruction Instruction)>();
        var compositeEffectIndexes = new HashSet<int>();
        bool complete = true;

        for (int index = 0; index < projectedEffects.Count; index++)
        {
            LifeModuleEffectProjectionDto projection = projectedEffects[index];
            CharacterCreationFoundationEffectInstruction effect = effects[index];
            if (string.Equals(effect.EffectKind, "pushtext", StringComparison.Ordinal))
            {
                compositeEffectIndexes.Add(index);
                if (effect.PromptIds.Count > 0
                    || !TryCompileSelectionPush(
                        projection,
                        effect,
                        lifeModulesSourceDigest,
                        out var push))
                {
                    complete = false;
                    continue;
                }

                pushes.Add(push!);
                stack.Push((index, push!));
                continue;
            }

            if (!string.Equals(effect.EffectKind, "addqualities", StringComparison.Ordinal))
                continue;

            compositeEffectIndexes.Add(index);
            if (effect.PromptIds.Count > 0
                || qualitySourceAuthority is null
                || !TryReadAddQualityNames(projection, out string[] qualityNames))
            {
                complete = false;
                continue;
            }

            int consumerCountBefore = consumers.Count;
            for (int addQualityIndex = 0; addQualityIndex < qualityNames.Length;
                 addQualityIndex++)
            {
                string qualityName = qualityNames[addQualityIndex];
                CharacterCreationFoundationEffectTargetBinding? targetBinding;
                XElement? qualitySource;
                string sourceNodeDigest;
                if (!qualitySourceAuthority.TryResolveExact(qualityName, out targetBinding)
                    || targetBinding is null
                    || !qualitySourceAuthority.TryGetDefinition(
                        targetBinding,
                        out qualitySource,
                        out sourceNodeDigest)
                    || qualitySource is null
                    || !TryInspectDependentQuality(
                        qualitySource,
                        out bool hasSelectText,
                        out bool hasRuntimeRequirements,
                        out bool bonusSupported))
                {
                    complete = false;
                    continue;
                }

                string? consumerId = hasSelectText
                    ? $"{effect.EffectId}:addquality:{addQualityIndex + 1}:selecttext"
                    : null;
                bool dependentSupported = !hasRuntimeRequirements && bonusSupported;
                var dependent =
                    new CharacterCreationFoundationDependentQualityInstruction(
                        effect.Order,
                        effect.EffectId,
                        addQualityIndex + 1,
                        lifeModulesSourceDigest,
                        targetBinding,
                        sourceNodeDigest,
                        consumerId,
                        hasRuntimeRequirements,
                        dependentSupported
                            ? CharacterCreationFoundationEffectCompilationStatuses.Supported
                            : CharacterCreationFoundationEffectCompilationStatuses.Unsupported,
                        dependentSupported
                            ? null
                            : CharacterCreationFoundationBlockers.FinalizationEffectUnsupported,
                        string.Empty);
                dependent = dependent with
                {
                    InstructionDigest = CharacterCreationFoundationDraftLedgerIntegrity
                        .ComputeCanonicalDigest(
                            dependent with { InstructionDigest = string.Empty })
                };
                dependentQualities.Add(dependent);
                if (!dependentSupported)
                    complete = false;

                if (!hasSelectText)
                    continue;

                IReadOnlyList<string> sourceAnchors = effect.SourceAnchorIds
                    .Append($"qualities.xml#quality:{targetBinding.SourceId}")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var consumer =
                    new CharacterCreationFoundationSelectionConsumerInstruction(
                        effect.Order,
                        consumerId!,
                        effect.EffectId,
                        addQualityIndex + 1,
                        lifeModulesSourceDigest,
                        targetBinding,
                        sourceNodeDigest,
                        sourceAnchors,
                        string.Empty);
                consumer = consumer with
                {
                    InstructionDigest = CharacterCreationFoundationDraftLedgerIntegrity
                        .ComputeCanonicalDigest(
                            consumer with { InstructionDigest = string.Empty })
                };
                consumers.Add(consumer);
                if (!stack.TryPop(out var pushed))
                {
                    complete = false;
                    continue;
                }

                var binding = new CharacterCreationFoundationSelectionBinding(
                    pushed.Instruction.EffectId,
                    consumer.ConsumerId,
                    pushed.Instruction.Literal,
                    pushed.Instruction.InstructionDigest,
                    consumer.InstructionDigest,
                    string.Empty);
                binding = binding with
                {
                    BindingDigest = CharacterCreationFoundationDraftLedgerIntegrity
                        .ComputeCanonicalDigest(binding with { BindingDigest = string.Empty })
                };
                bindings.Add(binding);
            }

            // This writer family is deliberately paired. An addqualities effect
            // which consumes no pushed value is a different effect family.
            if (consumers.Count == consumerCountBefore)
                complete = false;
        }

        if (stack.Count > 0)
            complete = false;
        if (compositeEffectIndexes.Count > 0
            && effects.Any(effect => string.Equals(
                effect.EffectKind,
                "qualitylevel",
                StringComparison.Ordinal)))
        {
            complete = false;
        }

        if (compositeEffectIndexes.Count > 0)
        {
            foreach (int index in compositeEffectIndexes)
            {
                effects[index] = WithCompilationStatus(
                    effects[index],
                    complete
                        ? CharacterCreationFoundationEffectCompilationStatuses.Supported
                        : CharacterCreationFoundationEffectCompilationStatuses.Unsupported,
                    complete
                        ? null
                        : CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
            }
        }

        return new CompositeSelectionCompilation(
            effects,
            pushes.ToArray(),
            consumers.ToArray(),
            bindings.ToArray(),
            dependentQualities.ToArray());
    }

    private static bool TryCompileSelectionPush(
        LifeModuleEffectProjectionDto projection,
        CharacterCreationFoundationEffectInstruction effect,
        string lifeModulesSourceDigest,
        out CharacterCreationFoundationSelectionPushInstruction? instruction)
    {
        instruction = null;
        if (!projection.IsFullyTyped
            || !string.Equals(projection.Domain, "story", StringComparison.Ordinal)
            || projection.BudgetId is not null
            || projection.BudgetDelta != 0
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                lifeModulesSourceDigest)
            || projection.SourceAnchorIds.Count == 0
            || projection.SourceAnchorIds.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        try
        {
            XElement element = XElement.Parse(projection.RawXml, LoadOptions.None);
            string literal = element.Value;
            if (element.Name.NamespaceName.Length != 0
                || !string.Equals(element.Name.LocalName, "pushtext", StringComparison.Ordinal)
                || element.HasAttributes
                || element.HasElements
                || element.Nodes().Any(node => node is not XText)
                || string.IsNullOrWhiteSpace(literal)
                || !string.Equals(literal, literal.Trim(), StringComparison.Ordinal)
                || ContainsPlaceholderToken(literal)
                || literal.Contains('$')
                || projection.Parameters.Count != 0
                || !string.Equals(projection.TargetId, literal, StringComparison.Ordinal)
                || !string.Equals(projection.AfterValue, literal, StringComparison.Ordinal))
            {
                return false;
            }

            var candidate = new CharacterCreationFoundationSelectionPushInstruction(
                effect.Order,
                effect.EffectId,
                effect.SourcePhase,
                literal,
                lifeModulesSourceDigest,
                effect.SourceAnchorIds.ToArray(),
                string.Empty);
            instruction = candidate with
            {
                InstructionDigest = CharacterCreationFoundationDraftLedgerIntegrity
                    .ComputeCanonicalDigest(candidate with { InstructionDigest = string.Empty })
            };
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static bool TryReadAddQualityNames(
        LifeModuleEffectProjectionDto projection,
        out string[] qualityNames)
    {
        qualityNames = [];
        if (!projection.IsFullyTyped
            || !string.Equals(projection.Domain, "quality", StringComparison.Ordinal)
            || projection.BudgetId is not null
            || projection.BudgetDelta != 0
            || projection.SourceAnchorIds.Count == 0
            || projection.SourceAnchorIds.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        try
        {
            XElement element = XElement.Parse(projection.RawXml, LoadOptions.None);
            XElement[] children = element.Elements().ToArray();
            if (element.Name.NamespaceName.Length != 0
                || !string.Equals(element.Name.LocalName, "addqualities", StringComparison.Ordinal)
                || element.HasAttributes
                || children.Length == 0
                || element.Nodes().Any(node => node is XText text
                    ? !string.IsNullOrWhiteSpace(text.Value)
                    : node is not XElement)
                || children.Any(child => child.Name.NamespaceName.Length != 0
                    || !string.Equals(child.Name.LocalName, "addquality", StringComparison.Ordinal)
                    || child.HasAttributes
                    || child.HasElements
                    || child.Nodes().Any(node => node is not XText)))
            {
                return false;
            }

            qualityNames = children.Select(child => child.Value).ToArray();
            if (qualityNames.Any(name => string.IsNullOrWhiteSpace(name)
                    || !string.Equals(name, name.Trim(), StringComparison.Ordinal)
                    || ContainsPlaceholderToken(name)
                    || name.Contains('$')))
            {
                qualityNames = [];
                return false;
            }

            var expectedParameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["addquality"] = string.Join("|", qualityNames)
            };
            return string.Equals(projection.TargetId, "addqualities", StringComparison.Ordinal)
                   && projection.AfterValue is null
                   && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                       expectedParameters,
                       projection.Parameters);
        }
        catch (System.Xml.XmlException)
        {
            qualityNames = [];
            return false;
        }
    }

    internal static bool TryInspectDependentQuality(
        XElement source,
        out bool hasSelectText,
        out bool hasRuntimeRequirements,
        out bool bonusSupported)
    {
        hasSelectText = false;
        hasRuntimeRequirements = false;
        bonusSupported = false;
        string[] allowedChildren =
        [
            "id", "name", "karma", "category", "limit", "bonus", "forbidden",
            "required", "source", "page", "metagenic", "metagenetic", "altnotes",
            "notes", "notesColor", "doublecareer", "canbuywithspellpoints", "print",
            "implemented", "contributetobp", "contributetolimit", "stagedpurchase",
            "mutant"
        ];
        string[] singletonChildren = allowedChildren;
        string[] scalarChildren =
        [
            "id", "name", "karma", "category", "limit", "source", "page",
            "metagenic", "metagenetic", "altnotes", "notes", "notesColor",
            "doublecareer", "canbuywithspellpoints", "print", "implemented",
            "contributetobp", "contributetolimit", "stagedpurchase"
        ];
        if (source.Name.NamespaceName.Length != 0
            || !string.Equals(source.Name.LocalName, "quality", StringComparison.Ordinal)
            || source.HasAttributes
            || source.Elements().Any(child => child.Name.NamespaceName.Length != 0
                || !allowedChildren.Contains(child.Name.LocalName, StringComparer.Ordinal))
            || singletonChildren.Any(name => source.Elements(name).Take(2).Count() > 1)
            || (source.Elements("metagenic").Any()
                && source.Elements("metagenetic").Any())
            || (source.Elements("altnotes").Any() && source.Elements("notes").Any())
            || scalarChildren.SelectMany(name => source.Elements(name)).Any(element =>
                element.HasAttributes
                || element.HasElements
                || (element.Name.LocalName is not ("altnotes" or "notes")
                    && !string.Equals(
                        element.Value,
                        element.Value.Trim(),
                        StringComparison.Ordinal)))
            || new[]
            {
                "metagenic", "metagenetic", "doublecareer", "canbuywithspellpoints",
                "print", "implemented", "contributetobp", "contributetolimit",
                "stagedpurchase"
            }.Any(name => source.Element(name) is XElement element
                && !bool.TryParse(element.Value, out _))
            || source.Element("mutant") is XElement mutant
                && (mutant.HasAttributes
                    || mutant.HasElements
                    || !string.IsNullOrWhiteSpace(mutant.Value))
            || source.Element("notesColor") is XElement notesColor
                && string.IsNullOrWhiteSpace(notesColor.Value)
            || source.Element("id") is not XElement id
            || source.Element("name") is not XElement name
            || source.Element("karma") is not XElement karma
            || source.Element("category") is not XElement category
            || source.Element("source") is not XElement sourceBook
            || source.Element("page") is not XElement page
            || new[] { id, name, karma, category, sourceBook, page }
                .Any(element => element.HasAttributes || element.HasElements
                    || !string.Equals(element.Value, element.Value.Trim(), StringComparison.Ordinal))
            || !Guid.TryParseExact(id.Value, "D", out Guid parsedId)
            || parsedId == Guid.Empty
            || !int.TryParse(
                karma.Value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out _)
            || category.Value is not ("Positive" or "Negative"))
        {
            return false;
        }

        hasRuntimeRequirements = source.Elements()
            .Where(element => element.Name.LocalName is "required" or "forbidden")
            .Any(element => element.HasElements || !string.IsNullOrWhiteSpace(element.Value));
        XElement? bonus = source.Element("bonus");
        if (bonus is null)
        {
            bonusSupported = true;
            return true;
        }

        if (bonus.HasAttributes
            || bonus.Name.NamespaceName.Length != 0
            || bonus.Nodes().Any(node => node is XText text
                ? !string.IsNullOrWhiteSpace(text.Value)
                : node is not XElement))
        {
            return false;
        }

        XElement[] bonusEffects = bonus.Elements().ToArray();
        XElement[] selectTexts = bonusEffects.Where(effect => string.Equals(
                effect.Name.LocalName,
                "selecttext",
                StringComparison.Ordinal))
            .ToArray();
        if (selectTexts.Length > 1
            || selectTexts.Any(effect => effect.Name.NamespaceName.Length != 0
                || effect.HasAttributes
                || effect.HasElements
                || effect.Nodes().Any(node => node is XText text
                    ? !string.IsNullOrWhiteSpace(text.Value)
                    : true)))
        {
            return false;
        }

        hasSelectText = selectTexts.Length == 1;
        bonusSupported = bonusEffects.All(TryValidateDependentBonusEffect);
        return true;
    }

    internal static bool TryValidateDependentBonusEffect(XElement effect)
    {
        if (effect.Name.NamespaceName.Length != 0)
            return false;

        string kind = effect.Name.LocalName;
        if (string.Equals(kind, "selecttext", StringComparison.Ordinal))
        {
            return !effect.HasAttributes
                   && !effect.HasElements
                   && effect.Nodes().All(node => node is XText text
                       && string.IsNullOrWhiteSpace(text.Value));
        }

        if (kind is "notoriety" or "trustfund" or "damageresistance")
        {
            return TryReadExactScalar(effect, numeric: true, out _);
        }

        if (kind is "blockskillcategorydefaulting" or "skillgroupcategorydisable")
        {
            return TryReadExactScalar(effect, numeric: false, out _);
        }

        if (kind is "skillcategorykarmacostmultiplier"
            or "skillcategoryspecializationkarmacostmultiplier"
            or "skillgroupcategorykarmacostmultiplier"
            or "skillcategorypointcostmultiplier")
        {
            if (effect.HasAttributes
                || effect.Nodes().Any(node => node is XText text
                    ? !string.IsNullOrWhiteSpace(text.Value)
                    : node is not XElement))
            {
                return false;
            }

            XElement[] children = effect.Elements().ToArray();
            XElement[] names = children.Where(child => string.Equals(
                    child.Name.LocalName,
                    "name",
                    StringComparison.Ordinal))
                .ToArray();
            XElement[] values = children.Where(child => string.Equals(
                    child.Name.LocalName,
                    "val",
                    StringComparison.Ordinal))
                .ToArray();
            return children.Length == 2
                   && names.Length == 1
                   && values.Length == 1
                   && children.All(child => child.Name.NamespaceName.Length == 0
                       && !child.HasAttributes
                       && !child.HasElements)
                   && !string.IsNullOrWhiteSpace(names[0].Value)
                   && string.Equals(
                       names[0].Value,
                       names[0].Value.Trim(),
                       StringComparison.Ordinal)
                   && !ContainsPlaceholderToken(names[0].Value)
                   && !names[0].Value.Contains('$')
                   && decimal.TryParse(
                       values[0].Value,
                       System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out _);
        }

        return false;
    }

    internal static bool TryReadExactScalar(
        XElement effect,
        bool numeric,
        out string value)
    {
        value = effect.Value;
        return !effect.HasAttributes
               && !effect.HasElements
               && effect.Nodes().All(node => node is XText)
               && !string.IsNullOrWhiteSpace(value)
               && string.Equals(value, value.Trim(), StringComparison.Ordinal)
               && !ContainsPlaceholderToken(value)
               && !value.Contains('$')
               && (!numeric
                   || decimal.TryParse(
                       value,
                       System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out _));
    }

    private static CharacterCreationFoundationEffectInstruction WithCompilationStatus(
        CharacterCreationFoundationEffectInstruction instruction,
        string status,
        string? blocker)
    {
        CharacterCreationFoundationEffectInstruction updated = instruction with
        {
            CompilationStatus = status,
            Blocker = blocker,
            InstructionDigest = string.Empty
        };
        return updated with
        {
            InstructionDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeCanonicalDigest(updated with { InstructionDigest = string.Empty })
        };
    }

    private static bool HasUnprojectedKnowledgeSkillPrompt(
        LifeModuleEffectProjectionDto effect)
    {
        if (!string.Equals(effect.Domain, "knowledge-skill", StringComparison.Ordinal))
            return false;

        try
        {
            XElement element = XElement.Parse(effect.RawXml, LoadOptions.None);
            return element.Name.NamespaceName.Length == 0
                   && string.Equals(
                       element.Name.LocalName,
                       "knowledgeskilllevel",
                       StringComparison.Ordinal)
                   && (element.Elements("selectskill").Any()
                       || element.Descendants()
                           .Where(descendant => !descendant.HasElements)
                           .Any(descendant => ContainsPlaceholderToken(descendant.Value)));
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Chummer5's knowledgeskilllevel handler does not resolve the legacy Life
    /// Module name/group/spec fields. Without a direct selectskill child it
    /// writes one FreeKnowledgeSkills pool Improvement whose only operative
    /// field is val. Keep the ignored literals reviewable; never manufacture a
    /// knowledge-skill identity from them.
    /// </summary>
    private static bool IsSupportedKnowledgeSkillLevel(
        LifeModuleEffectProjectionDto effect,
        string lifeModulesSourceDigest,
        out CharacterCreationFoundationEffectTargetBinding? targetBinding,
        out IReadOnlyDictionary<string, string> ignoredSourceMetadata)
    {
        targetBinding = null;
        ignoredSourceMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                lifeModulesSourceDigest)
            || !effect.IsFullyTyped
            || !string.Equals(effect.Domain, "knowledge-skill", StringComparison.Ordinal)
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
                    "knowledgeskilllevel",
                    StringComparison.Ordinal)
                || element.HasAttributes)
            {
                return false;
            }

            XElement[] children = element.Elements().ToArray();
            XElement[] names = children.Where(child => string.Equals(
                    child.Name.LocalName,
                    "name",
                    StringComparison.Ordinal))
                .ToArray();
            XElement[] groups = children.Where(child => string.Equals(
                    child.Name.LocalName,
                    "group",
                    StringComparison.Ordinal))
                .ToArray();
            XElement[] values = children.Where(child => string.Equals(
                    child.Name.LocalName,
                    "val",
                    StringComparison.Ordinal))
                .ToArray();
            XElement[] specializations = children.Where(child => string.Equals(
                    child.Name.LocalName,
                    "spec",
                    StringComparison.Ordinal))
                .ToArray();
            if (children.Any(child => child.Name.NamespaceName.Length != 0)
                || children.Length
                != names.Length + groups.Length + values.Length + specializations.Length
                || names.Length != 1
                || groups.Length > 1
                || values.Length > 1
                || specializations.Length > 1
                || element.Nodes().Any(node => node is XText text
                    ? !string.IsNullOrWhiteSpace(text.Value)
                    : node is not XElement)
                || children.Any(child => child.HasElements || child.HasAttributes))
            {
                return false;
            }

            string literalName = names[0].Value;
            string? literalGroup = groups.Length == 0 ? null : groups[0].Value;
            string? rawValue = values.Length == 0 ? null : values[0].Value;
            string? literalSpecialization = specializations.Length == 0
                ? null
                : specializations[0].Value;
            string? projectedValue = rawValue?.Trim();
            if (string.IsNullOrWhiteSpace(literalName)
                || !string.Equals(literalName, literalName.Trim(), StringComparison.Ordinal)
                || ContainsPlaceholderToken(literalName)
                || (literalGroup is not null
                    && (string.IsNullOrWhiteSpace(literalGroup)
                        || !string.Equals(
                            literalGroup,
                            literalGroup.Trim(),
                            StringComparison.Ordinal)
                        || ContainsPlaceholderToken(literalGroup)))
                || (literalSpecialization is not null
                    && (string.IsNullOrWhiteSpace(literalSpecialization)
                        || !string.Equals(
                            literalSpecialization,
                            literalSpecialization.Trim(),
                            StringComparison.Ordinal)
                        || ContainsPlaceholderToken(literalSpecialization)))
                || (rawValue is not null
                    && (string.IsNullOrWhiteSpace(rawValue)
                        || !decimal.TryParse(
                            rawValue,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out _))))
            {
                return false;
            }

            var expectedParameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = literalName
            };
            if (literalGroup is not null)
                expectedParameters["group"] = literalGroup;
            if (projectedValue is not null)
                expectedParameters["val"] = projectedValue;
            if (literalSpecialization is not null)
                expectedParameters["spec"] = literalSpecialization;
            if (!string.Equals(effect.TargetId, literalName, StringComparison.Ordinal)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    expectedParameters,
                    effect.Parameters)
                || (rawValue is null
                    ? effect.AfterValue is not null
                    : !string.Equals(
                        effect.AfterValue,
                        projectedValue,
                        StringComparison.Ordinal)))
            {
                return false;
            }

            targetBinding = new CharacterCreationFoundationEffectTargetBinding(
                TargetKind: "free-knowledge-skill-pool",
                SourceId: FreeKnowledgeSkillsIdentity,
                CanonicalName: FreeKnowledgeSkillsIdentity,
                SourceDigest: lifeModulesSourceDigest);
            var ignored = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["legacy-ignored-literal-name"] = literalName
            };
            if (literalGroup is not null)
                ignored["legacy-ignored-literal-group"] = literalGroup;
            if (literalSpecialization is not null)
            {
                ignored["legacy-ignored-literal-spec"] = literalSpecialization;
            }

            ignoredSourceMetadata = ignored;
            return true;
        }
        catch (System.Xml.XmlException)
        {
            targetBinding = null;
            ignoredSourceMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
            return false;
        }
    }

    private static bool IsSupportedSkillLevel(
        LifeModuleEffectProjectionDto effect,
        CharacterCreationFoundationSkillSourceAuthority? skillSourceAuthority,
        out CharacterCreationFoundationEffectTargetBinding? targetBinding,
        out IReadOnlyDictionary<string, string> ignoredSourceMetadata)
    {
        targetBinding = null;
        ignoredSourceMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (skillSourceAuthority is null
            || !effect.IsFullyTyped
            || !string.Equals(effect.Domain, "active-skill", StringComparison.Ordinal)
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
                || !string.Equals(element.Name.LocalName, "skilllevel", StringComparison.Ordinal)
                || element.HasAttributes)
            {
                return false;
            }

            XElement[] children = element.Elements().ToArray();
            XElement[] names = children.Where(child => string.Equals(
                    child.Name.LocalName,
                    "name",
                    StringComparison.Ordinal))
                .ToArray();
            XElement[] values = children.Where(child => string.Equals(
                    child.Name.LocalName,
                    "val",
                    StringComparison.Ordinal))
                .ToArray();
            XElement[] specializations = children.Where(child => string.Equals(
                    child.Name.LocalName,
                    "spec",
                    StringComparison.Ordinal))
                .ToArray();
            if (children.Any(child => child.Name.NamespaceName.Length != 0)
                || children.Length != names.Length + values.Length + specializations.Length
                || names.Length != 1
                || values.Length > 1
                || specializations.Length > 1
                || element.Nodes().Any(node => node is XText text
                    ? !string.IsNullOrWhiteSpace(text.Value)
                    : node is not XElement)
                || children.Any(child => child.HasElements || child.HasAttributes))
            {
                return false;
            }

            string canonicalName = names[0].Value;
            string? rawValue = values.Length == 0 ? null : values[0].Value;
            string? literalSpecialization = specializations.Length == 0
                ? null
                : specializations[0].Value;
            if (string.IsNullOrWhiteSpace(canonicalName)
                || !string.Equals(canonicalName, canonicalName.Trim(), StringComparison.Ordinal)
                || (rawValue is not null
                    && !string.Equals(rawValue, rawValue.Trim(), StringComparison.Ordinal))
                || (literalSpecialization is not null
                    && (string.IsNullOrWhiteSpace(literalSpecialization)
                        || !string.Equals(
                            literalSpecialization,
                            literalSpecialization.Trim(),
                            StringComparison.Ordinal))))
            {
                return false;
            }

            var expectedParameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = canonicalName
            };
            if (rawValue is not null)
                expectedParameters["val"] = rawValue;
            if (literalSpecialization is not null)
                expectedParameters["spec"] = literalSpecialization;
            if (!string.Equals(effect.TargetId, canonicalName, StringComparison.Ordinal)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    expectedParameters,
                    effect.Parameters)
                || (rawValue is null
                    ? effect.AfterValue is not null
                    : !string.Equals(effect.AfterValue, rawValue, StringComparison.Ordinal))
                || !skillSourceAuthority.TryResolveExactActive(
                    canonicalName,
                    out targetBinding))
            {
                targetBinding = null;
                return false;
            }

            if (literalSpecialization is not null)
            {
                ignoredSourceMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["legacy-ignored-literal-spec"] = literalSpecialization
                };
            }

            return true;
        }
        catch (System.Xml.XmlException)
        {
            targetBinding = null;
            ignoredSourceMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
            return false;
        }
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
        return ParseLegacyLevelValue(rawValue);
    }

    internal static int ParseLegacySkillLevelValue(string? rawValue)
    {
        return ParseLegacyLevelValue(rawValue);
    }

    internal static decimal ParseLegacyKnowledgeSkillLevelValue(string? rawValue)
    {
        if (rawValue is null)
            return 1m;

        return decimal.TryParse(
            rawValue,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out decimal value)
            ? value
            : 0m;
    }

    private static int ParseLegacyLevelValue(string? rawValue)
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

    private static bool ContainsPlaceholderToken(string value)
    {
        int open = value.IndexOf("[", StringComparison.Ordinal);
        return open >= 0
               && value.IndexOf("]", open + 1, StringComparison.Ordinal) > open;
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

    private sealed record CompositeSelectionCompilation(
        CharacterCreationFoundationEffectInstruction[] Effects,
        CharacterCreationFoundationSelectionPushInstruction[] Pushes,
        CharacterCreationFoundationSelectionConsumerInstruction[] Consumers,
        CharacterCreationFoundationSelectionBinding[] Bindings,
        CharacterCreationFoundationDependentQualityInstruction[] DependentQualities);
}
