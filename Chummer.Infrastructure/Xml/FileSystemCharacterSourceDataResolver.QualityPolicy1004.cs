using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileQualityPolicy1004(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            foreach (string field in new[] { "exceedpositivequalities", "exceedpositivequalitiescostdoubled",
                "exceednegativequalities", "exceednegativequalitiesnobonus" })
            {
                var leaves = profile.Elements().Where(node => node.Name.LocalName == field).Take(2).ToArray();
                if (leaves is not [var leaf] || leaf.Name != field
                    || !invocation.TryGetExecutedProfileQualityPolicy1004(_settingsProfileId, authority.RawProfileInputsDigest,
                        profileDigest, field, CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                        out var result, out var cost, out bool value, out bool noTarget, out var execution)
                    || result is null || cost is null || _sourceInputs.HasSourceDrift) continue;
                // Account only this verified scalar and its text. Unknown siblings,
                // legacy aliases and the rest of the selected profile remain independent.
                ledger.Tree(leaf, D.Compiled, new
                {
                    Operation = "canonical-quality-limit-policy", Field = field, Value = value,
                    CostPolicy = cost, Result = result, NoTarget = noTarget, Execution = execution
                });
            }
        }
    }
}
