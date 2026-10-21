using Chummer.Contracts.Characters;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

/// <summary>Two independent projections; neither ever hashes a pruned catalog as full input.</summary>
internal static class CharacterCreationSelectedPolicyProjection
{
    internal static CharacterCreationSelectedPolicyInputs FromFull(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionOriginalSelections original)
    {
        var consumers = inputs.EffectivePolicy!.Consumers!;
        string schema = consumers.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5
            ? CharacterCreationSelectedPolicyInputs.SchemaV3
            : consumers.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV4
                ? CharacterCreationSelectedPolicyInputs.SchemaV2 : CharacterCreationSelectedPolicyInputs.SchemaV1;
        return Seal(FromConsumers(consumers), ObservationDigest(inputs.Skills,
            inputs.Lifestyles.Gear.Resources, inputs.Lifestyles.Authority, inputs.Contacts.BootstrapBinding,
            inputs.Contacts.Policy, inputs.Contacts.CarryoverPolicy, inputs.Contacts.Binding.SourceDigest,
            inputs.Magic.SnapshotDigest, original, inputs.Contacts.OriginalEffects, schema), schema);
    }

    internal static CharacterCreationSelectedPolicyInputs FromSelected(CharacterCreationSelectedCompletionWitness witness,
        CharacterCreationCompletionOriginalSelections original, IReadOnlyList<CharacterContactRuleEffect> effects)
    {
        bool knowledge = witness.Schema == CharacterCreationSelectedCompletionWitness.SchemaV6;
        bool compensation = witness.Schema == CharacterCreationSelectedCompletionWitness.SchemaV5
            || knowledge && witness.Skills.KarmaPurchasePolicy?.CompensateSkillGroupKarmaDifference == true;
        string schema = knowledge ? CharacterCreationSelectedPolicyInputs.SchemaV3
            : compensation ? CharacterCreationSelectedPolicyInputs.SchemaV2 : CharacterCreationSelectedPolicyInputs.SchemaV1;
        var attributes = witness.Skills.Attributes;
        var skills = witness.Skills.Authority;
        var magic = witness.Magic;
        var talent = magic.Talent;
        var qualities = witness.Qualities;
        var resources = witness.Resources;
        var cash = resources.Authority;
        var gear = witness.Gear;
        var lifestyles = witness.Lifestyles;
        var loadout = witness.Loadout;
        var policy = new CharacterCreationSelectedConsumerPolicy(
            new(qualities.ProvenanceAuthorityDigest, qualities.SourceDigest, qualities.ProfileDigest,
                qualities.GmPolicyDigest, qualities.RuntimeDigest, qualities.QualityKarmaLimit,
                qualities.MayExceedPositiveQualityLimit, qualities.MayExceedNegativeQualityLimit,
                qualities.MetagenicLimit, qualities.CostPolicy ?? CharacterCreationQualityCostPolicy.Default),
            witness.ContactsPolicy,
            new(witness.AttributePolicy, attributes.KarmaAttribute, attributes.MaxNumberMaxAttributesCreate,
                attributes.Binding.PrerequisiteAuthorityDigest, attributes.Attributes),
            new(skills.AuthorityDigest, skills.RawProfileInputsDigest, skills.EffectiveSkillsInputsDigest, skills.RuntimeDigest,
                skills.KnowledgePointsExpression, skills.MaxActiveSkillRatingCreate, skills.MaxKnowledgeSkillRatingCreate,
                skills.MaxSkillGroupRatingCreate, skills.BaseNativeLanguageLimit, skills.UsePointsOnBrokenGroups,
                skills.StrictSkillGroupsInCreateMode, skills.SpecializationsBreakSkillGroups,
                witness.Skills.KarmaPurchasePolicy, skills.TalentAccess, witness.Skills.KnowledgePointContributions,
                witness.Skills.MovementCapability, witness.Skills.SelectedActiveSkillPoints, witness.Skills.SelectedSkillGroupPoints),
            new(magic.ProvenanceAuthorityDigest, magic.PrerequisiteAuthorityDigest, magic.SourceInputsDigest,
                magic.CustomDataInputsDigest, magic.GmPolicyDigest, magic.RuntimeDigest,
                talent.Identity, CharacterCreationFinalizationDigest.Compute(talent),
                talent.Magic, talent.Resonance, talent.Depth, talent.SpellBudget, talent.ComplexFormBudget,
                talent.AdeptPowerPointBudget, talent.RequiresTradition, talent.RequiresStream,
                talent.AllowsAdeptPowers, talent.AllowsSpells, talent.AllowsComplexForms, magic.MysticAdeptPowerPointPolicy),
            new(cash.AuthorityDigest, cash.SourceDigest, cash.ProfileDigest, cash.RulesDigest, cash.RuntimeDigest,
                cash.KarmaToNuyenRate, cash.MaximumKarmaInvestment, cash.NuyenCarryover, cash.MaximumAvailability,
                cash.UnrestrictedNuyen, resources.PriorityOption, resources.PurchaseAuthorityExact, resources.KnownPurchaseCost),
            new(gear.ProvenanceAuthorityDigest, gear.SourceDigest, gear.ProfileDigest, gear.RulesDigest, gear.RuntimeDigest,
                gear.MaximumAvailability, gear.MaximumBasketLines, gear.MaximumQuantityPerLine, null),
            new(lifestyles.AuthorityDigest, lifestyles.SourceDigest, lifestyles.ProfileDigest, lifestyles.GmPolicyDigest,
                lifestyles.RuntimeDigest, lifestyles.TrustFundLevel, lifestyles.FreeGridsEnabled, lifestyles.MetatypeCostPercent,
                CharacterCreationFinalizationDigest.Compute(lifestyles.LifestyleOptions),
                CharacterCreationFinalizationDigest.Compute(lifestyles.QualityOptions)),
            loadout is null ? null : new(loadout.AuthorityDigest, loadout.SourceDigest, loadout.ProfileDigest,
                loadout.RulesDigest, loadout.RuntimeDigest, loadout.MaximumAvailability, loadout.MaximumBasketLines,
                loadout.MaximumQuantityPerLine, witness.LoadoutLegalityPolicy),
            CharacterCreationCompletionDomainPolicyProjection.DescribeCoverage(witness.AttributePolicy is not null,
                witness.Skills.KarmaPurchasePolicy is not null, magic.MysticAdeptPowerPointPolicy,
                loadout is not null, witness.LoadoutLegalityPolicy is not null,
                CharacterCreationCompletionConsumerPolicyProjection.DescribeCoverage(
                    witness.EffectivePolicy?.Consumers?.Schema is CharacterCreationCompletionConsumerPolicy.SchemaV3 or CharacterCreationCompletionConsumerPolicy.SchemaV4 or CharacterCreationCompletionConsumerPolicy.SchemaV5),
                attributes.HouseRules is not null
                    && CharacterCreationAttributePolicyAuthority.MatchesHouseRules(witness.AttributePolicy, attributes),
            witness.LoadoutLegalityPolicy?.HasCreationLifecycle(witness.Skills.Binding.RawCharacterXmlDigest) == true,
                compensation ? witness.Skills : null, knowledge));
        return Seal(policy, ObservationDigest(witness.Skills, resources, lifestyles, witness.BootstrapBinding,
            witness.ContactsPolicy, witness.CarryoverPolicy, witness.ContactsSourceProvenanceSeed,
            magic.OriginalInputsSnapshotDigest, original, effects, schema), schema);
    }

    internal static bool IsValid(CharacterCreationSelectedCompletionWitness witness,
        CharacterCreationCompletionOriginalSelections original, IReadOnlyList<CharacterContactRuleEffect> effects)
    {
        if (!witness.HasVersionedShape() || witness.EffectivePolicy is not { Schema: CharacterCreationEffectivePolicyContext.SchemaV2,
                Consumers: { Schema: CharacterCreationCompletionConsumerPolicy.SchemaV2 or CharacterCreationCompletionConsumerPolicy.SchemaV3
                    or CharacterCreationCompletionConsumerPolicy.SchemaV4 or CharacterCreationCompletionConsumerPolicy.SchemaV5,
                    Domains: { } domains } consumers } policy
            || witness.SelectedPolicyInputs is not { } selected
            || consumers.Qualities is null || consumers.Contacts is null || consumers.Coverage is null
            || domains.Attributes is null || domains.Skills is null || domains.Magic is null || domains.Resources is null
            || domains.Gear is null || domains.Lifestyles is null
            || !CharacterCreationFinalizationDigest.IsCanonical(consumers.OriginalInputsDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(domains.Gear.CatalogDigest)
            || domains.Gear.LoadoutLegality is not null
            || domains.Loadout is { } purchase && !CharacterCreationFinalizationDigest.IsCanonical(purchase.CatalogDigest)
            || policy.PolicyDigest != CharacterCreationEffectivePolicyContext.ComputeDigest(policy)) return false;
        if ((policy.SettingObservations is null) != (policy.SelectedConsumption is null)
            || policy.SelectedConsumption is { Schema: not CharacterCreationPolicyConsumption.SchemaV1 }
            || !CharacterCreationPolicyConsumptionProjection.HasValidObservation(policy)
            || !CharacterCreationPolicyConsumptionProjection.MatchesSource(policy, witness.Magic.MysticAdeptPowerPointPolicy)) return false;
        if (policy.SettingObservations?.Schema == CharacterCreationSettingsObservation.SchemaV2
            && witness.Lifestyles.FreeGridsActivation is not null
            && !FreeGridsMatchesSource(policy, witness.Lifestyles, witness.Magic.MysticAdeptPowerPointPolicy)) return false;
        var inventory = policy with { Schema = CharacterCreationEffectivePolicyContext.SchemaV1, Consumers = null, SelectedConsumption = null };
        inventory = inventory with { PolicyDigest = CharacterCreationEffectivePolicyContext.ComputeDigest(inventory) };
        if (!CharacterCreationEffectivePolicyValidation.IsValid(inventory, witness.Skills)) return false;
        if (!CharacterCreationAttributePolicyAuthority.MatchesHouseRules(witness.AttributePolicy, witness.Skills.Attributes))
            return false;
        if (witness.AttributePolicy is { } attribute
            && (attribute.Schema != CharacterCreationAttributePolicy.SchemaV1
                || attribute.SettingsProfileId != policy.SettingsProfileId || attribute.BuildMethod != policy.BuildMethod
                || attribute.RawProfileInputsDigest != policy.ProfileInputsDigest
                || attribute.KarmaAttribute != witness.Skills.Attributes.KarmaAttribute
                || attribute.MaxNumberMaxAttributesCreate != witness.Skills.Attributes.MaxNumberMaxAttributesCreate
                || attribute.SourceAnchorIds is not { Count: > 0 }
                || attribute.AuthorityDigest != CharacterCreationAttributePolicyAuthority.ComputeDigest(attribute))) return false;
        if (witness.LoadoutLegalityPolicy is { } legality
            && (witness.Loadout is null || legality.SettingsProfileId != policy.SettingsProfileId
                || legality.RawProfileInputsDigest != policy.ProfileInputsDigest
                || legality.PolicyDigest != CharacterCreationLoadoutLegalityPolicy.ComputeDigest(legality)
            || legality.Lifecycle is not null && !legality.HasCreationLifecycle(witness.Skills.Binding.RawCharacterXmlDigest)
                || witness.Loadout.RulesDigest != legality.ComputeRulesDigest(witness.Loadout.MaximumAvailability))) return false;
        // Compare actual typed inputs and validated original state, not copied full-input claims.
        var expected = FromSelected(witness, original, effects);
        return CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selected, expected)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(FromConsumers(consumers), expected.Policy);
    }

    internal static CharacterCreationSelectedConsumerPolicy FromConsumers(CharacterCreationCompletionConsumerPolicy consumers)
    {
        var domains = consumers.Domains!;
        return new(consumers.Qualities, consumers.Contacts, domains.Attributes, domains.Skills, domains.Magic,
            domains.Resources, Catalog(domains.Gear), domains.Lifestyles,
            domains.Loadout is null ? null : Catalog(domains.Loadout), consumers.Coverage);
    }

    // Each route supplies its own canonical quality evaluation and resolved policy.
    // Preserve source BP and the actual contribution flags; never scale serialized instances.
    internal static object? QualityKarmaInput(CharacterCreationCompletionQualityPolicy policy,
        CharacterCreationQualitiesCompletionEvaluation? result)
        => result is null ? null : new
        {
            Policy = policy, result.Selections, result.GrantedQualities, result.ProposedAttributeKarmaUsed
        };

    // Rebuild from each route's own authoritative inputs. Point-funded lower bounds,
    // enabled members, payer order and compiled modifiers are reconstructed inside
    // the canonical evaluator; retained execution fields are never its arguments.
    internal static IReadOnlyList<CharacterCreationActiveSkillKarmaExecution>? SkillGroupExecutions(
        CharacterCreationEffectivePolicyContext policy, CharacterCreationSkillsCompletionInputs inputs,
        CharacterCreationCompletionSelections choices, CharacterCreationAttributeSkillsPreview? actual,
        CharacterCreationQualitiesCompletionEvaluation? qualities, IEnumerable<string> grantedQualityXml)
    {
        if (policy.SettingObservations?.Schema != CharacterCreationSettingsObservation.SchemaV2
            || actual is null || actual.Blockers.Count != 0 || choices.SkillKarmaPurchases is null)
            return null;
        CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> replay;
        if (policy.Consumers?.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5)
        {
            if (qualities is null) return null;
            replay = CharacterCreationSkillsService.PreviewCompletionKnowledgePurchases(inputs, choices,
                qualities.Selections, grantedQualityXml, CancellationToken.None, captureExecution: true);
        }
        else
            replay = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs, choices.AttributeAllocations,
                choices.SkillAllocations, choices.GroupAllocations, choices.SkillKarmaPurchases,
                CancellationToken.None, captureExecution: true);
        if (replay.Value is null || replay.Blockers.Count != 0
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(replay.Value, actual))
            return null;
        var executions = new List<CharacterCreationActiveSkillKarmaExecution>();
        foreach (var purchase in choices.SkillKarmaPurchases.Where(row => row.Kind == CharacterCreationSkillKinds.Active))
        {
            var row = actual.Skills.SingleOrDefault(row => row.Kind == purchase.Kind
                && row.SourceSkillId == purchase.SourceSkillId);
            if (row?.SkillKarmaPrice?.Execution is not { } execution
                || row.KarmaCost != execution.EvaluatedCharge) return null;
            executions.Add(execution);
        }
        return executions;
    }

    internal static object FreeGridsInput(CharacterCreationSelectedConsumerPolicy policy,
        CharacterCreationLifestylesAuthority authority)
        => new { Policy = policy.Lifestyles, Activation = authority.FreeGridsActivation };

    // Replay compares supplied inputs to the retained canonical settings and source inventory.
    // The effective flag is not evidence of the raw setting, and a digest is never decoded.
    internal static bool FreeGridsMatchesSource(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationLifestylesAuthority? authority, CharacterCreationMysticAdeptPowerPointPolicy? settingsSource)
    {
        if (authority?.FreeGridsActivation is not { } activation || settingsSource is null
            || !CharacterCreationLifestylesRules.IsValidAuthority(authority)
            || !CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(settingsSource)
            || authority.SettingsProfileId != policy.SettingsProfileId
            || settingsSource.SettingsProfileId != policy.SettingsProfileId
            || authority.ProfileDigest != policy.ProfileInputsDigest
            || authority.AuthorityDigest != policy.Consumers?.Domains?.Lifestyles.AuthorityDigest) return false;
        var sources = policy.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules
            && row.SourceId == "lifestyles").ToArray();
        return sources.Length == 1 && FreeGridsActivationMatchesSource(activation, authority.FreeGridsEnabled,
            settingsSource.CanonicalSourceXml, policy.EnabledSourcebooks, sources[0].ContentDigest);
    }

    internal static bool FreeGridsActivationMatchesSource(CharacterCreationFreeGridsActivation activation,
        bool enabled, string canonicalSettingsXml, IReadOnlyList<string> enabledSourcebooks, string? lifestylesSourceDigest)
    {
        if (activation.SettingsSourceDigest != CharacterCreationFinalizationDigest.Compute(canonicalSettingsXml)
            || !CharacterCreationFinalizationDigest.IsCanonical(activation.LifestylesSourceDigest)
            || activation.LifestylesSourceDigest != lifestylesSourceDigest) return false;
        var settings = XElement.Parse(canonicalSettingsXml);
        if (activation.RawSettingValue != settings.Element("allowfreegrids")?.Value.Trim()) return false;
        bool fromSetting = string.Equals(activation.RawSettingValue, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(activation.RawSettingValue, "yes", StringComparison.OrdinalIgnoreCase)
            || activation.RawSettingValue == "1";
        // Reconstruct the resolver's book input from source bytes as well as the
        // inventory. Resealing a claimed enabled-book list cannot activate a fallback.
        string[] sourceBooks = settings.Element("books")?.Elements("book").Select(row => row.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal).ToArray() ?? [];
        if (!sourceBooks.SequenceEqual(enabledSourcebooks)) return false;
        // This is the same sourcebook rule used by the resolver, not a catalog identity.
        bool fromBook = sourceBooks.Contains("HT", StringComparer.OrdinalIgnoreCase);
        var reason = fromSetting ? CharacterCreationFreeGridsActivationReason.Setting
            : fromBook ? CharacterCreationFreeGridsActivationReason.Sourcebook : CharacterCreationFreeGridsActivationReason.Disabled;
        return activation.SourcebookEnabled == fromBook && activation.Reason == reason
            && enabled == (fromSetting || fromBook);
    }

    private static CharacterCreationSelectedCatalogPolicy Catalog(CharacterCreationCatalogPurchasePolicy policy)
        => new(policy.AuthorityDigest, policy.SourceDigest, policy.ProfileDigest, policy.RulesDigest, policy.RuntimeDigest,
            policy.MaximumAvailability, policy.MaximumBasketLines, policy.MaximumQuantityPerLine, policy.LoadoutLegality);

    private static string ObservationDigest(CharacterCreationSkillsCompletionInputs skills,
        CharacterCreationResourcesCompletionInputs resources, CharacterCreationLifestylesAuthority lifestyles,
        CharacterCreationBootstrapBinding bootstrap, CharacterCreationKarmaContactsPolicy contacts,
        CharacterCreationKarmaCarryoverPolicy carryover, string contactsSource, string magicInputs,
        CharacterCreationCompletionOriginalSelections original, IReadOnlyList<CharacterContactRuleEffect> effects, string schema)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = schema,
            Skills = skills, Resources = resources, Lifestyles = lifestyles, Bootstrap = bootstrap,
            Contacts = contacts, Carryover = carryover, ContactsSource = contactsSource, MagicInputs = magicInputs,
            OriginalSelections = original, OriginalContactEffects = effects
        });

    private static CharacterCreationSelectedPolicyInputs Seal(CharacterCreationSelectedConsumerPolicy policy, string observation, string schema)
    {
        var inputs = new CharacterCreationSelectedPolicyInputs(schema, policy, observation, string.Empty);
        return inputs with { InputsDigest = CharacterCreationSelectedPolicyInputs.ComputeDigest(inputs) };
    }
}
