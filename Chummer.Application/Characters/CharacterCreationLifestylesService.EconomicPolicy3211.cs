using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationLifestylesService
{
    internal sealed partial class CompleteGraphExecution
    {
        internal bool TryReadEconomic3211(CharacterCreationCompleteAssessment authority,
            CharacterCreationLifestylesAuthority source, out string? result)
        {
            result = null;
            if (authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(source, input.Authority)
                || price.Blockers.Count != 0 || source.FreeGridsActivation is null
                || price.Authority.FreeGridsEnabled != source.FreeGridsEnabled
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    price.Authority.FreeGridsActivation, source.FreeGridsActivation)) return false;
            // The retained resolver reads the setting and HT sourcebook activation
            // before any basket enumeration. Price has completed source/grant checks
            // and the effective basket; an empty request is not this capability.
            result = JsonSerializer.Serialize(new
            {
                Operation = "canonical-lifestyle-free-grid-activation", authorityDigest, EvaluationDigest,
                source.FreeGridsEnabled, source.FreeGridsActivation, Result = price
            });
            return true;
        }
    }
}
