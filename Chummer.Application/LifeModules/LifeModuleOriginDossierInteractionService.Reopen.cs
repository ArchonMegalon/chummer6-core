using System.Text.Json;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

public sealed partial class LifeModuleOriginDossierInteractionService
{
    /// <summary>
    /// Opens a read-only choice list. A pristine opening may acquire fresh
    /// admission digests after saving, but only if every displayed choice,
    /// effect, source, owner and content binding is unchanged. Strict Restore,
    /// Prepare and Confirm retain their existing replay/admission contracts.
    /// </summary>
    public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Reopen(
        LifeModuleOriginDossierDraftCheckpoint checkpoint)
    {
        var restored = Restore(checkpoint);
        if (restored.Outcome != LifeModuleOriginDossierOutcomes.Conflict
            || restored.Blockers.Count != 1
            || restored.Blockers[0] != LifeModuleOriginDossierBlockers.DecisionStale
            || !IsUnselectedOpening(checkpoint))
            return restored;

        // Start reads current Core authority; it never creates a runner,
        // accepts a decision, or dispatches a provider request. Return that
        // freshly sealed view, not a rewritten historical checkpoint.
        var fresh = Start(checkpoint.WorkspaceId);
        if (fresh.Outcome != LifeModuleOriginDossierOutcomes.Success
            || fresh.Value is not { } current
            || current.OwnerId != checkpoint.OwnerId
            || !IsUnselectedOpening(current)
            || OpeningShape(checkpoint.Projection) != OpeningShape(current.Projection))
            return restored;
        return fresh;
    }

    private static bool IsUnselectedOpening(LifeModuleOriginDossierDraftCheckpoint checkpoint)
        => checkpoint.PendingPreview is null
           && checkpoint.TimelineChapterDigests.Count == 0
           && checkpoint.LtdProvenance.State == OriginLtdProvenanceStates.NotRequested
           && checkpoint.Projection.VisibleChapters.Count == 0
           && checkpoint.Projection.CurrentTurn is
           {
               TurnSequence: 1,
               IsTerminal: false,
               AcceptedDecisionIds.Count: 0,
               CanonicalFacts.Count: 0
           };

    // Comparison only, never persisted or admitted as a projection. Foundation
    // command digests contain SavedRevision even when rules/content are equal.
    // Keep every other field (including effect digests, labels, follow-ups,
    // locale and previous-turn identity) in the comparison. Unknown future
    // serialized fields are compared too rather than silently discarded.
    private static string OpeningShape(OriginStoryArcSeed seed)
        => JsonSerializer.Serialize(seed with
        {
            SeedDigest = string.Empty,
            CurrentTurn = seed.CurrentTurn with
            {
                DecisionGraphDigest = string.Empty,
                DecisionDigest = string.Empty,
                SeedDigest = string.Empty,
                LegalChoices = seed.CurrentTurn.LegalChoices.Select(choice => choice with
                {
                    DecisionCommandDigest = string.Empty,
                    ChoiceDigest = string.Empty
                }).ToArray()
            },
            CanonicalLayer = seed.CanonicalLayer with
            {
                DecisionGraphDigest = string.Empty,
                MechanicsSnapshotDigest = string.Empty,
                LayerDigest = string.Empty
            }
        });
}
