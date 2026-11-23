using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileKarma1208(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (profile.Descendants().Where(node => node.Name.LocalName == "karmacost").ToArray() is not [var costs]
                || costs.Parent != profile || costs.Name != "karmacost" || costs.HasAttributes) return;
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            foreach (var leaf in costs.Elements())
            {
                if (leaf.Name.NamespaceName.Length != 0 || leaf.HasAttributes
                    || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                    || !invocation.TryGetExecutedProfileKarma1208(_settingsProfileId, authority.RawProfileInputsDigest,
                        profileDigest, leaf.Name.LocalName,
                        CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                        out var result, out var execution) || _sourceInputs.HasSourceDrift) continue;
                ledger.Tree(leaf, D.Compiled, new
                {
                    Operation = "selected-profile-creation-karma-branch", Field = leaf.Name.LocalName,
                    Result = result, Execution = execution
                });
            }
        }
    }
}
