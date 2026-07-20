using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public sealed class DelegatedGmCharacterEditService : IDelegatedGmCharacterEditService
{
    private const int MaximumOperationCount = 3;
    private const int MaximumReasonLength = 500;
    private const int MaximumIdempotencyKeyLength = 200;
    private const int MaximumIdentifierLength = 200;
    private const int MaximumNameLength = 256;
    private const int MaximumAliasLength = 256;
    private const int MaximumNotesLength = 4096;

    private static readonly ImmutableHashSet<string> AllowedPatchPaths =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            DelegatedGmCharacterEditContract.ProfileNamePath,
            DelegatedGmCharacterEditContract.ProfileAliasPath,
            DelegatedGmCharacterEditContract.ProfileNotesPath);

    private static readonly ImmutableHashSet<string> ForbiddenPathSegments =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "owner",
            "owner-id",
            "account",
            "account-id",
            "auth",
            "authentication",
            "authorization",
            "provenance",
            "private-contact",
            "private-contacts",
            "private_contact",
            "private_contacts");

    private readonly IWorkspaceStore _workspaceStore;
    private readonly IRulesetWorkspaceCodecResolver _workspaceCodecResolver;
    private readonly ICampaignGmCharacterEditAuthorizer _authorizer;
    private readonly TimeProvider _timeProvider;

    public DelegatedGmCharacterEditService(
        IWorkspaceStore workspaceStore,
        IRulesetWorkspaceCodecResolver workspaceCodecResolver,
        ICampaignGmCharacterEditAuthorizer authorizer,
        TimeProvider? timeProvider = null)
    {
        _workspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
        _workspaceCodecResolver = workspaceCodecResolver
            ?? throw new ArgumentNullException(nameof(workspaceCodecResolver));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DelegatedGmCharacterEditResult Execute(DelegatedGmCharacterEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TryNormalizeCommand(
                command,
                out NormalizedCommand normalized,
                out DelegatedGmCharacterEditResult? validationFailure))
        {
            return validationFailure!;
        }

        CampaignGmCharacterEditAuthorization authorization;
        try
        {
            authorization = _authorizer.Authorize(new CampaignGmCharacterEditAuthorizationRequest(
                normalized.CampaignId,
                normalized.ActorId,
                normalized.CharacterOwner,
                normalized.CharacterId,
                normalized.Operations.Select(static operation => operation.Path).ToImmutableArray()));
        }
        catch
        {
            return Failure(
                DelegatedGmCharacterEditOutcome.Unavailable,
                "campaign_authority_unavailable",
                "Campaign authorization could not be verified.");
        }

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        if (!IsValidAuthorization(normalized, authorization, nowUtc))
        {
            return Failure(
                DelegatedGmCharacterEditOutcome.Denied,
                "campaign_delegation_denied",
                "An active, campaign-bound Game Master delegation is required.");
        }

        string commandSha256 = ComputeCommandSha256(normalized);
        string idempotencyKeySha256 = ComputeSha256(normalized.IdempotencyKey);
        DelegatedGmCharacterEditStoreResult replay = _workspaceStore.LookupDelegatedGmCharacterEdit(
            normalized.CharacterOwner,
            normalized.CharacterId,
            idempotencyKeySha256,
            commandSha256);
        DelegatedGmCharacterEditResult? lookupResult = MapLookupResult(replay);
        if (lookupResult is not null)
        {
            return lookupResult;
        }

        WorkspaceStoreReadResult read = _workspaceStore.Get(
            normalized.CharacterOwner,
            normalized.CharacterId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument current)
        {
            return MapWorkspaceReadFailure(read);
        }

        if (current.ContentRevision != normalized.ExpectedRevision)
        {
            return Failure(
                DelegatedGmCharacterEditOutcome.Conflict,
                "stale_revision",
                "Character changed since the expected revision.");
        }

        WorkspaceDocument updatedDocument;
        try
        {
            WorkspacePayloadEnvelope currentEnvelope = current.Document.PayloadEnvelope;
            IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(currentEnvelope.RulesetId);
            CharacterValidationResult currentValidation = codec.Validate(currentEnvelope);
            if (!currentValidation.IsValid)
            {
                return Failure(
                    DelegatedGmCharacterEditOutcome.Corrupt,
                    "character_document_corrupt",
                    "Character document failed canonical validation.");
            }

            UpdateWorkspaceMetadata metadata = BuildMetadataPatch(normalized.Operations);
            WorkspacePayloadEnvelope updatedEnvelope = codec.UpdateMetadata(currentEnvelope, metadata);
            _ = codec.ParseSummary(updatedEnvelope);
            CharacterValidationResult updatedValidation = codec.Validate(updatedEnvelope);
            if (!updatedValidation.IsValid
                || codec.ParseSection("profile", updatedEnvelope) is not CharacterProfileSection)
            {
                return Failure(
                    DelegatedGmCharacterEditOutcome.Invalid,
                    "patch_breaks_character_contract",
                    "The bounded patch would make the character document invalid.");
            }

            updatedDocument = new WorkspaceDocument(
                new WorkspaceDocumentState(updatedEnvelope),
                current.Document.Format);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or FormatException
                                           or InvalidDataException
                                           or InvalidOperationException
                                           or JsonException
                                           or XmlException)
        {
            return Failure(
                DelegatedGmCharacterEditOutcome.Corrupt,
                "character_patch_failed",
                "Character document could not be patched by its canonical ruleset codec.");
        }

        long newRevision = normalized.ExpectedRevision + 1;
        DelegatedGmCharacterEditAuditReceipt receipt = BuildAuditReceipt(
            normalized,
            authorization,
            idempotencyKeySha256,
            commandSha256,
            newRevision,
            nowUtc);
        DelegatedGmCharacterEditStoreResult committed = _workspaceStore.ApplyDelegatedGmCharacterEdit(
            normalized.CharacterOwner,
            normalized.CharacterId,
            normalized.ExpectedRevision,
            updatedDocument,
            new DelegatedGmCharacterEditLedgerEntry(
                idempotencyKeySha256,
                commandSha256,
                receipt));
        return MapCommitResult(committed);
    }

    private static bool TryNormalizeCommand(
        DelegatedGmCharacterEditCommand command,
        out NormalizedCommand normalized,
        out DelegatedGmCharacterEditResult? failure)
    {
        normalized = default;
        failure = null;

        string campaignId = command.CampaignId?.Trim() ?? string.Empty;
        string actorId = command.ActorId?.Trim() ?? string.Empty;
        string characterId = command.CharacterId.Value?.Trim() ?? string.Empty;
        string reason = command.Reason?.Trim() ?? string.Empty;
        string idempotencyKey = command.IdempotencyKey?.Trim() ?? string.Empty;
        OwnerScope owner = command.CharacterOwner;

        if (!IsBoundedIdentifier(campaignId)
            || !IsBoundedIdentifier(actorId)
            || !IsBoundedIdentifier(characterId)
            || string.IsNullOrWhiteSpace(owner.NormalizedValue)
            || owner.UsesLocalSingleUserValue)
        {
            failure = Failure(
                DelegatedGmCharacterEditOutcome.Invalid,
                "invalid_authority_binding",
                "Campaign, actor, owner, and character identifiers must be non-local and bounded.");
            return false;
        }

        if (command.ExpectedRevision <= 0 || command.ExpectedRevision == long.MaxValue)
        {
            failure = Failure(
                DelegatedGmCharacterEditOutcome.Invalid,
                "invalid_expected_revision",
                "ExpectedRevision must identify a mutable positive revision.");
            return false;
        }

        if (reason.Length is 0 or > MaximumReasonLength || ContainsUnsupportedControlCharacter(reason))
        {
            failure = Failure(
                DelegatedGmCharacterEditOutcome.Invalid,
                "reason_required",
                "A bounded, nonblank edit reason is required.");
            return false;
        }

        if (idempotencyKey.Length is 0 or > MaximumIdempotencyKeyLength
            || idempotencyKey.Any(static character =>
                !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '/')))
        {
            failure = Failure(
                DelegatedGmCharacterEditOutcome.Invalid,
                "invalid_idempotency_key",
                "IdempotencyKey must use the bounded opaque-key grammar.");
            return false;
        }

        if (command.Operations is null
            || command.Operations.Count is 0 or > MaximumOperationCount)
        {
            failure = Failure(
                DelegatedGmCharacterEditOutcome.Invalid,
                "invalid_patch_count",
                "One to three bounded patch operations are required.");
            return false;
        }

        ImmutableArray<NormalizedPatchOperation>.Builder operations =
            ImmutableArray.CreateBuilder<NormalizedPatchOperation>(command.Operations.Count);
        HashSet<string> seenPaths = new(StringComparer.Ordinal);
        foreach (DelegatedGmCharacterPatchOperation? operation in command.Operations)
        {
            if (operation is null || !Enum.IsDefined(operation.Operation))
            {
                failure = Failure(
                    DelegatedGmCharacterEditOutcome.Invalid,
                    "invalid_patch_operation",
                    "Only declared bounded patch operations are accepted.");
                return false;
            }

            string path = NormalizePath(operation.Path);
            if (IsForbiddenPath(path))
            {
                failure = Failure(
                    DelegatedGmCharacterEditOutcome.Forbidden,
                    "forbidden_character_field",
                    "Owner, account, authorization, provenance, and private-contact fields cannot be delegated.");
                return false;
            }

            if (operation.Operation != DelegatedGmCharacterPatchOperationKind.Replace
                || !AllowedPatchPaths.Contains(path)
                || !seenPaths.Add(path)
                || operation.Value is null
                || operation.Value.Length > MaximumValueLength(path)
                || ContainsUnsupportedControlCharacter(operation.Value))
            {
                failure = Failure(
                    DelegatedGmCharacterEditOutcome.Invalid,
                    "patch_outside_delegated_scope",
                    "The patch must contain unique replace operations for allowlisted profile fields.");
                return false;
            }

            operations.Add(new NormalizedPatchOperation(operation.Operation, path, operation.Value));
        }

        normalized = new NormalizedCommand(
            campaignId,
            actorId,
            owner,
            new CharacterWorkspaceId(characterId),
            command.ExpectedRevision,
            idempotencyKey,
            reason,
            operations.ToImmutable().Sort(static (left, right) =>
                StringComparer.Ordinal.Compare(left.Path, right.Path)));
        return true;
    }

    private static bool IsValidAuthorization(
        NormalizedCommand command,
        CampaignGmCharacterEditAuthorization authorization,
        DateTimeOffset nowUtc)
    {
        if (authorization is null
            || !authorization.Authorized
            || !string.Equals(authorization.CampaignId?.Trim(), command.CampaignId, StringComparison.Ordinal)
            || !string.Equals(authorization.ActorId?.Trim(), command.ActorId, StringComparison.Ordinal)
            || !string.Equals(authorization.Role, DelegatedGmCharacterEditContract.GameMasterRole, StringComparison.Ordinal)
            || !string.Equals(authorization.Scope, DelegatedGmCharacterEditContract.CharacterEditScope, StringComparison.Ordinal)
            || !string.Equals(authorization.CharacterOwner.NormalizedValue, command.CharacterOwner.NormalizedValue, StringComparison.Ordinal)
            || authorization.CharacterOwner.UsesLocalSingleUserValue
            || !string.Equals(authorization.CharacterId.Value, command.CharacterId.Value, StringComparison.Ordinal)
            || !IsBoundedIdentifier(authorization.DelegationId)
            || !IsBoundedIdentifier(authorization.GrantedByCampaignOwnerId)
            || !IsBoundedIdentifier(authorization.AuthorityReceiptId)
            || authorization.AuthorityRevision <= 0
            || authorization.ValidFromUtc.ToUniversalTime() > nowUtc
            || authorization.ExpiresAtUtc.ToUniversalTime() <= nowUtc
            || authorization.ExpiresAtUtc <= authorization.ValidFromUtc
            || authorization.AllowedPatchPaths.IsDefaultOrEmpty)
        {
            return false;
        }

        ImmutableHashSet<string> authorizedPaths = authorization.AllowedPatchPaths
            .Select(NormalizePath)
            .Where(AllowedPatchPaths.Contains)
            .ToImmutableHashSet(StringComparer.Ordinal);
        return command.Operations.All(operation => authorizedPaths.Contains(operation.Path));
    }

    private static UpdateWorkspaceMetadata BuildMetadataPatch(
        ImmutableArray<NormalizedPatchOperation> operations)
    {
        string? name = null;
        string? alias = null;
        string? notes = null;
        foreach (NormalizedPatchOperation operation in operations)
        {
            switch (operation.Path)
            {
                case DelegatedGmCharacterEditContract.ProfileNamePath:
                    name = operation.Value;
                    break;
                case DelegatedGmCharacterEditContract.ProfileAliasPath:
                    alias = operation.Value;
                    break;
                case DelegatedGmCharacterEditContract.ProfileNotesPath:
                    notes = operation.Value;
                    break;
            }
        }

        return new UpdateWorkspaceMetadata(name, alias, notes);
    }

    private static DelegatedGmCharacterEditAuditReceipt BuildAuditReceipt(
        NormalizedCommand command,
        CampaignGmCharacterEditAuthorization authorization,
        string idempotencyKeySha256,
        string commandSha256,
        long newRevision,
        DateTimeOffset nowUtc)
    {
        string receiptSeed = string.Join(
            "\n",
            commandSha256,
            authorization.DelegationId,
            authorization.AuthorityReceiptId,
            idempotencyKeySha256);
        string receiptId = "gm-edit-" + ComputeSha256(receiptSeed)[..24];
        ImmutableArray<DelegatedGmCharacterEditAuditOperation> operations = command.Operations
            .Select(static operation => new DelegatedGmCharacterEditAuditOperation(
                operation.Operation,
                operation.Path,
                ComputeSha256(operation.Value),
                operation.Value.Length))
            .ToImmutableArray();

        return new DelegatedGmCharacterEditAuditReceipt(
            DelegatedGmCharacterEditContract.Name,
            receiptId,
            command.CampaignId,
            authorization.DelegationId.Trim(),
            authorization.GrantedByCampaignOwnerId.Trim(),
            authorization.AuthorityReceiptId.Trim(),
            authorization.AuthorityRevision,
            command.ActorId,
            DelegatedGmCharacterEditContract.GameMasterRole,
            command.CharacterOwner.NormalizedValue,
            command.CharacterId,
            command.Reason,
            idempotencyKeySha256,
            commandSha256,
            command.ExpectedRevision,
            newRevision,
            nowUtc,
            operations);
    }

    private static DelegatedGmCharacterEditResult? MapLookupResult(
        DelegatedGmCharacterEditStoreResult result)
    {
        return result.Outcome switch
        {
            DelegatedGmCharacterEditStoreOutcome.NotFound => null,
            DelegatedGmCharacterEditStoreOutcome.Replayed when result.Receipt is not null =>
                new DelegatedGmCharacterEditResult(
                    DelegatedGmCharacterEditOutcome.Replayed,
                    result.Receipt),
            DelegatedGmCharacterEditStoreOutcome.WorkspaceMissing => Failure(
                DelegatedGmCharacterEditOutcome.Missing,
                "character_missing",
                "Character workspace was not found."),
            DelegatedGmCharacterEditStoreOutcome.IdempotencyConflict => Failure(
                DelegatedGmCharacterEditOutcome.Conflict,
                "idempotency_key_reused",
                "IdempotencyKey was already used for a different command."),
            DelegatedGmCharacterEditStoreOutcome.Corrupt => Failure(
                DelegatedGmCharacterEditOutcome.Corrupt,
                "audit_ledger_corrupt",
                "Character audit ledger is corrupt."),
            DelegatedGmCharacterEditStoreOutcome.Unavailable => Failure(
                DelegatedGmCharacterEditOutcome.Unavailable,
                "character_store_unavailable",
                "Character storage is unavailable."),
            _ => Failure(
                DelegatedGmCharacterEditOutcome.Unavailable,
                "invalid_store_lookup_result",
                "Character storage returned an invalid idempotency result.")
        };
    }

    private static DelegatedGmCharacterEditResult MapCommitResult(
        DelegatedGmCharacterEditStoreResult result)
    {
        return result.Outcome switch
        {
            DelegatedGmCharacterEditStoreOutcome.Applied when result.Receipt is not null =>
                new DelegatedGmCharacterEditResult(
                    DelegatedGmCharacterEditOutcome.Applied,
                    result.Receipt),
            DelegatedGmCharacterEditStoreOutcome.Replayed when result.Receipt is not null =>
                new DelegatedGmCharacterEditResult(
                    DelegatedGmCharacterEditOutcome.Replayed,
                    result.Receipt),
            DelegatedGmCharacterEditStoreOutcome.WorkspaceMissing => Failure(
                DelegatedGmCharacterEditOutcome.Missing,
                "character_missing",
                "Character workspace was not found."),
            DelegatedGmCharacterEditStoreOutcome.RevisionConflict => Failure(
                DelegatedGmCharacterEditOutcome.Conflict,
                "stale_revision",
                "Character changed since the expected revision."),
            DelegatedGmCharacterEditStoreOutcome.IdempotencyConflict => Failure(
                DelegatedGmCharacterEditOutcome.Conflict,
                "idempotency_key_reused",
                "IdempotencyKey was already used for a different command."),
            DelegatedGmCharacterEditStoreOutcome.Corrupt => Failure(
                DelegatedGmCharacterEditOutcome.Corrupt,
                "audit_ledger_corrupt",
                "Character audit ledger is corrupt."),
            _ => Failure(
                DelegatedGmCharacterEditOutcome.Unavailable,
                "character_store_unavailable",
                "Character storage is unavailable.")
        };
    }

    private static DelegatedGmCharacterEditResult MapWorkspaceReadFailure(
        WorkspaceStoreReadResult result)
    {
        return result.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => Failure(
                DelegatedGmCharacterEditOutcome.Missing,
                "character_missing",
                "Character workspace was not found."),
            WorkspaceOperationOutcome.Corrupt => Failure(
                DelegatedGmCharacterEditOutcome.Corrupt,
                "character_document_corrupt",
                "Character document is corrupt."),
            _ => Failure(
                DelegatedGmCharacterEditOutcome.Unavailable,
                "character_store_unavailable",
                "Character storage is unavailable.")
        };
    }

    private static string ComputeCommandSha256(NormalizedCommand command)
    {
        StringBuilder builder = new();
        AppendFingerprintField(builder, DelegatedGmCharacterEditContract.Name);
        AppendFingerprintField(builder, command.CampaignId);
        AppendFingerprintField(builder, command.ActorId);
        AppendFingerprintField(builder, command.CharacterOwner.NormalizedValue);
        AppendFingerprintField(builder, command.CharacterId.Value);
        AppendFingerprintField(builder, command.ExpectedRevision.ToString(CultureInfo.InvariantCulture));
        AppendFingerprintField(builder, command.Reason);
        foreach (NormalizedPatchOperation operation in command.Operations)
        {
            AppendFingerprintField(builder, ((int)operation.Operation).ToString(CultureInfo.InvariantCulture));
            AppendFingerprintField(builder, operation.Path);
            AppendFingerprintField(builder, operation.Value);
        }

        return ComputeSha256(builder.ToString());
    }

    private static void AppendFingerprintField(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('\n');
    }

    private static string ComputeSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private static bool IsBoundedIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string candidate = value.Trim();
        return candidate.Length <= MaximumIdentifierLength
            && !ContainsUnsupportedControlCharacter(candidate);
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().ToLowerInvariant();
    }

    private static bool IsForbiddenPath(string path)
    {
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(ForbiddenPathSegments.Contains);
    }

    private static int MaximumValueLength(string path)
    {
        return path switch
        {
            DelegatedGmCharacterEditContract.ProfileNamePath => MaximumNameLength,
            DelegatedGmCharacterEditContract.ProfileAliasPath => MaximumAliasLength,
            DelegatedGmCharacterEditContract.ProfileNotesPath => MaximumNotesLength,
            _ => 0
        };
    }

    private static bool ContainsUnsupportedControlCharacter(string value)
    {
        return value.Any(static character => char.IsControl(character)
            && character is not '\r' and not '\n' and not '\t');
    }

    private static DelegatedGmCharacterEditResult Failure(
        DelegatedGmCharacterEditOutcome outcome,
        string errorCode,
        string error)
    {
        return new DelegatedGmCharacterEditResult(
            outcome,
            ErrorCode: errorCode,
            Error: error);
    }

    private readonly record struct NormalizedPatchOperation(
        DelegatedGmCharacterPatchOperationKind Operation,
        string Path,
        string Value);

    private readonly record struct NormalizedCommand(
        string CampaignId,
        string ActorId,
        OwnerScope CharacterOwner,
        CharacterWorkspaceId CharacterId,
        long ExpectedRevision,
        string IdempotencyKey,
        string Reason,
        ImmutableArray<NormalizedPatchOperation> Operations);
}
