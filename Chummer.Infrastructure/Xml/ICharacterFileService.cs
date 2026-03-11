using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public interface ICharacterFileService
{
    CharacterFileSummary ParseSummaryFromXml(string xml);

    CharacterValidationResult ValidateXml(string xml);

    string ApplyMetadataUpdate(string xml, CharacterMetadataUpdate update);
}
