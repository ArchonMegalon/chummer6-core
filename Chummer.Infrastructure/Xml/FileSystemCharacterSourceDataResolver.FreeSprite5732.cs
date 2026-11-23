using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private static void RequireLivePathAbsent5836(string path)
    {
        // The caller has already obtained a false existence observation. Retained
        // reads require that exact observation in the archive; never probe the host.
        if (ActiveSourceInputs.Value is { IsLiveCapture5732: false }) return;
        // File/Directory.Exists can suppress access errors. Only a known missing
        // path may become an absence fact; this probe never reads source bytes.
        try { _ = File.GetAttributes(path); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        throw new IOException("Optional source input absence could not be established.");
    }

    private sealed partial class SourceInputSnapshot
    {
        private bool _captureFreeSprite5732;
        private string LiveFreeSpriteDependencyRecipe5732 => _captureFreeSprite5732
            ? RetainedCharacterRuleSourceInputs.WithFreeSpriteRecipe5732(LiveGradeDependencyRecipe5719)
            : LiveGradeDependencyRecipe5719;

        // An old closed archive keeps its original inputs and read-only recovery.
        // A later conversion must still refuse any dependency it did not retain.
        public bool IsLiveCapture5732 => _retained is null;
        public bool CaptureFreeSpriteInputs5732(bool selected)
            => _retained is not null
                ? RetainedCharacterRuleSourceInputs.HasFreeSpriteRecipe5732(_retained.DependencyRecipe1244)
                : selected;
        public void ObserveFreeSpriteInputs5732() => _captureFreeSprite5732 = true;
    }

    private sealed partial class SourceDataContext
    {
        private bool CaptureFreeSpriteDependencies5732()
        {
            // Native Critter-tab Sprites already retain these through 5751. A finite
            // metatypes.xml source can also own conversion without that tab/catalog.
            // Category only selects bytes to retain; the owner graph still proves
            // the selected metatype and exact Denial source before granting anything.
            bool selected = !SelectsSpriteCatalog5751
                && _buildMethod is (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
                && _character.Elements("created").ToArray() is [var created]
                && !created.HasAttributes && !created.HasElements && bool.TryParse(created.Value, out bool finished) && finished
                && _character.Elements("metatypecategory").ToArray() is [var category]
                && !category.HasAttributes && !category.HasElements
                && (category.Value.EndsWith("Sprites", StringComparison.Ordinal)
                    || category.Value == CharacterFreeSpriteConversionRules.FreeSpriteCategory);
            if (!_sourceInputs.CaptureFreeSpriteInputs5732(selected)) return true;

            const string file = "critterpowers.xml";
            // Retain genuine absence without inventing a digest or source authority.
            // A policy/read failure is not absence and must still reject capture.
            if ((!TryComputeEffectiveInputDigest(_catalog, file, out _, out bool inputsAbsent, requireKnownAbsence: true) && !inputsAbsent)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, file, out _)) return false;
            // Keep the exact query, even when empty, along with base/custom/amend
            // bytes. Cold source resolution cannot substitute an ambient catalog.
            foreach (CustomDirectory directory in _customDirectories)
                _ = EnumerateSourceFiles(directory.Path, $"*_{file}", SearchOption.AllDirectories);
            _sourceInputs.ObserveFreeSpriteInputs5732();
            return true;
        }
    }
}
