namespace Chummer.Contracts.Characters;

/// <summary>
/// SR6 wire identities, not an assertion that a complete creation workflow is
/// available. Point Buy and Life Path must never dispatch to SR5 Karma or Life
/// Modules simply because those methods serve a similar purpose. The optional
/// SR6 Karma system also needs its own rules despite sharing a wire name with SR5.
/// </summary>
public static class Sr6CharacterCreationBuildMethods
{
    public const string Priority = "Priority";
    public const string SumToTen = "SumtoTen";
    public const string PointBuy = "PointBuy";
    public const string LifePath = "LifePath";
    public const string Karma = "Karma";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
        new[] { Priority, SumToTen, PointBuy, LifePath, Karma });

    public static bool IsKnown(string? buildMethod)
        => buildMethod is Priority or SumToTen or PointBuy or LifePath or Karma;
}
