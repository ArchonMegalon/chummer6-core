using System.Xml;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

internal static class CharacterCareerPowerGovernance
{
    internal static bool Allows(WorkspaceStoredDocument saved)
    {
        if (saved.HasDelegatedGmHistory
            || saved.Document.Format != WorkspaceDocumentFormat.NativeXml
            || saved.Document.RulesetId != "sr5" || saved.Document.SchemaVersion != 1
            || saved.Document.PayloadKind is not ("workspace" or "sr5/chum5-xml")) return false;
        try
        {
            // Historical campaign evidence is a reason to refuse this local lane,
            // never a Creation-era permission to perform a Career transaction.
            using var auxiliary = JsonSerializer.SerializeToDocument(saved.Document.AuxiliaryState);
            return AllowsJson(auxiliary.RootElement) && Allows(CharacterCareerPowerGraph.Parse(saved.Document.Content));
        }
        catch (Exception e) when (e is XmlException or InvalidDataException) { return false; }
    }

    internal static bool Allows(XElement root)
    {
        if (root.Name != "character" || root.HasAttributes) return false;
        return AllowsMetadata(root);
    }

    internal static bool AllowsMetadata(XElement root)
    {
        // Absence of governance is the only local authorization posture. Even an
        // empty, false, namespaced or unrecognized campaign declaration is denied.
        // Opaque metadata/extension containers cannot conceal a permission policy.
        foreach (var node in root.DescendantsAndSelf())
        {
            if (node.Name.NamespaceName.Length != 0 || IsGovernance(node.Name.LocalName)
                || node.Name.LocalName == "improvementsource" && IsGovernance(node.Value)
                || node.Attributes().Any(a => a.IsNamespaceDeclaration
                    || a.Name.NamespaceName.Length != 0 || IsGovernance(a.Name.LocalName)))
                return false;
        }
        return true;
    }

    private static bool AllowsJson(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            return value.EnumerateObject().All(p => (!IsGovernanceProperty(p.Name) || p.Value.ValueKind == JsonValueKind.Null)
                && AllowsJson(p.Value));
        return value.ValueKind != JsonValueKind.Array || value.EnumerateArray().All(AllowsJson);
    }

    private static bool IsGovernanceProperty(string name) =>
        new[] { "campaign", "govern", "permission", "gamemaster", "delegat" }
            .Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static bool IsGovernance(string name) =>
        new[] { "campaign", "govern", "permission", "gamemaster", "delegat" }
            .Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase))
        || name.Equals("metadata", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("extension", StringComparison.OrdinalIgnoreCase)
        || name.Equals("sharing", StringComparison.OrdinalIgnoreCase)
        || name.Equals("authority", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Local-only authority; campaign or unknown governance never implies GM permission.</summary>
public sealed class CampaignDenyingCareerPowerAuthorityResolver(ICharacterSourceDataResolver sources)
    : ICharacterCareerPowerAuthorityResolver
{
    private readonly OwnerManagedCareerPowerAuthorityResolver inner = new(sources);
    public ICharacterCareerPowerAdmission? Open(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerPowerCommand command, string runtimeFingerprint) =>
        OpenDetailed(owner, saved, command, runtimeFingerprint, out _);

    public ICharacterCareerPowerAdmission? OpenDetailed(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerPowerCommand command, string runtimeFingerprint, out string? blocker)
    {
        blocker = "power_campaign_authority_unavailable";
        return CharacterCareerPowerGovernance.Allows(saved)
            ? inner.OpenDetailed(owner, saved, command, runtimeFingerprint, out blocker) : null;
    }
}
