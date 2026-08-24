using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCreationBootstrapService : ICharacterCreationBootstrapService
{
    private const int MaximumDisplayIdentityLength = 256;
    private const string ChummerVersion = "5.225.0";

    private readonly IWorkspaceStore _workspaceStore;
    private readonly IRulesetWorkspaceCodecResolver _codecResolver;
    private readonly ICharacterFileQueries _characterFileQueries;
    private readonly ICharacterSourceDataResolver _sourceDataResolver;

    public CharacterCreationBootstrapService(
        IWorkspaceStore workspaceStore,
        IRulesetWorkspaceCodecResolver codecResolver,
        ICharacterFileQueries characterFileQueries,
        ICharacterSourceDataResolver sourceDataResolver)
    {
        _workspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
        _codecResolver = codecResolver ?? throw new ArgumentNullException(nameof(codecResolver));
        _characterFileQueries = characterFileQueries
                                ?? throw new ArgumentNullException(nameof(characterFileQueries));
        _sourceDataResolver = sourceDataResolver
                              ?? throw new ArgumentNullException(nameof(sourceDataResolver));
    }

    public CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> Create(
        CharacterCreationBootstrapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string[] requestBlockers = ValidateRequest(request);
        if (requestBlockers.Length != 0)
            return Invalid<CharacterCreationBootstrapReceipt>(requestBlockers);

        string characterXml = BuildCharacterXml(request);
        IRulesetWorkspaceCodec codec;
        WorkspacePayloadEnvelope envelope;
        try
        {
            codec = _codecResolver.Resolve(request.RulesetId);
            if (!string.Equals(codec.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal))
            {
                return Invalid<CharacterCreationBootstrapReceipt>(
                    CharacterCreationBootstrapBlockers.RulesetSr5Required);
            }

            envelope = codec.WrapImport(
                request.RulesetId,
                new WorkspaceImportDocument(
                    characterXml,
                    request.RulesetId,
                    WorkspaceDocumentFormat.NativeXml));
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or InvalidDataException
                                           or InvalidOperationException)
        {
            return Invalid<CharacterCreationBootstrapReceipt>(
                CharacterCreationBootstrapBlockers.CharacterDocumentInvalid);
        }

        var characterDocument = new CharacterDocument(characterXml);
        CharacterValidationResult genericValidation;
        CharacterFileSummary summary;
        try
        {
            genericValidation = _characterFileQueries.Validate(characterDocument);
            summary = _characterFileQueries.ParseSummary(characterDocument);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or FormatException
                                           or InvalidDataException
                                           or InvalidOperationException)
        {
            return Invalid<CharacterCreationBootstrapReceipt>(
                CharacterCreationBootstrapBlockers.CharacterDocumentInvalid);
        }

        CharacterValidationIssue[] genericErrors = genericValidation.Issues
            .Where(issue => string.Equals(issue.Severity, "Error", StringComparison.Ordinal))
            .ToArray();
        bool hasOnlyExpectedMissingMetatype = !genericValidation.IsValid
                                              && genericErrors.Length == 1
                                              && string.Equals(
                                                  genericErrors[0].Code,
                                                  "MissingRequiredNode",
                                                  StringComparison.Ordinal)
                                              && string.Equals(
                                                  genericErrors[0].Path,
                                                  "/character/metatype",
                                                  StringComparison.Ordinal);
        if (!hasOnlyExpectedMissingMetatype
            || !string.IsNullOrEmpty(summary.Metatype)
            || summary.Created
            || !string.Equals(summary.BuildMethod, request.BuildMethod, StringComparison.Ordinal))
        {
            return Invalid<CharacterCreationBootstrapReceipt>(
                CharacterCreationBootstrapBlockers.CharacterDocumentInvalid);
        }

        CharacterWorkspaceId workspaceId = new(Guid.NewGuid().ToString("N"));
        WorkspaceDocument document = new(envelope, WorkspaceDocumentFormat.NativeXml);
        if (!CharacterCreationBootstrapAuthority.TryPrepareBinding(
                workspaceId,
                document,
                _sourceDataResolver,
                out CharacterCreationBootstrapBinding binding,
                out IReadOnlyList<string> sourceAnchorIds,
                out IReadOnlyList<string> authorityBlockers))
        {
            return Invalid<CharacterCreationBootstrapReceipt>(authorityBlockers);
        }

        WorkspaceDocument boundDocument = document with
        {
            State = document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: binding)
            }
        };
        if (_workspaceStore is not ICharacterCreationBootstrapAtomicCreateCapability capability
            || !capability.SupportsCharacterCreationBootstrapAtomicCreate)
        {
            return Unavailable<CharacterCreationBootstrapReceipt>(
                CharacterCreationBootstrapBlockers.AtomicCreateUnavailable);
        }

        WorkspaceStoreMutationResult created = capability
            .CreateCharacterCreationBootstrapWorkspaceDocument(workspaceId, boundDocument);
        if (!created.Success || created.Entry is not WorkspaceStoreEntry entry)
        {
            string outcome = created.Outcome == WorkspaceOperationOutcome.Conflict
                ? CharacterCreationBootstrapOutcomes.Conflict
                : CharacterCreationBootstrapOutcomes.Unavailable;
            return new CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt>(
                outcome,
                null,
                [CharacterCreationBootstrapBlockers.WorkspaceCreateFailed]);
        }

        var unsignedReceipt = new CharacterCreationBootstrapReceipt(
            CharacterCreationBootstrapSchemas.ReceiptV1,
            workspaceId,
            entry.ContentRevision,
            entry.SavedRevision,
            summary,
            binding,
            sourceAnchorIds.ToArray(),
            string.Empty);
        CharacterCreationBootstrapReceipt receipt = unsignedReceipt with
        {
            ReceiptDigest = CharacterCreationBootstrapReceiptDigest.Compute(unsignedReceipt)
        };
        if (!CharacterCreationBootstrapReceiptDigest.IsValid(receipt))
        {
            return Unavailable<CharacterCreationBootstrapReceipt>(
                CharacterCreationBootstrapBlockers.WorkspaceCreateFailed);
        }

        return new CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt>(
            CharacterCreationBootstrapOutcomes.Success,
            receipt,
            []);
    }

    private static string[] ValidateRequest(CharacterCreationBootstrapRequest request)
    {
        var blockers = new List<string>();
        if (!string.Equals(
                request.Schema,
                CharacterCreationBootstrapSchemas.RequestV1,
                StringComparison.Ordinal))
            blockers.Add(CharacterCreationBootstrapBlockers.RequestSchemaInvalid);
        if (!string.Equals(
                request.Stage,
                CharacterCreationBootstrapStages.AwaitingFoundationSelection,
                StringComparison.Ordinal))
            blockers.Add(CharacterCreationBootstrapBlockers.RequestStageInvalid);
        if (!string.Equals(request.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal))
            blockers.Add(CharacterCreationBootstrapBlockers.RulesetSr5Required);
        if (string.IsNullOrWhiteSpace(request.Name)
            || request.Name.Trim().Length > MaximumDisplayIdentityLength
            || string.IsNullOrWhiteSpace(request.Alias)
            || request.Alias.Trim().Length > MaximumDisplayIdentityLength)
            blockers.Add(CharacterCreationBootstrapBlockers.DisplayIdentityRequired);
        if (!CharacterCreationBuildMethods.IsSupported(request.BuildMethod))
            blockers.Add(CharacterCreationBootstrapBlockers.BuildMethodInvalid);
        if (!Guid.TryParseExact(request.SettingsProfileId, "D", out Guid settingsId)
            || settingsId == Guid.Empty
            || !string.Equals(
                settingsId.ToString("D"),
                request.SettingsProfileId,
                StringComparison.Ordinal))
            blockers.Add(CharacterCreationBootstrapBlockers.SettingsProfileInvalid);
        if (!CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(
                request.BuildMethod,
                request.SettingsProfileId))
            blockers.Add(CharacterCreationBootstrapBlockers.SettingsProfileInvalid);
        return blockers.Distinct(StringComparer.Ordinal)
            .OrderBy(blocker => blocker, StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildCharacterXml(CharacterCreationBootstrapRequest request)
    {
        XDocument document = new(
            new XElement(
                "character",
                new XElement("name", request.Name.Trim()),
                new XElement("alias", request.Alias.Trim()),
                new XElement("buildmethod", request.BuildMethod),
                new XElement("createdversion", ChummerVersion),
                new XElement("appversion", ChummerVersion),
                new XElement("karma", "0"),
                new XElement("nuyen", "0"),
                new XElement("created", "False"),
                new XElement("gameedition", "SR5"),
                new XElement("settings", request.SettingsProfileId),
                new XElement(
                    CharacterCreationBootstrapXml.MarkerElement,
                    new XElement(
                        CharacterCreationBootstrapXml.SchemaElement,
                        CharacterCreationBootstrapSchemas.MarkerV1),
                    new XElement(
                        CharacterCreationBootstrapXml.StageElement,
                        CharacterCreationBootstrapStages.AwaitingFoundationSelection))));
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }

    private static CharacterCreationBootstrapResult<T> Invalid<T>(params string[] blockers)
        where T : class
        => new(CharacterCreationBootstrapOutcomes.Invalid, null, blockers);

    private static CharacterCreationBootstrapResult<T> Invalid<T>(IReadOnlyList<string> blockers)
        where T : class
        => new(CharacterCreationBootstrapOutcomes.Invalid, null, blockers);

    private static CharacterCreationBootstrapResult<T> Unavailable<T>(params string[] blockers)
        where T : class
        => new(CharacterCreationBootstrapOutcomes.Unavailable, null, blockers);
}
