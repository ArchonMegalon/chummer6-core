namespace Chummer.Contracts.Owners;

public readonly record struct OwnerScope(string Value)
{
    public static OwnerScope LocalSingleUser { get; } = new("local-single-user");

    public string NormalizedValue => string.IsNullOrWhiteSpace(Value)
        ? string.Empty
        : Value.Trim().ToLowerInvariant();

    public bool IsLocalSingleUser => string.Equals(
        NormalizedValue,
        LocalSingleUser.NormalizedValue,
        StringComparison.Ordinal);

    public override string ToString() => NormalizedValue;
}
