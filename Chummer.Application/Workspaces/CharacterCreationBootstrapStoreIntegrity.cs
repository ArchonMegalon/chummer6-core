using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public static class CharacterCreationBootstrapStoreIntegrity
{
    public static bool IsValidInitialState(
        CharacterWorkspaceId workspaceId,
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        WorkspaceDocumentAuxiliaryState auxiliary = document.AuxiliaryState;
        CharacterCreationBootstrapBinding? binding = auxiliary.CharacterCreationBootstrapBinding;
        if (binding is null
            || auxiliary.CharacterCreationFoundationDraft is not null
            || auxiliary.CharacterCreationPrerequisiteDraft is not null
            || auxiliary.CharacterCreationAttributesDraft is not null
            || auxiliary.CharacterCreationSkillsDraft is not null
            || auxiliary.CharacterCreationSkillsReceipts is not null
            || auxiliary.CharacterCreationContactReceipts is not null
            || !IsValidBinding(workspaceId, binding)
            || !string.Equals(document.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal)
            || !CharacterCreationBootstrapBindingDigest.FixedTimeEquals(
                binding.RawCharacterXmlDigest,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                    document.Content)))
        {
            return false;
        }

        try
        {
            XElement? character = XDocument.Parse(document.Content, LoadOptions.None).Root;
            if (character is null || character.Name != "character")
                return false;
            XElement[] markers = character.Elements(CharacterCreationBootstrapXml.MarkerElement)
                .Take(2).ToArray();
            if (markers.Length != 1)
                return false;
            XElement marker = markers[0];
            if (marker.Attributes().Any()
                || marker.Elements().Count() != 2
                || !string.Equals(
                    marker.Element(CharacterCreationBootstrapXml.SchemaElement)?.Value.Trim(),
                    CharacterCreationBootstrapSchemas.MarkerV1,
                    StringComparison.Ordinal)
                || !string.Equals(
                    marker.Element(CharacterCreationBootstrapXml.StageElement)?.Value.Trim(),
                    CharacterCreationBootstrapStages.AwaitingFoundationSelection,
                    StringComparison.Ordinal))
                return false;
            XElement[] metatypes = character.Elements("metatype").Take(2).ToArray();
            return metatypes.Length <= 1
                   && metatypes.All(node => string.IsNullOrWhiteSpace(node.Value))
                   && character.Elements("created").SingleOrDefault() is XElement createdNode
                   && bool.TryParse(createdNode.Value.Trim(), out bool created)
                   && !created
                   && character.Elements("buildmethod").SingleOrDefault() is XElement buildMethod
                   && string.Equals(
                       buildMethod.Value.Trim(),
                       binding.BuildMethod,
                       StringComparison.Ordinal)
                   && character.Elements("settings").SingleOrDefault() is XElement settings
                   && string.Equals(
                       settings.Value.Trim(),
                       binding.SettingsProfileId,
                       StringComparison.Ordinal)
                   && character.Elements("gameedition").SingleOrDefault() is XElement edition
                   && string.Equals(edition.Value.Trim(), "SR5", StringComparison.Ordinal)
                   && !character.Descendants().Any(element => element.Name.LocalName is
                       "prioritymetatype"
                       or "priorityattributes"
                       or "priorityspecial"
                       or "priorityskills"
                       or "priorityresources"
                       or "prioritytalent"
                       or "sumtoten"
                       or "lifemodule"
                       or "lifemodules");
        }
        catch
        {
            return false;
        }
    }

    public static bool IsValidBinding(
        CharacterWorkspaceId workspaceId,
        CharacterCreationBootstrapBinding? binding)
    {
        if (binding is null
            || !CharacterCreationBootstrapBindingDigest.IsValid(binding)
            || binding.WorkspaceId != workspaceId
            || !string.Equals(binding.Schema, CharacterCreationBootstrapSchemas.BindingV1,
                StringComparison.Ordinal)
            || !string.Equals(binding.Stage,
                CharacterCreationBootstrapStages.AwaitingFoundationSelection,
                StringComparison.Ordinal)
            || !string.Equals(binding.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal)
            || !CharacterCreationBuildMethods.IsSupported(binding.BuildMethod)
            || !CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(
                binding.BuildMethod,
                binding.SettingsProfileId)
            || binding.InitialContentRevision
                != CharacterCreationBootstrapRevisions.InitialContentRevision
            || binding.InitialSavedRevision
                != CharacterCreationBootstrapRevisions.InitialSavedRevision
            || !Guid.TryParseExact(binding.SettingsProfileId, "D", out Guid settingsId)
            || settingsId == Guid.Empty
            || !string.Equals(settingsId.ToString("D"), binding.SettingsProfileId,
                StringComparison.Ordinal)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                binding.RawCharacterXmlDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                binding.RawProfileInputsDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                binding.MetatypeAuthorityDigest)
            || (binding.BuildMethod is CharacterCreationBuildMethods.Priority
                    or CharacterCreationBuildMethods.SumToTen
                && !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                    binding.PrerequisiteAuthorityDigest))
            || (binding.BuildMethod is not (CharacterCreationBuildMethods.Priority
                    or CharacterCreationBuildMethods.SumToTen)
                && !string.IsNullOrEmpty(binding.PrerequisiteAuthorityDigest))
            || !string.Equals(
                binding.SettingsSourceAnchor,
                $"settings.xml#setting:{binding.SettingsProfileId}",
                StringComparison.Ordinal)
            || !CharacterCreationBootstrapProfiles.HasExactCanonicalSourceAnchors(
                binding.BuildMethod,
                binding.SettingsProfileId,
                binding.SourceAnchorIds))
        {
            return false;
        }

        return true;
    }

    public static bool HasMatchingRawCharacterDigest(
        CharacterCreationBootstrapBinding binding,
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(document);
        return CharacterCreationBootstrapBindingDigest.FixedTimeEquals(
            binding.RawCharacterXmlDigest,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                document.Content));
    }
}
