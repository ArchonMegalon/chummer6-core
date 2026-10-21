using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// A source resolver receives this only after the private, authority-bound
// canonical consumers and compiler have both finished. It is not a receipt.
public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    private readonly IReadOnlyDictionary<string, string> consumers;

    internal CharacterCreationRuleSourceScopeInvocation(string policyDigest, string captureDigest,
        string requestDigest, string graphDigest, IReadOnlyDictionary<string, string> consumers,
        IReadOnlyList<CharacterCreationCompiledSourceEffect> compiledEffects)
    {
        PolicyDigest = policyDigest;
        CaptureDigest = captureDigest;
        RequestDigest = requestDigest;
        GraphDigest = graphDigest;
        this.consumers = new ReadOnlyDictionary<string, string>(
            consumers.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal));
        CompiledEffects = Array.AsReadOnly(compiledEffects.ToArray());
    }

    [JsonIgnore] public string PolicyDigest { get; }
    [JsonIgnore] public string CaptureDigest { get; }
    [JsonIgnore] public string RequestDigest { get; }
    [JsonIgnore] public string GraphDigest { get; }
    [JsonIgnore] public IReadOnlyList<CharacterCreationCompiledSourceEffect> CompiledEffects { get; }
    public bool TryGetCompletedConsumer(string scope, out string digest)
        => consumers.TryGetValue(scope, out digest!);
    public override string ToString() => nameof(CharacterCreationRuleSourceScopeInvocation);
}

public enum CharacterCreationSourceOperationDisposition
{
    Unconsumed,
    SelectorEvaluated,
    Compiled,
    NotSelected,
    Structural,
    LegacyNonOperative
}

// Diagnostics remain private to the evaluation. Paths identify source nodes,
// never filesystem locations; source text is retained only inside the capture.
public sealed record CharacterCreationSourceOperation(
    int Order, string Path, string SourceDigest,
    CharacterCreationSourceOperationDisposition Disposition, string? ExecutionDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLegacyIniClassification886? Classification886 { get; init; }
    [JsonIgnore] internal string? ClassificationSeal886 { get; init; }
    internal bool ValidClassification886 => Disposition == CharacterCreationSourceOperationDisposition.LegacyNonOperative
        && ExecutionDigest is null && ConsumedValue718 is null && OperationResult718 is null
        && Classification886 is not null && ClassificationSeal886 is not null
        && ClassificationSeal886 == CharacterCreationFinalizationDigest.Compute(new { Order, Path, SourceDigest, Classification886 });

    // Private diagnostics only. The existing public digest contract is unchanged.
    [JsonIgnore] public string? ConsumedValue718 { get; init; }
    [JsonIgnore] public string? OperationResult718 { get; init; }
}

public sealed record CharacterCreationRuleSourceScopeResult(
    string ObligationId, string Scope, string SourceDigest, string ConsumerDigest,
    IReadOnlyList<CharacterCreationSourceOperation> Operations)
{
    public bool IsComplete => Operations.Count != 0
        && Operations.Select(row => row.Order).SequenceEqual(Enumerable.Range(0, Operations.Count))
        && Operations.Select(row => row.Path).Distinct(StringComparer.Ordinal).Count() == Operations.Count
        && Operations.All(row => row.ValidClassification886 || (row.Classification886 is null
            && (row.Disposition is CharacterCreationSourceOperationDisposition.SelectorEvaluated
                or CharacterCreationSourceOperationDisposition.Compiled
                or CharacterCreationSourceOperationDisposition.NotSelected
                or CharacterCreationSourceOperationDisposition.Structural)
            && CharacterCreationFinalizationDigest.IsCanonical(row.SourceDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(row.ExecutionDigest)));
}
