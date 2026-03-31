using System;
using System.IO;
using System.Linq;

namespace Chummer.Tests;

internal static class LegacyChummer5FixtureCorpus
{
    public static readonly string[] FileNames =
    [
        "Apex Predator.chum5",
        "BLUE.chum5",
        "Barrett.chum5",
        "Bastion.chum5",
        "Blindfire.chum5",
        "Davis Jones.chum5",
        "Draught.chum5",
        "Fuzzy-chargen.chum5",
        "Gangerbean.chum5",
        "Gentle Earthquake.chum5",
        "Ghile Mear.chum5",
        "Glessner.chum5",
        "Harmony.chum5",
        "Miko.chum5",
        "Mittens Chargen.chum5",
        "Monomax (approved) 3.chum5",
        "Munin.chum5",
        "Munin_Career.chum5",
        "Ocelot2.0.chum5",
        "Pañcama.chum5",
        "Popstar.chum5",
        "Rez0luti0n2.0.chum5",
        "SCSi.chum5",
        "Serpent.chum5",
        "Skink.chum5",
        "Soma (Career).chum5",
        "Soma.chum5",
        "Spirit_Warden.chum5",
        "Tenshi.chum5",
        "Ushi Resub.chum5",
        "Wesson.chum5",
        "Yeti-#ffffff2.chum5",
        "prime.chum5",
        "resub.chum5"
    ];

    public static string ResolvePath(string fileName)
    {
        DirectoryInfo current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (true)
        {
            string candidate = Path.Combine(current.FullName, "Chummer.Tests", "TestFiles", fileName);
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

        throw new FileNotFoundException("Could not locate legacy Chummer5 fixture.", fileName);
    }

    public static string[] ResolveAllPaths()
        => FileNames.Select(ResolvePath).ToArray();
}
