using Chummer.Application.Content;

namespace Chummer.Infrastructure.Xml;

public static class LifeModulesCatalogPathResolver
{
    public static string Resolve(string baseDirectory, string currentDirectory)
    {
        string[] candidates =
        {
            Path.Combine(baseDirectory, "data", "lifemodules.xml"),
            Path.Combine(baseDirectory, "Chummer", "data", "lifemodules.xml"),
            Path.Combine(currentDirectory, "data", "lifemodules.xml"),
            Path.Combine(currentDirectory, "Chummer", "data", "lifemodules.xml")
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate lifemodules.xml.");
    }

    public static string Resolve(IContentOverlayCatalogService overlays)
    {
        ArgumentNullException.ThrowIfNull(overlays);
        return overlays.ResolveDataFile("lifemodules.xml");
    }
}
