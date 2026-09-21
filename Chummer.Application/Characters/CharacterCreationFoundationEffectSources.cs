namespace Chummer.Application.Characters;

/// <summary>Engine-local source snapshot; never sent to a provider or UI.</summary>
public sealed record CharacterCreationFoundationEffectSources(
    string SettingsProfileId,
    string ProfileInputsDigest,
    IReadOnlyList<string> EnabledSourcebooks,
    string SkillsXml,
    string QualitiesXml)
{
    internal bool TryCreateAuthorities(out CharacterCreationFoundationSkillSourceAuthority? skills,
        out CharacterCreationFoundationQualitySourceAuthority? qualities, out string contextDigest)
    {
        skills = null;
        qualities = null;
        contextDigest = string.Empty;
        if (string.IsNullOrWhiteSpace(SettingsProfileId)
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(ProfileInputsDigest)
            || EnabledSourcebooks is null || EnabledSourcebooks.Count == 0
            || EnabledSourcebooks.Any(book => string.IsNullOrWhiteSpace(book) || book != book.Trim())
            || string.IsNullOrWhiteSpace(SkillsXml) || string.IsNullOrWhiteSpace(QualitiesXml))
            return false;

        var books = EnabledSourcebooks.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string skillsDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(SkillsXml);
        string qualitiesDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(QualitiesXml);
        if (!CharacterCreationFoundationSkillSourceAuthority.TryCreate(SkillsXml, skillsDigest, out skills, books)
            || !CharacterCreationFoundationQualitySourceAuthority.TryCreate(QualitiesXml, qualitiesDigest, out qualities, books))
        {
            skills = null;
            qualities = null;
            return false;
        }
        contextDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            SettingsProfileId, ProfileInputsDigest,
            Books = books.OrderBy(book => book, StringComparer.Ordinal).ToArray(),
            SkillsDigest = skillsDigest, QualitiesDigest = qualitiesDigest
        });
        return true;
    }
}
