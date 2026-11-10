using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ISr6CareerAttributeAdvancementService
{
    Sr6CareerAttributeResult Quote(OwnerContextStamp owner, CharacterWorkspaceId workspaceId, string attributeId);
    Sr6CareerAttributeResult Advance(OwnerContextStamp owner, Sr6CareerAttributeCommand command);
    Sr6CareerAttributeResult Lookup(OwnerContextStamp owner, Sr6CareerAttributeCommand command);
}

/// <summary>Uses the store's existing owner/path lease and atomic checkpoint replacement.</summary>
public interface ISr6CareerAttributeAtomicCommitCapability
{
    string Sr6CareerAttributeRuntimeDigest { get; }
    Sr6CareerAttributeResult CommitSr6CareerAttribute(OwnerScope owner, Sr6CareerAttributeCommand command);
}
