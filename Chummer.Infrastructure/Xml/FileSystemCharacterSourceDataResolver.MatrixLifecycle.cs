using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        // Called only after the existing lifecycle/edition/source-drift gates.
        private static void ExecuteMatrixLifecycle(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (!invocation.TryGetExecutedMatrixInitiative(authority.SettingsProfileId, authority.RawProfileInputsDigest,
                CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)), out var native)) return;
            // Treat the four fields as a coherent policy. No partial admission of
            // a malformed group, even on the AI branch where two caps are inactive.
            foreach (var definition in CharacterCreationSettingDefinitions.MatrixLifecycle)
                if (!Integer1217(profile, definition.Element, out int value)
                    || value != native!.Policy.Value(definition.Key)) return;
            foreach (var definition in CharacterCreationSettingDefinitions.MatrixLifecycle)
                ledger.Tree(PlainLeaf1217(profile, definition.Element)!, CharacterCreationSourceOperationDisposition.Compiled, native);
        }
    }
}
