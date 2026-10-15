using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Historical reconstruction of the three authorities used by bootstrap binding
/// and pending Priority validation. This is deliberately not an initial-screen
/// activation snapshot: the complete Quality and Magic authorities are admitted
/// independently through the completion inputs, not retained twice here.
/// Its schema and digest never impersonate CharacterCreationBootstrapSourceSnapshot.
/// </summary>
public sealed record CharacterCreationCompletionBootstrapSnapshot(
    string Schema,
    string RawCharacterXmlDigest,
    CharacterCreationSourceProfileAuthority SourceProfile,
    CharacterCreationMetatypeCatalogAuthority Metatypes,
    CharacterCreationPrerequisiteAuthority Prerequisite,
    string SnapshotDigest)
{
    public const string SchemaV1 = "chummer.creation-completion-bootstrap.v1";

    public static string ComputeDigest(CharacterCreationCompletionBootstrapSnapshot snapshot)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            snapshot with { SnapshotDigest = string.Empty });

    public bool IsValid()
        => Schema == SchemaV1
            && SourceProfile is not null && Metatypes is not null && Prerequisite is not null
            && SourceProfile.BuildMethod is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen
            && CharacterCreationFinalizationDigest.IsCanonical(RawCharacterXmlDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(SnapshotDigest)
            && SnapshotDigest == ComputeDigest(this);

    public static bool TryCapture(ICharacterSourceDataContext source, string characterXml,
        out CharacterCreationCompletionBootstrapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(source);
        snapshot = null!;
        try
        {
            if (!source.TryResolveCreationSourceProfile(out var profile) || profile is null
                || profile.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
                || !source.TryResolveCreationMetatypeCatalog(out var metatypes) || metatypes is null
                || !source.TryResolveCreationPrerequisiteAuthority(out var prerequisite) || prerequisite is null)
                return false;
            var captured = new CharacterCreationCompletionBootstrapSnapshot(SchemaV1,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(characterXml),
                profile, metatypes, prerequisite, string.Empty);
            snapshot = captured with { SnapshotDigest = ComputeDigest(captured) };
            return snapshot.IsValid();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException
            or IOException or InvalidOperationException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    // The normal bootstrap validator revalidates these actual authorities. The
    // frozen context cannot load live sources or answer other domain queries.
    public ICharacterSourceDataContext CreateFrozenContext() => new FrozenContext(this);

    private sealed class FrozenContext(CharacterCreationCompletionBootstrapSnapshot snapshot)
        : ICharacterSourceDataContext
    {
        public bool TryResolveCreationSourceProfile(out CharacterCreationSourceProfileAuthority authority)
        { authority = snapshot.SourceProfile; return true; }

        public bool TryResolveCreationMetatypeCatalog(out CharacterCreationMetatypeCatalogAuthority authority)
        { authority = snapshot.Metatypes; return true; }

        public bool TryResolveCreationPrerequisiteAuthority(out CharacterCreationPrerequisiteAuthority authority)
        { authority = snapshot.Prerequisite; return true; }

        public bool TryIsBookEnabled(string sourceCode, out bool enabled)
        {
            enabled = snapshot.SourceProfile.EnabledSourcebooks.Contains(sourceCode, StringComparer.OrdinalIgnoreCase);
            return !string.IsNullOrWhiteSpace(sourceCode);
        }

        public bool TryResolveCyberwareGradeDeviceRating(string gradeName, string improvementSource, out int deviceRating)
        { deviceRating = 0; return false; }

        public bool TryResolveVehicleModBonuses(string sourceId, string name, out CharacterVehicleModSourceBonuses bonuses)
        { bonuses = CharacterVehicleModSourceBonuses.Empty; return false; }
    }
}
