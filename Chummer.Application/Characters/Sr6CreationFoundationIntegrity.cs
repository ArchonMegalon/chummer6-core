using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using System.Text;

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
            || selection.Assignments is null || selection.Assignments.Count != (selection.PointBuy is null ? 5 : 0)) return false;
        var assignments = selection.Assignments.ToArray();
        if (selection.PointBuy is { } pointBuy)
        {
            if (assignments.Length != 0 || !ValidPointBuyShape(pointBuy)) return false;
        }
        else if (assignments.Length != 5 || assignments.Any(item => item is null
                || !CharacterCreationPriorityCategoryIds.Ordered.Contains(item.CategoryId, StringComparer.Ordinal)
                || item.Rank is not ("A" or "B" or "C" or "D" or "E"))
            || assignments.Select(item => item.CategoryId).Distinct(StringComparer.Ordinal).Count() != 5)
            return false;
        Sr6CreationAttributeSelection? attributes = null;
        if (selection.Attributes is not null && !TryFreezeAttributes(selection.Attributes, out attributes)) return false;
        Sr6CreationSkillSelection? skills = null;
        if (selection.Skills is not null && !TryFreezeSkills(selection.Skills, out skills)) return false;
        Sr6CreationKnowledgeSelection? knowledge = null;
        if (selection.Knowledge is not null && !TryFreezeKnowledge(selection.Knowledge, out knowledge)) return false;
        frozen = selection with { Assignments = selection.PointBuy is not null ? [] : CharacterCreationPriorityCategoryIds.Ordered
            .Select(category => assignments.Single(item => item.CategoryId == category)).ToArray(),
            Attributes = attributes, Skills = skills, Knowledge = knowledge };
        return true;
    }

    public static bool ValidPointBuyShape(Sr6CreationPointBuySelection? selection)
        => selection is { AdditionalAttributePoints: >= 0 and <= 1000, AdditionalSkillPoints: >= 0 and <= 1000,
            AdditionalAdjustmentPoints: >= 0 and <= 1000, ResourceUnits: >= 0 and <= 1000 };

    public static bool TryFreezeKnowledge(Sr6CreationKnowledgeSelection? selection, out Sr6CreationKnowledgeSelection? frozen)
    {
        frozen = null;
        if (selection is null || !KnowledgeName(selection.NativeLanguage)
            || selection.KnowledgeSkills is not { Count: <= 32 } || selection.Languages is not { Count: <= 32 }) return false;
        var topics = selection.KnowledgeSkills.ToArray();
        var languages = selection.Languages.ToArray();
        if (topics.Length + languages.Length > 32
            || topics.Any(row => row is null || row.Id == Guid.Empty || !KnowledgeName(row.Name))
            || languages.Any(row => row is null || row.Id == Guid.Empty || !KnowledgeName(row.Name)
                || !Sr6CreationLanguageLevels.Ordered.Contains(row.Level, StringComparer.Ordinal))
            || topics.Select(row => row.Id).Concat(languages.Select(row => row.Id)).Distinct().Count() != topics.Length + languages.Length
            || topics.Select(row => row.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != topics.Length
            || languages.Select(row => row.Name).Append(selection.NativeLanguage).Distinct(StringComparer.OrdinalIgnoreCase).Count() != languages.Length + 1)
            return false;
        frozen = new(selection.NativeLanguage, topics.OrderBy(row => row.Id).ToArray(), languages.OrderBy(row => row.Id).ToArray());
        return true;
    }

    private static bool KnowledgeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name != name.Trim() || name.Any(char.IsControl)) return false;
        try { return name.IsNormalized(NormalizationForm.FormC); }
        catch (ArgumentException) { return false; }
    }

    public static bool TryFreezeSkills(Sr6CreationSkillSelection? selection, out Sr6CreationSkillSelection? frozen)
    {
        frozen = null;
        if (selection?.Allocations is not { Count: <= 19 }
            || selection.AspectedSkillId is not (null or "Sorcery" or "Conjuring" or "Enchanting")) return false;
        var rows = selection.Allocations.ToArray();
        if (rows.Length > 19 || rows.Any(row => row is null
            || !Sr6CreationSkillIds.Ordered.Contains(row.SkillId, StringComparer.Ordinal)
            || row.Rating is < 0 or > 6 || row.Specializations is not { Count: <= 12 })
            || rows.Select(row => row.SkillId).Distinct(StringComparer.Ordinal).Count() != rows.Length) return false;
        var copies = new List<Sr6CreationSkillSpend>();
        foreach (var row in rows)
        {
            string[] names = row.Specializations.ToArray();
            if (names.Length > 12 || names.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 80
                    || name != name.Trim() || name.Any(char.IsControl))
                || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) return false;
            copies.Add(row with { Specializations = names.Order(StringComparer.Ordinal).ToArray() });
        }
        frozen = new(copies.OrderBy(row => Array.IndexOf(Sr6CreationSkillIds.Ordered.ToArray(), row.SkillId)).ToArray(), selection.AspectedSkillId);
        return true;
    }

    public static bool TryFreezeAttributes(Sr6CreationAttributeSelection? selection, out Sr6CreationAttributeSelection? frozen)
    {
        frozen = null;
        if (selection?.Allocations is not { Count: 11 }) return false;
        var rows = selection.Allocations.ToArray();
        if (rows.Length != 11 || rows.Any(row => row is null
                || !Sr6CreationAttributeIds.Ordered.Contains(row.AttributeId, StringComparer.Ordinal)
                || row.AttributePoints is < 0 or > 24 || row.AdjustmentPoints is < 0 or > 13)
            || rows.Select(row => row.AttributeId).Distinct(StringComparer.Ordinal).Count() != 11) return false;
        frozen = new(Sr6CreationAttributeIds.Ordered.Select(id => rows.Single(row => row.AttributeId == id)).ToArray());
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
            || bootstrap.BuildMethod is not (Sr6CharacterCreationBuildMethods.Priority or Sr6CharacterCreationBuildMethods.SumToTen or Sr6CharacterCreationBuildMethods.PointBuy)
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
                || (bootstrap.BuildMethod == Sr6CharacterCreationBuildMethods.PointBuy
                    ? command.Selection.PointBuy is null || preview.PointBuy is null || preview.Budget.MagicResonanceRank is not null
                    : command.Selection.PointBuy is not null || preview.PointBuy is not null || preview.Budget.MagicResonanceRank is not ("A" or "B" or "C" or "D" or "E"))
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
