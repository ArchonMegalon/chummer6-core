using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// An invocation can be minted only by the original-aware Application compiler.
// No record-with clone, public report or numeric result can construct one.
public sealed class CharacterCreationEssenceInvocation1222
{
    internal CharacterCreationEssenceInvocation1222(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, XElement graph, string capturePolicyDigest)
    {
        var original = inputs.OriginalCharacter ?? throw new InvalidDataException("Missing original.");
        if (request.EssenceResultSchema != CharacterCreationCompletionReviewSchemas.EssenceResultV1
            || !CharacterCreationCompletionReviewRules.HasValidEssenceResult(request)
            || !CharacterCreationFinalizationDigest.IsCanonical(capturePolicyDigest)
            || request.EffectivePolicyDigest != inputs.EffectivePolicy?.PolicyDigest)
            throw new InvalidDataException("Mismatched Essence policy binding.");
        CapturePolicyDigest = capturePolicyDigest;
        CaptureDigest = inputs.RuleSourceCaptureDigest!;
        PolicyDigest = inputs.EffectivePolicy!.PolicyDigest;
        ProfileId = inputs.EffectivePolicy.SettingsProfileId;
        ProfileDigest = inputs.EffectivePolicy.ProfileInputsDigest;
        ProfileXml = inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml;
        RequestDigest = CharacterCreationFinalizationDigest.Compute(request);
        OriginalXml = original.RawCharacterXml;
        OwnerRevisionDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            original.WorkspaceId, original.ContentRevision, original.SavedRevision,
            original.RawCharacterXml, original.AuxiliaryState, original.BootstrapSourceSnapshot
        });
        InputsDigest = inputs.SnapshotDigest;
        MetatypeId = inputs.Skills.PrerequisiteDraft.HeritageSelection!.MetatypeSourceId;
        MetavariantId = inputs.Skills.PrerequisiteDraft.HeritageSelection.MetavariantSourceId;
        GraphXml = graph.ToString(SaveOptions.DisableFormatting);
        GraphDigest = CharacterCreationFinalizationDigest.Compute(GraphXml);
        // Attribute-enable rows are zero-valued native talent operations, not
        // numeric modifiers. Only exact producer-attested occurrences qualify;
        // an XML copy carries no such custody and receives no exemption.
        if (!CharacterCreationAwakenedLegacyProjector.TryReadPhysicalTalent1279(graph, out var talentRows))
            throw new InvalidDataException("Invalid physical talent provenance.");
        PhysicalTalentAttributeEnableRows1291 = Array.AsReadOnly(
            (graph.Element("improvements")?.Elements().ToArray() ?? [])
                .Select((row, index) => (row, index))
                .Where(item => talentRows.Any(row => ReferenceEquals(row, item.row))
                    && item.row.Element("improvementttype")?.Value == "Attribute")
                .Select(item => item.index).ToArray());
        if (!CharacterCreationOwnedGearGraph1233.TryReadNativeStackOwners1351(graph, inputs, request, out var owners))
            throw new InvalidDataException("Invalid native stacked-focus producer custody.");
        nativeOwners1351.Add(this, owners);
    }
    private static readonly ConditionalWeakTable<CharacterCreationEssenceInvocation1222, Guid[]> nativeOwners1351 = new();
    private sealed record InterpreterGraph1351(CharacterCreationEssenceInvocation1222 Invocation,
        XElement[] Nodes, XElement[] Owners);
    private static readonly ConditionalWeakTable<XElement, InterpreterGraph1351> interpreterGraphs1351 = new();

    // Infrastructure receives an exact graph copy from this privately constructed
    // invocation. No public DTO, copied invocation or caller-supplied XML can mint
    // an owner exemption. The source resolver still replays every component source.
    public XElement CreateInterpreterGraph1351()
    {
        if (!nativeOwners1351.TryGetValue(this, out var owners))
            throw new InvalidDataException("Missing native graph producer custody.");
        var graph = XElement.Parse(GraphXml, LoadOptions.PreserveWhitespace);
        if (CharacterCreationFinalizationDigest.Compute(GraphXml) != GraphDigest)
            throw new InvalidDataException("Mismatched Essence graph.");
        var aggregates = owners.Select(id => graph.Element("gears")!.Elements("gear")
            .Single(g => g.Element("guid")?.Value == id.ToString("D"))).ToArray();
        interpreterGraphs1351.Add(graph, new(this, graph.DescendantsAndSelf().ToArray(), aggregates));
        return graph;
    }

    public bool MatchesInterpreterGraph1351(XElement graph)
        => nativeOwners1351.TryGetValue(this, out _)
            && interpreterGraphs1351.TryGetValue(graph, out var binding)
            && ReferenceEquals(binding.Invocation, this)
            && graph.DescendantsAndSelf().SequenceEqual(binding.Nodes, ReferenceEqualityComparer.Instance)
            && graph.ToString(SaveOptions.DisableFormatting) == GraphXml;

    public bool IsNativeStackOwner1351(XElement graph, XElement owner)
        => interpreterGraphs1351.TryGetValue(graph, out var binding)
            && binding.Owners.Any(node => ReferenceEquals(node, owner))
            && MatchesInterpreterGraph1351(graph);

    public string CaptureDigest { get; }
    // Raw inventory identity from the privately retained capture, distinct from
    // the completion consumer policy carried by PolicyDigest.
    public string CapturePolicyDigest { get; }
    public string PolicyDigest { get; }
    public string ProfileId { get; }
    public string ProfileDigest { get; }
    public string ProfileXml { get; }
    public string InputsDigest { get; }
    public string RequestDigest { get; }
    public string OriginalXml { get; }
    public string OwnerRevisionDigest { get; }
    public string MetatypeId { get; }
    public string? MetavariantId { get; }
    public string GraphXml { get; }
    public string GraphDigest { get; }
    public IReadOnlyList<int> PhysicalTalentAttributeEnableRows1291 { get; }
}

// Data only. The retained resolver must independently confirm this exact object
// under the same original invocation before Application can use it as execution.
public sealed record CharacterCreationEssenceExecution1222(string GraphXml, string GraphDigest,
    string SourceDigest, string ExecutionDigest, CharacterCreationEssenceRules1222.Result Result,
    string ProfileId, string ProfileDigest, string ProfileXmlDigest, string OwnerRevisionDigest,
    string Format, int Decimals, bool Unrounded, bool MaximumOnly, bool ShownValue, decimal Acquisition, string CapturePolicyDigest, string PolicyDigest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationEssenceSettingEvidence? SettingsEvidence { get; init; }
}

public abstract partial class CharacterCreationRuleSourceResolution
{
    public virtual CharacterCreationEssenceExecution1222? ExecuteEssence1222(
        CharacterCreationEssenceInvocation1222 invocation) => null;
    public virtual bool HasExecutedEssence1222(CharacterCreationEssenceInvocation1222 invocation,
        CharacterCreationEssenceExecution1222 execution) => false;
}
