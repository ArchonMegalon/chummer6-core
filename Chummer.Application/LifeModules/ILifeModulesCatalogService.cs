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
}
