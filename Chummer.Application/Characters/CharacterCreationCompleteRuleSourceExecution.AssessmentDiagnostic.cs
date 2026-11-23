using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Two bounded diagnostic snapshots per private source execution. Resolve creates this
    // execution for the current request; it is never a wire or retained capability.
    // Actual source consumption, campaign observations and Apply still execute.
    private readonly int assessmentThread = Environment.CurrentManagedThreadId;
    private string? assessmentKey;
    private byte[]? assessmentJson;
    private string? authorityDiagnosticKey;
    private byte[]? authorityDiagnosticJson;
    private const int MaximumAssessmentBytes = 1024 * 1024;

    internal CharacterCreationCompleteAssessment FullAuthorityDiagnostic(
        CharacterCreationCompletionReviewInputs currentInputs, CharacterCreationCompletionReviewRequest currentRequest,
        Func<CharacterCreationCompleteAssessment> evaluate)
        => ReuseDiagnostic(new { Inputs = currentInputs, Request = currentRequest }, evaluate,
            ref assessmentKey, ref assessmentJson);

    // Full and selected routes independently reconstruct their policy projection.
    // Reuse only the pure diagnostic of the exact arguments they then supply.
    // This retains neither a witness admission nor executed consumer coverage.
    internal CharacterCreationCompleteAssessment AuthorityDiagnostic(object arguments,
        Func<CharacterCreationCompleteAssessment> evaluate)
        => ReuseDiagnostic(arguments, evaluate, ref authorityDiagnosticKey, ref authorityDiagnosticJson);

    private CharacterCreationCompleteAssessment ReuseDiagnostic(object arguments,
        Func<CharacterCreationCompleteAssessment> evaluate, ref string? retainedKey, ref byte[]? retainedJson)
    {
        if (Environment.CurrentManagedThreadId != assessmentThread) return evaluate();
        // Hash the actual complete graphs on every access, including the private
        // execution's inputs/request. Caller-supplied snapshot digests alone
        // cannot retain a diagnostic after any selection, source or policy edit.
        string Key() => CharacterCreationFinalizationDigest.Compute(new
        {
            Arguments = CharacterCreationFinalizationDigest.Compute(arguments),
            ExecutionInputs = CharacterCreationFinalizationDigest.Compute(Inputs),
            ExecutionRequest = CharacterCreationFinalizationDigest.Compute(request),
            HasNativeFocusSources1339
        });
        string key = Key();
        if (retainedKey == key && retainedJson is { } retained)
        {
            // No mutable obligation list is shared with a caller or consumer.
            var restored = JsonSerializer.Deserialize<CharacterCreationCompleteAssessment>(retained)!;
            if (Key() == key) return restored;
            retainedKey = null; retainedJson = null;
            return evaluate();
        }
        var result = evaluate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result);
        if (bytes.Length <= MaximumAssessmentBytes && Key() == key)
        { retainedKey = key; retainedJson = bytes; }
        else { retainedKey = null; retainedJson = null; }
        return result;
    }
}
