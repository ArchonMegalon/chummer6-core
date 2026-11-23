using System.Xml;

namespace Chummer.Contracts.Characters;

/// <summary>SR5 Career manual contact edits. Creation points and expenses are deliberately outside this rule.</summary>
public static class CharacterCareerContactsRules
{
    public const string Version = "chummer.core.sr5-career-contacts/v1";
    public static IReadOnlyList<string> SourceAnchors { get; } = Array.AsReadOnly(new[]
    {
        "Chummer/Controls/Characters/ContactControl.cs#CreateSecondRowAsync:no-payment-in-play",
        "Chummer/Forms/Character Forms/CharacterCareer.cs#cmdAddContact_Click",
        "Chummer/Forms/Character Forms/CharacterShared.cs#AddContact",
        "Chummer/Forms/Character Forms/CharacterShared.cs#DeleteContact",
        "Chummer/Forms/Character Forms/CharacterShared.cs#AddEnemy",
        "Chummer/Forms/Character Forms/CharacterShared.cs#DeleteEnemy",
        "Chummer/Backend/Characters/Contact.cs#ConnectionMaximum",
        "Chummer/Backend/Characters/Contact.cs#LoyaltyEnabled",
        "Chummer.Contracts/Characters/CharacterContactEditSemantics.cs#CharacterContactEditSemanticsResolver"
    });

    public static bool IsValidIdentity(CharacterCareerContactIdentity? identity)
    {
        if (identity is null) return false;
        var fields = new[] { identity.Name, identity.Role, identity.Location, identity.CustomName, identity.Metatype,
            identity.Gender, identity.Age, identity.ContactType, identity.PreferredPayment, identity.HobbiesVice,
            identity.PersonalLife, identity.GroupName };
        if (fields.Any(value => value is null || value.Length > 1024) || identity.Notes is null || identity.Notes.Length > 16384)
            return false;
        try { foreach (string value in fields.Append(identity.Notes)) XmlConvert.VerifyXmlChars(value); return true; }
        catch (XmlException) { return false; }
    }

    public static bool IsValidChange(CharacterCareerContactChange? change)
        => change is not null && change.ContactId != Guid.Empty && Enum.IsDefined(change.Kind)
            && (change.Identity is null || IsValidIdentity(change.Identity))
            && (change.Connection is null or >= 1 and <= 12) && (change.Loyalty is null or >= 1 and <= 6)
            && (change.EntityType is null || Enum.IsDefined(change.EntityType.Value));

    private static bool IsValidState(CharacterCareerContactState? state)
        => state is not null && state.ContactId != Guid.Empty && IsValidIdentity(state.Identity)
            && Enum.IsDefined(state.EntityType) && state.Connection is >= 1 and <= 12 && state.Loyalty is >= 1 and <= 6
            && state.RemovalBlocker is null or "contact_linked_relation" or "contact_owned_relation"
                or "contact_nested_state_requires_cleanup";

    public static bool TryQuote(CharacterCareerContactChange change, CharacterCareerContactState? before,
        IReadOnlyList<CharacterContactRuleEffect> effects, out CharacterCareerContactsQuote? quote, out string error)
    {
        quote = null;
        error = "contact_change_invalid";
        if (!IsValidChange(change) || effects is null || effects.Count > 1024
            || effects.Any(effect => effect is null || effect.ImprovementType is not
                ("FriendsInHighPlaces" or "ContactForceGroup" or "ContactMakeFree" or "ContactForcedLoyalty")
                || new[] { effect.ImprovedName, effect.Value, effect.Unique, effect.Condition }
                    .Any(value => value is null || value.Length > 1024)
                || effect.Enabled <= 0 || effect.Condition is not ("" or "career")
                || (effect.ImprovementType != "FriendsInHighPlaces"
                    && effect.ImprovedName != change.ContactId.ToString("D")))) return false;
        bool adding = change.Kind == CharacterCareerContactChangeKind.Add;
        error = "contact_target_presence_conflict";
        if (adding != (before is null) || (before is not null && (!IsValidState(before) || before.ContactId != change.ContactId)))
            return false;
        var original = before ?? new(change.ContactId, new(), change.EntityType ?? CharacterCareerContactEntityType.Contact,
            1, 1, false, false, false, false);
        error = "contact_pet_requires_separate_authority";
        if (original.EntityType == CharacterCareerContactEntityType.Pet || change.EntityType == CharacterCareerContactEntityType.Pet)
            return false;
        error = "contact_entity_type_conversion_unsupported";
        if (!adding && change.EntityType is { } type && type != original.EntityType) return false;
        error = "contact_effect_authority_unresolved";
        if (!Resolve(original, effects, out var authority)) return false;
        if (change.Kind == CharacterCareerContactChangeKind.Remove)
        {
            error = "contact_remove_fields_invalid";
            if (change != new CharacterCareerContactChange(change.ContactId, CharacterCareerContactChangeKind.Remove)) return false;
            error = original.ReadOnly ? "contact_readonly_remove" : original.RemovalBlocker ?? "contact_remove_unavailable";
            if (!authority.CanDelete || original.Linked || original.RemovalBlocker is not null) return false;
            quote = new(before, null, authority, null, effects);
            error = string.Empty;
            return true;
        }
        var after = original with
        {
            Identity = change.Identity ?? original.Identity, Connection = change.Connection ?? original.Connection,
            Loyalty = change.Loyalty ?? original.Loyalty, IsGroup = change.IsGroup ?? original.IsGroup,
            Free = change.Free ?? original.Free, Family = change.Family ?? original.Family,
            Blackmail = change.Blackmail ?? original.Blackmail
        };
        error = "contact_linked_identity_edit";
        if (after.Identity != original.Identity && !authority.IdentityEditable) return false;
        error = "contact_readonly_connection";
        if (after.Connection != original.Connection && !authority.ConnectionEditable) return false;
        error = "contact_group_source_restricted";
        if (after.IsGroup != original.IsGroup && !authority.GroupEditable) return false;
        error = "contact_free_field_unavailable_in_career";
        if (after.Free != original.Free) return false;
        error = "contact_enemy_family_restricted";
        if (after.Family != original.Family && !authority.FamilyEditable) return false;
        error = "contact_enemy_blackmail_restricted";
        if (after.Blackmail != original.Blackmail && !authority.BlackmailEditable) return false;
        error = "contact_effect_authority_unresolved";
        if (!Resolve(after, effects, out var afterAuthority)) return false;
        error = "contact_loyalty_source_restricted";
        if (after.Loyalty != original.Loyalty && !afterAuthority.LoyaltyEditable) return false;
        error = "contact_no_change";
        if (!adding && after == before) return false;
        quote = new(before, after, authority, afterAuthority, effects);
        error = string.Empty;
        return true;
    }

    private static bool Resolve(CharacterCareerContactState state, IReadOnlyList<CharacterContactRuleEffect> effects,
        out CharacterContactEditSemantics semantics)
        => CharacterContactEditSemanticsResolver.TryResolve(new(state.ContactId.ToString("D"), true, state.ReadOnly,
            state.Linked, state.IsGroup, state.Free, state.Family, state.Blackmail, state.Connection, state.Loyalty,
            state.EntityType == CharacterCareerContactEntityType.Enemy), effects, out semantics);
}
