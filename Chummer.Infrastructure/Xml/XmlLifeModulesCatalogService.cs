using System.Xml.Linq;
using Chummer.Application.LifeModules;
using Chummer.Contracts.LifeModules;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlLifeModulesCatalogService : ILifeModulesCatalogService
{
    private readonly Lazy<XDocument> _document;

    public XmlLifeModulesCatalogService(string lifeModulesPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lifeModulesPath);
        _document = new Lazy<XDocument>(() => XDocument.Load(lifeModulesPath));
    }

    public IReadOnlyList<LifeModuleStageDto> GetStages()
    {
        return _document.Value.Root!
            .Element("stages")!
            .Elements("stage")
            .Select(stage => new LifeModuleStageDto(
                int.TryParse(stage.Attribute("order")?.Value, out int order) ? order : -1,
                (stage.Value ?? string.Empty).Trim()))
            .OrderBy(stage => stage.Order)
            .ToArray();
    }

    public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null)
    {
        IEnumerable<XElement> modules = _document.Value.Root!
            .Element("modules")!
            .Elements("module");

        if (!string.IsNullOrWhiteSpace(stage))
        {
            string normalizedStage = stage.Trim();
            modules = modules.Where(module =>
                string.Equals((module.Element("stage")?.Value ?? string.Empty).Trim(), normalizedStage, StringComparison.Ordinal));
        }

        return modules.Select(module => new LifeModuleSummaryDto(
            Id: (module.Element("id")?.Value ?? string.Empty).Trim(),
            Stage: (module.Element("stage")?.Value ?? string.Empty).Trim(),
            Name: (module.Element("name")?.Value ?? string.Empty).Trim(),
            Karma: (module.Element("karma")?.Value ?? string.Empty).Trim(),
            Source: (module.Element("source")?.Value ?? string.Empty).Trim(),
            Page: (module.Element("page")?.Value ?? string.Empty).Trim(),
            Story: (module.Element("story")?.Value ?? string.Empty).Trim()))
            .ToArray();
    }
}
