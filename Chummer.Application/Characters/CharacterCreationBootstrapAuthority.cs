using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Rebinds a typed pending-selection document to the currently installed source
/// corpus.  The XML marker alone is intentionally insufficient: persisted binding,
/// raw document digest, settings profile, and all applicable source authorities must
/// agree on every load.
/// </summary>
public static class CharacterCreationBootstrapAuthority
{
    private static readonly string[] s_PreselectedElements =
    [
        "prioritymetatype",
        "priorityattributes",
        "priorityspecial",
        "priorityskills",
        "priorityresources",
        "prioritytalent",
        "sumtoten",
        "lifemodule",
        "lifemodules"
    ];

    public static bool HasBootstrapState(WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.AuxiliaryState.CharacterCreationBootstrapBinding is not null)
            return true;

        try
        {
            XDocument xml = XDocument.Parse(document.Content, LoadOptions.None);
            return xml.Root?.DescendantsAndSelf().Any(element => string.Equals(
                element.Name.LocalName,
                CharacterCreationBootstrapXml.MarkerElement,
                StringComparison.Ordinal)) == true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryPrepareBinding(
        CharacterWorkspaceId workspaceId,
        WorkspaceDocument document,
        ICharacterSourceDataResolver sourceDataResolver,
        out CharacterCreationBootstrapBinding binding,
        out IReadOnlyList<string> sourceAnchorIds,
        out IReadOnlyList<string> blockers)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sourceDataResolver);

        var failures = new List<string>();
        if (!TryValidateDocumentShape(document, out BootstrapDocumentShape? shape, failures)
            || shape is null)
        {
            binding = EmptyBinding(workspaceId);
            sourceAnchorIds = [];
            blockers = Normalize(failures);
            return false;
        }

        ICharacterSourceDataContext? context;
        try
        {
            context = sourceDataResolver.TryCreateContext(document.Content);
        }
        catch (Exception exception) when (IsAuthorityReadFailure(exception))
        {
            failures.Add(CharacterCreationBootstrapBlockers.SourceContextUnavailable);
            binding = EmptyBinding(workspaceId);
            sourceAnchorIds = [];
            blockers = Normalize(failures);
            return false;
        }
        if (context is null)
        {
            failures.Add(CharacterCreationBootstrapBlockers.SourceContextUnavailable);
            binding = EmptyBinding(workspaceId);
            sourceAnchorIds = [];
            blockers = Normalize(failures);
            return false;
        }

        CharacterCreationSourceProfileAuthority sourceProfile =
            CharacterCreationSourceProfileAuthority.Unavailable;
        CharacterCreationMetatypeCatalogAuthority metatypeAuthority =
            CharacterCreationMetatypeCatalogAuthority.Unavailable;
        CharacterCreationPrerequisiteAuthority prerequisiteAuthority =
            CharacterCreationPrerequisiteAuthority.Unavailable;
        bool requiresPrerequisiteAuthority = shape.BuildMethod is
            CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen;
        bool sourceProfileResolved;
        bool metatypeResolved;
        bool prerequisiteResolved = false;
        try
        {
            sourceProfileResolved = context.TryResolveCreationSourceProfile(out sourceProfile);
            metatypeResolved = context.TryResolveCreationMetatypeCatalog(out metatypeAuthority);
            if (requiresPrerequisiteAuthority)
            {
                prerequisiteResolved = context.TryResolveCreationPrerequisiteAuthority(
                    out prerequisiteAuthority);
            }
        }
        catch (Exception exception) when (IsAuthorityReadFailure(exception))
        {
            failures.Add(CharacterCreationBootstrapBlockers.SourceContextUnavailable);
            binding = EmptyBinding(workspaceId);
            sourceAnchorIds = [];
            blockers = Normalize(failures);
            return false;
        }
        sourceProfile ??= CharacterCreationSourceProfileAuthority.Unavailable;
        metatypeAuthority ??= CharacterCreationMetatypeCatalogAuthority.Unavailable;
        prerequisiteAuthority ??= CharacterCreationPrerequisiteAuthority.Unavailable;

        string settingsAnchor = $"settings.xml#setting:{shape.SettingsProfileId}";
        bool profileValid = sourceProfileResolved
                            && string.Equals(
                                sourceProfile.SettingsProfileId,
                                shape.SettingsProfileId,
                                StringComparison.Ordinal)
                            && string.Equals(
                                sourceProfile.BuildMethod,
                                shape.BuildMethod,
                                StringComparison.Ordinal)
                            && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                                sourceProfile.RawProfileInputsDigest)
                            && sourceProfile.SourceAnchorIds is { Count: 1 }
                            && string.Equals(
                                sourceProfile.SourceAnchorIds[0],
                                settingsAnchor,
                                StringComparison.Ordinal)
                            && sourceProfile.EnabledSourcebooks is { Count: > 0 };
        if (!profileValid)
            failures.Add(CharacterCreationBootstrapBlockers.SourceProfileInvalid);

        bool metatypeValid = metatypeResolved
                             && metatypeAuthority.IsAuthoritative
                             && metatypeAuthority.Blockers is { Count: 0 }
                             && string.Equals(
                                 metatypeAuthority.Schema,
                                 CharacterCreationMetatypeCatalogSchemas.CatalogV1,
                                 StringComparison.Ordinal)
                             && metatypeAuthority.Options is { Count: > 0 }
                             && metatypeAuthority.SourceContext is not null
                             && metatypeAuthority.SourceContext.IsAuthoritative
                             && metatypeAuthority.SourceContext.Blockers is { Count: 0 }
                             && string.Equals(
                                 metatypeAuthority.SourceContext.SettingsProfileId,
                                 shape.SettingsProfileId,
                                 StringComparison.Ordinal)
                             && string.Equals(
                                 metatypeAuthority.SourceContext.RawProfileInputsDigest,
                                 sourceProfile.RawProfileInputsDigest,
                                 StringComparison.Ordinal)
                             && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                                 metatypeAuthority.SourceContext.RawMetatypesXmlDigest)
                             && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                                 metatypeAuthority.SourceContext.EffectiveMetatypesInputsDigest)
                             && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                                 metatypeAuthority.SourceContext.SelectedCustomDataInputsDigest)
                             && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                                 metatypeAuthority.SourceContext.AuthorityDigest)
                             && metatypeAuthority.SourceContext.SourceAnchorIds is not null
                             && metatypeAuthority.SourceContext.SourceAnchorIds.Count(anchor =>
                                 string.Equals(anchor, settingsAnchor, StringComparison.Ordinal)) == 1;
        if (!metatypeValid)
        {
            failures.Add(CharacterCreationBootstrapBlockers.MetatypeAuthorityUnavailable);
            failures.AddRange(metatypeAuthority.Blockers ?? []);
            failures.AddRange(metatypeAuthority.SourceContext?.Blockers ?? []);
        }

        string prerequisiteAuthorityDigest = string.Empty;
        bool prerequisiteValid = !requiresPrerequisiteAuthority
                                 || prerequisiteResolved
                                 && prerequisiteAuthority.IsAuthoritative
                                 && prerequisiteAuthority.Blockers is { Count: 0 }
                                 && prerequisiteAuthority.Options is { Count: > 0 }
                                 && string.Equals(
                                     prerequisiteAuthority.Schema,
                                     CharacterCreationPrerequisiteSchemas.AuthorityV1,
                                     StringComparison.Ordinal)
                                 && string.Equals(
                                     prerequisiteAuthority.SettingsProfileId,
                                     shape.SettingsProfileId,
                                     StringComparison.Ordinal)
                                 && string.Equals(
                                     prerequisiteAuthority.BuildMethod,
                                     shape.BuildMethod,
                                     StringComparison.Ordinal)
                                 && string.Equals(
                                     prerequisiteAuthority.RawProfileInputsDigest,
                                     sourceProfile.RawProfileInputsDigest,
                                     StringComparison.Ordinal)
                                 && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                                     prerequisiteAuthority.AuthorityDigest)
                                 && CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                                     prerequisiteAuthority.AuthorityDigest,
                                     CharacterCreationPrerequisiteAuthorityDigest.Compute(
                                         prerequisiteAuthority))
                                 && prerequisiteAuthority.SourceAnchorIds is not null
                                 && prerequisiteAuthority.SourceAnchorIds.Count(anchor =>
                                     string.Equals(anchor, settingsAnchor, StringComparison.Ordinal)) == 1;
        if (!prerequisiteValid)
        {
            failures.Add(CharacterCreationBootstrapBlockers.PrerequisiteAuthorityUnavailable);
            failures.AddRange(prerequisiteAuthority.Blockers ?? []);
        }
        else if (requiresPrerequisiteAuthority)
            prerequisiteAuthorityDigest = prerequisiteAuthority.AuthorityDigest;

        if (failures.Count != 0)
        {
            binding = EmptyBinding(workspaceId);
            sourceAnchorIds = [];
            blockers = Normalize(failures);
            return false;
        }

        string[] anchors = sourceProfile.SourceAnchorIds!
            .Concat(metatypeAuthority.SourceContext!.SourceAnchorIds!)
            .Concat(requiresPrerequisiteAuthority ? prerequisiteAuthority.SourceAnchorIds! : [])
            .Where(anchor => !string.IsNullOrWhiteSpace(anchor))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(anchor => anchor, StringComparer.Ordinal)
            .ToArray();
        var unsigned = new CharacterCreationBootstrapBinding(
            Schema: CharacterCreationBootstrapSchemas.BindingV1,
            Stage: CharacterCreationBootstrapStages.AwaitingFoundationSelection,
            WorkspaceId: workspaceId,
            RulesetId: RulesetDefaults.Sr5,
            BuildMethod: shape.BuildMethod,
            SettingsProfileId: shape.SettingsProfileId,
            RawCharacterXmlDigest: CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeRawCharacterXmlDigest(document.Content),
            RawProfileInputsDigest: sourceProfile.RawProfileInputsDigest,
            MetatypeAuthorityDigest: metatypeAuthority.SourceContext.AuthorityDigest,
            PrerequisiteAuthorityDigest: prerequisiteAuthorityDigest,
            SettingsSourceAnchor: settingsAnchor,
            BindingDigest: string.Empty);
        binding = unsigned with
        {
            BindingDigest = CharacterCreationBootstrapBindingDigest.Compute(unsigned)
        };
        sourceAnchorIds = anchors;
        blockers = [];
        return true;
    }

    public static bool TryValidatePending(
        WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver sourceDataResolver,
        out IReadOnlyList<string> blockers)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        CharacterCreationBootstrapBinding? persisted = workspace.Document.AuxiliaryState
            .CharacterCreationBootstrapBinding;
        if (persisted is null)
        {
            blockers = [CharacterCreationBootstrapBlockers.BindingMissing];
            return false;
        }

        if (!CharacterCreationBootstrapBindingDigest.IsValid(persisted)
            || persisted.WorkspaceId != workspace.Id
            || !string.Equals(
                persisted.Schema,
                CharacterCreationBootstrapSchemas.BindingV1,
                StringComparison.Ordinal)
            || !string.Equals(
                persisted.Stage,
                CharacterCreationBootstrapStages.AwaitingFoundationSelection,
                StringComparison.Ordinal))
        {
            blockers = [CharacterCreationBootstrapBlockers.BindingInvalid];
            return false;
        }

        if (!TryPrepareBinding(
                workspace.Id,
                workspace.Document,
                sourceDataResolver,
                out CharacterCreationBootstrapBinding current,
                out _,
                out IReadOnlyList<string> preparationBlockers))
        {
            blockers = preparationBlockers;
            return false;
        }

        if (!CharacterCreationBootstrapBindingDigest.FixedTimeEquals(
                persisted.BindingDigest,
                current.BindingDigest))
        {
            blockers = [CharacterCreationBootstrapBlockers.BindingStale];
            return false;
        }

        blockers = [];
        return true;
    }

    private static bool TryValidateDocumentShape(
        WorkspaceDocument document,
        out BootstrapDocumentShape? shape,
        ICollection<string> blockers)
    {
        shape = null;
        if (!string.Equals(document.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal))
        {
            blockers.Add(CharacterCreationBootstrapBlockers.RulesetSr5Required);
            return false;
        }

        XDocument xml;
        try
        {
            xml = XDocument.Parse(document.Content, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            blockers.Add(CharacterCreationBootstrapBlockers.CharacterDocumentInvalid);
            return false;
        }

        XElement? character = xml.Root;
        if (character is null
            || character.Name != "character"
            || character.Attributes().Any())
        {
            blockers.Add(CharacterCreationBootstrapBlockers.CharacterDocumentInvalid);
            return false;
        }

        XElement[] markers = character.Elements(CharacterCreationBootstrapXml.MarkerElement)
            .Take(2)
            .ToArray();
        if (markers.Length != 1)
        {
            blockers.Add(markers.Length > 1
                ? CharacterCreationBootstrapBlockers.MarkerDuplicate
                : CharacterCreationBootstrapBlockers.MarkerInvalid);
            return false;
        }

        XElement marker = markers[0];
        XElement[] markerChildren = marker.Elements().ToArray();
        if (marker.Attributes().Any()
            || markerChildren.Length != 2
            || markerChildren.Count(child => child.Name == CharacterCreationBootstrapXml.SchemaElement) != 1
            || markerChildren.Count(child => child.Name == CharacterCreationBootstrapXml.StageElement) != 1
            || !string.Equals(
                ReadSingle(marker, CharacterCreationBootstrapXml.SchemaElement),
                CharacterCreationBootstrapSchemas.MarkerV1,
                StringComparison.Ordinal)
            || !string.Equals(
                ReadSingle(marker, CharacterCreationBootstrapXml.StageElement),
                CharacterCreationBootstrapStages.AwaitingFoundationSelection,
                StringComparison.Ordinal))
        {
            blockers.Add(CharacterCreationBootstrapBlockers.MarkerInvalid);
        }

        XElement[] metatypes = character.Elements("metatype").Take(2).ToArray();
        if (metatypes.Length > 1 || metatypes.Any(node => !string.IsNullOrWhiteSpace(node.Value)))
            blockers.Add(CharacterCreationBootstrapBlockers.MetatypeAlreadySelected);
        XElement[] createdNodes = character.Elements("created").Take(2).ToArray();
        if (createdNodes.Length != 1
            || !bool.TryParse(createdNodes[0].Value.Trim(), out bool created)
            || created)
        {
            blockers.Add(CharacterCreationBootstrapBlockers.CharacterAlreadyCreated);
        }

        string buildMethod = ReadExactlyOne(character, "buildmethod", blockers);
        if (!CharacterCreationBuildMethods.IsSupported(buildMethod))
            blockers.Add(CharacterCreationBootstrapBlockers.BuildMethodInvalid);
        string settingsProfileId = ReadExactlyOne(character, "settings", blockers);
        if (!Guid.TryParseExact(settingsProfileId, "D", out Guid settingsId)
            || settingsId == Guid.Empty
            || !string.Equals(settingsId.ToString("D"), settingsProfileId, StringComparison.Ordinal))
        {
            blockers.Add(CharacterCreationBootstrapBlockers.SettingsProfileInvalid);
        }

        if (!string.Equals(
                ReadExactlyOne(character, "gameedition", blockers),
                "SR5",
                StringComparison.Ordinal)
            || s_PreselectedElements.Any(element => character.Descendants(element).Any())
            || character.Elements("metavariant").Any(node => !string.IsNullOrWhiteSpace(node.Value)))
        {
            blockers.Add(CharacterCreationBootstrapBlockers.PreselectedCreationState);
        }

        if (blockers.Count != 0)
            return false;

        shape = new BootstrapDocumentShape(buildMethod, settingsProfileId);
        return true;
    }

    private static string ReadExactlyOne(
        XElement parent,
        string elementName,
        ICollection<string> blockers)
    {
        XElement[] matches = parent.Elements(elementName).Take(2).ToArray();
        if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Value))
        {
            blockers.Add(CharacterCreationBootstrapBlockers.CharacterDocumentInvalid);
            return string.Empty;
        }

        return matches[0].Value.Trim();
    }

    private static string ReadSingle(XElement parent, string elementName)
        => parent.Element(elementName)?.Value.Trim() ?? string.Empty;

    private static CharacterCreationBootstrapBinding EmptyBinding(CharacterWorkspaceId id)
        => new(
            CharacterCreationBootstrapSchemas.BindingV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection,
            id,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

    private static string[] Normalize(IEnumerable<string> blockers)
        => blockers.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static bool IsAuthorityReadFailure(Exception exception)
        => exception is ArgumentException
            or FormatException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or UnauthorizedAccessException
            or System.Xml.XmlException;

    private sealed record BootstrapDocumentShape(string BuildMethod, string SettingsProfileId);
}
