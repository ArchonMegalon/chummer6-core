using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileAttributeKarma3201(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (profile.Elements("karmacost").ToArray() is not [var costs]
                || costs.Elements("karmaattribute").ToArray() is not [var leaf]
                || !TryReadKarmaCost(profile, "karmaattribute", out int price)
                || !invocation.TryGetExecutedProfileAttributeKarma3201(authority.SettingsProfileId,
                    authority.RawProfileInputsDigest,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                    CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                    out int value, out var result, out var execution)
                || value != price || value != authority.KarmaAttribute || result is null || _sourceInputs.HasSourceDrift) return;
            ledger.Tree(leaf, CharacterCreationSourceOperationDisposition.Compiled, new
            {
                Operation = "canonical-attribute-Karma-price", Field = "karmaattribute",
                Value = value, Result = result, Execution = execution
            });
        }
    }
}
