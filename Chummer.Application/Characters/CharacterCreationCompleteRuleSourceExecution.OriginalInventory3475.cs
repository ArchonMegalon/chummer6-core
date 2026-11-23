namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // The same retained resolver used by Complete, not a source receipt supplied
    // by the proposal. This exposes no mutation or acquisition capability.
    internal ICharacterSourceDataResolver OriginalInventorySources3475 => resolution.Resolver;
}
