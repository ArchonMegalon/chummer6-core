using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterSustainedSpellService
{
    CharacterSustainedSpellResult Read(OwnerContextStamp owner, CharacterWorkspaceId workspace);
    CharacterSustainedSpellResult Preview(OwnerContextStamp owner, CharacterSustainedSpellBinding displayed,
        Guid operationId, CharacterSustainedSpellPreparationRequest request);
    CharacterSustainedSpellResult Confirm(OwnerContextStamp owner, CharacterSustainedSpellCommand command,
        string originalCommandDigest, CancellationToken cancellationToken = default);
    // Explicit exclusion of an original command under fresh current authorization.
    // Returns the applied receipt if Confirm won; cancellation never rolls it back.
    CharacterSustainedSpellResult Cancel(OwnerContextStamp currentOwner, CharacterSustainedSpellCommand originalCommand,
        string originalCommandDigest, CancellationToken cancellationToken = default);
    CharacterSustainedSpellResult Lookup(OwnerContextStamp currentOwner, CharacterSustainedSpellLookup original);
}
