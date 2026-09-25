using System.Runtime.CompilerServices;

namespace Chummer.Application.Characters;

/// <summary>Engine-local source snapshot; never sent to a provider or UI.</summary>
public sealed record CharacterCreationFoundationEffectSources(
    string SettingsProfileId,
    string ProfileInputsDigest,
    IReadOnlyList<string> EnabledSourcebooks,
    string SkillsXml,
    string QualitiesXml,
    string QualityLevelsXml)
{
    // Weak, identity-keyed storage leaves record equality, serialization and
    // snapshot lifetime unchanged. Ordinary `with` copies get a fresh entry:
    // changing XML/profile inputs must never carry parsed authority forward.
    private static readonly ConditionalWeakTable<CharacterCreationFoundationEffectSources, CompilationSlot>
        Compilations = new();

    /// <summary>
    /// Detaches the caller's book collection while retaining this exact source
    /// snapshot's immutable compilation. This does not admit current source
    /// files; the resolver must still perform its live-byte checks on every use.
    /// </summary>
    public CharacterCreationFoundationEffectSources CopyWithDetachedSourcebooks()
    {
        var copy = this with
        {
            EnabledSourcebooks = EnabledSourcebooks is null
                ? null! : Array.AsReadOnly(EnabledSourcebooks.ToArray())
        };
        Compilations.Add(copy, Compilations.GetValue(this, static _ => new CompilationSlot()));
        return copy;
    }

    internal bool TryCreateAuthorities(out CharacterCreationFoundationSkillSourceAuthority? skills,
        out CharacterCreationFoundationQualitySourceAuthority? qualities,
        out CharacterCreationFoundationQualityLevelSourceAuthority? qualityLevels, out string contextDigest)
    {
        skills = null;
        qualities = null;
        qualityLevels = null;
        contextDigest = string.Empty;
        if (string.IsNullOrWhiteSpace(SettingsProfileId)
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(ProfileInputsDigest)
            || EnabledSourcebooks is null
            || string.IsNullOrWhiteSpace(SkillsXml) || string.IsNullOrWhiteSpace(QualitiesXml)
            || string.IsNullOrWhiteSpace(QualityLevelsXml))
            return false;

        // Public callers can supply a mutable IReadOnlyList. Freeze and check
        // its values on every call, including cache hits; do not key by list
        // identity or a case-insensitive digest that changes existing receipts.
        string[] suppliedBooks = EnabledSourcebooks.ToArray();
        if (suppliedBooks.Length == 0
            || suppliedBooks.Any(book => string.IsNullOrWhiteSpace(book) || book != book.Trim()))
            return false;
        var books = suppliedBooks.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] orderedBooks = books.OrderBy(book => book, StringComparer.Ordinal).ToArray();
        CompilationSlot slot = Compilations.GetValue(this, static _ => new CompilationSlot());
        lock (slot)
        {
            if (slot.Value is { } cached && orderedBooks.SequenceEqual(cached.Books, StringComparer.Ordinal))
            {
                skills = cached.Skills;
                qualities = cached.Qualities;
                qualityLevels = cached.QualityLevels;
                contextDigest = cached.ContextDigest;
                return true;
            }
            if (!TryCompile(books, orderedBooks, out skills, out qualities, out qualityLevels, out contextDigest))
                return false;
            slot.Value = new CompiledAuthorities(orderedBooks, skills!, qualities!, qualityLevels!, contextDigest);
            return true;
        }
    }

    private bool TryCompile(IReadOnlySet<string> books, string[] orderedBooks,
        out CharacterCreationFoundationSkillSourceAuthority? skills,
        out CharacterCreationFoundationQualitySourceAuthority? qualities,
        out CharacterCreationFoundationQualityLevelSourceAuthority? qualityLevels, out string contextDigest)
    {
        contextDigest = string.Empty;
        string skillsDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(SkillsXml);
        string qualitiesDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(QualitiesXml);
        string levelsDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(QualityLevelsXml);
        if (!CharacterCreationFoundationSkillSourceAuthority.TryCreate(SkillsXml, skillsDigest, out skills, books)
            || !CharacterCreationFoundationQualitySourceAuthority.TryCreate(QualitiesXml, qualitiesDigest, out qualities, books)
            || !CharacterCreationFoundationQualityLevelSourceAuthority.TryCreate(QualityLevelsXml, levelsDigest, out qualityLevels))
        {
            skills = null;
            qualities = null;
            qualityLevels = null;
            return false;
        }
        contextDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            SettingsProfileId, ProfileInputsDigest,
            Books = orderedBooks,
            SkillsDigest = skillsDigest, QualitiesDigest = qualitiesDigest, QualityLevelsDigest = levelsDigest
        });
        return true;
    }

    private sealed class CompilationSlot
    {
        public CompiledAuthorities? Value;
    }

    private sealed record CompiledAuthorities(string[] Books,
        CharacterCreationFoundationSkillSourceAuthority Skills,
        CharacterCreationFoundationQualitySourceAuthority Qualities,
        CharacterCreationFoundationQualityLevelSourceAuthority QualityLevels,
        string ContextDigest);
}
