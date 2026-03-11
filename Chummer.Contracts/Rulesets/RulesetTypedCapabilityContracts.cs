using Chummer.Contracts.Content;

namespace Chummer.Contracts.Rulesets;

public sealed record TypedCapabilityContractDescriptor(
    string CapabilityId,
    string InputSchemaId,
    string OutputSchemaId,
    bool SessionSafe,
    RulesetGasBudget DefaultGasBudget,
    IReadOnlyList<string> LocalizationKeys);

public static class TypedCapabilitySchemaIds
{
    public const string DeriveAttributeLimitInput = "derive.attribute-limit.input.v1";
    public const string DeriveAttributeLimitOutput = "derive.attribute-limit.output.v1";
    public const string DeriveInitiativeInput = "derive.initiative.input.v1";
    public const string DeriveInitiativeOutput = "derive.initiative.output.v1";
    public const string ValidateChoiceInput = "validate.choice.input.v1";
    public const string ValidateChoiceOutput = "validate.choice.output.v1";
    public const string ValidateCharacterInput = "validate.character.input.v1";
    public const string ValidateCharacterOutput = "validate.character.output.v1";
    public const string AvailabilityItemInput = "availability.item.input.v1";
    public const string AvailabilityItemOutput = "availability.item.output.v1";
    public const string PriceItemInput = "price.item.input.v1";
    public const string PriceItemOutput = "price.item.output.v1";
    public const string FilterChoicesInput = "filter.choices.input.v1";
    public const string FilterChoicesOutput = "filter.choices.output.v1";
    public const string EffectApplyInput = "effect.apply.input.v1";
    public const string EffectApplyOutput = "effect.apply.output.v1";
    public const string BuildLabRecommendationInput = "buildlab.recommendation.input.v1";
    public const string BuildLabRecommendationOutput = "buildlab.recommendation.output.v1";
    public const string SessionQuickActionInput = "session.quickaction.input.v1";
    public const string SessionQuickActionOutput = "session.quickaction.output.v1";
}

public sealed record DeriveAttributeLimitInput(string AttributeId, int BaseValue, IReadOnlyList<string> ActiveModifiers);
public sealed record DeriveAttributeLimitOutput(int FinalValue, IReadOnlyList<RulesetCapabilityDiagnostic> Diagnostics);
public sealed record DeriveInitiativeInput(int Reaction, int Intuition, int InitiativeDice);
public sealed record DeriveInitiativeOutput(int FinalValue, string FormulaKey, IReadOnlyList<RulesetCapabilityDiagnostic> Diagnostics);
public sealed record ValidateChoiceInput(string ChoiceId, string CharacterId, IReadOnlyDictionary<string, RulesetCapabilityValue> Context);
public sealed record ValidateChoiceOutput(bool IsLegal, IReadOnlyList<RulesetCapabilityDiagnostic> Diagnostics);
public sealed record ValidateCharacterInput(string CharacterId, IReadOnlyList<string> ValidationScopes);
public sealed record ValidateCharacterOutput(bool IsValid, IReadOnlyList<RulesetCapabilityDiagnostic> Diagnostics);
public sealed record AvailabilityItemInput(string ItemId, string CharacterId);
public sealed record AvailabilityItemOutput(int AvailabilityValue, IReadOnlyList<RulesetCapabilityDiagnostic> Diagnostics);
public sealed record PriceItemInput(string ItemId, string CharacterId, int Quantity);
public sealed record PriceItemOutput(decimal Price, string Currency, IReadOnlyList<RulesetCapabilityDiagnostic> Diagnostics);
public sealed record FilterChoicesInput(string CatalogId, string CharacterId, IReadOnlyList<string> CandidateIds);
public sealed record FilterChoicesOutput(IReadOnlyList<string> EnabledIds, IReadOnlyDictionary<string, DisabledReasonPayload> DisabledReasons);
public sealed record EffectApplyInput(string EffectId, string CharacterId, IReadOnlyDictionary<string, RulesetCapabilityValue> Context);
public sealed record EffectApplyOutput(bool Applied, IReadOnlyList<RulesetCapabilityDiagnostic> Diagnostics);
public sealed record BuildLabRecommendationInput(string CharacterId, int KarmaBudget, IReadOnlyList<string> DesiredRoleTags);
public sealed record BuildLabRecommendationOutput(IReadOnlyList<string> RecommendationIds, IReadOnlyList<RulesetCapabilityDiagnostic> Diagnostics);
public sealed record SessionQuickActionInput(string ActionId, string CharacterId, IReadOnlyDictionary<string, RulesetCapabilityValue> Context);
public sealed record SessionQuickActionOutput(bool Allowed, DisabledReasonPayload? DisabledReason, IReadOnlyList<RulesetCapabilityDiagnostic> Diagnostics);

public static class RulesetTypedCapabilityCatalog
{
    public static readonly IReadOnlyList<TypedCapabilityContractDescriptor> Descriptors =
    [
        new(
            RulePackCapabilityIds.DeriveAttributeLimit,
            TypedCapabilitySchemaIds.DeriveAttributeLimitInput,
            TypedCapabilitySchemaIds.DeriveAttributeLimitOutput,
            SessionSafe: true,
            DefaultGasBudget: new RulesetGasBudget(500, 300, 10),
            LocalizationKeys: ["ruleset.capability.derive.attribute-limit.title"]),
        new(
            RulePackCapabilityIds.DeriveInitiative,
            TypedCapabilitySchemaIds.DeriveInitiativeInput,
            TypedCapabilitySchemaIds.DeriveInitiativeOutput,
            SessionSafe: true,
            DefaultGasBudget: new RulesetGasBudget(500, 300, 10),
            LocalizationKeys: ["ruleset.capability.derive.initiative.title"]),
        new(
            RulePackCapabilityIds.ValidateChoice,
            TypedCapabilitySchemaIds.ValidateChoiceInput,
            TypedCapabilitySchemaIds.ValidateChoiceOutput,
            SessionSafe: false,
            DefaultGasBudget: new RulesetGasBudget(1_000, 500, 10),
            LocalizationKeys: ["ruleset.capability.validate.choice.title"]),
        new(
            RulePackCapabilityIds.ValidateCharacter,
            TypedCapabilitySchemaIds.ValidateCharacterInput,
            TypedCapabilitySchemaIds.ValidateCharacterOutput,
            SessionSafe: false,
            DefaultGasBudget: new RulesetGasBudget(2_000, 1_000, 25),
            LocalizationKeys: ["ruleset.capability.validate.character.title"]),
        new(
            RulePackCapabilityIds.AvailabilityItem,
            TypedCapabilitySchemaIds.AvailabilityItemInput,
            TypedCapabilitySchemaIds.AvailabilityItemOutput,
            SessionSafe: true,
            DefaultGasBudget: new RulesetGasBudget(800, 500, 10),
            LocalizationKeys: ["ruleset.capability.availability.item.title"]),
        new(
            RulePackCapabilityIds.PriceItem,
            TypedCapabilitySchemaIds.PriceItemInput,
            TypedCapabilitySchemaIds.PriceItemOutput,
            SessionSafe: true,
            DefaultGasBudget: new RulesetGasBudget(800, 500, 10),
            LocalizationKeys: ["ruleset.capability.price.item.title"]),
        new(
            RulePackCapabilityIds.FilterChoices,
            TypedCapabilitySchemaIds.FilterChoicesInput,
            TypedCapabilitySchemaIds.FilterChoicesOutput,
            SessionSafe: false,
            DefaultGasBudget: new RulesetGasBudget(1_500, 800, 10),
            LocalizationKeys: ["ruleset.capability.filter.choices.title"]),
        new(
            RulePackCapabilityIds.EffectApply,
            TypedCapabilitySchemaIds.EffectApplyInput,
            TypedCapabilitySchemaIds.EffectApplyOutput,
            SessionSafe: true,
            DefaultGasBudget: new RulesetGasBudget(1_000, 600, 10),
            LocalizationKeys: ["ruleset.capability.effect.apply.title"]),
        new(
            RulePackCapabilityIds.BuildLabRecommendation,
            TypedCapabilitySchemaIds.BuildLabRecommendationInput,
            TypedCapabilitySchemaIds.BuildLabRecommendationOutput,
            SessionSafe: false,
            DefaultGasBudget: new RulesetGasBudget(2_500, 1_200, 25),
            LocalizationKeys: ["ruleset.capability.buildlab.recommendation.title"]),
        new(
            RulePackCapabilityIds.SessionQuickActions,
            TypedCapabilitySchemaIds.SessionQuickActionInput,
            TypedCapabilitySchemaIds.SessionQuickActionOutput,
            SessionSafe: true,
            DefaultGasBudget: new RulesetGasBudget(500, 250, 10),
            LocalizationKeys: ["ruleset.capability.session.quickaction.title"])
    ];
}
