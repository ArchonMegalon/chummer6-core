using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Explain;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Explain;

/// <summary>
/// Read-only, owner/workspace-bound projection of the first Core rule question.
/// This is a preparation seam, not an artifact-signing or publication authority.
/// </summary>
public sealed class WorkspaceRuleQuestionService(
    IOwnerContextAccessor owners,
    IWorkspaceStore store,
    IRulesetWorkspaceCodecResolver codecs,
    ICharacterSourceDataResolver sources) : IWorkspaceRuleQuestionService
{
    private const int MaximumCharacterXmlLength = 4 * 1024 * 1024;

    public WorkspaceRuleQuestionResult Resolve(
        OwnerContextStamp expectedOwner,
        WorkspaceRuleQuestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        IOwnerContextLease? lease = null;
        try
        {
            if (!IsRequestAdmissible(request)
                || !expectedOwner.IsValid
                || owners is not IOwnerContextLeaseAccessor authority)
            {
                return Unresolved("workspace_rule_question.unavailable");
            }
            if (!authority.TryAcquire(expectedOwner, out lease) || lease is null)
            {
                return Unresolved("workspace_rule_question.unavailable");
            }
            if (lease.Stamp != expectedOwner)
            {
                return Unresolved("workspace_rule_question.unavailable");
            }

                WorkspaceStoreReadResult firstRead = Read(expectedOwner.Owner, request.WorkspaceId);
                if (!firstRead.Success || firstRead.Value is not WorkspaceStoredDocument first)
                    return Unresolved("workspace_rule_question.workspace_unavailable");

                string initialXml = first.Document.Content;
                string initialDocumentDigest = CharacterCreationBootstrapActivationIntegrity
                    .ComputeDocumentDigest(first.Document);
                if (first.ContentRevision != request.ExpectedContentRevision
                    || first.Id != request.WorkspaceId
                    || first.SavedRevision < 0
                    || first.SavedRevision > first.ContentRevision
                    || !TryReadCharacter(initialXml, out _, out _, out XElement[] savedQualities))
                {
                    return Unresolved("workspace_rule_question.workspace_unavailable");
                }

                IRulesetWorkspaceCodec codec = codecs.Resolve(request.RulesetId);
                if (first.Document.Format != WorkspaceDocumentFormat.NativeXml
                    || !string.Equals(first.Document.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal)
                    || !string.Equals(codec.RulesetId, first.Document.RulesetId, StringComparison.Ordinal)
                    || codec.SchemaVersion != first.Document.SchemaVersion
                    || !string.Equals(codec.PayloadKind, first.Document.PayloadKind, StringComparison.Ordinal)
                    || !codec.Validate(first.Document.PayloadEnvelope).IsValid)
                {
                    return Unresolved("workspace_rule_question.workspace_unavailable");
                }

                if (sources is not ICharacterSourceDataResolverOperationScopeFactory sourceFactory)
                    return Unresolved("workspace_rule_question.source_unavailable");
                using ICharacterSourceDataResolverOperationScope sourceOperation =
                    sourceFactory.CreateOperationScope();
                ICharacterSourceDataContext? sourceContext = sourceOperation.TryCreateContext(initialXml);
                if (sourceContext is null
                    || !sourceContext.TryResolveCreationSourceProfile(
                        out CharacterCreationSourceProfileAuthority profile)
                    || !ValidProfile(profile))
                {
                    return Unresolved("workspace_rule_question.source_unavailable");
                }

                XElement? selected = savedQualities.SingleOrDefault(item =>
                    string.Equals(Scalar(item, "guid"), request.SubjectId, StringComparison.OrdinalIgnoreCase));
                if (selected is null
                    || !TryReadQualityIdentity(selected, out string sourceId, out string qualityName)
                    || !savedQualities
                        .Where(item => string.Equals(Scalar(item, "sourceid"), sourceId,
                            StringComparison.OrdinalIgnoreCase)
                            && string.Equals(Scalar(item, "extra"), Scalar(selected, "extra"),
                                StringComparison.Ordinal)
                            && string.Equals(Scalar(item, "sourcename"), Scalar(selected, "sourcename"),
                                StringComparison.Ordinal)
                            && string.Equals(Scalar(item, "qualitytype"), Scalar(selected, "qualitytype"),
                                StringComparison.Ordinal))
                        .All(ValidLevelShape)
                    || !sourceContext.TryResolveQualityLevelSource(
                        sourceId, qualityName, out CharacterQualityLevelSource source)
                    || !source.SourceCitationResolved
                    || !CharacterCreationQualitiesRules.IsCanonicalDigest(source.SourceNodeDigest)
                    || source.SourcePage is not > 0
                    || !profile.EnabledSourcebooks.Contains(source.SourceBook, StringComparer.OrdinalIgnoreCase)
                    || !Guid.TryParseExact(sourceId, "D", out _))
                {
                    return Unresolved("workspace_rule_question.source_unavailable");
                }

                CharacterQualitiesSection qualities = new CharacterSectionService(sourceOperation)
                    .ParseQualities(initialXml);
                CharacterQualitySummary[] matches = qualities.Qualities
                    .Where(item => string.Equals(item.Guid, request.SubjectId,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (matches.Length != 1
                    || matches[0].LevelSemantics is not CharacterQualityLevelSemantics semantics
                    || semantics.AnchorQualityId == Guid.Empty
                    || semantics.AnchorQualityId != Guid.ParseExact(request.SubjectId, "D")
                    || semantics.Level <= 0
                    || semantics.MaximumLevel < semantics.Level)
                {
                    return Unresolved("workspace_rule_question.quality_unavailable");
                }

                // Re-admit the same original XML and operation after calculation. A fresh
                // operation here would hide source changes made during this read.
                ICharacterSourceDataContext? finalSourceContext =
                    sourceOperation.TryCreateContext(initialXml);
                if (finalSourceContext is null
                    || !finalSourceContext.TryResolveCreationSourceProfile(
                        out CharacterCreationSourceProfileAuthority finalProfile)
                    || !ValidProfile(finalProfile)
                    || !finalSourceContext.TryResolveQualityLevelSource(
                        sourceId, qualityName, out CharacterQualityLevelSource finalSource)
                    || !SameProfile(profile, finalProfile)
                    || !SameSource(source, finalSource))
                {
                    return Unresolved("workspace_rule_question.source_unavailable");
                }

                WorkspaceStoreReadResult finalRead = Read(expectedOwner.Owner, request.WorkspaceId);
                if (!finalRead.Success || finalRead.Value is not WorkspaceStoredDocument current
                    || !SameDocument(first, current, initialDocumentDigest))
                {
                    return Unresolved("workspace_rule_question.workspace_changed");
                }

                IReadOnlyList<WorkspaceRuleExecutingModule> modules = Modules(codec, store, owners, sources);
                string anchorId = $"workspace-rule-question:quality:{request.SubjectId}";
                var anchor = new WorkspaceRuleSourceAnchor(
                    AnchorId: anchorId,
                    RulesetId: RulesetDefaults.Sr5,
                    SourceBook: source.SourceBook,
                    Page: source.SourcePage!.Value,
                    QualitySourceId: sourceId,
                    SourceNodeDigest: source.SourceNodeDigest,
                    SettingsProfileId: profile.SettingsProfileId,
                    SourceProfileDigest: profile.RawProfileInputsDigest,
                    CalculationTrace: [
                        $"workspace_rule_question.trace.saved_quality_group:{semantics.Level}/{semantics.MaximumLevel}",
                        $"workspace_rule_question.trace.active_source_row:{source.SourceBook}/{source.SourcePage.Value}"]);
                var binding = new WorkspaceRuleQuestionBinding(
                    Schema: WorkspaceRuleQuestionSchemas.BindingV1,
                    OwnerId: expectedOwner.Owner.Value,
                    TrustedLocalOwner: expectedOwner.Owner.IsLocalSingleUser,
                    OwnerAuthorityInstanceId: expectedOwner.AuthorityInstanceId,
                    OwnerTransitionRevision: expectedOwner.TransitionRevision,
                    WorkspaceId: request.WorkspaceId,
                    RulesetId: RulesetDefaults.Sr5,
                    ContentRevision: first.ContentRevision,
                    SavedRevision: first.SavedRevision,
                    WorkspaceDocumentDigest: initialDocumentDigest,
                    SettingsProfileId: profile.SettingsProfileId,
                    SourceProfileDigest: profile.RawProfileInputsDigest,
                    SourceNodeDigest: source.SourceNodeDigest,
                    EngineIdentityKind: WorkspaceRuleQuestionSchemas.ExecutingModulesV1,
                    EngineFingerprint: WorkspaceRuleQuestionIntegrity.ComputeEngineFingerprint(modules),
                    ExecutingModules: modules,
                    Intent: request.Intent,
                    SubjectId: request.SubjectId,
                    Locale: request.Locale);
                var result = new WorkspaceRuleQuestionResult(
                    Schema: WorkspaceRuleQuestionSchemas.ResultV1,
                    Status: WorkspaceRuleQuestionStatuses.Resolved,
                    Explanation: new BuildGhostRuleExplanation(
                        Schema: BuildGhostContractVersions.RuleExplanationV1,
                        ExplanationId: anchorId,
                        RuleId: WorkspaceRuleQuestionIntents.QualityLevelRuleId,
                        Question: Localized(request.Locale, "question", semantics.Level, semantics.MaximumLevel),
                        Status: WorkspaceRuleQuestionStatuses.Resolved,
                        Explanation: Localized(request.Locale, "resolved", semantics.Level, semantics.MaximumLevel),
                        SourceAnchorIds: [anchorId],
                        UncertaintyReason: null,
                        SourceLookupRoute: null),
                    SourceAnchors: [anchor],
                    Binding: binding,
                    Level: semantics.Level,
                    MaximumLevel: semantics.MaximumLevel,
                    ResultDigest: string.Empty,
                    FailureReason: null);
                return result with { ResultDigest = WorkspaceRuleQuestionIntegrity.ComputeResultDigest(result) };
        }
        catch
        {
            return Unresolved("workspace_rule_question.unavailable");
        }
        finally
        {
            lease?.Dispose();
        }
    }

    private WorkspaceStoreReadResult Read(OwnerScope owner, CharacterWorkspaceId id)
    {
        // FileWorkspaceStore rejects the reserved local-single-user value through its
        // scoped overload. IsLocalSingleUser is the trusted bit carried by the admitted
        // OwnerScope; a matching owner name without that bit must remain scoped and fail.
        return owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner, id);
    }

    private static bool IsRequestAdmissible(WorkspaceRuleQuestionRequest request)
        => string.Equals(request.Schema, WorkspaceRuleQuestionSchemas.RequestV1, StringComparison.Ordinal)
            && string.Equals(request.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal)
            && string.Equals(request.Intent, WorkspaceRuleQuestionIntents.QualityLevel, StringComparison.Ordinal)
            && request.ExpectedContentRevision > 0
            && request.WorkspaceId.Value is { Length: > 0 and <= 128 }
            && !string.IsNullOrWhiteSpace(request.WorkspaceId.Value)
            && Guid.TryParseExact(request.SubjectId, "D", out Guid subject)
            && subject != Guid.Empty
            && TryLocale(request.Locale, out _);

    private static bool TryLocale(string value, out string language)
    {
        language = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 11
            || value.StartsWith('-') || value.EndsWith('-') || value.Contains("--"))
            return false;
        string[] parts = value.Split('-');
        if (parts.Length is < 1 or > 2 || parts[0].Length != 2
            || parts[0].Any(character => !char.IsLetter(character))
            || parts.Skip(1).Any(part => part.Length is < 2 or > 8
                || part.Any(character => !char.IsLetterOrDigit(character))))
            return false;
        language = parts[0].ToLowerInvariant();
        return language is "de" or "en" or "es";
    }

    private static bool ValidProfile(CharacterCreationSourceProfileAuthority profile)
        => !string.IsNullOrWhiteSpace(profile.SettingsProfileId)
            && CharacterCreationQualitiesRules.IsCanonicalDigest(profile.RawProfileInputsDigest);

    private static bool SameProfile(
        CharacterCreationSourceProfileAuthority left,
        CharacterCreationSourceProfileAuthority right)
        => string.Equals(left.SettingsProfileId, right.SettingsProfileId, StringComparison.Ordinal)
            && string.Equals(left.RawProfileInputsDigest, right.RawProfileInputsDigest, StringComparison.Ordinal)
            && left.EnabledSourcebooks.SequenceEqual(right.EnabledSourcebooks, StringComparer.Ordinal)
            && string.Equals(left.BuildMethod, right.BuildMethod, StringComparison.Ordinal)
            && left.BuildPoints == right.BuildPoints
            && left.LifeModuleBudgetIsExact == right.LifeModuleBudgetIsExact
            && left.BudgetBlockers.SequenceEqual(right.BudgetBlockers, StringComparer.Ordinal);

    private static bool SameSource(CharacterQualityLevelSource left, CharacterQualityLevelSource right)
        => string.Equals(left.SourceId, right.SourceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.QualityType, right.QualityType, StringComparison.Ordinal)
            && left.MaximumLevel == right.MaximumLevel
            && left.NoLevels == right.NoLevels
            && left.UsesUnsupportedSemantics == right.UsesUnsupportedSemantics
            && string.Equals(left.SourceBook, right.SourceBook, StringComparison.Ordinal)
            && left.SourcePage == right.SourcePage
            && string.Equals(left.SourceNodeDigest, right.SourceNodeDigest, StringComparison.Ordinal);

    private static bool SameDocument(
        WorkspaceStoredDocument first,
        WorkspaceStoredDocument current,
        string initialDigest)
        => first.Id == current.Id
            && first.ContentRevision == current.ContentRevision
            && first.SavedRevision == current.SavedRevision
            && first.Document.Format == current.Document.Format
            && string.Equals(first.Document.Content, current.Document.Content, StringComparison.Ordinal)
            && string.Equals(first.Document.RulesetId, current.Document.RulesetId, StringComparison.Ordinal)
            && first.Document.SchemaVersion == current.Document.SchemaVersion
            && string.Equals(first.Document.PayloadKind, current.Document.PayloadKind, StringComparison.Ordinal)
            && string.Equals(initialDigest,
                CharacterCreationBootstrapActivationIntegrity.ComputeDocumentDigest(current.Document),
                StringComparison.Ordinal);

    private static IReadOnlyList<WorkspaceRuleExecutingModule> Modules(
        IRulesetWorkspaceCodec codec,
        IWorkspaceStore store,
        IOwnerContextAccessor owners,
        ICharacterSourceDataResolver sources)
    {
        (string Role, Type Type)[] types =
        [
            ("service", typeof(WorkspaceRuleQuestionService)),
            ("section", typeof(CharacterSectionService)),
            ("rules", typeof(CharacterCreationQualitiesRules)),
            ("codec", codec.GetType()),
            ("source", sources.GetType()),
            ("store", store.GetType()),
            ("owner", owners.GetType())
        ];
        return types
            .Select(item => new WorkspaceRuleExecutingModule(
                item.Role,
                item.Type.FullName ?? item.Type.Name,
                item.Type.Assembly.GetName().Name ?? string.Empty,
                item.Type.Module.ModuleVersionId))
            .OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => item.ImplementationType, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Localized(string locale, string kind, int level, int maximumLevel)
    {
        TryLocale(locale, out string language);
        if (kind == "question")
            return language switch
            {
                "de" => "Welche Stufe der gespeicherten Qualität ist durch die aktiven Regeln belegt?",
                "es" => "¿Qué nivel de la cualidad guardada prueban las reglas activas?",
                _ => "Which level of the saved quality is proven by the active rules?"
            };
        return language switch
        {
            "de" => $"Aktive Regeln lösen Stufe {level} von {maximumLevel} auf.",
            "es" => $"Las reglas activas resuelven el nivel {level} de {maximumLevel}.",
            _ => $"Active rules resolve level {level} of {maximumLevel}."
        };
    }

    private static bool TryReadCharacter(
        string xml,
        out XDocument document,
        out XElement root,
        out XElement[] qualities)
    {
        document = null!;
        root = null!;
        qualities = [];
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaximumCharacterXmlLength)
            return false;
        try
        {
            using StringReader input = new(xml);
            using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumCharacterXmlLength,
                MaxCharactersFromEntities = 0
            });
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            root = document.Root!;
            if (root is null || root.Name.Namespace != XNamespace.None
                || root.Name.LocalName != "character"
                || !Scalar(root, "settings", out string settings)
                || string.IsNullOrWhiteSpace(settings)
                || !Scalar(root, "created", out string created)
                || !bool.TryParse(created, out _))
                return false;
            XElement[] containers = root.Elements("qualities").ToArray();
            if (containers.Length != 1 || containers[0].HasAttributes
                || containers[0].Elements().Any(item => item.Name != "quality")
                || containers[0].Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
                return false;
            qualities = containers[0].Elements("quality").ToArray();
            return qualities.Length > 0
                && qualities.Select(item => Scalar(item, "guid"))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() == qualities.Length
                && qualities.All(ValidQualityShape);
        }
        catch
        {
            document = null!;
            root = null!;
            qualities = [];
            return false;
        }
    }

    private static bool ValidQualityShape(XElement quality)
    {
        if (quality.HasAttributes)
            return false;
        string[] required = ["guid", "sourceid", "name", "qualitytype", "qualitysource",
            "bp", "extra", "sourcename"];
        foreach (string name in required)
        {
            if (!Scalar(quality, name, out string value)
                || (name is "guid" or "sourceid" or "name"
                    && string.IsNullOrWhiteSpace(value)))
                return false;
        }
        if (!Guid.TryParseExact(Scalar(quality, "guid"), "D", out Guid guid) || guid == Guid.Empty
            || !Guid.TryParseExact(Scalar(quality, "sourceid"), "D", out Guid source) || source == Guid.Empty
            || Scalar(quality, "qualitytype") is not ("Positive" or "Negative")
            || !int.TryParse(Scalar(quality, "bp"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int bp)
            || Scalar(quality, "qualitysource").Length == 0)
            return false;
        return true;
    }

    private static bool ValidLevelShape(XElement quality)
    {
        foreach (string name in new[] { "notes", "weaponguid", "bonus", "firstlevelbonus", "naturalweapons" })
            if (!OptionalScalar(quality, name))
                return false;
        return true;
    }

    private static bool TryReadQualityIdentity(XElement quality, out string sourceId, out string name)
    {
        sourceId = Scalar(quality, "sourceid");
        name = Scalar(quality, "name");
        return Guid.TryParseExact(sourceId, "D", out Guid source) && source != Guid.Empty
            && !string.IsNullOrWhiteSpace(name)
            && Scalar(quality, "qualitysource") == "Selected"
            && int.TryParse(Scalar(quality, "bp"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int bp)
            && bp == 0
            && Scalar(quality, "qualitytype") is "Positive" or "Negative";
    }

    private static string Scalar(XElement parent, string name)
        => Scalar(parent, name, out string value) ? value : string.Empty;

    private static bool Scalar(XElement parent, string name, out string value)
    {
        XElement[] elements = parent.Elements(name).ToArray();
        value = elements.Length == 1 ? elements[0].Value.Trim() : string.Empty;
        return elements.Length == 1 && !elements[0].HasAttributes && !elements[0].HasElements;
    }

    private static bool OptionalScalar(XElement parent, string name)
    {
        XElement[] elements = parent.Elements(name).ToArray();
        return elements.Length <= 1
            && elements.All(item => !item.HasAttributes && !item.HasElements);
    }

    private static WorkspaceRuleQuestionResult Unresolved(string reason)
    {
        var result = new WorkspaceRuleQuestionResult(
            Schema: WorkspaceRuleQuestionSchemas.ResultV1,
            Status: WorkspaceRuleQuestionStatuses.Unresolved,
            Explanation: new BuildGhostRuleExplanation(
                Schema: BuildGhostContractVersions.RuleExplanationV1,
                ExplanationId: "workspace-rule-question:unresolved",
                RuleId: WorkspaceRuleQuestionIntents.QualityLevelRuleId,
                Question: "Which quality level is supported by the current rules?",
                Status: "bounded-uncertainty",
                Explanation: "This rule could not be resolved from the current Chummer context.",
                SourceAnchorIds: [],
                UncertaintyReason: reason,
                SourceLookupRoute: null),
            SourceAnchors: [],
            Binding: null,
            Level: null,
            MaximumLevel: null,
            ResultDigest: string.Empty,
            FailureReason: reason);
        return result with { ResultDigest = WorkspaceRuleQuestionIntegrity.ComputeResultDigest(result) };
    }
}
