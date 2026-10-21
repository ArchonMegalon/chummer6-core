using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Checked, read-only diagnostics. Copying these values cannot register execution.
    public bool TryGetExecutedProfileCarryover995(CharacterCreationKarmaCarryoverPolicy policy,
        string profileXmlDigest, string leafXmlDigest, out int remaining, out int carried, out string? executionDigest)
        => CharacterCreationCompleteRuleSourceExecution.TryReadCarryover995(this, policy, profileXmlDigest,
            leafXmlDigest, out remaining, out carried, out executionDigest);
}
