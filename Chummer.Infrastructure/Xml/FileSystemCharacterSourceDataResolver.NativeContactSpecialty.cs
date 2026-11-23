using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteNativeContactSpecialty(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (_sourceInputs.HasSourceDrift || !invocation.TryGetExecutedNativeContactSpecialty(authority.SettingsProfileId,
                authority.RawProfileInputsDigest, CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)), out var native)) return;
            foreach (var definition in CharacterCreationNativeContactSpecialtyDefinitions.All)
                if (native!.Policy.Value(definition.Element) is bool value
                    && Boolean1217(profile, definition.Element, out bool current) && current == value)
                    ledger.Tree(PlainLeaf1217(profile, definition.Element)!, CharacterCreationSourceOperationDisposition.Compiled, native);
        }
    }
}
