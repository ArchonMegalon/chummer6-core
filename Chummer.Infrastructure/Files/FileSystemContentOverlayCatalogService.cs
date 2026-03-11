using System.Text.Json;
using System.Security.Cryptography;
using Chummer.Application.Content;

namespace Chummer.Infrastructure.Files;

public sealed class FileSystemContentOverlayCatalogService : IContentOverlayCatalogService
{
    private readonly ContentOverlayCatalog _catalog;
    private readonly IReadOnlyList<string> _dataDirectories;
    private readonly IReadOnlyList<string> _languageDirectories;

    public FileSystemContentOverlayCatalogService(string baseDirectory, string currentDirectory, string? configuredAmendsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        string baseDataPath = ResolveBaseDirectory(baseDirectory, currentDirectory, "data");
        string baseLanguagePath = ResolveBaseDirectory(baseDirectory, currentDirectory, "lang");

        List<ContentOverlayPack> overlays = DiscoverOverlayRootDirectories(configuredAmendsPath)
            .Select(BuildOverlayPack)
            .OrderBy(pack => pack.Priority)
            .ThenBy(pack => pack.Id, StringComparer.Ordinal)
            .ToList();

        _catalog = new ContentOverlayCatalog(
            BaseDataPath: baseDataPath,
            BaseLanguagePath: baseLanguagePath,
            Overlays: overlays);

        _dataDirectories = BuildDirectoryList(
            _catalog.BaseDataPath,
            _catalog.Overlays,
            pack => pack.DataPath);

        _languageDirectories = BuildDirectoryList(
            _catalog.BaseLanguagePath,
            _catalog.Overlays,
            pack => pack.LanguagePath);
    }

    public ContentOverlayCatalog GetCatalog() => _catalog;

    public IReadOnlyList<string> GetDataDirectories() => _dataDirectories;

    public IReadOnlyList<string> GetLanguageDirectories() => _languageDirectories;

    public string ResolveDataFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string normalizedName = Path.GetFileName(fileName);
        if (!string.Equals(normalizedName, fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Data file name must not include directory separators.");
        }

        foreach (string directory in BuildResolutionOrder(
                     _catalog.BaseDataPath,
                     _catalog.Overlays,
                     pack => pack.DataPath,
                     pack => string.Equals(pack.Mode, ContentOverlayModes.ReplaceFile, StringComparison.Ordinal)))
        {
            string candidate = Path.Combine(directory, normalizedName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate data file '{normalizedName}'.");
    }

    private static IReadOnlyList<string> BuildDirectoryList(
        string baseDirectory,
        IReadOnlyList<ContentOverlayPack> overlays,
        Func<ContentOverlayPack, string> selector)
    {
        var directories = new List<string>();
        if (Directory.Exists(baseDirectory))
        {
            directories.Add(baseDirectory);
        }

        foreach (ContentOverlayPack pack in overlays)
        {
            if (!pack.Enabled)
            {
                continue;
            }

            string path = selector(pack);
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                continue;
            }

            directories.Add(path);
        }

        return directories;
    }

    private static IEnumerable<string> BuildResolutionOrder(
        string baseDirectory,
        IReadOnlyList<ContentOverlayPack> overlays,
        Func<ContentOverlayPack, string> selector)
    {
        return BuildResolutionOrder(baseDirectory, overlays, selector, pack => true);
    }

    private static IEnumerable<string> BuildResolutionOrder(
        string baseDirectory,
        IReadOnlyList<ContentOverlayPack> overlays,
        Func<ContentOverlayPack, string> selector,
        Func<ContentOverlayPack, bool> predicate)
    {
        foreach (ContentOverlayPack pack in overlays
                     .Where(pack => pack.Enabled && predicate(pack))
                     .OrderByDescending(pack => pack.Priority)
                     .ThenByDescending(pack => pack.Id, StringComparer.Ordinal))
        {
            string path = selector(pack);
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                continue;
            }

            yield return path;
        }

        if (Directory.Exists(baseDirectory))
        {
            yield return baseDirectory;
        }
    }

    private static string ResolveBaseDirectory(string baseDirectory, string currentDirectory, string segment)
    {
        string[] candidates =
        {
            Path.Combine(baseDirectory, segment),
            Path.Combine(baseDirectory, "Chummer", segment),
            Path.Combine(currentDirectory, segment),
            Path.Combine(currentDirectory, "Chummer", segment)
        };

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static IEnumerable<string> DiscoverOverlayRootDirectories(string? configuredAmendsPath)
    {
        if (string.IsNullOrWhiteSpace(configuredAmendsPath))
        {
            yield break;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        char[] separators = BuildAmendsPathSeparators();

        foreach (string rawPath in configuredAmendsPath.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(rawPath);
            if (!Directory.Exists(fullPath))
            {
                continue;
            }

            string manifestPath = Path.Combine(fullPath, "manifest.json");
            bool hasRootManifest = File.Exists(manifestPath);
            bool hasRootContent = Directory.Exists(Path.Combine(fullPath, "data")) || Directory.Exists(Path.Combine(fullPath, "lang"));

            if (hasRootManifest || hasRootContent)
            {
                if (seen.Add(fullPath))
                {
                    yield return fullPath;
                }
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(fullPath, "*", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                string childManifestPath = Path.Combine(childDirectory, "manifest.json");
                if (!File.Exists(childManifestPath))
                {
                    continue;
                }

                string fullChildPath = Path.GetFullPath(childDirectory);
                if (seen.Add(fullChildPath))
                {
                    yield return fullChildPath;
                }
            }
        }
    }

    private static char[] BuildAmendsPathSeparators()
    {
        HashSet<char> separators = [';', ','];
        separators.Add(Path.PathSeparator);
        return separators.ToArray();
    }

    private static ContentOverlayPack BuildOverlayPack(string rootPath)
    {
        ContentOverlayManifest manifest = LoadManifest(rootPath);
        ValidateManifestChecksums(rootPath, manifest.Checksums);

        string id = string.IsNullOrWhiteSpace(manifest.Id)
            ? Path.GetFileName(rootPath)
            : manifest.Id.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = "overlay";
        }

        string name = string.IsNullOrWhiteSpace(manifest.Name)
            ? id
            : manifest.Name.Trim();
        string description = manifest.Description?.Trim() ?? string.Empty;

        string dataPath = Path.Combine(rootPath, "data");
        string languagePath = Path.Combine(rootPath, "lang");

        return new ContentOverlayPack(
            Id: id,
            Name: name,
            RootPath: rootPath,
            DataPath: Directory.Exists(dataPath) ? dataPath : string.Empty,
            LanguagePath: Directory.Exists(languagePath) ? languagePath : string.Empty,
            Priority: manifest.Priority ?? 0,
            Enabled: manifest.Enabled ?? true,
            Mode: NormalizeMode(manifest.Mode),
            Description: description);
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return ContentOverlayModes.ReplaceFile;
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            ContentOverlayModes.MergeCatalog => ContentOverlayModes.MergeCatalog,
            _ => ContentOverlayModes.ReplaceFile
        };
    }

    private static ContentOverlayManifest LoadManifest(string rootPath)
    {
        string manifestPath = Path.Combine(rootPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new ContentOverlayManifest();
        }

        try
        {
            string json = File.ReadAllText(manifestPath);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

            ContentOverlayManifest? manifest = JsonSerializer.Deserialize<ContentOverlayManifest>(json, options);
            return manifest ?? new ContentOverlayManifest();
        }
        catch
        {
            return new ContentOverlayManifest();
        }
    }

    private sealed record ContentOverlayManifest(
        string? Id = null,
        string? Name = null,
        int? Priority = null,
        bool? Enabled = null,
        string? Mode = null,
        string? Description = null,
        IReadOnlyDictionary<string, string>? Checksums = null);

    private static void ValidateManifestChecksums(string rootPath, IReadOnlyDictionary<string, string>? checksums)
    {
        if (checksums is null || checksums.Count == 0)
        {
            return;
        }

        string fullRootPath = Path.GetFullPath(rootPath);
        string rootPrefix = fullRootPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullRootPath
            : fullRootPath + Path.DirectorySeparatorChar;

        foreach ((string relativePath, string checksumValue) in checksums.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new InvalidOperationException("Overlay manifest checksum entry must include a non-empty relative path.");
            }

            string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string candidatePath = Path.GetFullPath(Path.Combine(fullRootPath, normalizedRelativePath));
            if (!candidatePath.StartsWith(rootPrefix, StringComparison.Ordinal) &&
                !string.Equals(candidatePath, fullRootPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Overlay manifest checksum path '{relativePath}' escapes pack root '{rootPath}'.");
            }

            if (!File.Exists(candidatePath))
            {
                throw new InvalidOperationException(
                    $"Overlay manifest checksum path '{relativePath}' does not exist under '{rootPath}'.");
            }

            string expectedChecksum = NormalizeSha256Checksum(checksumValue);
            string actualChecksum = ComputeSha256(candidatePath);
            if (!string.Equals(expectedChecksum, actualChecksum, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Checksum mismatch for '{relativePath}'. Expected '{expectedChecksum}', got '{actualChecksum}'.");
            }
        }
    }

    private static string NormalizeSha256Checksum(string? value)
    {
        string normalized = (value ?? string.Empty).Trim();
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["sha256:".Length..];
        }

        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException(
                $"Invalid checksum format '{value}'. Use a 64-character SHA-256 hex digest or prefix it with 'sha256:'.");
        }

        return normalized.ToLowerInvariant();
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
