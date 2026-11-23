using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D3207 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileRegrouping3207(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (_sourceInputs.HasSourceDrift || profile.HasAttributes) return;
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            foreach (string field in new[] { "allowskillregrouping", "breakskillgroupsincreatemode" })
            {
                if (profile.Elements().Where(row => row.Name.LocalName == field).ToArray() is not [var leaf]
                    || leaf.Name != field || leaf.HasAttributes || leaf.Nodes().ToArray() is not [XText text]
                    || text.GetType() != typeof(XText) || leaf.Value != leaf.Value.Trim()
                    || !bool.TryParse(leaf.Value, out bool value)
                    || !invocation.TryGetExecutedSkillGroupSetting3207(_settingsProfileId, authority.RawProfileInputsDigest,
                        profileDigest, field, CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                        out bool executed, out bool inactive, out var digest) || executed != value) continue;
                // The private receipt names exactly this plain element and text.
                ledger.Tree(leaf, inactive ? D3207.NotSelected : D3207.Compiled, new
                {
                    Operation = "canonical-skill-group-setting3207", Field = field, Input = executed,
                    Result = executed, NoTarget = inactive, CanonicalExecution = digest
                });
            }
        }
    }
}
