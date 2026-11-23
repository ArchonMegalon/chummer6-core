using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

/// <summary>Projects only through the store's verified retained current-environment resolver.
/// The complete settings row and amended skill XML remain in that existing portable closure.</summary>
public static class SessionPlayVehiclePolicySource5400
{
    public const string Standard = "sr5-vehicle-specific-199-266/v1";
    public const string OverrideElement = "sessionplayvehiclepolicy";
    private static readonly (SessionPlayPilotSkill Skill, string Id)[] Skills = [
        (SessionPlayPilotSkill.GroundCraft, "ae91a8a6-80e7-4f52-b9eb-21725a5528a4"),
        (SessionPlayPilotSkill.Aircraft, "10d5c887-a1e5-4cca-8613-3a28f1aab810"),
        (SessionPlayPilotSkill.Aerospace, "3ba9397e-f790-44ca-ae40-15a2356e348d"),
        (SessionPlayPilotSkill.Watercraft, "1579818e-af85-47cd-8c9f-2e86e9dc19da"),
        (SessionPlayPilotSkill.Walker, "b8a24d87-465a-4365-9948-038fe1ac62c4") ];

    public static SessionPlayVehiclePolicy5400? TryProject(ICharacterSourceDataResolver resolver,
        string characterXml, SessionPlayBinding binding, string? environmentDigest, IList<SessionPlayDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(environmentDigest))
        {
            diagnostics.Add(new("vehicle-policy-retained-environment-required", "vehicle-policy",
                "Adopt and save this character's complete rule environment before vehicle tests. No ambient profile is substituted."));
            return null;
        }
        try { return Project(resolver, characterXml, binding, environmentDigest); }
        catch (Exception error) when (error is SessionPlayException or System.Xml.XmlException)
        {
            diagnostics.Add(new("vehicle-policy-source-invalid", "vehicle-policy",
                "Repair and re-adopt the selected profile's sessionplayvehiclepolicy (version, jumpedhotsimbonus, armorequality) "
                + "or its Pilot skill source/default flags. No standard replacement was used."));
            return null;
        }
    }

    // Public for the existing source/retained-environment tests; callers still cannot grant server authority.
    public static SessionPlayVehiclePolicy5400 Project(ICharacterSourceDataResolver resolver, string characterXml,
        SessionPlayBinding binding, string environmentDigest)
    {
        Require(binding.Version.RulesetId == "sr5" && !string.IsNullOrWhiteSpace(environmentDigest), "vehicle-policy-retained-environment-required");
        var context = resolver.TryCreateContext(characterXml);
        Require(context is not null, "vehicle-policy-profile-unavailable");
        Require(context!.TryResolveCareerSettingsProfile(out var settingsXml, out var state)
            && !string.IsNullOrWhiteSpace(state), "vehicle-policy-profile-unavailable");
        var settings = WorkspaceSessionPlayAuthorityResolver.Parse(settingsXml);
        Require(settings.Name == "settings", "vehicle-policy-profile-invalid");
        var interpreted = InterpretSettings5497(settings);
        int hot = interpreted.JumpedHotSimBonus;
        var equality = interpreted.ArmorEquality;
        string id = interpreted.PolicyId;
        var pilots = new List<SessionPlayPilotPolicy5400>();
        foreach (var (skill, sourceId) in Skills)
        {
            Require(context.TryResolveActiveSkillSource(sourceId, out var source), "vehicle-policy-pilot-source-unavailable");
            var xml = WorkspaceSessionPlayAuthorityResolver.Parse(source.RawSourceXml);
            string name = SessionPlayElementalFollowups.PilotSkillName5393(skill);
            Require(xml.Name == "skill" && Guid.TryParse(Leaf(xml, "id"), out var resolvedId)
                && resolvedId == Guid.Parse(sourceId) && Leaf(xml, "name") == name
                && Leaf(xml, "attribute") == "REA" && Leaf(xml, "category") == "Vehicle Active",
                "vehicle-policy-pilot-source-unsupported");
            Require(bool.TryParse(Leaf(xml, "default"), out bool canDefault), "vehicle-policy-pilot-default-invalid");
            pilots.Add(new(skill, sourceId, name, canDefault, Digest(source.RawSourceXml)));
        }
        var policy = new SessionPlayVehiclePolicy5400(1, id, binding.OwnerId, binding.Version, binding.SourceDigest,
            environmentDigest, Digest(state), hot, equality, pilots, "");
        return policy with { Digest = Digest(policy) };
    }

    // The canonical Profile consumer and vehicle authority share this exact
    // interpreter. Missing explicit fields throw; only absence selects Standard.
    internal sealed record SettingsPolicy5497(string PolicyId, int JumpedHotSimBonus,
        SessionPlayVehicleArmorEquality ArmorEquality);

    internal static SettingsPolicy5497 InterpretSettings5497(XElement settings)
    {
        Require(settings.Name == "settings", "vehicle-policy-profile-invalid");
        var rows = settings.Elements().Where(e => e.Name.LocalName == OverrideElement).ToArray();
        Require(rows.Length <= 1, "vehicle-policy-override-ambiguous");
        // Explicit versioned standard interpretation, only after successful retained profile resolution.
        int hot = 1;
        var equality = SessionPlayVehicleArmorEquality.ArmorStopsDamage;
        string id = Standard;
        if (rows.Length == 1)
        {
            var row = rows[0];
            Require(row.Name == OverrideElement && !row.HasAttributes
                && row.Elements().Count() == 3
                && row.Nodes().All(n => n is XElement || n is XText t && string.IsNullOrWhiteSpace(t.Value)),
                "vehicle-policy-override-invalid");
            string version = Leaf(row, "version"), bonus = Leaf(row, "jumpedhotsimbonus"), armor = Leaf(row, "armorequality");
            Require(version == "1" && bonus is "1" or "2" or "3", "vehicle-policy-override-version-or-bonus-invalid");
            hot = int.Parse(bonus, CultureInfo.InvariantCulture);
            equality = armor switch {
                "stops" => SessionPlayVehicleArmorEquality.ArmorStopsDamage,
                "resist" => SessionPlayVehicleArmorEquality.RollResistance,
                _ => throw new SessionPlayException(SessionPlayFailures.Invalid("vehicle-policy-override-armor-invalid")) };
            id = "sr5-profile-vehicle-override/v1";
        }
        return new(id, hot, equality);
    }

    // A narrow typed envelope may record an unavailable policy (missing/invalid
    // scalar values); arbitrary settings/effects and foreign XML stay rejected.
    internal static bool IsRecognizedOverride5497(XElement row)
        => row.Name == OverrideElement && !row.HasAttributes
            && row.Nodes().All(n => n is XElement || n is XText t && t.GetType() == typeof(XText) && string.IsNullOrWhiteSpace(t.Value))
            && row.Elements().All(field => field.Name.Namespace == XNamespace.None
                && field.Name.LocalName is "version" or "jumpedhotsimbonus" or "armorequality"
                && !field.HasAttributes && !field.HasElements
                && field.Nodes().All(n => n is XText t && t.GetType() == typeof(XText)));

    private static string Leaf(XElement parent, string name)
    {
        var rows = parent.Elements().Where(e => e.Name.LocalName == name).ToArray();
        Require(rows.Length == 1 && rows[0].Name == name && !rows[0].HasAttributes && !rows[0].HasElements,
            "vehicle-policy-explicit-field-missing-or-invalid");
        return rows[0].Value.Trim();
    }
}
