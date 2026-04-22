namespace Chummer.Contracts.Rulesets;

public static class SourceAnchorBindingPolicies
{
    public const string UserLocalFileOnly = "user-local-file-only";
}

public static class LocalSourceBindingStorageModes
{
    public const string DevicePrivate = "device-private";
}

public sealed record SourceAnchor(
    string Id,
    string RulesetId,
    string SourcePackRef,
    string Locale,
    int Page,
    string SectionHint,
    string AnchorKey,
    string BindingPolicy = SourceAnchorBindingPolicies.UserLocalFileOnly);

public sealed record LocalSourceBinding(
    string InstallRef,
    string SourcePackRef,
    string LocalFileHash,
    string LocalPathStorage = LocalSourceBindingStorageModes.DevicePrivate,
    bool CloudSync = false);
