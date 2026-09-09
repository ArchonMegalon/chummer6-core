using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

internal sealed record WorkspaceContinuationDomainCheck(
    string Domain, bool Evaluated, IReadOnlyList<string> Blockers,
    IReadOnlyList<string> ReadinessBlockers);

/// <summary>
/// A bounded read-only observation, never a durable restore/replay authorization.
/// Even valid historical receipt hashes do not authenticate their origin.
/// </summary>
internal sealed record WorkspaceContinuationCandidateEvaluation(
    WorkspaceContinuationExport? Candidate, string? SourceDigest, bool HistoryConsistent,
    IReadOnlyList<WorkspaceContinuationDomainCheck> DomainChecks,
    IReadOnlyList<string> BoundaryBlockers)
{
    public bool CurrentDraftChecksPassed => Candidate is not null && SourceDigest is not null
        && HistoryConsistent && BoundaryBlockers.Count == 0
        && DomainChecks.All(check => check.Evaluated && check.Blockers.Count == 0);
    public bool RestoreAuthorized => false;
    public bool HistoricalProvenanceVerified => false;
}

/// <summary>
/// Recomputes every present Creation draft from Core services against one private
/// candidate and captured source view. No target store is supplied: reads cannot
/// fall back to another owner, and no store mutation or replay can be executed.
/// Atomic restore must separately reacquire owner, sources and destination CAS.
/// </summary>
internal sealed class WorkspaceContinuationCandidateEvaluator(
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver,
    ICharacterFileQueries characterQueries,
    ILifeModulesCatalogService lifeModules)
{
    public WorkspaceContinuationCandidateEvaluation Evaluate(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> bytes, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Failure("owner-authority-unavailable");
        using (lease)
        {
            if (!WorkspaceContinuationCodec.TryDecodeCandidate(bytes, maximumBytes, out var candidate))
                return Failure("continuation-wire-invalid");
            if (!string.Equals(candidate!.Snapshot.OwnerId,
                    expectedOwner.Owner.NormalizedValue, StringComparison.Ordinal))
                return Failure("continuation-owner-mismatch");

            var snapshot = candidate.Snapshot.Workspace;
            var workspace = new WorkspaceStoredDocument(snapshot.Id, snapshot.Document,
                snapshot.ContentRevision, snapshot.SavedRevision, snapshot.LastUpdatedUtc);
            bool historyConsistent = WorkspaceContinuationHistoryIntegrity.TryValidate(
                expectedOwner.Owner, candidate.Snapshot);
            if (!historyConsistent)
                return Failure("continuation-history-inconsistent", candidate);

            WorkspaceDocument document = workspace.Document;
            if (document.Format != WorkspaceDocumentFormat.NativeXml
                || document.SchemaVersion != 1
                || document.PayloadKind is not ("workspace" or "sr5/chum5-xml")
                || !string.Equals(document.RulesetId, "sr5", StringComparison.Ordinal))
                return Failure("continuation-runtime-unsupported", candidate, historyConsistent);

            var checks = new List<WorkspaceContinuationDomainCheck>();
            var boundary = new List<string>();
            string? sourceDigest = null;
            try
            {
                // Source files are not a route for parsing external XML entities.
                using var reader = XmlReader.Create(new StringReader(document.Content), new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
                });
                XElement root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root
                    ?? throw new InvalidDataException("Missing character root.");
                if (root.Name != "character" || root.HasAttributes)
                    return Failure("continuation-character-invalid", candidate, historyConsistent);
                var character = new CharacterDocument(document.Content);
                CharacterValidationResult validation = characterQueries.Validate(character);
                if (!validation.IsValid)
                    return Failure("continuation-character-invalid", candidate, historyConsistent);
                CharacterFileSummary summary = characterQueries.ParseSummary(character);
                // Reject ambiguous lifecycle/container selection before any first-node
                // parser can disagree with another domain about the candidate.
                foreach (string element in new[] { "created", "contacts", "lifestyles" })
                    if (root.Elements(element).Skip(1).Any())
                        return Failure("continuation-character-ambiguous", candidate, historyConsistent);

                ICharacterSourceDataContext? context = sourceResolver.TryCreateContext(document.Content);
                if (context is null || !WorkspaceContinuationSourceCapture.TryCapture(
                        context, document.Content, lifeModules, out var sources))
                    return Failure("continuation-sources-unavailable", candidate, historyConsistent);
                sourceDigest = sources.Digest;
                ICharacterSourceDataResolver frozen = sources.CreateResolver();
                var view = new WorkspaceContinuationReadView(expectedOwner.Owner, workspace);
                var prerequisite = new CharacterCreationPrerequisiteService(view, characterQueries, frozen);
                var attributes = new CharacterCreationAttributesService(view, frozen);
                WorkspaceDocumentAuxiliaryState auxiliary = document.AuxiliaryState;
                // Either representation makes bootstrap validation mandatory.
                // Removing its auxiliary binding must not turn a marked pending
                // character into an apparently empty, valid continuation.
                bool hasBootstrapState = CharacterCreationBootstrapAuthority.HasBootstrapState(document);

                if (summary.Created && (hasBootstrapState || HasActiveDrafts(auxiliary)))
                    boundary.Add("active-creation-drafts-on-career-character");

                if (hasBootstrapState)
                    Run("bootstrap", () =>
                    {
                        bool valid = CharacterCreationBootstrapAuthority.TryValidatePending(
                            workspace, frozen, out var blockers);
                        return Check("bootstrap", valid, blockers);
                    });
                if (auxiliary.CharacterCreationFoundationDraft is not null)
                    Run("foundation", () =>
                    {
                        var foundation = new CharacterCreationFoundationService(view, characterQueries,
                            frozen, sources.LifeModules, new CharacterCreationFoundationDraftApplyAuthority(view));
                        return Check("foundation", true, foundation.ValidateContinuationDraft(workspace));
                    });
                if (auxiliary.CharacterCreationPrerequisiteDraft is not null)
                    Run("prerequisite", () =>
                    {
                        var result = prerequisite.Load(new(workspace.Id));
                        return Check("prerequisite", result.Value?.PendingDraft is not null, result.Blockers,
                            CharacterCreationPrerequisiteBlockers.PersistenceAuthorityRequired,
                            CharacterCreationPrerequisiteBlockers.CharacterAlreadyCreated,
                            CharacterCreationPrerequisiteBlockers.DependentAttributesDraftExists);
                    });
                if (auxiliary.CharacterCreationAttributesDraft is not null)
                    Run("attributes", () =>
                    {
                        var result = attributes.Load(new(workspace.Id));
                        return Check("attributes", result.Value?.PendingDraft is not null, result.Blockers,
                            CharacterCreationAttributesBlockers.PersistenceAuthorityRequired);
                    });
                if (auxiliary.CharacterCreationSkillsDraft is not null)
                    Run("skills", () =>
                    {
                        var result = new CharacterCreationSkillsService(view, frozen)
                            .LoadForContinuation(new(workspace.Id));
                        return Check("skills", result.Value?.PendingDraft is not null, result.Blockers,
                            CharacterCreationSkillsBlockers.PersistenceAuthorityRequired);
                    });
                if (auxiliary.CharacterCreationMagicResonanceDraft is not null)
                    Run("magic-resonance", () =>
                    {
                        var result = new CharacterCreationMagicResonanceService(view, frozen)
                            .LoadForContinuation(new(workspace.Id));
                        return Check("magic-resonance", result.Value?.PendingDraft is not null, result.Blockers,
                            CharacterCreationMagicResonanceBlockers.PersistenceAuthorityRequired);
                    });
                if (auxiliary.CharacterCreationQualitiesDraft is not null)
                    Run("qualities", () =>
                    {
                        var result = new CharacterCreationQualitiesService(view, frozen, prerequisite, attributes)
                            .Load(new(workspace.Id));
                        string[] readiness = QualitiesCheckpointIsOnlyReadiness(result.Value?.Binding)
                            ? [CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired,
                               CharacterCreationQualitiesBlockers.CreatedCharacter,
                               CharacterCreationQualitiesBlockers.RevisionConflict]
                            : [CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired,
                               CharacterCreationQualitiesBlockers.CreatedCharacter];
                        return Check("qualities", result.Value?.PendingDraft is not null, result.Blockers, readiness);
                    });
                if (auxiliary.CharacterCreationResourcesDraft is not null)
                    Run("resources", () => Check("resources", true,
                        new CharacterCreationResourcesService(view, frozen).ValidateContinuationDraft(workspace)));
                if (auxiliary.CharacterCreationGearDraft is not null)
                    Run("gear", () =>
                    {
                        var result = new CharacterCreationGearService(view, frozen).Load(new(workspace.Id));
                        return Check("gear", result.Value?.PendingDraft is not null, result.Blockers,
                            CharacterCreationGearBlockers.PersistenceAuthorityRequired,
                            CharacterCreationGearBlockers.CareerModeRejected);
                    });

                // These two Creation domains store typed decisions in the XML.
                // Their archived receipts are not reinterpreted as active Career drafts.
                if (!summary.Created && (auxiliary.CharacterCreationContactReceipts is not null
                        || root.Elements("contacts").Any(container => container.HasElements)))
                    Run("contacts", () =>
                    {
                        var result = new CharacterCreationContactsService(view)
                            .Load(expectedOwner.Owner, new(workspace.Id));
                        return Check("contacts", result.Value is not null, result.Blockers,
                            CharacterCreationContactsBlockers.PersistenceAuthorityRequired,
                            CharacterCreationContactsBlockers.CareerModeRejected);
                    });
                if (!summary.Created && (auxiliary.CharacterCreationLifestyleReceipts is not null
                        || root.Elements("lifestyles").Any(container => container.HasElements)))
                    Run("lifestyles", () =>
                    {
                        var result = new CharacterCreationLifestylesService(view, frozen).Load(new(workspace.Id));
                        IEnumerable<string> blockers = result.Blockers;
                        // Load projects overspend without adding Preview's funds blocker.
                        if (result.Value?.Budget.Overspend > 0)
                            blockers = blockers.Append(CharacterCreationLifestylesBlockers.InsufficientFunds);
                        return Check("lifestyles", result.Value is not null, blockers,
                            CharacterCreationLifestylesBlockers.PersistenceAuthorityRequired,
                            CharacterCreationLifestylesBlockers.CareerModeRejected);
                    });
                if (summary.Created)
                    Run("career-reputation", () =>
                    {
                        bool valid = CharacterCareerReputationProjector.TryReadForContinuation(
                            workspace, frozen, out var projection, out string error);
                        return Check("career-reputation", valid && projection is not null,
                            valid ? [] : [error]);
                    });

                // Observe all sources again through a new resolver context. A cached
                // candidate capture is not proof that the producer is still current.
                ICharacterSourceDataContext? current = sourceResolver.TryCreateContext(document.Content);
                if (current is null || !WorkspaceContinuationSourceCapture.TryCapture(
                        current, document.Content, lifeModules, out var finalSources)
                    || !string.Equals(sourceDigest, finalSources.Digest, StringComparison.Ordinal))
                    boundary.Add("continuation-sources-changed");

                // Hash against bounded captured bytes again: an internal consumer must
                // not have mutated the supposedly private candidate during evaluation.
                _ = WorkspaceContinuationCodec.Encode(candidate, maximumBytes);
            }
            catch (Exception exception) when (IsMalformedOrUnavailable(exception))
            {
                boundary.Add("continuation-evaluation-unavailable");
            }
            return new(candidate, sourceDigest, historyConsistent,
                checks.OrderBy(check => check.Domain, StringComparer.Ordinal).ToArray(),
                boundary.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray());

            void Run(string domain, Func<WorkspaceContinuationDomainCheck> evaluate)
            {
                try { checks.Add(evaluate()); }
                catch (Exception exception) when (IsMalformedOrUnavailable(exception))
                {
                    checks.Add(Check(domain, false, ["continuation-domain-unavailable"]));
                }
            }
        }
    }

    private static bool HasActiveDrafts(WorkspaceDocumentAuxiliaryState state) =>
        state.CharacterCreationBootstrapBinding is not null
        || state.CharacterCreationFoundationDraft is not null
        || state.CharacterCreationPrerequisiteDraft is not null
        || state.CharacterCreationAttributesDraft is not null
        || state.CharacterCreationSkillsDraft is not null
        || state.CharacterCreationMagicResonanceDraft is not null
        || state.CharacterCreationQualitiesDraft is not null
        || state.CharacterCreationResourcesDraft is not null
        || state.CharacterCreationGearDraft is not null;

    private static bool QualitiesCheckpointIsOnlyReadiness(CharacterCreationQualitiesBinding? binding) =>
        binding is not null && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
        && binding.ContentRevision > 0 && binding.SavedRevision > 0
        && binding.SavedRevision < binding.ContentRevision
        && CharacterCreationQualitiesRules.IsCanonicalDigest(binding.RawCharacterXmlDigest)
        && DelegatedGmCharacterEditLedgerValidator.IsSha256(binding.AuxiliaryStateDigest);

    private static WorkspaceContinuationDomainCheck Check(
        string domain, bool evaluated, IEnumerable<string> blockers, params string[] readiness)
    {
        string[] all = blockers.Distinct(StringComparer.Ordinal).ToArray();
        var blocking = all.Where(code => !readiness.Contains(code, StringComparer.Ordinal)).ToList();
        if (!evaluated && blocking.Count == 0)
            blocking.Add("continuation-domain-unavailable");
        return new(domain, evaluated,
            blocking.OrderBy(code => code, StringComparer.Ordinal).ToArray(),
            all.Where(code => readiness.Contains(code, StringComparer.Ordinal))
                .OrderBy(code => code, StringComparer.Ordinal).ToArray());
    }

    private static bool IsMalformedOrUnavailable(Exception exception) => exception is
        ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException
        or XmlException or JsonException or FormatException or OverflowException
        or NullReferenceException or IndexOutOfRangeException or KeyNotFoundException or NotSupportedException;

    private static WorkspaceContinuationCandidateEvaluation Failure(
        string blocker, WorkspaceContinuationExport? candidate = null, bool historyConsistent = false) =>
        new(candidate, null, historyConsistent, [], [blocker]);
}
