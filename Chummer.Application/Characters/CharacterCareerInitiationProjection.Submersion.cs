using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerInitiationProjection
{
    private static bool TrySubmersionQuote(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerInitiationSelection selection, string runtime, CharacterCareerInitiationSources sources,
        ICharacterSourceDataContext? context, XElement root, XElement settings, out CharacterCareerInitiationQuote? quote,
        CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? current = null)
    {
        quote = null;
        if (context is null || sources.Echoes is null) return false;
        if (current is not null && !current.Matches(saved, context)) return false;
        var state = current is null ? CharacterCareerResonanceEvaluator.Evaluate(root, context)
            : current.Resonance ?? throw new InvalidDataException("submersion_imported_resonance_unavailable");
        var blockers = new List<string>(state.Blockers);
        if (!CharacterCareerInitiationTransaction.ObserveHistory(saved))
            blockers.Add("submersion_receipt_history_inconsistent");
        if (root.Elements().Any(x => x.Name.LocalName.Contains("campaign", StringComparison.OrdinalIgnoreCase)
            && (x.HasElements || !string.IsNullOrWhiteSpace(x.Value)))) blockers.Add("initiation_campaign_authority_unavailable");
        if (Boolean(root, "ignorerules", false)) blockers.Add("submersion_ignore_rules_unsupported");
        if (Boolean(root, "initiationdisabled", false)) blockers.Add("initiation_disabled");
        if (!Boolean(settings, "careerinitiationenabled", true)) blockers.Add("initiation_profile_disabled");
        int grade = int.Parse(Scalar(root, "submersiongrade", "0"), Invariant);
        bool deferred = selection.Mode == CharacterCareerInitiationMode.DeferredBenefit;
        bool gradeOnly = selection.Mode == CharacterCareerInitiationMode.GradeOnly;
        int next = deferred ? grade : checked(grade + 1);
        if (!Enum.IsDefined(selection.Mode)) blockers.Add("submersion_mode_unsupported");
        if (!deferred && selection.TargetGradeId is not null) blockers.Add("submersion_target_grade_unexpected");
        if (gradeOnly && selection.BenefitSourceId != Guid.Empty) blockers.Add("submersion_grade_only_benefit_unexpected");
        var grades = CharacterCareerResonanceEvaluator.SubmersionGrades(root, state.Blockers);
        if (grades.Any(g => g.GradeXml.Length > 65536)) blockers.Add("submersion_grade_snapshot_too_large");
        if (deferred)
        {
            if (selection.TargetGradeId is null || selection.TargetGradeId == Guid.Empty)
                blockers.Add("submersion_target_grade_required");
            else if (grades.SingleOrDefault(g => g.GradeId == selection.TargetGradeId) is not { } target)
                blockers.Add("submersion_target_grade_unavailable");
            else
                blockers.AddRange(target.Blockers);
        }
        if (grade < 0 || next > CharacterCareerInitiationRules.MaximumGrade) blockers.Add("submersion_grade_invalid");
        string configuredCap = Scalar(settings, "careerinitiationmaximumgrade", "");
        if (!deferred && configuredCap.Length > 0 && (!int.TryParse(configuredCap, NumberStyles.None, Invariant, out int cap)
            || cap < 0 || next > cap)) blockers.Add("initiation_profile_grade_cap");
        if (!deferred && state.IsSupported && next > state.EffectiveResonance) blockers.Add("submersion_resonance_cap");
        var adjustments = new[] { ("group", selection.Group), ("ordeal", selection.Ordeal), ("schooling", selection.Schooling) }
            .Select(x => SubmersionAdjustment(x.Item1, x.Item2, deferred, settings, root)).ToArray();
        blockers.AddRange(adjustments.Where(a => a.Selected).SelectMany(a => a.Blockers));
        decimal discount = adjustments.Where(a => a.Selected).Sum(a => a.Discount);
        if (discount > 1) blockers.Add("submersion_discount_total_invalid");
        var costs = Container(settings, "karmacost");
        int cost = deferred ? 0 : CharacterCareerInitiationRules.Cost(Integer(costs, "karmainitiationflat"),
            Integer(costs, "karmainitiation"), next, discount <= 1 ? discount : 0);
        int karma = Integer(root, "karma");
        decimal nuyen = Number(root, "nuyen", 0);
        if (karma < cost) blockers.Add("initiation_insufficient_karma");
        if (karma < 0 || nuyen < 0) blockers.Add("initiation_balance_invalid");
        var benefits = sources.Echoes.Select(row => CharacterCareerResonanceEvaluator.EchoBenefit(row, root)).ToArray();
        if (benefits.Length > 4096 || benefits.Select(b => b.SourceId).Distinct().Count() != benefits.Length) return false;
        var selected = benefits.SingleOrDefault(b => b.SourceId == selection.BenefitSourceId);
        if (!gradeOnly)
        {
            if (selection.BenefitSourceId == Guid.Empty) blockers.Add("initiation_benefit_choice_required");
            else if (selected is null) blockers.Add("initiation_benefit_unavailable");
            else blockers.AddRange(selected.Blockers);
        }
        var binding = new CharacterCareerInitiationBinding(owner.Owner.NormalizedValue, saved.Id.Value,
            owner.AuthorityInstanceId, owner.TransitionRevision, saved.ContentRevision, saved.SavedRevision,
            CharacterCareerInitiationRules.Hash(saved.Document.Content), saved.Document.AuxiliaryStateDigest, runtime);
        quote = new(binding, selection, grade, next, 0, null, false, karma, nuyen, cost, 0,
            CharacterCareerInitiationRules.Digest(new { Sources = sources, ResonanceSourceDigest = state.SourceDigest }),
            adjustments, benefits, Array.Empty<string>(), blockers.Distinct(StringComparer.Ordinal).ToArray())
        {
            Family = CharacterCareerInitiationFamily.Resonance,
            EffectiveResonance = state.IsSupported ? state.EffectiveResonance : null,
            SubmersionGrades = grades
        };
        return true;
    }

    private static CharacterCareerInitiationAdjustment SubmersionAdjustment(string id, bool selected,
        bool deferred, XElement settings, XElement root)
    {
        // A saved grade already owns its discount choices. Filling its first Echo
        // must neither price that grade again nor reinterpret its historic flags.
        if (deferred)
            return new(id, selected, 0, 0, "", ["submersion_discount_requires_new_grade"]);
        var blockers = new List<string>();
        // Retained CharacterSettings initializes RES rates to .10/.20/.10. The
        // selected effective profile may explicitly override them, like MAG rates.
        decimal discount = Number(settings, "karmaresinitiation" + id + "percent", id == "ordeal" ? 0.20m : 0.10m);
        if (discount is < 0 or > 1)
        {
            blockers.Add("submersion_" + id + "_rate_invalid");
            discount = 0;
        }
        if (id == "group" && !Boolean(root, "groupmember", false))
            blockers.Add("submersion_network_membership_required");
        if (id == "schooling" && !Boolean(settings, "allowtechnomancerschooling", false))
            blockers.Add("submersion_schooling_disabled");
        // CharacterCareer's RES branch records the task/network/schooling choice
        // and spends only Karma. Do not import MAG schooling fees or a calendar.
        return new(id, selected, discount, 0, "", blockers.AsReadOnly());
    }
}
