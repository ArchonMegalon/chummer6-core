using Chummer.Contracts.BuildGhost;

namespace Chummer.Application.BuildGhost;

public sealed record BuildGhostPacketValidationResult(
    bool Accepted,
    IReadOnlyList<string> RejectionReasons);

public static class BuildGhostPacketValidator
{
    public static BuildGhostPacketValidationResult Validate(BuildGhostAnalysisPacket? packet)
    {
        List<string> reasons = [];
        if (packet is null)
        {
            return new BuildGhostPacketValidationResult(false, ["packet-missing"]);
        }

        RequireExact(reasons, "schema", packet.Schema, BuildGhostContractVersions.AnalysisV1);
        RequireExact(reasons, "persona", packet.PersonaId, BuildGhostPersonaIds.Rook);
        RequireExact(reasons, "avatar", packet.AvatarId, BuildGhostPersonaIds.StockDefaultAvatar);
        RequireExact(reasons, "voice", packet.VoiceId, BuildGhostPersonaIds.RookVoice);
        Require(reasons, "owner", packet.OwnerId);
        Require(reasons, "ruleset", packet.RulesetId);
        Require(reasons, "runtime", packet.RuntimeFingerprint);
        Require(reasons, "workspace", packet.WorkspaceId);
        if (packet.WorkspaceRevision < 0)
        {
            reasons.Add("workspace-revision-invalid");
        }

        RequireSha256(reasons, "source", packet.SourceDigest);
        RequireSha256(reasons, "input", packet.InputDigest);
        RequireSha256(reasons, "packet", packet.PacketDigest);
        IReadOnlyList<string> supportedLocales = packet.SupportedLocales ?? [];
        if (string.IsNullOrWhiteSpace(packet.Locale)
            || supportedLocales.Count == 0
            || !supportedLocales.Contains(packet.Locale, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("locale-authority-mismatch");
        }

        if (packet.LocaleFallbackChain is null
            || packet.LocaleFallbackChain.Count == 0
            || !string.Equals(packet.LocaleFallbackChain[0], packet.Locale, StringComparison.OrdinalIgnoreCase)
            || packet.LocaleFallbackChain.Any(locale => !supportedLocales.Contains(locale, StringComparer.OrdinalIgnoreCase)))
        {
            reasons.Add("locale-fallback-authority-mismatch");
        }

        if (packet.GroupCapabilityPosture is { } group
            && (string.IsNullOrWhiteSpace(packet.CampaignId)
                || string.IsNullOrWhiteSpace(group.GroupId)
                || group.GroupRevision is not >= 0
                || !BuildGhostCanonicalDigest.IsSha256(group.MembershipDigest)
                || !string.Equals(group.VisibilityPosture, "authorized-visible-scope", StringComparison.Ordinal)))
        {
            reasons.Add("group-authority-mismatch");
        }

        IReadOnlyList<BuildGhostAllowedAction> suggestedActions = packet.AllowedSuggestedActions ?? [];
        if (packet.AllowedSuggestedActions is null)
        {
            reasons.Add("suggested-actions-missing");
        }
        else if (suggestedActions.Any(action =>
                !action.RequiresExplicitReview
                || action.WorkspaceRevision != packet.WorkspaceRevision
                || !string.Equals(action.SourceDigest, packet.SourceDigest, StringComparison.Ordinal)))
        {
            reasons.Add("suggested-action-binding-mismatch");
        }

        if (BuildGhostCanonicalDigest.IsSha256(packet.PacketDigest))
        {
            string expected = BuildGhostCanonicalDigest.Compute(packet with { PacketDigest = string.Empty });
            if (!string.Equals(expected, packet.PacketDigest, StringComparison.Ordinal))
            {
                reasons.Add("packet-digest-mismatch");
            }
        }

        return new BuildGhostPacketValidationResult(reasons.Count == 0, reasons);
    }

    private static void Require(ICollection<string> reasons, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            reasons.Add($"{field}-missing");
        }
    }

    private static void RequireExact(ICollection<string> reasons, string field, string? value, string expected)
    {
        if (!string.Equals(value, expected, StringComparison.Ordinal))
        {
            reasons.Add($"{field}-mismatch");
        }
    }

    private static void RequireSha256(ICollection<string> reasons, string field, string? value)
    {
        if (!BuildGhostCanonicalDigest.IsSha256(value))
        {
            reasons.Add($"{field}-digest-invalid");
        }
    }
}
