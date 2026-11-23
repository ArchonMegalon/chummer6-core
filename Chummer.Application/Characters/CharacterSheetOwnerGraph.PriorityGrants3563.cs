using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    private sealed record PrioritySourceObservation8110(
        CharacterCreationPriorityGrantAuthority3563 Proof, string RootDigest, string SourceDigest);
    private PrioritySourceObservation8110? prioritySourceObservation8110;
    private readonly int prioritySourceThread8565 = Environment.CurrentManagedThreadId;
    private PrioritySourceObservation8110? creationPriorityObservation8565;
    private bool creationPriorityInvalid8565;


    private readonly HashSet<XElement> priorityGrantRows3563 = [];
    private readonly HashSet<Guid> nativePriorityQualityOwners9569 = [];
    private readonly HashSet<string> prioritySkillIds3563 = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> prioritySkillBases3563 = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> priorityGroupBases3563 = new(StringComparer.Ordinal);

    internal bool IsPrioritySkillGrant3563(XElement effect) => priorityGrantRows3563.Contains(effect);
    internal int PrioritySkillBase3563(string name) => prioritySkillBases3563.GetValueOrDefault(name);
    internal int PriorityGroupBase3563(string name) => priorityGroupBases3563.GetValueOrDefault(name);
    internal IEnumerable<string> PrioritySkillIds3563 => prioritySkillIds3563;
    internal IEnumerable<string> PriorityGroupNames3563 => priorityGroupBases3563.Keys;
    internal bool HasPriorityGrants3563 => priorityGrantRows3563.Count != 0;

    private void CompilePrioritySkillGrants3563(CharacterCreationPriorityGrantAuthority3563? proof)
    {
        if (proof is null)
        {
            Need(!(Node(root, "improvements")?.Elements() ?? []).Any(e =>
                Text(e, "improvementttype", "") is "SkillBase" or "SkillGroupBase"),
                "priority-grant-authority-unavailable", "improvements");
            RequireNoUnprovenPriorityGrants3563();
            return;
        }
        // The compiler must execute the real grant projection once. Only its
        // exact native retained-source/whole-root observation can initialize the
        // later private revalidation guard; imported and public sources stay cold.
        var retainedPriority8566 = !proof.IsImported5693
            && Environment.CurrentManagedThreadId == prioritySourceThread8565
            ? context as IRetainedCurrentSourceSnapshot8110 : null;
        string? priorityRoot8566 = retainedPriority8566?.IsCurrentRetainedSourceSnapshot8110() == true
            ? CharacterCreationOwnedGearGraph1233.GraphDigest(root) : null;
        Need(proof.TryResolve(root, context, out var grants, out var qualities, out var digest),
            "priority-grant-source-or-selection-mismatch", "prioritytalent");
        // A disabled, substituted or extra talent owner cannot keep its base grants.
        var matched = new HashSet<Guid>();
        foreach (var quality in qualities)
        {
            var candidates = owners.Where(o => o.Kind == "Quality" && o.SourceId.ToString("D") == quality.SourceId
                && Text(o.Saved, "qualitysource", "") == "Heritage" && !matched.Contains(o.Id)).ToArray();
            Need(candidates.Length == 1, "priority-grant-talent-owner-missing-or-duplicate", "qualities");
            var owner = candidates[0];
            Need(owner.Enabled && !suppressedOwners.Contains(owner.Id)
                && XNode.DeepEquals(owner.Source, Parse(quality.CanonicalSourceXml))
                && (quality.ForcedSelection.Length == 0 || owner.Extra == quality.ForcedSelection),
                "priority-grant-talent-owner-mismatch", owner.Path);
            if (owner.Source.Element("bonus")?.Element("selectskillgroup") is not null)
                Need(grants.Groups.Values.Count(g => g.CanonicalName == owner.Extra) == 1,
                    "priority-grant-talent-group-mismatch", owner.Path);
            matched.Add(owner.Id);
        }
        Need(owners.Where(o => o.Kind == "Quality" && Text(o.Saved, "qualitysource", "") == "Heritage")
            .All(o => matched.Contains(o.Id)), "priority-grant-unbound-talent-owner", "qualities");
        foreach (var grant in grants.Skills.Values)
        {
            prioritySkillIds3563.Add(grant.SourceId);
            prioritySkillBases3563.Add(grant.CanonicalName, grant.BaseRating);
            Add("SkillBase", grant.CanonicalName, grant.BaseRating);
        }
        foreach (var grant in grants.Groups.Values)
        {
            priorityGroupBases3563.Add(grant.CanonicalName, grant.BaseRating);
            Add("SkillGroupBase", grant.CanonicalName, grant.BaseRating);
        }
        // Actual owners matched above to the source-replayed native selection.
        // A saved Heritage label alone never adds an owner to this set.
        if (!proof.IsImported5693) nativePriorityQualityOwners9569.UnionWith(matched);
        sourceEvidence.Add(digest);
        if (priorityRoot8566 is not null)
        {
            bool unchanged = retainedPriority8566!.IsCurrentRetainedSourceSnapshot8110()
                && CharacterCreationOwnedGearGraph1233.GraphDigest(root) == priorityRoot8566;
            if (!unchanged) creationPriorityInvalid8565 = true;
            Need(unchanged, "priority-grant-source-or-selection-mismatch", "prioritytalent");
            // All talent owners/grants above have been reconciled. This is the
            // actual compiler's completed evaluation, never a serialized claim.
            if (creationPriorityObservation8565 is null)
            {
                creationPriorityObservation8565 = new(proof, priorityRoot8566, digest);
                CharacterCreationCompletionCost3340.Log.Checkpoint(8566);
            }
        }
        checks.Add(() => RevalidatePrioritySource8110(proof, digest));

        void Add(string kind, string name, int rating)
        {
            // Use the actual writer's full row contract. The existing Key/equality
            // check still reconciles every saved row, including disabled/extra rows.
            var row = CharacterCreationAwakenedLegacyProjector.Improvement(kind, name, "", "Heritage", rating);
            expected.Add(row); priorityGrantRows3563.Add(row);
        }
    }

    // One private graph/context and one privately captured native grant proof.
    // Each hit still validates the concrete retained source plus the whole current
    // character, before and after. This retains only a previously proven source
    // digest; it never admits a character mutation, a foreign context or a saved
    // authority. Cold reads and changed roots still execute the original proof.
    private bool RevalidateCreationPriority8565(CharacterCreationPriorityGrantAuthority3563 proof,
        string expectedDigest, IRetainedCurrentSourceSnapshot8110 retained)
    {
        try
        {
            if (creationPriorityInvalid8565) return false;
            if (!retained.IsCurrentRetainedSourceSnapshot8110())
            { creationPriorityInvalid8565 = true; return false; }
            string observedRoot = CharacterCreationOwnedGearGraph1233.GraphDigest(root);
            bool hit = creationPriorityObservation8565 is { } observed
                && ReferenceEquals(observed.Proof, proof) && observed.SourceDigest == expectedDigest
                && observed.RootDigest == observedRoot;
            if (!hit && (!proof.TryResolve(root, context, out _, out _, out var actual) || actual != expectedDigest))
                return false;
            if (!retained.IsCurrentRetainedSourceSnapshot8110()
                || CharacterCreationOwnedGearGraph1233.GraphDigest(root) != observedRoot)
            { creationPriorityInvalid8565 = true; return false; }
            creationPriorityObservation8565 ??= new(proof, observedRoot, expectedDigest);
            if (hit) CharacterCreationCompletionCost3340.Log.Checkpoint(8565);
            return true;
        }
        catch
        {
            creationPriorityInvalid8565 = true;
            throw;
        }
    }

    // Retained sources own immutable bytes and membership. This witness keeps
    // fresh character, source-snapshot and complete-root observations while
    // avoiding repeated pure grant projection for that exact private lifetime.
    // No calculated grant, mutable DTO, public source or later evaluation is reused.
    private bool RevalidatePrioritySource8110(CharacterCreationPriorityGrantAuthority3563 proof,
        string expectedDigest)
    {
        try
        {
            if (creationPriorityInvalid8565) return false;
            if (stockEvaluation is not { } evaluation)
            {
                // Creation has no Career retained-evaluation scope. Its concrete
                // retained context still owns immutable source bytes/membership.
                // Keep imported grants and caller-supplied contexts on the existing
                // independent replay path; a public digest cannot opt into reuse.
                // Live source contexts also implement the snapshot interface, but
                // have no retained snapshot. Enter the fast path only after the
                // compiler actually captured one. A captured snapshot that later
                // becomes invalid still fails closed in the existing guard.
                if (!proof.IsImported5693 && Environment.CurrentManagedThreadId == prioritySourceThread8565
                    && context is IRetainedCurrentSourceSnapshot8110 retained
                    && creationPriorityObservation8565 is not null)
                    return RevalidateCreationPriority8565(proof, expectedDigest, retained);
                return proof.TryResolve(root, context, out _, out _, out var ordinary) && ordinary == expectedDigest;
            }
            if (stockGraphInvalid || stockCurrentObservation is not { } current
                || !ReferenceEquals(current.RetainedEvaluation2764, evaluation)
                || !evaluation.Allows(current, context)) return false;
            if (context is not IRetainedCurrentSourceSnapshot8110 source)
                return proof.TryResolve(root, context, out _, out _, out var unretained) && unretained == expectedDigest;
            if (!source.IsCurrentRetainedSourceSnapshot8110())
            {
                evaluation.Invalidate6371();
                return false;
            }
            string observedRoot = CharacterCreationOwnedGearGraph1233.GraphDigest(root);
            bool reused = prioritySourceObservation8110 is { } observed
                && ReferenceEquals(observed.Proof, proof) && observed.SourceDigest == expectedDigest
                && observed.RootDigest == observedRoot;
            if (!reused && (!proof.TryResolve(root, context, out _, out _, out var actual)
                || actual != expectedDigest))
            {
                evaluation.Invalidate6371();
                return false;
            }
            if (!ReferenceEquals(current.RetainedEvaluation2764, evaluation)
                || !evaluation.Allows(current, context) || !source.IsCurrentRetainedSourceSnapshot8110()
                || CharacterCreationOwnedGearGraph1233.GraphDigest(root) != observedRoot)
            {
                evaluation.Invalidate6371();
                return false;
            }
            // Retain one successful original observation, never refresh it on a
            // different root or transfer it to another proof/source/lifetime.
            prioritySourceObservation8110 ??= new(proof, observedRoot, expectedDigest);
            return true;
        }
        catch
        {
            stockEvaluation?.Invalidate6371();
            throw;
        }
    }

    private void RequireNoUnprovenPriorityGrants3563()
    {
        string talent = Text(root, "prioritytalent", "");
        bool heritage = owners.Any(o => o.Kind == "Quality" && Text(o.Saved, "qualitysource", "") == "Heritage");
        if (talent.Length == 0 && !heritage) return;
        // Removing both the history and every saved base row cannot prove that
        // the selected talent has no grants. This source read proves absence
        // only; it cannot choose a plan or authorize a saved improvement.
        Need(Text(root, "buildmethod", "") is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen,
            "priority-grant-authority-unavailable", "buildmethod");
        string rank = Text(root, "priorityspecial", "");
        Need(context.TryResolveCreationPrerequisiteAuthority(out var authority)
            && authority.IsAuthoritative && authority.Blockers.Count == 0,
            "priority-grant-authority-unavailable", "priorityspecial");
        var options = authority.Options.Where(o => o.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{o.Rank},{o.SumToTenValue}") == rank)
            .SelectMany(o => o.TalentOptions).Where(t => t.Value == talent).ToArray();
        Need(options.Length == 1 && options[0].IsEnabled && options[0].Blockers.Count == 0
            && options[0].ActiveSkillGrant is not { BaseRating: > 0 }
            && options[0].SkillGroupGrant is not { BaseRating: > 0 },
            "priority-grant-authority-unavailable", "prioritytalent");
        string digest = CharacterCreationFinalizationDigest.Compute(authority);
        sourceEvidence.Add(digest);
        checks.Add(() => context.TryResolveCreationPrerequisiteAuthority(out var current)
            && CharacterCreationFinalizationDigest.Compute(current) == digest);
    }
}
