using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileCarryover995(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            var containers = profile.Elements().Where(node => node.Name.LocalName == "karmacost").ToArray();
            if (containers is not [var costs] || costs.Name != "karmacost" || costs.HasAttributes
                || costs.Elements().Where(node => node.Name.LocalName == "karmacarryover").ToArray() is not [var leaf]
                || leaf.Name != "karmacarryover" || leaf.HasAttributes
                || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                || !TryResolveCreationCarryoverPolicy(out var policy) || policy is null
                || policy.SettingsProfileId != authority.SettingsProfileId
                || policy.RawProfileInputsDigest != authority.RawProfileInputsDigest
                || !invocation.TryGetExecutedProfileCarryover995(policy,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                    CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                    out int remaining, out int carried, out var execution) || _sourceInputs.HasSourceDrift) return;
            ledger.Tree(leaf, D.Compiled, new
            {
                Operation = "canonical-creation-karma-carryover", Policy = policy,
                Remaining = remaining, Carried = carried, Execution = execution
            });
        }
    }
}
