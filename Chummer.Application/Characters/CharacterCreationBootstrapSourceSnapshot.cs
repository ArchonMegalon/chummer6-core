using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Immutable, activation-only capture of every source authority used to build the
/// first Creation screen. Domain projection receives the frozen context below and
/// therefore cannot delegate back to the live source corpus. A second complete
/// capture is performed only at consumer acceptance and must match this digest.
/// </summary>
public sealed record CharacterCreationBootstrapSourceSnapshot(
    string RawCharacterXmlDigest,
    CharacterCreationSourceProfileAuthority SourceProfile,
    bool SourceProfileResolved,
    CharacterCreationMetatypeCatalogAuthority Metatypes,
    bool MetatypesResolved,
    CharacterCreationPrerequisiteAuthority Prerequisite,
    bool PrerequisiteResolved,
    CharacterCreationQualitiesAuthority Qualities,
    bool QualitiesResolved,
    CharacterCreationMagicResonanceAuthority MagicResonance,
    bool MagicResonanceResolved,
    string SnapshotDigest)
{
    public bool CanProjectCompleteInitialCreation =>
        string.Equals(
            SourceProfile.BuildMethod,
            CharacterCreationBuildMethods.Priority,
            StringComparison.Ordinal)
        && SourceProfileResolved
        && MetatypesResolved
        && PrerequisiteResolved
        && QualitiesResolved
        && MagicResonanceResolved;

    public ICharacterSourceDataContext CreateFrozenContext() =>
        new FrozenCreationSourceDataContext(this);

    public static bool TryCapture(
        ICharacterSourceDataContext source,
        string characterXml,
        out CharacterCreationBootstrapSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(source);
        snapshot = Empty;
        try
        {
            string rawCharacterXmlDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeRawCharacterXmlDigest(characterXml);
            if (!TryRead(
                    source,
                    rawCharacterXmlDigest,
                    out CharacterCreationBootstrapSourceSnapshot captured))
            {
                return false;
            }

            snapshot = captured;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or FormatException
                                           or IOException
                                           or InvalidDataException
                                           or InvalidOperationException
                                           or UnauthorizedAccessException
                                           or System.Xml.XmlException)
        {
            return false;
        }
    }

    public static string ComputeDigest(CharacterCreationBootstrapSourceSnapshot snapshot)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            snapshot.RawCharacterXmlDigest,
            snapshot.SourceProfile,
            snapshot.SourceProfileResolved,
            snapshot.Metatypes,
            snapshot.MetatypesResolved,
            snapshot.Prerequisite,
            snapshot.PrerequisiteResolved,
            snapshot.Qualities,
            snapshot.QualitiesResolved,
            snapshot.MagicResonance,
            snapshot.MagicResonanceResolved
        });

    private static bool TryRead(
        ICharacterSourceDataContext source,
        string rawCharacterXmlDigest,
        out CharacterCreationBootstrapSourceSnapshot snapshot)
    {
        snapshot = Empty;
        bool profileResolved = source.TryResolveCreationSourceProfile(
            out CharacterCreationSourceProfileAuthority profile);
        bool metatypesResolved = source.TryResolveCreationMetatypeCatalog(
            out CharacterCreationMetatypeCatalogAuthority metatypes);
        bool prerequisiteResolved = source.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority prerequisite);
        bool qualitiesResolved = source.TryResolveCreationQualitiesAuthority(
            out CharacterCreationQualitiesAuthority qualities);
        bool magicResolved = source.TryResolveCreationMagicResonanceAuthority(
            out CharacterCreationMagicResonanceAuthority magic);
        bool prerequisiteRequired = profile.BuildMethod is
            CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen;
        if (!profileResolved
            || !metatypesResolved
            || prerequisiteRequired && !prerequisiteResolved)
        {
            return false;
        }

        var unsigned = new CharacterCreationBootstrapSourceSnapshot(
            rawCharacterXmlDigest,
            profile,
            profileResolved,
            metatypes,
            metatypesResolved,
            prerequisite,
            prerequisiteResolved,
            qualities,
            qualitiesResolved,
            magic,
            magicResolved,
            string.Empty);
        snapshot = unsigned with { SnapshotDigest = ComputeDigest(unsigned) };
        return CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(snapshot.SnapshotDigest);
    }

    private static CharacterCreationBootstrapSourceSnapshot Empty { get; } = new(
        string.Empty,
        CharacterCreationSourceProfileAuthority.Unavailable,
        false,
        CharacterCreationMetatypeCatalogAuthority.Unavailable,
        false,
        CharacterCreationPrerequisiteAuthority.Unavailable,
        false,
        CharacterCreationQualitiesAuthority.Unavailable,
        false,
        CharacterCreationMagicResonanceAuthority.Unavailable,
        false,
        string.Empty);

    private sealed class FrozenCreationSourceDataContext : ICharacterSourceDataContext
    {
        private readonly CharacterCreationBootstrapSourceSnapshot _snapshot;

        public FrozenCreationSourceDataContext(CharacterCreationBootstrapSourceSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public bool TryResolveCreationSourceProfile(out CharacterCreationSourceProfileAuthority authority)
        {
            authority = _snapshot.SourceProfile;
            return _snapshot.SourceProfileResolved;
        }

        public bool TryResolveCreationMetatypeCatalog(out CharacterCreationMetatypeCatalogAuthority authority)
        {
            authority = _snapshot.Metatypes;
            return _snapshot.MetatypesResolved;
        }

        public bool TryResolveCreationPrerequisiteAuthority(out CharacterCreationPrerequisiteAuthority authority)
        {
            authority = _snapshot.Prerequisite;
            return _snapshot.PrerequisiteResolved;
        }

        public bool TryResolveCreationQualitiesAuthority(out CharacterCreationQualitiesAuthority authority)
        {
            authority = _snapshot.Qualities;
            return _snapshot.QualitiesResolved;
        }

        public bool TryResolveCreationMagicResonanceAuthority(
            out CharacterCreationMagicResonanceAuthority authority)
        {
            authority = _snapshot.MagicResonance;
            return _snapshot.MagicResonanceResolved;
        }

        public bool TryIsBookEnabled(string sourceCode, out bool enabled)
        {
            enabled = _snapshot.SourceProfile.EnabledSourcebooks.Contains(
                sourceCode,
                StringComparer.OrdinalIgnoreCase);
            return !string.IsNullOrWhiteSpace(sourceCode);
        }

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
        {
            deviceRating = 0;
            return false;
        }

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
        {
            bonuses = CharacterVehicleModSourceBonuses.Empty;
            return false;
        }
    }
}
