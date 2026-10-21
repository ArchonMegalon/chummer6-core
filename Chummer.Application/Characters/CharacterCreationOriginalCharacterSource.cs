using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// A disposable, independently validated original graph. Public digests and
/// portable snapshots cannot construct this execution input without replaying
/// the existing bootstrap, original-history and source-policy validators.
/// It carries no owner lease or save capability.
/// </summary>
internal sealed class CharacterCreationOriginalCharacterSource
{
    private readonly XDocument document;
    private readonly string inputsDigest;
    private readonly string observationDigest;
    private readonly string bootstrapDigest;
    private readonly string rawCharacterXmlDigest;

    private CharacterCreationOriginalCharacterSource(XDocument document, string inputsDigest,
        WorkspaceStoredDocument original, CharacterCreationCompletionBootstrapSnapshot bootstrap)
    {
        this.document = document;
        this.inputsDigest = inputsDigest;
        observationDigest = ObservationDigest(original);
        bootstrapDigest = bootstrap.SnapshotDigest;
        rawCharacterXmlDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
            original.Document.Content);
    }

    internal XDocument CopyDocument() => new(document);
    internal bool MatchesInputs(string digest) => inputsDigest == digest;
    internal bool MatchesObservation(WorkspaceStoredDocument original, CharacterCreationCompletionBootstrapSnapshot bootstrap)
        => observationDigest == ObservationDigest(original) && bootstrapDigest == bootstrap.SnapshotDigest;

    // Observe a privately validated original, never reinterpret the frozen
    // legality policy's null lifecycle. Both routes earn this source through
    // canonical original-history/source validators. Bind exact original bytes,
    // not a reserialized XML tree, to the assessment.
    internal string? CreationLifecycleDigestFor(CharacterCreationCompleteAssessment authority,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
            || authority.Phase != CharacterCreationCompletePhase.Authority
            || !MatchesInputs(authority.InputsSnapshotDigest)
            || rawCharacterXmlDigest != authority.RawCharacterXmlDigest)
            return null;
        if (!HasSingleCreationField(document)) return null;
        return CharacterCreationFinalizationDigest.Compute(new
        {
            inputsDigest, observationDigest, bootstrapDigest, rawCharacterXmlDigest, Created = false
        });
    }

    // Syntax predicate only; calling it cannot issue private execution evidence.
    internal static bool HasSingleCreationField(XDocument original)
    {
        if (original.Root?.Name != "character") return false;
        var fields = original.Root.Elements().Where(node => node.Name.LocalName == "created").ToArray();
        return fields.Length == 1 && fields[0].Name == "created"
            && !fields[0].HasAttributes && !fields[0].HasElements
            && bool.TryParse(fields[0].Value, out bool created) && !created;
    }

    internal static bool TryCapture(WorkspaceStoredDocument original, ICharacterSourceDataContext context,
        out CharacterCreationCompletionOriginalCharacter? captured, CancellationToken cancellationToken)
    {
        captured = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryParse(original.Document.Content, out _, cancellationToken)
            || !CharacterCreationCompletionBootstrapSnapshot.TryCapture(context, original.Document.Content, out var bootstrap))
            return false;
        captured = new(CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1,
            original.Id, original.ContentRevision, original.SavedRevision, original.Document.Content,
            original.Document.AuxiliaryState, JsonSerializer.SerializeToElement(bootstrap));
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    internal static bool TryFromFull(CharacterCreationCompletionReviewInputs inputs,
        out CharacterCreationOriginalCharacterSource? source, CancellationToken cancellationToken = default)
    {
        source = null;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (inputs.OriginalCharacter is not { } captured
                || captured.Schema != CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1
                || captured.AuxiliaryState is null || captured.BootstrapSourceSnapshot.ValueKind != JsonValueKind.Object
                || !TryParse(captured.RawCharacterXml, out var graph, cancellationToken)
                || inputs.Schema != CharacterCreationCompletionReviewSchemas.InputsV3
                || inputs.EffectivePolicy?.Consumers?.Schema != CharacterCreationCompletionConsumerPolicy.SchemaV5
                || inputs.EffectivePolicy.SettingObservations is null
                || !CharacterCreationFinalizationDigest.IsCanonical(inputs.SnapshotDigest)
                || inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)
                || !CharacterCreationCompletionReviewService.HasInputsShape(inputs)
                || !CharacterCreationCompletionReviewService.HasCoherentInputs(inputs)
                || !CharacterCreationEffectivePolicyValidation.IsValid(inputs.EffectivePolicy, inputs))
                return false;
            var document = new WorkspaceDocument(captured.RawCharacterXml, RulesetDefaults.Sr5);
            document = document with { State = document.State with { AuxiliaryState = captured.AuxiliaryState } };
            var original = new WorkspaceStoredDocument(captured.WorkspaceId, document, captured.ContentRevision,
                captured.SavedRevision, DateTimeOffset.UnixEpoch);
            var bootstrap = captured.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>();
            if (bootstrap is null || !bootstrap.IsValid()
                || !Same(captured.BootstrapSourceSnapshot, JsonSerializer.SerializeToElement(bootstrap))
                || !CharacterCreationCompletionFinalizationProjector.MatchesOriginal(original, bootstrap, inputs)
                || !inputs.EffectivePolicy.EnabledSourcebooks.SequenceEqual(
                    bootstrap.SourceProfile.EnabledSourcebooks.Order(StringComparer.Ordinal))
                || !CharacterCreationAttributePolicyAuthority.MatchesSourceHouseRules(
                    inputs.AttributePolicy, inputs.Skills.Attributes, bootstrap.Prerequisite)
                || !Same(CharacterContactEditSemanticsResolver.ReadEffects(graph!.Root!), inputs.Contacts.OriginalEffects))
                return false;
            source = new(graph!, inputs.SnapshotDigest, original, bootstrap);
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (Exception error) when (IsMalformed(error)) { return false; }
    }

    internal static bool TryFromSelected(WorkspaceStoredDocument original,
        CharacterCreationCompletionBootstrapSnapshot bootstrap, CharacterCreationSelectedCompletionWitness witness,
        CharacterCreationCompletionReviewRequest request, out CharacterCreationOriginalCharacterSource? source,
        CancellationToken cancellationToken = default)
    {
        source = null;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (request.OriginalCharacterExecutionSchema != CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1
                || !TryParse(original.Document.Content, out var graph, cancellationToken)
                || !CharacterCreationSelectedCompletionEvaluator.IsValidEnvelope(witness, request)
                || !CharacterCreationSelectedCompletionEvaluator.TryReadOriginal(original, bootstrap, witness,
                    out var baseline, out var effects)
                || baseline is null || effects is null
                || !CharacterCreationEffectivePolicyValidation.IsValidSelected(witness, baseline, effects)
                || CharacterCreationCompletionPreservationRules.Check(witness.Skills, baseline, request.Selections).Count != 0)
                return false;
            source = new(graph!, witness.ProvenanceInputsSnapshotDigest, original, bootstrap);
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (Exception error) when (IsMalformed(error)) { return false; }
    }

    // Reuse the completion archive's uncompressed input budget. Do this before
    // invoking older validators that parse XML themselves. No DTD or resolver,
    // including internal entity expansion; no XML is normalized in the contract.
    internal static bool HasBoundedXml(string? xml)
        => !string.IsNullOrEmpty(xml) && xml.Length <= CharacterCreationCompletionReplayCodec.MaximumUncompressedBytes
            && Encoding.UTF8.GetByteCount(xml) <= CharacterCreationCompletionReplayCodec.MaximumUncompressedBytes;

    internal static bool TryParse(string xml, out XDocument? document, CancellationToken cancellationToken = default)
    {
        document = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasBoundedXml(xml)) return false;
        try
        {
            using var text = new StringReader(xml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = CharacterCreationCompletionReplayCodec.MaximumUncompressedBytes
            });
            var parsed = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            cancellationToken.ThrowIfCancellationRequested();
            if (parsed.Root?.Name != "character") return false;
            document = parsed;
            return true;
        }
        catch (XmlException) { return false; }
    }

    private static string ObservationDigest(WorkspaceStoredDocument original)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            original.Id, original.ContentRevision, original.SavedRevision,
            RawCharacterXmlDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(original.Document.Content),
            original.Document.AuxiliaryStateDigest
        });

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);

    private static bool IsMalformed(Exception error)
        => error is XmlException or InvalidDataException or FormatException or OverflowException
            or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or NullReferenceException;
}
