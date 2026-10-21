using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Recovery from the store's admitted private manifest, valid only during its workspace lease.
/// A digest selects an already retained input; it is neither admission nor mutation authority.
/// Historical evaluators must still check the recovered context against the original character.
/// </summary>
public interface ICharacterCreationRuleSourceRecovery
{
    bool TryRecoverRuleSources(string captureDigest, out CharacterCreationRuleSourceExecutionContext? execution);

    /// <summary>Re-read every referenced artifact, including on a cached workspace-read path.</summary>
    bool ValidateRetainedRuleSources();

    // Exact private committed history, or live admission while constructing a
    // receipt. Public archive DTOs alone never supply either capability.
    bool TryRecoverCampaignAuthority(CharacterCreationCampaignBinding binding, string receiptDigest,
        out CharacterCreationCampaignExecutionContext? execution)
    { execution = null; return false; }
}

// Used only after live admission, while constructing and checking the new receipt.
// This supplies replay inputs; it does not publish bytes or grant store/owner authority.
internal sealed class AdmittedCharacterCreationRuleSources(
    string captureDigest, CharacterCreationRuleSourceExecutionContext execution,
    ICharacterCreationRuleSourceRecovery? history) : ICharacterCreationRuleSourceRecovery
{
    public bool ValidateRetainedRuleSources() => history?.ValidateRetainedRuleSources() ?? true;

    public bool TryRecoverCampaignAuthority(CharacterCreationCampaignBinding binding, string receiptDigest,
        out CharacterCreationCampaignExecutionContext? result)
    {
        result = null;
        return history?.TryRecoverCampaignAuthority(binding, receiptDigest, out result) ?? false;
    }

    public bool TryRecoverRuleSources(string digest, out CharacterCreationRuleSourceExecutionContext? result)
    {
        result = null;
        if (!ValidateRetainedRuleSources()) return false;
        if (digest == captureDigest) { result = execution; return true; }
        return history?.TryRecoverRuleSources(digest, out result) ?? false;
    }
}

/// <summary>
/// Private local workspace persistence metadata only. Never include in public proofs, provider
/// requests or portable exports. Deserialization alone does not authorize recovery or execution.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CharacterCreationRetainedRuleSourceReference(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string OwnerBinding,
    [property: JsonRequired] string WorkspaceId,
    [property: JsonRequired] string IncarnationId,
    [property: JsonRequired] string CaptureDigest,
    [property: JsonRequired] long ByteLength)
{
    public override string ToString() => nameof(CharacterCreationRetainedRuleSourceReference);
}
