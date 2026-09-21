using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Stored shape and identity checks only. Edition rules are re-evaluated by SR6.</summary>
public static class Sr6CreationFoundationIntegrity
{
    public const int MaximumDecisions = 128;
    public static string Digest<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    public static string PreviewDigest(Sr6CreationFoundationPreview preview) => Digest(preview with { PreviewDigest = string.Empty });
    public static string DecisionDigest(Sr6CreationFoundationDecision decision) => Digest(decision with { DecisionDigest = string.Empty });

    public static bool TryFreezeSelection(Sr6CreationFoundationSelection? selection, out Sr6CreationFoundationSelection frozen)
    {
        frozen = null!;
        if (selection is null || selection.MetatypeId is not ("human" or "elf" or "dwarf" or "ork" or "troll")
            || selection.TalentId is not ("mundane" or "magician" or "aspected-magician" or "adept" or "mystic-adept" or "technomancer")
            || selection.Assignments is not { Count: 5 }) return false;
        var assignments = selection.Assignments.ToArray();
        if (assignments.Length != 5 || assignments.Any(item => item is null
                || !CharacterCreationPriorityCategoryIds.Ordered.Contains(item.CategoryId, StringComparer.Ordinal)
                || item.Rank is not ("A" or "B" or "C" or "D" or "E"))
            || assignments.Select(item => item.CategoryId).Distinct(StringComparer.Ordinal).Count() != 5)
            return false;
        frozen = selection with { Assignments = CharacterCreationPriorityCategoryIds.Ordered
            .Select(category => assignments.Single(item => item.CategoryId == category)).ToArray() };
        return true;
    }

    public static bool TryFreezeRequest(Sr6CreationFoundationConfirmRequest? request, out Sr6CreationFoundationConfirmRequest frozen)
    {
        frozen = null!;
        if (request is not { ExplicitlyConfirmed: true } || request.OperationId == Guid.Empty
            || !ValidBinding(request.Binding) || !Hash(request.PreviewDigest)
            || !TryFreezeSelection(request.Selection, out var selection)) return false;
        frozen = request with { Selection = selection };
        return true;
    }

    public static bool ValidBinding(Sr6CreationFoundationBinding? binding)
        => binding is not null && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
           && binding.ContentRevision is > 0 and < long.MaxValue
           && binding.SavedRevision >= 0 && binding.SavedRevision <= binding.ContentRevision
           && binding.AuxiliaryStateDigest is { Length: 64 }
           && Hash("sha256:" + binding.AuxiliaryStateDigest)
           && Hash(binding.BootstrapBindingDigest) && Hash(binding.AuthorityDigest);

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision, WorkspaceDocumentAuxiliaryState state)
    {
        var ledger = state.Sr6CreationFoundationDecisions;
        if (ledger is null) return true;
        var bootstrap = state.CharacterCreationBootstrapBinding;
        if (ledger.Count is < 1 or > MaximumDecisions || bootstrap is not { RulesetId: RulesetDefaults.Sr6 }
            || bootstrap.BuildMethod is not (Sr6CharacterCreationBuildMethods.Priority or Sr6CharacterCreationBuildMethods.SumToTen)
            || !CharacterCreationBootstrapBindingDigest.IsValid(bootstrap)) return false;
        var seen = new HashSet<Guid>();
        long previousRevision = 0;
        for (int index = 0; index < ledger.Count; index++)
        {
            var decision = ledger[index];
            if (decision is null || decision.Schema != Sr6CreationFoundationDecision.SchemaV1
                || !TryFreezeRequest(decision.Command, out var command)
                || !seen.Add(command.OperationId) || command.Binding.WorkspaceId != id
                || command.Binding.BootstrapBindingDigest != bootstrap.BindingDigest
                || command.Binding.ContentRevision < previousRevision
                || decision.CommittedContentRevision != command.Binding.ContentRevision + 1
                || decision.CommittedContentRevision > revision
                || decision.Preview is not { } preview || preview.Binding != command.Binding
                || !TryFreezeSelection(preview.Selection, out _)
                || Digest(preview.Selection) != Digest(command.Selection)
                || preview.Budget is not { AttributePoints: >= 0, SkillPoints: >= 0, ResourcesNuyen: >= 0, MetatypeAdjustmentPoints: >= 0 }
                || preview.Budget.MagicResonanceRank is not ("A" or "B" or "C" or "D" or "E")
                || preview.BaseMagic is < 0 or > 6 || preview.BaseResonance is < 0 or > 6
                || preview.SourceAnchorIds is not { Count: > 0 and <= 8 }
                || preview.SourceAnchorIds.Any(string.IsNullOrWhiteSpace)
                || !Hash(preview.PreviewDigest) || preview.PreviewDigest != command.PreviewDigest
                || preview.PreviewDigest != PreviewDigest(preview)
                || !Hash(decision.DecisionDigest) || decision.DecisionDigest != DecisionDigest(decision)) return false;
            var priorState = state with { Sr6CreationFoundationDecisions = index == 0 ? null : ledger.Take(index).ToArray() };
            if (command.Binding.AuxiliaryStateDigest != WorkspaceDocumentAuxiliaryStateDigest.Compute(priorState)) return false;
            previousRevision = decision.CommittedContentRevision;
        }
        return true;
    }

    private static bool Hash(string? digest) => CharacterCreationBootstrapBindingDigest.IsCanonical(digest);
}
