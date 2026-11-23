using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private static void ExecuteNativeLoadout1432(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (!invocation.TryGetExecutedNativeLoadout1432(authority.SettingsProfileId, authority.RawProfileInputsDigest,
                CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)), out var native)) return;
            foreach (var definition in CharacterCreationNativeLoadoutDefinitions1432.All)
            {
                if (!native!.Supports(definition.Element) || PlainLeaf1217(profile, definition.Element) is not { } leaf) continue;
                bool matches = native.Policy.Value(definition.Element) switch
                {
                    bool expected => bool.TryParse(leaf.Value, out bool value) && value == expected,
                    int expected => int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value == expected,
                    _ => false
                };
                if (matches) ledger.Tree(leaf, CharacterCreationSourceOperationDisposition.Compiled, native);
            }
        }
    }
}
