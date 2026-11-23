using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteNativeMagic1430(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (!invocation.TryGetExecutedNativeMagic1430(authority.SettingsProfileId, authority.RawProfileInputsDigest,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)), out var result)) return;
            foreach (var definition in CharacterCreationNativeMagicDefinitions1430.All)
            {
                if (result!.Policy.Value(definition.Element) is not { } expected
                    || PlainLeaf1217(profile, definition.Element) is not { } leaf) continue;
                bool matches = expected switch
                {
                    bool flag => bool.TryParse(leaf.Value, out bool actual) && actual == flag,
                    int number => int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int actual) && actual == number,
                    string expression => leaf.Value == expression,
                    _ => false
                };
                if (matches) ledger.Tree(leaf, CharacterCreationSourceOperationDisposition.Compiled, result);
            }
        }
    }
}
