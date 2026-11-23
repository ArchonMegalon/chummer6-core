using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Contracts.Characters.CharacterCreationRoleFindingState;
using static Chummer.Contracts.Characters.CharacterCreationRoleRoutineKind;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Called only with the strict planner's freshly finalized output. The retained
    // resolution belongs to that plan's exact original/source/policy observation.
    internal CharacterCreationRoleAssessment AssessRoleObjectives(
        CharacterCreationRoleCompletionRequest objective, CharacterCreationCompletionBaseline baseline,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var final = baseline.Finalization;
        var root = RoleXml(final.CharacterXml);
        using var priorityScope = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var priorityContext = (priorityScope ?? resolution.Resolver).TryCreateContext(final.CharacterXml);
        var sheet = CharacterSheetDerivedQuery.ParseForRuleEffects2912(final.CharacterXml,
            resolution.Resolver, out var owners, priorityContext is null ? null : original.PriorityGrants3563(priorityContext));
        var routines = new List<CharacterCreationRoleRoutineAssessment>();
        foreach (var routine in objective.Brief.Routines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            routines.Add(new(routine.Id, AssessRoutine(routine).ToArray()));
        }
        var coverage = new List<CharacterCreationRoleFinding>();
        foreach (var role in objective.Brief.Routines.Select(r => r.Role).Distinct())
        {
            coverage.Add(Unknown("role-coverage:" + role, RoleDependency(role)));
            if (role is CharacterCreationRole.Mage or CharacterCreationRole.MysticAdept
                or CharacterCreationRole.Technomancer or CharacterCreationRole.Decker or CharacterCreationRole.Rigger
                && !objective.Brief.Routines.Any(r => r.Role == role && r.IsFallback && r.Kind == SkillReadiness))
                coverage.Add(Failure("fallback-required:" + role,
                    "Declare a separate ordinary physical fallback skill routine; specialist action evidence does not assess physical fallback play."));
        }
        foreach (var assessed in routines)
        {
            var action = assessed.Findings.FirstOrDefault(f => f.Code is "casting-action" or "adept-power-synergy" or "physical-attack-action");
            if (action is not null)
                coverage.Add(action with { Code = "ordinary-action:" + assessed.RoutineId,
                    Detail = "Bounded declared action coverage only; strength targets remain separate findings. " + action.Detail });
        }
        if (objective.Brief.SecondaryRole is not null)
            coverage.Add(Unknown("hybrid-switching", "Both declared routines share the finalized Karma/Nuyen/PP graph. Action switching, setup and simultaneous effects still require a sequence evaluator."));
        var result = new CharacterCreationRoleAssessment(CharacterCreationRoleObjectiveSchemas.AssessmentV1,
            objective.Brief.Routines.Any(r => r.Kind == PhysicalAttack)
                ? CharacterCreationPhysicalRoutineRules3819.Semantics : AwakenedRoutineEvaluator3470,
            CharacterCreationFinalizationDigest.Compute(objective),
            final.Binding, Inputs.SnapshotDigest, Inputs.EffectivePolicy!.PolicyDigest,
            Inputs.RuleSourceCaptureDigest!, Inputs.SourcebookSelection, baseline.Plan.PlanDigest,
            final.ProjectionDigest, sheet.AuthorityDigest, routines.ToArray(), coverage.ToArray(), "");
        return result with { AssessmentDigest = CharacterCreationFinalizationDigest.Compute(result) };

        IEnumerable<CharacterCreationRoleFinding> AssessRoutine(CharacterCreationRoleRoutineObjective routine)
        {
            var c = routine.Conditions;
            if (c.UseEdge || c.ExternalAssistance || c.SpriteAssistance || (c.Consumables && routine.Kind != PhysicalAttack) || c.SustainedEffects != 0
                || c.Preparation.Length != 0 || c.Situation != "ordinary")
            {
                yield return Unknown("conditions", "This evaluator supports ordinary unassisted tests without Edge, consumables, preparation or sustaining. The requested conditions need an action evaluator.");
                yield break;
            }
            if (routine.Kind is not (Casting or PhysicalAttack)) yield return Skill(routine);
            if (routine.Kind != SkillReadiness && routine.Action is null)
                yield return Failure("action-scenario-required", "Declare the intended force/level, target, access or vehicle scenario as applicable; no action inputs are inferred from a role.");
            var t = routine.Targets;
            if (t.MinimumDefensePool is int defense)
                yield return Minimum("defense-pool", sheet.DefensePool, defense,
                    sheet.DefenseUnavailableReason ?? sheet.UnavailableReason,
                    sheet.DefensePool is int pool ? [new("Core defense pool", pool, sheet.AuthorityDigest!)] : []);
            if (t.Limit is { } limit && t.MinimumLimit is int target)
            {
                int? actual = limit switch
                {
                    CharacterCreationRoleLimit.Physical => sheet.PhysicalLimit,
                    CharacterCreationRoleLimit.Mental => sheet.MentalLimit,
                    CharacterCreationRoleLimit.Social => sheet.SocialLimit,
                    _ => null
                };
                yield return Minimum("limit:" + limit, actual, target, sheet.UnavailableReason,
                    actual is int value ? [new("Core base limit; contextual limit modifiers excluded", value, sheet.AuthorityDigest!)] : []);
            }
            if (t.MinimumGroundWalkMeters is decimal meters)
            {
                var ground = sheet.Movement?.Ground;
                yield return Minimum("ground-walk-meters", ground?.WalkMetersPerTurn, meters,
                    ground?.UnavailableReason ?? sheet.UnavailableReason,
                    ground?.WalkMetersPerTurn is decimal value ? [new("Core ground movement", value, sheet.AuthorityDigest!)] : []);
            }
            foreach (string sourceId in routine.RequiredEquipmentSourceIds)
                yield return Equipment(sourceId);
            if (routine.RequiredEquipmentSourceIds.Count > 0 && routine.Kind != PhysicalAttack)
                yield return Unknown("loadout-action-readiness", "Equipped source identity is assessed separately. Ammunition compatibility, access, tools, capacity and mode-specific usability require an action/loadout evaluator.");
            if (routine.Kind != Casting && t.MaximumDamageProbability is decimal risk)
                yield return Unknown("damage-risk", "The requested damage probability needs force/level, drain/fading damage and opposed-action mechanics. A resistance pool alone is not a damage-risk calculation.", risk);
            if (t.MinimumResistancePool is int resistance)
                yield return routine.Kind is Casting or TechnomancerForm
                    ? Resistance(routine.Kind == TechnomancerForm, resistance)
                    : Unknown("resistance-pool", "A drain/fading target requires an explicit Casting or TechnomancerForm routine.", resistance);
            switch (routine.Kind)
            {
                case SkillReadiness:
                    if (routine.IsFallback && routine.Action?.AccessMode == "ordinary-skill")
                        yield return AssessOrdinaryFallback3470(routine, root, sheet);
                    else if (routine.Action is not null)
                        yield return Unknown("skill-action-scenario", "SkillReadiness assesses an ordinary pool only. The supplied action scenario needs a contextual action evaluator.");
                    if (routine.MagicIdentity is not null)
                        yield return Unknown("skill-magic-dependency", "Use an explicit casting, form or adept routine to assess the requested magic dependency.");
                    break;
                case PhysicalAttack:
                    if (priorityContext is null)
                        yield return Unknown("physical-attack-action", "The current finalized source context is unavailable.");
                    else
                        foreach (var finding in CharacterCreationPhysicalRoutineRules3819.Evaluate(
                            routine, final.CharacterXml, priorityContext, original.PriorityGrants3563(priorityContext)))
                            yield return finding;
                    break;
                case Casting:
                    yield return MagicSelection(routine, baseline.Plan.Selections.MagicSelections.Spells);
                    if (t.MinimumResistancePool is null)
                        yield return Failure("resistance-target-required", "Declare a tradition-specific resistance target; no Willpower floor is inferred.");
                    var casting = AssessOrdinaryCasting3470(routine, baseline, root, sheet,
                        Resistance(false, t.MinimumResistancePool ?? 0)).ToArray();
                    foreach (var finding in casting) yield return finding;
                    if (!casting.Any(f => f.Code == "skill-pool"))
                        yield return Unknown("skill-pool", "The declared spell action is not available for a source-backed casting pool assessment.", t.MinimumSkillDicePool);
                    if (t.MaximumDamageProbability is { } maximumRisk && !casting.Any(f => f.Code == "damage-risk"))
                        yield return Unknown("damage-risk", "The declared spell action cannot supply this requested drain risk; its failure/unsupported reason is retained above.", maximumRisk);
                    break;
                case AdeptSkill:
                    yield return MagicSelection(routine, baseline.Plan.Selections.MagicSelections.AdeptPowers.Select(p => p.Identity).ToArray());
                    foreach (var finding in AssessOrdinaryAdept3470(routine, baseline, root, sheet)) yield return finding;
                    break;
                case TechnomancerForm:
                    yield return MagicSelection(routine, baseline.Plan.Selections.MagicSelections.ComplexForms);
                    if (t.MinimumResistancePool is null)
                        yield return Failure("resistance-target-required", "Declare a fading-resistance target for this form routine.");
                    foreach (var finding in AssessMatrixRoutine3470(routine, baseline))
                        yield return finding;
                    break;
                case DeckerMatrix:
                    foreach (var finding in AssessMatrixRoutine3470(routine, baseline))
                        yield return finding;
                    break;
                case RiggerManual:
                    foreach (var finding in AssessMatrixRoutine3470(routine, baseline))
                        yield return finding;
                    break;
                case RiggerRemote:
                    foreach (var finding in AssessMatrixRoutine3470(routine, baseline))
                        yield return finding;
                    break;
                case RiggerJumpedIn:
                    foreach (var finding in AssessMatrixRoutine3470(routine, baseline))
                        yield return finding;
                    break;
            }
        }

        CharacterCreationRoleFinding Skill(CharacterCreationRoleRoutineObjective routine)
        {
            if (sheet.UnavailableReason is { } unavailable)
                return Unknown("skill-pool", unavailable, routine.Targets.MinimumSkillDicePool);
            string sourceId = Guid.Parse(routine.SkillSourceId).ToString("D");
            // Sheet Guid is an instance identity for saved rows and a source identity
            // for canonical unsaved group/granted rows. Never join by display name.
            var saved = root.Element("newskills")?.Elements().Where(e => e.Name == "skills" || e.Name == "knoskills")
                .Elements("skill").Where(e => Guid.TryParse((string?)e.Element("suid"), out var id)
                    && id == Guid.Parse(sourceId)).ToArray() ?? [];
            if (saved.Length > 1) return Unknown("skill-pool", "The source skill has ambiguous saved instances.", routine.Targets.MinimumSkillDicePool);
            string? instance = saved.Length == 1 ? (string?)saved[0].Element("guid") : sourceId;
            var matches = sheet.Skills.Where(s => Guid.TryParse(s.Guid, out var id)
                && Guid.TryParse(instance, out var expected) && id == expected).ToArray();
            if (matches.Length != 1)
                return Unknown("skill-pool", "The sheet has no unique source-resolved row for this skill. Unselected/default-only action evaluation is not implemented here.", routine.Targets.MinimumSkillDicePool);
            var skill = matches[0];
            if (skill.IsNativeLanguage || skill.DicePool is null || skill.EffectiveRating is null || skill.UnavailableReason is not null)
                return Unknown("skill-pool", skill.UnavailableReason ?? "A native language has no numeric test pool.", routine.Targets.MinimumSkillDicePool);
            var attributes = sheet.Attributes.Where(a => a.Name == skill.Attribute).ToArray();
            if (attributes.Length != 1) return Unknown("skill-pool", "The linked attribute projection is unavailable.", routine.Targets.MinimumSkillDicePool);
            int rating = skill.EffectiveRating.Value;
            int basis = rating > 0 ? rating : -1;
            CharacterCreationRoleContribution[] contributions =
            [
                new(skill.Attribute + " (Core linked attribute)", attributes[0].Total, sheet.AuthorityDigest!),
                new(rating > 0 ? "Core effective skill rating" : "Core defaulting term", basis, sheet.AuthorityDigest!),
                new("Remaining Core pool contribution (effects, wounds and clamp; no per-power attribution)",
                    skill.DicePool.Value - attributes[0].Total - basis, sheet.AuthorityDigest!)
            ];
            return Minimum("skill-pool", skill.DicePool, routine.Targets.MinimumSkillDicePool, null, contributions);
        }

        CharacterCreationRoleFinding Equipment(string sourceId)
        {
            if (owners is null || sheet.UnavailableReason is not null)
                return Unknown("equipped-source:" + sourceId, sheet.UnavailableReason ?? "The equipped owner projection is unavailable.");
            var selected = owners.Where(o => o.Metric == CharacterRuleEffectMetric.Selected
                && o.State == CharacterRuleEffectValueState.Known && Guid.TryParse(o.Text, out var id)
                && id == Guid.Parse(sourceId)).ToArray();
            bool equipped = selected.Any(s => owners.Any(o => o.Entity == s.Entity
                && o.Metric == CharacterRuleEffectMetric.Enabled && o.Qualifier == "equipped-owner"
                && o.State == CharacterRuleEffectValueState.Known && o.Flag == true));
            return new("equipped-source:" + sourceId, equipped ? AssessedPass : AssessedFailure,
                equipped ? "A source-reconciled equipped owner is present; action usability is separate."
                    : "Equip an admitted item from the requested source within the same shared budget, or explicitly revise the objective.",
                equipped ? 1 : 0, 1, []);
        }

        CharacterCreationRoleFinding Resistance(bool fading, int target)
        {
            var selections = baseline.Plan.Selections.MagicSelections;
            var identity = fading ? selections.Stream : selections.Tradition;
            string code = fading ? "fading-resistance-pool" : "drain-resistance-pool";
            if (identity is null) return Failure(code, "Select the required tradition/stream before assessing this resistance target.", target);
            var options = (fading ? Inputs.Magic.Authority.Streams : Inputs.Magic.Authority.Traditions)
                .Where(o => o.Identity == identity && o.IsEnabled).ToArray();
            if (options.Length != 1) return Unknown(code, "The selected tradition/stream source is unavailable.", target);
            var option = options[0];
            if (Guid.TryParse(identity.SourceId, out var traditionId)
                && traditionId == CharacterTraditionNameRules.CustomMagicalTraditionSourceId)
                return Unknown(code, "Custom drain selectors require the current tradition selector evaluator; the source default cannot replace a player choice.", target);
            if (!CharacterCreationNativeMagic1430.TryEvaluateCurrentDrain(root, RoleXml(option.CanonicalSourceXml), fading, out int pool))
                return Unknown(code, "The existing native drain kernel cannot consume this source expression, custom selector, attribute modifier or resistance effect. Extend that kernel rather than substitute WIL or an estimated pool.", target);
            // This is the existing expression evaluator over the independently finalized
            // graph. We do not parse a second formula or claim an expected-damage result.
            return Minimum(code, pool, target, null,
                [new("Selected " + option.Name + " expression: " + option.DrainExpression, pool, option.CanonicalSourceXmlDigest)]);
        }
    }

    private static CharacterCreationRoleFinding MagicSelection(CharacterCreationRoleRoutineObjective routine,
        IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> selected)
        => routine.MagicIdentity is not null && selected.Contains(routine.MagicIdentity)
            ? new("magic-selection", AssessedPass, "The intended source identity is selected. Its action contribution is assessed separately.", 1, 1, [])
            : Failure("magic-selection", "Choose the intended source-bound spell, form or power and include it in the legal candidate; a nonempty catalog selection is insufficient.");

    private static CharacterCreationRoleFinding Minimum(string code, decimal? actual, decimal target,
        string? reason, IReadOnlyList<CharacterCreationRoleContribution> contributions)
        => actual is null ? Unknown(code, reason ?? "The required Core projection is unavailable.", target)
            : new(code, actual >= target ? AssessedPass : AssessedFailure,
                actual >= target ? "Meets the declared readiness target under the stated conditions; this is not an action-success probability."
                    : "Below the declared target. Retain player locks; offer an explicitly permitted allocation alternative or ask the player to revise the objective.",
                actual, target, contributions);

    private static CharacterCreationRoleFinding Unknown(string code, string detail, decimal? target = null)
        => new(code, Unsupported, detail, null, target, []);
    private static CharacterCreationRoleFinding Failure(string code, string detail, decimal? target = null)
        => new(code, AssessedFailure, detail, null, target, []);

    private static string RoleDependency(CharacterCreationRole role) => role switch
    {
        CharacterCreationRole.Mage or CharacterCreationRole.MysticAdept => "Declared ordinary direct casting and supported power contributions have separate action findings. Other spell families, sustain/setup, repeated-action recovery, GM glitch consequences and equipment/fallback action usability remain optional or requested coverage gaps; no whole-role completion is inferred.",
        CharacterCreationRole.Adept => "Declared supported power contributions have separate action findings. Other power mechanics, opposed skill outcomes, initiative, surprise, fallback and loadout usability remain distinct coverage gaps.",
        CharacterCreationRole.Technomancer => "Whole-role usefulness requires form/action, persona/access, fading damage and physical safety evaluation. Sprites are not assumed.",
        CharacterCreationRole.Decker => "Whole-role usefulness requires Matrix action/access, deck/program loadout and defenses plus ordinary physical safety.",
        CharacterCreationRole.Rigger => "Whole-role usefulness requires a distinct manual, remote or jumped-in routine with its actual vehicle/drone/controller graph and defenses.",
        CharacterCreationRole.Face => "Whole-role usefulness requires the requested social interaction, opposition, contacts, identity support and escape/loadout; a social skill pool alone is a readiness measure.",
        CharacterCreationRole.Infiltrator => "Whole-role usefulness requires the requested entry method, opposition, tools/identity support, perception and escape/loadout; a skill pool alone is a readiness measure.",
        _ => "Whole-role usefulness requires the intended physical action, survival and usable weapon/ammunition or tool loadout. Sheet readiness targets alone do not establish it."
    };

    private static XElement RoleXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XElement.Load(reader);
    }
}
