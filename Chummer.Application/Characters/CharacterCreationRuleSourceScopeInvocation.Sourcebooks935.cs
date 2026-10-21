using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Read-only typed output, never a transferable execution token. Registration
    // is private to the completed consumer and keyed to this exact invocation.
    public bool TryGetExecutedProfileSourcebooks935(string profileId, string profileInputsDigest, string booksInputsDigest,
        out CharacterCreationSourcebookSelectionProjection.Result? projection,
        out CharacterCreationSourcebookSelectionAuthority? catalogue)
        => CharacterCreationCompleteRuleSourceExecution.TryReadProfileSourcebooks935(this,
            profileId, profileInputsDigest, booksInputsDigest, out projection, out catalogue);
}
