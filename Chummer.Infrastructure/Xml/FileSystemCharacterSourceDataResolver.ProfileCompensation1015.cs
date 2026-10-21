using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileCompensation1015(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            const string field = "compensateskillgroupkarmadifference";
            if (profile.Elements().Where(node => node.Name.LocalName == field).ToArray() is not [var leaf]
                || leaf.Name != field || leaf.HasAttributes
                || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                || !TryResolveCreationPrioritySkillKarmaPolicy(out var policy) || policy is null
                || policy.SettingsProfileId != authority.SettingsProfileId
                || policy.RawProfileInputsDigest != authority.RawProfileInputsDigest
                || !invocation.TryGetExecutedProfileCompensation1015(policy.SettingsProfileId, policy.RawProfileInputsDigest,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                    CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)), policy.AuthorityDigest,
                    out bool enabled, out bool projectGroups, out var execution)
                || enabled != policy.CompensateSkillGroupKarmaDifference || _sourceInputs.HasSourceDrift) return;
            // The completed consumer chose its projection branch and evaluated the
            // selected prices. Account only this flag, never adjacent group settings.
            ledger.Tree(leaf, D.Compiled, new
            {
                Operation = "canonical-skill-group-compensation-policy", Input = enabled,
                ProjectGroups = projectGroups, Policy = policy.AuthorityDigest, Execution = execution
            });
        }
    }
}
