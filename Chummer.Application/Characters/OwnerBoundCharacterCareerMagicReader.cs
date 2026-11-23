using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerMagicReader
{
    CharacterCareerMagicRead? Read(OwnerContextStamp owner, CharacterWorkspaceId workspace);
}

internal static class CharacterCareerMagicReadBinding
{
    internal static CharacterCareerMagicBinding Create(OwnerContextStamp owner, WorkspaceStoredDocument saved) =>
        new(owner.Owner.NormalizedValue, saved.Id.Value, owner.AuthorityInstanceId, owner.TransitionRevision,
            saved.ContentRevision, saved.SavedRevision, CharacterCareerPowerTransaction.Hash(saved.Document.Content),
            saved.Document.AuxiliaryStateDigest);
    internal static bool MatchesOwner(CharacterCareerMagicBinding binding, OwnerContextStamp owner) =>
        binding.OwnerId == owner.Owner.NormalizedValue && binding.OwnerAuthorityInstanceId == owner.AuthorityInstanceId
        && binding.OwnerTransitionRevision == owner.TransitionRevision;
    internal static bool MatchesSaved(CharacterCareerMagicBinding binding, OwnerScope owner, WorkspaceStoredDocument saved) =>
        binding.OwnerId == owner.NormalizedValue && binding.WorkspaceId == saved.Id.Value
        && binding.WorkspaceRevision == saved.ContentRevision && binding.SavedRevision == saved.SavedRevision
        && binding.WorkspaceRevision == binding.SavedRevision
        && binding.CharacterDigest == CharacterCareerPowerTransaction.Hash(saved.Document.Content)
        && binding.AuxiliaryDigest == saved.Document.AuxiliaryStateDigest;
}

/// <summary>A read-only, owner-admitted catalogue of actual evaluated Career plans.</summary>
public sealed class OwnerBoundCharacterCareerMagicReader(IWorkspaceStore store, IOwnerContextAccessor owners,
    ICharacterSourceDataResolver sources, OwnerBoundCharacterCareerPowerService powers) : ICharacterCareerMagicReader
{
    public CharacterCareerMagicRead? Read(OwnerContextStamp owner, CharacterWorkspaceId workspace)
    {
        if (!OwnerContextAdmission.TryAcquire(owners, owner, out var lease)) return null;
        using (lease)
        {
            try
            {
                if (owners is IOwnerContextLeaseAccessor leaseOwners
                    && store is ICharacterCareerInitiationCurrentEnvironmentCapability retainedEnvironment
                    && retainedEnvironment.TryReadCurrentCareerMagic5722(leaseOwners, lease, workspace, sources, out var currentRead))
                    return currentRead;
                var saved = Get(owner, workspace);
                if (saved is null || saved.ContentRevision <= 0 || saved.ContentRevision != saved.SavedRevision
                    || !CharacterCareerPowerTransaction.IsValidHistory(saved)) return null;
                var binding = CharacterCareerMagicReadBinding.Create(owner, saved);
                var root = CharacterCareerPowerGraph.Parse(saved.Document.Content);
                if (saved.Document.RulesetId == "sr5" && CharacterCareerPowerGraph.Bool(root, "created", false)
                    && CharacterCareerPowerGraph.Bool(root, "resenabled", false))
                {
                    var resonanceContext = sources.TryCreateContext(saved.Document.Content);
                    IReadOnlyList<string> resonanceBlockers = resonanceContext is null
                        ? new[] { "resonance_source_unavailable" }
                        : CharacterCareerResonanceEvaluator.Evaluate(root, resonanceContext).Blockers;
                    return StillSaved(owner, workspace, binding)
                        ? new(binding, true, CharacterCareerPowerGovernance.Allows(saved), false, null, [], [], resonanceBlockers)
                        { Family = CharacterCareerInitiationFamily.Resonance } : null;
                }
                bool applicable = saved.Document.RulesetId == "sr5" && root.Name == "character"
                    && CharacterCareerPowerGraph.Bool(root, "created", false)
                    && CharacterCareerPowerGraph.Bool(root, "magenabled", false);
                bool adept = CharacterCareerPowerGraph.Bool(root, "adept", false);
                bool allows = CharacterCareerPowerGovernance.Allows(saved);
                var blockers = new List<string>();
                if (!allows) blockers.Add("power_campaign_authority_unavailable");
                if (!applicable) blockers.Add("power_requires_career_magic");
                if (!adept) blockers.Add("power_requires_career_adept");
                bool atomicAvailable = store is ICharacterCareerPowerAtomicCommitCapability;
                if (!atomicAvailable) blockers.Add("power_atomic_commit_unavailable");
                var fallback = CharacterCareerPowerGraph.UnavailableOwnedRows(root, "power_evaluation_unavailable");
                if (!applicable || !adept)
                    return StillSaved(owner, workspace, binding)
                        ? new(binding, applicable, allows && applicable, adept, null, fallback, [], blockers.AsReadOnly()) : null;

                CharacterCareerPowerGraph.ReadGraph graph;
                ICharacterSourceDataContext? context = sources.TryCreateContext(saved.Document.Content);
                CharacterCareerPowerReadEvaluation? evaluation;
                try
                {
                    CharacterCareerPowerGraph.Need(context is not null, "power_source_context_unavailable");
                    evaluation = powers.OpenReadEvaluation(owners, lease, workspace, binding, sources, context!);
                    graph = CharacterCareerPowerGraph.EvaluateRead(saved, evaluation?.Context ?? context!);
                }
                catch (Exception e) when (CharacterCareerPowerGraph.ReadUnavailable(e))
                {
                    string blocker = CharacterCareerPowerGraph.ReadBlocker(e);
                    blockers.Add(blocker);
                    if (blocker == "power_campaign_authority_unavailable") allows = false;
                    return StillSaved(owner, workspace, binding) ? new(binding, applicable, allows, adept, null,
                        CharacterCareerPowerGraph.UnavailableOwnedRows(root, blocker), [], blockers.AsReadOnly()) : null;
                }
                blockers.AddRange(graph.Blockers);
                var choices = new List<CharacterCareerMagicPowerChoice>();
                if (allows && atomicAvailable && graph.RemainingPoints is not null)
                {
                    foreach (var option in graph.Options)
                    {
                        var targets = new List<CharacterCareerMagicPowerTarget>();
                        // The upper bound comes from State's resolved native source/MAG cap.
                        // The canonical Build rejects free+paid overflow, effects,
                        // prerequisites, instance limits, unaffordable targets and stale inputs.
                        for (int rating = option.CurrentPaidRating + 1; rating <= option.MaximumRating; rating++)
                        {
                            var identity = option.Existing ? option.Identity : option.Identity with { InternalId = Guid.NewGuid() };
                            var command = new CharacterCareerPowerCommand(Guid.NewGuid(), identity, rating, option.Discounted)
                                { ReadBinding = binding };
                            var review = evaluation?.Review(command)
                                ?? powers.ReviewAdmitted(owners, lease, workspace, command);
                            if (review.Plan is not { } plan) continue;
                            if (plan.Command.ReadBinding != binding) return null;
                            var effects = CharacterCareerPowerGraph.ReadEffects(CharacterCareerPowerGraph.Parse(option.Source.RawSourceXml),
                                identity, plan.Quote.Proposed.TotalRating);
                            targets.Add(new(plan, effects));
                        }
                        if (targets.Count > 0)
                            choices.Add(new(option.Source.Name,
                                option.Identity.Extra + (option.Discounted ? " (Way discount)" : ""),
                                Array.AsReadOnly(targets.ToArray())));
                    }
                }
                // Fence both source scopes and every admitted target. In particular a plan
                // obtained while a source temporarily differed cannot survive source A-B-A.
                var retained = CharacterCareerPowerGraph.EvaluateRead(saved, context!);
                var freshContext = sources.TryCreateContext(saved.Document.Content);
                if (freshContext is null) return null;
                var fresh = CharacterCareerPowerGraph.EvaluateRead(saved, freshContext);
                if (graph.SourceDigest != retained.SourceDigest || graph.SourceDigest != fresh.SourceDigest
                    || !CharacterCareerPowerTransaction.Same(graph.Owned, retained.Owned)
                    || !CharacterCareerPowerTransaction.Same(graph.Owned, fresh.Owned)
                    || graph.RemainingPoints != retained.RemainingPoints || graph.RemainingPoints != fresh.RemainingPoints
                    || !CharacterCareerPowerTransaction.Same(graph.Blockers, fresh.Blockers)
                    || !CharacterCareerPowerTransaction.Same(graph.Blockers, retained.Blockers)) return null;
                // Every candidate in the built-in path used the same captured source
                // answers. Fence all those answers, including rejected candidates,
                // instead of rebuilding every accepted plan against identical inputs.
                if (evaluation is not null)
                {
                    if (!powers.CompleteReadEvaluation(owners, lease, workspace, evaluation, freshContext)) return null;
                }
                else
                    foreach (var target in choices.SelectMany(c => c.Targets))
                    {
                        var again = powers.ReviewAdmitted(owners, lease, workspace, target.Plan.Command);
                        if (!CharacterCareerPowerTransaction.Same(again.Plan, target.Plan)) return null;
                    }
                if (!StillSaved(owner, workspace, binding)) return null;
                return new(binding, applicable, allows, adept, graph.RemainingPoints, graph.Owned,
                    Array.AsReadOnly(choices.ToArray()), Array.AsReadOnly(blockers.Distinct().ToArray()));
            }
            catch (Exception e) when (CharacterCareerPowerGraph.ReadUnavailable(e)) { return null; }
        }
    }

    internal static CharacterCareerMagicRead? ReadCurrent5722(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        ICharacterSourceDataContext context, CharacterCurrentRuleEnvironmentContext? current)
    {
        if (saved.ContentRevision <= 0 || saved.SavedRevision != saved.ContentRevision
            || !CharacterCareerPowerTransaction.IsValidHistory(saved)
            || !CharacterCareerInitiationTransaction.ObserveHistory(saved)) return null;
        var root = CharacterCareerPowerGraph.Parse(saved.Document.Content);
        bool resonance = CharacterCareerPowerGraph.Bool(root, "resenabled", false);
        var proof = CharacterCurrentRuleEnvironmentConsumers.CreateLearningAuthority5703(saved, current, context, resonance);
        if (proof is not null && !proof.Matches(saved, context)) return null;
        bool applicable = saved.Document.RulesetId == "sr5" && CharacterCareerPowerGraph.Bool(root, "created", false)
            && (resonance || CharacterCareerPowerGraph.Bool(root, "magenabled", false));
        bool adept = !resonance && CharacterCareerPowerGraph.Bool(root, "adept", false);
        var blockers = new List<string>();
        if (resonance) blockers.AddRange((proof?.Resonance
            ?? CharacterCareerResonanceEvaluator.Evaluate(root, context)).Blockers);
        else
        {
            if (!context.TryResolveCareerSettingsProfile(out var settings, out var state) || string.IsNullOrWhiteSpace(state)) return null;
            blockers.AddRange((proof?.Magic ?? CharacterCareerMagicEvaluator.Evaluate(root,
                CharacterCareerInitiationProjection.Parse(settings, "settings"), sourceContext: context)).Blockers);
        }
        bool allows = CharacterCareerPowerGovernance.Allows(saved);
        if (!allows) blockers.Add("power_campaign_authority_unavailable");
        // Power purchases still require their own retained transaction admission. This
        // read admits the grade wizard, without manufacturing spendable power plans.
        if (adept) blockers.Add("power_retained_transaction_unavailable");
        return new(CharacterCareerMagicReadBinding.Create(owner, saved), applicable, allows && applicable,
            adept, null, adept ? CharacterCareerPowerGraph.UnavailableOwnedRows(root,
                "power_retained_transaction_unavailable") : [], [], blockers.AsReadOnly())
        { Family = resonance ? CharacterCareerInitiationFamily.Resonance : CharacterCareerInitiationFamily.Magic };
    }

    private WorkspaceStoredDocument? Get(OwnerContextStamp owner, CharacterWorkspaceId workspace)
    {
        var read = owner.Owner.IsLocalSingleUser ? store.Get(workspace) : store.Get(owner.Owner, workspace);
        return read.Success ? read.Value : null;
    }
    private bool StillSaved(OwnerContextStamp owner, CharacterWorkspaceId workspace, CharacterCareerMagicBinding binding) =>
        Get(owner, workspace) is { } current && CharacterCareerMagicReadBinding.MatchesSaved(binding, owner.Owner, current);
}
