using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Characters;

// Filled by the existing sheet calculation, after exact observation admission.
internal sealed class SessionPlaySustainingCapture
{
    internal readonly List<SessionPlaySustainingSkillBasis> Skills = [];
    internal SessionPlaySustainingAuthority? Authority;
    internal SessionPlayCastingAuthority? Casting;
}

internal sealed partial class CharacterSheetOwnerGraph
{
    internal SessionPlaySustainingAuthority? CapturePlaySustaining5262(XElement settings,
        CharacterSheetDerivedSection derived, SessionPlaySustainingBasis basis)
    {
        if (SustainedSpellPreparation5064 is not { } preparation) return null;
        string source = derived.AuthorityDigest!;
        var choices = preparation.Options.Select(o => new SessionPlaySustainingChoice(
            Digest(new { source, o.SpellId, o.SourceId }), o.SpellId, o.SourceId, o.DisplayName,
            o.SourceBook, o.SourcePage, source, CharacterSustainedObjectRules.MinimumForce,
            CharacterSustainedObjectRules.MaximumForce, CharacterSustainedObjectRules.MinimumNetHits,
            CharacterSustainedObjectRules.MaximumNetHits)).ToArray();
        var baseline = preparation.Objects.Select(o =>
        {
            var effect = new SessionPlayMechanicalEffect("baseline:" + Digest(new { source, o.Identity }),
                o.Identity.LinkedObjectId, o.DisplayName, o.Force, o.NetHits, o.SelfSustained, true, o.Identity, "");
            return effect with { StateDigest = Digest(effect) };
        }).ToArray();
        // Eligibility is the existing kernel's predicate; the complete improvement
        // graph was already authenticated. Keep values unrounded for that kernel.
        var allowances = (Node(root, "improvements")?.Elements("improvement") ?? [])
            .Where(e => Text(e, "improvementttype") == "PenaltyFreeSustain"
                && Int(e, "enabled", 1) > 0 && Int(e, "addtorating", 0) == 0
                && Text(e, "improvedname", "") == "" && Text(e, "condition", "") == "")
            .Select(e => new SessionPlaySustainingAllowance(Dec(e, "val"), Int(e, "rating"))).ToArray();
        var captured = new SessionPlaySustainingAuthority("", choices, baseline,
            Int(settings, "dicepenaltysustaining"), Flag(root, "psyche", false), allowances, basis);
        return captured with { Digest = Digest(captured) };
    }
}
