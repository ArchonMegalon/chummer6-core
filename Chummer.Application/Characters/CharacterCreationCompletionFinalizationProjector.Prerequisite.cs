using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCreationFinalizationProjector;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationCompletionFinalizationProjector
{
    // Preview and finalization must present the same confirmed selection to the
    // private priority-grant/source evaluator. These are compiled facts, not a
    // replacement for its fresh source replay or permission to change the draft.
    internal static void CompilePrerequisiteSelection(XElement root, CharacterCreationPrerequisiteDraft prerequisite)
    {
        var heritage = prerequisite.HeritageSelection!;
        SetDirect(root, "metatype", heritage.MetatypeName);
        SetDirect(root, "metavariant", heritage.MetavariantName ?? string.Empty);
        SetDirect(root, "metatypebp", heritage.KarmaCost.ToString(CultureInfo.InvariantCulture));
        SetDirect(root, "buildmethod", prerequisite.BuildMethod);
        foreach (var assignment in prerequisite.Assignments.OrderBy(value => value.Order))
        {
            string field = assignment.CategoryId switch
            {
                CharacterCreationPriorityCategoryIds.Heritage => "prioritymetatype",
                CharacterCreationPriorityCategoryIds.Talent => "priorityspecial",
                CharacterCreationPriorityCategoryIds.Attributes => "priorityattributes",
                CharacterCreationPriorityCategoryIds.Skills => "priorityskills",
                CharacterCreationPriorityCategoryIds.Resources => "priorityresources",
                _ => throw new InvalidDataException("Unknown Priority category.")
            };
            SetDirect(root, field, string.Create(CultureInfo.InvariantCulture, $"{assignment.Rank},{assignment.SumToTenValue}"));
        }
        SetDirect(root, "prioritytalent", prerequisite.TalentSelection!.Value);
        SetDirect(root, "sumtoten", prerequisite.SumToTenTarget.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
    }
}
