using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileLoadout1215(XElement profile, CharacterCreationPrerequisiteAuthority prerequisite,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            // Independent canonical resolution from retained bytes. A public projection,
            // report or forged digest cannot install the invocation's private execution.
            if (!TryResolveCreationLoadoutAuthority(out var authority)
                || !CharacterCreationLoadoutRules.IsValidAuthority(authority)
                || !TryResolveCreationLoadoutLegalityPolicy(out var legality) || legality is null
                || authority.SettingsProfileId != prerequisite.SettingsProfileId
                || authority.ProfileDigest != prerequisite.RawProfileInputsDigest
                || _sourceInputs.HasSourceDrift) return;
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            foreach (var leaf in profile.Descendants().Where(node => !node.HasElements))
            {
                string leafDigest = CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting));
                if (invocation.TryGetExecutedProfileLoadout1215(authority, legality, _rawCharacterXmlDigest,
                    profileDigest, ProfileRelativePath1215(profile, leaf), leafDigest, out var result))
                    ledger.Tree(leaf, D.Compiled, new { Operation = "selected-profile-loadout1215", Result = result });
            }
        }

        private static string ProfileRelativePath1215(XElement profile, XElement node)
            => ReferenceEquals(profile, node) ? string.Empty : ProfileRelativePath1215(profile, node.Parent!)
                + "/" + node.Name + "[" + node.Parent!.Nodes().TakeWhile(child => !ReferenceEquals(child, node)).Count() + "]";
    }
}
