using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Hub;

public sealed class DefaultHubPublicationService : IHubPublicationService
{
    private readonly IHubDraftStore _draftStore;
    private readonly IHubModerationCaseStore _moderationCaseStore;
    private readonly IHubPublisherStore _publisherStore;

    public DefaultHubPublicationService(
        IHubDraftStore draftStore,
        IHubModerationCaseStore moderationCaseStore,
        IHubPublisherStore publisherStore)
    {
        _draftStore = draftStore;
        _moderationCaseStore = moderationCaseStore;
        _publisherStore = publisherStore;
    }

    public HubPublicationResult<HubPublishDraftList> ListDrafts(OwnerScope owner, string? kind = null, string? rulesetId = null, string? state = null)
    {
        IReadOnlyList<HubPublishDraftReceipt> items = _draftStore
            .List(owner, HubCatalogItemKinds.NormalizeOptional(kind), RulesetDefaults.NormalizeOptional(rulesetId), NormalizeStateOptional(state))
            .OrderByDescending(record => record.UpdatedAtUtc)
            .Select(ToReceipt)
            .ToArray();

        return HubPublicationResult<HubPublishDraftList>.Implemented(new HubPublishDraftList(items));
    }

    public HubPublicationResult<HubDraftDetailProjection?> GetDraft(OwnerScope owner, string draftId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);

        HubDraftRecord? draft = _draftStore.Get(owner, draftId);
        if (draft is null)
        {
            return HubPublicationResult<HubDraftDetailProjection?>.Implemented(null);
        }

        HubModerationCaseRecord? moderationCase = _moderationCaseStore.GetByDraftId(owner, draft.DraftId);
        HubModerationQueueItem? moderation = moderationCase is null
            ? null
            : new HubModerationQueueItem(
                CaseId: moderationCase.CaseId,
                DraftId: moderationCase.DraftId,
                ProjectKind: moderationCase.ProjectKind,
                ProjectId: moderationCase.ProjectId,
                RulesetId: moderationCase.RulesetId,
                Title: moderationCase.Title,
                OwnerId: moderationCase.OwnerId,
                PublisherId: moderationCase.PublisherId,
                State: moderationCase.State,
                CreatedAtUtc: moderationCase.CreatedAtUtc,
                Summary: moderationCase.Summary);

        return HubPublicationResult<HubDraftDetailProjection?>.Implemented(
            new HubDraftDetailProjection(
                Draft: ToReceipt(draft),
                Moderation: moderation,
                Description: draft.Description,
                LatestModerationNotes: moderationCase?.Notes,
                LatestModerationUpdatedAtUtc: moderationCase?.UpdatedAtUtc));
    }

    public HubPublicationResult<HubPublishDraftReceipt> CreateDraft(OwnerScope owner, HubPublishDraftRequest? request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string normalizedKind = HubCatalogItemKinds.NormalizeRequired(request.ProjectKind, nameof(request.ProjectKind));
        string normalizedProjectId = NormalizeProjectId(request.ProjectId);
        string normalizedTitle = NormalizeTitle(request.Title);
        string? normalizedSummary = NormalizeOptionalText(request.Summary);
        string? normalizedDescription = NormalizeOptionalText(request.Description);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(request.RulesetId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HubDraftRecord? existing = _draftStore.Get(owner, normalizedKind, normalizedProjectId, normalizedRulesetId);
        string? publisherId = ResolvePublisherId(owner, request.PublisherId, existing?.PublisherId);

        HubDraftRecord persisted = _draftStore.Upsert(
            owner,
            new HubDraftRecord(
                DraftId: existing?.DraftId ?? $"draft-{Guid.NewGuid():N}",
                ProjectKind: normalizedKind,
                ProjectId: normalizedProjectId,
                RulesetId: normalizedRulesetId,
                Title: normalizedTitle,
                Summary: normalizedSummary,
                Description: normalizedDescription,
                OwnerId: owner.NormalizedValue,
                PublisherId: publisherId,
                State: existing?.State ?? HubPublicationStates.Draft,
                CreatedAtUtc: existing?.CreatedAtUtc ?? now,
                UpdatedAtUtc: now,
                SubmittedAtUtc: existing?.SubmittedAtUtc));

        return HubPublicationResult<HubPublishDraftReceipt>.Implemented(ToReceipt(persisted));
    }

    public HubPublicationResult<HubPublishDraftReceipt?> UpdateDraft(OwnerScope owner, string draftId, HubUpdateDraftRequest? request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        ArgumentNullException.ThrowIfNull(request);

        HubDraftRecord? existing = _draftStore.Get(owner, draftId);
        if (existing is null)
        {
            return HubPublicationResult<HubPublishDraftReceipt?>.Implemented(null);
        }

        HubDraftRecord persisted = _draftStore.Upsert(
            owner,
            existing with
            {
                Title = NormalizeTitle(request.Title),
                Summary = NormalizeOptionalText(request.Summary),
                Description = NormalizeOptionalText(request.Description),
                PublisherId = ResolvePublisherId(owner, request.PublisherId, existing.PublisherId),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

        return HubPublicationResult<HubPublishDraftReceipt?>.Implemented(ToReceipt(persisted));
    }

    public HubPublicationResult<HubPublishDraftReceipt?> ArchiveDraft(OwnerScope owner, string draftId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);

        HubDraftRecord? existing = _draftStore.Get(owner, draftId);
        if (existing is null)
        {
            return HubPublicationResult<HubPublishDraftReceipt?>.Implemented(null);
        }

        HubDraftRecord archived = _draftStore.Upsert(
            owner,
            existing with
            {
                State = HubPublicationStates.Archived,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        _moderationCaseStore.DeleteByDraftId(owner, draftId);

        return HubPublicationResult<HubPublishDraftReceipt?>.Implemented(ToReceipt(archived));
    }

    public HubPublicationResult<bool> DeleteDraft(OwnerScope owner, string draftId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);

        bool deleted = _draftStore.Delete(owner, draftId);
        _moderationCaseStore.DeleteByDraftId(owner, draftId);
        return HubPublicationResult<bool>.Implemented(deleted);
    }

    public HubPublicationResult<HubProjectSubmissionReceipt> SubmitForReview(OwnerScope owner, string kind, string itemId, string? rulesetId, HubSubmitProjectRequest? request)
    {
        string normalizedKind = HubCatalogItemKinds.NormalizeRequired(kind, nameof(kind));
        string normalizedItemId = NormalizeProjectId(itemId);
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        HubDraftRecord? existing = ResolveDraft(owner, normalizedKind, normalizedItemId, normalizedRulesetId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string? publisherId = ResolvePublisherId(owner, request?.PublisherId, existing?.PublisherId);

        HubDraftRecord draft = _draftStore.Upsert(
            owner,
            new HubDraftRecord(
                DraftId: existing?.DraftId ?? $"draft-{Guid.NewGuid():N}",
                ProjectKind: normalizedKind,
                ProjectId: normalizedItemId,
                RulesetId: existing?.RulesetId ?? normalizedRulesetId ?? throw new InvalidOperationException("Ruleset id is required when submitting a project without an existing draft."),
                Title: existing?.Title ?? normalizedItemId,
                Summary: existing?.Summary,
                Description: existing?.Description,
                OwnerId: owner.NormalizedValue,
                PublisherId: publisherId,
                State: HubPublicationStates.Submitted,
                CreatedAtUtc: existing?.CreatedAtUtc ?? now,
                UpdatedAtUtc: now,
                SubmittedAtUtc: now));

        HubModerationCaseRecord? existingCase = _moderationCaseStore.Get(owner, normalizedKind, normalizedItemId, draft.RulesetId);
        HubModerationCaseRecord moderationCase = _moderationCaseStore.Upsert(
            owner,
            new HubModerationCaseRecord(
                CaseId: existingCase?.CaseId ?? $"case-{Guid.NewGuid():N}",
                DraftId: draft.DraftId,
                ProjectKind: draft.ProjectKind,
                ProjectId: draft.ProjectId,
                RulesetId: draft.RulesetId,
                Title: draft.Title,
                OwnerId: owner.NormalizedValue,
                PublisherId: draft.PublisherId,
                State: HubModerationStates.PendingReview,
                CreatedAtUtc: existingCase?.CreatedAtUtc ?? now,
                UpdatedAtUtc: now,
                Summary: request?.Notes,
                Notes: request?.Notes));

        return HubPublicationResult<HubProjectSubmissionReceipt>.Implemented(
            new HubProjectSubmissionReceipt(
                DraftId: draft.DraftId,
                CaseId: moderationCase.CaseId,
                ProjectKind: draft.ProjectKind,
                ProjectId: draft.ProjectId,
                RulesetId: draft.RulesetId,
                OwnerId: draft.OwnerId,
                PublisherId: draft.PublisherId,
                State: draft.State,
                ReviewState: moderationCase.State,
                Notes: request?.Notes,
                SubmittedAtUtc: draft.SubmittedAtUtc));
    }

    private HubDraftRecord? ResolveDraft(OwnerScope owner, string kind, string itemId, string? rulesetId)
    {
        if (rulesetId is not null)
        {
            return _draftStore.Get(owner, kind, itemId, rulesetId);
        }

        HubDraftRecord[] candidates = _draftStore.List(owner, kind: kind, state: null)
            .Where(record => string.Equals(record.ProjectId, itemId.Trim(), StringComparison.Ordinal))
            .ToArray();

        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new InvalidOperationException("Multiple drafts matched the submission request; specify a ruleset id explicitly.")
        };
    }

    private static HubPublishDraftReceipt ToReceipt(HubDraftRecord record)
        => new(
            DraftId: record.DraftId,
            ProjectKind: record.ProjectKind,
            ProjectId: record.ProjectId,
            RulesetId: record.RulesetId,
            Title: record.Title,
            Summary: record.Summary,
            OwnerId: record.OwnerId,
            PublisherId: record.PublisherId,
            State: record.State,
            CreatedAtUtc: record.CreatedAtUtc,
            UpdatedAtUtc: record.UpdatedAtUtc,
            SubmittedAtUtc: record.SubmittedAtUtc);

    private string? ResolvePublisherId(OwnerScope owner, string? requestedPublisherId, string? existingPublisherId)
    {
        string? normalizedRequestedPublisherId = NormalizePublisherIdOptional(requestedPublisherId);
        if (normalizedRequestedPublisherId is not null)
        {
            return _publisherStore.Get(owner, normalizedRequestedPublisherId) is null
                ? throw new ArgumentException($"Unknown publisher '{normalizedRequestedPublisherId}'.", nameof(requestedPublisherId))
                : normalizedRequestedPublisherId;
        }

        string? normalizedExistingPublisherId = NormalizePublisherIdOptional(existingPublisherId);
        if (normalizedExistingPublisherId is not null)
        {
            return normalizedExistingPublisherId;
        }

        HubPublisherRecord[] publishers = _publisherStore.List(owner).ToArray();
        return publishers.Length == 1
            ? publishers[0].PublisherId
            : null;
    }

    private static string NormalizeProjectId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string NormalizeTitle(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? NormalizeStateOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();

    private static string? NormalizePublisherIdOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();
}
