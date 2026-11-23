using Chummer.Application.Characters;
using System.Security.Cryptography;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    // Do not inherit an unrelated source/tracing scope while issuing or projecting
    // owned semantics. Normal nested scopes inside Evaluate still work unchanged.
    internal static bool HasIsolatedOperationEnvironment3287 => ActiveSourceInputs.Value is null
        && _activeSynchronousOperation is null && ActiveCurrentCatalogTrace.Value is null
        && _compatibilityDiagnostics is null;

    internal static bool CanOwnOperationInputs3287(CharacterCreationRuleSourceCapture capture,
        ICharacterFileQueries queries) => capture is RuleSourceCapture
        && queries is XmlCharacterFileQueries { HasImmutableEvaluationQueries2764: true }
        && capture.SourcebookSelection is null
        && Chummer5aLegacyImportCompatibility.IsSupportedProfile(capture.CompatibilityProfile);

    // The runtime type check is private and non-substitutable. Reconstruct every
    // file, inventory, absent-file observation, selection and metadata collection
    // from owned bytes. Neither a caller interface nor a matching digest can issue.
    internal static void RetireOperationInputs3287(CharacterCreationRuleSourceCapture owned)
    {
        if (owned is not RuleSourceCapture capture) throw RetainedCharacterRuleSourceInputs.Rejected();
        capture.RetireOwnedOperationInputs3287();
    }

    private sealed partial class RuleSourceCapture
    {
        internal void RetireOwnedOperationInputs3287()
        {
            CryptographicOperations.ZeroMemory(_privateInputs);
            _inputs.RetireOwnedOperationBytes3287();
            _requiredRulePacks = null; _requiredAssets = null; _orderedCustomSources = null;
        }
    }

    internal static CharacterCreationRuleSourceCapture OwnOperationInputs3287(
        CharacterCreationRuleSourceCapture capture, ICharacterFileQueries queries, byte[] archive)
    {
        if (!CanOwnOperationInputs3287(capture, queries) || !HasIsolatedOperationEnvironment3287
            || !archive.AsSpan().SequenceEqual(capture.ExportPrivateInputs()))
            throw RetainedCharacterRuleSourceInputs.Rejected();
        var inputs = RetainedCharacterRuleSourceInputs.Import(archive, capture.IntegrityDigest);
        RuleSourceCapture? owned = null;
        try
        {
            if (!VerifyRetainedPolicy(inputs)) throw RetainedCharacterRuleSourceInputs.Rejected();
            owned = new RuleSourceCapture(inputs);
            if (!owned.OwnsEvaluationInputs2764(inputs)
                || !archive.AsSpan().SequenceEqual(owned.ExportPrivateInputs()))
                throw RetainedCharacterRuleSourceInputs.Rejected();
            return owned;
        }
        catch
        {
            if (owned is not null) owned.RetireOwnedOperationInputs3287();
            else inputs.RetireOwnedOperationBytes3287();
            throw;
        }
    }
}

// Only called on the new private import created above, never a caller's capture.
internal sealed partial class RetainedCharacterRuleSourceInputs
{
    internal void RetireOwnedOperationBytes3287()
    {
        foreach (var bytes in _files.Values) CryptographicOperations.ZeroMemory(bytes);
        _files.Clear(); _existence.Clear(); _inventories.Clear();
        Array.Clear(_selection);
    }
}
