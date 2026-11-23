using Chummer.Contracts.Workspaces;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using System.Globalization;

namespace Chummer.Application.Characters;

/// <summary>Current-state source consumption, independent of completion, owner/CAS and activation.</summary>
public static class CharacterCurrentRuleEnvironmentAdmission
{
    /// <summary>Checked observation only: no domain success, owner permission or activation.</summary>
    public static CharacterCurrentRuleEnvironmentContext? CreateCurrentEnvironmentContext(
        WorkspaceStoredDocument current, CharacterCreationRuleSourceCapture retainedCurrentCapture,
        ICharacterFileQueries queries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(retainedCurrentCapture);
        ArgumentNullException.ThrowIfNull(queries);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (current.Document.RulesetId != Chummer5aLegacyImportCompatibility.RulesetId
                || !Chummer5aLegacyImportCompatibility.IsSupportedProfile(retainedCurrentCapture.CompatibilityProfile)
                || !retainedCurrentCapture.MatchesOriginalCharacter(current.Document.Content)
                || string.IsNullOrWhiteSpace(retainedCurrentCapture.RequiredPackageSetDigest)) return null;
            var xml = CharacterCurrentRuleEnvironmentConsumers.ParsePurchaseXml(current.Document.Content);
            if (xml is null || xml.Name != "character" || xml.HasAttributes || xml.Elements("created").Count() != 1
                || !bool.TryParse(xml.Element("created")!.Value, out bool created)) return null;
            string profile = CurrentCharacterRuleSourceSemantics.ProfileV2;
            string fingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV2;
            // Select the explicit reader before asking it to discover the source
            // profile. V7-only prompt settings correctly refuse a V2 context.
            // The retained canonical tuple is still checked below; this marker
            // alone cannot admit a domain or authorize execution.
            var capability = xml.Elements(CharacterCreationLifestyleMechanicsRules.CapabilityElement).ToArray();
            if (capability.Length != 0)
            {
                if (capability is not [var marker] || marker.HasAttributes || marker.HasElements
                    || marker.Value != CharacterCreationLifestyleMechanicsRules.Capability) return null;
                profile = CurrentCharacterRuleSourceSemantics.ProfileV7;
                fingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV7;
            }
            // Standalone files were unavailable to V1–V8. Select only the new
            // reader here; exact retained identity and the full-profile checks
            // below still have to succeed before a context can be returned.
            bool standaloneCandidate = created && capability.Length == 0
                && xml.Elements("settings").ToArray() is [var standaloneSelection]
                && !standaloneSelection.HasAttributes && !standaloneSelection.HasElements
                && CharacterRetainedSettingsFile.IsFileSelector(standaloneSelection.Value);
            if (standaloneCandidate)
            {
                profile = CurrentCharacterRuleSourceSemantics.ProfileV9;
                fingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV9;
            }
            var retained = retainedCurrentCapture.CreateCurrentEnvironmentResolution(profile, fingerprint);
            if (retained is null) return null;
            // Only the exact retained source profile selects the native-method interpretation.
            // Legacy Priority/Sum-to-Ten contexts and explicit V2 resolutions remain V2.
            var source = retained.Resolver.TryCreateContext(current.Document.Content);
            if (capability.Length == 0 && source is not null && source.TryResolveCreationSourceProfile(out var method)
                && method.BuildMethod is CharacterCreationBuildMethods.Karma or CharacterCreationBuildMethods.LifeModules
                && method.BuildMethod == xml.Element("buildmethod")?.Value
                && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(method.RawProfileInputsDigest))
            {
                profile = CurrentCharacterRuleSourceSemantics.ProfileV3;
                fingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV3;
                // Select the successor only for the new nonempty lifestyle-quality
                // capability. Explicit V2/V3 resolutions and empty catalogs retain
                // their old interpretation; selection is never domain admission.
                if (source.TryResolveCreationLifestylesAuthority(out var lifestyles) && lifestyles.QualityOptions.Count > 0)
                {
                    profile = CurrentCharacterRuleSourceSemantics.ProfileV4;
                    fingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV4;
                    // Keep the profile of every previously admissible V4 record.
                    // Restore/revalidation derives this choice from unchanged retained
                    // bytes. Only capabilities that V4 could not admit select V5.
                    if (lifestyles.QualityOptions.Any(option => !option.IsSelectable || !option.EligibilityIsExact
                        || option.Blockers.Count != 0 || option.AllowedFreeLifestyleNames.Count != 0
                        || option.SelectionSemantics is not { RequiresText: false, AllowMultiple: false }
                        || option.Area != 0 || option.Comforts != 0 || option.Security != 0
                        || option.AreaMaximumModifier != 0 || option.ComfortsMaximumModifier != 0 || option.SecurityMaximumModifier != 0))
                    {
                        profile = CurrentCharacterRuleSourceSemantics.ProfileV5;
                        fingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV5;
                    }
                }
                var selectedResolution = retainedCurrentCapture.CreateCurrentEnvironmentResolution(profile, fingerprint);
                if (selectedResolution is null) return null;
                var selectedContext = new CharacterCurrentRuleEnvironmentContext(current, retainedCurrentCapture, queries,
                    created ? CharacterCurrentRuleStage.Career : CharacterCurrentRuleStage.Creation, profile, fingerprint);
                if (selectedResolution is ICurrentLifestyleSourceCapabilities2678 capabilities
                    && (capabilities.RequiresStockLifestyles(selectedContext, cancellationToken)
                        || source.TryResolveCreationLifestylesAuthority(out var stockAuthority)
                            && (stockAuthority.FreeGridsEnabled || stockAuthority.LifestyleOptions.Any(option =>
                                !option.IsSelectable || !option.EligibilityIsExact || option.Blockers.Count != 0))))
                {
                    profile = CurrentCharacterRuleSourceSemantics.ProfileV6;
                    fingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV6;
                    if (retainedCurrentCapture.CreateCurrentEnvironmentResolution(profile, fingerprint) is null) return null;
                }
            }
            // This marker is an explicit persisted capability, never an automatic source-shape upgrade.
            if (capability.Length != 0)
            {
                if (capability is not [var field] || field.HasAttributes || field.HasElements
                    || field.Value != CharacterCreationLifestyleMechanicsRules.Capability) return null;
                // V7 also admits marked Priority/Sum-to-Ten; their unmarked contexts stay V2.
                // Observe the exact retained canonical profile, independently of completion/ownership.
                if (source is null || !source.TryResolveCreationSourceProfile(out var mechanicsMethod)
                    || !CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(
                        mechanicsMethod.BuildMethod, mechanicsMethod.SettingsProfileId)
                    || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(mechanicsMethod.RawProfileInputsDigest)
                    || xml.Elements("buildmethod").ToArray() is not [var buildMethod]
                    || buildMethod.HasAttributes || buildMethod.HasElements || buildMethod.Value != mechanicsMethod.BuildMethod
                    || xml.Elements("settings").ToArray() is not [var settings]
                    || settings.HasAttributes || settings.HasElements || settings.Value != mechanicsMethod.SettingsProfileId) return null;
                profile = CurrentCharacterRuleSourceSemantics.ProfileV7;
                fingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV7;
                if (retainedCurrentCapture.CreateCurrentEnvironmentResolution(profile, fingerprint) is null) return null;
            }
            // Only previously unsupported V3 Career source shapes select this
            // bounded successor. Explicit old resolutions/captures are unchanged.
            if (created && profile == CurrentCharacterRuleSourceSemantics.ProfileV3
                && source is ICurrentCareerConditionSourceCapabilities5497 conditionSource)
            {
                if (!conditionSource.TryRequiresCareerCondition5497(out bool required)) return null;
                if (required)
                {
                    profile = CurrentCharacterRuleSourceSemantics.ProfileV8;
                    fingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV8;
                    if (retainedCurrentCapture.CreateCurrentEnvironmentResolution(profile, fingerprint) is null) return null;
                }
            }
            // Full retained Priority settings were not in V2's grammar. Select a
            // separate interpretation for unmarked Career imports, without adding
            // native creation/lifestyle capabilities or reconstructing old choices.
            // The known full-profile label is a source-shape discriminator only;
            // the selected catalog and every domain still have to execute below.
            if (created && capability.Length == 0
                && (profile == CurrentCharacterRuleSourceSemantics.ProfileV2 || standaloneCandidate)
                && source is not null && source.TryResolveCreationSourceProfile(out var importedMethod)
                && importedMethod.BuildMethod == CharacterCreationBuildMethods.Priority
                // Career identity comes from the exact selected retained row below,
                // not native Bootstrap preset membership or a profile display name.
                && CharacterCurrentRuleEnvironmentConsumers.HasImportedSettingsIdentity5691(importedMethod)
                && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(importedMethod.RawProfileInputsDigest)
                && xml.Elements("buildmethod").ToArray() is [var importedBuild]
                && !importedBuild.HasAttributes && !importedBuild.HasElements && importedBuild.Value == importedMethod.BuildMethod
                && xml.Elements("settings").ToArray() is [var importedSettings]
                && !importedSettings.HasAttributes && !importedSettings.HasElements && importedSettings.Value == importedMethod.SettingsProfileId
                && source.TryResolveCareerSettingsProfile(out var importedProfile, out var importedState)
                && !string.IsNullOrWhiteSpace(importedState)
                && CharacterCurrentRuleEnvironmentConsumers.ParsePurchaseXml(importedProfile).Elements("gameplayoptionname").Any())
            {
                profile = CurrentCharacterRuleSourceSemantics.ProfileV9;
                fingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV9;
                if (retainedCurrentCapture.CreateCurrentEnvironmentResolution(profile, fingerprint) is null) return null;
            }
            else if (standaloneCandidate) return null;

            // V7 never admitted a native Priority Career graph with effects.
            // Select a successor only for that shape. Selection does not prove
            // history: the native owner reader must replay the actual receipts;
            // missing or invalid history never falls back to the imported reader.
            if (created && profile == CurrentCharacterRuleSourceSemantics.ProfileV7
                && xml.Element("buildmethod")?.Value == CharacterCreationBuildMethods.Priority
                && xml.Elements("improvements").Any(container => container.Elements().Any()))
            {
                profile = CurrentCharacterRuleSourceSemantics.ProfileV10;
                fingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV10;
                if (retainedCurrentCapture.CreateCurrentEnvironmentResolution(profile, fingerprint) is null) return null;
            }

            var context = new CharacterCurrentRuleEnvironmentContext(current, retainedCurrentCapture, queries,
                created ? CharacterCurrentRuleStage.Career : CharacterCurrentRuleStage.Creation, profile, fingerprint);
            return context.IsUnchanged ? context : null;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or OverflowException or System.Xml.XmlException or System.Text.Json.JsonException)
        { return null; }
    }

    public static CharacterCurrentRuleEnvironmentAdmissionResult AdmitCurrentEnvironment(
        WorkspaceStoredDocument current, CharacterCreationRuleSourceCapture retainedCurrentCapture,
        ICharacterFileQueries queries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(retainedCurrentCapture);
        ArgumentNullException.ThrowIfNull(queries);
        cancellationToken.ThrowIfCancellationRequested();
        if (current.Document.RulesetId != Chummer5aLegacyImportCompatibility.RulesetId)
            return new(null, ["current-rule-environment-edition-unsupported"]);
        if (!Chummer5aLegacyImportCompatibility.IsSupportedProfile(retainedCurrentCapture.CompatibilityProfile))
            return new(null, ["current-rule-environment-retained-profile-unsupported"]);
        if (!retainedCurrentCapture.MatchesOriginalCharacter(current.Document.Content))
            return new(null, ["current-rule-environment-character-mismatch"]);
        var execution = CharacterCurrentRuleEnvironmentExecution.Evaluate(current, retainedCurrentCapture,
            queries, cancellationToken, out var blockers);
        return new(execution, blockers);
    }
}

public sealed class CharacterCurrentRuleEnvironmentAdmissionResult
{
    internal CharacterCurrentRuleEnvironmentAdmissionResult(CharacterCurrentRuleEnvironmentExecution? execution,
        IReadOnlyList<string> blockers)
    {
        Execution = execution;
        Blockers = Array.AsReadOnly(blockers.ToArray());
    }
    public CharacterCurrentRuleEnvironmentExecution? Execution { get; }
    public IReadOnlyList<string> Blockers { get; }
}

/// <summary>Source recapture/readmission observation only; never permission to publish a transition.</summary>
public sealed class CharacterCurrentRuleEnvironmentReadmissionResult
{
    internal CharacterCurrentRuleEnvironmentReadmissionResult(CharacterCreationRuleSourceCapture? afterCapture,
        CharacterCurrentRuleEnvironmentExecution? afterExecution, IReadOnlyList<string> blockers)
        => (AfterCapture, AfterExecution, Blockers) = (afterCapture, afterExecution, Array.AsReadOnly(blockers.ToArray()));
    public CharacterCreationRuleSourceCapture? AfterCapture { get; }
    public CharacterCurrentRuleEnvironmentExecution? AfterExecution { get; }
    public IReadOnlyList<string> Blockers { get; }
}

/// <summary>Application-issued capability. Never owner authority, a saved budget, or a serialization grant.</summary>
public sealed partial class CharacterCurrentRuleEnvironmentExecution
{
    private readonly CharacterCurrentRuleEnvironmentContext context;
    private readonly CharacterCreationRuleSourceResolution resolution;
    private readonly IReadOnlyList<CharacterCurrentRuleCatalogValidation> catalogs;
    private readonly IReadOnlyList<CharacterCurrentRuleDomainExecution> executions;
    private readonly string outcomeDigest;

    private CharacterCurrentRuleEnvironmentExecution(CharacterCurrentRuleEnvironmentContext context,
        CharacterCreationRuleSourceResolution resolution,
        IReadOnlyList<CharacterCurrentRuleCatalogValidation> catalogs,
        IReadOnlyList<CharacterCurrentRuleDomainExecution> executions)
    {
        this.context = context;
        this.resolution = resolution;
        this.catalogs = Array.AsReadOnly(catalogs.ToArray());
        this.executions = Array.AsReadOnly(executions.ToArray());
        outcomeDigest = OutcomeDigest(this.catalogs, this.executions);
    }

    public string CaptureDigest => context.CaptureDigest;
    public string RequiredPackageSetDigest => context.RequiredPackageSetDigest ?? string.Empty;
    public string CompatibilityProfile => context.Capture.CompatibilityProfile ?? string.Empty;
    public string ExecutionProfile => context.SemanticsProfile;
    public string ExecutionProfileFingerprint => context.SemanticsFingerprint;

    /// <summary>Differences in actual admitted kernel selections and catalog fields.
    /// This display report is never an execution or mutation capability.</summary>
    public IReadOnlyList<string> DescribeUpdateFrom(CharacterCurrentRuleEnvironmentExecution previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var changes = new List<string>();
        foreach (var after in executions)
        {
            var before = previous.executions.Single(value => value.Domain == after.Domain);
            var oldCatalog = previous.catalogs.Single(value => value.Domain == after.Domain);
            var newCatalog = catalogs.Single(value => value.Domain == after.Domain);
            string domain = System.Text.RegularExpressions.Regex.Replace(after.Domain.ToString(), "([a-z])([A-Z])", "$1 $2");
            foreach (string key in before.SelectedKeys.Except(after.SelectedKeys, StringComparer.Ordinal))
                changes.Add($"{domain}: remove selected {oldCatalog.Entries.Single(value => value.Key == key).Name}.");
            foreach (string key in after.SelectedKeys.Except(before.SelectedKeys, StringComparer.Ordinal))
                changes.Add($"{domain}: select {newCatalog.Entries.Single(value => value.Key == key).Name}.");
            var oldAvailable = oldCatalog.Entries.Where(value => value.State == CharacterCurrentRuleEntryState.AvailableNotSelected)
                .ToDictionary(value => value.Key, StringComparer.Ordinal);
            var newAvailable = newCatalog.Entries.Where(value => value.State == CharacterCurrentRuleEntryState.AvailableNotSelected)
                .ToDictionary(value => value.Key, StringComparer.Ordinal);
            foreach (string key in oldAvailable.Keys.Intersect(newAvailable.Keys, StringComparer.Ordinal))
            {
                var oldEntry = oldAvailable[key];
                var newEntry = newAvailable[key];
                string scope = before.SelectedKeys.Contains(key) || after.SelectedKeys.Contains(key) ? "selected" : "available";
                if (oldEntry.Xml == newEntry.Xml) continue;
                var oldFields = Fields(oldEntry.Xml);
                var newFields = Fields(newEntry.Xml);
                foreach (string field in oldFields.Keys.Union(newFields.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    string oldValue = oldFields.GetValueOrDefault(field, "absent");
                    string newValue = newFields.GetValueOrDefault(field, "absent");
                    if (oldValue != newValue)
                        changes.Add($"{domain}, {scope} {newEntry.Name}: rule {field}: {oldValue} → {newValue}.");
                }
            }
            int added = newCatalog.Entries.Count(value => value.State == CharacterCurrentRuleEntryState.AvailableNotSelected
                && !oldCatalog.Entries.Any(old => old.Key == value.Key && old.State == CharacterCurrentRuleEntryState.AvailableNotSelected));
            int removed = oldCatalog.Entries.Count(value => value.State == CharacterCurrentRuleEntryState.AvailableNotSelected
                && !newCatalog.Entries.Any(next => next.Key == value.Key && next.State == CharacterCurrentRuleEntryState.AvailableNotSelected));
            if (added != 0 || removed != 0) changes.Add($"{domain}: {added} available choices added; {removed} removed.");
            if (before.State != after.State) changes.Add($"{domain}: evaluation state {before.State} → {after.State}.");
        }
        return Array.AsReadOnly(changes.ToArray());

        static Dictionary<string, string> Fields(string xml)
        {
            var element = System.Xml.Linq.XElement.Parse(xml);
            // Keep full element paths and repeated values; never interpret a formula as a total.
            return element.DescendantsAndSelf().Where(value => !value.HasElements)
                .GroupBy(value => string.Join("/", value.AncestorsAndSelf().Reverse().Select(part => part.Name.LocalName)), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => string.Join("; ", group.Select(value => value.Value)), StringComparer.Ordinal);
        }
    }

    // Fixed-name diagnostics only. No source/report data or admission result is retained here.
    private static readonly System.Diagnostics.ActivitySource AdmissionActivities9887 =
        new("Chummer.Startup.Shell8910");

    private static string? AdmissionDomainStage9887(CharacterCurrentRuleDomain domain, bool execute)
        => (domain, execute) switch
        {
            (CharacterCurrentRuleDomain.Profile, false) => "admission-catalog-profile",
            (CharacterCurrentRuleDomain.Profile, true) => "admission-execute-profile",
            (CharacterCurrentRuleDomain.Metatypes, false) => "admission-catalog-metatypes",
            (CharacterCurrentRuleDomain.Metatypes, true) => "admission-execute-metatypes",
            (CharacterCurrentRuleDomain.Priorities, false) => "admission-catalog-priorities",
            (CharacterCurrentRuleDomain.Priorities, true) => "admission-execute-priorities",
            (CharacterCurrentRuleDomain.Skills, false) => "admission-catalog-skills",
            (CharacterCurrentRuleDomain.Skills, true) => "admission-execute-skills",
            (CharacterCurrentRuleDomain.Qualities, false) => "admission-catalog-qualities",
            (CharacterCurrentRuleDomain.Qualities, true) => "admission-execute-qualities",
            (CharacterCurrentRuleDomain.Traditions, false) => "admission-catalog-traditions",
            (CharacterCurrentRuleDomain.Traditions, true) => "admission-execute-traditions",
            (CharacterCurrentRuleDomain.Streams, false) => "admission-catalog-streams",
            (CharacterCurrentRuleDomain.Streams, true) => "admission-execute-streams",
            (CharacterCurrentRuleDomain.Powers, false) => "admission-catalog-powers",
            (CharacterCurrentRuleDomain.Powers, true) => "admission-execute-powers",
            (CharacterCurrentRuleDomain.Spells, false) => "admission-catalog-spells",
            (CharacterCurrentRuleDomain.Spells, true) => "admission-execute-spells",
            (CharacterCurrentRuleDomain.ComplexForms, false) => "admission-catalog-complex-forms",
            (CharacterCurrentRuleDomain.ComplexForms, true) => "admission-execute-complex-forms",
            (CharacterCurrentRuleDomain.Gear, false) => "admission-catalog-gear",
            (CharacterCurrentRuleDomain.Gear, true) => "admission-execute-gear",
            (CharacterCurrentRuleDomain.Lifestyles, false) => "admission-catalog-lifestyles",
            (CharacterCurrentRuleDomain.Lifestyles, true) => "admission-execute-lifestyles",
            (CharacterCurrentRuleDomain.Weapons, false) => "admission-catalog-weapons",
            (CharacterCurrentRuleDomain.Weapons, true) => "admission-execute-weapons",
            (CharacterCurrentRuleDomain.Armor, false) => "admission-catalog-armor",
            (CharacterCurrentRuleDomain.Armor, true) => "admission-execute-armor",
            _ => null
        };

    internal static CharacterCurrentRuleEnvironmentExecution? Evaluate(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture capture, ICharacterFileQueries queries,
        CancellationToken cancellationToken, out IReadOnlyList<string> blockers)
    {
        blockers = ["current-rule-environment-canonical-consumption-unavailable"];
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var admissionTiming = AdmissionActivities9887.StartActivity("admission-evaluate");
            using var contextTiming = AdmissionActivities9887.StartActivity("admission-context");
            var context = CharacterCurrentRuleEnvironmentAdmission.CreateCurrentEnvironmentContext(
                current, capture, queries, cancellationToken);
            contextTiming?.Dispose();
            if (context is null) return null;
            using var scopeTiming = AdmissionActivities9887.StartActivity("admission-scope-open");
            var resolution = capture.CreateCurrentEnvironmentResolution(context.SemanticsProfile, context.SemanticsFingerprint);
            if (resolution is null || !context.IsUnchanged) return null;
            using var retainedEvaluation = (resolution as IRetainedCurrentRuleEvaluationIssuer2764)
                ?.BeginRetainedEvaluation2764(context);
            scopeTiming?.Dispose();
            var catalogs = new List<CharacterCurrentRuleCatalogValidation>();
            var executions = new List<CharacterCurrentRuleDomainExecution>();
            foreach (var domain in Enum.GetValues<CharacterCurrentRuleDomain>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var catalogTiming = AdmissionDomainStage9887(domain, execute: false) is { } catalogStage
                    ? AdmissionActivities9887.StartActivity(catalogStage) : null;
                var catalog = resolution.ValidateCurrentRuleCatalog(context, domain, cancellationToken);
                if (catalog is null || catalog.Domain != domain || !catalog.IsComplete
                    || !resolution.HasValidatedCurrentRuleCatalog(context, catalog))
                { return null; }
                catalogTiming?.Dispose();
                using var executionTiming = AdmissionDomainStage9887(domain, execute: true) is { } executionStage
                    ? AdmissionActivities9887.StartActivity(executionStage) : null;
                var execution = resolution.ExecuteCurrentRuleDomain(context, catalog, cancellationToken);
                if (execution is null || execution.Domain != domain || !execution.IsComplete
                    || !resolution.HasExecutedCurrentRuleDomain(context, catalog, execution))
                { return null; }
                // Unavailable and deleted entries are never valid selected dependencies.
                if (execution.SelectedKeys.Any(key => catalog.Entries.Count(entry => entry.Key == key
                        && entry.State == CharacterCurrentRuleEntryState.AvailableNotSelected) != 1)) return null;
                catalogs.Add(catalog);
                executions.Add(execution);
            }
            // Retire derived reuse before the final archive/receipt fence and before
            // any execution can escape. A boundary failure retains refusal blockers.
            using (AdmissionActivities9887.StartActivity("admission-scope-retire"))
                retainedEvaluation?.Dispose();
            using var fencesTiming = AdmissionActivities9887.StartActivity("admission-final-fences");
            if (!context.VerifyRetainedArchive() || catalogs.Count != 14 || executions.Count != 14
                || !ValidateIssuedReports8139(context, resolution, catalogs.AsReadOnly(), executions.AsReadOnly(),
                    cancellationToken)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            fencesTiming?.Dispose();
            blockers = [];
            using (AdmissionActivities9887.StartActivity("admission-issue"))
                return new(context, resolution, catalogs, executions);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or OverflowException or System.Xml.XmlException or System.Text.Json.JsonException)
        { return null; }
    }

    /// <summary>Canonical observation of an actual proposed after-record. Owner/lease,
    /// transition legality, revision predicates and atomic publication remain store-owned.</summary>
    public CharacterCurrentRuleEnvironmentReadmissionResult ReadmitProposedCurrentEnvironment(
        WorkspaceStoredDocument before, CharacterCreationRuleSourceCapture retainedBeforeCapture,
        ICharacterFileQueries queries, WorkspaceStoredDocument proposedAfter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(retainedBeforeCapture);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(proposedAfter);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var observedBefore = Reevaluate(before, retainedBeforeCapture, queries, cancellationToken);
            if (observedBefore is null)
                return new(null, null, ["current-rule-environment-before-binding-changed"]);
            if (proposedAfter.Id != before.Id || proposedAfter.Document.RulesetId != before.Document.RulesetId)
                return new(null, null, ["current-rule-environment-after-identity-changed"]);
            // Auxiliary/receipt/revision-only records still require full fresh AFTER admission.
            // Their XML can legitimately reuse the same retained capture; never reuse execution.
            var afterCapture = string.Equals(before.Document.Content, proposedAfter.Document.Content, StringComparison.Ordinal)
                ? retainedBeforeCapture
                : retainedBeforeCapture.RecaptureCurrentEnvironment(proposedAfter.Document.Content,
                    context.SemanticsProfile, context.SemanticsFingerprint, cancellationToken);
            if (afterCapture is null || !afterCapture.MatchesOriginalCharacter(proposedAfter.Document.Content)
                || RetainedSourceMetadata(retainedBeforeCapture) != RetainedSourceMetadata(afterCapture))
                return new(null, null, ["current-rule-environment-after-retained-capture-unavailable"]);
            var admitted = CharacterCurrentRuleEnvironmentAdmission.AdmitCurrentEnvironment(
                proposedAfter, afterCapture, queries, cancellationToken);
            if (admitted.Execution is null) return new(null, null, admitted.Blockers);
            cancellationToken.ThrowIfCancellationRequested();
            if (!observedBefore.context.VerifyRetainedArchive() || !admitted.Execution.context.VerifyRetainedArchive())
                return new(null, null, ["current-rule-environment-after-binding-changed"]);
            return new(afterCapture, admitted.Execution, []);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or OverflowException or System.Xml.XmlException or System.Text.Json.JsonException)
        { return new(null, null, ["current-rule-environment-after-observation-unavailable"]); }
    }

    private static string RetainedSourceMetadata(CharacterCreationRuleSourceCapture capture)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            capture.CompatibilityProfile, capture.EffectivePolicyDigest, capture.RequiredPackageSetDigest,
            capture.RequiredRulePacks, capture.RequiredAssets, capture.OrderedCustomSources,
            capture.CompatibilityDiagnostics, capture.SourcebookSelection
        });

    public bool Revalidate(WorkspaceStoredDocument current, CharacterCreationRuleSourceCapture retainedCurrentCapture,
        ICharacterFileQueries queries, CancellationToken cancellationToken = default)
        => Reevaluate(current, retainedCurrentCapture, queries, cancellationToken) is not null;

    /// <summary>Checks an already computed display observation against exact retained inputs.
    /// The store owns the bounded display-observation lifetime and live private-file/owner fences. This is
    /// not mutation admission; ordinary Revalidate and all proposed-after paths stay fresh.</summary>
    public bool RevalidateDisplayObservation(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture capture, ICharacterFileQueries queries,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!ReferenceEquals(context.Queries, queries) || context.Capture.GetType() != capture.GetType()
                || context.Capture.SourcebookSelection is not null || capture.SourcebookSelection is not null
                || !context.VerifyRetainedArchive() || outcomeDigest != OutcomeDigest(catalogs, executions)) return false;
            var observed = CharacterCurrentRuleEnvironmentAdmission.CreateCurrentEnvironmentContext(
                current, capture, queries, cancellationToken);
            if (observed is null || observed.BindingDigest != context.BindingDigest
                || !context.Capture.ExportPrivateInputs().AsSpan().SequenceEqual(capture.ExportPrivateInputs())
                || catalogs.Count != 14 || executions.Count != 14) return false;
            // Keep the issuing resolution's identity and frozen-output checks. Only the
            // deterministic catalog construction and domain execution are reused.
            if (!ValidateIssuedReports8139(context, resolution, catalogs, executions, cancellationToken)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            return context.VerifyRetainedArchive() && observed.VerifyRetainedArchive();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or OverflowException or System.Xml.XmlException or System.Text.Json.JsonException)
        { return false; }
    }

    private CharacterCurrentRuleEnvironmentExecution? Reevaluate(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture capture, ICharacterFileQueries queries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(context.Queries, queries) || !context.IsUnchanged
            || capture.CompatibilityProfile != CompatibilityProfile
            || capture.IntegrityDigest != CaptureDigest || capture.RequiredPackageSetDigest != RequiredPackageSetDigest
            || !capture.MatchesOriginalCharacter(current.Document.Content)
            || outcomeDigest != OutcomeDigest(catalogs, executions)) return null;
        var repeated = Evaluate(current, capture, queries, cancellationToken, out _);
        return repeated is not null && repeated.context.BindingDigest == context.BindingDigest
            && repeated.outcomeDigest == outcomeDigest ? repeated : null;
    }

    /// <summary>Fresh catalog read on the admitted retained context; no ambient resolver escapes.</summary>
    public CharacterCurrentRuleCatalogValidation? QueryCatalog(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        CharacterCurrentRuleDomain domain, CancellationToken cancellationToken = default)
        => Reevaluate(current, retainedCurrentCapture, queries, cancellationToken)?.catalogs.SingleOrDefault(row => row.Domain == domain);

    // Pure display of a catalog already evaluated for these exact inputs. The
    // store owns the read lifetime and repeats live record/source/owner fences.
    // Sourcebook envelopes keep their ordinary fresh-evaluation path.
    internal CharacterCurrentRuleCatalogValidation? QueryCatalogDisplayObservation(
        WorkspaceStoredDocument current, CharacterCreationRuleSourceCapture capture,
        ICharacterFileQueries queries, CharacterCurrentRuleDomain domain, CancellationToken cancellationToken)
        => capture.SourcebookSelection is not null
            ? QueryCatalog(current, capture, queries, domain, cancellationToken)
            : RevalidateDisplayObservation(current, capture, queries, cancellationToken)
                ? catalogs.SingleOrDefault(row => row.Domain == domain) : null;

    /// <summary>Executes the existing Career price/availability kernel at the supplied current revision.</summary>
    public CharacterCurrentRuleGearQuote? QuoteCareerGear(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        CharacterCareerGearPurchaseRequest request, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Career
            || request.WorkspaceId != current.Id) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Gear);
        if (!CharacterCurrentRuleEnvironmentConsumers.TryAvailablePurchaseEntry(catalog, request.SourceId, out string key)) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null || !source.TryResolveCareerSettingsProfile(out string settings, out _)
            || !source.TryResolveCareerGearPurchaseSource(request.SourceId, out var item) || item is null
            || !CharacterCreationLegacySourceProjector.TryBuildCareerPurchasedGear(
                CharacterCreationLegacySourceProjector.BindCareerAmmunitionSource(item, source), request, settings,
                request.SourceId, out _, out decimal unit, out decimal total, out int availability, out string legality, out _)
            || !repeated.context.VerifyRetainedArchive()) return null;
        var xml = CharacterCurrentRuleEnvironmentConsumers.ParsePurchaseXml(current.Document.Content);
        if (!decimal.TryParse(xml?.Element("nuyen")?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal funds)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(repeated.context.BindingDigest, CaptureDigest, key, unit, total, availability, legality, total <= funds);
    }

    /// <summary>Canonical Creation basket quote; requires actual saved Resources choices.</summary>
    public CharacterCurrentRuleCreationGearQuote? QuoteCreationGear(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        IReadOnlyList<CharacterCreationGearSelection> selections, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Creation
            || current.Document.AuxiliaryState.CharacterCreationResourcesDraft is not { } resources) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Gear);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null || !source.TryResolveCreationGearAuthority(out var authority)) return null;
        foreach (var selection in selections)
        {
            var option = authority.Options.SingleOrDefault(option => option.OptionId == selection.OptionId);
            if (option is null || !CharacterCurrentRuleEnvironmentConsumers.TryAvailablePurchaseEntry(catalog, option.SourceId, out _)) return null;
        }
        if (!CharacterCreationGearRules.TryProjectBasket(selections, authority, resources.Budget.TotalStartingNuyen,
                out var lines, out var budget, out _) || !repeated.context.VerifyRetainedArchive()) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(repeated.context.BindingDigest, CaptureDigest, Array.AsReadOnly(lines), budget);
    }

    /// <summary>Ordinary Career quality source price from the exact current saved graph.
    /// Owner/GM/eligibility and mutation authority remain the existing service's job.</summary>
    public CharacterCurrentRuleQualityQuote? QuoteCareerQuality(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        CharacterCareerQualityIdentity identity, CharacterCareerQualityOperation operation,
        CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Career) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Qualities);
        if (!CharacterCurrentRuleEnvironmentConsumers.TryAvailablePurchaseEntry(catalog, identity.SourceId, out string key)) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null || !CharacterCurrentRuleEnvironmentConsumers.CanQuoteCurrentOrdinaryQuality1965(repeated.context, source, identity)
            || !source.TryResolveCareerQualitySettings(out var settings, out _)
            || !source.TryResolveCareerQualitySource(identity.SourceId, out var item) || item is null
            || !CharacterCareerQualityGraph.TryProject(current.Document.Content, item, identity, operation, null, out var graph,
                importedCareerEffects5698: CurrentCharacterRuleSourceSemantics.IsOwnedPriorityCareerProfile9569(
                    repeated.context.SemanticsProfile, repeated.context.SemanticsFingerprint))
            || graph is null || !CharacterCareerQualityRules.TryCalculateOrdinaryCurrentPrice(operation, graph.Definition,
                settings, graph.InstancesBefore.Length, out int ruleKarmaCost, out int karmaDelta)
            || !repeated.context.VerifyRetainedArchive()) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(repeated.context.BindingDigest, CaptureDigest, key, ruleKarmaCost, karmaDelta);
    }

    /// <summary>Fresh canonical Creation quality quote; no owner/save or reusable budget grant.</summary>
    public CharacterCreationQualitiesPreview? QuoteCreationQualities(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        IReadOnlyList<string> selectedOptionIds, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Creation) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Qualities);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var quote = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentCreationQualities(
            repeated.context, catalog, source, selectedOptionIds);
        cancellationToken.ThrowIfCancellationRequested();
        return quote is not null && repeated.context.VerifyRetainedArchive() ? quote : null;
    }

    /// <summary>Reevaluates the exact retained current context before the canonical skill query.</summary>
    public CharacterCreationSkillCatalogEntry? FindSkill(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string catalogKey, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Skills);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.FindCurrentSkill(
            repeated.context, catalog, source, catalogKey, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Reevaluates the exact retained current context before the canonical skill query.</summary>
    public CharacterCareerActiveSkillAdvanceQuote? QuoteCareerActiveSkill(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        Guid skillId, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Career) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Skills);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentActiveSkill(
            repeated.context, catalog, source, skillId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Reevaluates the exact retained current context before the canonical skill query.</summary>
    public CharacterCareerKnowledgeSkillAdvanceQuote? QuoteCareerKnowledgeSkill(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        Guid skillId, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Career) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Skills);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentKnowledgeSkill(
            repeated.context, catalog, source, skillId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Reevaluates the exact retained current context before the canonical skill query.</summary>
    public CharacterCareerSkillGroupAdvanceQuote? QuoteCareerSkillGroup(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        Guid groupId, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Career) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Skills);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentSkillGroup(
            repeated.context, catalog, source, groupId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Reevaluates the exact retained current context before the canonical skill query.</summary>
    public CharacterCareerSkillSpecializationQuote? QuoteCareerSkillSpecialization(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        Guid skillId, string optionIdentity, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Career) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Skills);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentSkillSpecialization(
            repeated.context, catalog, source, skillId, optionIdentity, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    internal IReadOnlyList<CharacterCareerSkillSpecializationInput>? ReadCareerSkillSpecializationInputs2559(
        WorkspaceStoredDocument current, CharacterCreationRuleSourceCapture retainedCurrentCapture,
        ICharacterFileQueries queries, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Career) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Skills);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.ReadCurrentSkillSpecializationInputs2559(
            repeated.context, catalog, source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Fresh canonical current Creation query; missing choices remain missing.</summary>
    public CharacterCreationMetatypeOptionProjection? FindMetatype(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string optionId, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Metatypes);
        if (!Guid.TryParse(optionId, out Guid sourceId)
            || !CharacterCurrentRuleEnvironmentConsumers.TryAvailablePurchaseEntry(catalog, sourceId, out _)) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.FindCurrentCreationMetatype(
            repeated.context, catalog, source, optionId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Fresh canonical current Creation query; missing choices remain missing.</summary>
    public CharacterCurrentCreationMetatypeQuote? QuoteMetatype(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string optionId, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Metatypes);
        if (!Guid.TryParse(optionId, out Guid sourceId)
            || !CharacterCurrentRuleEnvironmentConsumers.TryAvailablePurchaseEntry(catalog, sourceId, out _)) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentCreationMetatype(
            repeated.context, catalog, source, optionId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Fresh canonical current Creation query; missing choices remain missing.</summary>
    public CharacterCurrentCreationSelectionQuote? QuoteCreationSelection(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        IReadOnlyDictionary<string, string> ranks, string? heritageSelectionId, string? talentSelectionId, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Priorities);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentCreationSelection(
            repeated.context, catalog, source, ranks, heritageSelectionId, talentSelectionId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }


    /// <summary>Native Life Modules selection cost/requirements; never grants persistence authority.</summary>
    public CharacterCreationFoundationPreview? QuoteLifeModuleFoundation(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string metatypeOptionId, CharacterCreationFoundationSelection selection,
        IReadOnlyDictionary<string, string>? answers = null, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Creation) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Metatypes);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var quote = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentLifeModuleFoundation1958(
            repeated.context, catalog, source, metatypeOptionId, selection, answers);
        cancellationToken.ThrowIfCancellationRequested();
        return quote is not null && repeated.context.VerifyRetainedArchive() ? quote : null;
    }

    /// <summary>Read the native foundation state/catalog DTO under this retained
    /// execution. No source context, workspace view or mutation authority escapes.</summary>
    public CharacterCreationFoundationResult<CharacterCreationFoundationState>? LoadLifeModuleFinalization(
        WorkspaceStoredDocument current, CharacterCreationRuleSourceCapture retainedCurrentCapture,
        ICharacterFileQueries queries, CancellationToken cancellationToken = default)
        => ReadLifeModuleNative(current, retainedCurrentCapture, queries,
            (workspace, files, resolver, modules) =>
                OwnerBoundCharacterCreationLifeModuleFinalizationService.ReadNativeState(workspace, files, resolver, modules)
                    is { Value: not null } result ? result : null, cancellationToken);

    /// <summary>Advisory DTOs from the exact retained source admitted for native
    /// Life Module reads/previews. No resolver or execution authority escapes.</summary>
    public CharacterCreationLifeModuleCatalogProjection? LoadLifeModuleSuggestionCatalog(
        WorkspaceStoredDocument current, CharacterCreationRuleSourceCapture retainedCurrentCapture,
        ICharacterFileQueries queries, CharacterCreationFoundationState expectedFoundation,
        string environmentDigest, CancellationToken cancellationToken = default)
        => ReadLifeModuleNative(current, retainedCurrentCapture, queries,
            (workspace, files, resolver, modules) =>
                OwnerBoundCharacterCreationLifeModuleFinalizationService.ReadNativeSuggestionCatalog(
                    workspace, files, resolver, modules, expectedFoundation, environmentDigest), cancellationToken);

    private T? ReadLifeModuleNative<T>(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        Func<WorkspaceStoredDocument, ICharacterFileQueries, ICharacterSourceDataResolver,
            Chummer.Application.LifeModules.ILifeModulesCatalogService, T?> read,
        CancellationToken cancellationToken) where T : class
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Creation
            || !CurrentCharacterRuleSourceSemantics.IsNativeExecutionProfile(
                repeated.ExecutionProfile, repeated.ExecutionProfileFingerprint)) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var resolver = scope ?? repeated.resolution.Resolver;
        if (resolver.TryCreateContext(current.Document.Content) is not ICharacterCurrentNativeMethodSource1958 native
            || !native.TryResolveCurrentLifeModulesCatalog(out var modules) || modules is null) return null;
        var result = read(current, queries, resolver, modules);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive()
            && repeated.Revalidate(current, retainedCurrentCapture, queries, cancellationToken) ? result : null;
    }

    /// <summary>Prices the actual pending finalization request under this retained
    /// execution. The native preview retains its blockers and digest; Confirm
    /// remains the separately owner-bound atomic mutation authority.</summary>
    public CharacterCreationFoundationFinalizationPreview? QuoteLifeModuleFinalization(
        WorkspaceStoredDocument current, CharacterCreationRuleSourceCapture retainedCurrentCapture,
        ICharacterFileQueries queries, CharacterCreationFoundationFinalizationPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Binding is null) return null;
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Creation
            || !CurrentCharacterRuleSourceSemantics.IsNativeExecutionProfile(
                repeated.context.SemanticsProfile, repeated.context.SemanticsFingerprint)
            || request.Binding.WorkspaceId != current.Id || request.Binding.ContentRevision != current.ContentRevision
            || request.Binding.SavedRevision != current.SavedRevision) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var quote = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentLifeModuleFinalization1960(
            repeated.context, source, request);
        cancellationToken.ThrowIfCancellationRequested();
        return quote is not null && repeated.context.VerifyRetainedArchive() ? quote : null;
    }

    /// <summary>Prepare the existing native transaction using only this execution's
    /// retained sources. Returned bytes and receipt still require store authority.</summary>
    public CharacterCurrentLifeModuleFinalizationPreparation? PrepareLifeModuleFinalization(
        Chummer.Contracts.Owners.OwnerScope owner, WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        CharacterCreationFoundationFinalizationConfirmRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!CharacterCreationLifeModuleFinalizationTransaction.IsConfirmed(request)) return null;
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Creation
            || !CurrentCharacterRuleSourceSemantics.IsNativeExecutionProfile(
                repeated.ExecutionProfile, repeated.ExecutionProfileFingerprint)) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var resolver = scope ?? repeated.resolution.Resolver;
        var source = resolver.TryCreateContext(current.Document.Content);
        if (source is not ICharacterCurrentNativeMethodSource1958 native
            || !native.TryResolveCurrentLifeModulesCatalog(out var modules) || modules is null
            || !CharacterCreationLifeModuleFinalizationTransaction.TryBuild(owner, current, resolver,
                modules, queries, request, out var document, out var receipt, out _)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return document is not null && receipt is not null && repeated.context.VerifyRetainedArchive()
            && repeated.Revalidate(current, retainedCurrentCapture, queries, cancellationToken)
            ? new(document, receipt) : null;
    }

    /// <summary>Quotes actual native Karma choices using the exact retained current policies.</summary>
    public CharacterCreationKarmaMetatypeQuote? QuoteKarmaBuild(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        CharacterCurrentKarmaChoices1958 choices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Creation) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Metatypes);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var quote = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentKarmaBuild1958(repeated.context,
            catalog, source, choices);
        cancellationToken.ThrowIfCancellationRequested();
        return quote is not null && CharacterCurrentRuleEnvironmentConsumers.CurrentKarmaQuoteCatalogs1958(quote, repeated.catalogs, source)
            && repeated.context.VerifyRetainedArchive() ? quote : null;
    }

    /// <summary>Read-only native Karma preview under the current retained source and revision.</summary>
    public CharacterCreationKarmaMetatypeQuote? QuoteKarmaFoundation(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string metatypeOptionId, string? talentOptionId = null,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributes = null,
        CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || repeated.context.Stage != CharacterCurrentRuleStage.Creation) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Metatypes);
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var quote = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentKarmaFoundation1958(
            repeated.context, catalog, source, metatypeOptionId, talentOptionId, attributes);
        cancellationToken.ThrowIfCancellationRequested();
        return quote is not null && repeated.context.VerifyRetainedArchive() ? quote : null;
    }

    /// <summary>Exact available option, original current state, and retained canonical quote.</summary>
    public CharacterCurrentTraditionQuote1949? QuoteTradition(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string catalogKey, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Traditions);
        if (catalog.Entries.SingleOrDefault(row => row.Key == catalogKey)?.State
            != CharacterCurrentRuleEntryState.AvailableNotSelected) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentTradition(
            repeated.context, catalog, source, catalogKey, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Exact available option, original current state, and retained canonical quote.</summary>
    public CharacterCurrentTraditionQuote1949? QuoteStream(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string catalogKey, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Streams);
        if (catalog.Entries.SingleOrDefault(row => row.Key == catalogKey)?.State
            != CharacterCurrentRuleEntryState.AvailableNotSelected) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentStream(
            repeated.context, catalog, source, catalogKey, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Exact available option, original current state, and retained canonical quote.</summary>
    public CharacterCurrentMagicOptionQuote1949? QuotePower(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string catalogKey, int rating, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Powers);
        if (catalog.Entries.SingleOrDefault(row => row.Key == catalogKey)?.State
            != CharacterCurrentRuleEntryState.AvailableNotSelected) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentPower(
            repeated.context, catalog, source, catalogKey, rating, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Exact available option, original current state, and retained canonical quote.</summary>
    public CharacterCurrentMagicOptionQuote1949? QuoteSpell(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string catalogKey, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Spells);
        if (catalog.Entries.SingleOrDefault(row => row.Key == catalogKey)?.State
            != CharacterCurrentRuleEntryState.AvailableNotSelected) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentSpell(
            repeated.context, catalog, source, catalogKey, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Exact available option, original current state, and retained canonical quote.</summary>
    public CharacterCurrentMagicOptionQuote1949? QuoteComplexForm(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string catalogKey, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.ComplexForms);
        if (catalog.Entries.SingleOrDefault(row => row.Key == catalogKey)?.State
            != CharacterCurrentRuleEntryState.AvailableNotSelected) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentComplexForm(
            repeated.context, catalog, source, catalogKey, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Exact available option, original current state, and retained canonical quote.</summary>
    public CharacterCurrentLoadoutQuote1949? QuoteWeapon(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string catalogKey, int quantity, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Weapons);
        if (catalog.Entries.SingleOrDefault(row => row.Key == catalogKey)?.State
            != CharacterCurrentRuleEntryState.AvailableNotSelected) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentWeapon(
            repeated.context, catalog, source, catalogKey, quantity, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Exact available option, original current state, and retained canonical quote.</summary>
    public CharacterCurrentLoadoutQuote1949? QuoteArmor(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        string catalogKey, int quantity, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Armor);
        if (catalog.Entries.SingleOrDefault(row => row.Key == catalogKey)?.State
            != CharacterCurrentRuleEntryState.AvailableNotSelected) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentArmor(
            repeated.context, catalog, source, catalogKey, quantity, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    /// <summary>Fresh numeric lifestyle projection; it grants no owner or mutation permission.</summary>
    public CharacterCreationLifestyleProjection? QuoteLifestyle(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture retainedCurrentCapture, ICharacterFileQueries queries,
        CharacterCreationLifestyleConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var repeated = Reevaluate(current, retainedCurrentCapture, queries, cancellationToken);
        if (repeated is null || configuration is null) return null;
        var catalog = repeated.catalogs.Single(row => row.Domain == CharacterCurrentRuleDomain.Lifestyles);
        const string prefix = "lifestyle:";
        if (string.IsNullOrWhiteSpace(configuration.BaseLifestyleOptionId)
            || !configuration.BaseLifestyleOptionId.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParse(configuration.BaseLifestyleOptionId[prefix.Length..], out Guid sourceId)
            || !CharacterCurrentRuleEnvironmentConsumers.TryAvailablePurchaseEntry(catalog, sourceId, out _)) return null;
        using var scope = (repeated.resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var source = (scope ?? repeated.resolution.Resolver).TryCreateContext(current.Document.Content);
        if (source is null) return null;
        var result = CharacterCurrentRuleEnvironmentConsumers.QuoteCurrentCreationLifestyle(
            repeated.context, catalog, source, configuration, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result is not null && repeated.context.VerifyRetainedArchive() ? result : null;
    }

    private static string OutcomeDigest(IReadOnlyList<CharacterCurrentRuleCatalogValidation> catalogs,
        IReadOnlyList<CharacterCurrentRuleDomainExecution> executions)
        => CharacterCreationFinalizationDigest.Compute(new
        { Catalogs = catalogs.Select(row => row.ContentDigest).ToArray(), Executions = executions.Select(row => row.ContentDigest).ToArray() });
}

/// <summary>Fresh canonical read result, not a mutation plan or reusable wallet approval.</summary>
public sealed class CharacterCurrentRuleGearQuote
{
    internal CharacterCurrentRuleGearQuote(string binding, string capture, string key, decimal unit, decimal total,
        int availability, string legality, bool affordable)
        => (CurrentBindingDigest, CaptureDigest, CatalogKey, UnitCost, TotalCost, Availability, Legality, IsAffordable)
            = (binding, capture, key, unit, total, availability, legality, affordable);
    public string CurrentBindingDigest { get; }
    public string CaptureDigest { get; }
    public string CatalogKey { get; }
    public decimal UnitCost { get; }
    public decimal TotalCost { get; }
    public int Availability { get; }
    public string Legality { get; }
    public bool IsAffordable { get; }
}

public sealed class CharacterCurrentRuleCreationGearQuote
{
    internal CharacterCurrentRuleCreationGearQuote(string binding, string capture,
        IReadOnlyList<CharacterCreationGearLine> lines, CharacterCreationGearBudget budget)
        => (CurrentBindingDigest, CaptureDigest, Lines, Budget) = (binding, capture, lines, budget);
    public string CurrentBindingDigest { get; }
    public string CaptureDigest { get; }
    public IReadOnlyList<CharacterCreationGearLine> Lines { get; }
    public CharacterCreationGearBudget Budget { get; }
}

/// <summary>Canonical numeric source quote only, not a successful owner/GM mutation admission.</summary>
public sealed class CharacterCurrentRuleQualityQuote
{
    internal CharacterCurrentRuleQualityQuote(string binding, string capture, string key, int cost, int delta)
        => (CurrentBindingDigest, CaptureDigest, CatalogKey, RuleKarmaCost, KarmaDelta) = (binding, capture, key, cost, delta);
    public string CurrentBindingDigest { get; }
    public string CaptureDigest { get; }
    public string CatalogKey { get; }
    public int RuleKarmaCost { get; }
    public int KarmaDelta { get; }
}
