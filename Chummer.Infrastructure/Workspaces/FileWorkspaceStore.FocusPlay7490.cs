using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Session;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerFocusPlayCommitCapability7490
{
    public CharacterCareerFocusBondingResult CommitFocusWithPlay7490(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFocusBondingCommand command, ICharacterSourceDataResolver sources,
        bool retire, SessionFocusCanonicalAdmission7490? admission, CancellationToken token)
        => WriteFocusBonding(owners, lease, command, sources, retire, token, admission);

    private void PublishFocusContinuation7490(SessionFocusCanonicalAdmission7490 admission, OwnerContextStamp stamp,
        WorkspaceStoredDocument before, WorkspaceStoredDocument after, string? slot,
        CharacterCareerFocusBondingReceipt receipt, ICharacterSourceDataResolver sources,
        GearCurrentEnvironment? current, CharacterCurrentRuleEnvironmentReadmissionResult? readmission)
    {
        Conflict(!string.IsNullOrWhiteSpace(slot), "focus-continuation-slot-missing");
        var beforeSources = sources; var afterSources = sources;
        string? beforeEnvironment = null, afterEnvironment = null;
        CharacterCurrentRuleEnvironmentContext? beforeContext = null, afterContext = null;
        if (current is not null)
        {
            Conflict(readmission?.AfterCapture is not null && readmission.AfterExecution is not null
                && readmission.Blockers.Count == 0, "focus-continuation-after-admission-required");
            var capture = readmission!.AfterCapture!; var execution = readmission.AfterExecution!;
            beforeEnvironment = ConditionEnvironmentDigest(current);
            afterEnvironment = Digest(new { capture.IntegrityDigest, capture.EffectivePolicyDigest,
                execution.ExecutionProfile, execution.ExecutionProfileFingerprint });
            beforeContext = CharacterCurrentRuleEnvironmentAdmission.CreateCurrentEnvironmentContext(before,
                current.Capture, _retainedCharacterQueries!);
            afterContext = CharacterCurrentRuleEnvironmentAdmission.CreateCurrentEnvironmentContext(after,
                capture, _retainedCharacterQueries!);
            beforeSources = current.Capture.CreateCurrentEnvironmentResolution(current.Execution.ExecutionProfile,
                current.Execution.ExecutionProfileFingerprint)!.Resolver;
            afterSources = capture.CreateCurrentEnvironmentResolution(execution.ExecutionProfile,
                execution.ExecutionProfileFingerprint)!.Resolver;
            Conflict(beforeContext is not null && afterContext is not null, "focus-continuation-context-unavailable");
        }
        // The production resolver refuses absent retained context. Synthetic source admission belongs
        // only to an explicitly supplied test resolver; the journal/canonical boundary stays production.
        admission.Publish(stamp, before, after, slot!, receipt, beforeSources, beforeEnvironment, beforeContext,
            afterSources, afterEnvironment, afterContext);
        if (current is not null)
        {
            Conflict(beforeContext!.VerifyRetainedArchive() && afterContext!.VerifyRetainedArchive(),
                "focus-continuation-source-changed");
            current.ValidateReadObservation();
        }
    }
}
