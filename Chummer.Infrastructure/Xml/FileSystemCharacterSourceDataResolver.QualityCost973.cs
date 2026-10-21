using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileQualityCost973(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (profile.Elements("karmacost").ToArray() is not [var costs]
                || costs.Elements("karmaquality").ToArray() is not [var leaf]
                || !invocation.TryGetExecutedProfileQualityCost973(_settingsProfileId, authority.RawProfileInputsDigest,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                    CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                    out var result, out var costPolicy, out bool noTarget, out var execution)
                || result is null || costPolicy is null || _sourceInputs.HasSourceDrift) return;
            // Only the verified scalar and its text; no sibling costs or flags.
            ledger.Tree(leaf, D.Compiled, new
            {
                Operation = "canonical-quality-karma-cost", CostPolicy = costPolicy, Result = result,
                NoTarget = noTarget, Execution = execution
            });
        }
    }
}
