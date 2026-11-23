using System.Text.Json.Serialization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Process-local read request issued by the canonical compiler/finalizer.
/// It grants only source reads for one exact candidate, never a source capture,
/// original-state admission, rule-coverage assertion or permission to save.</summary>
public sealed class CharacterCreationCompiledGraphRead
{
    private readonly CharacterCreationRuleSourceResolution owner;
    private readonly string captureDigest;
    private readonly string originalXml;
    private readonly string requestDigest;

    internal CharacterCreationCompiledGraphRead(CharacterCreationRuleSourceResolution owner,
        string captureDigest, string originalXml, string candidateXml, string requestDigest)
    {
        this.owner = owner;
        this.captureDigest = captureDigest;
        this.originalXml = originalXml;
        this.requestDigest = requestDigest;
        CharacterXml = candidateXml;
    }

    [JsonIgnore]
    public string CharacterXml { get; }

    public bool IsIssuedFor(CharacterCreationRuleSourceResolution resolution, string capture, string original)
        => ReferenceEquals(owner, resolution) && captureDigest == capture && originalXml == original
            && CharacterCreationFinalizationDigest.IsCanonical(requestDigest);

    public override string ToString() => nameof(CharacterCreationCompiledGraphRead);
}

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Only private compiler/finalizer call sites supply this graph. Public plans,
    // digests and review DTOs cannot construct the process-local read request.
    private ICharacterSourceDataResolver? CompiledGraphResolver(XElement root)
    {
        if (Inputs.OriginalCharacter is not { } saved
            || Inputs.RuleSourceCaptureDigest is not { } capture
            || capture != request.RuleSourceCaptureDigest
            || Inputs.SnapshotDigest != request.InputsSnapshotDigest
            || request.LifecycleResultSchema != CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            || !original.MatchesInputs(Inputs.SnapshotDigest)) return null;
        return resolution.CreateCompiledGraphRead(new(resolution, capture, saved.RawCharacterXml,
            root.ToString(SaveOptions.DisableFormatting), requestDigest));
    }
}
