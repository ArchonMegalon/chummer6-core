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
                var saved = Get(owner, workspace);
                if (saved is null || saved.ContentRevision <= 0 || saved.ContentRevision != saved.SavedRevision
                    || !CharacterCareerPowerTransaction.IsValidHistory(saved)) return null;
                var binding = CharacterCareerMagicReadBinding.Create(owner, saved);
                var root = CharacterCareerPowerGraph.Parse(saved.Document.Content);
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
                try
                {
                    CharacterCareerPowerGraph.Need(context is not null, "power_source_context_unavailable");
                    graph = CharacterCareerPowerGraph.EvaluateRead(saved.Document.Content, context!);
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
                        // ReviewAdmitted independently rejects free+paid overflow, effects,
                        // prerequisites, instance limits, unaffordable targets and stale inputs.
                        for (int rating = option.CurrentPaidRating + 1; rating <= option.MaximumRating; rating++)
                        {
                            var identity = option.Existing ? option.Identity : option.Identity with { InternalId = Guid.NewGuid() };
                            var command = new CharacterCareerPowerCommand(Guid.NewGuid(), identity, rating, option.Discounted)
                                { ReadBinding = binding };
                            var review = powers.ReviewAdmitted(owners, lease, workspace, command);
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
                var retained = CharacterCareerPowerGraph.EvaluateRead(saved.Document.Content, context!);
                var freshContext = sources.TryCreateContext(saved.Document.Content);
                if (freshContext is null) return null;
                var fresh = CharacterCareerPowerGraph.EvaluateRead(saved.Document.Content, freshContext);
                if (graph.SourceDigest != retained.SourceDigest || graph.SourceDigest != fresh.SourceDigest
                    || !CharacterCareerPowerTransaction.Same(graph.Owned, retained.Owned)
                    || !CharacterCareerPowerTransaction.Same(graph.Owned, fresh.Owned)
                    || graph.RemainingPoints != retained.RemainingPoints || graph.RemainingPoints != fresh.RemainingPoints
                    || !CharacterCareerPowerTransaction.Same(graph.Blockers, fresh.Blockers)
                    || !CharacterCareerPowerTransaction.Same(graph.Blockers, retained.Blockers)) return null;
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

    private WorkspaceStoredDocument? Get(OwnerContextStamp owner, CharacterWorkspaceId workspace)
    {
        var read = owner.Owner.IsLocalSingleUser ? store.Get(workspace) : store.Get(owner.Owner, workspace);
        return read.Success ? read.Value : null;
    }
    private bool StillSaved(OwnerContextStamp owner, CharacterWorkspaceId workspace, CharacterCareerMagicBinding binding) =>
        Get(owner, workspace) is { } current && CharacterCareerMagicReadBinding.MatchesSaved(binding, owner.Owner, current);
}
