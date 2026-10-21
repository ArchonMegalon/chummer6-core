using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

// Server allocation, NOT a client attestation or campaign permission. The host
// admits its authenticated response; Core separately admits the live owner lease.
public sealed record CharacterCreationReservation(
    string Schema, string OperationId, string OwnerId, string InstallationId,
    CharacterWorkspaceId WorkspaceId, string IncarnationId, string RequestDigest,
    string AuthorityDigest, string ReservationDigest)
{
    public const string SchemaV1 = "chummer.new-creation-reservation/v1";
    public override string ToString() => nameof(CharacterCreationReservation);
    public static bool Id(string? value) => Guid.TryParseExact(value, "N", out var id)
        && id != Guid.Empty && id.ToString("N") == value;
    public static bool IsValid(CharacterCreationReservation? value) => value is not null
        && value.Schema == SchemaV1 && Id(value.OperationId) && Id(value.WorkspaceId.Value)
        && Id(value.IncarnationId) && value.OwnerId is { Length: > 0 and <= 512 }
        && value.OwnerId == value.OwnerId.Trim() && !value.OwnerId.Any(char.IsControl)
        && value.InstallationId is { Length: > 0 and <= 64 }
        && value.InstallationId == value.InstallationId.Trim() && !value.InstallationId.Any(char.IsControl)
        && CharacterCreationFinalizationDigest.IsCanonical(value.RequestDigest)
        && CharacterCreationFinalizationDigest.IsCanonical(value.AuthorityDigest)
        && value.ReservationDigest == CharacterCreationFinalizationDigest.Compute(value with { ReservationDigest = "" });
}

// Separate versioned path: old request, receipt and identity serialization stays exact.
public sealed record CharacterCreationReservedBootstrapRequest(
    string Schema, CharacterCreationBootstrapRequest Creation, CharacterCreationReservation Reservation)
{
    public const string SchemaV1 = "chummer.reserved-creation-bootstrap/v1";
}
