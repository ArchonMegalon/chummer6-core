using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Characters;

/// <summary>Advisory display data, never an eligibility, acceptance or mutation
/// capability. Consumers must re-read through the owner-bound service before
/// use and compare EnvironmentDigest across asynchronous work.</summary>
public sealed record CharacterCreationLifeModuleCatalogProjection(
    CharacterCreationFoundationBinding Binding,
    string FoundationSnapshotDigest,
    string CatalogSourceDigest,
    string EnvironmentDigest,
    IReadOnlyList<LifeModuleLegalOptionDto> Options);
