namespace Chummer.Contracts.LifeModules;

public sealed record LifeModuleStageDto(
    int Order,
    string Name);

public sealed record LifeModuleSummaryDto(
    string Id,
    string Stage,
    string Name,
    string Karma,
    string Source,
    string Page,
    string Story);
