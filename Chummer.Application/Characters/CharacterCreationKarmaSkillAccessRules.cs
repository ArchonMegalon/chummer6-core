using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Purchasable catalog identities for a source-bound Karma foundation. This does
/// not allocate skills or invent Priority grants. Callers must use current
/// source authorities; a digest alone does not authenticate a supplied catalog.
/// </summary>
public static class CharacterCreationKarmaSkillAccessRules
{
    public static CharacterCreationKarmaSkillAccess? Evaluate(CharacterCreationSkillsCatalog catalog,
        CharacterCreationKarmaTalentCatalog talents, CharacterCreationMetatypeOptionProjection metatype,
        string talentOptionId, string? selectedUnlock = null)
    {
        if (!CharacterCreationSkillsCatalogAuthority.IsValid(catalog)
            || talents is not { Schema: CharacterCreationKarmaTalentCatalog.SchemaV1,
                Options: not null, KarmaQuality: > 0 }
            || talents.SettingsProfileId != catalog.SettingsProfileId
            || talents.RawProfileInputsDigest != catalog.RawProfileInputsDigest
            || !CharacterCreationSkillsDigest.IsCanonical(talents.SourceInputsDigest)
            || talents.Options.Any(option => option is null)
            || talents.Options.Select(option => option.OptionId).Distinct(StringComparer.Ordinal).Count() != talents.Options.Count
            || talents.AuthorityDigest != CharacterCreationKarmaTalentAuthority.ComputeDigest(talents)
            || metatype is not { IsEnabled: true, Blockers.Count: 0, SourceAnchorIds.Count: > 0,
                Movement.Walk: not null, Movement.Run: not null, Movement.Sprint: not null }) return null;
        var talent = talents.Options.SingleOrDefault(option => option.OptionId == talentOptionId);
        if (talent is not { IsEnabled: true, Blockers.Count: 0, SourceAnchorIds.Count: > 0 }
            || !CharacterCreationKarmaTalentAuthority.IsCompatible(talent, metatype)) return null;

        var filters = new HashSet<string>(StringComparer.Ordinal);
        string[] choices = [];
        if (talent.OptionId == CharacterCreationKarmaTalentCatalog.MundaneOptionId)
        {
            if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(talent,
                CharacterCreationKarmaTalentAuthority.Mundane($"settings.xml#setting:{catalog.SettingsProfileId}")))
                return null;
        }
        else
        {
            if (talent.SourceNodeXml is not { Length: > 0 and <= 32 * 1024 }) return null;
            try
            {
                using var text = new StringReader(talent.SourceNodeXml);
                using var reader = XmlReader.Create(text, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024
                });
                var row = XElement.Load(reader);
                var expected = CharacterCreationKarmaTalentAuthority.Project(row, talents.KarmaQuality, true);
                if (expected is null || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(talent, expected))
                    return null;
                foreach (var effect in row.Element("bonus")!.Elements("unlockskills"))
                {
                    string[] options = effect.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (options.Length == 0 || !CharacterCreationSkillsAccessRules.TryChooseUnlock(effect,
                        [options[0]], string.Empty, out string single)) return null;
                    if (options.Length == 1) filters.Add(single);
                    else
                    {
                        // One explicit talent selector, not implicit activation
                        // of every comma-separated choice (e.g. Aspected Mage).
                        if (choices.Length != 0) return null;
                        choices = options.Order(StringComparer.Ordinal).ToArray();
                    }
                }
            }
            catch (XmlException) { return null; }
        }

        string[] blockers = choices.Length == 0
            ? selectedUnlock is null ? [] : [CharacterCreationKarmaSkillAccess.UnlockInvalid]
            : selectedUnlock is null ? [CharacterCreationKarmaSkillAccess.UnlockRequired]
            : choices.Contains(selectedUnlock, StringComparer.Ordinal) ? [] : [CharacterCreationKarmaSkillAccess.UnlockInvalid];
        if (choices.Length > 0 && blockers.Length == 0) filters.Add(selectedUnlock!);
        var rates = new[] { metatype.Movement.Walk, metatype.Movement.Run, metatype.Movement.Sprint };
        if (rates.Any(rate => rate.Ground < 0 || rate.Swim < 0 || rate.Fly < 0)) return null;
        var movement = new CharacterCreationMovementCapability(
            !metatype.Movement.IsSpecial && rates.Any(rate => rate.Ground > 0),
            !metatype.Movement.IsSpecial && rates.Any(rate => rate.Swim > 0),
            !metatype.Movement.IsSpecial && rates.Any(rate => rate.Fly > 0));
        string[] active = blockers.Length != 0 ? [] : catalog.ActiveSkills.Where(skill =>
                (!skill.RequiresGroundMovement || movement.Ground)
                && (!skill.RequiresSwimMovement || movement.Swim)
                && (!skill.RequiresFlyMovement || movement.Fly)
                && (CharacterCreationSkillsAccessRules.IsOrdinary(skill)
                    || filters.Any(filter => CharacterCreationSkillsAccessRules.Matches(skill, filter))))
            .Select(skill => skill.SourceSkillId).Order(StringComparer.Ordinal).ToArray();
        var allowed = active.ToHashSet(StringComparer.Ordinal);
        // Disabled members remain in the canonical catalog but do not prevent
        // spending on a group that has other enabled members (legacy behavior).
        string[] groups = catalog.SkillGroups.Where(group => group.MemberSkillSourceIds.Any(allowed.Contains))
            .Select(group => group.GroupId).Order(StringComparer.Ordinal).ToArray();
        var result = new CharacterCreationKarmaSkillAccess(CharacterCreationKarmaSkillAccess.SchemaV1,
            catalog.CatalogDigest, talent.SourceNodeDigest,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(metatype),
            selectedUnlock, movement, choices, active, groups, blockers, string.Empty);
        return result with
        {
            AccessDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(result)
        };
    }
}
