using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Opt-in semantic adapter over the existing strict planner, with no second
/// feasibility engine, provider invocation or mutation authority. Physical purchases
/// and typed initial loading use the same strict candidate/finalization path.</summary>
public static partial class CharacterCreationRoleCompletionPlanner
{
    public const string InvalidBrief = "creation-role-brief-invalid";
    public const string SourceUnavailable = "creation-role-source-unavailable";

    public static CharacterCreationFoundationResult<CharacterCreationRoleCompletionCandidate> PlanComplete(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationRoleCompletionRequest request,
        WorkspaceStoredDocument original, CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationStartingNuyenSource startingCashSource, CharacterCreationStartingCashChoice? cashChoice,
        CharacterCreationRuleSourceExecutionContext ruleSources,
        CancellationToken cancellationToken = default, CharacterCreationCampaignExecutionContext? campaign = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ruleSources);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Valid(request)) return new(CharacterCreationFoundationOutcomes.Invalid, null, [InvalidBrief]);
        // Lists supplied by a transport must not change the brief while the solver runs.
        request = JsonSerializer.Deserialize<CharacterCreationRoleCompletionRequest>(
            JsonSerializer.SerializeToUtf8Bytes(request))!;
        if (!Valid(request)) return new(CharacterCreationFoundationOutcomes.Invalid, null, [InvalidBrief]);
        var result = CharacterCreationCompletionPlanner.PlanCompleteBaseline(inputs,
            new(request.Brief.Plan, request.MaximumCandidateEvaluations, request.PermitCatalogMagicCompletion),
            original, bootstrap, startingCashSource, cashChoice, ruleSources, cancellationToken, campaign);
        if (result.Outcome != CharacterCreationFoundationOutcomes.Success || result.Value is not { } baseline)
            return new(result.Outcome, null, result.Blockers);
        // Resolve the same privately retained capture against the original again. Neither
        // a client-supplied sheet nor a public source fingerprint substitutes for custody.
        var source = ruleSources.Resolve(inputs, baseline.Plan.Review.Request, cancellationToken);
        if (source is null) return new(CharacterCreationFoundationOutcomes.Conflict, null, [SourceUnavailable]);
        try
        {
            if (request.Brief.Routines.Any(r => r.Kind == CharacterCreationRoleRoutineKind.PhysicalAttack
                && r.Action?.PhysicalAttack is null))
                return PlanPhysicalLoadout3847(inputs, request, original, bootstrap, startingCashSource,
                    cashChoice, ruleSources, baseline, source, cancellationToken, campaign);
            var assessment = source.AssessRoleObjectives(request, baseline, cancellationToken);
            return new(CharacterCreationFoundationOutcomes.Success, new(request, baseline, assessment), []);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or OverflowException or System.Xml.XmlException or FormatException)
        { return new(CharacterCreationFoundationOutcomes.Blocked, null, [SourceUnavailable]); }
    }

    internal static bool Valid(CharacterCreationRoleCompletionRequest request)
    {
        if (request.Brief is not { } b || b.Schema != CharacterCreationRoleObjectiveSchemas.BriefV1
            || b.Plan is null || !Enum.IsDefined(b.PrimaryRole)
            || b.SecondaryRole is { } second && (!Enum.IsDefined(second) || second == b.PrimaryRole)
            || b.Concept is null || b.Concept.Length > 2048
            || b.Routines is null || b.Routines.Count is < 1 or > 16
            || request.MaximumCandidateEvaluations is < 1 or > CharacterCreationCompletionPlanner.MaximumBaselineCandidateEvaluations)
            return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in b.Routines)
        {
            if (r?.MagicIdentity is { } magic && (string.IsNullOrWhiteSpace(magic.Kind) || magic.Kind.Length > 64
                || !Guid.TryParse(magic.SourceId, out var magicId) || magicId == Guid.Empty)) return false;
            if (r?.Action?.PhysicalAttack is { } physical && (r.Kind != CharacterCreationRoleRoutineKind.PhysicalAttack
                || physical.WeaponId == Guid.Empty || physical.WeaponSourceId == Guid.Empty
                || physical.AmmunitionId == Guid.Empty || physical.AmmunitionSourceId == Guid.Empty
                || !CharacterCreationFinalizationDigest.IsCanonical(physical.WeaponSourceDigest)
                || !CharacterCreationFinalizationDigest.IsCanonical(physical.AmmunitionSourceDigest))) return false;
            if (r?.Action is { } action && (action.ForceOrLevel is < 1 or > 1000
                || action.Target is null || action.Target.Length > 512
                || action.AccessMode is null || action.AccessMode.Length > 128
                || action.VehicleSourceId is not null && (!Guid.TryParse(action.VehicleSourceId, out var vehicle) || vehicle == Guid.Empty)))
                return false;
            if (r is null || r.Schema != CharacterCreationRoleObjectiveSchemas.RoutineV1
                || string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 128 || !ids.Add(r.Id)
                || !Enum.IsDefined(r.Kind) || r.Role != b.PrimaryRole && r.Role != b.SecondaryRole
                || !Guid.TryParse(r.SkillSourceId, out var skill) || skill == Guid.Empty
                || r.Conditions is not { } c || c.SustainedEffects is < 0 or > 100
                || c.Preparation is null || c.Preparation.Length > 512
                || c.Situation is null || c.Situation.Length > 512
                || r.Targets is not { } t || t.MinimumSkillDicePool is < 1 or > 1000
                || t.MinimumDefensePool is < 0 or > 1000 || t.MinimumResistancePool is < 0 or > 1000
                || t.MinimumLimit is < 0 or > 1000 || t.MinimumGroundWalkMeters is < 0 or > 100000
                || t.MaximumDamageProbability is < 0 or > 1
                || (t.Limit is null) != (t.MinimumLimit is null)
                || t.Limit is { } limit && !Enum.IsDefined(limit)
                || r.RequiredEquipmentSourceIds is null || r.RequiredEquipmentSourceIds.Count > 64
                || r.RequiredEquipmentSourceIds.Any(id => !Guid.TryParse(id, out var g) || g == Guid.Empty)
                || r.RequiredEquipmentSourceIds.Select(Guid.Parse).Distinct().Count() != r.RequiredEquipmentSourceIds.Count)
                return false;
        }
        return b.Routines.Any(r => r.Role == b.PrimaryRole && !r.IsFallback)
            && (b.SecondaryRole is null || b.Routines.Any(r => r.Role == b.SecondaryRole && !r.IsFallback));
    }
}
