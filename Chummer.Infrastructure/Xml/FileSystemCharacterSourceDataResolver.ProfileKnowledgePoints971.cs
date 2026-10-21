using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileKnowledgePoints971(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            var leaves = profile.Elements().Where(node => node.Name.LocalName == "knowledgepointsexpression").ToArray();
            if (leaves is not [var leaf] || leaf.Name != "knowledgepointsexpression" || leaf.HasAttributes
                || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                || !TryResolveCreationSkillsAuthority(out var skills) || !skills.IsAuthoritative
                || skills.SettingsProfileId != authority.SettingsProfileId
                || skills.RawProfileInputsDigest != authority.RawProfileInputsDigest
                || !invocation.TryGetExecutedKnowledgePoints971(skills.SettingsProfileId, skills.RawProfileInputsDigest,
                    skills.KnowledgePointsExpression,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                    CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                    out var budget, out int overflow, out var execution) || budget is null || _sourceInputs.HasSourceDrift) return;
            // Only this plain leaf and its text are accounted. Empty allocations
            // still reach this point through the completed canonical budget path.
            ledger.Tree(leaf, D.Compiled, new
            {
                Operation = "canonical-knowledge-point-expression", Input = skills.KnowledgePointsExpression,
                Attributes = invocation.Attributes718, Result = budget, OverflowToActive = overflow, Execution = execution
            });
        }
    }
}
