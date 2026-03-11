using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

public interface ILifeModulesCatalogService
{
    IReadOnlyList<LifeModuleStageDto> GetStages();

    IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null);
}
