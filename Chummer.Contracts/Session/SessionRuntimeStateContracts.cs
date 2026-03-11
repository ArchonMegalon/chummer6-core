namespace Chummer.Contracts.Session;

public sealed record SessionProfileBinding(
    string CharacterId,
    string ProfileId,
    string RulesetId,
    string RuntimeFingerprint,
    DateTimeOffset SelectedAtUtc);

public sealed record SessionRuntimeBundleRecord(
    string CharacterId,
    string ProfileId,
    string RulesetId,
    SessionRuntimeBundleIssueReceipt Receipt,
    DateTimeOffset IssuedAtUtc);
