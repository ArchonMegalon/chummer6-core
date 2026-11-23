using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteRetainedMagic1221(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (profile.Elements("karmacost").ToArray() is not [var costs]) return;
            string digest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            foreach (var leaf in costs.Elements())
            {
                if (_sourceInputs.HasSourceDrift || !invocation.TryGetExecutedRetainedMagicKarma1221(_settingsProfileId,
                    authority.RawProfileInputsDigest, digest, leaf.Name.LocalName,
                    CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                    out var result, out var execution)) continue;
                ledger.Tree(leaf, D.Compiled, new
                    { Operation = "creation-retained-magic-karma1221", Field = leaf.Name.LocalName, Result = result, Execution = execution });
            }
        }
    }
}
