using System.Xml.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Rulesets.Sr6;

/// <summary>
/// SR6's built-in pending-draft source. No SR5 XML catalog is opened here and
/// no allocations, default metatype, or finalized statistics are granted.
/// Companion profiles explicitly bind their own book and method source.
/// </summary>
public sealed class Sr6CharacterCreationBootstrapProvider : IRulesetCharacterCreationBootstrapProvider
{
    public string RulesetId => RulesetDefaults.Sr6;

    public bool TryPrepareBinding(CharacterWorkspaceId workspaceId, WorkspaceDocument document,
        out CharacterCreationBootstrapBinding binding, out IReadOnlyList<string> sourceAnchorIds,
        out IReadOnlyList<string> blockers)
    {
        binding = null!;
        sourceAnchorIds = [];
        blockers = [CharacterCreationBootstrapBlockers.CharacterDocumentInvalid];
        if (document.RulesetId != RulesetDefaults.Sr6)
            return false;

        try
        {
            XElement? root = XDocument.Parse(document.Content, LoadOptions.None).Root;
            if (root is null || root.Name != "character" || root.Attributes().Any())
                return false;
            string method = root.Elements("buildmethod").SingleOrDefault()?.Value ?? string.Empty;
            string profileId = root.Elements("settings").SingleOrDefault()?.Value ?? string.Empty;
            if (!Sr6CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(method, profileId))
            {
                blockers = [CharacterCreationBootstrapBlockers.SettingsProfileInvalid];
                return false;
            }

            string[] anchors = Sr6CharacterCreationBootstrapProfiles.ExpectedSourceAnchorIds(method, profileId);
            string profileDigest = Digest(new
            {
                Schema = "chummer.sr6.pending-creation-profile.v1",
                RulesetId,
                BuildMethod = method,
                SettingsProfileId = profileId,
                Stage = CharacterCreationBootstrapStages.AwaitingFoundationSelection,
                Scope = "pending-draft-only",
                SourceAnchorIds = anchors
            });

            var metatypes = new Sr6MetatypeProvider();
            string[] metatypeNames = ["Dwarf", "Elf", "Human", "Ork", "Troll"];
            string[] attributes = ["Body", "Agility", "Reaction", "Strength", "Willpower", "Logic", "Intuition", "Charisma", "Edge"];
            string metatypeDigest = Digest(
                metatypeNames.Select(name => new
                {
                    Metatype = name,
                    Ranges = attributes.Select(attribute => metatypes.GetAttributeRange(name, attribute)).ToArray()
                }).ToArray());
            string prerequisiteDigest = method is Sr6CharacterCreationBuildMethods.Priority or Sr6CharacterCreationBuildMethods.SumToTen
                ? Digest(
                    "ABCDE".Select(new Sr6CharacterCreationProvider().GetPriorityRow).ToArray())
                : string.Empty;
            var unsigned = new CharacterCreationBootstrapBinding(
                CharacterCreationBootstrapSchemas.BindingV1,
                CharacterCreationBootstrapStages.AwaitingFoundationSelection,
                workspaceId, RulesetId, method, profileId,
                CharacterCreationBootstrapRevisions.InitialContentRevision,
                CharacterCreationBootstrapRevisions.InitialSavedRevision,
                "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document.Content))),
                profileDigest, metatypeDigest, prerequisiteDigest,
                Sr6CharacterCreationBootstrapProfiles.SettingsSourceAnchor(profileId), anchors, string.Empty);
            CharacterCreationBootstrapBinding candidate = unsigned with
            {
                BindingDigest = CharacterCreationBootstrapBindingDigest.Compute(unsigned)
            };
            WorkspaceDocument bound = document with
            {
                State = document.State with
                {
                    AuxiliaryState = document.AuxiliaryState with { CharacterCreationBootstrapBinding = candidate }
                }
            };
            if (!CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(workspaceId, bound))
                return false;

            binding = candidate;
            sourceAnchorIds = anchors;
            blockers = [];
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Recompute from the current SR6 inputs on reopening a pending draft. A
    /// self-consistent persisted checksum alone cannot admit changed source data.
    /// Later foundation editors must retain this check before proposing changes.
    /// </summary>
    public bool IsCurrent(CharacterWorkspaceId workspaceId, WorkspaceDocument document)
        => document.AuxiliaryState.CharacterCreationBootstrapBinding is { } stored
           && CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(workspaceId, document)
           && TryPrepareBinding(workspaceId, document, out var current, out _, out _)
           && CharacterCreationBootstrapBindingDigest.FixedTimeEquals(stored.BindingDigest, current.BindingDigest);

    // Only fixed-shape, edition-owned snapshots enter this helper; dictionary
    // order and caller-supplied JSON are deliberately not source authority.
    private static string Digest<T>(T source)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source)));
}
