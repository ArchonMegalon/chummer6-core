using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D1430 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteRetainedCompatibility1430(XElement profile,
            CharacterCreationPrerequisiteAuthority prerequisite, CharacterCreationRuleSourceScopeInvocation invocation,
            SourceScopeLedger ledger)
        {
            if (!invocation.HasLifecycleResultV1 || !TryResolveCreationMagicResonanceAuthority(out var magic) || !magic.IsAuthoritative
                || magic.SettingsProfileId != prerequisite.SettingsProfileId
                || magic.PrerequisiteAuthorityDigest != prerequisite.AuthorityDigest
                || magic.MysticAdeptPowerPointPolicy?.CanonicalSourceXml != profile.ToString(SaveOptions.DisableFormatting)
                || _sourceInputs.HasSourceDrift) return;
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            // Enumerate actual occurrences without an unknown-field fallback. Only the
            // private completed consumer can recognize one of the four exact leaves.
            foreach (var leaf in profile.Elements())
            {
                string relativePath = RelativeRetainedPath1430(profile, leaf);
                if (!invocation.TryGetExecutedRetainedCompatibility1430(magic, profileDigest, relativePath,
                    leaf.Name.ToString(), CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                    leaf.Value, out var result, out var execution) || result is null || _sourceInputs.HasSourceDrift) continue;
                ledger.Tree(leaf, D1430.Compiled, new
                {
                    Operation = "source-grounded-retained-native-compatibility1430", Result = result, Execution = execution
                });
            }
        }

        private static string RelativeRetainedPath1430(XElement profile, XElement node)
            => ReferenceEquals(profile, node) ? string.Empty : RelativeRetainedPath1430(profile, node.Parent!)
                + "/" + node.Name + "[" + node.Parent!.Nodes().TakeWhile(child => !ReferenceEquals(child, node)).Count() + "]";
    }
}
