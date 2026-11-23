using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D3200 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileMagic3200(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (_sourceInputs.HasSourceDrift) return;
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            foreach (string field in new[] { "karmamysadpp", "priorityspellsasadeptpowers", "mysadeptsecondmagattribute" })
            {
                var container = profile;
                if (field == "karmamysadpp")
                {
                    if (profile.Elements("karmacost").ToArray() is not [var costs] || costs.HasAttributes) continue;
                    container = costs;
                }
                if (container.Elements().Where(row => row.Name.LocalName == field).ToArray() is not [var leaf]
                    || leaf.Name != field || leaf.HasAttributes || leaf.Nodes().ToArray() is not [XText text]
                    || text.GetType() != typeof(XText)
                    || !invocation.TryGetExecutedProfileMagic3200(_settingsProfileId, authority.RawProfileInputsDigest,
                        profileDigest, field, CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                        out var value, out bool inactive, out var kind, out var execution) || _sourceInputs.HasSourceDrift) continue;
                ledger.Tree(leaf, inactive ? D3200.NotSelected : D3200.Compiled, new
                {
                    Operation = "canonical-profile-magic3200", Field = field, Input = leaf.Value,
                    Result = value, TalentKind = kind, Inactive = inactive, CanonicalExecution = execution
                });
            }
        }
    }
}
