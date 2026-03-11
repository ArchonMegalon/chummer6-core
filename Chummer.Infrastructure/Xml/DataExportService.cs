using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Tools;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class DataExportService : IDataExportService
{
    private readonly ICharacterFileQueries _characterFileQueries;
    private readonly ICharacterSectionQueries _sectionQueries;

    public DataExportService(
        ICharacterFileQueries characterFileQueries,
        ICharacterSectionQueries sectionQueries)
    {
        _characterFileQueries = characterFileQueries;
        _sectionQueries = sectionQueries;
    }

    public DataExportBundle BuildBundle(CharacterDocument document)
    {
        CharacterFileSummary? summary = SafeParse(() => _characterFileQueries.ParseSummary(document));
        summary ??= BuildFallbackSummary(document);

        CharacterProfileSection? profile = SafeParse(() => ParseSection<CharacterProfileSection>("profile", document));
        CharacterProgressSection? progress = SafeParse(() => ParseSection<CharacterProgressSection>("progress", document));
        CharacterAttributesSection? attributes = SafeParse(() => ParseSection<CharacterAttributesSection>("attributes", document));
        CharacterSkillsSection? skills = SafeParse(() => ParseSection<CharacterSkillsSection>("skills", document));
        CharacterInventorySection? inventory = SafeParse(() => ParseSection<CharacterInventorySection>("inventory", document));
        CharacterQualitiesSection? qualities = SafeParse(() => ParseSection<CharacterQualitiesSection>("qualities", document));
        CharacterContactsSection? contacts = SafeParse(() => ParseSection<CharacterContactsSection>("contacts", document));

        return new DataExportBundle(
            Summary: summary,
            Profile: profile,
            Progress: progress,
            Attributes: attributes,
            Skills: skills,
            Inventory: inventory,
            Qualities: qualities,
            Contacts: contacts);
    }

    private TSection ParseSection<TSection>(string sectionId, CharacterDocument document)
    {
        return (TSection)_sectionQueries.ParseSection(sectionId, document);
    }

    private static T? SafeParse<T>(Func<T> parser) where T : class
    {
        try
        {
            return parser();
        }
        catch
        {
            return null;
        }
    }

    private static CharacterFileSummary BuildFallbackSummary(CharacterDocument document)
    {
        try
        {
            XDocument doc = XDocument.Parse(document.Content, LoadOptions.None);
            XElement? root = doc.Root;
            string name = root?.Element("name")?.Value ?? string.Empty;
            string alias = root?.Element("alias")?.Value ?? string.Empty;
            string metatype = root?.Element("metatype")?.Value ?? string.Empty;
            string buildMethod = root?.Element("buildmethod")?.Value ?? string.Empty;
            decimal karma = decimal.TryParse(root?.Element("karma")?.Value, out decimal parsedKarma) ? parsedKarma : 0m;
            decimal nuyen = decimal.TryParse(root?.Element("nuyen")?.Value, out decimal parsedNuyen) ? parsedNuyen : 0m;
            bool created = bool.TryParse(root?.Element("created")?.Value, out bool parsedCreated) && parsedCreated;

            return new CharacterFileSummary(
                Name: name,
                Alias: alias,
                Metatype: metatype,
                BuildMethod: buildMethod,
                CreatedVersion: string.Empty,
                AppVersion: string.Empty,
                Karma: karma,
                Nuyen: nuyen,
                Created: created);
        }
        catch
        {
            return new CharacterFileSummary(
                Name: string.Empty,
                Alias: string.Empty,
                Metatype: string.Empty,
                BuildMethod: string.Empty,
                CreatedVersion: string.Empty,
                AppVersion: string.Empty,
                Karma: 0m,
                Nuyen: 0m,
                Created: false);
        }
    }
}
