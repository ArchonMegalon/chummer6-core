using System.Globalization;
using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Optional live-source capability. Historical inventory reads retain their bytes.</summary>
public interface ICharacterCreationPolicyConsumptionSource
{
    bool TryResolveCreationPolicyConsumption(out CharacterCreationEffectivePolicyContext? policy);
}

/// <summary>Accounts for existing evaluator executions; never calculates a rule outcome.
/// Full and selected routes supply their own typed inputs and actual results. Only their
/// common, independently reproducible projections are hashed. Source observations alone
/// cannot produce evaluated rows, and this bridge never satisfies Complete.</summary>
internal static class CharacterCreationPolicyConsumptionProjection
{
    // Shared minimum inventory. Serialization order and all eight historical
    // unresolved operations are unchanged. A hash cannot discharge an operation.
    internal static IReadOnlyList<CharacterCreationConsumerPolicyOperation> ActiveGraphObligations { get; } =
        Array.AsReadOnly(new[]
        {
            CharacterCreationConsumerPolicyOperation.SelectedEffects, CharacterCreationConsumerPolicyOperation.Amendments,
            CharacterCreationConsumerPolicyOperation.Errata, CharacterCreationConsumerPolicyOperation.CharacterMechanics,
            CharacterCreationConsumerPolicyOperation.Campaign, CharacterCreationConsumerPolicyOperation.LifestyleGrantEffects,
            CharacterCreationConsumerPolicyOperation.SkillKarmaModifiers, CharacterCreationConsumerPolicyOperation.LifestyleCostEffects
        });

    internal static IEnumerable<(CharacterCreationSettingObservation Setting, CharacterCreationSettingConsumer Consumer, bool HasInput)>
        DescribeSettingObligations(CharacterCreationEffectivePolicyContext source, CharacterCreationSelectedConsumerPolicy policy)
    {
        foreach (var setting in source.SettingObservations!.Settings)
            foreach (var binding in Bindings(setting.Key, policy))
                yield return (setting, binding.Consumer, binding.Input is not null && binding.Value is not null);
    }

    internal static bool MatchesSource(CharacterCreationEffectivePolicyContext policy, CharacterCreationMysticAdeptPowerPointPolicy? source)
        => policy.SettingObservations is null || source is not null
            && source.SettingsProfileId == policy.SettingsProfileId
            && CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(source)
            && CharacterCreationSettingsObservation.TryCapture(source.CanonicalSourceXml, policy.ProfileInputsDigest,
                policy.SettingObservations.Schema, out var expected)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(policy.SettingObservations, expected);

    internal static bool HasValidObservation(CharacterCreationEffectivePolicyContext policy)
    {
        if (policy.SettingObservations is not { } observation) return policy.SelectedConsumption is null;
        var definitions = CharacterCreationSettingDefinitions.ForSchema(observation.Schema);
        if (definitions is null
            || observation.ProfileInputsDigest != policy.ProfileInputsDigest
            || observation.Settings is null || policy.Settings is null
            || observation.Settings.Count > CharacterCreationEffectivePolicyContext.MaximumSettings + definitions.Count
            || observation.Settings.Any(row => row is null || !Enum.IsDefined(row.State)
                || row.Key != CharacterCreationObservedSetting.Opaque && !definitions.Any(d => d.Key == row.Key))) return false;
        var actual = observation.Settings.Where(row => row.State != CharacterCreationSettingObservationState.Absent).ToArray();
        if (!actual.Select(row => row.SettingId).SequenceEqual(policy.Settings.Skip(3).Select(row => row.SettingId))) return false;
        foreach (var row in observation.Settings)
        {
            if (row.State == CharacterCreationSettingObservationState.Absent)
            {
                if (row.Key == CharacterCreationObservedSetting.Opaque || row.SettingId != "absent:" + row.Key
                    || row.SourceValueDigest is not null || row.ValueDigest is not null) return false;
            }
            else if (!CharacterCreationFinalizationDigest.IsCanonical(row.SourceValueDigest)) return false;
            if (row.Key == CharacterCreationObservedSetting.Opaque
                ? row.State != CharacterCreationSettingObservationState.Opaque || row.ValueDigest is not null
                : row.State == CharacterCreationSettingObservationState.Opaque) return false;
            if (row.State == CharacterCreationSettingObservationState.Present
                ? !CharacterCreationFinalizationDigest.IsCanonical(row.ValueDigest) : row.ValueDigest is not null) return false;
        }
        foreach (var definition in definitions)
        {
            var rows = observation.Settings.Where(row => row.Key == definition.Key).ToArray();
            if (rows.Length == 0 || rows.Length > 1 && rows.Any(row => row.State != CharacterCreationSettingObservationState.Duplicate)
                || rows.Length == 1 && rows[0].State == CharacterCreationSettingObservationState.Duplicate) return false;
        }
        // Absent records follow all actual occurrences in stable definition order.
        return observation.Settings.Select(row => row.SettingId).SequenceEqual(actual.Select(row => row.SettingId)
            .Concat(definitions.Where(d => !actual.Any(row => row.Key == d.Key)).Select(d => "absent:" + d.Key)));
    }

    internal static CharacterCreationPolicyConsumption? FromFull(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReview review)
    {
        if (inputs.EffectivePolicy?.SettingObservations is null) return null;
        var values = new Dictionary<CharacterCreationSettingConsumer, object?>
        {
            [CharacterCreationSettingConsumer.Attributes] = review.Skills?.Attributes,
            [CharacterCreationSettingConsumer.Skills] = review.Skills,
            [CharacterCreationSettingConsumer.Qualities] = review.UpstreamBudget is { } upstream ? QualityResult(upstream.Qualities.Qualities) : null,
            [CharacterCreationSettingConsumer.Magic] = review.UpstreamBudget is { } magic ? MagicResult(magic.Magic) : null,
            [CharacterCreationSettingConsumer.Resources] = review.Lifestyles is { } lifestyles
                ? new { lifestyles.Gear.Resources.SelectedOption, lifestyles.Gear.Resources.Budget } : null,
            [CharacterCreationSettingConsumer.Gear] = review.Lifestyles is { } gear ? new { gear.Gear.Lines, gear.Gear.Budget } : null,
            [CharacterCreationSettingConsumer.Lifestyles] = review.Lifestyles is { } living
                ? new { living.Authority, living.Lifestyles, living.Budget } : null,
            [CharacterCreationSettingConsumer.Contacts] = review.Contacts is { } contacts
                ? new { contacts.Contacts, contacts.ContactBudget, contacts.HighPlacesBudget, contacts.AdditionalKarmaCost, contacts.GroupKarma } : null,
            [CharacterCreationSettingConsumer.Loadout] = review.Loadout
        };
        return Project(inputs.EffectivePolicy, inputs.SnapshotDigest, review.Request.Selections,
            CharacterCreationSelectedPolicyProjection.FromConsumers(inputs.EffectivePolicy.Consumers!),
            review.Skills, values, review.Blockers.Count == 0, review.Lifestyles?.Authority, review.Lifestyles?.Lifestyles,
            review.UpstreamBudget?.Qualities.Qualities, inputs.Lifestyles.Authority, inputs.Magic.Authority.MysticAdeptPowerPointPolicy,
            review.Blockers.Count == 0 ? CharacterCreationSelectedPolicyProjection.SkillGroupExecutions(
                inputs.EffectivePolicy, inputs.Skills, review.Request.Selections, review.Skills,
                review.UpstreamBudget?.Qualities.Qualities,
                (inputs.Magic.SelectedTalent.GrantedQualitySources ?? []).Select(row => row.CanonicalSourceXml)) : null);
    }

    internal static CharacterCreationPolicyConsumption? FromSelected(CharacterCreationSelectedCompletionWitness witness,
        CharacterCreationCompletionReviewRequest request, CharacterCreationSelectedCompletionEvaluation result,
        CharacterCreationCompletionOriginalSelections original, IReadOnlyList<CharacterContactRuleEffect> effects)
    {
        if (witness.EffectivePolicy?.SettingObservations is null) return null;
        var values = new Dictionary<CharacterCreationSettingConsumer, object?>
        {
            [CharacterCreationSettingConsumer.Attributes] = result.Skills.Attributes,
            [CharacterCreationSettingConsumer.Skills] = result.Skills,
            [CharacterCreationSettingConsumer.Qualities] = QualityResult(result.Qualities),
            [CharacterCreationSettingConsumer.Magic] = MagicResult(result.Magic.Proposed),
            [CharacterCreationSettingConsumer.Resources] = new { result.Resources.SelectedOption, result.Resources.Budget },
            [CharacterCreationSettingConsumer.Gear] = new { Lines = result.Gear, Budget = result.GearBudget },
            [CharacterCreationSettingConsumer.Lifestyles] = new { result.Lifestyles.Authority, result.Lifestyles.Lifestyles, result.Lifestyles.Budget },
            [CharacterCreationSettingConsumer.Contacts] = new { result.Contacts.Contacts, result.Contacts.ContactBudget,
                result.Contacts.HighPlacesBudget, result.Contacts.AdditionalKarmaCost, result.Contacts.GroupKarma },
            [CharacterCreationSettingConsumer.Loadout] = result.Loadout
        };
        return Project(witness.EffectivePolicy, witness.ProvenanceInputsSnapshotDigest, request.Selections,
            CharacterCreationSelectedPolicyProjection.FromSelected(witness, original, effects).Policy,
            result.Skills, values, true, result.Lifestyles.Authority, result.Lifestyles.Lifestyles, result.Qualities,
            witness.Lifestyles, witness.Magic.MysticAdeptPowerPointPolicy,
            CharacterCreationSelectedPolicyProjection.SkillGroupExecutions(witness.EffectivePolicy, witness.Skills,
                request.Selections, result.Skills, result.Qualities,
                (witness.Magic.Talent.GrantedQualitySources ?? []).Select(row => row.CanonicalSourceXml)));
    }

    // The result fields here are the same common fields checked by MatchesFullReview.
    private static object QualityResult<T>(T value) => Fields(value,
        "Selections", "GrantedQualities", "PositiveQualityBudget", "NegativeQualityBudget", "SourceAnchorIds");

    private static object MagicResult<T>(T value) => Fields(value,
        "Selections", "EffectiveAttributes", "TraditionBudget", "StreamBudget", "AdeptPowerPointBudget",
        "SpellBudget", "ComplexFormBudget", "MysticAdeptPowerPoints", "SourceAnchorIds");

    private static object Fields<T>(T value, params string[] fields)
    {
        var json = JsonSerializer.SerializeToElement(value);
        // Some optional result fields are intentionally omitted in historical shapes.
        return fields.ToDictionary(field => field,
            field => json.TryGetProperty(field, out var item) ? (JsonElement?)item : null);
    }

    private static CharacterCreationPolicyConsumption Project(CharacterCreationEffectivePolicyContext source, string snapshot,
        CharacterCreationCompletionSelections choices, CharacterCreationSelectedConsumerPolicy policy,
        CharacterCreationAttributeSkillsPreview? skills, IReadOnlyDictionary<CharacterCreationSettingConsumer, object?> evaluations, bool successful,
        CharacterCreationLifestylesAuthority? lifestyleAuthority, IReadOnlyList<CharacterCreationLifestyleProjection>? lifestyles,
        CharacterCreationQualitiesCompletionEvaluation? qualities, CharacterCreationLifestylesAuthority sourceLifestyleAuthority,
        CharacterCreationMysticAdeptPowerPointPolicy? settingsSource,
        IReadOnlyList<CharacterCreationActiveSkillKarmaExecution>? skillExecutions)
    {
        var definitions = CharacterCreationSettingDefinitions.ForSchema(source.SettingObservations!.Schema)!;
        var rows = new List<CharacterCreationSettingConsumption>();
        foreach (var observation in source.SettingObservations!.Settings)
        {
            foreach (var binding in Bindings(observation.Key, policy))
            {
                var definition = definitions.SingleOrDefault(row => row.Key == observation.Key);
                bool qualityKarma = observation.Key == CharacterCreationObservedSetting.QualityKarma;
                bool skillGroup = source.SettingObservations.Schema == CharacterCreationSettingsObservation.SchemaV2
                    && observation.Key is CharacterCreationObservedSetting.NewSkillGroup or CharacterCreationObservedSetting.ImproveSkillGroup;
                bool freeGrids = observation.Key == CharacterCreationObservedSetting.FreeGrids
                    && source.SettingObservations.Schema == CharacterCreationSettingsObservation.SchemaV2;
                bool freeGridsSource = freeGrids
                    && CharacterCreationSelectedPolicyProjection.FreeGridsMatchesSource(source, sourceLifestyleAuthority, settingsSource);
                object? evaluation = qualityKarma ? QualityKarmaResult(qualities)
                    : evaluations.GetValueOrDefault(binding.Consumer);
                object? input = qualityKarma ? CharacterCreationSelectedPolicyProjection.QualityKarmaInput(policy.Qualities, qualities)
                    : freeGridsSource ? CharacterCreationSelectedPolicyProjection.FreeGridsInput(policy, sourceLifestyleAuthority)
                    : binding.Input;
                object? suppliedValue = freeGrids ? freeGridsSource ? sourceLifestyleAuthority.FreeGridsActivation!.RawSettingValue : null
                    : binding.Value;
                string? inputValue = suppliedValue is null || definition is null ? null
                    : CharacterCreationSettingDefinitions.ValueDigest(definition.Kind,
                        Convert.ToString(suppliedValue, CultureInfo.InvariantCulture)!);
                var reason = observation.State switch
                {
                    CharacterCreationSettingObservationState.Absent => CharacterCreationSettingConsumptionReason.AbsentSourceDefaultUnproven,
                    CharacterCreationSettingObservationState.Opaque => CharacterCreationSettingConsumptionReason.OpaqueSource,
                    CharacterCreationSettingObservationState.Invalid => CharacterCreationSettingConsumptionReason.InvalidSource,
                    CharacterCreationSettingObservationState.Duplicate => CharacterCreationSettingConsumptionReason.DuplicateSource,
                    _ => inputValue is null ? CharacterCreationSettingConsumptionReason.ConsumerInputUnavailable
                        : inputValue != observation.ValueDigest ? CharacterCreationSettingConsumptionReason.SourceValueMismatch
                        : !successful || evaluation is null
                            ? CharacterCreationSettingConsumptionReason.EvaluationUnavailable
                        : !(skillGroup ? SkillGroupWasUsed(observation.Key, skillExecutions)
                            : qualityKarma ? QualityKarmaWasUsed(qualities)
                            : freeGrids ? FreeGridsWasUsed(sourceLifestyleAuthority, lifestyles)
                                && lifestyleAuthority?.FreeGridsEnabled == sourceLifestyleAuthority.FreeGridsEnabled
                                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                                    sourceLifestyleAuthority.FreeGridsActivation, lifestyleAuthority?.FreeGridsActivation)
                            : WasUsed(observation.Key, binding.Consumer, choices, policy, skills, source, evaluations))
                            ? CharacterCreationSettingConsumptionReason.EvaluatedUseUnavailable
                        : CharacterCreationSettingConsumptionReason.Evaluated
                };
                bool evaluated = reason == CharacterCreationSettingConsumptionReason.Evaluated;
                rows.Add(new(observation.Key, observation.SettingId, observation.SourceValueDigest, binding.Consumer, reason,
                    inputValue, evaluated ? CharacterCreationFinalizationDigest.Compute(new
                    { Schema = CharacterCreationPolicyConsumption.SchemaV1, Consumer = binding.Consumer, Snapshot = snapshot,
                        Policy = input, Selections = choices }) : null,
                    evaluated ? CharacterCreationFinalizationDigest.Compute(new
                    { Schema = CharacterCreationPolicyConsumption.SchemaV1, Consumer = binding.Consumer, Result = evaluation }) : null));
            }
        }
        // These are independent active-graph obligations. Settings accounting never
        // erases selected-effect, grant, prerequisite, amendment or campaign gaps.
        return new(CharacterCreationPolicyConsumption.SchemaV1,
            CharacterCreationFinalizationDigest.Compute(source.SettingObservations), snapshot,
            CharacterCreationFinalizationDigest.Compute(choices), rows.ToArray(),
            ActiveGraphObligations)
        { Effects = EffectEvaluations(snapshot, choices, skills, lifestyleAuthority, lifestyles, successful) };
    }

    // Only a verified active-skill payer can execute these compensation terms.
    // A zero multiplier still executes when its rating weight is nonzero. Zero
    // weight/width, unfunded quotes and unavailable evidence cannot establish use.
    private static bool SkillGroupWasUsed(CharacterCreationObservedSetting key,
        IReadOnlyList<CharacterCreationActiveSkillKarmaExecution>? executions)
        => executions?.Any(row => row.Operation == CharacterCreationSkillKarmaOperation.ActiveSkillPurchase
            && row.Upper > row.Lower && row.GroupCompensation is { } group
            && (key == CharacterCreationObservedSetting.NewSkillGroup
                ? group.NewGroup?.RatingWeight > 0 : group.ImproveGroup.RatingWeight > 0)) == true;

    // Canonical EvaluateSelectionCore includes both selected and valid granted costs.
    // Flags, not origin/name/free labels, decide cap/debit participation. Metagenic-only
    // BP and zero source costs do not establish use, even with a nonzero multiplier.
    internal static bool QualityKarmaWasUsed(CharacterCreationQualitiesCompletionEvaluation? result)
        => result is { Blockers.Count: 0 }
            && (result.Selections.Any(row => row.KarmaCost != 0 && (row.CountsAgainstQualityLimit || row.CountsAgainstKarma))
                || result.GrantedQualities.Any(row => row.KarmaCost != 0 && (row.CountsAgainstQualityLimit || row.CountsAgainstKarma)));

    private static object? QualityKarmaResult(CharacterCreationQualitiesCompletionEvaluation? result)
        => result is null ? null : new
        {
            result.PositiveQualityBudget, result.NegativeQualityBudget,
            result.MetagenicPositiveKarma, result.MetagenicNegativeKarma, result.KarmaRemaining
        };

    internal static bool FreeGridsWasUsed(CharacterCreationLifestylesAuthority? authority,
        IReadOnlyList<CharacterCreationLifestyleProjection>? lifestyles)
    {
        if (authority is not { FreeGridsEnabled: true } || !CharacterCreationLifestylesRules.IsValidAuthority(authority)) return false;
        return lifestyles?.Any(result =>
        {
            var option = authority.LifestyleOptions.SingleOrDefault(row => row.OptionId == result.Configuration.BaseLifestyleOptionId);
            if (option is not { IsSelectable: true, EligibilityIsExact: true, BuiltInQualities.Count: > 0 }
                || result.SourceId != option.SourceId || result.Economics.Blockers.Count != 0
                || option.BuiltInQualities.Any(builtIn => !authority.QualityOptions.Any(row => row.OptionId == builtIn.QualityOptionId))
                || result.LifestyleDigest != CharacterCreationLifestylesRules.ComputeProjectionDigest(result)) return false;
            var expected = CharacterCreationLifestylesRules.AddBuiltInQualities(result.Configuration with { Qualities = [] }, option).Qualities;
            // Require the canonical built-in instances, including their source-selected Extra.
            // A paid duplicate, free label, generic selection or price of zero proves no use.
            return expected.Count > 0 && expected.All(builtIn => result.Configuration.Qualities.Count(row =>
                row.OptionId == builtIn.OptionId && row.Extra == builtIn.Extra) == 1
                && result.Configuration.Qualities.Contains(builtIn));
        }) == true;
    }

    private static IReadOnlyList<CharacterCreationPolicyEffectEvaluation> EffectEvaluations(string snapshot,
        CharacterCreationCompletionSelections choices, CharacterCreationAttributeSkillsPreview? skills,
        CharacterCreationLifestylesAuthority? authority, IReadOnlyList<CharacterCreationLifestyleProjection>? lifestyles, bool successful)
    {
        var rows = new List<CharacterCreationPolicyEffectEvaluation>();
        foreach (var skill in skills?.Skills ?? [])
        {
            if (skill.SkillKarmaPrice is not { } price) continue;
            // These effects are the exact source-compiled occurrences actually
            // submitted to the canonical target price, including native conditions.
            foreach (var effect in price.Effects)
            {
                bool evaluated = successful && (price.Upper > price.Lower || skill.KarmaSpecialization);
                rows.Add(new(CharacterCreationConsumerPolicyOperation.SkillKarmaModifiers,
                    CharacterCreationFinalizationDigest.Compute(effect), CharacterCreationFinalizationDigest.Compute(price.Target),
                    evaluated ? CharacterCreationSettingConsumptionReason.Evaluated : CharacterCreationSettingConsumptionReason.EvaluatedUseUnavailable,
                    CharacterCreationFinalizationDigest.Compute(new { Snapshot = snapshot, Selections = choices, price.Target, price.Lower, price.Upper, price.Effects }),
                    evaluated ? CharacterCreationFinalizationDigest.Compute(price) : null));
            }
        }
        foreach (var effect in authority?.PurchasedQualityCosts?.Occurrences ?? [])
        {
            // No selected lifestyle is an unresolved occurrence, never inferred
            // inapplicability. Basic/non-Standard also stays explicitly unresolved.
            foreach (var lifestyle in lifestyles is { Count: > 0 } ? lifestyles.Cast<CharacterCreationLifestyleProjection?>()
                         : new CharacterCreationLifestyleProjection?[] { null })
            {
                bool evaluated = successful && lifestyle is not null && !lifestyle.Economics.CoveredByTrustFund
                    && lifestyle.Configuration.Increments > 0
                    && (effect.EffectType == CharacterCreationLifestyleCostEffectType.LifestyleCost
                        || lifestyle.Configuration.StyleId == CharacterCreationLifestyleStyleIds.Standard);
                rows.Add(new(CharacterCreationConsumerPolicyOperation.LifestyleCostEffects,
                    CharacterCreationFinalizationDigest.Compute(effect), CharacterCreationFinalizationDigest.Compute(lifestyle?.Configuration.LifestyleId),
                    evaluated ? CharacterCreationSettingConsumptionReason.Evaluated : CharacterCreationSettingConsumptionReason.EvaluatedUseUnavailable,
                    CharacterCreationFinalizationDigest.Compute(new { Snapshot = snapshot, Selections = choices, PurchasedQualityCosts = authority!.PurchasedQualityCosts }),
                    evaluated ? CharacterCreationFinalizationDigest.Compute(lifestyle) : null));
            }
        }
        return rows.ToArray();
    }

    private sealed record Binding(CharacterCreationSettingConsumer Consumer, object? Value, object? Input);

    private static IEnumerable<Binding> Bindings(CharacterCreationObservedSetting key, CharacterCreationSelectedConsumerPolicy p)
    {
        var a = p.Attributes; var s = p.Skills; var q = p.Qualities; var r = p.Resources;
        var c = p.Contacts; var m = p.Magic.MysticAdeptExchange;
        yield return key switch
        {
            CharacterCreationObservedSetting.AttributeKarma => new(CharacterCreationSettingConsumer.Attributes, a.Spending?.KarmaAttribute, a),
            CharacterCreationObservedSetting.MaximumAttributes => new(CharacterCreationSettingConsumer.Attributes, a.Spending?.MaxNumberMaxAttributesCreate, a),
            CharacterCreationObservedSetting.SecondMaximumAttribute => new(CharacterCreationSettingConsumer.Attributes,
                a.Spending?.MaxNumberMaxAttributesCreate is 1 or 2 ? a.Spending.MaxNumberMaxAttributesCreate == 2 : null, a),
            CharacterCreationObservedSetting.AlternateAttributeKarma => new(CharacterCreationSettingConsumer.Attributes, a.Spending?.AlternateMetatypeAttributeKarma, a),
            CharacterCreationObservedSetting.ReverseAttributeOrder => new(CharacterCreationSettingConsumer.Attributes, a.Spending?.ReverseAttributePriorityOrder, a),
            CharacterCreationObservedSetting.KnowledgePoints => new(CharacterCreationSettingConsumer.Skills, s.KnowledgePointsExpression, s),
            CharacterCreationObservedSetting.BrokenGroups => new(CharacterCreationSettingConsumer.Skills, s.UsePointsOnBrokenGroups, s),
            CharacterCreationObservedSetting.StrictGroups => new(CharacterCreationSettingConsumer.Skills, s.StrictSkillGroupsInCreateMode, s),
            CharacterCreationObservedSetting.SpecializationsBreakGroups => new(CharacterCreationSettingConsumer.Skills, s.SpecializationsBreakSkillGroups, s),
            CharacterCreationObservedSetting.NewActiveSkill => new(CharacterCreationSettingConsumer.Skills, s.Purchases?.KarmaNewActiveSkill, s),
            CharacterCreationObservedSetting.ImproveActiveSkill => new(CharacterCreationSettingConsumer.Skills, s.Purchases?.KarmaImproveActiveSkill, s),
            CharacterCreationObservedSetting.NewKnowledgeSkill => new(CharacterCreationSettingConsumer.Skills, s.Purchases?.KarmaNewKnowledgeSkill, s),
            CharacterCreationObservedSetting.ImproveKnowledgeSkill => new(CharacterCreationSettingConsumer.Skills, s.Purchases?.KarmaImproveKnowledgeSkill, s),
            CharacterCreationObservedSetting.NewSkillGroup => new(CharacterCreationSettingConsumer.Skills, s.Purchases?.KarmaNewSkillGroup, s),
            CharacterCreationObservedSetting.ImproveSkillGroup => new(CharacterCreationSettingConsumer.Skills, s.Purchases?.KarmaImproveSkillGroup, s),
            CharacterCreationObservedSetting.ActiveSpecialization => new(CharacterCreationSettingConsumer.Skills, s.Purchases?.KarmaSpecialization, s),
            CharacterCreationObservedSetting.KnowledgeSpecialization => new(CharacterCreationSettingConsumer.Skills, s.Purchases?.KarmaKnowledgeSpecialization, s),
            CharacterCreationObservedSetting.QualityLimit => new(CharacterCreationSettingConsumer.Qualities, q.QualityKarmaLimit, q),
            CharacterCreationObservedSetting.QualityKarma => new(CharacterCreationSettingConsumer.Qualities, q.CostPolicy.KarmaMultiplier, q),
            CharacterCreationObservedSetting.ExceedPositiveQualities => new(CharacterCreationSettingConsumer.Qualities, q.MayExceedPositiveQualityLimit, q),
            CharacterCreationObservedSetting.ExceedNegativeQualities => new(CharacterCreationSettingConsumer.Qualities, q.MayExceedNegativeQualityLimit, q),
            CharacterCreationObservedSetting.ContactPoints => new(CharacterCreationSettingConsumer.Contacts, c.ContactPointsExpression, c),
            CharacterCreationObservedSetting.GroupContactKarma => new(CharacterCreationSettingConsumer.Contacts, c.GroupContactKarmaMultiplier, c),
            CharacterCreationObservedSetting.ResourceKarmaMaximum => new(CharacterCreationSettingConsumer.Resources, r.MaximumKarmaInvestment, r),
            CharacterCreationObservedSetting.ResourceKarmaRate => new(CharacterCreationSettingConsumer.Resources, r.KarmaToNuyenRate, r),
            CharacterCreationObservedSetting.ResourceExpression => new(CharacterCreationSettingConsumer.Resources,
                "{Karma} * " + r.KarmaToNuyenRate.ToString("G29", CultureInfo.InvariantCulture) + " + {PriorityNuyen}", r),
            CharacterCreationObservedSetting.UnrestrictedNuyen => new(CharacterCreationSettingConsumer.Resources, r.UnrestrictedNuyen, r),
            CharacterCreationObservedSetting.MaximumAvailability => new(CharacterCreationSettingConsumer.Gear, p.Gear.MaximumAvailability, p.Gear),
            CharacterCreationObservedSetting.FreeGrids => new(CharacterCreationSettingConsumer.Lifestyles, p.Lifestyles.FreeGridsEnabled, p.Lifestyles),
            CharacterCreationObservedSetting.MysticAdeptKarma => new(CharacterCreationSettingConsumer.Magic, m?.KarmaPerPowerPoint, p.Magic),
            CharacterCreationObservedSetting.MysticAdeptSpellExchange => new(CharacterCreationSettingConsumer.Magic, m?.PrioritySpellsAsPowerPoints, p.Magic),
            CharacterCreationObservedSetting.SeparateAdeptMagic => new(CharacterCreationSettingConsumer.Magic, m?.UsesSeparateMagicAttribute, p.Magic),
            _ => new(CharacterCreationSettingConsumer.None, null, null)
        };
        if (key == CharacterCreationObservedSetting.MaximumAvailability)
            yield return new(CharacterCreationSettingConsumer.Loadout, p.Loadout?.MaximumAvailability, p.Loadout);
    }

    private static bool WasUsed(CharacterCreationObservedSetting key, CharacterCreationSettingConsumer consumer,
        CharacterCreationCompletionSelections choices, CharacterCreationSelectedConsumerPolicy policy, CharacterCreationAttributeSkillsPreview? result,
        CharacterCreationEffectivePolicyContext source, IReadOnlyDictionary<CharacterCreationSettingConsumer, object?> evaluations)
    {
        var rows = result?.Skills ?? [];
        return key switch
        {
            CharacterCreationObservedSetting.AttributeKarma => choices.AttributeAllocations.Any(row => row.KarmaLevels > 0),
            CharacterCreationObservedSetting.AlternateAttributeKarma or CharacterCreationObservedSetting.ReverseAttributeOrder
                => policy.Coverage.Any(row => row.Operation == CharacterCreationConsumerPolicyOperation.AttributeHouseRules
                        && row.Disposition == CharacterCreationConsumerPolicyDisposition.Admitted)
                    && choices.AttributeAllocations.Any(row => row.KarmaLevels > 0),
            CharacterCreationObservedSetting.MaximumAttributes => result?.Attributes.Attributes.Count > 0,
            CharacterCreationObservedSetting.SecondMaximumAttribute => result?.Attributes.Attributes.Count > 0
                && source.SettingObservations!.Settings.Any(row => row.Key == CharacterCreationObservedSetting.MaximumAttributes
                    && row.State == CharacterCreationSettingObservationState.Absent),
            // This expression is evaluated to construct the knowledge budget itself.
            CharacterCreationObservedSetting.KnowledgePoints => result is not null,
            CharacterCreationObservedSetting.BrokenGroups or CharacterCreationObservedSetting.StrictGroups
                => rows.Any(row => row.GroupRating > 0),
            // The skill evaluator only reads this flag for a point-funded
            // specialization on an actually funded group member. Unrelated
            // individual skills, native languages and Karma specializations
            // do not execute that branch merely because a group was selected.
            CharacterCreationObservedSetting.SpecializationsBreakGroups
                => rows.Any(row => row.GroupRating > 0 && row.SpecializationOptionId is not null
                    && !row.KarmaSpecialization),
            CharacterCreationObservedSetting.NewActiveSkill => Purchased(CharacterCreationSkillKinds.Active, first: true),
            CharacterCreationObservedSetting.NewKnowledgeSkill => Purchased(CharacterCreationSkillKinds.Knowledge, first: true),
            CharacterCreationObservedSetting.ImproveActiveSkill => Purchased(CharacterCreationSkillKinds.Active, first: false),
            CharacterCreationObservedSetting.ImproveKnowledgeSkill => Purchased(CharacterCreationSkillKinds.Knowledge, first: false),
            CharacterCreationObservedSetting.ActiveSpecialization => rows.Any(row => row.Kind == CharacterCreationSkillKinds.Active && row.KarmaSpecialization),
            CharacterCreationObservedSetting.KnowledgeSpecialization => rows.Any(row => row.Kind == CharacterCreationSkillKinds.Knowledge && row.KarmaSpecialization),
            CharacterCreationObservedSetting.QualityLimit => choices.QualityOptionIds.Count > 0,
            // The contact consumer computes the allowance, including its attributes,
            // independently of how much of that allowance the character spends.
            CharacterCreationObservedSetting.ContactPoints => result?.Attributes.Attributes.Count > 0,
            CharacterCreationObservedSetting.GroupContactKarma => choices.Contacts.Any(row => row.IsGroup && !row.Free),
            CharacterCreationObservedSetting.ResourceKarmaMaximum or CharacterCreationObservedSetting.ResourceExpression
                or CharacterCreationObservedSetting.UnrestrictedNuyen => choices.TotalKarmaInvestment > 0,
            CharacterCreationObservedSetting.ResourceKarmaRate => choices.TotalKarmaInvestment > 0
                && source.SettingObservations!.Settings.Any(row => row.Key == CharacterCreationObservedSetting.ResourceExpression
                    && row.State == CharacterCreationSettingObservationState.Absent),
            CharacterCreationObservedSetting.MaximumAvailability => consumer == CharacterCreationSettingConsumer.Gear
                ? choices.Gear.Count > 0 : evaluations[consumer] is CharacterCreationLoadoutCompletionProjection { Lines.Count: > 0 },
            CharacterCreationObservedSetting.MysticAdeptKarma or CharacterCreationObservedSetting.MysticAdeptSpellExchange
                => policy.Magic.MysticAdeptExchange is { UsesSeparateMagicAttribute: false }
                    && choices.MagicSelections.MysticAdeptPowerPoints > 0,
            CharacterCreationObservedSetting.SeparateAdeptMagic => policy.Magic.MysticAdeptExchange is { UsesSeparateMagicAttribute: true }
                && result?.Attributes.Attributes.Any(row => string.Equals(row.AttributeId, "MAGAdept", StringComparison.OrdinalIgnoreCase)) == true,
            // Historical FreeGrids rows retain their unproven semantics. V2 is
            // handled above with source-bound activation and actual built-in results.
            _ => false
        };

        bool Purchased(string kind, bool first) => rows.Any(row => row.Kind == kind && row.KarmaLevels > 0
            && (first ? row.Rating - row.KarmaLevels == 0 : row.Rating > 1));
    }
}
