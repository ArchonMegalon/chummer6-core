using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Exact live admission and historical reconstruction. Storage alone owns the atomic write.</summary>
public static class CharacterCreationCompletionTransaction
{
    public static string AuthorityDigest(CharacterCreationCompletionAuthority authority)
        => CharacterCreationFinalizationDigest.Compute(authority with { AuthorityDigest = string.Empty });

    public static bool IsConfirmed(CharacterCreationCompletionApplyRequest? request)
        => request is { ExplicitlyConfirmed: true, Binding: { } binding, Proposal: { Selections: not null } proposal,
                StartingCash: { DiceTotal: >= 0 } cash }
            && binding.BuildMethod is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen
            && binding.ContentRevision is > 0 and < long.MaxValue && binding.SavedRevision == binding.ContentRevision
            && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
            && CharacterCreationFinalizationDigest.IsCanonical(binding.RawCharacterXmlDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(binding.AuthorityDigest)
            && CharacterCareerReputationTransaction.IsDigest(binding.AuxiliaryStateDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(proposal.InputsSnapshotDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(request.CompletionReviewDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(request.ProjectionDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(cash.SourceAuthorityDigest)
            && request.IdempotencyKey is { Length: >= 8 and <= 200 }
            && request.IdempotencyKey == request.IdempotencyKey.Trim()
            && !request.IdempotencyKey.Any(char.IsControl);

    public static CharacterCreationCompletionApplyRequest Freeze(CharacterCreationCompletionApplyRequest request)
        => JsonSerializer.Deserialize<CharacterCreationCompletionApplyRequest>(JsonSerializer.Serialize(request))
            ?? throw new InvalidDataException("Completion command could not be detached.");

    public static string CommandDigest(CharacterCreationCompletionApplyRequest request)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.character-creation-completion-command.v1",
            request.Binding, request.Proposal, request.CompletionReviewDigest, request.StartingCash,
            request.ProjectionDigest, request.ExplicitlyConfirmed
        });

    public static bool TryAdmit(IWorkspaceStore admittedStore, OwnerScope owner, WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationStartingCashChoice startingCash,
        CancellationToken cancellationToken,
        out CharacterCreationCompletionAuthority? authority,
        out CharacterCreationCompletionFinalizationProjection? projection, out IReadOnlyList<string> blockers)
    {
        authority = null; projection = null;
        blockers = [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid];
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // A new real source observation for every admission, including the one
            // after temp-file flush. Archived sources are never passed here.
            using ICharacterSourceDataResolverOperationScope? scope =
                (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var live = scope ?? resolver;
            var context = live.TryCreateContext(workspace.Document.Content);
            if (context is null || !CharacterCreationCompletionBootstrapSnapshot.TryCapture(
                    context, workspace.Document.Content, out var bootstrap)) return false;
            var view = new CompletionObservationStore(admittedStore, owner, workspace);
            // Retained purchases validate their actual saved draft graph and
            // resolve source authority over its read-only projected XML too.
            // Keep the real operation-scoped resolver, as the ordinary owner
            // completion reader does; an original-XML-only resolver would reject
            // that historical admission. No proposed draft is supplied here.
            var skills = new CharacterCreationSkillsService(view, live).LoadCompletionInputs(new(workspace.Id));
            if (skills.Value is null) { blockers = skills.Blockers; return false; }
            cancellationToken.ThrowIfCancellationRequested();
            var magic = new CharacterCreationMagicResonanceService(view, live).LoadCompletionInputs(new(workspace.Id));
            if (magic.Value is null) { blockers = magic.Blockers; return false; }
            cancellationToken.ThrowIfCancellationRequested();
            var lifestyles = new CharacterCreationLifestylesService(view, live, queries).LoadCompletionInputs(new(workspace.Id));
            if (lifestyles.Value is null) { blockers = lifestyles.Blockers; return false; }
            cancellationToken.ThrowIfCancellationRequested();
            var contacts = new CharacterCreationContactsService(view, live, queries).LoadCompletionInputs(new(workspace.Id));
            if (contacts.Value is null) { blockers = contacts.Blockers; return false; }
            var inputs = new CharacterCreationCompletionReviewInputs(CharacterCreationCompletionReviewSchemas.InputsV1,
                skills.Value, magic.Value, lifestyles.Value, contacts.Value, string.Empty);
            if (proposal.Selections.Loadout is not null)
            {
                var loadout = CharacterCreationLoadoutCompletionService.LoadFromObservation(workspace, live,
                    requireAuthority: true);
                if (loadout.Outcome != CharacterCreationFoundationOutcomes.Success || loadout.Value is null)
                { blockers = loadout.Blockers; return false; }
                inputs = inputs with { Schema = CharacterCreationCompletionReviewSchemas.InputsV2, Loadout = loadout.Value };
            }
            inputs = inputs with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs) };
            var evaluated = CharacterCreationCompletionFinalizationProjector.ReviewProjectAndCapture(workspace, bootstrap, inputs,
                proposal, sourceId => TryResolveStartingCash(context, sourceId, out var source) ? source : null,
                startingCash, cancellationToken);
            if (evaluated.Outcome != CharacterCreationFoundationOutcomes.Success || evaluated.Value is not { } prepared)
            { blockers = evaluated.Blockers; return false; }
            var cashSource = prepared.Projection.StartingCash.Source;
            if (prepared.Witness is not { } witness)
            { blockers = [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid]; return false; }
            var selected = ProjectSelected(workspace, prepared.Bootstrap, witness, prepared.Proposal,
                cashSource, startingCash, cancellationToken);
            // A compact history must reproduce the entire full-source result,
            // including XML, source anchors, deterministic IDs and every delta.
            // An internally consistent selected budget alone is insufficient.
            if (selected.Outcome != CharacterCreationFoundationOutcomes.Success || selected.Value is null
                || !Same(selected.Value, prepared.Projection)
                || !CharacterCreationCompletionReplayCodec.TryEncode(prepared.Bootstrap, witness, out var replay))
            { blockers = [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid]; return false; }
            // The captured context must still represent the same source/profile.
            // Keep this after capture, replay and compression, not before them.
            if (!CharacterCreationCompletionBootstrapSnapshot.TryCapture(context, workspace.Document.Content, out var after)
                || after.SnapshotDigest != bootstrap.SnapshotDigest
                || !TryResolveStartingCash(context, prepared.StartingLifestyleSourceId, out var cashAfter)
                || !Same(cashSource, cashAfter))
            { blockers = [CharacterCreationCompletionReviewSchemas.SnapshotConflict]; return false; }
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = new CharacterCreationCompletionAuthority(CharacterCreationCompletionAuthority.SchemaV3,
                workspace.Document.Content, null, null, prepared.Proposal, cashSource, startingCash, string.Empty)
                { SelectedReplay = replay };
            authority = candidate with { AuthorityDigest = AuthorityDigest(candidate) };
            projection = prepared.Projection;
            blockers = [];
            return true;
        }
        catch (Exception error) when (IsAdmissionFailure(error)) { return false; }
    }

    public static CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>? Lookup(
        WorkspaceStoredDocument workspace, CharacterCreationCompletionApplyRequest request)
    {
        var ledger = workspace.Document.AuxiliaryState.CharacterCreationFinalizationReceipts;
        if (ledger is null) return null;
        if (!IsConfirmed(request) || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(workspace.Id,
                workspace.ContentRevision, workspace.Document.AuxiliaryState))
            return Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        string key = CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(request.IdempotencyKey);
        var entry = ledger.SingleOrDefault(item => item.IdempotencyKeyDigest == key);
        if (entry is null) return Blocked(CharacterCreationFinalizationBlockers.CharacterAlreadyCreated);
        return entry.Receipt.CompletionAuthorityDigest is not null
            && entry.CommandDigest == CommandDigest(request)
            && workspace.CanReplayReceipt(entry.Receipt.ContentRevision)
            ? new(CharacterCreationFoundationOutcomes.Success, entry.Receipt, [])
            : Blocked(CharacterCreationFinalizationBlockers.IdempotencyConflict);
    }

    public static bool TryBuild(IWorkspaceStore admittedStore, OwnerScope owner, WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries,
        CharacterCreationCompletionApplyRequest request, CancellationToken cancellationToken,
        out WorkspaceDocument? replacement, out CharacterCreationFinalizationReceipt? receipt)
    {
        replacement = null; receipt = null;
        if (!IsConfirmed(request) || Lookup(workspace, request) is not null
            || !TryAdmit(admittedStore, owner, workspace, resolver, queries, request.Proposal, request.StartingCash,
                cancellationToken, out var authority, out var projection, out _)
            || projection!.Binding != request.Binding || projection.ReviewDigest != request.CompletionReviewDigest
            || projection.ProjectionDigest != request.ProjectionDigest) return false;
        string key = CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(request.IdempotencyKey);
        string command = CommandDigest(request);
        var result = new CharacterCreationFinalizationReceipt(CharacterCreationFinalizationSchemas.ReceiptV1,
            ReceiptId(workspace.Id, key, command), workspace.Id, key, command,
            workspace.ContentRevision, workspace.ContentRevision + 1,
            workspace.SavedRevision, workspace.ContentRevision + 1, projection.Binding.RawCharacterXmlDigest,
            projection.ExpectedResultRawCharacterXmlDigest, workspace.Document.AuxiliaryStateDigest,
            projection.Binding.AuthorityDigest, projection.ReviewDigest, projection.ProjectionDigest,
            projection.Binding.BuildMethod, true, true, CharacterCreationFinalizationDigest.ReceiptLedgerRootDigest,
            string.Empty)
        {
            CarryoverPolicy = projection.CarryoverPolicy,
            StartingCash = request.StartingCash,
            StartingCashAuthorityDigest = projection.StartingCash.AuthorityDigest,
            CompletionAuthorityDigest = authority!.AuthorityDigest
        };
        result = result with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(result) };
        var auxiliary = new WorkspaceDocumentAuxiliaryState(
            CharacterCreationFinalizationReceipts: [new(key, command, result)],
            CharacterCreationFinalizationArchive: new(workspace.Document.AuxiliaryState)
            { CompletionAuthority = authority });
        if (!WorkspaceAuxiliaryStateIntegrity.IsValidShape(workspace.Id, result.ContentRevision, auxiliary)) return false;
        replacement = workspace.Document with { State = workspace.Document.State with
            { Payload = projection.CharacterXml, AuxiliaryState = auxiliary } };
        receipt = result;
        return true;
    }

    public static bool IsValidArchive(CharacterWorkspaceId id, CharacterCreationFinalizationArchive archive,
        CharacterCreationFinalizationReceipt receipt)
    {
        var authority = archive.CompletionAuthority;
        if (receipt.CompletionAuthorityDigest is null) return authority is null;
        if (authority is not { RawCharacterXml.Length: > 0 }
            || authority.Schema is not (CharacterCreationCompletionAuthority.SchemaV2 or CharacterCreationCompletionAuthority.SchemaV3)
            || archive.KarmaAuthority is not null || archive.LifeModuleAuthority is not null || archive.StartingCash is not null
            || receipt.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
            || !CharacterCreationFinalizationDigest.IsCanonical(authority.AuthorityDigest)) return false;
        try
        {
            if (receipt.CompletionAuthorityDigest != authority.AuthorityDigest
                || authority.AuthorityDigest != AuthorityDigest(authority)) return false;
            var document = new WorkspaceDocument(authority.RawCharacterXml, "sr5");
            document = document with { State = document.State with { AuxiliaryState = archive.State } };
            var original = new WorkspaceStoredDocument(id, document, receipt.PreviousContentRevision,
                receipt.PreviousSavedRevision, DateTimeOffset.UnixEpoch);
            if (document.AuxiliaryStateDigest != receipt.PreviousAuxiliaryStateDigest
                || CharacterCreationFinalizationDigest.ComputeUtf8(authority.RawCharacterXml) != receipt.PreviousRawCharacterXmlDigest)
                return false;
            CharacterCreationCompletionFinalizationProjection projection;
            if (authority.Schema == CharacterCreationCompletionAuthority.SchemaV2)
            {
                // Preserve the original full-envelope reader and byte identity.
                // A V2 history must never borrow V3's reduced source semantics.
                if (authority.SelectedReplay is not null || authority.Inputs is null
                    || authority.Inputs.Loadout is not null || authority.Proposal.Selections.Loadout is not null
                    || authority.BootstrapSourceSnapshot is not { ValueKind: JsonValueKind.Object } snapshot)
                    return false;
                var bootstrap = snapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>();
                if (bootstrap is null || !bootstrap.IsValid()
                    || !Same(snapshot, JsonSerializer.SerializeToElement(bootstrap))) return false;
                var full = CharacterCreationCompletionFinalizationProjector.ReviewAndProject(original, bootstrap,
                    authority.Inputs, authority.Proposal, _ => authority.StartingCashSource, authority.StartingCash);
                if (full.Outcome != CharacterCreationFoundationOutcomes.Success || full.Value is null) return false;
                projection = full.Value.Projection;
            }
            else
            {
                if (authority.Inputs is not null || authority.BootstrapSourceSnapshot is not null
                    || !CharacterCreationCompletionReplayCodec.TryDecode(authority.SelectedReplay,
                        out var bootstrap, out var witness) || !bootstrap.IsValid()) return false;
                var selected = ProjectSelected(original, bootstrap, witness, authority.Proposal,
                    authority.StartingCashSource, authority.StartingCash);
                if (selected.Outcome != CharacterCreationFoundationOutcomes.Success || selected.Value is null) return false;
                projection = selected.Value;
            }
            var request = new CharacterCreationCompletionApplyRequest(projection.Binding, authority.Proposal,
                projection.ReviewDigest, authority.StartingCash, projection.ProjectionDigest, "historical-validation", true);
            return IsConfirmed(request) && projection.Binding.WorkspaceId == id
                && projection.Binding.ContentRevision == receipt.PreviousContentRevision
                && projection.Binding.SavedRevision == receipt.PreviousSavedRevision
                && projection.Binding.AuxiliaryStateDigest == receipt.PreviousAuxiliaryStateDigest
                && projection.Binding.BuildMethod == receipt.BuildMethod
                && projection.Binding.AuthorityDigest == receipt.AuthorityDigest
                && projection.ReviewDigest == receipt.PreviewDigest && projection.ProjectionDigest == receipt.PlanDigest
                && projection.ExpectedResultRawCharacterXmlDigest == receipt.RawCharacterXmlDigest
                && Same(projection.CarryoverPolicy, receipt.CarryoverPolicy)
                && projection.StartingCash.Choice == receipt.StartingCash
                && projection.StartingCash.AuthorityDigest == receipt.StartingCashAuthorityDigest
                && CommandDigest(request) == receipt.CommandDigest
                && ReceiptId(id, receipt.IdempotencyKeyDigest, receipt.CommandDigest) == receipt.ReceiptId;
        }
        catch (Exception error) when (IsAdmissionFailure(error)) { return false; }
    }

    public static CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> Blocked(string blocker)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);

    private static CharacterCreationFoundationResult<CharacterCreationCompletionFinalizationProjection> ProjectSelected(
        WorkspaceStoredDocument original, CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest proposal,
        CharacterCreationStartingNuyenSource startingCashSource, CharacterCreationStartingCashChoice startingCash,
        CancellationToken cancellationToken = default)
    {
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, proposal,
            cancellationToken);
        if (replay.Outcome != CharacterCreationFoundationOutcomes.Success || replay.Value is not { } evaluated
            || replay.Blockers.Count != 0)
            return new(replay.Outcome, null, replay.Blockers);
        if (evaluated.Balances.RemainingCreationKarma is not int remainingKarma
            || evaluated.Balances.RemainingNuyen is not decimal remainingNuyen)
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationFinalizationBlockers.GlobalKarmaExceeded]);
        // Full input/review digests remain explicit original provenance seeds.
        // They are not reattached as the digest of a trimmed full authority.
        var graph = new CharacterCreationCompletionFinalizationProjector.EvaluatedGraphInputs(
            original, witness.ProvenanceInputsSnapshotDigest, witness.ProvenanceReviewDigest,
            bootstrap.SnapshotDigest, witness.Skills.PrerequisiteDraft, evaluated.Skills,
            witness.Skills.SelectedActiveSkillPoints, witness.Skills.SelectedSkillGroupPoints,
            witness.Skills.Authority.SourceAnchorIds,
            new(evaluated.Qualities.Selections, evaluated.Qualities.KarmaRemaining, evaluated.Qualities.SourceAnchorIds),
            new(evaluated.Resources.SelectedOption, evaluated.Resources.Budget),
            new(evaluated.Gear, evaluated.GearBudget, evaluated.GearSourceAnchorIds),
            new(evaluated.Lifestyles.Authority, evaluated.Lifestyles.Lifestyles,
                evaluated.Lifestyles.StartingLifestyleId, evaluated.Lifestyles.SourceAnchorIds),
            new(witness.ContactsPolicy, proposal.Selections.Contacts), witness.CarryoverPolicy,
            remainingKarma, remainingNuyen,
            new CharacterCreationCompletionFinalizationProjector.SelectedAwakenedGraphInputs(
                witness.Magic, witness.Skills.Attributes.Attributes, evaluated.Magic.Proposed.SourceAnchorIds))
            { Loadout = evaluated.Loadout };
        return CharacterCreationCompletionFinalizationProjector.ProjectEvaluated(graph,
            startingCashSource, startingCash, cancellationToken);
    }

    private static bool TryResolveStartingCash(ICharacterSourceDataContext context,
        Guid? lifestyleSourceId, out CharacterCreationStartingNuyenSource? source)
        => (lifestyleSourceId is Guid sourceId
            ? context.TryResolveCreationPriorityStartingNuyen(sourceId, out source)
            : context.TryResolveCreationDefaultStartingNuyen(out source)) && source is not null;

    private static string ReceiptId(CharacterWorkspaceId id, string key, string command)
        => CharacterCreationFinalizationDigest.Compute(new
        { Schema = CharacterCreationFinalizationSchemas.ReceiptV1, Id = id, idempotencyDigest = key, commandDigest = command });

    private static bool Same<T>(T left, T right)
        => CharacterCreationFinalizationDigest.Compute(left!) == CharacterCreationFinalizationDigest.Compute(right!);

    private static bool IsAdmissionFailure(Exception error)
        => error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException
            or System.Xml.XmlException or OverflowException or FormatException or JsonException;

    /// <summary>
    /// Carries the actual admitted store's capability observation to the ordinary
    /// source loaders, without giving them an Apply or write capability. Unlike a
    /// hypothetical continuation view, this keeps the exact store observation,
    /// including LocalHistory. Its caller holds the real owner/workspace lease.
    /// No Get is reissued against the file store while its operation lease is held.
    /// </summary>
    private sealed class CompletionObservationStore(
        IWorkspaceStore admittedStore, OwnerScope owner, WorkspaceStoredDocument observation)
        : IWorkspaceStore, IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit
        {
            get
            {
                if (string.IsNullOrWhiteSpace(owner.Value)
                    || owner.UsesLocalSingleUserValue && !owner.IsLocalSingleUser) return false;
                // This existing view translates an exact linked-owner lease into
                // local-shaped evaluator methods. Its cached Get also checks that
                // the lease is still active and belongs to this observation.
                if (admittedStore is OwnerBoundCreationWorkspaceStore scoped)
                    return scoped.SupportsWorkspaceAuxiliaryStateAtomicCommit
                        && ReferenceEquals(scoped.Get(owner, observation.Id).Value, observation);
                return owner.IsLocalSingleUser
                    ? admittedStore is IWorkspaceAuxiliaryStateAtomicCommitCapability
                        { SupportsWorkspaceAuxiliaryStateAtomicCommit: true }
                    : admittedStore is IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
                        { SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit: true };
            }
        }

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
            => id == observation.Id
                && (admittedStore is not OwnerBoundCreationWorkspaceStore scoped
                    || ReferenceEquals(scoped.Get(owner, observation.Id).Value, observation))
                ? new(WorkspaceOperationOutcome.Success, observation)
                : new(WorkspaceOperationOutcome.Missing);
        public WorkspaceStoreReadResult Get(OwnerScope requestedOwner, CharacterWorkspaceId id)
            => requestedOwner == owner ? Get(id) : new(WorkspaceOperationOutcome.Missing);
        public IReadOnlyList<WorkspaceStoreEntry> List() => [];
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope requestedOwner) => [];
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope requestedOwner, WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id,
            long expectedContentRevision, WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope requestedOwner, CharacterWorkspaceId id,
            long expectedContentRevision, WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id, long expectedContentRevision, string expectedAuxiliaryStateDigest,
            WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long expectedContentRevision) => Denied();
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope requestedOwner, CharacterWorkspaceId id,
            long expectedContentRevision) => Denied();
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision) => Denied();
        public WorkspaceStoreMutationResult Delete(OwnerScope requestedOwner, CharacterWorkspaceId id,
            long expectedContentRevision) => Denied();
        private static WorkspaceStoreMutationResult Denied()
            => new(WorkspaceOperationOutcome.Unavailable, Error: "Completion admission is read-only.");
    }
}
