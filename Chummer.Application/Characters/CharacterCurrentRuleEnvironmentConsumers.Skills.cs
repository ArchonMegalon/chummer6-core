using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Exact retained source data only; no aggregate or mutation authority.</summary>
public interface ICharacterCurrentSkillEffectSource
{
    bool TryReadCurrentSkillEffectSource(string kind, Guid sourceId, out string sourceXml, out string sourceState);
}

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    internal static bool IsCurrentSkillProfileSetting1951(string parent, string name)
        => parent == "karmacost"
            ? name is "karmanewactiveskill" or "karmaimproveactiveskill" or "karmanewknowledgeskill"
                or "karmaimproveknowledgeskill" or "karmanewskillgroup" or "karmaimproveskillgroup"
                or "karmaspecialization" or "karmaknospecialization"
            : parent == "setting" && name is "maxskillrating" or "maxskillratingcreate"
                or "maxknowledgeskillrating" or "maxknowledgeskillratingcreate" or "knowledgepointsexpression"
                or "usepointsonbrokengroups" or "breakskillgroupsincreatemode" or "specializationsbreakskillgroups"
                or "allowskillregrouping" or "allowpointbuyspecializationsonkarmaskills" or "compensateskillgroupkarmadifference";

    internal static bool TryObserveCurrentSkillProfileSettings1951(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement profile, out string canonicalPolicyDigest)
    {
        canonicalPolicyDigest = string.Empty;
        if (!current.IsUnchanged || profile.Name != "setting" || profile.HasAttributes
            || !source.TryResolveCareerSettingsProfile(out var settingsXml, out var settingsState)
            || string.IsNullOrWhiteSpace(settingsState)) return false;
        var settings = ParseSkillsXml1949(settingsXml);
        if (!XNode.DeepEquals(settings, new XElement("settings", profile.Nodes()))) return false;
        foreach (var parent in profile.DescendantsAndSelf())
        {
            var owned = parent.Elements().Where(field => IsCurrentSkillProfileSetting1951(parent.Name.LocalName, field.Name.LocalName)).ToArray();
            if (owned.Any(field => field.Name.Namespace != XNamespace.None || field.HasAttributes || field.HasElements)
                || owned.GroupBy(field => field.Name).Any(group => group.Count() != 1)) return false;
        }
        CharacterCreationKarmaSkillsPolicy? policy;
        string method = SkillScalar1949(ParseSkillsXml1949(current.CharacterXml), "buildmethod");
        bool resolved;
        if (method is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
            resolved = source.TryResolveCreationPrioritySkillKarmaPolicy(out policy);
        else if (method == CharacterCreationBuildMethods.Karma)
            resolved = source.TryResolveCreationKarmaSkillsPolicy(out policy);
        else if (method == CharacterCreationBuildMethods.LifeModules)
            resolved = source.TryResolveCreationLifeModuleSkillsPolicy(out policy);
        else return false;
        if (!resolved || policy is null || !MatchesSettingsRow5691(source, profile, policy.SettingsProfileId)
            || policy.AuthorityDigest != CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy)
            || policy.KnowledgePointsExpression != CharacterCreationStandardPrioritySkillsRules.KnowledgePointsExpression
            || !ReadCurrentCareerSkillMaximum(current, source, settings, "maxskillrating", out int activeMaximum) || activeMaximum > 1000
            || !ReadCurrentCareerSkillMaximum(current, source, settings, "maxknowledgeskillrating", out int knowledgeMaximum) || knowledgeMaximum > 1000
            || !CharacterCreationSkillGroupPolicyAuthority.TryResolve(settings, out bool broken, out bool strict, out bool specializationBreaks)
            || broken != policy.UsePointsOnBrokenGroups || strict != policy.StrictSkillGroupsInCreateMode
            || specializationBreaks != policy.SpecializationsBreakSkillGroups) return false;
        // Policy-only numeric evaluations use the existing pure kernels. These
        // are not current attributes, selected skills, fabricated groups, owners,
        // budgets or permission to purchase. Selected-state execution remains in
        // the Skills consumer and recompiles its retained effects independently.
        if (!CharacterCreationSkillCostRules.TryActive(0, 2, policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill, out int activeCost)
            || !CharacterCreationSkillCostRules.TryKnowledge(0, 2, policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill, out int knowledgeCost)
            || !CharacterCreationSkillCostRules.TryGroup(0, 2, policy.KarmaNewSkillGroup, policy.KarmaImproveSkillGroup, out int groupCost)
            || !CharacterCreationKarmaSkillsRules.TryActiveInterval(0, 2, policy, policy.CompensateSkillGroupKarmaDifference,
                1, 3, out int compensatedCost, out int compensationFloor)
            || !CharacterCreationKarmaSkillsRules.TryKnowledgePoints(policy.KnowledgePointsExpression,
                new Dictionary<string, int>(StringComparer.Ordinal) { ["INT"] = 1, ["LOG"] = 2 }, out int expressionValue)) return false;
        if (!current.IsUnchanged) return false;
        canonicalPolicyDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.current-skill-profile-policy.v1", current.BindingDigest,
            Settings = settingsXml, SettingsState = settingsState, Policy = policy,
            ActiveMaximum = activeMaximum, KnowledgeMaximum = knowledgeMaximum,
            GroupPolicy = new { broken, strict, specializationBreaks },
            PolicyKernelResults = new { activeCost, knowledgeCost, groupCost, compensatedCost, compensationFloor, expressionValue }
        });
        return true;
    }

    private const string SkillsGrammar1949 = "chummer.current-skills.catalog-and-current.v3-source-effects";
    private sealed record SkillsRow1949(string Key, string Xml, CharacterCreationSkillCatalogEntry Option,
        CharacterCareerSkillSpecializationSource Specializations);
    private sealed record SkillsCatalog1949(CharacterCurrentRuleEnvironmentContext Current, ICharacterSourceDataContext RetainedSource, string Frozen,
        CharacterCreationSkillsAuthority Authority, CharacterCreationKarmaSkillsPolicy Policy,
        string SettingsXml, string SettingsState, int ActiveMaximum, int KnowledgeMaximum,
        IReadOnlyList<SkillsRow1949> Rows, IReadOnlyDictionary<string, string> Unavailable,
        SelectedSkillEffects1950 SelectedEffects)
    {
        public PrioritySkillMovement1990? PriorityMovement { get; init; }
    }
    private sealed record PrioritySkillMovement1990(CharacterCreationMetatypeMovementProjection Movement, string Digest);
    private static readonly ConditionalWeakTable<CharacterCurrentRuleCatalogValidation, SkillsCatalog1949>
        SkillsCatalogs1949 = new();

    public static CharacterCurrentRuleCatalogValidation? ValidateSkillsCatalog(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogInput input,
        ICharacterSourceDataContext source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var blockers = new List<string>();
            var compiled = CompileSkills1949(current, input, source, blockers, cancellationToken);
            var entries = input.Entries.Select(entry => compiled is not null && compiled.Unavailable.TryGetValue(entry.Key, out var reason)
                ? entry with { State = CharacterCurrentRuleEntryState.Unavailable, UnavailableReason = reason } : entry).ToArray();
            var result = CharacterCurrentRuleCatalogValidation.CreateSnapshot(input, Array.AsReadOnly(entries),
                Array.AsReadOnly(blockers.Distinct(StringComparer.Ordinal).ToArray()), SkillsGrammar1949);
            if (result.IsComplete && compiled is not null && current.IsUnchanged)
            {
                SkillsCatalogs1949.Add(result, compiled with { Frozen = result.ContentDigest });
                RetainedEvaluationFor2764(current, source)?.RememberSkills(result);
            }
            return result;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
                                      or OverflowException)
        {
            return new(input, input.Entries, ["current-skills-invalid-catalog"], SkillsGrammar1949);
        }
    }

    public static CharacterCurrentRuleDomainExecution? ExecuteSkills(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!TryCurrentSkills1949(current, catalog, source, cancellationToken, out var compiled)) return null;
            if (UsesNativeMethods1958(current) && current.Stage == CharacterCurrentRuleStage.Creation
                && source.TryResolveCreationSourceProfile(out var nativeProfile))
                return nativeProfile.BuildMethod == CharacterCreationBuildMethods.Karma
                    ? ExecuteNativeKarmaSkills1958(current, catalog, source) : ExecuteNativeLifeModuleSkills1958(current, catalog, source);
            var root = ParseSkillsXml1949(current.CharacterXml);
            root = SkillsEvaluationGraph1950(compiled, root);
            var selected = new HashSet<string>(StringComparer.Ordinal);
            var blockers = new List<string>();
            SkillsOutcome1949 outcome;
            var state = CharacterCurrentRuleConsumptionState.Evaluated;
            if (current.Stage == CharacterCurrentRuleStage.Career)
            {
                var career = EvaluateCareerSkills1949(current, compiled, root, selected, blockers);
                if (career is null) return null;
                outcome = new(career, null);
            }
            else
            {
                var creation = EvaluateCreationSkills1949(current, compiled, root, selected, blockers, out bool configured);
                if (creation is null) return null;
                outcome = new(null, creation);
                if (!configured) state = CharacterCurrentRuleConsumptionState.NotConfigured;
            }
            if (!current.IsUnchanged || catalog.ContentDigest != compiled.Frozen) return null;
            return IssueRuleEffects2912(current, catalog, new(current, catalog, state, selected.Order(StringComparer.Ordinal).ToArray(),
                CharacterCreationFinalizationDigest.Compute(new
                {
                    current.BindingDigest, Catalog = catalog.ContentDigest, compiled.SettingsState,
                    compiled.Policy, compiled.Authority, SelectedEffects = compiled.SelectedEffects.Digest, Outcome = outcome
                }), blockers.Distinct(StringComparer.Ordinal).ToArray()), TagRuleEffects2912(outcome, SkillsEffects2912(outcome), "Advancement, allocation and specialization constraints are calculated. Action dice pools require the supported sheet graph."));
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
                                      or OverflowException or KeyNotFoundException) { return null; }
    }

    /// <summary>Exact retained option query. Copied reports, tombstones, unknown entries and stale
    /// revisions fail closed. This observation grants neither purchase nor aggregate admission.</summary>
    public static CharacterCreationSkillCatalogEntry? FindCurrentSkill(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, string catalogKey, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentSkills1949(current, catalog, source, cancellationToken, out var compiled)) return null;
        var option = compiled.Rows.SingleOrDefault(row => row.Key == catalogKey)?.Option;
        return option is null ? null : option with
        {
            Specializations = Array.AsReadOnly(option.Specializations.ToArray()),
            SourceAnchorIds = Array.AsReadOnly(option.SourceAnchorIds.ToArray())
        };
    }

    /// <summary>Re-executes Career kernels on the original current character. The result is a
    /// quote only; mutation must reacquire aggregate admission and the current owner/revision.</summary>
    public static CharacterCareerActiveSkillAdvanceQuote? QuoteCurrentActiveSkill(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, Guid skillId, CancellationToken cancellationToken = default)
    {
        if (current.Stage != CharacterCurrentRuleStage.Career
            || !TryCurrentSkills1949(current, catalog, source, cancellationToken, out var compiled)) return null;
        try
        {
            var blockers = new List<string>();
            var root = ParseSkillsXml1949(current.CharacterXml);
            root = SkillsEvaluationGraph1950(compiled, root);
            var evaluated = EvaluateCareerSkills1949(current, compiled, root,
                new(StringComparer.Ordinal), blockers);
            return blockers.Count == 0 && current.IsUnchanged
                ? evaluated?.Active.SingleOrDefault(quote => quote.Identity.SkillId == skillId) : null;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
                                      or OverflowException or KeyNotFoundException) { return null; }
    }

    public static CharacterCareerKnowledgeSkillAdvanceQuote? QuoteCurrentKnowledgeSkill(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, Guid skillId, CancellationToken cancellationToken = default)
        => ReadCareerSkills1949(current, catalog, source, cancellationToken)?.Knowledge
            .SingleOrDefault(quote => quote.Identity.SkillId == skillId);

    public static CharacterCareerSkillGroupAdvanceQuote? QuoteCurrentSkillGroup(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, Guid groupId, CancellationToken cancellationToken = default)
        => ReadCareerSkills1949(current, catalog, source, cancellationToken)?.Groups
            .SingleOrDefault(quote => quote.Identity.InternalId == groupId);

    public static CharacterCareerSkillSpecializationQuote? QuoteCurrentSkillSpecialization(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, Guid skillId, string optionIdentity, CancellationToken cancellationToken = default)
        => ReadCareerSkills1949(current, catalog, source, cancellationToken)?.Specializations
            .SingleOrDefault(quote => quote.Identity.SkillId == skillId && quote.Selection.OptionIdentity == optionIdentity);

    // The current transaction uses precisely the same inputs as QuoteCurrentSkillSpecialization.
    // The template selection is replaced only by the existing request/confirmation kernel.
    internal static IReadOnlyList<CharacterCareerSkillSpecializationInput>? ReadCurrentSkillSpecializationInputs2559(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, CancellationToken cancellationToken = default)
        => ReadCareerSkills1949(current, catalog, source, cancellationToken)?.SpecializationInputs;

    private static CareerSkills1949? ReadCareerSkills1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, CancellationToken cancellationToken)
    {
        if (current.Stage != CharacterCurrentRuleStage.Career
            || !TryCurrentSkills1949(current, catalog, source, cancellationToken, out var compiled)) return null;
        try
        {
            var blockers = new List<string>();
            var root = ParseSkillsXml1949(current.CharacterXml);
            root = SkillsEvaluationGraph1950(compiled, root);
            var evaluated = EvaluateCareerSkills1949(current, compiled, root,
                new(StringComparer.Ordinal), blockers);
            return blockers.Count == 0 && current.IsUnchanged ? evaluated : null;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
                                      or OverflowException or KeyNotFoundException) { return null; }
    }

    private static bool TryCurrentSkills1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source,
        CancellationToken cancellationToken, out SkillsCatalog1949 compiled)
    {
        cancellationToken.ThrowIfCancellationRequested();
        compiled = null!;
        var evaluation = RetainedEvaluationFor2764(current, source);
        if (!catalog.IsComplete || !current.IsUnchanged || !SkillsCatalogs1949.TryGetValue(catalog, out var saved)
            || !ReferenceEquals(saved.Current, current) || saved.Frozen != catalog.ContentDigest) return false;
        // Only a compilation issued inside this exact retained evaluation can hit.
        // A copied report, another source (even byte-equal), or a later evaluation
        // must replay both observations below. Cached graphs stay private.
        if (evaluation is not null && ReferenceEquals(source, saved.RetainedSource)
            && evaluation.OwnsSkills(catalog))
        {
            compiled = saved;
            return true;
        }
        var blockers = new List<string>();
        var replay = CompileSkills1949(current, catalog.Input, source, blockers, cancellationToken);
        if (replay is null || blockers.Count != 0 || saved.SettingsState != replay.SettingsState
            || saved.Authority.AuthorityDigest != replay.Authority.AuthorityDigest
            || saved.Policy.AuthorityDigest != replay.Policy.AuthorityDigest
            || saved.SelectedEffects.Digest != replay.SelectedEffects.Digest || !current.IsUnchanged) return false;
        // Quotes retain the actual source instance that issued the catalog observation.
        // An equivalent caller-provided adapter cannot replace that retained source.
        var retained = ReferenceEquals(source, saved.RetainedSource) ? replay
            : CompileSkills1949(current, catalog.Input, saved.RetainedSource, blockers, cancellationToken);
        if (retained is null || blockers.Count != 0 || retained.Authority.AuthorityDigest != saved.Authority.AuthorityDigest
            || retained.SettingsState != saved.SettingsState
            || retained.SelectedEffects.Digest != saved.SelectedEffects.Digest || !current.IsUnchanged) return false;
        compiled = retained with { Frozen = saved.Frozen };
        return true;
    }

    private static SkillsCatalog1949? CompileSkills1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogInput input, ICharacterSourceDataContext source, List<string> blockers,
        CancellationToken cancellationToken)
    {
        if (!current.IsUnchanged || input.Domain != CharacterCurrentRuleDomain.Skills || input.Query != "skills.xml"
            || input.ContextDigest != current.BindingDigest || input.Blockers.Count != 0
            || !input.Dependencies.SequenceEqual(new[] { current.CaptureDigest, current.EffectivePolicyDigest,
                current.RequiredPackageSetDigest ?? string.Empty, current.SemanticsProfile, current.SemanticsFingerprint },
                StringComparer.Ordinal)) return Fail("current-skills-catalog-binding");
        if (input.Entries.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count() != input.Entries.Count
            || input.Entries.Count(entry => entry.Key == "$global") != 1) return Fail("current-skills-catalog-identity");
        foreach (var operation in input.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.Order < 0 || operation.Order >= input.Operations.Count
                || !ReferenceEquals(input.Operations[operation.Order], operation) || operation.Query != input.Query
                || !CharacterCreationFinalizationDigest.IsCanonical(operation.SourceIdentity)
                || !CharacterCreationFinalizationDigest.IsCanonical(operation.SourceDigest)
                || operation.Phase is not ("override_" or "custom_" or "amend_")
                || operation.Operation is not ("" or "RECURSE" or "REPLACE" or "REMOVE" or "APPEND" or "ADDNODE")
                || !operation.Dependencies.SequenceEqual(input.Dependencies, StringComparer.Ordinal)
                || !ValidSkillsSelector1949(ParseSkillsXml1949(operation.Selector)))
                return Fail("current-skills-merge-grammar");
        }
        var global = input.Entries.Single(entry => entry.Key == "$global");
        if (global.State != CharacterCurrentRuleEntryState.AvailableNotSelected
            || !ValidSkillsGlobal1949(ParseSkillsXml1949(global.Xml))) return Fail("current-skills-global-grammar");
        if (!source.TryResolveCareerSettingsProfile(out var settingsXml, out var settingsState)
            || string.IsNullOrEmpty(settingsState)) return Fail("current-skills-settings-unavailable");
        var settings = ParseSkillsXml1949(settingsXml);
        if (settings.Elements("books").Count() != 1 || settings.Element("books") is not { } books
            || books.HasAttributes || DirectSkillText1949(books) || books.Elements().Any(book => book.Name != "book"
                || book.HasAttributes || book.HasElements || string.IsNullOrWhiteSpace(book.Value) || book.Value != book.Value.Trim())
            || books.Elements().Select(book => book.Value).Distinct(StringComparer.Ordinal).Count() != books.Elements().Count())
            return Fail("current-skills-sourcebook-grammar");
        if (!ReadCurrentCareerSkillMaximum(current, source, settings, "maxskillrating", out int activeMaximum)
            || !ReadCurrentCareerSkillMaximum(current, source, settings, "maxknowledgeskillrating", out int knowledgeMaximum)
            || activeMaximum > 1000 || knowledgeMaximum > 1000) return Fail("current-skills-career-caps");
        CharacterCreationKarmaSkillsPolicy? policy;
        var character = ParseSkillsXml1949(current.CharacterXml);
        bool policyAvailable = SkillScalar1949(character, "buildmethod") switch
        {
            CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen =>
                source.TryResolveCreationPrioritySkillKarmaPolicy(out policy),
            CharacterCreationBuildMethods.Karma => source.TryResolveCreationKarmaSkillsPolicy(out policy),
            CharacterCreationBuildMethods.LifeModules => source.TryResolveCreationLifeModuleSkillsPolicy(out policy),
            _ => SetNoPolicy(out policy)
        };
        if (!policyAvailable || policy is null || policy.KnowledgePointsExpression !=
                CharacterCreationStandardPrioritySkillsRules.KnowledgePointsExpression)
            return Fail("current-skills-profile-grammar");
        var selectedEffects = CompileSelectedSkillEffects1950(current, source, character, blockers);
        if (selectedEffects is null) return null;
        SkillsEffects1949 futureEffects = new([], [], [], []);
        if (current.Stage == CharacterCurrentRuleStage.Career)
        {
            var effects = ReadSkillsEffects1949(selectedEffects.Improvements, blockers);
            if (effects is null) return null;
            futureEffects = effects;
        }
        // This first grammar does not claim that unknown global skill settings are inert.
        string[] skillSettings = ["maxskillrating", "maxskillratingcreate", "maxknowledgeskillrating",
            "maxknowledgeskillratingcreate", "knowledgepointsexpression", "usepointsonbrokengroups",
            "breakskillgroupsincreatemode", "specializationsbreakskillgroups", "allowskillregrouping",
            "allowpointbuyspecializationsonkarmaskills", "compensateskillgroupkarmadifference"];
        if (settings.Elements().Any(node =>
                (node.Name.LocalName.Contains("skill", StringComparison.OrdinalIgnoreCase)
                 || node.Name.LocalName.Contains("specialization", StringComparison.OrdinalIgnoreCase)
                 || node.Name.LocalName.Contains("knowledge", StringComparison.OrdinalIgnoreCase))
                && !skillSettings.Contains(node.Name.LocalName, StringComparer.Ordinal)
                && !IsCurrentKarmaPromptSkillCompatibility2916(current, source, node)))
            return Fail("current-skills-unknown-setting");
        string[] costNames = ["karmanewactiveskill", "karmaimproveactiveskill", "karmanewknowledgeskill",
            "karmaimproveknowledgeskill", "karmanewskillgroup", "karmaimproveskillgroup", "karmaspecialization", "karmaknospecialization"];
        if (settings.Elements("karmacost").Count() > 1 || settings.Element("karmacost") is { } costs
            && costs.Elements().Any(node => (node.Name.LocalName.Contains("skill", StringComparison.OrdinalIgnoreCase)
                    || node.Name.LocalName.Contains("specialization", StringComparison.OrdinalIgnoreCase))
                && !costNames.Contains(node.Name.LocalName, StringComparer.Ordinal)
                && !IsCurrentKarmaPromptSkillCompatibility2916(current, source, node))) return Fail("current-skills-unknown-cost-setting");
        var rows = new List<SkillsRow1949>();
        var ids = new HashSet<Guid>();
        var unavailable = new Dictionary<string, string>(StringComparer.Ordinal);
        var enabledBooks = settings.Element("books")?.Elements("book").Select(book => book.Value).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        // Reuse only the exact imported read already admitted by the private
        // synchronous evaluation. Its archive is immutable and privately owned;
        // every row still observes mutable bindings and source drift through
        // Allows. Public/foreign reads keep their full archive verification.
        var priorityRead9627 = selectedEffects.Native?.Imported?.Read;
        var priorityEvaluation9627 = RetainedEvaluationFor2764(current, source);
        bool reusePriority9627 = priorityRead9627 is not null
            && ReferenceEquals(priorityRead9627.Current, current)
            && priorityEvaluation9627?.CareerEffects is CurrentCareerEffects1960 retainedEffects9627
            && ReferenceEquals(retainedEffects9627.Imported?.Read, priorityRead9627);
        bool retainedPriority9627 = reusePriority9627 && priorityRead9627!.HasPrioritySelection9569;
        // These selectors use only the context's immutable character XML,
        // ruleset, stage and semantics. Resolve them lazily once for the row
        // walk; each row still performs its existing mutable/source observation.
        bool? nativeMovement9641 = null;
        bool? nativePriorityCareer9641 = null;
        foreach (var entry in input.Entries.Where(entry => entry.Key != "$global"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.State == CharacterCurrentRuleEntryState.Tombstone) continue;
            if (entry.State != CharacterCurrentRuleEntryState.AvailableNotSelected
                || !TrySkillRow1949(entry, input.ContentDigest, source, enabledBooks,
                    (nativeMovement9641 ??= UsesNativeMethods1958(current) || UsesOwnedPriorityCareer9569(current)
                        || UsesPrioritySkillMovement1990(current, character)) == true,
                    current.Stage == CharacterCurrentRuleStage.Career ? selectedEffects.MagicUnlocks5343 : [],
                    out var row, out var reason, reusePriority9627
                        ? priorityEvaluation9627!.Allows(current, source) && retainedPriority9627
                        : priorityRead9627?.HasPrioritySelection9569 == true,
                    (nativePriorityCareer9641 ??= UsesNativePriorityCareer9569(current)) == true))
                return Fail("current-skills-entry-grammar:" + entry.Key);
            if (reason is not null) { unavailable.Add(entry.Key, reason); continue; }
            if (!ids.Add(Guid.Parse(row.Option.SourceSkillId))) return Fail("current-skills-entry-identity:" + entry.Key);
            rows.Add(row);
        }
        // V2 retains the same canonical movement rows as V3. Eligibility is a
        // separate retained-source observation; it never changes group identity.
        var priorityMovement = current.Stage == CharacterCurrentRuleStage.Career
            && UsesPrioritySkillMovement1990(current, character)
            && rows.Any(row => row.Option.RequiresGroundMovement || row.Option.RequiresSwimMovement || row.Option.RequiresFlyMovement)
                ? ReadPrioritySkillMovement1990(current, source, character, policy) : null;
        if (priorityMovement is not null)
            selectedEffects = selectedEffects with { Digest = CharacterCreationFinalizationDigest.Compute(new
                { SelectedEffects = selectedEffects.Digest, PriorityMovement = priorityMovement.Digest }) };
        var metadata = ParseSkillsXml1949(global.Xml);
        if (metadata.Element("categories") is { } categories && rows.Any(row => !categories.Elements("category")
                .Any(category => category.Attribute("type")?.Value == row.Option.Kind && category.Value == row.Option.Category))
            || metadata.Element("skillgroups") is { } groupNames && rows.Any(row => row.Option.SkillGroup is { } name
                && !groupNames.Elements("name").Any(group => group.Value == name))) return Fail("current-skills-global-membership");
        // Exercise the existing interval kernels for every available future entry. These
        // hypothetical catalog bounds are grammar validation, never current allocations.
        foreach (var row in rows)
        {
            bool priced = row.Option.Kind == CharacterCreationSkillKinds.Active
                ? CharacterCreationSkillCostRules.TryActive(0, policy.MaxActiveSkillRatingCreate,
                    policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill, out _)
                : CharacterCreationSkillCostRules.TryKnowledge(0, policy.MaxKnowledgeSkillRatingCreate,
                    policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill, out _);
            if (!priced || policy.KarmaSpecialization is < 0 or > CharacterCareerSkillSpecializationRules.MaximumSettingCost
                || policy.KarmaKnowledgeSpecialization is < 0 or > CharacterCareerSkillSpecializationRules.MaximumSettingCost)
                return Fail("current-skills-future-cost-grammar");
        }
        if (!CharacterCreationSkillCostRules.TryGroup(0, policy.MaxSkillGroupRatingCreate,
                policy.KarmaNewSkillGroup, policy.KarmaImproveSkillGroup, out _)) return Fail("current-skills-future-group-cost");
        if (!FutureSkillCosts1949(rows, policy, futureEffects, activeMaximum, knowledgeMaximum, settingsState))
            return Fail("current-skills-future-quote-grammar");
        var active = rows.Where(row => row.Option.Kind == CharacterCreationSkillKinds.Active).Select(row => row.Option)
            .OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.SourceSkillId, StringComparer.Ordinal).ToArray();
        var knowledge = rows.Where(row => row.Option.Kind == CharacterCreationSkillKinds.Knowledge).Select(row => row.Option)
            .OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.SourceSkillId, StringComparer.Ordinal).ToArray();
        var groups = active.Where(row => row.SkillGroup is not null).GroupBy(row => row.SkillGroup!, StringComparer.Ordinal)
            .Select(group =>
            {
                string[] members = group.Select(row => row.SourceSkillId).Order(StringComparer.Ordinal).ToArray();
                string digest = CharacterCreationSkillsDigest.Compute(new
                {
                    Schema = "chummer.sr5.creation-skill-group-source.v1", Name = group.Key,
                    MemberSkillSourceIds = members, EffectiveSkillsInputsDigest = input.ContentDigest
                });
                return new CharacterCreationSkillGroupCatalogEntry(digest, group.Key, members, digest,
                    [$"skills.xml#skillgroup:{group.Key}"]);
            }).OrderBy(group => group.Name, StringComparer.Ordinal).ThenBy(group => group.GroupId, StringComparer.Ordinal).ToArray();
        // The catalog has its own independent proof. Never clear the old resolver's custom veto.
        var authority = new CharacterCreationSkillsAuthority(CharacterCreationSkillsSchemas.AuthorityV1,
            policy.SettingsProfileId, input.ContentDigest, policy.RawProfileInputsDigest, policy.KnowledgePointsExpression,
            policy.MaxActiveSkillRatingCreate, policy.MaxKnowledgeSkillRatingCreate, policy.MaxSkillGroupRatingCreate,
            CharacterCreationStandardPrioritySkillsRules.BaseNativeLanguageCount, policy.UsePointsOnBrokenGroups,
            policy.StrictSkillGroupsInCreateMode, policy.SpecializationsBreakSkillGroups, active, knowledge, groups,
            [], policy.SourceAnchorIds.Concat(new[] { "skills.xml", "weapons.xml" }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), [], true,
            CharacterCreationStandardPrioritySkillsRules.ComputeRuntimeDigest(policy.UsePointsOnBrokenGroups,
                policy.StrictSkillGroupsInCreateMode, policy.SpecializationsBreakSkillGroups,
                policy.MaxActiveSkillRatingCreate, policy.MaxKnowledgeSkillRatingCreate), string.Empty);
        authority = authority with { AuthorityDigest = CharacterCreationSkillsDigest.Compute(authority) };
        if (!CharacterCreationSkillsDraftIntegrity.IsValidAuthority(authority)) return Fail("current-skills-canonical-catalog");
        return new(current, source, string.Empty, authority, policy, settingsXml, settingsState, activeMaximum, knowledgeMaximum, rows, unavailable, selectedEffects) { PriorityMovement = priorityMovement };

        SkillsCatalog1949? Fail(string blocker) { blockers.Add(blocker); return null; }
        static bool SetNoPolicy(out CharacterCreationKarmaSkillsPolicy? missing) { missing = null; return false; }
    }

    private static bool TrySkillRow1949(CharacterCurrentRuleCatalogEntry entry, string inputsDigest,
        ICharacterSourceDataContext source, IReadOnlySet<string> enabledBooks, bool nativeMovement,
        IReadOnlyList<string> magicUnlocks,
        out SkillsRow1949 row, out string? unavailable, bool importedPriority5699 = false,
        bool nativePriorityCareer9569 = false)
    {
        row = null!;
        unavailable = null;
        var node = ParseSkillsXml1949(entry.Xml);
        string[] fields = ["id", "name", "hide", "ignoresourcedisabled", "attribute", "category", "default",
            "exotic", "skillgroup", "requiresgroundmovement", "requiresswimmovement", "requiresflymovement",
            "specs", "source", "page"];
        if (node.Name != "skill" || !SkillShape1949(node, fields, ["specs"])
            || !Guid.TryParseExact(SkillScalar1949(node, "id"), "D", out var id) || id == Guid.Empty
            || entry.Name != SkillScalar1949(node, "name") || string.IsNullOrWhiteSpace(entry.Name)) return false;
        string kind = entry.Key == "skills/id:" + id.ToString("D") ? CharacterCreationSkillKinds.Active
            : entry.Key == "knowledgeskills/id:" + id.ToString("D") ? CharacterCreationSkillKinds.Knowledge : string.Empty;
        string category = SkillScalar1949(node, "category"), attribute = SkillScalar1949(node, "attribute");
        if (!CharacterCreationStandardPrioritySkillsRules.IsSupportedCategory(kind, category)
            || !CharacterCreationStandardPrioritySkillsRules.IsSupportedAttribute(attribute)
            || !ReadSkillBool1949(node, "default", null, out var canDefault)
            || !ReadSkillBool1949(node, "exotic", false, out var exotic)
            || !ReadSkillBool1949(node, "hide", false, out var hidden)
            || !ReadSkillBool1949(node, "ignoresourcedisabled", false, out var ignores)
            || !ReadSkillBool1949(node, "requiresgroundmovement", false, out var ground)
            || !ReadSkillBool1949(node, "requiresswimmovement", false, out var swim)
            || !ReadSkillBool1949(node, "requiresflymovement", false, out var fly)
            || node.Element("specs") is not { } specs || specs.HasAttributes || DirectSkillText1949(specs)
            || specs.Elements().Any(spec => spec.Name != "spec" || spec.HasAttributes || spec.HasElements
                || string.IsNullOrWhiteSpace(spec.Value))
            // Match the producer's trimmed labels for collisions; keep raw XML for source identity.
            || specs.Elements().Select(spec => spec.Value.Trim()).Distinct(StringComparer.Ordinal).Count() != specs.Elements().Count()) return false;
        string groupValue = SkillScalar1949(node, "skillgroup");
        string? group = groupValue.Length == 0 ? null : groupValue;
        if (kind == CharacterCreationSkillKinds.Knowledge && (group is not null || exotic) || exotic && group is not null) return false;
        string book = SkillScalar1949(node, "source");
        if (hidden || book.Length != 0 && !enabledBooks.Contains(book))
        {
            // These exact, fully validated scalar flags are row-local. Both Find and every
            // canonical quote use only Rows; selected references to this key block. No stock
            // lookup is attempted for unavailable rows, including ignoresourcedisabled rows
            // that the current Career source reader itself cannot resolve.
            unavailable = hidden ? "current-skills-hidden" : "current-skills-source-disabled";
            return true;
        }
        // Native V3 consumes movement access from the canonical Creation quote or
        // the freshly compiled Career metatype. Keep these source rows and flags
        // in its catalog: dropping them truncates Athletics to Gymnastics and
        // invalidates the group authority before current-state execution can run.
        // The native Priority producer uses IsOrdinary for pseudo-magical
        // skills (including Arcana). V10 consumes those same current source
        // rows; older interpreter profiles retain their previous refusal.
        if (exotic || !nativeMovement && (ground || swim || fly)
            || category == "Pseudo-Magical Active" && !nativePriorityCareer9569
            || category == "Resonance Active" && (!importedPriority5699 || !magicUnlocks.Contains("Technomancer"))
            || category == "Magical Active" && magicUnlocks.Count == 0)
        {
            // The exact row is local, but its current access/specific-identity join is
            // not yet implemented here. Exclude it from every query and quote rather
            // than advertising an Available option that current execution cannot use.
            unavailable = "current-skills-access-compiler-required";
            return true;
        }
        string raw;
        var careerKind = kind == CharacterCreationSkillKinds.Active ? CharacterCareerSkillKind.Active : CharacterCareerSkillKind.Knowledge;
        if (careerKind == CharacterCareerSkillKind.Active)
        {
            if (!source.TryResolveActiveSkillSource(id.ToString("D"), out var resolved)
                || resolved.Name != entry.Name || resolved.SkillCategory != category || resolved.SkillGroup != groupValue)
                return false;
            raw = resolved.RawSourceXml;
        }
        else
        {
            if (group is not null || exotic || !source.TryResolveKnowledgeSkillSource(id.ToString("D"), out var resolved)
                || resolved.Name != entry.Name || resolved.SkillCategory != category) return false;
            raw = resolved.RawSourceXml;
        }
        // The catalog text already passed the bounded, DTD-prohibiting parser above.
        // Identical immutable source text needs no second parse. Differing text keeps
        // the structural comparison; source and specialization reads still occur.
        if (exotic && group is not null
            || !string.Equals(raw, entry.Xml, StringComparison.Ordinal)
                && !XNode.DeepEquals(node, ParseSkillsXml1949(raw))
            || !source.TryResolveCareerSkillSpecializationSource(id.ToString("D"), careerKind, out var specializations)) return false;
        var options = specializations.Options.GroupBy(option => option.Name, StringComparer.Ordinal)
            .Select(grouping => grouping.OrderBy(option => option.Kind).ThenBy(option => option.OptionIdentity, StringComparer.Ordinal).First())
            .Select(option => new CharacterCreationSkillSpecializationOption(option.OptionIdentity, option.Name, option.SourceAnchor))
            .OrderBy(option => option.Name, StringComparer.Ordinal).ThenBy(option => option.OptionId, StringComparer.Ordinal).ToArray();
        string[] anchors = [$"skills.xml#skill:{id:D}"];
        bool native = CharacterCreationStandardPrioritySkillsRules.CanBeNativeLanguage(kind, category);
        var optionRow = new CharacterCreationSkillCatalogEntry(id.ToString("D"), kind, entry.Name, category, attribute,
            group, exotic, CharacterCreationStandardPrioritySkillsRules.ComputeCatalogProjectionDigest(inputsDigest,
                id.ToString("D"), kind, entry.Name, category, attribute, group, exotic, options, anchors,
                canDefault, ignores, ground, swim, fly, native), options, anchors)
        {
            CanDefault = canDefault, IgnoresSourceDisabled = ignores, RequiresGroundMovement = ground,
            RequiresSwimMovement = swim, RequiresFlyMovement = fly, CanBeNativeLanguage = native
        };
        // A category, saved MAG flag or saved SpecialSkills row grants no access.
        // These filters came from a freshly compiled and reconciled owner graph.
        if (category == "Magical Active" && !magicUnlocks.Any(filter => CharacterCreationSkillsAccessRules.Matches(optionRow, filter)))
        {
            unavailable = "current-skills-access-compiler-required";
            return true;
        }
        row = new(entry.Key, entry.Xml, optionRow, specializations);
        return true;
    }

    private sealed record SkillsOutcome1949(CareerSkills1949? Career, CreationSkills1949? Creation);
    private sealed record CreationSkills1949(string? OriginalAbsentGraph,
        IReadOnlyList<CharacterCreationSkillProjection> Skills, IReadOnlyList<CharacterCreationSkillGroupProjection> Groups,
        CharacterCreationBudgetState? ActiveBudget, CharacterCreationBudgetState? GroupBudget,
        CharacterCreationBudgetState? KnowledgeBudget, int KnowledgeOverflow, IReadOnlyList<string> CanonicalBlockers,
        IReadOnlyList<CharacterCreationSkillAllocation> Allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> GroupAllocations,
        IReadOnlyList<CharacterCreationAttributeAllocation> AttributeAllocations);

    private static CreationSkills1949? EvaluateCreationSkills1949(CharacterCurrentRuleEnvironmentContext current,
        SkillsCatalog1949 catalog, XElement root, HashSet<string> selected, List<string> blockers, out bool configured)
    {
        configured = true;
        var auxiliary = current.Current.Document.AuxiliaryState;
        var draft = auxiliary.CharacterCreationSkillsDraft;
        var prerequisite = auxiliary.CharacterCreationPrerequisiteDraft;
        var attributes = auxiliary.CharacterCreationAttributesDraft;
        if (root.Name != "character" || !ReadSkillBool1949(root, "created", null, out bool created) || created
            || root.Elements("newskills").Count() > 1 || root.Elements("improvements").Count() > 1
            || root.Element("skills") is not null) return Fail("current-skills-creation-state");
        // Unknown saved global effects and additional creation ledgers are not absence.
        if (auxiliary.CharacterCreationCompletionDrafts is { Count: > 0 }
            || auxiliary.LifeModuleDecisionAcceptances is { Count: > 0 }) return Fail("current-skills-creation-effects-unavailable");
        var graph = root.Element("newskills");
        bool graphHasChoices = graph is not null && (graph.HasAttributes || DirectSkillText1949(graph)
            || !ReadSkillInt1949(graph, "skillptsmax", 0, out _) || !ReadSkillInt1949(graph, "skillgrpsmax", 0, out _)
            || graph.Elements().Any(node => node.Name.Namespace != XNamespace.None
                || !new[] { "skills", "knoskills", "groups", "skilljackknowledgeskills", "skillptsmax", "skillgrpsmax" }.Contains(node.Name.LocalName))
            || graph.Elements().GroupBy(node => node.Name).Any(group => group.Count() != 1)
            || graph.Elements().Where(node => node.Name != "skillptsmax" && node.Name != "skillgrpsmax")
                .Any(node => node.HasElements || node.HasAttributes || DirectSkillText1949(node)));
        if (draft is null)
        {
            if (graphHasChoices || auxiliary.CharacterCreationSkillsReceipts is { Count: > 0 }
                || prerequisite?.TalentSelection?.GrantPlan is not null) return Fail("current-skills-creation-choice-authority");
            configured = false;
            return new(graph?.ToString(SaveOptions.DisableFormatting), [], [], null, null, null, 0, [], [], [], []);
        }
        if (graphHasChoices || draft.CharacterEffectsApplied || prerequisite is null || attributes is null
            || draft.WorkspaceId != current.Current.Id || attributes.WorkspaceId != current.Current.Id
            || draft.BaseContentRevision <= 0 || draft.BaseContentRevision >= current.Current.ContentRevision
            || draft.BaseRawCharacterXmlDigest != CharacterCreationSkillsDigest.ComputeUtf8(current.CharacterXml)
            || attributes.BaseRawCharacterXmlDigest != draft.BaseRawCharacterXmlDigest
            || draft.DraftDigest != CharacterCreationSkillsDraftIntegrity.ComputeDigest(draft)
            || draft.PrerequisiteDraftDigest != prerequisite.DraftDigest
            || draft.AttributesDraftDigest != attributes.DraftDigest
            || prerequisite.BuildMethod != SkillScalar1949(root, "buildmethod")
            || prerequisite.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
            || prerequisite.PriorityTable != "Standard") return Fail("current-skills-creation-draft-binding");
        var assignments = prerequisite.Assignments.Where(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Skills).ToArray();
        if (assignments is not [var assignment]
            || !CharacterCreationStandardPrioritySkillsRules.TryGetBudget(assignment.Rank, out int activePoints, out int groupPoints))
            return Fail("current-skills-creation-budget");
        var groups = new List<CharacterCreationSkillGroupAllocation>();
        foreach (var allocation in draft.GroupAllocations)
        {
            var historical = draft.SkillGroups.Where(group => group.GroupId == allocation.GroupId).ToArray();
            if (historical is not [var oldGroup]) return Fail("current-skills-creation-group-binding");
            var matches = catalog.Authority.SkillGroups.Where(group => group.Name == oldGroup.Name
                && group.MemberSkillSourceIds.SequenceEqual(oldGroup.MemberSkillSourceIds, StringComparer.Ordinal)).ToArray();
            if (matches is not [var group]) return Fail("current-skills-creation-group-source");
            groups.Add(new(group.GroupId, allocation.Rating));
        }
        // Movement newly admitted by V2 must come from the current prerequisite
        // producer, not merely a self-consistent saved draft digest.
        if (UsesPrioritySkillMovement1990(current, root)
            && catalog.Rows.Any(row => row.Option.RequiresGroundMovement || row.Option.RequiresSwimMovement || row.Option.RequiresFlyMovement)
            && (!catalog.RetainedSource.TryResolveCreationPrerequisiteAuthority(out var currentPrerequisite)
                || !CharacterCreationPrerequisiteService.TryEvaluateCurrentSelection1949(currentPrerequisite,
                    prerequisite, out var evaluatedPrerequisite, out _)
                || evaluatedPrerequisite?.HeritageSelection is not { } heritage
                || CharacterCreationFinalizationDigest.Compute(heritage)
                    != CharacterCreationFinalizationDigest.Compute(prerequisite.HeritageSelection)))
            return Fail("current-skills-creation-movement-authority");
        // Saved allocations are re-evaluated; historical Skills/SkillGroups results are not a result delegate.
        var result = CharacterCreationSkillsService.EvaluateCurrentAllocations1949(catalog.Authority, prerequisite,
            attributes, activePoints, groupPoints, draft.Allocations, groups, catalog.SelectedEffects.Knowledge);
        foreach (var blocker in result.Blockers)
        {
            // A still-unselected native language is a legitimate remaining Creation choice.
            // Preserve the canonical blocker in the outcome, but do not fabricate a choice.
            if (blocker != CharacterCreationSkillsBlockers.NativeLanguageRequired
                || draft.Allocations.Any(allocation => allocation.IsNativeLanguage)) blockers.Add(blocker);
        }
        foreach (var skill in result.Skills)
        {
            var matches = catalog.Rows.Where(row => row.Option.SourceSkillId == skill.SourceSkillId && row.Option.Kind == skill.Kind).ToArray();
            if (matches is not [var row]) return Fail("current-skills-creation-selected-source");
            selected.Add(row.Key);
        }
        foreach (var group in result.Groups)
        {
            selected.Add("$global");
            foreach (var id in group.MemberSkillSourceIds)
            {
                var matches = catalog.Rows.Where(row => row.Option.SourceSkillId == id && row.Option.Kind == CharacterCreationSkillKinds.Active).ToArray();
                if (matches is not [var row]) return Fail("current-skills-creation-selected-group-source");
                selected.Add(row.Key);
            }
        }
        return new(null, result.Skills, result.Groups, result.ActiveBudget, result.GroupBudget, result.KnowledgeBudget,
            result.KnowledgeOverflow, result.Blockers, draft.Allocations, draft.GroupAllocations, attributes.Allocations);
        CreationSkills1949? Fail(string reason) { blockers.Add(reason); return null; }
    }

    private sealed record SavedSkill1949(Guid Id, SkillsRow1949 Source, XElement? Node,
        CharacterCareerSkillRatingPoints Points, bool Native, bool AllowUpgrade, IReadOnlyList<string> Specializations,
        bool MovementAvailable);
    private sealed record CareerSkills1949(IReadOnlyList<CharacterCareerActiveSkillAdvanceQuote> Active,
        IReadOnlyList<CharacterCareerKnowledgeSkillAdvanceQuote> Knowledge,
        IReadOnlyList<CharacterCareerSkillGroupAdvanceQuote> Groups,
        IReadOnlyList<CharacterCareerSkillSpecializationQuote> Specializations,
        IReadOnlyList<CharacterCareerSkillRating> Ratings,
        IReadOnlyList<CharacterCareerSkillSpecializationInput> SpecializationInputs,
        // Projection-only transport; do not change persisted domain outcome identities.
        [property: System.Text.Json.Serialization.JsonIgnore]
        IReadOnlyList<CharacterCareerSkillGroupAdvanceInput> GroupInputs);
    private sealed record SkillsEffects1949(
        IReadOnlyList<CharacterCareerActiveSkillKarmaModifier> Active,
        IReadOnlyList<CharacterCareerKnowledgeSkillKarmaModifier> Knowledge,
        IReadOnlyList<CharacterCareerSkillGroupKarmaModifier> Groups,
        IReadOnlyList<CharacterCareerSkillSpecializationModifier> Specializations);

    // These are explicitly hypothetical catalog probes, separate from current consumption.
    // Cost expressions are piecewise affine between modifier bounds: check endpoints and
    // both sides of every supported bound so an unused option cannot hide overflow or
    // an invalid modifier that would make its later canonical quote unavailable.
    private static bool FutureSkillCosts1949(IReadOnlyList<SkillsRow1949> rows, CharacterCreationKarmaSkillsPolicy policy,
        SkillsEffects1949 effects, int activeMaximum, int knowledgeMaximum, string settingsState)
    {
        foreach (var row in rows)
        {
            var option = row.Option;
            bool active = option.Kind == CharacterCreationSkillKinds.Active;
            int cap = active ? activeMaximum : knowledgeMaximum;
            var activeEffects = effects.Active.Where(modifier => modifier.Target.Length == 0 || modifier.Target ==
                (modifier.Kind is CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCost
                    or CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCostMultiplier ? option.Name : option.Category)).ToArray();
            var knowledgeEffects = effects.Knowledge.Where(modifier => modifier.Target.Length == 0 || modifier.Target ==
                (modifier.Kind is CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCost
                    or CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMultiplier ? option.Name : option.Category)).ToArray();
            var specEffects = effects.Specializations.Where(modifier => modifier.TargetSkillCategory.Length == 0
                || modifier.TargetSkillCategory == option.Category).ToArray();
            var bounds = activeEffects.SelectMany(modifier => new[] { modifier.Minimum, modifier.Maximum })
                .Concat(knowledgeEffects.SelectMany(modifier => new[] { modifier.Minimum, modifier.Maximum }))
                .Concat(specEffects.Select(modifier => modifier.MinimumRating)).Concat(new[] { 0, cap });
            var ratings = bounds.SelectMany(bound => new[] { Math.Clamp(bound - 1, 0, cap), Math.Clamp(bound, 0, cap), Math.Clamp(bound + 1, 0, cap) })
                .Distinct().Order().ToArray();
            Guid id = Guid.Parse(option.SourceSkillId);
            // These probes share only exact immutable raw strings. Keep digest reuse
            // local to this row; every rating/selection still runs the full rules.
            var specializationQuotes = new CharacterCareerSkillSpecializationQuoteBatch();
            foreach (int rating in ratings)
            {
                if (active)
                {
                    if (!CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(new(new(id, id), true, option.Name,
                            option.Category, option.Name, rating, 0, rating, cap, 0,
                            new(policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill, policy.KarmaNewSkillGroup,
                                policy.KarmaImproveSkillGroup, policy.CompensateSkillGroupKarmaDifference), [], activeEffects,
                            row.Xml, settingsState), out var activeProbe) || !CharacterCareerActiveSkillAdvanceRules.IsCoherent(activeProbe)) return false;
                }
                else if (!CharacterCareerKnowledgeSkillAdvanceRules.TryCreateQuote(new(new(id, id), true, "sr5", true,
                             true, false, option.Name, option.Category, option.Category, option.Name, rating, 0, rating, cap, 0,
                             new(policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill), knowledgeEffects,
                             row.Xml, row.Xml, settingsState), out var knowledgeProbe) || !CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(knowledgeProbe)) return false;
                foreach (var specialization in row.Specializations.Options)
                    if (!specializationQuotes.TryCreateQuote(new(new(id, id,
                                active ? CharacterCareerSkillKind.Active : CharacterCareerSkillKind.Knowledge),
                            true, true, option.IsExotic, true, true, false, option.Name, option.Category, option.Name,
                            option.SkillGroup ?? string.Empty, rating, 0, 0, 0, false, false,
                            new(policy.KarmaSpecialization, policy.KarmaKnowledgeSpecialization, policy.SpecializationsBreakSkillGroups),
                            specEffects, row.Specializations.Options,
                            new(specialization.Name, specialization.Kind, specialization.OptionIdentity),
                            row.Xml, row.Specializations.RawSourceState, settingsState), out var specializationProbe)
                        || !CharacterCareerSkillSpecializationRules.IsCoherent(specializationProbe)) return false;
            }
        }
        foreach (var group in rows.Where(row => row.Option.Kind == CharacterCreationSkillKinds.Active && row.Option.SkillGroup is not null)
                     .GroupBy(row => row.Option.SkillGroup!, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            var modifiers = effects.Groups.Where(modifier => modifier.Target.Length == 0
                || (modifier.Kind is CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost
                    or CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier ? modifier.Target == group.Key
                    : members.Any(member => member.Option.Category == modifier.Target))).ToArray();
            int last = Math.Max(0, Math.Min(activeMaximum - 1, 999));
            var bounds = modifiers.SelectMany(modifier => new[] { modifier.Minimum, modifier.Maximum }).Concat(new[] { 0, last });
            foreach (int rating in bounds.SelectMany(bound => new[] { Math.Clamp(bound - 1, 0, last), Math.Clamp(bound, 0, last), Math.Clamp(bound + 1, 0, last) }).Distinct())
                if (!CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(new(new(Guid.Parse(members[0].Option.SourceSkillId)),
                        true, "sr5", true, true, group.Key, rating, 0, activeMaximum, 0, false, false,
                        new(policy.KarmaNewSkillGroup, policy.KarmaImproveSkillGroup),
                        members.Select(member => new CharacterCareerSkillGroupMember(Guid.Parse(member.Option.SourceSkillId), rating,
                            true, member.Option.Category)).ToArray(), modifiers, string.Join("\n", members.Select(member => member.Xml)),
                        settingsState), out var groupProbe) || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(groupProbe)) return false;
        }
        return true;
    }

    private static CareerSkills1949? EvaluateCareerSkills1949(CharacterCurrentRuleEnvironmentContext current,
        SkillsCatalog1949 catalog, XElement root, HashSet<string> selected, List<string> blockers)
    {
        if (root.Name != "character" || !ReadSkillBool1949(root, "created", null, out bool created) || !created
            || !ReadSkillInt1949(root, "karma", 0, out int karma) || karma > CharacterCareerActiveSkillAdvanceRules.MaximumKarma
            || root.Elements("newskills").Count() > 1 || root.Elements("improvements").Count() > 1
            || root.Element("skills") is not null) return Fail("current-skills-career-state");
        var effects = ReadSkillsEffects1949(root.Element("improvements"), blockers);
        if (effects is null) return null;
        var graph = root.Element("newskills");
        if (graph is not null && (!SkillShape1949(graph,
                ["skillptsmax", "skillgrpsmax", "skills", "knoskills", "groups", "skilljackknowledgeskills"],
                ["skills", "knoskills", "groups", "skilljackknowledgeskills"])
            || graph.Element("skilljackknowledgeskills")?.HasElements == true)) return Fail("current-skills-saved-graph");
        var skills = new List<SavedSkill1949>();
        var skillIds = new HashSet<Guid>();
        foreach (var (container, kind) in new[] { ("skills", CharacterCreationSkillKinds.Active), ("knoskills", CharacterCreationSkillKinds.Knowledge) })
        {
            var nodes = graph?.Element(container);
            if (nodes is not null && (nodes.HasAttributes || DirectSkillText1949(nodes) || nodes.Elements().Any(node => node.Name != "skill")))
                return Fail("current-skills-saved-container");
            foreach (var node in nodes?.Elements() ?? [])
            {
                if (!SkillShape1949(node, ["guid", "suid", "isknowledge", "skillcategory", "requiresgroundmovement",
                        "requiresswimmovement", "requiresflymovement", "karma", "base", "notes", "notesColor", "name",
                        "buywithkarma", "specs", "type", "isnativelanguage", "disableupgrades"], ["specs"])
                    || !Guid.TryParseExact(SkillScalar1949(node, "guid"), "D", out var id) || id == Guid.Empty || !skillIds.Add(id)
                    || !Guid.TryParseExact(SkillScalar1949(node, "suid"), "D", out var sourceId) || sourceId == Guid.Empty
                    || !ReadSkillBool1949(node, "isknowledge", null, out bool knowledge) || knowledge != (kind == CharacterCreationSkillKinds.Knowledge)
                    || !ReadSkillInt1949(node, "base", 0, out int basePoints) || !ReadSkillInt1949(node, "karma", 0, out int karmaPoints)
                    || !ReadSkillBool1949(node, "isnativelanguage", false, out bool native)
                    || !ReadSkillBool1949(node, "disableupgrades", false, out bool disabled)) return Fail("current-skills-saved-entry");
                var matches = catalog.Rows.Where(row => row.Option.SourceSkillId == sourceId.ToString("D") && row.Option.Kind == kind).ToArray();
                // Native Priority finalization writes active skills by source ID only.
                // The current catalog must still resolve exactly one source row; an
                // explicitly saved name must agree, and Knowledge keeps its name.
                if (matches is not [var row] || row.Option.IsExotic
                    || SkillScalar1949(node, "name") != row.Option.Name
                        && !(UsesNativePriorityCareer9569(current)
                            && kind == CharacterCreationSkillKinds.Active && node.Element("name") is null)
                    || native && !row.Option.CanBeNativeLanguage
                    || disabled && kind == CharacterCreationSkillKinds.Active
                    || !CharacterCareerSkillRatingRules.TryReadFreePoints(root.Element("improvements"), row.Option.Name, false, out var free))
                    return Fail("current-skills-selected-source");
                if (kind == CharacterCreationSkillKinds.Knowledge && node.Element("type") is not null
                    && SkillScalar1949(node, "type") != row.Option.Category) return Fail("current-skills-knowledge-type");
                // The native graph or retained V2 metatype supplies current
                // movement. Saved booleans or the historical talent plan cannot.
                bool movementAvailable = HasCurrentSkillMovement1990(catalog, row.Option);
                if (row.Option.Category == "Pseudo-Magical Active" && !UsesNativePriorityCareer9569(current)
                    || row.Option.Category == "Resonance Active" && (catalog.SelectedEffects.Native?.Imported?.Read.HasPrioritySelection9569 != true
                        || !catalog.SelectedEffects.MagicUnlocks5343.Any(filter => CharacterCreationSkillsAccessRules.Matches(row.Option, filter)))
                    || row.Option.Category == "Magical Active" && !catalog.SelectedEffects.MagicUnlocks5343
                        .Any(filter => CharacterCreationSkillsAccessRules.Matches(row.Option, filter)))
                    return Fail("current-skills-selected-access-unavailable");
                var specs = new List<string>();
                if (node.Element("specs") is { } specContainer)
                {
                    if (specContainer.HasAttributes || DirectSkillText1949(specContainer)) return Fail("current-skills-specialization-state");
                    foreach (var spec in specContainer.Elements())
                    {
                        if (spec.Name != "spec" || !SkillShape1949(spec, ["guid", "name", "free", "expertise"], [])
                            || !ReadSkillBool1949(spec, "free", false, out bool isFree) || isFree
                            || !ReadSkillBool1949(spec, "expertise", false, out bool expertise) || expertise
                            || string.IsNullOrWhiteSpace(SkillScalar1949(spec, "name"))) return Fail("current-skills-specialization-state");
                        specs.Add(SkillScalar1949(spec, "name"));
                    }
                    if (specs.Distinct(StringComparer.Ordinal).Count() != specs.Count) return Fail("current-skills-specialization-duplicate");
                }
                // The native producer also serializes zero-paid, ineligible group
                // members. Only its exact archived row may remain inert here.
                if (!movementAvailable && !IsCurrentInertLifeModuleSkill2058(current, catalog, row, node))
                    return Fail("current-skills-inactive-native-row-unproven");
                skills.Add(new(id, row, node, new(basePoints, karmaPoints, free.Base, free.Levels), native, !disabled, specs,
                    movementAvailable));
                selected.Add(row.Key);
            }
        }
        if (skills.Select(skill => (skill.Source.Option.Kind, skill.Source.Option.SourceSkillId)).Distinct().Count() != skills.Count)
            return Fail("current-skills-source-duplicate");
        var groupRatings = new Dictionary<string, CharacterCareerSkillGroupRating>(StringComparer.Ordinal);
        var groupStates = new List<(XElement Node, Guid Id, string Name, CharacterCareerSkillRatingPoints Points,
            bool Broken, CharacterCreationSkillGroupCatalogEntry Source)>();
        var groups = graph?.Element("groups");
        if (groups is not null && (groups.HasAttributes || DirectSkillText1949(groups) || groups.Elements().Any(node => node.Name != "group")))
            return Fail("current-skills-group-state");
        foreach (var group in groups?.Elements() ?? [])
        {
            if (!SkillShape1949(group, ["id", "name", "base", "karma", "isbroken"], [])
                || !Guid.TryParseExact(SkillScalar1949(group, "id"), "D", out var id) || id == Guid.Empty
                || groupStates.Any(saved => saved.Id == id) || skillIds.Contains(id)
                || !ReadSkillInt1949(group, "base", 0, out int basePoints) || !ReadSkillInt1949(group, "karma", 0, out int karmaPoints)
                || !ReadSkillBool1949(group, "isbroken", false, out bool broken)) return Fail("current-skills-group-state");
            string name = SkillScalar1949(group, "name");
            var definitions = catalog.Authority.SkillGroups.Where(candidate => candidate.Name == name).ToArray();
            if (definitions is not [var definition] || groupRatings.ContainsKey(name)
                || !CharacterCareerSkillRatingRules.TryReadFreePoints(root.Element("improvements"), name, true, out var free))
                return Fail("current-skills-group-source");
            // Native Life Modules writes allocated skills only. A known member
            // omitted by that exact archived request has zero saved points; its
            // group contribution is applied once by the existing rating kernel.
            // These are calculation members, never XML rows or purchasable IDs.
            var memberSources = skills.Where(skill => skill.MovementAvailable && skill.Source.Option.Kind == CharacterCreationSkillKinds.Active
                && skill.Source.Option.SkillGroup == name).Select(skill => skill.Source.Option.SourceSkillId).ToHashSet(StringComparer.Ordinal);
            // Keep the source group complete, but calculate with current movement
            // eligibility for finalized Life Modules and V2. Eligible omissions still
            // require the original native archive proof below.
            var eligibleMemberIds = catalog.PriorityMovement is null && catalog.SelectedEffects.Native?.FinalizedModules is null
                ? definition.MemberSkillSourceIds
                : definition.MemberSkillSourceIds.Where(sourceId => catalog.Rows.Any(row =>
                    row.Option.SourceSkillId == sourceId && HasCurrentSkillMovement1990(catalog, row.Option))).ToArray();
            var archivedSkills = current.Current.Document.AuxiliaryState.CharacterCreationFinalizationArchive?.LifeModuleAuthority?.Parts.Skills;
            // Excluding an inert member from calculations must not permit removal
            // of a row the original producer actually saved.
            if (catalog.SelectedEffects.Native?.FinalizedModules is not null && (archivedSkills is null
                || definition.MemberSkillSourceIds.Except(eligibleMemberIds, StringComparer.Ordinal).Any(sourceId =>
                    archivedSkills.Skills.Any(skill => skill.Allocation.Kind == CharacterCreationSkillKinds.Active
                        && skill.Allocation.SourceSkillId == sourceId)
                    && !skills.Any(skill => skill.Source.Option.Kind == CharacterCreationSkillKinds.Active
                        && skill.Source.Option.SourceSkillId == sourceId))))
                return Fail("current-skills-inactive-native-row-missing");
            foreach (string sourceId in eligibleMemberIds.Where(sourceId => !memberSources.Contains(sourceId)))
            {
                var archive = current.Current.Document.AuxiliaryState.CharacterCreationFinalizationArchive?.LifeModuleAuthority;
                var matches = catalog.Rows.Where(row => row.Option.SourceSkillId == sourceId
                    && row.Option.Kind == CharacterCreationSkillKinds.Active && row.Option.SkillGroup == name).ToArray();
                if (catalog.SelectedEffects.Native?.FinalizedModules is null || archive is null
                    || archive.Parts.Skills.Groups.Count(row => row.Name == name) != 1
                    || archive.Parts.Skills.Skills.Any(row => row.Allocation.SourceSkillId == sourceId)
                    || matches is not [var row] || row.Option.IsExotic
                    || !Guid.TryParseExact(sourceId, "D", out var memberId) || memberId == Guid.Empty
                    || !skillIds.Add(memberId) || groupStates.Any(saved => saved.Id == memberId) || id == memberId
                    || !memberSources.Add(sourceId)
                    || !HasCurrentSkillMovement1990(catalog, row.Option)
                    || row.Option.Category is "Magical Active" or "Pseudo-Magical Active" or "Resonance Active"
                    || !CharacterCareerSkillRatingRules.TryReadFreePoints(root.Element("improvements"), row.Option.Name, false, out var memberFree))
                    return Fail("current-skills-group-omission-unproven");
                skills.Add(new(memberId, row, null, new(0, 0, memberFree.Base, memberFree.Levels), false, true, [], true));
                selected.Add(row.Key);
            }
            var members = skills.Where(skill => skill.MovementAvailable && skill.Source.Option.Kind == CharacterCreationSkillKinds.Active
                && skill.Source.Option.SkillGroup == name).ToArray();
            if (!members.Select(member => member.Source.Option.SourceSkillId).Order(StringComparer.Ordinal)
                    .SequenceEqual(eligibleMemberIds, StringComparer.Ordinal)) return Fail("current-skills-group-members-incomplete");
            var points = new CharacterCareerSkillRatingPoints(basePoints, karmaPoints, free.Base, free.Levels);
            if (!CharacterCareerSkillRatingRules.TryProjectGroup(points, members.Select(member => member.Points).ToArray(),
                    false, catalog.ActiveMaximum, out var rating)) return Fail("current-skills-group-rating");
            groupRatings.Add(name, rating);
            groupStates.Add((group, id, name, points, broken, definition));
            selected.Add("$global");
        }
        foreach (var improvement in root.Element("improvements")?.Elements("improvement") ?? [])
        {
            if (!ReadSkillBool1949(improvement, "enabled", true, out bool enabled) || !enabled
                || SkillScalar1949(improvement, "condition") == "create") continue;
            string type = SkillScalar1949(improvement, "improvementttype"), target = SkillScalar1949(improvement, "improvedname");
            if (type is "SkillBase" or "SkillLevel" && !skills.Any(skill => skill.Source.Option.Name == target)
                || type is "SkillGroupBase" or "SkillGroupLevel" && !groupRatings.ContainsKey(target))
                return Fail("current-skills-unsaved-grant-target");
        }
        var ratings = new Dictionary<Guid, CharacterCareerSkillRating>();
        foreach (var skill in skills.Where(skill => skill.MovementAvailable))
        {
            var definition = skill.Source.Option;
            var group = definition.SkillGroup is { } name ? groupRatings.GetValueOrDefault(name) : null;
            int cap = definition.Kind == CharacterCreationSkillKinds.Active ? catalog.ActiveMaximum : catalog.KnowledgeMaximum;
            if (!CharacterCareerSkillRatingRules.TryProjectSkill(skill.Points, group, catalog.Policy.UsePointsOnBrokenGroups, cap,
                    out var rating)) return Fail("current-skills-rating");
            ratings.Add(skill.Id, rating);
        }
        var activeQuotes = new List<CharacterCareerActiveSkillAdvanceQuote>();
        var knowledgeQuotes = new List<CharacterCareerKnowledgeSkillAdvanceQuote>();
        var groupQuotes = new List<CharacterCareerSkillGroupAdvanceQuote>();
        var groupInputs = new List<CharacterCareerSkillGroupAdvanceInput>();
        var specializationQuotes = new List<CharacterCareerSkillSpecializationQuote>();
        var specializationInputs = new List<CharacterCareerSkillSpecializationInput>();
        var policy = catalog.Policy;
        string rawRules = catalog.SettingsState + "\0" + catalog.Authority.AuthorityDigest;
        if (catalog.PriorityMovement is not null) rawRules += "\0" + catalog.PriorityMovement.Digest;
        foreach (var skill in skills.Where(skill => skill.MovementAvailable))
        {
            var definition = skill.Source.Option;
            int rating = ratings[skill.Id].TotalBaseRating;
            // Catalog identities used only to project omitted members cannot
            // become a saved-skill quote or specialization/mutation target.
            if (skill.Node is null) continue;
            var peers = skills.Where(other => other.MovementAvailable && other.Id != skill.Id && definition.SkillGroup is not null
                && other.Source.Option.SkillGroup == definition.SkillGroup && other.Source.Option.Kind == CharacterCreationSkillKinds.Active).ToArray();
            if (definition.Kind == CharacterCreationSkillKinds.Active)
            {
                var input = new CharacterCareerActiveSkillAdvanceInput(new(skill.Id, Guid.Parse(definition.SourceSkillId)),
                    true, definition.Name, definition.Category, definition.Name, skill.Points.BasePoints, skill.Points.KarmaPoints,
                    rating, catalog.ActiveMaximum, karma,
                    new(policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill, policy.KarmaNewSkillGroup,
                        policy.KarmaImproveSkillGroup, policy.CompensateSkillGroupKarmaDifference),
                    peers.Select(peer => new CharacterCareerActiveSkillGroupMember(peer.Id, ratings[peer.Id].TotalBaseRating, true)).ToArray(),
                    effects.Active.Where(modifier => modifier.Target.Length == 0 || modifier.Target ==
                        (modifier.Kind is CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCost
                            or CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCostMultiplier ? definition.Name : definition.Category)).ToArray(),
                    current.CharacterXml + "\0" + skill.Source.Xml, rawRules);
                if (!CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(input, out var quote)) return Fail("current-skills-active-kernel");
                activeQuotes.Add(quote);
            }
            else
            {
                var input = new CharacterCareerKnowledgeSkillAdvanceInput(new(skill.Id, Guid.Parse(definition.SourceSkillId)),
                    true, "sr5", true, skill.AllowUpgrade, skill.Native, definition.Name, definition.Category, definition.Category,
                    definition.Name, skill.Points.BasePoints, skill.Points.KarmaPoints, rating, catalog.KnowledgeMaximum, karma,
                    new(policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill), effects.Knowledge.Where(modifier =>
                        modifier.Target.Length == 0 || modifier.Target == (modifier.Kind is CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCost
                            or CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMultiplier ? definition.Name : definition.Category)).ToArray(),
                    current.CharacterXml, skill.Source.Xml, rawRules);
                if (!CharacterCareerKnowledgeSkillAdvanceRules.TryCreateQuote(input, out var quote)) return Fail("current-skills-knowledge-kernel");
                knowledgeQuotes.Add(quote);
            }
            // Quote each real retained option and each actually selected custom specialty. No
            // synthetic purchase or invented selection is inserted in current state.
            var selections = skill.Source.Specializations.Options.Select(option => new CharacterCareerSkillSpecializationSelection(
                option.Name, option.Kind, option.OptionIdentity)).Concat(skill.Specializations
                    .Where(name => !skill.Source.Specializations.Options.Any(option => option.Name == name))
                    .Select(name => new CharacterCareerSkillSpecializationSelection(name, CharacterCareerSkillSpecializationOptionKind.Custom, null)));
            var specializationInput = new CharacterCareerSkillSpecializationInput(
                new(skill.Id, Guid.Parse(definition.SourceSkillId), definition.Kind == CharacterCreationSkillKinds.Active
                    ? CharacterCareerSkillKind.Active : CharacterCareerSkillKind.Knowledge),
                true, true, definition.IsExotic, true, skill.AllowUpgrade, skill.Native, definition.Name, definition.Category,
                definition.Name, definition.SkillGroup ?? string.Empty, rating, skill.Specializations.Count, karma,
                definition.SkillGroup is null ? 0 : peers.Length + 1, false, false,
                new(policy.KarmaSpecialization, policy.KarmaKnowledgeSpecialization, policy.SpecializationsBreakSkillGroups),
                effects.Specializations.Where(modifier => modifier.TargetSkillCategory.Length == 0
                    || modifier.TargetSkillCategory == definition.Category).ToArray(),
                Array.AsReadOnly(skill.Source.Specializations.Options.ToArray()),
                new("", CharacterCareerSkillSpecializationOptionKind.Custom, null),
                current.CharacterXml, skill.Source.Specializations.RawSourceState, rawRules)
                { CurrentSpecializations = Array.AsReadOnly(skill.Specializations.ToArray()) };
            specializationInputs.Add(specializationInput);
            foreach (var selection in selections)
            {
                if (!CharacterCareerSkillSpecializationRules.TryCreateQuote(specializationInput with { Selection = selection },
                        out var quote)) return Fail("current-skills-specialization-kernel");
                specializationQuotes.Add(quote);
            }
        }
        foreach (var group in groupStates)
        {
            var members = skills.Where(skill => skill.MovementAvailable && skill.Source.Option.SkillGroup == group.Name
                && skill.Source.Option.Kind == CharacterCreationSkillKinds.Active).ToArray();
            var projection = groupRatings[group.Name];
            // A capped/broken grant transition needs the existing workspace projector; refuse
            // rather than charge from an uncapped or invented group total.
            if (projection.Rating != group.Points.BasePoints + group.Points.KarmaPoints + group.Points.FreeBase + group.Points.FreeLevels)
                return Fail("current-skills-group-transition-unavailable");
            var input = new CharacterCareerSkillGroupAdvanceInput(new(group.Id), true, "sr5", true, true, group.Name,
                group.Points.BasePoints, group.Points.KarmaPoints, catalog.ActiveMaximum, karma, false, group.Broken,
                new(policy.KarmaNewSkillGroup, policy.KarmaImproveSkillGroup), members.Select(member =>
                    new CharacterCareerSkillGroupMember(member.Id, ratings[member.Id].TotalBaseRating, true, member.Source.Option.Category)).ToArray(),
                effects.Groups.Where(modifier => modifier.Target.Length == 0 || (modifier.Kind is CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost
                    or CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier ? modifier.Target == group.Name
                    : members.Any(member => member.Source.Option.Category == modifier.Target))).ToArray(),
                current.CharacterXml + "\0" + CharacterCreationFinalizationDigest.Compute(group.Source), rawRules)
                { FreeRatingPoints = group.Points.FreeBase + group.Points.FreeLevels };
            if (!CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(input, out var quote)) return Fail("current-skills-group-kernel");
            groupQuotes.Add(quote);
            groupInputs.Add(input);
        }
        return new(activeQuotes, knowledgeQuotes, groupQuotes, specializationQuotes,
            skills.Where(skill => skill.MovementAvailable).Select(skill => ratings[skill.Id]).ToArray(),
            Array.AsReadOnly(specializationInputs.ToArray()), Array.AsReadOnly(groupInputs.ToArray()));
        CareerSkills1949? Fail(string reason) { blockers.Add(reason); return null; }
    }

    private static bool IsCurrentInertLifeModuleSkill2058(CharacterCurrentRuleEnvironmentContext current,
        SkillsCatalog1949 catalog, SkillsRow1949 source, XElement saved)
    {
        // CompileCurrentCareerEffects1960 already validated the archive/receipt
        // and recompiled every module source, owner and effect. The archive proves
        // the original allocation only; current source rows and movement still rule.
        var quote = current.Current.Document.AuxiliaryState.CharacterCreationFinalizationArchive?.LifeModuleAuthority?.Parts.Skills;
        if (catalog.SelectedEffects.Native?.FinalizedModules is null || quote is null
            || source.Option.Kind != CharacterCreationSkillKinds.Active
            || quote.Skills.Where(skill => skill.Allocation.SourceSkillId == source.Option.SourceSkillId
                && skill.Allocation.Kind == source.Option.Kind).ToArray() is not [var historical]
            || historical.IsEnabled || historical.Allocation.KarmaLevels != 0 || historical.Allocation.KnowledgePointLevels != 0
            || historical.Allocation.IsNativeLanguage || historical.Allocation.SpecializationOptionId is not null
            || historical.KarmaCost != 0 || historical.SpecializationKarmaCost != 0 || historical.KnowledgePointCost != 0
            || historical.ModuleLevels <= 0 && historical.GroupLevels <= 0) return false;
        // Reuse the producer's exact stable ID and scalar serialization. Comparing
        // the complete instance rejects changed paid points, identity and specialties;
        // only the existing inert notes metadata exception remains available.
        return CharacterCreationPurchasedSkillsLegacyProjector.TryProject(current.Current.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.LifeModuleAuthority!.Parts.SkillsCatalog,
                [(historical.Allocation, historical.Rating, historical.KarmaCost)], [], 0m, quote.QuoteDigest, "life-module",
                out var projected, out _)
            && projected?.Element("skills")?.Elements("skill").SingleOrDefault() is { } expected
            && SameCurrentInstance1960(saved, expected);
    }

    private static SkillsEffects1949? ReadSkillsEffects1949(XElement? improvements, List<string> blockers)
    {
        var active = new List<CharacterCareerActiveSkillKarmaModifier>();
        var knowledge = new List<CharacterCareerKnowledgeSkillKarmaModifier>();
        var groups = new List<CharacterCareerSkillGroupKarmaModifier>();
        var specializations = new List<CharacterCareerSkillSpecializationModifier>();
        if (improvements is not null && (improvements.HasAttributes || DirectSkillText1949(improvements)
            || improvements.Elements().Any(node => node.Name != "improvement"))) return Fail();
        int position = 0;
        foreach (var node in improvements?.Elements() ?? [])
        {
            int ordinal = position++;
            if (!SkillShape1949(node, ["improvementttype", "improvedname", "val", "min", "max", "aug", "augmax",
                    "condition", "enabled", "addtorating", "unique", "uniquename", "target", "exclude", "custom",
                    "sourcename", "improvementsource", "notes", "order", "rating"], [])
                || !ReadSkillBool1949(node, "enabled", true, out bool enabled)) return Fail();
            if (!enabled) continue;
            string type = SkillScalar1949(node, "improvementttype");
            if (type is not ("SkillBase" or "SkillLevel" or "SkillGroupBase" or "SkillGroupLevel")
                && (!CharacterCreationSkillKarmaModifierRules.IsSupportedType(type) || type == "SkillCategoryPointCostMultiplier")) return Fail();
            string condition = SkillScalar1949(node, "condition");
            if (condition is not ("" or "career" or "create")) return Fail();
            if (condition == "create") continue;
            if (new[] { "unique", "uniquename", "target", "exclude" }.Any(name => SkillScalar1949(node, name).Length != 0)
                || !ReadSkillBool1949(node, "addtorating", false, out bool addToRating) || addToRating
                || !ReadSkillInt1949(node, "min", 0, out int minimum) || !ReadSkillInt1949(node, "max", 0, out int maximum)
                || !decimal.TryParse(SkillScalar1949(node, "val"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal value)
                || minimum > 1000 || maximum > 1000 || maximum != 0 && maximum < minimum
                || value is < -CharacterCareerActiveSkillAdvanceRules.MaximumKarma or > CharacterCareerActiveSkillAdvanceRules.MaximumKarma
                || new[] { "aug", "augmax" }.Any(name => node.Element(name) is not null && SkillScalar1949(node, name) != "0")) return Fail();
            string target = SkillScalar1949(node, "improvedname");
            if (target.Length > CharacterCareerActiveSkillAdvanceRules.MaximumNameLength) return Fail();
            string identity = CharacterCreationFinalizationDigest.Compute(new { Position = ordinal,
                Xml = node.ToString(SaveOptions.DisableFormatting) })[7..];
            string lowered = type.Replace("KarmaCost", "Cost", StringComparison.Ordinal);
            switch (type)
            {
                case "SkillBase": case "SkillLevel": case "SkillGroupBase": case "SkillGroupLevel":
                    if (minimum != 0 || maximum != 0 || value < 0 || value > 1000 || decimal.Truncate(value) != value
                        || string.IsNullOrWhiteSpace(target)) return Fail();
                    break; // Canonical free-points reader consumes these for each actual target.
                case "ActiveSkillKarmaCost": case "ActiveSkillKarmaCostMultiplier":
                    active.Add(new(identity, Enum.Parse<CharacterCareerActiveSkillKarmaModifierKind>(lowered), target, minimum, maximum, value)); break;
                case "KnowledgeSkillKarmaCost": case "KnowledgeSkillKarmaCostMultiplier":
                    knowledge.Add(new(identity, Enum.Parse<CharacterCareerKnowledgeSkillKarmaModifierKind>(lowered), target, minimum, maximum, value)); break;
                case "SkillCategoryKarmaCost": case "SkillCategoryKarmaCostMultiplier":
                    active.Add(new(identity, Enum.Parse<CharacterCareerActiveSkillKarmaModifierKind>(lowered), target, minimum, maximum, value));
                    knowledge.Add(new(identity, Enum.Parse<CharacterCareerKnowledgeSkillKarmaModifierKind>(lowered), target, minimum, maximum, value)); break;
                case "SkillGroupKarmaCost": case "SkillGroupKarmaCostMultiplier":
                case "SkillGroupCategoryKarmaCost": case "SkillGroupCategoryKarmaCostMultiplier":
                    groups.Add(new(identity, Enum.Parse<CharacterCareerSkillGroupKarmaModifierKind>(lowered), target, minimum, maximum, value)); break;
                case "SkillCategorySpecializationKarmaCost": case "SkillCategorySpecializationKarmaCostMultiplier":
                    if (maximum != 0) return Fail();
                    specializations.Add(new(identity, Enum.Parse<CharacterCareerSkillSpecializationModifierKind>(type), target, minimum, value)); break;
                default: return Fail(); // Unknown global/mechanic has no proven locality.
            }
        }
        return new(active, knowledge, groups, specializations);
        SkillsEffects1949? Fail() { blockers.Add("current-skills-global-improvement-unsupported"); return null; }
    }

    private static bool ValidSkillsGlobal1949(XElement root)
    {
        if (root.Name != "chummer" || !SkillShape1949(root, ["categories", "skillgroups"], ["categories", "skillgroups"])) return false;
        if (root.Element("categories") is { } categories
            && (categories.HasAttributes || DirectSkillText1949(categories)
                || categories.Elements().Any(node => node.Name != "category" || node.HasElements
                    || node.Attributes().Count() != 1 || node.Attribute("type") is not { } type
                    || !CharacterCreationStandardPrioritySkillsRules.IsSupportedCategory(type.Value, node.Value))
                || categories.Elements().Select(node => node.ToString(SaveOptions.DisableFormatting)).Distinct(StringComparer.Ordinal)
                    .Count() != categories.Elements().Count())) return false;
        return root.Element("skillgroups") is not { } groups || !groups.HasAttributes && !DirectSkillText1949(groups)
            && groups.Elements().All(node => node.Name == "name" && !node.HasElements && !node.HasAttributes
                && !string.IsNullOrWhiteSpace(node.Value) && node.Value == node.Value.Trim())
            && groups.Elements().Select(node => node.Value).Distinct(StringComparer.Ordinal).Count() == groups.Elements().Count();
    }

    private static bool ValidSkillsSelector1949(XElement selector) => selector.DescendantsAndSelf().All(node =>
        node.Name.Namespace == XNamespace.None && new[] { "skill", "id", "name", "hide", "ignoresourcedisabled", "attribute", "category", "default",
            "exotic", "skillgroup", "requiresgroundmovement", "requiresswimmovement", "requiresflymovement", "specs", "spec", "source", "page" }
            .Contains(node.Name.LocalName, StringComparer.Ordinal) && node.Attributes().All(attribute =>
            attribute.Name == "amendoperation" && attribute.Value is "" or "RECURSE" or "REPLACE" or "REMOVE" or "APPEND" or "ADDNODE"
            || (attribute.Name == "addifnotfound" || attribute.Name == "isidnode") && bool.TryParse(attribute.Value, out _)));

    private static XElement ParseSkillsXml1949(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        return XElement.Load(reader);
    }
    private sealed record SelectedSkillEffects1950(string Digest, XElement Improvements,
        IReadOnlyList<CharacterCreationKnowledgeCategoryEffect> Knowledge, CurrentCareerEffects1960? Native,
        CharacterCreationMetatypeMovementProjection? Movement = null)
    {
        public IReadOnlyList<string> MagicUnlocks5343 { get; init; } = [];
    }

    // Call only on compiler output after complete source/owner/effect reconciliation.
    // Named, conditional and unknown unlock forms retain their existing refusal.
    private static IReadOnlyList<string> CompiledMagicUnlocks5343(IEnumerable<XElement> effects, bool importedPriority5699 = false)
        => Array.AsReadOnly(effects.Where(effect => SkillScalar1949(effect, "improvementttype") == "SpecialSkills"
                && ReadSkillSwitch1950(effect, "enabled", true, out bool enabled) && enabled
                && SkillScalar1949(effect, "condition").Length == 0
                && SkillScalar1949(effect, "target").Length == 0)
            .Select(effect => SkillScalar1949(effect, "improvedname"))
            .Where(filter => filter is "Magician" or "Sorcery" or "Conjuring" or "Enchanting"
                or "Adept" or "Aware" or "Explorer" or "Spellcasting" || importedPriority5699 && filter == "Technomancer")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());

    private static bool UsesPrioritySkillMovement1990(CharacterCurrentRuleEnvironmentContext current, XElement root)
        => (current.SemanticsProfile == CurrentCharacterRuleSourceSemantics.ProfileV2
                && current.SemanticsFingerprint == CurrentCharacterRuleSourceSemantics.FingerprintV2
                || UsesPriorityCurrent3027(current))
            && current.Current.Document.RulesetId == "sr5"
            && SkillScalar1949(root, "buildmethod") is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen;

    private static PrioritySkillMovement1990? ReadPrioritySkillMovement1990(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement root, CharacterCreationKarmaSkillsPolicy policy)
    {
        // The saved identity selects a typed current-source option. Saved
        // movement can corroborate that option, never issue movement access.
        if (!current.IsUnchanged || !UsesPrioritySkillMovement1990(current, root)
            || policy.Schema != CharacterCreationKarmaSkillsPolicy.PriorityPurchaseSchemaV1
            || policy.AuthorityDigest != CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy)
            || new[] { "ignorerules", "ai", "critter", "critterenabled", "iscritter" }.Any(field =>
                root.Elements(field).Count() > 1 || !ReadSkillSwitch1950(root, field, false, out bool enabled) || enabled)
            || new[] { "drugs", "critterpowers", "sustainedspells" }.Any(field =>
                root.Elements(field).Any(node => node.HasElements || node.HasAttributes || DirectSkillText1949(node)))
            || new[] { "metatypeguid", "metatype", "metavariantguid", "metavariant", "walk", "run", "sprint" }
                .Any(field => root.Elements(field).Count() > 1 || root.Element(field) is { } node
                    && (node.HasAttributes || node.HasElements))
            || !Guid.TryParseExact(SkillScalar1949(root, "metatypeguid"), "D", out var id) || id == Guid.Empty
            || SkillScalar1949(root, "metavariant") is not ("" or "None")
            || SkillScalar1949(root, "metavariantguid") is { Length: > 0 } variant && variant != Guid.Empty.ToString("D")
            || new[] { "movement", "init", "initiative", "reach", "armor" }.Any(field =>
                root.Elements(field).Any(node => node.HasElements || node.HasAttributes || !string.IsNullOrWhiteSpace(node.Value)))
            || !source.TryResolveCreationPrerequisiteAuthority(out var prerequisite) || !prerequisite.IsAuthoritative
            || prerequisite.Blockers.Count != 0 || prerequisite.BuildMethod != SkillScalar1949(root, "buildmethod")
            || prerequisite.PriorityTable != "Standard" || prerequisite.SettingsProfileId != policy.SettingsProfileId
            || prerequisite.RawProfileInputsDigest != policy.RawProfileInputsDigest
            || prerequisite.AuthorityDigest != CharacterCreationPrerequisiteAuthorityDigest.Compute(prerequisite)
            || !source.TryResolveCreationMetatypeCatalog(out var catalog) || !catalog.IsAuthoritative
            || catalog.Blockers.Count != 0 || !catalog.SourceContext.IsAuthoritative || catalog.SourceContext.Blockers.Count != 0
            || catalog.SourceContext.SettingsProfileId != policy.SettingsProfileId
            || catalog.SourceContext.RawProfileInputsDigest != policy.RawProfileInputsDigest
            || catalog.SourceContext.RawMetatypesXmlDigest != prerequisite.RawMetatypesXmlDigest
            || catalog.SourceContext.EffectiveMetatypesInputsDigest != prerequisite.EffectiveMetatypesInputsDigest
            || catalog.SourceContext.SelectedCustomDataInputsDigest != prerequisite.SelectedCustomDataInputsDigest
            || catalog.Options.Where(option => option.OptionId == id.ToString("D")
                && option.Label == SkillScalar1949(root, "metatype") && option.IsEnabled && option.Blockers.Count == 0)
                .ToArray() is not [var metatype]
            || metatype.Movement.IsSpecial || !CurrentMetatypeScalars1950(root, metatype)) return null;
        // Registration is admitted only through 5064's full authenticated sheet
        // graph. Sustaining changes pools, never movement rates or purchase cost.
        // Keep the other exclusions: their semantics have not been reconciled.
        if (!TrySustainedMovement5080(current, root, out string? sustainedAuthority)) return null;
        var rates = new[] { metatype.Movement.Walk, metatype.Movement.Run, metatype.Movement.Sprint };
        if (rates.Any(rate => rate.Ground < 0 || rate.Swim < 0 || rate.Fly < 0) || !current.IsUnchanged) return null;
        return new(metatype.Movement, CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.current-priority-skill-movement.v1", current.BindingDigest,
            Profile = policy.AuthorityDigest, Prerequisite = prerequisite.AuthorityDigest,
            catalog.SourceContext, Metatype = metatype, SustainedAuthority = sustainedAuthority
        }));
    }

    internal static bool TrySustainedMovement5080(CharacterCurrentRuleEnvironmentContext current, XElement root,
        out string? authority)
    {
        authority = null;
        var containers = root.Elements("sustainedobjects").ToArray();
        if (containers.Length == 0 || containers is [var empty] && !empty.HasAttributes
            && !empty.HasElements && string.IsNullOrWhiteSpace(empty.Value)) return true;
        try
        {
            var resolution = current.Capture.CreateCurrentEnvironmentResolution(current.SemanticsProfile, current.SemanticsFingerprint);
            if (resolution is null || !current.IsUnchanged) return false;
            var sheet = UsesOwnedPriorityCareer9569(current)
                ? CharacterSheetDerivedQuery.ParseImportedCareer5688(current).Sheet
                : CharacterSheetDerivedQuery.ParseWithPriorityGrants3563(current.CharacterXml, resolution.Resolver,
                    CharacterCreationPriorityGrantAuthority3563.Capture(current.Current, resolution.Resolver));
            if (sheet.UnavailableReason is not null || sheet.AuthorityDigest is null
                || sheet.SustainedSpellPreparation is not { } preparation || !current.IsUnchanged) return false;
            authority = CharacterCreationFinalizationDigest.Compute(new
                { sheet.AuthorityDigest, preparation.Objects, preparation.PenalizedObjectCount, preparation.DicePoolPenalty });
            return true;
        }
        catch (Exception error) when (error is XmlException or InvalidOperationException or ArgumentException
            or IOException or UnauthorizedAccessException or FormatException or OverflowException) { return false; }
    }

    private static bool HasCurrentSkillMovement1990(SkillsCatalog1949 catalog, CharacterCreationSkillCatalogEntry skill)
    {
        if (catalog.PriorityMovement is not { } priority)
            return HasCurrentSkillMovement1965(catalog.SelectedEffects.Movement
                ?? catalog.SelectedEffects.Native?.Metatype.Movement, skill);
        var rates = new[] { priority.Movement.Walk, priority.Movement.Run, priority.Movement.Sprint };
        return !priority.Movement.IsSpecial && rates.All(rate => rate.Ground >= 0 && rate.Swim >= 0 && rate.Fly >= 0)
            && (!skill.RequiresGroundMovement || rates.Any(rate => rate.Ground > 0))
            && (!skill.RequiresSwimMovement || rates.Any(rate => rate.Swim > 0))
            && (!skill.RequiresFlyMovement || rates.Any(rate => rate.Fly > 0));
    }

    private static bool HasCurrentSkillMovement1965(CharacterCreationMetatypeMovementProjection? movement,
        CharacterCreationSkillCatalogEntry skill)
    {
        if (!skill.RequiresGroundMovement && !skill.RequiresSwimMovement && !skill.RequiresFlyMovement) return true;
        if (movement is null || movement.IsSpecial) return false;
        var rates = new[] { movement.Walk, movement.Run, movement.Sprint };
        return rates.All(rate => rate.Ground >= 0 && rate.Swim >= 0 && rate.Fly >= 0)
            && (!skill.RequiresGroundMovement || rates.Any(rate => rate.Ground > 0))
            && (!skill.RequiresSwimMovement || rates.Any(rate => rate.Swim > 0))
            && (!skill.RequiresFlyMovement || rates.Any(rate => rate.Fly > 0));
    }

    private static SelectedSkillEffects1950? CompileSelectedSkillEffects1950(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source,
        XElement root, List<string> blockers)
    {
        if (UsesOwnedPriorityCareer9569(current))
        {
            var imported = CompileCurrentCareerEffects1960(current, source, blockers);
            return imported?.Imported is null ? null : ImportedSkillCosts5688(imported, blockers);
        }
        if (current.Stage == CharacterCurrentRuleStage.Career
            && !CharacterCareerMartialAcquisitionTransaction.TryProjectCurrentMartialEffects2438(root, source, out root))
            return Fail("martial-effects-unresolved");
        // The shared owner compiler closes ordinary racial/generated-quality effects,
        // including suppression. Its numeric output is rebuilt from current sources.
        // Keep finalized Life Module free ranks on their existing archive-backed path.
        if (TryReadNativeSkillCosts2559(current, source, root, blockers, out bool nativeCosts, out var ownedCosts))
            return ownedCosts;
        if (nativeCosts) return null;
        CurrentCareerEffects1960? native = null;
        var moduleImprovements = new XElement("improvements");
        var moduleOutputs = new List<object>();
        if (UsesNativeMethods1958(current) && current.Stage == CharacterCurrentRuleStage.Career)
        {
            native = CompileCurrentCareerEffects1960(current, source, blockers);
            if (native is null) return null;
            root = new XElement(native.Graph);
            // Join by actual owner and source, never by catalog membership or a
            // saved LifeModule label. Keep every effect in the typed provenance;
            // only the rank/price projection belongs in the skill kernels.
            foreach (var module in native.FinalizedModuleOwners)
            {
                if (!CompileFinalizedModuleSkillEffects1965(module, moduleImprovements, moduleOutputs))
                    return Fail("finalized-module-skill-effect-unsupported");
                var selected = root.Elements("qualities").Elements("quality").Single(row =>
                    ScalarCreation1949(row, "guid") == module.OwnerId.ToString("D"));
                if (FindCurrentFinalizedModuleOwner1965(native, new(module.OwnerId,
                        Guid.Parse(ScalarCreation1949(selected, "sourceid")))) != module)
                    return Fail("finalized-module-owner-mismatch");
                selected.Remove();
                root.Elements("improvements").Elements().Where(row =>
                    ScalarCreation1949(row, "sourcename") == module.OwnerId.ToString("D")).Remove();
            }
            var grants = root.Elements("qualities").Elements("quality")
                .Where(row => native.NativeQualityIds.Contains(ScalarCreation1949(row, "sourceid"), StringComparer.Ordinal)).ToArray();
            var owners = grants.Select(row => ScalarCreation1949(row, "guid")).ToHashSet(StringComparer.Ordinal);
            // Native talent/racial effects are already compiled and reconciled.
            // Only their supported ordinary cost rows enter the rank/price kernels.
            var nativeEffects = root.Elements("improvements").Elements()
                .Where(row => owners.Contains(ScalarCreation1949(row, "sourcename"))
                    || ScalarCreation1949(row, "improvementsource") == "Metatype").ToArray();
            foreach (var effect in nativeEffects.Where(row => CharacterCreationSkillKarmaModifierRules.IsSupportedType(
                         ScalarCreation1949(row, "improvementttype"))))
            {
                if (!TryAppendNativeSkillCost2559(effect, moduleImprovements))
                    return Fail("native-grant-rank-price-mechanic-unsupported");
            }
            nativeEffects.Remove(); grants.Remove();
            root.Elements("gears").Elements("gear").Where(row => native.GrantedGearIds.Contains(
                ScalarCreation1949(row, "guid"), StringComparer.Ordinal)).Remove();
        }
        // Persisted improvements are projections, never evidence of current source effects.
        if (root.Elements("improvements").Count() > 1) return Fail("saved-improvements-duplicate");
        var saved = root.Element("improvements");
        if (saved is not null && (saved.HasAttributes || DirectSkillText1949(saved)
            || saved.Elements().Any(node => node.Name != "improvement"))) return Fail("saved-improvements-shape");
        foreach (var effect in saved?.Elements() ?? [])
        {
            string type = SkillScalar1949(effect, "improvementttype");
            // Free ranks, unknown and custom improvements need their own source compiler.
            if ((!CharacterCreationSkillKarmaModifierRules.IsSupportedType(type) && SkillIndependentDomain1950(type) is null)
                || !ReadSkillSwitch1950(effect, "custom", false, out bool custom) || custom)
                return Fail("saved-improvement-source-unsupported");
        }
        foreach (string unsupported in new[] { "cyberwares", "biowares", "martialarts", "foci", "stackedfoci" })
            if (root.Elements(unsupported).Any(node => node.HasElements || node.HasAttributes || DirectSkillText1949(node)))
                return Fail("selected-" + unsupported + "-unsupported");
        var improvements = new XElement(moduleImprovements);
        var knowledge = new List<CharacterCreationKnowledgeCategoryEffect>();
        var consumed = new List<object>(moduleOutputs);
        var instanceIds = new HashSet<Guid>();
        var qualityLevels = new Dictionary<Guid, int>();
        var effectReader = source as ICharacterCurrentSkillEffectSource;
        foreach (var (kind, rowName) in new[] { ("qualities", "quality"), ("powers", "power"),
                     ("gears", "gear"), ("weapons", "weapon"), ("armors", "armor"),
                     ("spells", "spell"), ("complexforms", "complexform") })
        {
            if (root.Elements(kind).Count() > 1) return Fail("selected-container-duplicate");
            var container = root.Element(kind);
            if (container is null) continue;
            if (container.HasAttributes || DirectSkillText1949(container)
                || container.Elements().Any(node => node.Name != rowName)) return Fail("selected-container-shape");
            foreach (var selected in container.Elements())
                if (!CompileOne(kind, rowName, selected, 0, true)) return null;
        }
        if (!current.IsUnchanged) return Fail("current-changed");
        string digest = CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.current-selected-skill-effects.v1", current.BindingDigest,
            Sources = consumed, Effects = improvements.ToString(SaveOptions.DisableFormatting), Knowledge = knowledge
        });
        if (native is not null) digest = CharacterCreationFinalizationDigest.Compute(new { SkillEffects = digest, NativeSourceGraph = native.Digest });
        return new(digest, improvements, Array.AsReadOnly(knowledge.ToArray()), native)
        {
            MagicUnlocks5343 = native is null ? []
                : CompiledMagicUnlocks5343(native.Graph.Elements("improvements").Elements())
        };

        bool CompileOne(string kind, string rowName, XElement selected, int depth, bool parentActive)
        {
            if (depth > 16 || instanceIds.Count >= 2048 || selected.Name != rowName || selected.HasAttributes
                || DirectSkillText1949(selected)
                || !Guid.TryParseExact(SkillScalar1949(selected, "guid"), "D", out var instanceId)
                || instanceId == Guid.Empty || !instanceIds.Add(instanceId)
                || !Guid.TryParseExact(SkillScalar1949(selected, "sourceid"), "D", out var sourceId)
                || sourceId == Guid.Empty || SkillScalar1949(selected, "extra").Length != 0
                || effectReader is null
                || !effectReader.TryReadCurrentSkillEffectSource(kind, sourceId, out string xml, out string sourceState)
                || string.IsNullOrEmpty(sourceState)) return Reject("selected-source-identity");
            var row = ParseSkillsXml1949(xml);
            // The retained source ID owns the selected effect. A source update can
            // rename its display label without rewriting the saved selection.
            // Still require canonical, nonempty names on both sides; neither name
            // substitutes for the exact ID and source-state checks above.
            if (row.Name != rowName || SkillScalar1949(row, "id") != sourceId.ToString("D")
                || string.IsNullOrWhiteSpace(SkillScalar1949(row, "name"))
                || string.IsNullOrWhiteSpace(SkillScalar1949(selected, "name"))) return Reject("selected-source-mismatch");
            string[] alternateEffects = ["wirelessbonus", "wirelesspairbonus", "pairbonus", "select", "selectskill",
                "selectattribute", "selecttext", "selectweapon", "selectarmor", "selectgear", "addqualities",
                "addpowers", "addweapon", "weapons", "armors", "gears", "cyberwares", "biowares"];
            if (row.Elements().Any(node => alternateEffects.Contains(node.Name.LocalName, StringComparer.Ordinal)
                    && (node.HasElements || node.HasAttributes || !string.IsNullOrWhiteSpace(node.Value)))
                || selected.Elements().Any(node => new[] { "accessories", "mods", "cyberwares", "biowares", "weapons", "armors" }
                    .Contains(node.Name.LocalName, StringComparer.Ordinal) && node.HasElements)
                || SkillScalar1949(selected, "forcedvalue").Length != 0
                || SkillScalar1949(selected, "disabledby").Length != 0)
                return Reject("selected-variant-unsupported");
            int level = 1;
            if (kind == "qualities")
            {
                qualityLevels.TryGetValue(sourceId, out int previous);
                qualityLevels[sourceId] = level = checked(previous + 1);
                if (!ReadSkillInt1949(selected, "rating", 1, out int rating) || rating != 1)
                    return Reject("selected-quality-rating-shape");
            }
            else if (row.Element("firstlevelbonus")?.HasElements == true)
                return Reject("selected-first-level-unsupported");
            if (!CharacterCreationLegacySourceProjector.TryCompileCurrentSkillEffects(row,
                    kind == "qualities" && level == 1, out var compiled)) return Reject("selected-effect-unsupported");
            bool active = parentActive;
            if (kind is "gears" or "weapons" or "armors")
            {
                bool affectsChildren = selected.Elements().Any(node => (node.Name == "children" || node.Name == "gears") && node.HasElements);
                if (!ReadSkillSwitch1950(selected, "equipped", compiled.Count == 0 && !affectsChildren ? true : null, out bool equipped))
                    return Reject("selected-equipment-activation");
                active &= equipped;
            }
            if ((kind is "spells" or "complexforms") && compiled.Count > 0)
                return Reject("selected-spell-activation-unsupported");
            string nodeDigest = CharacterCreationSkillsDigest.ComputeUtf8(xml);
            var handoffs = new List<string>();
            foreach (var effect in compiled)
            {
                if (!CharacterCreationKnowledgeCategoryRules.TryLowerNativeCondition(effect.Condition, out string condition))
                    return Reject("selected-effect-condition");
                if (!CharacterCreationSkillKarmaModifierRules.IsSupportedType(effect.Type))
                {
                    string? domain = SkillIndependentDomain1950(effect.Type);
                    if (domain is null) return Reject("selected-effect-locality-unsupported");
                    // Enumerate every compiler output outside these rank/price kernels.
                    // This is not evidence that its separate domain has executed.
                    handoffs.Add(domain + ":" + effect.Type);
                    continue;
                }
                if (!active || condition == "career" && current.Stage == CharacterCurrentRuleStage.Creation
                    || condition == "create" && current.Stage == CharacterCurrentRuleStage.Career) continue;
                string file = kind switch { "gears" => "gear.xml", "armors" => "armor.xml", _ => kind + ".xml" };
                string anchor = file + "#id:" + sourceId.ToString("D");
                if (current.Stage == CharacterCurrentRuleStage.Creation)
                {
                    if (CharacterCreationKnowledgeCategoryRules.IsSupportedType(effect.Type))
                        knowledge.Add(new(effect.Type, effect.Target, effect.Value, effect.Minimum, effect.Maximum,
                            effect.Condition, sourceId, nodeDigest, kind + "/id:" + sourceId.ToString("D"),
                            level, effect.Container, effect.Position, [anchor]));
                    // This allocation kernel contains no personal Karma purchases. Those
                    // remain the responsibility of the separate Creation purchase consumer.
                    continue;
                }
                if (effect.Type == "SkillCategoryPointCostMultiplier") continue; // Creation points only.
                improvements.Add(new XElement("improvement", new XElement("improvementttype", effect.Type),
                    new XElement("improvedname", effect.Target), new XElement("val", effect.Value.ToString(CultureInfo.InvariantCulture)),
                    new XElement("min", effect.Minimum), new XElement("max", effect.Maximum),
                    new XElement("enabled", "True"), new XElement("sourcename", instanceId.ToString("D"))));
            }
            consumed.Add(new { Kind = kind, SourceId = sourceId, InstanceId = instanceId, Level = level,
                State = sourceState, Active = active, Compiled = compiled, OtherDomainOutputs = handoffs });
            foreach (string nested in new[] { "children", "gears" })
            {
                if (selected.Elements(nested).Count() > 1) return Reject("selected-nested-duplicate");
                if (selected.Element(nested) is not { } children) continue;
                if (children.HasAttributes || DirectSkillText1949(children)
                    || children.Elements().Any(node => node.Name != "gear")) return Reject("selected-nested-shape");
                foreach (var child in children.Elements())
                    if (!CompileOne("gears", "gear", child, depth + 1, active)) return false;
            }
            return true;
        }
        bool Reject(string reason) { Fail(reason); return false; }
        SelectedSkillEffects1950? Fail(string reason)
        { blockers.Add("current-skills-" + reason); return null; }
    }

    private static bool TryReadNativeSkillCosts2559(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement root, List<string> blockers,
        out bool applies, out SelectedSkillEffects1950? result)
    {
        result = null;
        applies = root.Elements("qualities").Elements("quality").Any(q =>
            SkillScalar1949(q, "qualitysource") is "Metatype" or "MetatypeRemovable" or "Improvement")
            || root.Elements("improvements").Elements().Any(e =>
                SkillScalar1949(e, "improvementsource") == "Metatype"
                || SkillScalar1949(e, "improvementttype") is "SpecificQuality" or "DisableQuality");
        if (!applies && root.Element("metatype") is null && root.Element("metatypeguid") is null
            && root.Element("metatypeid") is null) return false;
        // The immutable Life Module archive owns its generated ranks and paid intervals.
        if (SkillScalar1949(root, "buildmethod") == CharacterCreationBuildMethods.LifeModules)
        { applies = false; return false; }
        try
        {
            if (source is not ICharacterSheetOwnerSourceContext
                || !CharacterSheetDerivedQuery.TryMetatype(root, source, out string metatypeXml,
                    out string metatypeState, out Guid metatypeOwner))
            {
                if (applies) blockers.Add("current-skills-native-owner-source-unavailable");
                return false;
            }
            var metatype = ParseSkillsXml1949(metatypeXml);
            applies |= new[] { "qualities", "powers", "bonus" }.Any(name => metatype.Element(name)?.HasElements == true);
            if (!applies) return false;
            if (new[] { "ignorerules", "ai" }.Any(flag => !ReadSkillSwitch1950(root, flag, false, out bool on) || on)
                || root.Elements("qualities").Elements("quality").Any(q =>
                    !ReadSkillInt1949(q, "rating", 1, out int level) || level != 1)) return Fail();
            if (!source.TryResolveCareerSettingsProfile(out string settingsXml, out string settingsState)
                || string.IsNullOrEmpty(settingsState)) return Fail();
            var checks = new List<Func<bool>>();
            var owners = CharacterSheetOwnerGraph.Resolve(root, source, checks, metatype,
                ParseSkillsXml1949(settingsXml), metatypeOwner, out _);
            // Movement has its own current-source projection. Do not send the broader
            // owner graph back through the older native grant compiler just to obtain
            // movement, or infer a selected variant's rates from its parent metatype.
            CharacterCreationMetatypeMovementProjection? movement = null;
            string? movementCatalogDigest = null;
            if (UsesNativeMethods1958(current) && current.Stage == CharacterCurrentRuleStage.Career
                && source.TryResolveCreationMetatypeCatalog(out var movementCatalog)
                && movementCatalog.IsAuthoritative && movementCatalog.Blockers.Count == 0
                && movementCatalog.SourceContext.IsAuthoritative && movementCatalog.SourceContext.Blockers.Count == 0
                && movementCatalog.SourceContext.SettingsProfileId == SkillScalar1949(root, "settings")
                && movementCatalog.Options.Where(option => option.OptionId == SkillScalar1949(metatype, "id")
                    && option.Label == SkillScalar1949(metatype, "name") && option.IsEnabled && option.Blockers.Count == 0)
                    .ToArray() is [var movementMetatype])
            {
                movement = movementMetatype.Movement;
                movementCatalogDigest = CharacterCreationFinalizationDigest.Compute(movementCatalog);
                checks.Add(() => source.TryResolveCreationMetatypeCatalog(out var finalCatalog)
                    && CharacterCreationFinalizationDigest.Compute(finalCatalog) == movementCatalogDigest);
            }
            if (!TryProjectOwnedSkillCosts5688(owners, out var costs)) return Fail();
            if (!checks.All(check => check()) || !current.IsUnchanged
                || !source.TryResolveCareerSettingsProfile(out string finalSettings, out string finalSettingsState)
                || settingsXml != finalSettings || settingsState != finalSettingsState
                || !CharacterSheetDerivedQuery.TryMetatype(root, source, out string finalMetatype,
                    out string finalMetatypeState, out Guid finalOwner)
                || metatypeXml != finalMetatype || metatypeState != finalMetatypeState || metatypeOwner != finalOwner)
                return Fail();
            string digest = CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = "chummer.current-native-skill-costs.v1", current.BindingDigest,
                metatypeState, settingsState, owners.AuthorityDigest,
                Effects = costs.ToString(SaveOptions.DisableFormatting)
            });
            if (movementCatalogDigest is not null)
                digest = CharacterCreationFinalizationDigest.Compute(new { NativeCosts = digest, MovementCatalog = movementCatalogDigest });
            result = new(digest, costs, [], null, movement)
                { MagicUnlocks5343 = CompiledMagicUnlocks5343(owners.Effects) };
            return true;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
                                     or IOException or InvalidDataException or OverflowException or FormatException)
        { return Fail(); }
        bool Fail() { blockers.Add("current-skills-native-owner-or-cost-unsupported"); return false; }
    }

    private static bool TryProjectOwnedSkillCosts5688(CharacterSheetOwnerGraph owners, out XElement costs, bool importedPriorityRanks5699 = false)
    {
        costs = new XElement("improvements");
        foreach (var effect in owners.Effects)
        {
            string type = SkillScalar1949(effect, "improvementttype");
            if (CharacterCreationSkillKarmaModifierRules.IsSupportedType(type))
            {
                if (!TryAppendNativeSkillCost2559(effect, costs)) return false;
            }
            else if (importedPriorityRanks5699 && (type is "SkillBase" or "SkillLevel" or "SkillGroupBase" or "SkillGroupLevel"))
            {
                // The owner graph has already matched all grants to selected sources.
                // Reuse the existing strict rank reader after metadata normalization.
                var rank = new XElement(effect);
                foreach (string name in new[] { "customname", "customid", "customgroup" })
                {
                    if (SkillScalar1949(rank, name).Length != 0) return false;
                    rank.Elements(name).Remove();
                }
                rank.Elements("notesColor").Remove();
                foreach (string name in new[] { "enabled", "custom", "addtorating" })
                {
                    if (!ReadSkillSwitch1950(rank, name, name == "enabled", out bool value)) return false;
                    rank.SetElementValue(name, value ? "True" : "False");
                }
                costs.Add(rank);
            }
            // Dice-pool/defaulting effects do not change purchased ranks. Free ranks,
            // skill disabling, specialization blocks and rating bonuses do.
            else if (type is "SkillBase" or "SkillLevel" or "SkillGroupBase" or "SkillGroupLevel"
                or "SkillGroupCategoryDisable" or "BlockSkillSpecializations" or "BlockSkillCategorySpecializations"
                or "FreeKnowledgeSkills" or "WalkSpeed" or "RunSpeed" or "SprintSpeed" or "SprintBonus"
                || !ReadSkillSwitch1950(effect, "addtorating", false, out bool rating) || rating)
                return false;
        }
        // No personal Karma purchase is performed by the Creation allocation reader.
        // Native point-pool modifiers remain guarded until their allocation adapter exists.
        if (owners.Effects.Any(e => SkillScalar1949(e, "improvementttype") == "SkillCategoryPointCostMultiplier"))
            return false;
        return true;
    }

    private static bool TryAppendNativeSkillCost2559(XElement compiled, XElement costs)
    {
        string type = SkillScalar1949(compiled, "improvementttype");
        if (!CharacterCreationSkillKarmaModifierRules.IsSupportedType(type)
            || type == "SkillCategoryPointCostMultiplier"
            || !ReadSkillInt1949(compiled, "min", 0, out int minimum) || minimum != 0
            || !ReadSkillInt1949(compiled, "max", 0, out int maximum) || maximum != 0
            || SkillScalar1949(compiled, "condition").Length != 0
            || new[] { "target", "exclude", "unique", "uniquename", "customname", "customid", "customgroup" }
                .Any(field => SkillScalar1949(compiled, field).Length != 0)
            || !ReadSkillSwitch1950(compiled, "custom", false, out bool custom) || custom
            || !ReadSkillSwitch1950(compiled, "addtorating", false, out bool rating) || rating
            || !ReadSkillSwitch1950(compiled, "enabled", true, out bool enabled)) return false;
        // Project the complete supported scalar grammar; all values came from a source
        // compiler, never from the saved row passed to the ordinary current reader.
        costs.Add(new XElement("improvement", new XElement("improvementttype", type),
            new XElement("improvedname", SkillScalar1949(compiled, "improvedname")),
            new XElement("val", SkillScalar1949(compiled, "val")),
            new XElement("min", minimum), new XElement("max", maximum),
            new XElement("enabled", enabled ? "True" : "False"),
            new XElement("sourcename", SkillScalar1949(compiled, "sourcename"))));
        return true;
    }

    private static bool CompileFinalizedModuleSkillEffects1965(CurrentFinalizedModuleOwner1965 owner,
        XElement improvements, List<object> consumed)
    {
        var handoffs = new List<string>();
        foreach (string xml in owner.ImprovementXml)
        {
            var effect = ParseSkillsXml1949(xml);
            string type = SkillScalar1949(effect, "improvementttype");
            bool rank = type is "SkillBase" or "SkillLevel" or "SkillGroupBase" or "SkillGroupLevel";
            if (!rank && (!CharacterCreationSkillKarmaModifierRules.IsSupportedType(type)
                    || type == "SkillCategoryPointCostMultiplier"))
            {
                string? domain = type switch
                {
                    "Attributelevel" => "attributes",
                    // Native finalization spends the knowledge pool into saved
                    // base ranks. Reapplying the allowance as levels would double it.
                    "FreeKnowledgeSkills" or "SkillCategoryPointCostMultiplier" => "creation-knowledge-allocation",
                    "QualityLevel" or "SpecificQuality" or "FreePositiveQualities" or "FreeNegativeQualities" => "module-qualities",
                    "SkillCategory" => "skill-dice-pools",
                    "BlockSkillCategoryDefault" => "skill-defaulting",
                    "Notoriety" => "reputation",
                    "DamageResistance" => "damage-resistance",
                    "NuyenMaxBP" => "creation-resources",
                    _ => SkillIndependentDomain1950(type)
                };
                // In particular SkillGroupCategoryDisable changes advancement
                // eligibility and cannot be treated as a harmless pool modifier.
                if (domain is null) return false;
                handoffs.Add(domain + ":" + type);
                continue;
            }
            // The native graph has been matched in full, including owner, parent,
            // condition, value and rating. Normalize only legacy scalar spelling
            // and inert metadata for the existing strict rank/price reader.
            foreach (string field in new[] { "customname", "customid", "customgroup" })
            {
                if (SkillScalar1949(effect, field).Length != 0) return false;
                effect.Elements(field).Remove();
            }
            effect.Elements("notesColor").Remove();
            foreach (string field in new[] { "enabled", "custom", "addtorating" })
            {
                if (!ReadSkillSwitch1950(effect, field, field == "enabled", out bool value)) return false;
                effect.SetElementValue(field, value ? "True" : "False");
            }
            improvements.Add(effect);
        }
        consumed.Add(new { FinalizedModuleOwner = owner, OtherDomainOutputs = handoffs });
        return true;
    }

    private static string? SkillIndependentDomain1950(string type) => type switch
    {
        "Ambidextrous" or "FriendsInHighPlaces" or "Erased" or "Overclocker" => "qualities-and-talents",
        "LifestyleCost" or "BasicLifestyleCost" or "TrustFund" => "lifestyles",
        "Initiative" or "InitiativeDice" or "InitiativeDiceAdd" => "physical-initiative",
        "MatrixInitiative" or "MatrixInitiativeDice" or "MatrixInitiativeDiceAdd" => "matrix-initiative",
        _ => null
    };

    private static bool ReadSkillSwitch1950(XElement node, string name, bool? fallback, out bool value)
    {
        value = fallback ?? false;
        if (node.Element(name) is null) return fallback.HasValue;
        string text = SkillScalar1949(node, name);
        if (text is "0" or "1") { value = text == "1"; return true; }
        return bool.TryParse(text, out value);
    }

    private static XElement SkillsEvaluationGraph1950(SkillsCatalog1949 catalog, XElement original)
    {
        var graph = new XElement(original);
        graph.Elements("improvements").Remove();
        graph.Add(new XElement(catalog.SelectedEffects.Improvements));
        return graph;
    }

    private static bool DirectSkillText1949(XElement node) => node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value));
    private static bool SkillShape1949(XElement node, IReadOnlyList<string> fields, IReadOnlyList<string> structured)
        => !node.HasAttributes && !DirectSkillText1949(node)
           && node.Elements().All(child => child.Name.Namespace == XNamespace.None && fields.Contains(child.Name.LocalName)
               && (structured.Contains(child.Name.LocalName) || !child.HasAttributes && !child.HasElements && child.Value == child.Value.Trim()))
           && node.Elements().GroupBy(child => child.Name).All(group => group.Count() == 1);
    private static string SkillScalar1949(XElement node, string name)
    {
        var matches = node.Elements(name).Take(2).ToArray();
        if (matches.Length == 0) return string.Empty;
        if (matches.Length != 1 || matches[0].HasAttributes || matches[0].HasElements || matches[0].Value != matches[0].Value.Trim())
            throw new InvalidOperationException("Noncanonical skill scalar.");
        return matches[0].Value;
    }
    private static bool ReadSkillBool1949(XElement node, string name, bool? fallback, out bool value)
    {
        value = fallback ?? false;
        return node.Element(name) is null ? fallback.HasValue : bool.TryParse(SkillScalar1949(node, name), out value);
    }
    private static bool IsCurrentKarmaPromptSkillCompatibility2916(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement field)
    {
        if (field.Name.Namespace != XNamespace.None
            || field.HasAttributes || field.HasElements || field.Parent is not { } parent
            || parent.Name.Namespace != XNamespace.None || parent.HasAttributes
            || parent.Elements().Count(sibling => sibling.Name.LocalName == field.Name.LocalName) != 1)
            return false;
        string? grammarParent = (parent.Name.LocalName, field.Name.LocalName) switch
        {
            ("settings", "allowskilldicerolling" or "freemartialartspecialization") => "setting",
            ("karmacost", "karmacomplexformskillsoft") when parent.Parent?.Name == "settings" => "karmacost",
            _ => null
        };
        if (grammarParent is null
            || !UsesCurrentFullSkillProfile9765(current, source,
                parent.Name == "settings" ? parent : parent.Parent!)) return false;
        // Reuse the marked Profile's source-grounded value grammar: its ordinary
        // dice flag, disabled free martial specialization, and the retired SR4
        // skillsoft cost omitted by both SR5 loaders. No new skill policy or price
        // is inferred from these fields. Only this detached grammar view changes
        // the resolver's "settings" wrapper; retained source bytes stay intact.
        var observed = new XElement(field);
        _ = new XElement(grammarParent, observed);
        return KarmaPromptField2895(observed, out bool valid) && valid;
    }

    private static bool UsesCurrentFullSkillProfile9765(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement settings)
        => UsesKarmaQualityPrompts2742(current, source) || UsesPriorityCurrent3027(current)
            || UsesLifeModulesFullProfile9749(current, source, settings);

    private static bool ReadCurrentCareerSkillMaximum(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement settings, string name, out int value)
    {
        if (!UsesCurrentFullSkillProfile9765(current, source, settings))
            return ReadSkillInt1949(settings, name, null, out value);
        // Chummer5a CharacterSettings keeps Career caps at 12 when these leaves
        // are absent. Creation caps remain the separate source-owned 6/6 policy.
        // Apply this inherited default only to the supported V7 method profiles;
        // preserve the retained XML and historical explicit-cap admission.
        var fields = settings.Elements().Where(field => field.Name.LocalName == name).Take(2).ToArray();
        value = 12;
        if (fields.Length == 0) return true;
        if (fields.Length != 1 || fields[0].Name != name || fields[0].HasAttributes || fields[0].HasElements)
            return false;
        return ReadSkillInt1949(settings, name, null, out value);
    }

    private static bool ReadSkillInt1949(XElement node, string name, int? fallback, out int value)
    {
        value = fallback ?? 0;
        return node.Element(name) is null ? fallback.HasValue : int.TryParse(SkillScalar1949(node, name), NumberStyles.None,
            CultureInfo.InvariantCulture, out value) && value >= 0;
    }
}
