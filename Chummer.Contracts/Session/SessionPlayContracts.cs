using Chummer.Contracts.Characters;
using Chummer.Contracts.Trackers;

namespace Chummer.Contracts.Session;

public static class SessionPlayEventTypes
{
    public const string ElementalFollowup = "elemental.followup";
    public const string MagicCapabilitiesAdopt = "magic.capabilities.adopt";
    public const string SpellCast = "spell.cast.direct";
    public const string IndirectSpellCast = "spell.cast.indirect";
    public const string IndirectCastingAdopt = "magic.indirect.adopt";
    public const string SustainingApply = "sustaining.apply";
    public const string SustainingEnd = "sustaining.end";
    public const string InitiativeCapabilitiesAdopt = "initiative.capabilities.adopt";
    public const string InitiativeRoll = "initiative.roll";
    public const string InitiativePass = "initiative.pass";
    public const string WeaponFire = "weapon.fire";
    public const string WeaponReload = "weapon.reload";
    public const string WeaponUnload = "weapon.unload";
}

/// <summary>A current review offer, never authority for an old operation result.</summary>
public sealed record SessionPlayInitiativeUpgradeOffer(
    IReadOnlyList<CharacterInitiativeModeOption> Modes,
    IReadOnlyDictionary<string, Chummer.Contracts.Rulesets.RulesetCapabilityValue> Payload);

public sealed record SessionPlayDiagnostic(string Code, string Capability, string Message, bool AuthorizesMutation = false);
public sealed record SessionPlayFailure(string Code, string Kind, SessionOperationRecoveryContract Recovery)
{
    public string? OperationId { get; init; }
}
public static class SessionPlayFailures
{
    public static SessionPlayFailure Conflict(string code) => new(code, "conflict", Recovery(code, false));
    public static SessionPlayFailure Unavailable(string code) => new(code, "unavailable", Recovery(code, true));
    public static SessionPlayFailure Invalid(string code) => new(code, "validation", Recovery(code, false));
    private static SessionOperationRecoveryContract Recovery(string code, bool resume) => new(
        "session-play", resume ? SessionOperationFailureClasses.Interrupted : SessionOperationFailureClasses.ValidationBlocked,
        resume ? SessionOperationRetryClasses.Resume : SessionOperationRetryClasses.DoNotRetry,
        resume, false, true, "session.play." + code,
        [new(resume ? SessionOperationSafeActionIds.Resume : SessionOperationSafeActionIds.Continue,
            resume ? "session.recovery.action.resume" : "session.recovery.action.continue_current_state", true)]);
}

/// <summary>Resolved by the service, never accepted as caller authority.</summary>
public sealed record SessionPlayBinding(string OwnerId, CharacterVersionReference Version, string SourceDigest, string AuthorityDigest);
/// <summary>When StandardWoundRules is true, Derived contains uninjured source values; replay applies current wounds.</summary>
public sealed record SessionPlayAuthority(
    SessionPlayBinding Binding,
    IReadOnlyList<TrackerDefinition> Trackers,
    CharacterSheetDerivedSection Derived,
    bool StandardWoundRules,
    IReadOnlyList<CharacterWeaponFireState> Weapons,
    IReadOnlyList<SessionPlayDiagnostic> Diagnostics)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayAmmunition? Ammunition { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SessionAmmunitionOperation5712>? SettledOperations5712 { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlaySustainingAuthority? SustainingAuthority { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayCastingAuthority? CastingAuthority { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayVehiclePolicy5400? VehiclePolicy { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayInitialCondition5412? InitialCondition5412 { get; init; }
}
public sealed record SessionPlayOrigin(string OverlayId, SessionPlayAuthority Authority, DateTimeOffset CreatedAtUtc);

/// <summary>Append-only owner-private local operation. No server acknowledgement is implied.</summary>
public sealed record SessionPlayOperation(
    string OperationId, string PayloadDigest, string Kind, string OverlayId,
    CharacterVersionReference BaseCharacterVersion, IReadOnlyList<SessionEventEnvelope> Events,
    string? ClientCursor, DateTimeOffset PreparedAtUtc);
public sealed record SessionPlayOperationResult(
    string OperationId, string PayloadDigest, SessionOverlaySnapshot? Snapshot,
    SessionSyncReceipt? Sync, SessionPlayFailure? Failure, DateTimeOffset CompletedAtUtc);
public sealed record SessionPlayStoredOperation(long Ordinal, SessionPlayOperation Intent, SessionPlayOperationResult? Result);
public sealed record SessionPlayProjection(
    string OperationId, string ReplicaCursor, CharacterSheetDerivedSection Derived,
    int? InitiativeScore, IReadOnlyList<CharacterWeaponFireState> Weapons,
    IReadOnlyList<SessionEventEnvelope> History, IReadOnlyList<SessionPlayDiagnostic> Diagnostics,
    string Transport = "local-replica", string MergePolicy = "ordered-append-no-last-writer-wins",
    bool EffectsAreAnnotations = true)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterInitiativeMode? InitiativeMode { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayVehiclePolicy5400? VehiclePolicy { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlaySustainingProjection? MechanicalEffects { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayCastingProjection? Casting { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayElementalProjection? ElementalEffects { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayAmmunition? Ammunition { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayConditionProjection5412? Condition5412 { get; init; }
}

/// <summary>Local ledger custody only. Total includes loaded rounds; Available excludes them.
/// Identities always name the original canonical gear. Transfers never manufacture XML items.</summary>
public sealed record SessionPlayAmmoInventory(Guid GearId, Guid SourceId, string DisplayName,
    int TotalRounds, int AvailableRounds);
public sealed record SessionPlayAmmoFeed(string Id, int Capacity);
public sealed record SessionPlayAmmoWeapon(Guid WeaponId, int AmmoSlot, string DisplayName,
    IReadOnlyList<SessionPlayAmmoFeed> Feeds, IReadOnlyList<Guid> CompatibleGearIds,
    Guid LoadedGearId, int LoadedRounds, string? Feed)
{
    // Absent on older origins: preserve their exact durable JSON and original-result recovery.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayWeaponConfiguration? Configuration { get; init; }
}
/// <summary>Source-bound custody configuration, not attack/damage or canonical-sheet authority.</summary>
public sealed record SessionPlayWeaponConfiguration(Guid SourceId, string SourceDigest,
    IReadOnlyList<SessionPlayWeaponAttachment> Attachments);
public sealed record SessionPlayWeaponAttachment(Guid InstanceId, Guid SourceId, Guid ParentWeaponId,
    string DisplayName, bool Included, bool Equipped, int Rating, string Mount, string ExtraMount, string Selection);
public sealed record SessionPlayAmmunition(string Revision, IReadOnlyList<SessionPlayAmmoInventory> Inventory,
    IReadOnlyList<SessionPlayAmmoWeapon> Weapons);
public enum SessionPlayAmmoTransferKind { Reload, Replace, Unload }
public sealed record SessionPlayAmmoTransfer(SessionPlayAmmoTransferKind Kind, Guid WeaponId,
    Guid GearId, string Feed, int Rounds, string WeaponRevision, string AmmunitionRevision);
