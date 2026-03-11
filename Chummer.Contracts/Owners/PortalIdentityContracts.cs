namespace Chummer.Contracts.Owners;

public static class PortalIdentityProviderKinds
{
    public const string Password = "password";
    public const string OpenIdConnect = "openid-connect";
    public const string ApiKey = "api-key";
    public const string PortalBridge = "portal-bridge";
}

public static class PortalAccountStatuses
{
    public const string PendingConfirmation = "pending-confirmation";
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Deleted = "deleted";
}

public static class PortalSessionModes
{
    public const string InteractiveWeb = "interactive-web";
    public const string ApiClient = "api-client";
    public const string PortalBridge = "portal-bridge";
}

public static class PortalOwnerKinds
{
    public const string LocalSingleUser = "local-single-user";
    public const string Account = "account";
    public const string Campaign = "campaign";
    public const string Service = "service";
}

public sealed record PortalIdentityBinding(
    string ProviderKind,
    string SubjectId,
    string Email,
    bool EmailVerified = false,
    DateTimeOffset? LinkedAtUtc = null);

public sealed record PortalAccountProfile(
    string OwnerId,
    string Email,
    string DisplayName,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ConfirmedAtUtc = null,
    string? PreferredRulesetId = null,
    string? TimeZone = null);

public sealed record PortalSessionDescriptor(
    string SessionId,
    OwnerScope Owner,
    string Mode,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool IsPersistent = true,
    string? DeviceId = null);

public sealed record PortalOwnerDescriptor(
    OwnerScope Scope,
    string Kind,
    bool IsAuthenticated = false,
    string? ActorId = null,
    string? DisplayName = null);

public sealed record PortalAuthenticationReceipt(
    PortalOwnerDescriptor Owner,
    PortalAccountProfile Account,
    PortalSessionDescriptor Session,
    IReadOnlyList<PortalIdentityBinding> Identities);
