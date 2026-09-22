using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Projects an already revalidated saved ledger. Never saves, grants effects, or discards balances.</summary>
internal static class Sr6CreationDraftSummaryRules
{
    internal static Sr6CreationDraftSummary Project(Sr6CreationFoundationState state)
    {
        var saved = state.Selection;
        var steps = new List<Sr6CreationDraftStep>();
        Add("foundation", saved is not null, true,
            saved?.PointBuy is { } points ? [new("character-points", points.PointsRemaining)] : []);
        // Existing immutable ledgers normalize empty quality choices to null.
        // Optional absence is neither an unfinished mandatory choice nor proof of review.
        Add("qualities", saved?.Qualities is not null, saved is not null, unselectedStatus: Sr6CreationDraftStepStatuses.NoneSelected);
        Add("attributes", saved?.Attributes is not null, saved is not null,
            saved?.Attributes is { } attributes
                ? [new("attribute-points", attributes.AttributePointsRemaining), new("adjustment-points", attributes.AdjustmentPointsRemaining)] : []);
        Add("skills", saved?.Skills is not null, saved is not null,
            saved?.Skills is { } skills ? [new("skill-points", skills.PointsRemaining)] : []);
        Add("karma", saved?.Karma is not null, state.KarmaOptions is not null,
            saved?.Karma is { } karma ? [new("karma-above-cap", karma.UnspentAboveCarryOver)] : []);
        Add("knowledge", saved?.Knowledge is not null, saved?.Attributes is not null,
            saved?.Knowledge is { } knowledge ? [new("knowledge-points", knowledge.PointsRemaining)] : []);
        Add("contacts", saved?.Contacts is not null, state.ContactOptions is not null,
            saved?.Contacts is { } contacts ? [new("contact-points", contacts.PointsRemaining)] : []);
        if (saved is not null && saved.Selection.TalentId != "mundane")
        {
            Add("talent", saved.TalentAllocation is not null, state.TalentOptions is not null);
            if (saved.Selection.TalentId == "technomancer")
                Add("forms", saved.ComplexForms is not null, state.ComplexFormOptions is { Count: > 0 },
                    saved.ComplexForms is { } forms && saved.TalentAllocation is { } formBudget
                        ? [new("free-form-slots", Math.Max(0, formBudget.FreeComplexFormSlots - forms.FreeSlotsUsed))] : []);
            // A conjuring-only aspect has no spell entitlement. Before the aspect
            // and budget are saved, show the dependency as waiting, not completed.
            if (saved.Selection.TalentId is "magician" or "mystic-adept" or "aspected-magician"
                && (saved.TalentAllocation is null || saved.TalentAllocation.SpellOrRitualLimit > 0
                    || saved.TalentAllocation.AlchemicalSpellLimit > 0))
                Add("spells", saved.Spells is not null, state.SpellOptions is { Count: > 0 },
                    saved.Spells is { } spells && saved.TalentAllocation is { } spellBudget
                        ? [new("free-spell-slots", Math.Max(0, spellBudget.FreeSpellOrRitualSlots
                            + spellBudget.FreeAlchemicalSpellSlots - spells.FreeSlotsUsed))] : []);
            if (saved.Selection.TalentId is "adept" or "mystic-adept")
                Add("powers", saved.AdeptPowers is not null, state.AdeptPowerOptions is { Count: > 0 },
                    saved.AdeptPowers is { } powers ? [new("power-points", powers.QuarterPointsRemaining / 4m)] : []);
        }
        Add("gear", saved?.Gear is not null, saved is not null);
        Add("lifestyle", saved?.Lifestyle is not null, saved is not null);

        Sr6CreationDraftBalances? balances = null;
        if (saved is not null)
        {
            decimal resources = saved.Karma?.ResourcesNuyen ?? saved.Budget.ResourcesNuyen;
            decimal gear = saved.Gear?.SpentNuyen ?? 0m, lifestyle = saved.Lifestyle?.LifestyleSpentNuyen ?? 0m;
            decimal remaining = resources - gear - lifestyle;
            int remainingKarma = saved.Karma?.KarmaRemaining ?? saved.Qualities?.CustomizationKarma ?? Sr6CreationKarmaRules.BaseCustomizationKarma;
            int karmaCap = saved.Karma?.MaximumCarryOver ?? Sr6CreationKarmaRules.MaximumCarryOver;
            balances = new(resources, gear, lifestyle, remaining, Math.Min(remaining, Sr6CreationGearRules.MaximumCarryOver),
                Math.Max(0m, remaining - Sr6CreationGearRules.MaximumCarryOver), remainingKarma,
                Math.Min(remainingKarma, karmaCap), Math.Max(0, remainingKarma - karmaCap));
        }
        var natural = Sr6CreationNaturalValuesRules.Project(state);
        return new(state.Binding, steps.AsReadOnly(), balances,
            (saved?.SourceAnchorIds ?? []).Concat(["sr6_core_de_2024:p69-70"]).Distinct(StringComparer.Ordinal).ToArray())
            { NaturalValues = natural, PassiveValues = Sr6CreationPassiveValuesRules.Project(state, natural) };

        void Add(string id, bool reviewed, bool canOpen, Sr6CreationDraftRemainder[]? remainders = null, string? unselectedStatus = null)
        {
            var unspent = (remainders ?? []).Where(row => row.Amount > 0).ToArray();
            string status = reviewed ? unspent.Length > 0 ? Sr6CreationDraftStepStatuses.Unspent : Sr6CreationDraftStepStatuses.Saved
                : canOpen ? unselectedStatus ?? Sr6CreationDraftStepStatuses.Missing : Sr6CreationDraftStepStatuses.Waiting;
            steps.Add(new(id, status, canOpen, unspent));
        }
    }
}
