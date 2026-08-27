using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

/// <summary>
/// Deterministic read/preview/confirm/restart boundary for the live Origin
/// Dossier. It persists a user-owned view of the authoritative story ledger;
/// mechanics still advance only through <see cref="LifeModuleOriginDossierService"/>.
/// </summary>
public sealed class LifeModuleOriginDossierInteractionService
{
    private readonly LifeModuleOriginDossierService _dossier;

    public LifeModuleOriginDossierInteractionService(
        LifeModuleOriginDossierService dossier)
    {
        _dossier = dossier ?? throw new ArgumentNullException(nameof(dossier));
    }

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Start(
        string workspaceId)
    {
        LifeModuleOriginDossierResult<OriginStoryArcSeed> projected =
            _dossier.Project(workspaceId);
        if (!IsSuccess(projected.Outcome) || projected.Value is not { } projection)
            return Map<OriginStoryArcSeed, LifeModuleOriginDossierDraftCheckpoint>(projected);

        OriginLtdNarrativeProvenance provenance = CreateNotRequestedProvenance(
            projection.SeedDigest);
        return new(
            LifeModuleOriginDossierOutcomes.Success,
            SealCheckpoint(projection, null, provenance),
            []);
    }

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Restore(
        LifeModuleOriginDossierDraftCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!TryValidateCheckpoint(checkpoint))
            return Blocked<LifeModuleOriginDossierDraftCheckpoint>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.ProjectionInvalid);

        LifeModuleOriginDossierResult<OriginStoryArcSeed> resumed =
            _dossier.Resume(checkpoint.Projection);
        return !IsSuccess(resumed.Outcome) || resumed.Value is null
            ? Map<OriginStoryArcSeed, LifeModuleOriginDossierDraftCheckpoint>(resumed)
            : new(
                LifeModuleOriginDossierOutcomes.Success,
                checkpoint,
                []);
    }

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Prepare(
        LifeModuleOriginDossierDraftCheckpoint checkpoint,
        string choiceId)
    {
        LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> restored =
            Restore(checkpoint);
        if (!IsSuccess(restored.Outcome) || restored.Value is not { } current)
            return restored;

        LifeModuleNarrativeChoiceSeed[] matches = current.Projection.CurrentTurn.LegalChoices
            .Where(candidate => string.Equals(candidate.ChoiceId, choiceId, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1
            || !matches[0].IsLegal
            || matches[0].Blockers.Count != 0)
        {
            return Blocked<LifeModuleOriginDossierDraftCheckpoint>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.IllegalChoice);
        }

        LifeModuleOriginDossierDecisionPreview preview = SealPreview(
            current.Projection,
            matches[0],
            current.LtdProvenance);
        return new(
            LifeModuleOriginDossierOutcomes.Success,
            SealCheckpoint(current.Projection, preview, current.LtdProvenance),
            []);
    }

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierInteractionAdvance> Confirm(
        LifeModuleOriginDossierDraftCheckpoint checkpoint,
        string expectedPreviewDigest,
        string idempotencyKey,
        bool explicitlyConfirmed)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!TryValidateCheckpoint(checkpoint)
            || checkpoint.PendingPreview is not { } pending
            || !DigestsEqual(pending.PreviewDigest, expectedPreviewDigest))
        {
            return Blocked<LifeModuleOriginDossierInteractionAdvance>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.ProjectionInvalid);
        }

        LifeModuleOriginDossierResult<LifeModuleOriginDossierAdvance> accepted =
            _dossier.Accept(
                checkpoint.Projection,
                pending.SelectedChoice.ChoiceId,
                idempotencyKey,
                explicitlyConfirmed);
        if (!IsSuccess(accepted.Outcome) || accepted.Value is not { } advance)
            return Map<LifeModuleOriginDossierAdvance, LifeModuleOriginDossierInteractionAdvance>(accepted);

        OriginLtdNarrativeProvenance provenance = CreateNotRequestedProvenance(
            advance.Projection.SeedDigest);
        LifeModuleOriginDossierDraftCheckpoint next = SealCheckpoint(
            advance.Projection,
            null,
            provenance);
        return new(
            LifeModuleOriginDossierOutcomes.Success,
            new(next, advance.AcceptedDecision),
            []);
    }

    private static OriginLtdNarrativeProvenance CreateNotRequestedProvenance(
        string boundSeedDigest)
    {
        var provenance = new OriginLtdNarrativeProvenance(
            OriginDossierSchemas.LtdProvenanceV1,
            OriginLtdProvenanceStates.NotRequested,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            boundSeedDigest,
            false,
            string.Empty);
        return provenance with
        {
            ProvenanceDigest = ComputeProvenanceDigest(provenance)
        };
    }

    private static LifeModuleOriginDossierDecisionPreview SealPreview(
        OriginStoryArcSeed projection,
        LifeModuleNarrativeChoiceSeed choice,
        OriginLtdNarrativeProvenance provenance)
    {
        var card = new LifeModuleOriginDossierChoiceCard(
            choice.ChoiceId,
            choice.Label,
            choice.Source,
            choice.PageReference,
            choice.MechanicsPreview,
            choice.SourceAnchorIds.ToArray(),
            choice.ChoiceDigest,
            string.Empty);
        card = card with { CardDigest = ComputeCardDigest(card) };
        var preview = new LifeModuleOriginDossierDecisionPreview(
            OriginDossierSchemas.DecisionPreviewV1,
            projection.CurrentTurn.OwnerId,
            projection.CurrentTurn.WorkspaceId,
            projection.CurrentTurn.WorkspaceRevision,
            projection.CurrentTurn.TurnId,
            projection.CurrentTurn.VisibleStoryMarkdown,
            projection.CurrentTurn.DecisionPrompt,
            card,
            provenance,
            projection.SeedDigest,
            projection.CurrentTurn.DecisionDigest,
            projection.CanonicalLayer.MechanicsSnapshotDigest,
            string.Empty);
        return preview with { PreviewDigest = ComputePreviewDigest(preview) };
    }

    private static LifeModuleOriginDossierDraftCheckpoint SealCheckpoint(
        OriginStoryArcSeed projection,
        LifeModuleOriginDossierDecisionPreview? pending,
        OriginLtdNarrativeProvenance provenance)
    {
        var checkpoint = new LifeModuleOriginDossierDraftCheckpoint(
            OriginDossierSchemas.DraftCheckpointV1,
            projection.CurrentTurn.OwnerId,
            projection.CurrentTurn.WorkspaceId,
            projection.CurrentTurn.WorkspaceRevision,
            projection,
            projection.VisibleChapters.Select(static chapter => chapter.ChapterDigest).ToArray(),
            pending,
            provenance,
            projection.SeedDigest,
            projection.CurrentTurn.DecisionGraphDigest,
            projection.CurrentTurn.ContentDigest,
            projection.CurrentTurn.SourceDigest,
            projection.CurrentTurn.RulesDigest,
            projection.CurrentTurn.RuntimeDigest,
            projection.CanonicalLayer.MechanicsSnapshotDigest,
            string.Empty);
        return checkpoint with
        {
            CheckpointDigest = ComputeCheckpointDigest(checkpoint)
        };
    }

    private static bool TryValidateCheckpoint(
        LifeModuleOriginDossierDraftCheckpoint checkpoint)
    {
        OriginStoryArcSeed? projection = checkpoint.Projection;
        LifeModuleNarrativeTurnSeed? turn = projection?.CurrentTurn;
        if (!string.Equals(checkpoint.Schema, OriginDossierSchemas.DraftCheckpointV1, StringComparison.Ordinal)
            || projection is null
            || turn is null
            || checkpoint.TimelineChapterDigests is null
            || checkpoint.LtdProvenance is null
            || !string.Equals(checkpoint.OwnerId, turn.OwnerId, StringComparison.Ordinal)
            || !string.Equals(checkpoint.WorkspaceId, turn.WorkspaceId, StringComparison.Ordinal)
            || checkpoint.WorkspaceRevision != turn.WorkspaceRevision
            || !checkpoint.TimelineChapterDigests.SequenceEqual(
                projection.VisibleChapters.Select(static chapter => chapter.ChapterDigest),
                StringComparer.Ordinal)
            || !DigestsEqual(checkpoint.BoundSeedDigest, projection.SeedDigest)
            || !DigestsEqual(checkpoint.BoundDecisionGraphDigest, turn.DecisionGraphDigest)
            || !DigestsEqual(checkpoint.BoundContentDigest, turn.ContentDigest)
            || !DigestsEqual(checkpoint.BoundSourceDigest, turn.SourceDigest)
            || !DigestsEqual(checkpoint.BoundRulesDigest, turn.RulesDigest)
            || !DigestsEqual(checkpoint.BoundRuntimeDigest, turn.RuntimeDigest)
            || !DigestsEqual(
                checkpoint.BoundMechanicsSnapshotDigest,
                projection.CanonicalLayer.MechanicsSnapshotDigest)
            || !TryValidateProvenance(checkpoint.LtdProvenance, projection.SeedDigest)
            || checkpoint.PendingPreview is { } pending
               && !TryValidatePreview(pending, projection, checkpoint.LtdProvenance)
            || !DigestsEqual(checkpoint.CheckpointDigest, ComputeCheckpointDigest(checkpoint)))
        {
            return false;
        }
        return true;
    }

    private static bool TryValidatePreview(
        LifeModuleOriginDossierDecisionPreview preview,
        OriginStoryArcSeed projection,
        OriginLtdNarrativeProvenance provenance)
    {
        if (preview is null
            || preview.SelectedChoice is null
            || preview.LtdProvenance is null)
        {
            return false;
        }
        LifeModuleNarrativeChoiceSeed[] matches = projection.CurrentTurn.LegalChoices
            .Where(choice => string.Equals(
                choice.ChoiceId,
                preview.SelectedChoice.ChoiceId,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            return false;
        LifeModuleOriginDossierDecisionPreview expected = SealPreview(
            projection,
            matches[0],
            provenance);
        return DigestsEqual(preview.PreviewDigest, expected.PreviewDigest)
               && DigestsEqual(preview.SelectedChoice.CardDigest, expected.SelectedChoice.CardDigest);
    }

    private static bool TryValidateProvenance(
        OriginLtdNarrativeProvenance provenance,
        string seedDigest)
        => string.Equals(provenance.Schema, OriginDossierSchemas.LtdProvenanceV1, StringComparison.Ordinal)
           && string.Equals(provenance.State, OriginLtdProvenanceStates.NotRequested, StringComparison.Ordinal)
           && string.IsNullOrEmpty(provenance.ProviderId)
           && string.IsNullOrEmpty(provenance.ProviderModelId)
           && string.IsNullOrEmpty(provenance.ProviderRouteReceiptDigest)
           && string.IsNullOrEmpty(provenance.ProposalDigest)
           && !provenance.IsVerified
           && DigestsEqual(provenance.BoundSeedDigest, seedDigest)
           && DigestsEqual(provenance.ProvenanceDigest, ComputeProvenanceDigest(provenance));

    private static string ComputeCardDigest(LifeModuleOriginDossierChoiceCard card)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("choiceId", card.ChoiceId);
            writer.WriteString("label", card.Label);
            writer.WriteString("source", card.Source);
            writer.WriteString("pageReference", card.PageReference);
            writer.WriteString("mechanicsPreviewDigest", card.MechanicsPreview.PreviewDigest);
            WriteStringArray(writer, "sourceAnchorIds", card.SourceAnchorIds);
            writer.WriteString("choiceDigest", card.ChoiceDigest);
            writer.WriteEndObject();
        });

    private static string ComputeProvenanceDigest(OriginLtdNarrativeProvenance provenance)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schema", provenance.Schema);
            writer.WriteString("state", provenance.State);
            writer.WriteString("providerId", provenance.ProviderId);
            writer.WriteString("providerModelId", provenance.ProviderModelId);
            writer.WriteString("providerRouteReceiptDigest", provenance.ProviderRouteReceiptDigest);
            writer.WriteString("proposalDigest", provenance.ProposalDigest);
            writer.WriteString("boundSeedDigest", provenance.BoundSeedDigest);
            writer.WriteBoolean("isVerified", provenance.IsVerified);
            writer.WriteEndObject();
        });

    private static string ComputePreviewDigest(LifeModuleOriginDossierDecisionPreview preview)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schema", preview.Schema);
            writer.WriteString("ownerId", preview.OwnerId);
            writer.WriteString("workspaceId", preview.WorkspaceId);
            writer.WriteNumber("workspaceRevision", preview.WorkspaceRevision);
            writer.WriteString("turnId", preview.TurnId);
            writer.WriteString("visibleStoryMarkdown", preview.VisibleStoryMarkdown);
            writer.WriteString("decisionPrompt", preview.DecisionPrompt);
            writer.WriteString("cardDigest", preview.SelectedChoice.CardDigest);
            writer.WriteString("provenanceDigest", preview.LtdProvenance.ProvenanceDigest);
            writer.WriteString("boundSeedDigest", preview.BoundSeedDigest);
            writer.WriteString("boundDecisionDigest", preview.BoundDecisionDigest);
            writer.WriteString("boundMechanicsSnapshotDigest", preview.BoundMechanicsSnapshotDigest);
            writer.WriteEndObject();
        });

    private static string ComputeCheckpointDigest(LifeModuleOriginDossierDraftCheckpoint checkpoint)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schema", checkpoint.Schema);
            writer.WriteString("ownerId", checkpoint.OwnerId);
            writer.WriteString("workspaceId", checkpoint.WorkspaceId);
            writer.WriteNumber("workspaceRevision", checkpoint.WorkspaceRevision);
            writer.WriteString("projectionSeedDigest", checkpoint.Projection.SeedDigest);
            WriteStringArray(writer, "timelineChapterDigests", checkpoint.TimelineChapterDigests);
            writer.WriteString("pendingPreviewDigest", checkpoint.PendingPreview?.PreviewDigest ?? string.Empty);
            writer.WriteString("provenanceDigest", checkpoint.LtdProvenance.ProvenanceDigest);
            writer.WriteString("boundSeedDigest", checkpoint.BoundSeedDigest);
            writer.WriteString("boundDecisionGraphDigest", checkpoint.BoundDecisionGraphDigest);
            writer.WriteString("boundContentDigest", checkpoint.BoundContentDigest);
            writer.WriteString("boundSourceDigest", checkpoint.BoundSourceDigest);
            writer.WriteString("boundRulesDigest", checkpoint.BoundRulesDigest);
            writer.WriteString("boundRuntimeDigest", checkpoint.BoundRuntimeDigest);
            writer.WriteString("boundMechanicsSnapshotDigest", checkpoint.BoundMechanicsSnapshotDigest);
            writer.WriteEndObject();
        });

    private static bool IsSuccess(string outcome)
        => string.Equals(outcome, LifeModuleOriginDossierOutcomes.Success, StringComparison.Ordinal);

    private static bool DigestsEqual(string? left, string? right)
        => IsCanonicalDigest(left)
           && IsCanonicalDigest(right)
           && CryptographicOperations.FixedTimeEquals(
               Encoding.ASCII.GetBytes(left!),
               Encoding.ASCII.GetBytes(right!));

    private static bool IsCanonicalDigest(string? digest)
        => digest is { Length: 64 }
           && digest.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string ComputeDigest(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
            writer.Flush();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<string> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (string value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static LifeModuleOriginDossierResult<TTarget> Map<TSource, TTarget>(
        LifeModuleOriginDossierResult<TSource> source)
        => new(source.Outcome, default, source.Blockers?.ToArray() ?? []);

    private static LifeModuleOriginDossierResult<T> Blocked<T>(
        string outcome,
        string blocker)
        => new(outcome, default, [blocker]);
}
