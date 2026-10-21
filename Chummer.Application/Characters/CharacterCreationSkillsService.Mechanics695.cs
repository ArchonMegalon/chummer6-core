using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    // Share the allocation consumer's source/talent/movement gates when deriving
    // an untrained action. A catalogue entry alone does not make a usable skill.
    internal static bool IsActionSkillAvailable695(CharacterCreationSkillsAuthority authority,
        CharacterCreationSkillCatalogEntry skill, CharacterCreationMovementCapability movement)
        => !skill.IsExotic
            && CharacterCreationSkillsAccessRules.IsSkillAvailable(authority, skill.SourceSkillId)
            && IsMovementAvailable(skill, movement);
}
