namespace Chummer.Contracts.Owners;

public readonly record struct OwnerScope(string Value)
{
    private readonly bool _isTrustedLocalSingleUser;

    private OwnerScope(string value, bool isTrustedLocalSingleUser)
        : this(value)
    {
        _isTrustedLocalSingleUser = isTrustedLocalSingleUser;
    }

    public static OwnerScope LocalSingleUser { get; } = new(
        "local-single-user",
        isTrustedLocalSingleUser: true);

    public string NormalizedValue => string.IsNullOrWhiteSpace(Value)
        ? string.Empty
        : Value.Trim().ToLowerInvariant();

    public bool UsesLocalSingleUserValue => string.Equals(
        NormalizedValue,
        LocalSingleUser.NormalizedValue,
        StringComparison.Ordinal);

    public bool IsLocalSingleUser => _isTrustedLocalSingleUser && UsesLocalSingleUserValue;

    public override string ToString() => NormalizedValue;
}
