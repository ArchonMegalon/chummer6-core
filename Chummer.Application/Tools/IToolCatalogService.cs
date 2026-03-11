using Chummer.Contracts.Api;

namespace Chummer.Application.Tools;

public interface IToolCatalogService
{
    MasterIndexResponse GetMasterIndex();

    TranslatorLanguagesResponse GetTranslatorLanguages();
}
