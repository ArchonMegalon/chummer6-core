using System.Globalization;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed record CharacterCreationLegacyIniClassification886(
    string Schema, string Semantics, string Reason, string ObligationId, string Field, int Value,
    string SelectedNodeDigest, string AuthorityDigest, string PolicyDigest, string CaptureDigest,
    string RequestDigest, string GraphDigest);

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    [JsonIgnore] public CharacterCreationPhysicalInitiativeAuthority? PhysicalAuthority886 { get; internal init; }
    [JsonIgnore] public CharacterPhysicalInitiativeProjection? PhysicalInitiative886 { get; internal init; }

    private static string OccurrencePath886(XObject node)
    {
        if (node is XElement element)
            return element.Parent is null ? "/chummer[0]" : OccurrencePath886(element.Parent) + "/" + element.Name + "["
                + element.Parent.Nodes().TakeWhile(n => !ReferenceEquals(n, element)).Count() + "]";
        if (node is XText text && text.Parent is { } parent)
            return OccurrencePath886(parent) + "/text()[" + parent.Nodes().TakeWhile(n => !ReferenceEquals(n, text)).Count() + "]";
        return string.Empty;
    }

    private static int OccurrenceOrder886(XElement root, XObject target)
    {
        int order = 0, found = -1;
        void Walk(XElement node, int depth)
        {
            if (depth > 128) throw new InvalidDataException("Rule-source classification structure bound exceeded.");
            if (ReferenceEquals(node, target)) found = order;
            order++;
            foreach (var attribute in node.Attributes())
            {
                if (ReferenceEquals(attribute, target)) found = order;
                order++;
            }
            foreach (var child in node.Nodes())
            {
                if (child is XElement element) Walk(element, depth + 1);
                else if (child is XText text && !string.IsNullOrWhiteSpace(text.Value) || child is XProcessingInstruction)
                {
                    if (ReferenceEquals(child, target)) found = order;
                    order++;
                }
            }
        }
        Walk(root, 0);
        return found;
    }

    // This capability exists only on an invocation built after canonical compilation.
    // Each scalar and its actual text occurrences are checked; attributes/children/PI
    // cannot borrow a cap classification. A copied/resealed DTO cannot mint the seal.
    public CharacterCreationSourceOperation? ClassifyLegacyIni886(string obligation, XElement selected,
        XElement scalar, XObject occurrence, CharacterCreationSourceOperation operation)
    {
        if (PhysicalAuthority886 is not { } authority || PhysicalInitiative886 is not { } result
            || CreationDraft718 is not { } draft || !CharacterCreationPhysicalInitiative886.Matches(authority, draft)
            || result.AuthorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            || selected.Name != "metatype" || scalar.Parent != selected
            || selected.HasAttributes || scalar.HasAttributes || scalar.HasElements
            || scalar.Nodes().OfType<XProcessingInstruction>().Any()
            || selected.Elements(scalar.Name).Count() != 1
            || operation.Disposition != CharacterCreationSourceOperationDisposition.Unconsumed
            || operation.ExecutionDigest is not null || obligation != authority.MetatypeObligationId
            || operation.Path != OccurrencePath886(occurrence)
            || operation.Order != OccurrenceOrder886(selected.Document?.Root ?? selected.AncestorsAndSelf().Last(), occurrence)) return null;
        string selectedDigest = CharacterCreationFinalizationDigest.ComputeUtf8(selected.ToString(SaveOptions.DisableFormatting));
        if (selectedDigest != authority.MetatypeSourceNodeDigest) return null;
        int? expected = scalar.Name.ToString() switch
        {
            "inimin" => authority.Metatype.Minimum,
            "inimax" => authority.Metatype.Maximum,
            "iniaug" => authority.Metatype.AugmentedMaximum,
            _ => null
        };
        if (expected is null || !int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            || value != expected || !(ReferenceEquals(occurrence, scalar)
                || occurrence is XText text && text.Parent == scalar && !string.IsNullOrWhiteSpace(text.Value))) return null;
        string source = occurrence is XElement element ? element.ToString(SaveOptions.DisableFormatting) : ((XText)occurrence).Value;
        if (operation.SourceDigest != CharacterCreationFinalizationDigest.Compute(source)) return null;
        var evidence = new CharacterCreationLegacyIniClassification886(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4,
            authority.Semantics, "selected-metatype-ini-limit-no-runtime-consumer", obligation, scalar.Name.ToString(), value,
            selectedDigest, CharacterCreationFinalizationDigest.Compute(authority), PolicyDigest, CaptureDigest, RequestDigest, GraphDigest);
        var classified = operation with { Disposition = CharacterCreationSourceOperationDisposition.LegacyNonOperative,
            Classification886 = evidence };
        return classified with { ClassificationSeal886 = CharacterCreationFinalizationDigest.Compute(new
            { classified.Order, classified.Path, classified.SourceDigest, classified.Classification886 }) };
    }
}
