using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private static void ExecuteEssenceSettings(XElement profile,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (!invocation.TryGetEssence1222(out var execution) || execution is null
                || execution.GraphDigest != invocation.GraphDigest
                || !CharacterCreationEssenceSettingEvidenceRules.Matches(profile, execution, out var evidence)) return;
            foreach (var consumed in evidence!.Settings)
            {
                string field = CharacterCreationEssenceSettingDefinitions.All
                    .Single(row => row.Control == consumed.Setting.Control).Element;
                // Matches rechecks the exact leaf shape, source digest and value.
                ledger.Tree(profile.Element(field)!, CharacterCreationSourceOperationDisposition.Compiled,
                    new { Operation = "Finish-character-Essence-setting", execution.ExecutionDigest,
                        execution.SourceDigest, invocation.GraphDigest, Consumption = consumed });
            }
        }
    }

    private sealed partial class EssenceEngine1222
    {
        internal bool UnclampMinimum { get; private set; }
        private IReadOnlyList<Chummer.Application.Characters.CharacterCreationEssenceRules1222.Attribute> executedAttributes = [];

        internal CharacterCreationEssenceSettingEvidence? BindSettings(
            CharacterCreationEssenceSettingPolicy policy, CharacterCreationEssenceRules1222.Result result)
        {
            if (policy.Settings.Count == 0) return null;
            Require(executedAttributes.Count == result.Attributes.Count
                && executedAttributes.Select(row => row.Id).SequenceEqual(result.Attributes.Select(row => row.Id)));
            var consumed = new List<CharacterCreationEssenceSettingConsumption>();
            foreach (var setting in policy.Settings)
            {
                bool value = setting.Control switch
                {
                    CharacterCreationEssenceControl.MaximumOnly => MaximumOnly,
                    CharacterCreationEssenceControl.ShownValue => ShownValue,
                    CharacterCreationEssenceControl.Unrounded => Unrounded,
                    CharacterCreationEssenceControl.UnclampMinimum => UnclampMinimum,
                    _ => throw new InvalidDataException("Unknown Essence control.")
                };
                Require(setting.Value == value);
                consumed.Add(new(setting, CharacterCreationEssenceSettingEvidenceRules.Branch(setting.Control, executedAttributes.Count, ShownValue),
                    CharacterCreationFinalizationDigest.Compute(new
                    {
                        Operation = "Finish-character-Essence-setting", setting, decimals, Acquisition,
                        Unrounded, MaximumOnly, ShownValue, UnclampMinimum,
                        Attributes = executedAttributes, WareTrace, Effects, SourceBindings
                    }), CharacterCreationFinalizationDigest.Compute(result)));
            }
            return new(policy, consumed.AsReadOnly());
        }
    }
}
