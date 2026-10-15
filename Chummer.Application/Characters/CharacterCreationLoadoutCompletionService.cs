using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Bounded typed loadout observation and pure pricing. No store writes,
/// purchase grants, synthetic draft history, or caller-supplied price overrides.</summary>
public static class CharacterCreationLoadoutCompletionService
{
    internal static CharacterCreationFoundationResult<CharacterCreationLoadoutCompletionInputs> LoadFromObservation(
        WorkspaceStoredDocument workspace, ICharacterSourceDataResolver resolver, bool requireAuthority = false)
    {
        try
        {
            XElement? root = XDocument.Parse(workspace.Document.Content, LoadOptions.None).Root;
            if (root?.Name != "character") return RejectedInputs(CharacterCreationLoadoutCompletionSchemas.RequestInvalid);
            // Existing imported/purchased graphs are not safely representable by
            // this acquisition tranche. Never replace or silently omit their cost.
            foreach (string name in new[] { "weapons", "armors" })
            {
                XElement[] containers = root.Elements(name).ToArray();
                if (containers.Length > 1 || containers.Any(value => value.HasElements
                        || value.HasAttributes || !string.IsNullOrWhiteSpace(value.Value)))
                    return RejectedInputs(CharacterCreationLoadoutCompletionSchemas.ExistingGraphUnsupported);
            }
            var context = resolver.TryCreateContext(workspace.Document.Content);
            if (context is null || !context.TryResolveCreationLoadoutAuthority(out var authority)
                || !CharacterCreationLoadoutRules.IsValidAuthority(authority))
                return requireAuthority ? RejectedInputs(CharacterCreationLoadoutCompletionSchemas.AuthorityUnavailable)
                    : new(CharacterCreationFoundationOutcomes.Success, null, []);
            var binding = new CharacterCreationLoadoutBinding(workspace.Id, workspace.ContentRevision,
                workspace.SavedRevision, CharacterCreationFinalizationDigest.ComputeUtf8(workspace.Document.Content),
                workspace.Document.AuxiliaryStateDigest);
            var inputs = new CharacterCreationLoadoutCompletionInputs(CharacterCreationLoadoutCompletionSchemas.InputsV1,
                binding, authority, CharacterCreationLoadoutSelections.Empty, string.Empty);
            inputs = inputs with { SnapshotDigest = CharacterCreationLoadoutCompletionRules.ComputeInputsDigest(inputs) };
            if (!IsValidInputs(inputs) || !context.TryResolveCreationLoadoutAuthority(out var after)
                || after.AuthorityDigest != authority.AuthorityDigest)
                return RejectedInputs(CharacterCreationLoadoutCompletionSchemas.SnapshotConflict);
            return new(CharacterCreationFoundationOutcomes.Success, Detach(inputs), []);
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or ArgumentException or OverflowException)
        { return RejectedInputs(CharacterCreationLoadoutCompletionSchemas.AuthorityUnavailable); }
    }

    public static CharacterCreationFoundationResult<CharacterCreationLoadoutCompletionProjection> Preview(
        CharacterCreationLoadoutCompletionInputs inputs, CharacterCreationLoadoutSelections selections)
    {
        if (!IsValidInputs(inputs) || !IsValidSelections(selections))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationLoadoutCompletionSchemas.RequestInvalid]);
        inputs = Detach(inputs);
        selections = Detach(selections);
        if (!CharacterCreationLoadoutRules.TryProjectBasket(selections, inputs.Authority,
                out var lines, out decimal cost, out var blockers))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, blockers);
        return new(CharacterCreationFoundationOutcomes.Success, BuildProjection(selections, lines, cost), []);
    }

    internal static CharacterCreationLoadoutCompletionProjection BuildProjection(
        CharacterCreationLoadoutSelections selections, IReadOnlyList<CharacterCreationLoadoutLine> lines, decimal cost)
    {
        var projection = new CharacterCreationLoadoutCompletionProjection(selections, lines, cost, [], string.Empty);
        return projection with { ProjectionDigest = CharacterCreationLoadoutCompletionRules.ComputeProjectionDigest(projection) };
    }

    internal static bool IsValidInputs(CharacterCreationLoadoutCompletionInputs? inputs)
        => inputs is { Schema: CharacterCreationLoadoutCompletionSchemas.InputsV1, Binding: { } binding,
                Authority: { } authority, OriginalSelections: { Weapons.Count: 0, Armor.Count: 0 } }
            && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
            && binding.ContentRevision > 0 && binding.SavedRevision == binding.ContentRevision
            && CharacterCreationFinalizationDigest.IsCanonical(binding.RawCharacterXmlDigest)
            && CharacterCareerReputationTransaction.IsDigest(binding.AuxiliaryStateDigest)
            && CharacterCreationLoadoutRules.IsValidAuthority(authority)
            && inputs.SnapshotDigest == CharacterCreationLoadoutCompletionRules.ComputeInputsDigest(inputs);

    internal static bool IsValidSelections(CharacterCreationLoadoutSelections? selections)
        => selections is { Weapons: not null, Armor: not null }
            && selections.Weapons.Count <= 128 && selections.Armor.Count <= 128
            && selections.Weapons.All(row => row is not null && !string.IsNullOrWhiteSpace(row.OptionId)
                && row.Quantity is > 0 and <= 20)
            && selections.Armor.All(row => row is not null && !string.IsNullOrWhiteSpace(row.OptionId)
                && row.Quantity is > 0 and <= 20)
            && selections.Weapons.Select(row => row.OptionId).Distinct(StringComparer.Ordinal).Count() == selections.Weapons.Count
            && selections.Armor.Select(row => row.OptionId).Distinct(StringComparer.Ordinal).Count() == selections.Armor.Count;

    private static CharacterCreationFoundationResult<CharacterCreationLoadoutCompletionInputs> RejectedInputs(string blocker)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);

    private static T Detach<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
