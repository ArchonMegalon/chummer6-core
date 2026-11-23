using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D1241 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteRetainedSr5Compatibility1241(XElement profile,
            CharacterCreationPrerequisiteAuthority prerequisite, CharacterCreationRuleSourceScopeInvocation invocation,
            SourceScopeLedger ledger)
        {
            if (!TryResolveCreationMagicResonanceAuthority(out var magic) || !magic.IsAuthoritative
                || magic.SettingsProfileId != prerequisite.SettingsProfileId
                || magic.PrerequisiteAuthorityDigest != prerequisite.AuthorityDigest
                || magic.MysticAdeptPowerPointPolicy?.CanonicalSourceXml != profile.ToString(SaveOptions.DisableFormatting)
                || _sourceInputs.HasSourceDrift) return;
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            // Enumerate actual occurrences without an unknown-field fallback. Only the
            // private completed consumer can recognize one of the three exact leaves.
            foreach (var leaf in profile.Elements("karmacost").SelectMany(node => node.Elements()))
            {
                string relativePath = RelativeRetainedPath1241(profile, leaf);
                if (!invocation.TryGetExecutedRetainedSr5Compatibility1241(magic, profileDigest, relativePath,
                    leaf.Name.ToString(), CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                    leaf.Value, out var result, out var execution) || result is null || _sourceInputs.HasSourceDrift) continue;
                ledger.Tree(leaf, D1241.Compiled, new
                {
                    Operation = "source-grounded-retained-SR5-compatibility1241", Result = result, Execution = execution
                });
            }
        }

        private static string RelativeRetainedPath1241(XElement profile, XElement node)
            => ReferenceEquals(profile, node) ? string.Empty : RelativeRetainedPath1241(profile, node.Parent!)
                + "/" + node.Name + "[" + node.Parent!.Nodes().TakeWhile(child => !ReferenceEquals(child, node)).Count() + "]";
    }
}
