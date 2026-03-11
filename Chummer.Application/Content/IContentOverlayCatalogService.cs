namespace Chummer.Application.Content;

public interface IContentOverlayCatalogService
{
    ContentOverlayCatalog GetCatalog();

    IReadOnlyList<string> GetDataDirectories();

    IReadOnlyList<string> GetLanguageDirectories();

    string ResolveDataFile(string fileName);
}

public sealed record ContentOverlayCatalog(
    string BaseDataPath,
    string BaseLanguagePath,
    IReadOnlyList<ContentOverlayPack> Overlays);

public static class ContentOverlayModes
{
    public const string ReplaceFile = "replace-file";
    public const string MergeCatalog = "merge-catalog";
}

public sealed record ContentOverlayPack(
    string Id,
    string Name,
    string RootPath,
    string DataPath,
    string LanguagePath,
    int Priority,
    bool Enabled,
    string Mode,
    string Description);
