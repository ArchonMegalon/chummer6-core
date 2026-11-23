using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteContactsExecution
{
    // Set only by the existing successful full/selected canonical pricing factories.
    private readonly SelectedCompletionContactsPrice economicPrice3211;

    internal bool TryReadEconomic3211(CharacterCreationCompleteAssessment authority,
        CharacterCreationKarmaContactsPolicy source, out string? result, out bool inactive)
    {
        result = null; inactive = false;
        if (DigestFor(authority) is null || !Same(source, contactsPolicy)
            || economicPrice3211.Blockers.Count != 0 || economicPrice3211.AdditionalKarmaCost is null)
            return false;
        // These are the completed effective projections, after edit semantics,
        // free-contact effects and Friends in High Places pricing. Request emptiness
        // and a zero charge cannot establish this branch.
        inactive = !economicPrice3211.Contacts.Any(contact => contact.IsGroup && !contact.Free);
        if (inactive != (economicPrice3211.GroupKarma is null)) return false;
        result = JsonSerializer.Serialize(new
        {
            Operation = inactive ? "canonical-contact-no-paid-group" : "canonical-contact-group-quality-karma",
            authorityDigest, evaluationDigest, contactsPolicy, attributesDigest,
            Result = economicPrice3211, Inactive = inactive
        });
        return true;
    }
}
