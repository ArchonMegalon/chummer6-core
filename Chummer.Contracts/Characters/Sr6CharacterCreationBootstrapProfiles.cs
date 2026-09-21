namespace Chummer.Contracts.Characters;

/// <summary>
/// Built-in SR6 pending-draft profiles. These identities never select SR5
/// settings.xml rows, even for methods whose wire names are shared.
/// A profile permits a pending draft, not finalization or an implemented editor.
/// </summary>
public static class Sr6CharacterCreationBootstrapProfiles
{
    public const string Priority = "652c3398-9526-46dd-a495-d124e274f631";
    public const string SumToTen = "d65ebcb5-87a4-4f76-9f06-0aba803eeaaf";
    public const string PointBuy = "e03f6922-8b87-410d-a5a0-3ef6a8b9a010";
    public const string LifePath = "5fb191bd-fdc6-44f0-8fb5-66f3c89a9eb4";
    public const string Karma = "d829439a-bc21-44c2-b22b-757205225cec";

    public static bool TryResolveCanonicalSettingsProfileId(string? method, out string profileId)
    {
        profileId = method switch
        {
            Sr6CharacterCreationBuildMethods.Priority => Priority,
            Sr6CharacterCreationBuildMethods.SumToTen => SumToTen,
            Sr6CharacterCreationBuildMethods.PointBuy => PointBuy,
            Sr6CharacterCreationBuildMethods.LifePath => LifePath,
            Sr6CharacterCreationBuildMethods.Karma => Karma,
            _ => string.Empty
        };
        return profileId.Length != 0;
    }

    public static bool IsExactCanonicalTuple(string? method, string? profileId)
        => TryResolveCanonicalSettingsProfileId(method, out string expected)
           && string.Equals(profileId, expected, StringComparison.Ordinal);

    public static string SettingsSourceAnchor(string profileId)
        => $"sr6.creation.profiles#profile:{profileId}";

    public static string[] ExpectedSourceAnchorIds(string method, string profileId)
    {
        if (!IsExactCanonicalTuple(method, profileId))
            return [];

        string[] core = ["sr6_core_2019:p58-79", SettingsSourceAnchor(profileId)];
        string? companion = method switch
        {
            Sr6CharacterCreationBuildMethods.SumToTen => "sr6_schattenkompendium_2022:p28",
            Sr6CharacterCreationBuildMethods.PointBuy => "sr6_schattenkompendium_2022:p29-31",
            Sr6CharacterCreationBuildMethods.LifePath => "sr6_schattenkompendium_2022:p32-49",
            Sr6CharacterCreationBuildMethods.Karma => "sr6_schattenkompendium_2022:p156-157",
            _ => null
        };
        return companion is null ? core : [.. core, companion];
    }
}
