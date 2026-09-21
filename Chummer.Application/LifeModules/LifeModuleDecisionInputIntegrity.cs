using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Application.Characters;

namespace Chummer.Application.LifeModules;

internal static class LifeModuleDecisionInputIntegrity
{
    internal static bool TryNormalize(IReadOnlyDictionary<string, string>? values,
        out IReadOnlyDictionary<string, string> normalized)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        normalized = result;
        if (values is null || values.Count > 128)
            return false;
        foreach (var item in values)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 256
                || item.Value is null || item.Value.Length > 1024
                || !result.TryAdd(item.Key.Trim(), item.Value.Trim()))
                return false;
        }
        return true;
    }

    internal static bool ValidForms(IReadOnlyList<LifeModuleFollowUpPromptDto>? prompts)
        => prompts is null || prompts.Count is > 0 and <= 128
           && prompts.All(prompt => prompt is not null
               && !string.IsNullOrWhiteSpace(prompt.PromptId) && prompt.PromptId.Length <= 256
               && !string.IsNullOrWhiteSpace(prompt.Label)
               && prompt.InputKind is "text" or "single-select"
               && prompt.SourceAnchorIds is { Count: > 0 }
               && prompt.Options is not null && prompt.Options.Count <= 4096
               && prompt.Options.All(option => option is not null
                   && !string.IsNullOrWhiteSpace(option.OptionId)
                   && !string.IsNullOrWhiteSpace(option.Label)
                   && !string.IsNullOrWhiteSpace(option.SourceValue)))
           && prompts.Select(prompt => prompt.PromptId).Distinct(StringComparer.Ordinal).Count() == prompts.Count;

    internal static LifeModuleDecisionInputResolution Seal(LifeModuleDecisionInputResolution value)
    {
        if (!TryNormalize(value.Values, out var normalized))
            throw new InvalidOperationException("Invalid Life Module input values.");
        var result = value with
        {
            Values = normalized,
            MechanicsPreview = LifeModuleOriginDossierService.SealPreview(value.MechanicsPreview),
            ResolutionDigest = string.Empty
        };
        return result with { ResolutionDigest = Digest(result) };
    }

    internal static bool Matches(LifeModuleDecisionInputResolution? value,
        string workspaceId, long revision, string choiceId, string decisionDigest, string commandDigest)
        => value is not null && value.WorkspaceId == workspaceId && value.WorkspaceRevision == revision
           && value.ChoiceId == choiceId && value.DecisionDigest == decisionDigest
           && value.DecisionCommandDigest == commandDigest
           && LifeModuleDecisionAcceptanceIntegrity.IsDigest(value.ResolutionDigest)
           && CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(value.ResolvedPreviewDigest)
           && value.MechanicsPreview is { Items: not null, SourceAnchorIds.Count: > 0, PendingFollowUpIds.Count: 0 }
           && TryNormalize(value.Values, out var normalized)
           && value.Values.SequenceEqual(normalized)
           && Seal(value).ResolutionDigest == value.ResolutionDigest;

    internal static string Digest<T>(T value)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
}
