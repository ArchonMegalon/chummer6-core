using System;
using System.IO;
using System.Linq;

namespace Chummer.Tests;

internal static class LegacyChummer4FixtureCorpus
{
    public static readonly string[] FileNames =
    [
        "sr4-combat-adept.chum4",
        "sr4-hermetic-mage.chum4",
        "sr4-rigger-wheelman.chum4",
        "sr4-street-samurai.chum4",
        "sr4-technomancer-hacker.chum4"
    ];

    public static string ResolvePath(string fileName)
    {
        DirectoryInfo current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (true)
        {
            string candidate = Path.Combine(current.FullName, "Chummer.CoreEngine.Tests", "Fixtures", "Sr4", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (current.Parent is null)
            {
                break;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate legacy Chummer4 fixture.", fileName);
    }

    public static string[] ResolveAllPaths()
        => FileNames.Select(ResolvePath).ToArray();
}
