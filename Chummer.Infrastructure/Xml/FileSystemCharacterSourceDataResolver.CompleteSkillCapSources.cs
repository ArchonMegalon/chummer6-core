using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class RuleSourceAccounting
    {
        private bool captureCompleteSkillCaps;

        public override bool EnableCompleteSkillCapSourceExecution()
        {
            // Never relabel a trace which has already observed a profile file.
            // Each retained reconstruction gets its own default-false state.
            if (!CapturePurchases || files.Count != 0) return false;
            captureCompleteSkillCaps = true;
            return true;
        }

        public override IReadOnlyDictionary<string, string> ConsumeSkillLimits(
            IReadOnlyDictionary<string, int> completedLimits)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            using var scope = resolver.CreateOperationScope();
            var context = scope.TryCreateContext(inputs.CharacterXml);
            if (context is null || !context.TryResolveCreationSkillsAuthority(out var authority)
                || !authority.IsAuthoritative
                || !context.TryResolveCreationEffectivePolicy(out var policy)
                || policy is null || policy.PolicyDigest != inputs.PolicyDigest
                || !completedLimits.TryGetValue(CharacterCreationEffectivePolicyContext.ActiveLimit, out int active)
                || active != authority.MaxActiveSkillRatingCreate
                || !completedLimits.TryGetValue(CharacterCreationEffectivePolicyContext.GroupLimit, out int groups)
                || groups != authority.MaxSkillGroupRatingCreate) return result;

            int order = 0;
            foreach (var directory in inputs.SelectionInputs)
            {
                int sourceOrder = order++;
                var source = policy.Sources.Single(row =>
                    row.Kind == CharacterCreationPolicySourceKind.SelectedAmend && row.Order == sourceOrder);
                var inventory = inputs.ReadInventory(new(directory.Path, "*.xml", SearchOption.AllDirectories, false));
                // A supported leaf never hides an unknown sibling/file/operation.
                if (!inventory.Exists || inventory.Entries.Length == 0
                    || inventory.Entries.Any(path => !files.TryGetValue(path, out var file) || !file.Accounted))
                    continue;
                var observed = inventory.Entries.Select(path => files[path]).OrderBy(file => file.Order).ToArray();
                var operations = observed.Select(file => new { file.Order, file.Operations }).ToArray();
                object execution;
                if (observed.Any(file => file.RequiresKnowledgeScope))
                {
                    // This value comes from the completed canonical skill evaluator,
                    // including explicit empty-category scopes. Neither source
                    // projection nor a matching active/group cap proves knowledge use.
                    if (!captureCompleteSkillCaps
                        || !completedLimits.TryGetValue(CharacterCreationEffectivePolicyContext.KnowledgeLimit, out int knowledge)
                        || knowledge != authority.MaxKnowledgeSkillRatingCreate) continue;
                    execution = new { Source = source, Operations = operations, Active = active, Groups = groups, Knowledge = knowledge };
                }
                else
                {
                    // Preserve the exact historical V1 and non-Complete V2 digest shape.
                    execution = new { Source = source, Operations = operations, Active = active, Groups = groups };
                }
                result.Add("source:" + source.Kind + ":" + source.Order,
                    CharacterCreationFinalizationDigest.Compute(execution));
            }
            return result;
        }
    }

    private sealed partial class ProfileFileAccounting
    {
        // Set only by the owning retained resolution before the merger runs.
        // V1 and non-Complete V2 never recognize these additional leaves.
        internal bool IncludeAdditionalSkillCaps { get; set; }
        internal bool RequiresKnowledgeScope { get; private set; }

        // Called by the actual merger's post-operation observer, after its exact
        // scalar REPLACE shape checks. The canonical source parser still resolves
        // creation/career clamping, and CompleteSkillTrace executes the resulting
        // limit. This is a routing decision, not a new numeric/defaults evaluator.
        private bool ConsumeSkillCap(XName setting)
        {
            if (setting == "maxskillratingcreate") return true;
            if (!IncludeAdditionalSkillCaps) return false;
            if (setting == "maxskillrating") return true;
            if (setting != "maxknowledgeskillratingcreate" && setting != "maxknowledgeskillrating")
                return false;
            RequiresKnowledgeScope = true;
            return true;
        }
    }
}
