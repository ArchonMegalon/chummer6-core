using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Tools;

public interface IDataExportService
{
    DataExportBundle BuildBundle(CharacterDocument document);
}
