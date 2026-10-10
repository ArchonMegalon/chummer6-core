using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

public interface ILifeModulesCatalogService
{
    LifeModuleCatalogAuthorityDto GetAuthority();

    /// <summary>Engine-only immutable source capture for finalization; never a provider/UI payload.</summary>
    byte[]? ReadSourceBytes(string sourceDigest) => null;

    IReadOnlyList<LifeModuleStageDto> GetStages();

    IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null);

    IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(
        string? stage = null,
        IReadOnlyCollection<string>? enabledSources = null);

    /// <summary>
    /// Read-only foundation display needs nationalities and the exact selected
    /// modules for budget validation, not every possible later choice. Catalogs
    /// may filter before projection; the default preserves existing providers.
    /// </summary>
    IReadOnlyList<LifeModuleLegalOptionDto> GetFoundationOptionProjections(
        IReadOnlyCollection<string> selectedModuleIds,
        IReadOnlyCollection<string>? enabledSources = null)
    {
        ArgumentNullException.ThrowIfNull(selectedModuleIds);
        var selected = selectedModuleIds.ToHashSet(StringComparer.Ordinal);
        return GetOptionProjections(enabledSources: enabledSources)
            .Where(module => module.StageOrder == LifeModuleJourneyStageOrders.Nationality
                || selected.Contains(module.ModuleId))
            .ToArray();
    }
}
