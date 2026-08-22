using Chummer.Application.BuildGhost;
using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.BuildGhost.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BuildGhostWizardConversationValidatorTests
{
    private const string SourceDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [TestMethod]
    public void Grounded_follow_up_with_preview_only_suggestion_is_accepted()
    {
        BuildGhostWizardTurnRequest request = CreateRequest();
        BuildGhostWizardTurnResponse response = Sign(new BuildGhostWizardTurnResponse(
            Schema: BuildGhostWizardContractVersions.TurnResponseV1,
            RequestId: request.RequestId,
            ThreadId: request.ThreadId,
            PacketDigest: request.Context.PacketDigest,
            Locale: request.Locale,
            Text: "Body can be raised once with the remaining attribute point.",
            Status: BuildGhostConversationStatuses.Ready,
            ReferencedFactIds: ["fact:attribute-points"],
            ReferencedOptionIds: ["attribute:BOD:+1"],
            ReferencedSourceAnchorIds: ["source:attributes"],
            Suggestions:
            [
                new BuildGhostWizardSuggestion(
                    ActionId: "wizard-choice:attribute:BOD:+1",
                    ActionType: BuildGhostActionTypes.PreviewWizardChoice,
                    Label: "Preview Body +1",
                    PreviewOnly: true,
                    RequiresExplicitReview: true,
                    ExpectedWorkspaceRevision: request.WorkspaceRevision,
                    ExpectedWizardSnapshotDigest: request.Context.Wizard.SnapshotDigest,
                    ExpectedPacketDigest: request.Context.PacketDigest,
                    Consequences:
                    [
                        new CharacterCreationChoiceConsequence(
                            "attribute:BOD", "attribute", "BOD", "2", "3", ["source:attributes"])
                    ],
                    Costs: [new CharacterCreationChoiceCost(CharacterCreationBudgetIds.NormalAttributes, 1m, "points")],
                    SourceAnchorIds: ["source:attributes"])
            ],
            ResponseDigest: string.Empty));

        BuildGhostWizardTurnValidationResult result =
            BuildGhostWizardConversationValidator.ValidateResponse(request, response);

        Assert.IsTrue(result.Accepted, string.Join(", ", result.RejectionReasons));
        Assert.HasCount(1, result.SafeSuggestions);
    }

    [TestMethod]
    public void Unknown_references_fail_closed_to_local_text_without_suggestions()
    {
        BuildGhostWizardTurnRequest request = CreateRequest();
        BuildGhostWizardTurnResponse response = Sign(new BuildGhostWizardTurnResponse(
            Schema: BuildGhostWizardContractVersions.TurnResponseV1,
            RequestId: request.RequestId,
            ThreadId: request.ThreadId,
            PacketDigest: request.Context.PacketDigest,
            Locale: request.Locale,
            Text: "Invented answer",
            Status: BuildGhostConversationStatuses.Ready,
            ReferencedFactIds: ["fact:invented"],
            ReferencedOptionIds: ["option:invented"],
            ReferencedSourceAnchorIds: ["source:invented"],
            Suggestions: [],
            ResponseDigest: string.Empty));

        BuildGhostWizardTurnValidationResult result =
            BuildGhostWizardConversationValidator.ValidateResponse(request, response);

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(request.Context.DeterministicFallbackText, result.SafeText);
        Assert.IsEmpty(result.SafeSuggestions);
        CollectionAssert.Contains(result.RejectionReasons.ToArray(), "unknown-fact:fact:invented");
        CollectionAssert.Contains(result.RejectionReasons.ToArray(), "unknown-option:option:invented");
        CollectionAssert.Contains(result.RejectionReasons.ToArray(), "unknown-source-anchor:source:invented");
    }

    [TestMethod]
    public void Stale_suggestion_binding_is_rejected_before_any_mutation()
    {
        BuildGhostWizardTurnRequest request = CreateRequest();
        BuildGhostWizardTurnResponse response = Sign(new BuildGhostWizardTurnResponse(
            Schema: BuildGhostWizardContractVersions.TurnResponseV1,
            RequestId: request.RequestId,
            ThreadId: request.ThreadId,
            PacketDigest: request.Context.PacketDigest,
            Locale: request.Locale,
            Text: "Preview available.",
            Status: BuildGhostConversationStatuses.Ready,
            ReferencedFactIds: ["fact:attribute-points"],
            ReferencedOptionIds: ["attribute:BOD:+1"],
            ReferencedSourceAnchorIds: ["source:attributes"],
            Suggestions:
            [
                new BuildGhostWizardSuggestion(
                    ActionId: "wizard-choice:attribute:BOD:+1",
                    ActionType: BuildGhostActionTypes.PreviewWizardChoice,
                    Label: "Stale preview",
                    PreviewOnly: true,
                    RequiresExplicitReview: true,
                    ExpectedWorkspaceRevision: request.WorkspaceRevision + 1,
                    ExpectedWizardSnapshotDigest: request.Context.Wizard.SnapshotDigest,
                    ExpectedPacketDigest: request.Context.PacketDigest,
                    Consequences: [],
                    Costs: [],
                    SourceAnchorIds: ["source:attributes"])
            ],
            ResponseDigest: string.Empty));

        BuildGhostWizardTurnValidationResult result =
            BuildGhostWizardConversationValidator.ValidateResponse(request, response);

        Assert.IsFalse(result.Accepted);
        Assert.IsEmpty(result.SafeSuggestions);
        CollectionAssert.Contains(
            result.RejectionReasons.ToArray(),
            "suggestion-binding-mismatch:wizard-choice:attribute:BOD:+1");
    }

    [TestMethod]
    public void Context_digest_or_revision_drift_is_rejected()
    {
        BuildGhostWizardTurnRequest request = CreateRequest();
        BuildGhostWizardContextPacket changed = request.Context with
        {
            WorkspaceRevision = request.Context.WorkspaceRevision + 1
        };

        IReadOnlyList<string> reasons = BuildGhostWizardConversationValidator.ValidateContext(changed);

        CollectionAssert.Contains(reasons.ToArray(), "wizard-revision-mismatch");
        CollectionAssert.Contains(reasons.ToArray(), "wizard-context-packet-digest-mismatch");
    }

    private static BuildGhostWizardTurnRequest CreateRequest()
    {
        CharacterCreationWizardSnapshot wizard = Sign(new CharacterCreationWizardSnapshot(
            Schema: CharacterCreationWizardSchemas.SnapshotV1,
            WorkspaceId: "workspace-1",
            WorkspaceRevision: 7,
            ContentDigest: "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            SourceDigest: SourceDigest,
            RulesetId: "sr5",
            RuntimeFingerprint: "runtime:sr5:test",
            BuildMethod: CharacterCreationBuildMethods.Priority,
            CharacterCreated: false,
            ActiveStepId: CharacterCreationWizardStepIds.Attributes,
            Steps: [],
            Budgets:
            [
                new CharacterCreationBudgetState(
                    CharacterCreationBudgetIds.NormalAttributes,
                    "Attribute points",
                    24m,
                    23m,
                    1m,
                    true,
                    [],
                    "points")
            ],
            LegalOptionsByStep: new Dictionary<string, IReadOnlyList<CharacterCreationLegalOption>>(StringComparer.Ordinal)
            {
                [CharacterCreationWizardStepIds.Attributes] =
                [
                    new CharacterCreationLegalOption(
                        "attribute:BOD:+1",
                        "Body +1",
                        true,
                        null,
                        new Dictionary<string, string>(),
                        [new CharacterCreationChoiceCost(CharacterCreationBudgetIds.NormalAttributes, 1m, "points")],
                        [new CharacterCreationChoiceConsequence("attribute:BOD", "attribute", "BOD", "2", "3", ["source:attributes"])],
                        ["source:attributes"])
                ]
            },
            CompletionBlockers: ["skills-incomplete"],
            Warnings: [],
            CanFinalize: false,
            SnapshotDigest: string.Empty));
        BuildGhostAllowedAction action = new(
            "wizard-choice:attribute:BOD:+1",
            BuildGhostActionTypes.PreviewWizardChoice,
            null,
            true,
            wizard.WorkspaceRevision,
            wizard.SourceDigest);
        BuildGhostWizardContextPacket context = Sign(new BuildGhostWizardContextPacket(
            Schema: BuildGhostWizardContractVersions.ContextV1,
            OwnerId: "owner-1",
            ThreadId: "thread-1",
            WorkspaceId: wizard.WorkspaceId,
            WorkspaceRevision: wizard.WorkspaceRevision,
            ActiveStepId: wizard.ActiveStepId,
            Locale: "de-DE",
            Wizard: wizard,
            SourceAnchors:
            [
                new BuildGhostSourceAnchor(
                    "source:attributes", "sr5", "core", 66,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    ["remaining=24-23"])
            ],
            Facts:
            [
                new BuildGhostFact(
                    "fact:attribute-points", "budget", "Remaining attribute points", "1", 1m,
                    ["source:attributes"])
            ],
            AllowedQuestionScopes: ["current-step", "rules", "alternatives"],
            AllowedSuggestedActions: [action],
            DeterministicFallbackText: "Rook nutzt die belegte lokale Regelhilfe.",
            InputDigest: "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            PacketDigest: string.Empty));
        return Sign(new BuildGhostWizardTurnRequest(
            Schema: BuildGhostWizardContractVersions.TurnRequestV1,
            RequestId: "request-1",
            ThreadId: context.ThreadId,
            OwnerId: context.OwnerId,
            WorkspaceId: context.WorkspaceId,
            WorkspaceRevision: context.WorkspaceRevision,
            ActiveStepId: context.ActiveStepId,
            Locale: context.Locale,
            UserText: "Kann ich Konstitution noch erhöhen?",
            Context: context,
            RequestDigest: string.Empty));
    }

    private static CharacterCreationWizardSnapshot Sign(CharacterCreationWizardSnapshot snapshot)
        => snapshot with { SnapshotDigest = BuildGhostCanonicalDigest.Compute(snapshot with { SnapshotDigest = string.Empty }) };

    private static BuildGhostWizardContextPacket Sign(BuildGhostWizardContextPacket context)
        => context with { PacketDigest = BuildGhostCanonicalDigest.Compute(context with { PacketDigest = string.Empty }) };

    private static BuildGhostWizardTurnRequest Sign(BuildGhostWizardTurnRequest request)
        => request with { RequestDigest = BuildGhostCanonicalDigest.Compute(request with { RequestDigest = string.Empty }) };

    private static BuildGhostWizardTurnResponse Sign(BuildGhostWizardTurnResponse response)
        => response with { ResponseDigest = BuildGhostCanonicalDigest.Compute(response with { ResponseDigest = string.Empty }) };
}

