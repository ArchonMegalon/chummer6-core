using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCreationLifestyleAuthorityReviewCommitCapability
{
    public bool SupportsLifestyleAuthorityReviewCommit => true;

    public WorkspaceStoreMutationResult CommitLifestyleAuthorityReview(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationLifestylePreview preview, ICharacterSourceDataResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(resolver);
        // Freeze public mutable collections before any validation or disk work.
        JsonElement auxiliary = JsonSerializer.SerializeToElement(replacement.AuxiliaryState);
        var detached = auxiliary.Deserialize<WorkspaceDocumentAuxiliaryState>()!;
        if (!JsonElement.DeepEquals(auxiliary, JsonSerializer.SerializeToElement(detached)))
            return UnavailableMutation("Lifestyle authority review cannot be copied losslessly.");
        replacement = replacement with { State = replacement.State with { AuxiliaryState = detached } };
        preview = JsonSerializer.Deserialize<CharacterCreationLifestylePreview>(JsonSerializer.Serialize(preview))!;
        if (preview.WritePlan.AuthorityTransition is null)
            return UnavailableMutation("Lifestyle authority review is required.");
        var evaluator = new CharacterCreationLifestylesService(this, resolver);
        return ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpointCore(owner, preview.Binding.WorkspaceId,
            preview.Binding.ContentRevision, preview.Binding.AuxiliaryStateDigest, replacement,
            admitCurrentSources: current =>
            {
                if (!evaluator.AdmitsAuthorityReviewCommit(current, preview, replacement))
                    throw new IOException("Lifestyle authority review sources or binding changed.");
            }, admitsLifestyleAuthorityReview: true);
    }
}
