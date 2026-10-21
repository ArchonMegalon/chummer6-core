using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCreationCompositeStageAtomicCommitCapability
{
    public bool SupportsCompositeStageAtomicCommit => true;

    public WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationMagicResonancePreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)) return InvalidOwnerMutation();
        try
        {
            preview = JsonSerializer.SerializeToElement(preview).Deserialize<CharacterCreationMagicResonancePreview>()!;
            replacement = replacement with { State = replacement.State with
            { AuxiliaryState = JsonSerializer.SerializeToElement(replacement.AuxiliaryState)
                .Deserialize<WorkspaceDocumentAuxiliaryState>()! } };
            return ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpointCore(owner, preview.Binding.WorkspaceId,
                preview.Binding.ContentRevision, preview.Binding.AuxiliaryStateDigest, replacement, current =>
                {
                    if (!CharacterCreationCompositeStageCommitAdmission.IsCurrent(current, replacement, preview, resolver, queries))
                        throw new IOException("Composite MagicResonance authority changed before commit.");
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or JsonException or System.Xml.XmlException or OverflowException or FormatException)
        {
            return UnavailableMutation("Composite stage authority is unavailable.");
        }
    }

    public WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationQualitiesPreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)) return InvalidOwnerMutation();
        try
        {
            preview = JsonSerializer.SerializeToElement(preview).Deserialize<CharacterCreationQualitiesPreview>()!;
            replacement = replacement with { State = replacement.State with
            { AuxiliaryState = JsonSerializer.SerializeToElement(replacement.AuxiliaryState)
                .Deserialize<WorkspaceDocumentAuxiliaryState>()! } };
            return ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpointCore(owner, preview.Binding.WorkspaceId,
                preview.Binding.ContentRevision, preview.Binding.AuxiliaryStateDigest, replacement, current =>
                {
                    if (!CharacterCreationCompositeStageCommitAdmission.IsCurrent(current, replacement, preview, resolver, queries))
                        throw new IOException("Composite Qualities authority changed before commit.");
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or JsonException or System.Xml.XmlException or OverflowException or FormatException)
        {
            return UnavailableMutation("Composite stage authority is unavailable.");
        }
    }

    public WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationResourcesPreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)) return InvalidOwnerMutation();
        try
        {
            preview = JsonSerializer.SerializeToElement(preview).Deserialize<CharacterCreationResourcesPreview>()!;
            replacement = replacement with { State = replacement.State with
            { AuxiliaryState = JsonSerializer.SerializeToElement(replacement.AuxiliaryState)
                .Deserialize<WorkspaceDocumentAuxiliaryState>()! } };
            return ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpointCore(owner, preview.Binding.WorkspaceId,
                preview.Binding.ContentRevision, preview.Binding.AuxiliaryStateDigest, replacement, current =>
                {
                    if (!CharacterCreationCompositeStageCommitAdmission.IsCurrent(current, replacement, preview, resolver, queries))
                        throw new IOException("Composite Resources authority changed before commit.");
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or JsonException or System.Xml.XmlException or OverflowException or FormatException)
        {
            return UnavailableMutation("Composite stage authority is unavailable.");
        }
    }

    public WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationGearPreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)) return InvalidOwnerMutation();
        try
        {
            preview = JsonSerializer.SerializeToElement(preview).Deserialize<CharacterCreationGearPreview>()!;
            replacement = replacement with { State = replacement.State with
            { AuxiliaryState = JsonSerializer.SerializeToElement(replacement.AuxiliaryState)
                .Deserialize<WorkspaceDocumentAuxiliaryState>()! } };
            return ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpointCore(owner, preview.Binding.WorkspaceId,
                preview.Binding.ContentRevision, preview.Binding.AuxiliaryStateDigest, replacement, current =>
                {
                    if (!CharacterCreationCompositeStageCommitAdmission.IsCurrent(current, replacement, preview, resolver, queries))
                        throw new IOException("Composite Gear authority changed before commit.");
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or JsonException or System.Xml.XmlException or OverflowException or FormatException)
        {
            return UnavailableMutation("Composite stage authority is unavailable.");
        }
    }

    public WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationContactPreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)) return InvalidOwnerMutation();
        try
        {
            preview = JsonSerializer.SerializeToElement(preview).Deserialize<CharacterCreationContactPreview>()!;
            replacement = replacement with { State = replacement.State with
            { AuxiliaryState = JsonSerializer.SerializeToElement(replacement.AuxiliaryState)
                .Deserialize<WorkspaceDocumentAuxiliaryState>()! } };
            return ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpointCore(owner,
                preview.Binding.WorkspaceId, preview.Binding.ContentRevision, preview.Binding.AuxiliaryStateDigest,
                replacement, current =>
                {
                    if (!CharacterCreationCompositeStageCommitAdmission.IsCurrent(
                            current, replacement, preview, this, resolver, queries))
                        throw new IOException("Composite Contact authority changed before commit.");
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or JsonException or System.Xml.XmlException or OverflowException or FormatException)
        {
            return UnavailableMutation("Composite stage authority is unavailable.");
        }
    }

    public WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationLifestylePreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)) return InvalidOwnerMutation();
        try
        {
            preview = JsonSerializer.SerializeToElement(preview).Deserialize<CharacterCreationLifestylePreview>()!;
            replacement = replacement with { State = replacement.State with
            { AuxiliaryState = JsonSerializer.SerializeToElement(replacement.AuxiliaryState)
                .Deserialize<WorkspaceDocumentAuxiliaryState>()! } };
            return ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpointCore(owner,
                preview.Binding.WorkspaceId, preview.Binding.ContentRevision, preview.Binding.AuxiliaryStateDigest,
                replacement, current =>
                {
                    if (!CharacterCreationCompositeStageCommitAdmission.IsCurrent(
                            current, replacement, preview, this, resolver, queries))
                        throw new IOException("Composite Lifestyle authority changed before commit.");
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or JsonException or System.Xml.XmlException or OverflowException or FormatException)
        {
            return UnavailableMutation("Composite stage authority is unavailable.");
        }
    }

}
