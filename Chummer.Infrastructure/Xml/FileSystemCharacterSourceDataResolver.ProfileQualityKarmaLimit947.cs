using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileQualityKarmaLimit947(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (profile.Elements("qualitykarmalimit").ToArray() is not [var leaf]
                || leaf.HasAttributes || leaf.HasElements || leaf.Nodes().Any(node => node.GetType() != typeof(XText))
                || !int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit) || limit < 0
                || !invocation.TryGetExecutedProfileQualityKarmaLimit947(_settingsProfileId, authority.RawProfileInputsDigest,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                    CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                    out var result, out bool noTarget, out var execution) || result is null
                || result.PositiveQualityBudget.Total != limit || result.NegativeQualityBudget.Total != limit
                || _sourceInputs.HasSourceDrift) return;
            ledger.Tree(leaf, D.Compiled, new
            {
                Operation = "canonical-quality-karma-limit", Limit = limit, Result = result,
                NoTarget = noTarget, Execution = execution
            });
        }
    }
}
