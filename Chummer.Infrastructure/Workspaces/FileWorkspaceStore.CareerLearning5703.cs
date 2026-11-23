using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    private static bool LearningOwnerCurrent5703(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease? lease, OwnerContextStamp stamp)
        => stamp.IsValid && lease is not null && lease.Stamp == stamp && owners.Capture() == stamp;

    private static ICharacterSourceDataResolver LearningSources5703(GearCurrentEnvironment? current,
        ICharacterSourceDataResolver fallback)
    {
        if (current is null) return fallback;
        current.ValidateReadObservation();
        if (!current.Capture.MatchesOriginalCharacter(current.Stored.Document.Content))
            throw new IOException("learning_current_capture_mismatch");
        return current.Capture.CreateCurrentEnvironmentResolution(current.Execution.ExecutionProfile,
            current.Execution.ExecutionProfileFingerprint)?.Resolver
            ?? throw new IOException("learning_current_resolution_unavailable");
    }

    private CharacterCurrentRuleEnvironmentContext? LearningContext5703(GearCurrentEnvironment? current,
        WorkspaceStoredDocument saved)
    {
        if (current is null || !CurrentCharacterRuleSourceSemantics.IsImportedPriorityCareerProfile5667(
                current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint)) return null;
        var context = CharacterCurrentRuleEnvironmentAdmission.CreateCurrentEnvironmentContext(saved,
            current.Capture, _retainedCharacterQueries!);
        if (context is null || context.SemanticsProfile != current.Execution.ExecutionProfile
            || context.SemanticsFingerprint != current.Execution.ExecutionProfileFingerprint
            || !context.VerifyRetainedArchive()) throw new IOException("learning_imported_context_unavailable");
        return context;
    }

    private static string LearningRuntime5703(string runtime, GearCurrentEnvironment? current,
        string? slot, OwnerContextStamp stamp)
        => current is null ? runtime : CharacterCreationFinalizationDigest.Compute(new
        {
            Runtime = runtime, Slot = slot, current.Stored.LocalHistory!.IncarnationId,
            current.Capture.IntegrityDigest, current.Execution.ExecutionProfile,
            current.Execution.ExecutionProfileFingerprint, stamp.AuthorityInstanceId, stamp.TransitionRevision
        })[7..]; // Typed learning bindings use the existing unprefixed SHA-256 format.
}
