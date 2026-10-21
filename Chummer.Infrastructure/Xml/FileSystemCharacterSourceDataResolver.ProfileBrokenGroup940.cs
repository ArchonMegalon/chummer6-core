using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileBrokenGroup940(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (profile.Elements("usepointsonbrokengroups").ToArray() is not [var leaf]
                || leaf.HasAttributes || leaf.HasElements || leaf.Nodes().Any(node => node.NodeType != System.Xml.XmlNodeType.Text)
                || !CharacterCreationSkillGroupPolicyAuthority.TryResolve(profile, out bool broken, out _, out _)
                || !invocation.TryGetExecutedProfileBrokenGroup940(_settingsProfileId, authority.RawProfileInputsDigest,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                    CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                    out bool executed, out bool noTarget, out var checks, out var executionDigest)
                || broken != executed || _sourceInputs.HasSourceDrift) return;
            // Tree is safe only for this validated plain scalar: its own element
            // and text occurrences. No sibling or selected-profile subtree is covered.
            ledger.Tree(leaf, D.Compiled, new
            {
                Operation = "canonical-broken-group-policy", Input = leaf.Value,
                Result = executed, NoTarget = noTarget,
                Checks = checks.Select(check => new { check.TargetId, check.GroupRating, check.PointSpecialization }).ToArray(),
                CanonicalExecution = executionDigest
            });
        }
    }
}
