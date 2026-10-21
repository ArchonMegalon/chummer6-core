using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

public enum CharacterCreationPolicyDisposition { Effective, Unsupported, Missing }
public enum CharacterCreationPolicyDomain { Profile, Books, Amendments, SkillLimits, OtherSettings, Errata, CharacterMechanics }
public enum CharacterCreationPolicyConsumer { None, SourceResolver, StandardPrioritySkills }
public enum CharacterCreationPolicyReason
{
    ResolvedAuthority, ExistingConsumerDefault, ConfiguredLimitUnsupported, InvalidSetting,
    ConsumerUnavailable, UnclassifiedLegacySetting, AmendValidationUnavailable,
    ErrataAuthorityUnavailable, MechanicsCoverageUnavailable
}
public enum CharacterCreationPolicyRequirement { None, SkillLimits, Complete }
public enum CharacterCreationPolicySourceKind { Rules, GlobalAmend, SelectedAmend }

/// <summary>Content identity is evidence of observed bytes, not proof that every operation was applied.
/// SourceId is a fixed logical scope; identities are hashed and never contain filesystem paths.</summary>
public sealed record CharacterCreationPolicySource(
    CharacterCreationPolicySourceKind Kind, int Order, string SourceId, string IdentityDigest, string? ContentDigest);

public sealed record CharacterCreationPolicyDomainCoverage(
    CharacterCreationPolicyDomain Domain, CharacterCreationPolicyDisposition Disposition,
    CharacterCreationPolicyReason Reason);

/// <summary>Only recognized consumed settings have effective values. Unclassified legacy keys are
/// opaque identities with no raw names or values; their presence is never an application claim.</summary>
public sealed record CharacterCreationPolicySettingCoverage(
    CharacterCreationPolicyDomain Domain, string SettingId, CharacterCreationPolicyDisposition Disposition,
    int? IntegerValue, CharacterCreationPolicyConsumer Consumer, CharacterCreationPolicyReason Reason);

/// <summary>Bounded source-policy evidence. No save XML, owner, campaign account or local paths.
/// The v2 opt-in adds projections of admitted consumer policies; neither version establishes
/// complete rules, selected-effect evaluation or campaign applicability.</summary>
public sealed record CharacterCreationEffectivePolicyContext(
    string Schema, string RulesetId, string BuildMethod, string SettingsProfileId,
    string ProfileInputsDigest, IReadOnlyList<string> EnabledSourcebooks, string? BooksInputsDigest,
    IReadOnlyList<CharacterCreationPolicySource> Sources,
    IReadOnlyList<CharacterCreationPolicyDomainCoverage> Domains,
    IReadOnlyList<CharacterCreationPolicySettingCoverage> Settings, string PolicyDigest)
{
    // Explicit opt-in: legacy inventories omit this property and retain their exact digest shape.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationCompletionConsumerPolicy? Consumers { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSettingsObservation? SettingObservations { get; init; }

    // Selected history only. Input policy identity excludes this evaluated receipt;
    // the enclosing witness binds it and replay reconstructs it independently.
    // Full input admission rejects it. Historical JSON omits both new fields.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationPolicyConsumption? SelectedConsumption { get; init; }

    public const string SchemaV1 = "chummer.creation-effective-policy.v1";
    public const string SchemaV2 = "chummer.creation-effective-policy.v2";
    public const string Unavailable = "creation-effective-policy-unavailable";
    public const string Invalid = "creation-effective-policy-invalid";
    public const string CoverageUnavailable = "creation-effective-policy-coverage-unavailable";
    public const string ReplayUnavailable = "creation-effective-policy-selected-replay-unavailable";
    public const string SkillLimitsUnsupported = "creation-effective-policy-skill-limits-unsupported";
    public const int MaximumSettings = 1024;
    public const int MaximumSources = 256;
    public const string ActiveLimit = "active-skill-rating";
    public const string KnowledgeLimit = "knowledge-skill-rating";
    public const string GroupLimit = "skill-group-rating";

    // Fixed logical scopes, not local filenames. Order is part of the policy identity.
    public static IReadOnlyList<string> RulesScopes { get; } = Array.AsReadOnly(new[]
    {
        "profile", "metatypes", "priorities", "skills", "qualities", "traditions", "streams",
        "powers", "spells", "complexforms", "gear", "lifestyles", "weapons", "armor"
    });

    public static string ComputeDigest(CharacterCreationEffectivePolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CharacterCreationFinalizationDigest.Compute(context with { PolicyDigest = string.Empty, SelectedConsumption = null });
    }

    public static CharacterCreationPolicyDomainCoverage[] DescribeDomains(
        IReadOnlyList<CharacterCreationPolicySettingCoverage> settings, IReadOnlyList<CharacterCreationPolicySource> sources,
        string? booksInputsDigest)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sources);
        var limits = settings.Where(row => row.Domain == CharacterCreationPolicyDomain.SkillLimits).ToArray();
        var limitStatus = limits.Any(row => row.Disposition == CharacterCreationPolicyDisposition.Unsupported)
            ? CharacterCreationPolicyDisposition.Unsupported
            : limits.Length != 3 || limits.Any(row => row.Disposition == CharacterCreationPolicyDisposition.Missing)
                ? CharacterCreationPolicyDisposition.Missing : CharacterCreationPolicyDisposition.Effective;
        bool amends = sources.Any(row => row.Kind != CharacterCreationPolicySourceKind.Rules);
        return
        [
            new(CharacterCreationPolicyDomain.Profile, CharacterCreationPolicyDisposition.Effective, CharacterCreationPolicyReason.ResolvedAuthority),
            new(CharacterCreationPolicyDomain.Books, booksInputsDigest is null ? CharacterCreationPolicyDisposition.Missing : CharacterCreationPolicyDisposition.Effective,
                booksInputsDigest is null ? CharacterCreationPolicyReason.ConsumerUnavailable : CharacterCreationPolicyReason.ResolvedAuthority),
            new(CharacterCreationPolicyDomain.Amendments, amends ? CharacterCreationPolicyDisposition.Unsupported : CharacterCreationPolicyDisposition.Effective,
                amends ? CharacterCreationPolicyReason.AmendValidationUnavailable : CharacterCreationPolicyReason.ResolvedAuthority),
            new(CharacterCreationPolicyDomain.SkillLimits, limitStatus, limitStatus == CharacterCreationPolicyDisposition.Effective
                ? CharacterCreationPolicyReason.ResolvedAuthority : CharacterCreationPolicyReason.ConsumerUnavailable),
            new(CharacterCreationPolicyDomain.OtherSettings, CharacterCreationPolicyDisposition.Unsupported, CharacterCreationPolicyReason.UnclassifiedLegacySetting),
            new(CharacterCreationPolicyDomain.Errata, CharacterCreationPolicyDisposition.Missing, CharacterCreationPolicyReason.ErrataAuthorityUnavailable),
            new(CharacterCreationPolicyDomain.CharacterMechanics, CharacterCreationPolicyDisposition.Missing, CharacterCreationPolicyReason.MechanicsCoverageUnavailable)
        ];
    }
}
