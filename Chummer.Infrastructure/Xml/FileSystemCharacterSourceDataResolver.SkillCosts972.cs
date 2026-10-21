using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileSkillCosts972(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (profile.Elements("karmacost").ToArray() is not [var container] || container.HasAttributes
                || _sourceInputs.HasSourceDrift) return;
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            foreach (string field in new[] { "karmaspecialization", "karmaknospecialization", "karmanewknowledgeskill",
                "karmanewactiveskill", "karmanewskillgroup", "karmaimproveknowledgeskill", "karmaimproveactiveskill", "karmaimproveskillgroup" })
            {
                if (container.Elements().Where(node => node.Name.LocalName == field).ToArray() is not [var leaf]
                    || leaf.Name != field || leaf.HasAttributes || leaf.Nodes().ToArray() is not [XText text]
                    || text.GetType() != typeof(XText) || !TryReadKarmaCost(profile, field, out int sourceValue)
                    || !invocation.TryGetExecutedProfileSkillCost972(_settingsProfileId, authority.RawProfileInputsDigest,
                        profileDigest, field, CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                        out int value, out var execution) || sourceValue != value || _sourceInputs.HasSourceDrift) continue;
                // Account only this scalar's element/text, never its container or siblings.
                ledger.Tree(leaf, D.Compiled, new
                {
                    Operation = "canonical-skill-Karma-price", Field = field, Input = leaf.Value,
                    Result = value, CanonicalExecution = execution
                });
            }
        }
    }
}
