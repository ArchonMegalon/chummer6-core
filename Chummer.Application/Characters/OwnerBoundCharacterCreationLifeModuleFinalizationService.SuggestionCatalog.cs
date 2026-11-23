using System.Text.Json;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Characters;

public sealed partial class OwnerBoundCharacterCreationLifeModuleFinalizationService
{
    public CharacterCreationLifeModuleCatalogProjection? LoadSuggestionCatalog(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationState expectedFoundation,
        string? expectedEnvironmentDigest = null)
    {
        try
        {
            if (!expectedOwner.IsValid
                || expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser
                || expectedFoundation?.Binding is null
                || store is not ICharacterCreationCurrentLifeModuleFinalizationCapability capability) return null;
            var expected = JsonSerializer.SerializeToElement(expectedFoundation)
                .Deserialize<CharacterCreationFoundationState>()!;
            var result = capability.LoadCurrentLifeModuleSuggestionCatalog(ownerContext, expectedOwner,
                expected, characterFiles, sourceResolver, catalog);
            if (result is null || !MatchesFoundation(result, expected)
                || expectedEnvironmentDigest is not null && result.EnvironmentDigest != expectedEnvironmentDigest
                || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease)) return null;
            using (lease) return lease.Stamp == expectedOwner ? result : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }

    /// <summary>Isolated native projection, like ReadNativeState. The calling
    /// store retains owner/workspace leases and validates the environment before
    /// and after this read; this helper alone grants no admission authority.</summary>
    public static CharacterCreationLifeModuleCatalogProjection? ReadNativeSuggestionCatalog(
        WorkspaceStoredDocument workspace, ICharacterFileQueries queries,
        ICharacterSourceDataResolver resolver, ILifeModulesCatalogService modules,
        CharacterCreationFoundationState expectedFoundation, string environmentDigest)
    {
        if (expectedFoundation?.Binding is null || string.IsNullOrWhiteSpace(environmentDigest)) return null;
        var authority = modules.GetAuthority();
        if (authority.Schema != LifeModuleJourneySchemas.CatalogAuthorityV1
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(authority.RawXmlDigest)) return null;
        var loaded = ReadNativeState(workspace, queries, resolver, modules);
        if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success || loaded.Value is not { } state
            || state.RulesetId != RulesetDefaults.Sr5 || state.CharacterCreated
            || state.BuildMethod != CharacterCreationBuildMethods.LifeModules
            || state.AuthorityBlockers.Count != 0
            || !Same(state.Binding, expectedFoundation.Binding)
            || state.SnapshotDigest != expectedFoundation.SnapshotDigest) return null;

        // Use the same catalog instance and enabled sources that produced the
        // admitted foundation. Null would expose disabled sources. Full options
        // preserve follow-ups and accepted-fact anchors beyond Nationality.
        var options = modules.GetOptionProjections(enabledSources: state.Binding.EnabledSources);
        var result = new CharacterCreationLifeModuleCatalogProjection(state.Binding,
            state.SnapshotDigest, authority.RawXmlDigest, environmentDigest, options);
        if (!Same(authority, modules.GetAuthority()) || !MatchesFoundation(result, state)) return null;
        // Detach nested DTO collections from the catalog's shared projection cache.
        return JsonSerializer.SerializeToElement(result)
            .Deserialize<CharacterCreationLifeModuleCatalogProjection>();
    }

    private static bool MatchesFoundation(CharacterCreationLifeModuleCatalogProjection projection,
        CharacterCreationFoundationState foundation)
        => foundation.RulesetId == RulesetDefaults.Sr5 && !foundation.CharacterCreated
            && foundation.BuildMethod == CharacterCreationBuildMethods.LifeModules
            && foundation.AuthorityBlockers.Count == 0
            && Same(projection.Binding, foundation.Binding)
            && projection.FoundationSnapshotDigest == foundation.SnapshotDigest
            && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(projection.CatalogSourceDigest)
            && !string.IsNullOrWhiteSpace(projection.EnvironmentDigest)
            && projection.Options is not null
            && Same(projection.Options.Where(option => option.StageOrder == LifeModuleJourneyStageOrders.Nationality).ToArray(),
                foundation.NationalityOptions);

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
}
