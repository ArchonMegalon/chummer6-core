using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    internal SessionPlayCastingAuthority? CapturePlayCasting5296(CharacterSheetDerivedSection sheet, SessionPlaySustainingCapture capture, bool indirect = false, bool elemental = false)
    {
        if (!Flag(root, "magenabled", false) || !Flag(root, "magician", false)) return null;
        var choices = new List<SessionPlayCastingChoice>();
        var blockers = new List<string>();
        var traditions = owners.Where(o => o.Kind == "Tradition" && o.Enabled).ToArray();
        var skills = sheet.Skills.Where(s => s.Name == "Spellcasting" && s.Attribute == "MAG").ToArray();
        int magic = sheet.Attributes.SingleOrDefault(a => a.Name == "MAG")?.Total ?? 0;
        int drain = 0;
        string? reason = null;
        bool drainResolved = traditions.Length == 1 && (ImportedPriority5699
            ? CharacterCreationNativeMagic1430.TryEvaluateOwnedDrain5699(traditions[0].Source, false, sheet, this, out drain)
            : CharacterCreationNativeMagic1430.TryEvaluateCurrentDrain(root, traditions[0].Source, false, out drain));
        if (!drainResolved || drain is < 0 or > 200)
            reason = "The selected tradition needs a supported fixed drain expression and unmodified natural drain attributes.";
        else if (skills.Length != 1 || skills[0].EffectiveRating is not > 0 || skills[0].DicePool is null
            || skills[0].UnavailableReason is not null || magic is < 1 or > 100)
            reason = "Learned Spellcasting and positive source-derived Magic are required.";
        else if ((Node(root, "improvements")?.Elements("improvement") ?? []).Any(e => Text(e, "improvementttype", "")
            is "DrainValue" or "SpellCategoryDrain" or "SpellDescriptorDrain"
                or "SpellCategoryDamage" or "SpellDescriptorDamage" or "SpellDamage"))
            reason = "Spell-specific casting, drain or damage modifiers need a contextual Core consumer.";
        // Only the exact retained ordinary focus grammar has a contextual consumer.
        // Generic Gear and arbitrary conditions remain unsupported.
        if (reason is null && Effects.Any(e => Text(e, "improvementttype") == "SpellCategory"
            && (Text(e, "improvementsource") is not ("Quality" or "MentorSpirit")
                || Text(e, "condition").Length != 0) && !IsRetainedSpellcastingFocus7471(e)))
            reason = "Conditional or focus category modifiers need a contextual Core consumer.";
        var raw = skills.Length == 1 ? capture.Skills.SingleOrDefault(s => s.Id == skills[0].Guid) : null;
        if (reason is null && (raw is null || !raw.CanRoll)) reason = "The current casting pool cannot be derived without guessing.";
        if (reason is not null) blockers.Add(reason);
        foreach (var spell in owners.Where(o => o.Kind == "Spell"))
        {
            string? blocked = reason;
            bool resolvedSkill = CharacterDirectSpellRules5296.TryResolveSkill(spell.Source, context, out var effectiveSkill, out var skillEvidence);
            if (!resolvedSkill || skills.Length != 1 || skills[0].Name != effectiveSkill)
                blocked = "The spell needs an explicit supported useskill override or one matching category with useskill=Spellcasting. Restore that source metadata; other skills need a Core consumer.";
            if (resolvedSkill)
                checks.Add(() => CharacterDirectSpellRules5296.TryResolveSkill(spell.Source, context, out var finalSkill, out var finalEvidence)
                    && finalSkill == effectiveSkill && finalEvidence == skillEvidence);
            if (blocked is null && (!spell.Enabled
                || !(elemental ? CharacterDirectSpellRules5296.TryReadElementalSource5361(spell.Source, out _, out _, out _)
                    : indirect ? CharacterDirectSpellRules5296.TryReadIndirectSource(spell.Source, out _, out _)
                    : CharacterDirectSpellRules5296.TryReadSource(spell.Source, out _, out _))
                || new[] { "limited", "extended", "customextended", "alchemical", "barehandedadept" }.Any(f => Flag(spell.Saved, f, false))
                || Text(spell.Saved, "extra", "").Length != 0))
                blocked = "Only an ordinary instantaneous single-target Direct Combat spell is supported here.";
            var specs = skills.Length == 1 ? skills[0].Specializations.Where(s => s.Name == Text(spell.Source, "category", "")).ToArray() : [];
            if (blocked is null && specs.Length > 1) blocked = "The applicable casting specialization is ambiguous.";
            if (blocked is not null) { blockers.Add(Text(spell.Saved, "name") + ": " + blocked); continue; }
            if (!TryOrdinarySpellcastingFocusModifier7471(spell, out decimal focusModifier))
            {
                blockers.Add(Text(spell.Saved, "name") + ": Multiple applicable Spellcasting Foci or Power Foci need an explicit Core selection consumer.");
                continue;
            }
            int adjustment; string defense; string? kind = null; string? element = null;
            if (indirect)
            {
                string indirectKind;
                if (elemental)
                {
                    CharacterDirectSpellRules5296.TryReadElementalSource5361(spell.Source, out adjustment, out indirectKind, out var resolvedElement);
                    element = resolvedElement;
                }
                else CharacterDirectSpellRules5296.TryReadIndirectSource(spell.Source, out adjustment, out indirectKind);
                kind = indirectKind; defense = kind == "indirect-area" ? "threshold 3; no opposed defense" : "REA + INT";
            }
            else CharacterDirectSpellRules5296.TryReadSource(spell.Source, out adjustment, out defense);
            // Spell.DicePool/RelevantImprovements sums enabled matching rows, even
            // with the same unique label, then rounds once. Use only the graph's
            // source-compiled, reconciled effects; saved values are never authority.
            string name = Text(spell.Saved, "name"), sourceId = spell.SourceId.ToString("D");
            string category = Text(spell.Source, "category");
            int modifier = CharacterCreationEssenceRules1222.Round(Effects.Where(e => Active(e)
                && (Text(e, "improvementttype") == "SpellDicePool"
                    && (Text(e, "improvedname") == name || Text(e, "improvedname") == sourceId)
                    || Text(e, "improvementttype") == "SpellCategory" && Text(e, "improvedname") == category))
                .Sum(e => Dec(e, "val")) + focusModifier);
            int pool = checked(raw!.RawPool + (specs.Length == 1 ? specs[0].Bonus : 0) + modifier);
            choices.Add(new(Digest(new { sheet.AuthorityDigest, spell.Id, spell.SourceId, effectiveSkill, skillEvidence }), spell.Id, spell.SourceId,
                Text(spell.Saved, "name"), Text(spell.Source, "source"), Text(spell.Source, "page", ""), sheet.AuthorityDigest!,
                defense, Text(spell.Source, "damage"), Text(spell.Source, "dv"), adjustment, magic, pool, drain) { IndirectKind = kind, Element = element });
        }
        var authority = new SessionPlayCastingAuthority("", choices, blockers);
        if (!indirect)
        {
            var extra = CapturePlayCasting5296(sheet, capture, true);
            var elements = CapturePlayCasting5296(sheet, capture, true, true);
            string[] Replaced(IReadOnlyList<SessionPlayCastingChoice> accepted) => accepted.Select(c => c.DisplayName
                + ": Only an ordinary instantaneous single-target Direct Combat spell is supported here.").ToArray();
            // Empty non-elemental lists must not change an old direct-only authority.
            // WithoutElemental removes this shell again when reconstructing legacy bytes.
            if (extra?.Choices.Count > 0 || elements?.Choices.Count > 0)
                authority = authority with { Indirect = new(extra!.Choices, Replaced(extra.Choices)) {
                    Elemental = elements?.Choices.Count > 0 ? new(elements.Choices, Replaced(elements.Choices)) : null } };
        }
        return authority with { Digest = Digest(authority) };
    }
}
