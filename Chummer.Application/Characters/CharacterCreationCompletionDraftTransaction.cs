using System.Diagnostics;
using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Source admission and exact composite receipt reconciliation. The file store owns the write lease.</summary>
public static class CharacterCreationCompletionDraftTransaction
{
    private static readonly ActivitySource Activities =
        new(CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.ActivitySourceName);

    // All entry points are synchronous. This scope follows only the current call
    // stack; it does not flow to tasks or survive the admitted operation.
    [ThreadStatic]
    private static HistoryValidationOperation? currentValidation;

    // Join only an operation already on this synchronous call stack. Callers
    // cannot supply proof flags or memo identities.
    public static IDisposable? EnterValidationOperation()
        => currentValidation is null ? BeginValidationOperation() : null;

    internal static IDisposable BeginValidationOperation()
    {
        var operation = new HistoryValidationOperation(currentValidation);
        currentValidation = operation;
        return operation;
    }

    private sealed class HistoryValidationOperation(HistoryValidationOperation? previous) : IDisposable
    {
        // Only canonical identities of fully replayed prefixes, never payloads,
        // caller-supplied verification flags, live sources or owner authority.
        internal Dictionary<string, long> VerifiedPrefixes { get; } = new(StringComparer.Ordinal);

        internal int ReplayDepth;
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            VerifiedPrefixes.Clear();
            currentValidation = previous;
        }
    }

    public static bool IsConfirmed(CharacterCreationCompletionDraftApplyRequest? request)
        => request is { Schema: CharacterCreationCompletionDraftApplyRequest.SchemaV1,
                ExplicitlyConfirmed: true, Review: { Binding: { } binding, Review.Request.Selections: not null } review }
            && IsKey(request.IdempotencyKey)
            && CharacterCreationFinalizationDigest.IsCanonical(review.OwnerStampDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(review.OwnerScopeDigest)
            && review.Schema == CharacterCreationCompletionDraftReview.SchemaV1
            && binding.ContentRevision is > 0 and < long.MaxValue && binding.SavedRevision == binding.ContentRevision
            && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
            && binding.BuildMethod is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen
            && CharacterCreationFinalizationDigest.IsCanonical(binding.RawCharacterXmlDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(binding.AuthorityDigest)
            && CharacterCareerReputationTransaction.IsDigest(binding.AuxiliaryStateDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(review.SelectedWitnessDigest)
            && review.Review.Blockers is { Count: 0 }
            && review.DraftReviewDigest == CharacterCreationCompletionDraftReviewRules.ComputeDigest(review);

    public static bool IsLookup(CharacterCreationCompletionDraftReceiptLookup? request)
        => request is { Schema: CharacterCreationCompletionDraftReceiptLookup.SchemaV1 }
            && !string.IsNullOrWhiteSpace(request.WorkspaceId.Value) && IsKey(request.IdempotencyKey)
            && CharacterCreationFinalizationDigest.IsCanonical(request.CommandDigest);

    private static bool IsKey(string? key)
        => key is { Length: >= 8 and <= 200 } && key == key.Trim() && !key.Any(char.IsControl);

    public static CharacterCreationCompletionDraftApplyRequest Freeze(CharacterCreationCompletionDraftApplyRequest request)
        => JsonSerializer.Deserialize<CharacterCreationCompletionDraftApplyRequest>(JsonSerializer.Serialize(request))
            ?? throw new InvalidDataException("Invalid composite draft command.");

    public static CharacterCreationCompletionDraftReceiptLookup LookupRequest(CharacterCreationCompletionDraftApplyRequest request)
        => new(CharacterCreationCompletionDraftReceiptLookup.SchemaV1, request.Review.Binding.WorkspaceId,
            request.IdempotencyKey, CharacterCreationCompletionDraftRules.CommandDigest(request));

    public static CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt>? Lookup(
        WorkspaceStoredDocument workspace, CharacterCreationCompletionDraftReceiptLookup request,
        ICharacterCreationRuleSourceRecovery? recovery = null)
    {
        if (!IsLookup(request) || workspace.Id != request.WorkspaceId
            || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(workspace.Id, workspace.ContentRevision, workspace.Document.AuxiliaryState, recovery))
            return Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        string key = CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(request.IdempotencyKey);
        var state = workspace.Document.AuxiliaryState;
        // Finalization retains the original draft ledger inside its ordinary archive.
        var entries = state.CharacterCreationCompletionDrafts ?? state.CharacterCreationFinalizationArchive?.State.CharacterCreationCompletionDrafts;
        var entry = entries?.SingleOrDefault(row => row.Receipt.IdempotencyKeyDigest == key);
        if (entry is not null)
            return entry.Receipt.CommandDigest == request.CommandDigest && workspace.CanReplayReceipt(entry.Receipt.ContentRevision)
                ? new(CharacterCreationFoundationOutcomes.Success, entry.Receipt, [])
                : Blocked(CharacterCreationFinalizationBlockers.IdempotencyConflict);
        if (state.CharacterCreationFinalizationReceipts?.Any(row => row.IdempotencyKeyDigest == key) == true)
            return Blocked(CharacterCreationFinalizationBlockers.IdempotencyConflict);
        return null;
    }

    public static bool HasKey(WorkspaceDocumentAuxiliaryState state, string key)
        => (state.CharacterCreationCompletionDrafts
            ?? state.CharacterCreationFinalizationArchive?.State.CharacterCreationCompletionDrafts)
            ?.Any(row => row.Receipt.IdempotencyKeyDigest == key) == true;

    public static bool TryBuild(IWorkspaceStore store, OwnerScope owner, WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries,
        CharacterCreationCompletionDraftApplyRequest request, CancellationToken cancellationToken,
        out WorkspaceDocument? replacement, out CharacterCreationCompletionDraftReceipt? receipt,
        Action<CharacterCreationRuleSourceExecutionContext>? capturedRuleSources = null,
        ICharacterCreationRuleSourceRecovery? recovery = null)
        => TryBuild(store, owner, workspace, resolver, queries, request, null, cancellationToken,
            out replacement, out receipt, capturedRuleSources, recovery);

    public static bool TryBuild(IWorkspaceStore store, OwnerScope owner, WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries,
        CharacterCreationCompletionDraftApplyRequest request,
        CharacterCreationCampaignExecutionContext? campaign, CancellationToken cancellationToken,
        out WorkspaceDocument? replacement, out CharacterCreationCompletionDraftReceipt? receipt,
        Action<CharacterCreationRuleSourceExecutionContext>? capturedRuleSources = null,
        ICharacterCreationRuleSourceRecovery? recovery = null)
    {
        using var validation = EnterValidationOperation();
        replacement = null; receipt = null;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!WorkspaceAuxiliaryStateIntegrity.IsValidShape(workspace.Id, workspace.ContentRevision,
                    workspace.Document.AuxiliaryState, recovery)) return false;
            if (!IsConfirmed(request) || request.Review.OwnerScopeDigest != CharacterCreationFinalizationDigest.Compute(owner) || workspace.ContentRevision != request.Review.Binding.ContentRevision
                || workspace.SavedRevision != request.Review.Binding.SavedRevision
                || workspace.Document.AuxiliaryStateDigest != request.Review.Binding.AuxiliaryStateDigest
                || CharacterCreationFinalizationDigest.ComputeUtf8(workspace.Document.Content) != request.Review.Binding.RawCharacterXmlDigest
                || workspace.Document.AuxiliaryState.CharacterCreationFinalizationReceipts is not null
                || workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive is not null)
                return false;
            var proposal = request.Review.Review.Request;
            var admittedResolver = resolver.ForCreationSourceExecution(proposal.RuleSourceExecutionSchema);
            if (admittedResolver is null) return false;
            using ICharacterSourceDataResolverOperationScope? scope =
                (admittedResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var live = scope ?? admittedResolver;
            var view = CharacterCreationCompletionTransaction.Observe(store, owner, workspace);
            if (!CharacterCreationAttributesReplacementExecution.AuthorizesCommit(owner, workspace, request)) return false;
            if ((proposal.CampaignAuthority is null) != (campaign is null)
                || campaign is not null && (!campaign.AuthorizesOriginal(owner.Value, workspace, proposal.CampaignAuthority,
                        CharacterCreationCampaignPhase.Apply, cancellationToken)
                    || proposal.RuleSourceExecutionSchema is null
                    || proposal.CampaignAuthority!.RetainedIdentity.SourceDigest != proposal.RuleSourceCaptureDigest)) return false;
            CharacterCreationRuleSourceExecutionContext? captured = null;
            var inputs = OwnerBoundCharacterCreationCompletionReader.LoadForProposal(view, live, queries,
                new(workspace.Id) { IncludeKarmaPurchasePolicy = proposal.Selections.SkillKarmaPurchases is not null },
                proposal, cancellationToken, captured: value => captured = new(value, queries));
            if (inputs.Outcome != CharacterCreationFoundationOutcomes.Success || inputs.Value is not { } loaded
                || live.TryCreateContext(workspace.Document.Content) is not { } context
                || !CharacterCreationCompletionBootstrapSnapshot.TryCapture(context, workspace.Document.Content, out var bootstrap))
                return false;
            var evaluated = CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.Evaluate(
                loaded, proposal, cancellationToken, captured, campaign);
            if (evaluated.Outcome != CharacterCreationFoundationOutcomes.Success || evaluated.Value is not { } reviewed)
                return false;
            var capture = reviewed.Capture(workspace, bootstrap, cancellationToken);
            if (capture.Outcome != CharacterCreationFoundationOutcomes.Success || capture.Value is not { } witness
                || !CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness, out var archive))
                return false;
            string authority = CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = CharacterCreationCompletionDraftReview.SchemaV1,
                loaded.SnapshotDigest, BootstrapDigest = bootstrap.SnapshotDigest, witness.WitnessDigest
            });
            var binding = new CharacterCreationFinalizationBinding(workspace.Id, workspace.ContentRevision,
                workspace.SavedRevision, CharacterCreationFinalizationDigest.ComputeUtf8(workspace.Document.Content),
                workspace.Document.AuxiliaryStateDigest, loaded.Skills.PrerequisiteDraft.BuildMethod, authority);
            var review = new CharacterCreationCompletionDraftReview(CharacterCreationCompletionDraftReview.SchemaV1,
                binding, reviewed.ReviewSnapshot, witness.WitnessDigest, string.Empty) { OwnerScopeDigest = request.Review.OwnerScopeDigest, OwnerStampDigest = request.Review.OwnerStampDigest };
            review = review with { DraftReviewDigest = CharacterCreationCompletionDraftReviewRules.ComputeDigest(review) };
            if (!Same(review, request.Review)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            var baseline = new CharacterCreationCompletionDraftBaseline(CharacterCreationCompletionDraftBaseline.SchemaV1,
                workspace.Document.AuxiliaryState with { CharacterCreationCompletionDrafts = null }, workspace.Document.Content,
                review, archive!, string.Empty);
            baseline = baseline with { BaselineDigest = CharacterCreationCompletionDraftRules.BaselineDigest(baseline) };
            var ledger = workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts;
            if (ledger is { Count: >= 128 }) return false;
            string key = CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(request.IdempotencyKey);
            string command = CharacterCreationCompletionDraftRules.CommandDigest(request);
            string id = CharacterCreationFinalizationDigest.Compute(new
            { Schema = CharacterCreationCompletionDraftReceipt.SchemaV1, WorkspaceId = workspace.Id, Key = key, Command = command });
            var result = new CharacterCreationCompletionDraftReceipt(CharacterCreationCompletionDraftReceipt.SchemaV1,
                id, workspace.Id, key, command, workspace.ContentRevision, checked(workspace.ContentRevision + 1),
                workspace.SavedRevision, checked(workspace.ContentRevision + 1),
                binding.RawCharacterXmlDigest, binding.RawCharacterXmlDigest, binding.AuxiliaryStateDigest,
                review.DraftReviewDigest, baseline.BaselineDigest, string.Empty);
            result = result with { ReceiptDigest = CharacterCreationCompletionDraftRules.ReceiptDigest(result) };
            var state = workspace.Document.AuxiliaryState with
            { CharacterCreationCompletionDrafts = [.. ledger ?? [], new(baseline, result)] };
            ICharacterCreationRuleSourceRecovery? admitted = captured is null ? recovery
                : new AdmittedCharacterCreationRuleSources(proposal.RuleSourceCaptureDigest!, captured, recovery);
            if (campaign is not null)
            {
                if (admitted is null) return false;
                admitted = CharacterCreationCampaignRecovery.WithCommittedDraft(
                    campaign.CaptureCommitted(result, cancellationToken), admitted);
            }
            if (!IsValidLedger(workspace.Id, result.ContentRevision, state, admitted)) return false;
            if (campaign is not null && !campaign.AuthorizesOriginal(owner.Value, workspace,
                    proposal.CampaignAuthority, CharacterCreationCampaignPhase.Apply, cancellationToken)) return false;
            replacement = workspace.Document with { State = workspace.Document.State with { AuxiliaryState = state } };
            receipt = result;
            if (captured is not null) capturedRuleSources?.Invoke(captured);
            return true;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or OverflowException or FormatException or System.Xml.XmlException)
        { return false; }
    }

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision, WorkspaceDocumentAuxiliaryState state,
        ICharacterCreationRuleSourceRecovery? recovery = null)
    {
        if (recovery is not null && !recovery.ValidateRetainedRuleSources()) return false;
        var entries = state.CharacterCreationCompletionDrafts;
        if (entries is null) return true;
        if (entries.Count is 0 or > 128 || state.CharacterCreationFinalizationReceipts is not null
            || state.CharacterCreationFinalizationArchive is not null) return false;
        using var operation = currentValidation is null ? BeginValidationOperation() : null;
        var validation = currentValidation!;
        if (validation.ReplayDepth >= 132) return false;
        validation.ReplayDepth++;
        try
        {
            string ledgerIdentity = PrefixIdentity(id, entries);
            if (validation.VerifiedPrefixes.TryGetValue(ledgerIdentity, out long minimumRevision))
                return revision >= minimumRevision;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            long previousRevision = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry?.Baseline is not { } baseline || entry.Receipt is not { } receipt
                    || baseline.Schema != CharacterCreationCompletionDraftBaseline.SchemaV1
                    || baseline.OriginalState is null || baseline.OriginalState.CharacterCreationCompletionDrafts is not null
                    || baseline.OriginalState.CharacterCreationFinalizationArchive is not null
                    || baseline.OriginalState.CharacterCreationFinalizationReceipts is not null
                    || receipt.Schema != CharacterCreationCompletionDraftReceipt.SchemaV1
                    || receipt.WorkspaceId != id || receipt.PreviousContentRevision < 1
                    || receipt.PreviousSavedRevision != receipt.PreviousContentRevision
                    || receipt.PreviousContentRevision < previousRevision
                    || receipt.ContentRevision != checked(receipt.PreviousContentRevision + 1)
                    || receipt.SavedRevision != receipt.ContentRevision || receipt.ContentRevision > revision
                    || !keys.Add(receipt.IdempotencyKeyDigest)
                    || !CharacterCreationFinalizationDigest.IsCanonical(receipt.CommandDigest)
                    || !CharacterCreationFinalizationDigest.IsCanonical(receipt.IdempotencyKeyDigest)
                    || receipt.RawCharacterXmlDigest != receipt.PreviousRawCharacterXmlDigest
                    || receipt.ReceiptDigest != CharacterCreationCompletionDraftRules.ReceiptDigest(receipt)
                    || receipt.BaselineDigest != baseline.BaselineDigest
                    || baseline.BaselineDigest != CharacterCreationCompletionDraftRules.BaselineDigest(baseline)
                    || baseline.Review is not { Review: { UpstreamBudget: not null, Lifestyles: not null, Contacts: not null, Blockers.Count: 0 } }
                    || !CharacterCreationFinalizationDigest.IsCanonical(baseline.Review.OwnerStampDigest)
                    || !CharacterCreationFinalizationDigest.IsCanonical(baseline.Review.OwnerScopeDigest)
                    || baseline.Review.Schema != CharacterCreationCompletionDraftReview.SchemaV1
                    || baseline.Review.Review.ReviewDigest != CharacterCreationCompletionReviewRules.ComputeReviewDigest(baseline.Review.Review)
                    || receipt.CommandDigest != CharacterCreationCompletionDraftRules.CommandDigest(
                        CharacterCreationCompletionDraftApplyRequest.SchemaV1, baseline.Review, receipt.IdempotencyKeyDigest, true)
                    || receipt.ReceiptId != CharacterCreationFinalizationDigest.Compute(new
                        { Schema = CharacterCreationCompletionDraftReceipt.SchemaV1, WorkspaceId = id,
                            Key = receipt.IdempotencyKeyDigest, Command = receipt.CommandDigest })
                    || receipt.DraftReviewDigest != baseline.Review.DraftReviewDigest
                    || baseline.Review.DraftReviewDigest != CharacterCreationCompletionDraftReviewRules.ComputeDigest(baseline.Review))
                    return false;
                var priorState = baseline.OriginalState with
                { CharacterCreationCompletionDrafts = i == 0 ? null : entries.Take(i).ToArray() };
                if (WorkspaceDocumentAuxiliaryStateDigest.Compute(priorState) != receipt.PreviousAuxiliaryStateDigest)
                    return false;
                var binding = baseline.Review.Binding;
                if (binding.WorkspaceId != id || binding.ContentRevision != receipt.PreviousContentRevision
                    || binding.SavedRevision != receipt.PreviousSavedRevision
                    || binding.RawCharacterXmlDigest != receipt.PreviousRawCharacterXmlDigest
                    || binding.AuxiliaryStateDigest != receipt.PreviousAuxiliaryStateDigest)
                    return false;
                // Hash the actual complete prefix, not caller-provided digests.
                // A proof covers these exact entries and their already-checked
                // ancestry; a changed byte must go through semantic replay again.
                string prefixIdentity = PrefixIdentity(id, entries.Take(i + 1).ToArray());
                if (validation.VerifiedPrefixes.ContainsKey(prefixIdentity))
                {
                    previousRevision = receipt.ContentRevision;
                    continue;
                }
                if (!CharacterCreationCompletionReplayCodec.TryDecode(baseline.SelectedReplay, out var bootstrap, out var witness)
                    || !bootstrap.IsValid() || witness.WitnessDigest != baseline.Review.SelectedWitnessDigest)
                    return false;
                using var replayActivity = Activities.StartActivity("DraftHistoryReplay");
                if (baseline.RawCharacterXml is not { Length: > 0 and <= CharacterCareerReputationProjector.MaximumCharacterXmlLength }
                    || CharacterCreationFinalizationDigest.ComputeUtf8(baseline.RawCharacterXml) != receipt.RawCharacterXmlDigest
                    || bootstrap.RawCharacterXmlDigest != receipt.RawCharacterXmlDigest
                    || !IsCreation(baseline.RawCharacterXml)
                    || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(id, receipt.PreviousContentRevision, baseline.OriginalState, recovery))
                    return false;
                var document = new WorkspaceDocument(baseline.RawCharacterXml, "sr5");
                document = document with { State = document.State with { AuxiliaryState = priorState } };
                var original = new WorkspaceStoredDocument(id, document, receipt.PreviousContentRevision,
                    receipt.PreviousSavedRevision, DateTimeOffset.UnixEpoch);
                var proposal = baseline.Review.Review.Request;
                CharacterCreationRuleSourceExecutionContext? restored = null;
                if (proposal.RuleSourceExecutionSchema is not null
                    && (recovery is null || !recovery.TryRecoverRuleSources(proposal.RuleSourceCaptureDigest!, out restored)
                        || restored is null)) return false;
                CharacterCreationCampaignExecutionContext? campaign = null;
                if (proposal.CampaignAuthority is { } campaignBinding
                    && (proposal.RuleSourceExecutionSchema is null
                        || campaignBinding.RetainedIdentity.SourceDigest != proposal.RuleSourceCaptureDigest
                        || recovery is null
                        || !recovery.TryRecoverCampaignAuthority(campaignBinding, receipt.ReceiptDigest, out campaign)
                        || campaign is not { IsCommittedReplay: true, Phase: CharacterCreationCampaignPhase.Replay }
                        || !campaign.AuthorizesOriginal(campaignBinding.RetainedIdentity.OwnerId, original,
                            campaignBinding, CharacterCreationCampaignPhase.Replay))) return false;
                if (!CharacterCreationAttributesReplacementExecution.TryEnterReplay(original,
                        baseline.Review, witness, out var replacementReplay)) return false;
                using var replacementScope = replacementReplay;
                var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness,
                    proposal, ruleSources: restored, campaign: campaign);
                if (replay.Outcome != CharacterCreationFoundationOutcomes.Success || replay.Value is not { } selected
                    || !CharacterCreationSelectedCompletionEvaluator.MatchesFullReview(selected, baseline.Review.Review)
                    || binding.AuthorityDigest != CharacterCreationFinalizationDigest.Compute(new
                    {
                        Schema = CharacterCreationCompletionDraftReview.SchemaV1,
                        SnapshotDigest = witness.ProvenanceInputsSnapshotDigest,
                        BootstrapDigest = bootstrap.SnapshotDigest, witness.WitnessDigest
                    })) return false;
                // Publish only after all nested shape, prefix and semantic checks.
                validation.VerifiedPrefixes[prefixIdentity] = receipt.ContentRevision;
                previousRevision = receipt.ContentRevision;
            }
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException
            or OverflowException or FormatException or IOException or System.Xml.XmlException or NullReferenceException) { return false; }
        finally { validation.ReplayDepth--; }
    }

    private static string PrefixIdentity(CharacterWorkspaceId id, IReadOnlyList<CharacterCreationCompletionDraftEntry> entries)
        => CharacterCreationFinalizationDigest.Compute(new { WorkspaceId = id, Entries = entries });

    public static bool IsValidCurrentDocument(WorkspaceDocument document, long revision)
    {
        var entries = document.AuxiliaryState.CharacterCreationCompletionDrafts;
        if (entries is null) return true;
        if (entries.Count == 0 || !IsCreation(document.Content)) return false;
        // Source-bound pending choices cannot silently follow an unrelated raw
        // character replacement. Typed future editor transitions must rebase them.
        var state = document.AuxiliaryState;
        if (entries[^1]?.Baseline?.OriginalState is not { } origin || entries[^1].Receipt is null)
            return false;
        // Only implemented typed successors may differ from the origin. Other
        // stages retain the serialization guard. Current source and shared-budget
        // admission belongs to the owner-bound ordinary Confirm operation.
        return entries[^1].Receipt.ContentRevision <= revision
            && CharacterCreationFinalizationDigest.ComputeUtf8(document.Content) == entries[^1].Receipt.RawCharacterXmlDigest
            && Same(state.CharacterCreationBootstrapBinding, origin.CharacterCreationBootstrapBinding)
            && Same(state.CharacterCreationPrerequisiteDraft, origin.CharacterCreationPrerequisiteDraft)
            && AdmitsMagicSuccessor(document, revision, entries[^1])
            && AdmitsQualitiesSuccessor(document, revision, entries[^1])
            && AdmitsResourcesSuccessor(document, revision, entries[^1])
            && AdmitsGearSuccessor(document, revision, entries[^1])
            && AdmitsContactsSuccessor(document, revision, entries[^1])
            && AdmitsLifestylesSuccessor(document, revision, entries[^1])
            && AdmitsLoadoutSuccessor(document, revision, entries[^1])
            && CharacterCreationCompositePurchaseEdit.HasValidHistory(state);
    }


    private static bool Extends<T>(IReadOnlyList<T>? before, IReadOnlyList<T>? after)
        => after is not null && after.Count > (before?.Count ?? 0)
            && Same(before ?? [], after.Take(before?.Count ?? 0).ToArray());

    private static bool AdmitsMagicSuccessor(WorkspaceDocument document, long revision,
        CharacterCreationCompletionDraftEntry entry)
    {
        var state = document.AuxiliaryState;
        var origin = entry.Baseline.OriginalState;
        if (Same(state.CharacterCreationMagicResonanceDraft, origin.CharacterCreationMagicResonanceDraft)
            && Same(state.CharacterCreationMagicResonanceReceipts, origin.CharacterCreationMagicResonanceReceipts))
            return true;
        var draft = state.CharacterCreationMagicResonanceDraft;
        var receipts = state.CharacterCreationMagicResonanceReceipts;
        return draft is not null && draft.BaseContentRevision >= entry.Receipt.ContentRevision
            && draft.BaseContentRevision < revision
            && draft.BaseRawCharacterXmlDigest == entry.Receipt.RawCharacterXmlDigest
            && draft.DraftDigest == CharacterCreationMagicResonanceDraftIntegrity.ComputeDigest(draft)
            && Extends(origin.CharacterCreationMagicResonanceReceipts, receipts)
            && CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(receipts, entry.Receipt.WorkspaceId, revision)
            && receipts![^1].PreviousContentRevision == draft.BaseContentRevision
            && receipts[^1].DraftDigest == draft.DraftDigest
            && receipts[^1].PreviewDigest == draft.LastPreviewDigest
            && receipts[^1].CommandDigest == draft.LastCommandDigest
            && receipts[^1].IdempotencyKeyDigest == draft.LastIdempotencyKeyDigest;
    }

    private static bool AdmitsQualitiesSuccessor(WorkspaceDocument document, long revision,
        CharacterCreationCompletionDraftEntry entry)
    {
        var state = document.AuxiliaryState;
        var origin = entry.Baseline.OriginalState;
        if (Same(state.CharacterCreationQualitiesDraft, origin.CharacterCreationQualitiesDraft)
            && Same(state.CharacterCreationQualitiesReceipts, origin.CharacterCreationQualitiesReceipts))
            return true;
        var draft = state.CharacterCreationQualitiesDraft;
        return draft is not null && draft.BaseContentRevision >= entry.Receipt.ContentRevision
            && draft.BaseContentRevision < revision
            && draft.BaseRawCharacterXmlDigest == entry.Receipt.RawCharacterXmlDigest
            && Extends(origin.CharacterCreationQualitiesReceipts, state.CharacterCreationQualitiesReceipts)
            && CharacterCreationQualitiesReceiptLedgerIntegrity.IsValidLedger(
                entry.Receipt.WorkspaceId, revision, draft, state.CharacterCreationQualitiesReceipts);
    }


    private static bool AdmitsResourcesSuccessor(WorkspaceDocument document, long revision,
        CharacterCreationCompletionDraftEntry entry)
    {
        var state = document.AuxiliaryState;
        var origin = entry.Baseline.OriginalState;
        if (Same(state.CharacterCreationResourcesDraft, origin.CharacterCreationResourcesDraft)
            && Same(state.CharacterCreationResourcesReceipts, origin.CharacterCreationResourcesReceipts)) return true;
        var draft = state.CharacterCreationResourcesDraft;
        return draft is not null && draft.BaseContentRevision >= entry.Receipt.ContentRevision
            && draft.BaseContentRevision < revision
            && draft.BaseRawCharacterXmlDigest == entry.Receipt.RawCharacterXmlDigest
            && Extends(origin.CharacterCreationResourcesReceipts, state.CharacterCreationResourcesReceipts)
            && CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(entry.Receipt.WorkspaceId,
                revision, draft, state.CharacterCreationResourcesReceipts);
    }


    private static bool AdmitsGearSuccessor(WorkspaceDocument document, long revision,
        CharacterCreationCompletionDraftEntry entry)
    {
        var state = document.AuxiliaryState;
        var origin = entry.Baseline.OriginalState;
        if (Same(state.CharacterCreationGearDraft, origin.CharacterCreationGearDraft)
            && Same(state.CharacterCreationGearReceipts, origin.CharacterCreationGearReceipts)) return true;
        var draft = state.CharacterCreationGearDraft;
        return draft is not null && draft.BaseContentRevision >= entry.Receipt.ContentRevision
            && draft.BaseContentRevision < revision
            && draft.BaseRawCharacterXmlDigest == entry.Receipt.RawCharacterXmlDigest
            && CharacterCreationCompositePurchaseEdit.Matches(entry, draft.CompositePurchase)
            && Extends(origin.CharacterCreationGearReceipts, state.CharacterCreationGearReceipts)
            && CharacterCreationGearReceiptLedgerIntegrity.IsValidLedger(entry.Receipt.WorkspaceId,
                revision, draft, state.CharacterCreationGearReceipts);
    }


    private static bool AdmitsLoadoutSuccessor(WorkspaceDocument document, long revision,
        CharacterCreationCompletionDraftEntry entry)
    {
        var state = document.AuxiliaryState;
        var origin = entry.Baseline.OriginalState;
        if (Same(state.CharacterCreationLoadoutDraft, origin.CharacterCreationLoadoutDraft)
            && Same(state.CharacterCreationLoadoutReceipts, origin.CharacterCreationLoadoutReceipts)) return true;
        var draft = state.CharacterCreationLoadoutDraft;
        return draft is not null && draft.BaseContentRevision >= entry.Receipt.ContentRevision
            && draft.BaseContentRevision < revision
            && draft.RawCharacterXmlDigest == entry.Receipt.RawCharacterXmlDigest
            && CharacterCreationCompositePurchaseEdit.Matches(entry, draft.CompositePurchase)
            && Extends(origin.CharacterCreationLoadoutReceipts, state.CharacterCreationLoadoutReceipts)
            && CharacterCreationLoadoutReceiptLedgerIntegrity.IsValidLedger(entry.Receipt.WorkspaceId,
                revision, draft, state.CharacterCreationLoadoutReceipts);
    }

    private static bool AdmitsContactsSuccessor(WorkspaceDocument document, long revision,
        CharacterCreationCompletionDraftEntry entry)
    {
        var state = document.AuxiliaryState;
        var origin = entry.Baseline.OriginalState;
        if (Same(state.CharacterCreationContactsDraft, origin.CharacterCreationContactsDraft)
            && Same(state.CharacterCreationContactReceipts, origin.CharacterCreationContactReceipts)) return true;
        var draft = state.CharacterCreationContactsDraft;
        var receipts = state.CharacterCreationContactReceipts;
        return draft is not null && draft.BaseContentRevision >= entry.Receipt.ContentRevision
            && draft.RawCharacterXmlDigest == entry.Receipt.RawCharacterXmlDigest
            && CharacterCreationContactsDraftRules.IsValidShape(entry.Receipt.WorkspaceId, revision, draft)
            && Extends(origin.CharacterCreationContactReceipts, receipts)
            && receipts is { Count: > 0 }
            && CharacterCreationContactReceiptLedgerIntegrity.IsValidLedger(entry.Receipt.WorkspaceId, revision, receipts)
            && receipts[^1].Receipt.PreviousContentRevision == draft.BaseContentRevision
            && Same(receipts[^1].Receipt.WritePlan.PendingDraft, draft)
            && CharacterCreationFinalizationDigest.IsCanonical(receipts[^1].Receipt.WritePlan.CompositeReviewDigest);
    }

    private static bool AdmitsLifestylesSuccessor(WorkspaceDocument document, long revision,
        CharacterCreationCompletionDraftEntry entry)
    {
        var state = document.AuxiliaryState;
        var origin = entry.Baseline.OriginalState;
        if (Same(state.CharacterCreationLifestylesDraft, origin.CharacterCreationLifestylesDraft)
            && Same(state.CharacterCreationLifestyleReceipts, origin.CharacterCreationLifestyleReceipts)) return true;
        var draft = state.CharacterCreationLifestylesDraft;
        var receipts = state.CharacterCreationLifestyleReceipts;
        return draft is not null && draft.BaseContentRevision >= entry.Receipt.ContentRevision
            && draft.InputAuthority.RawCharacterXmlDigest == entry.Receipt.RawCharacterXmlDigest
            && CharacterCreationLifestylesDraftRules.IsValidShape(entry.Receipt.WorkspaceId, revision, draft)
            && Extends(origin.CharacterCreationLifestyleReceipts, receipts)
            && receipts is { Count: > 0 }
            && CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidLedger(entry.Receipt.WorkspaceId, revision, receipts)
            && receipts[^1].Receipt.PreviousContentRevision == draft.BaseContentRevision
            && Same(receipts[^1].Receipt.WritePlan.PendingDraft, draft)
            && CharacterCreationFinalizationDigest.IsCanonical(receipts[^1].Receipt.WritePlan.CompositeReviewDigest);
    }

    private static bool IsCreation(string xml)
    {
        using var input = new StringReader(xml);
        using var reader = System.Xml.XmlReader.Create(input, new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = CharacterCareerReputationProjector.MaximumCharacterXmlLength
        });
        var root = System.Xml.Linq.XDocument.Load(reader).Root;
        return root?.Name == "character" && root.Elements("created").ToArray() is [var created]
            && !created.HasAttributes && !created.HasElements
            && bool.TryParse(created.Value, out bool value) && !value;
    }

    public static CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> Blocked(string blocker)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
}
