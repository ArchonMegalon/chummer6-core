using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;
using static Chummer.Application.Session.WorkspaceSessionPlayAuthorityResolver;

namespace Chummer.Application.Session;

/// <summary>Projection only, inside the admitted retained-source observation. Never writes XML.</summary>
public static class SessionPlayInitialConditionSource5412
{
    public static SessionPlayInitialCondition5412 Project(XElement root, CharacterSheetDerivedSection sheet,
        SessionPlayBinding binding, string environmentDigest, SessionCanonicalCondition? canonical = null)
    {
        Identity(binding.OwnerId, "condition-owner-required");
        Identity(binding.Version.CharacterId, "condition-character-required");
        Require(binding.Version.RulesetId == "sr5", "condition-retained-source-required");
        Require(long.TryParse(binding.Version.VersionId,
            NumberStyles.None, CultureInfo.InvariantCulture, out long revision) && revision > 0
            && !string.IsNullOrWhiteSpace(binding.Version.RuntimeFingerprint)
            && !string.IsNullOrWhiteSpace(binding.SourceDigest) && !string.IsNullOrWhiteSpace(environmentDigest),
            "condition-retained-source-required");
        ValidateScalars(root);
        Require(sheet.UnavailableReason is null && sheet.Condition is not null
            && !string.IsNullOrWhiteSpace(sheet.AuthorityDigest), "condition-derived-authority-required");
        var c = sheet.Condition!;
        var bodies = sheet.Attributes.Where(a => a.Name == "BOD").ToArray();
        Require(bodies.Length == 1 && bodies[0].Total is > 0 and <= 100 && c.PhysicalMaximum is > 0 and <= 100 && c.StunMaximum is > 0 and <= 100
            && c.OverflowModifier is >= -100 and <= 100,
            "condition-body-monitor-required");
        int p = Integer(root, "physicalcmfilled", 0), s = Integer(root, "stuncmfilled", 0);
        Require(s <= c.StunMaximum && checked(bodies[0].Total + c.OverflowModifier) >= 0,
            "condition-monitor-outside-supported-track");
        var result = new SessionPlayInitialCondition5412(binding.OwnerId, binding.Version, binding.SourceDigest,
            environmentDigest, sheet.AuthorityDigest!, p, s, c.PhysicalMaximum, c.StunMaximum,
            bodies[0].Total, c.OverflowModifier, "");
        if (canonical is not null)
        {
            SessionConditionTransferRules.ValidateState(canonical.State);
            var known = canonical.State;
            Conflict(canonical.RecordedRevision <= revision && known.Physical + known.Overflow == p && known.Stun == s
                && known.PhysicalMaximum == c.PhysicalMaximum && known.StunMaximum == c.StunMaximum
                && known.Body == bodies[0].Total && known.OverflowLimit == bodies[0].Total + c.OverflowModifier,
                "canonical-condition-metadata-changed");
            result = result with { CanonicalCondition = known };
        }
        return result with { Digest = Digest(result) };
    }

    // Character.Save/Load uses cumulative physicalcmfilled. A root-level
    // physicalcmoverflow is NOT the Windows calculatedvalues display field.
    // Retain the older adapter's explicit zero convention; reject nonzero ambiguity.
    public static void ValidateScalars(XElement root)
    {
        Require(!Boolean(root, "physicalcmiscorecm", false) && !Boolean(root, "stuncmismatrixcm", false)
            && !Boolean(root, "ai", false) && Integer(root, "physicalcmoverflow", 0) == 0,
            "condition-unsupported-or-ambiguous-track");
        Require(Integer(root, "physicalcmfilled", 0) is >= 0 and <= 1000000
            && Integer(root, "stuncmfilled", 0) is >= 0 and <= 1000000, "condition-damage-invalid");
    }

    public static void Validate(SessionPlayAuthority authority)
    {
        var x = authority.InitialCondition5412;
        Require(x is not null && x.Digest == Digest(x with { Digest = "" }) && authority.StandardWoundRules,
            "condition-initial-authority-invalid");
        Conflict(x!.OwnerId == authority.Binding.OwnerId && x.Version == authority.Binding.Version
            && x.SourceDigest == authority.Binding.SourceDigest && !string.IsNullOrWhiteSpace(x.EnvironmentDigest),
            "condition-initial-binding-changed");
        Require(x.DerivedDigest == authority.Derived.AuthorityDigest
            && x.PhysicalMaximum == authority.Derived.Condition?.PhysicalMaximum
            && x.StunMaximum == authority.Derived.Condition?.StunMaximum
            && x.OverflowModifier == authority.Derived.Condition?.OverflowModifier
            && authority.Derived.Attributes.Count(a => a.Name == "BOD" && a.Total == x.Body) == 1,
            "condition-initial-rules-changed");
        if (x.CanonicalCondition is { } known)
        {
            SessionConditionTransferRules.ValidateState(known);
            Conflict(known.Physical + known.Overflow == x.PhysicalFilled && known.Stun == x.StunFilled
                && known.PhysicalMaximum == x.PhysicalMaximum && known.StunMaximum == x.StunMaximum
                && known.Body == x.Body && known.OverflowLimit == x.Body + x.OverflowModifier, "canonical-condition-metadata-changed");
        }
        Require(x.PhysicalFilled is >= 0 and <= 1000000 && x.StunFilled >= 0 && x.StunFilled <= x.StunMaximum
            && authority.Trackers.Single(t => t.TrackerId == "physical").DefaultValue == Math.Min(x.PhysicalFilled, x.PhysicalMaximum)
            && authority.Trackers.Single(t => t.TrackerId == "stun").DefaultValue == x.StunFilled,
            "condition-initial-trackers-changed");
    }
}
