namespace Chummer.Infrastructure.Xml;

internal sealed partial class RetainedCharacterRuleSourceInputs
{
    // These identifiers describe dependency observations, never consumer permission.
    // Absence of a recipe header means the exact historical recipe, including on V4 replay.
    internal const string LegacyDependencyRecipe1244 = "chummer-private-dependencies-legacy";
    internal const string EssenceDependencyRecipe1244 = "chummer-private-dependencies-essence-v1";
    internal const string LifeModuleDependencyRecipe2040 = "chummer-private-dependencies-lifemodules-v1";
    internal const string EssenceLifeModuleDependencyRecipe2040 = "chummer-private-dependencies-essence-lifemodules-v1";
    // A successful live workshop read selects this enlarged observation recipe.
    // The serialized name preserves the underlying recipe and never upgrades old archives.
    private const string VehicleRecipePrefix2330 = "chummer-private-dependencies-vehicle-workshop-v1:";
    internal static bool HasVehicleWorkshopRecipe2330(string recipe)
        => recipe.StartsWith(VehicleRecipePrefix2330, StringComparison.Ordinal);
    internal static string WithoutVehicleWorkshopRecipe2330(string recipe)
        => HasVehicleWorkshopRecipe2330(recipe) ? recipe[VehicleRecipePrefix2330.Length..] : recipe;
    internal static string WithVehicleWorkshopRecipe2330(string recipe)
        => ValidateDependencyRecipe1244(VehicleRecipePrefix2330 + recipe);
    internal string BaseDependencyRecipe2330 => BaseDependencyRecipe2193(DependencyRecipe1244);

    private const string LocalRecipeFormat1244 = "chummer-private-rule-inputs-local-v2";
    private const string PortableRecipeFormat1244 = "chummer-private-rule-inputs-v4";
    // These private formats bind the Focus catalog projection independently of the
    // dependency recipe. The four historical formats keep their original bytes.
    private const string LocalFocusFormat1339 = "chummer-private-rule-inputs-local-v3";
    private const string PortableFocusFormat1339 = "chummer-private-rule-inputs-v5";

    private const string LocalCompatibilityFormat = "chummer-private-rule-inputs-local-v4";
    private const string PortableCompatibilityFormat = "chummer-private-rule-inputs-v6";

    internal string DependencyRecipe1244 { get; }

    // New captures retain martial dependencies; old archive recipes replay unchanged.
    internal const string MartialDependencySuffix2193 = "+martial-v1";
    // V2 also replays the typed martial defaults and knowledge-neutral saved-effect
    // classification. Keep V1's policy projection and observation recipe unchanged.
    internal const string MartialDependencySuffix2421 = "+martial-v2";
    internal static string BaseDependencyRecipe2193(string recipe)
    {
        recipe = WithoutVehicleWorkshopRecipe2330(recipe);
        recipe = WithoutFreeSpriteRecipe5732(recipe);
        if (recipe.StartsWith(CurrentGradesRecipePrefix5719, StringComparison.Ordinal))
            recipe = recipe[CurrentGradesRecipePrefix5719.Length..];
        return recipe.EndsWith(MartialDependencySuffix2421, StringComparison.Ordinal)
            ? recipe[..^MartialDependencySuffix2421.Length]
            : recipe.EndsWith(MartialDependencySuffix2193, StringComparison.Ordinal)
            ? recipe[..^MartialDependencySuffix2193.Length] : recipe;
    }

    // An additive dependency prefix, not a rule/transaction capability. Vehicle
    // remains the outer prefix and martial remains the suffix of the base recipe.
    // Finite Sprite inputs, when present, sit between vehicle and current grades.
    private const string CurrentGradesRecipePrefix5719 = "chummer-private-dependencies-current-grades-v1:";
    internal static bool HasCurrentGradesRecipe5719(string recipe)
        => WithoutFreeSpriteRecipe5732(WithoutVehicleWorkshopRecipe2330(recipe))
            .StartsWith(CurrentGradesRecipePrefix5719, StringComparison.Ordinal);
    internal static string WithCurrentGradesRecipe5719(string recipe)
        => ValidateDependencyRecipe1244(CurrentGradesRecipePrefix5719 + recipe);

    // Only a completed live finite-source observation selects this additive recipe.
    // Unmarked historical archives keep their exact original dependency closure.
    private const string FreeSpriteRecipePrefix5732 = "chummer-private-dependencies-free-sprite-v1:";
    internal static bool HasFreeSpriteRecipe5732(string recipe)
        => WithoutVehicleWorkshopRecipe2330(recipe).StartsWith(FreeSpriteRecipePrefix5732, StringComparison.Ordinal);
    private static string WithoutFreeSpriteRecipe5732(string recipe)
        => recipe.StartsWith(FreeSpriteRecipePrefix5732, StringComparison.Ordinal)
            ? recipe[FreeSpriteRecipePrefix5732.Length..] : recipe;
    internal static string WithFreeSpriteRecipe5732(string recipe)
        => ValidateDependencyRecipe1244(FreeSpriteRecipePrefix5732 + recipe);

    private static string ValidateDependencyRecipe1244(string recipe)
        => BaseDependencyRecipe2193(recipe) is LegacyDependencyRecipe1244 or EssenceDependencyRecipe1244
            or LifeModuleDependencyRecipe2040 or EssenceLifeModuleDependencyRecipe2040 ? recipe : throw Rejected();

    private void WriteCaptureHeader1244(BinaryWriter writer)
    {
        if (CompatibilityProfile is not null)
        {
            WriteText(writer, _portable ? PortableCompatibilityFormat : LocalCompatibilityFormat);
            if (_portable) writer.Write((byte)SourcePathSeparator);
            WriteText(writer, DependencyRecipe1244);
            writer.Write(NativeFocusGearCatalog1339);
            WriteText(writer, CompatibilityProfile);
            return;
        }
        bool versioned = NativeFocusGearCatalog1339 || DependencyRecipe1244 != LegacyDependencyRecipe1244;
        WriteText(writer, NativeFocusGearCatalog1339 ? (_portable ? PortableFocusFormat1339 : LocalFocusFormat1339)
            : versioned ? (_portable ? PortableRecipeFormat1244 : LocalRecipeFormat1244)
            : (_portable ? PortableFormat : Format));
        if (_portable) writer.Write((byte)SourcePathSeparator);
        if (versioned) WriteText(writer, DependencyRecipe1244);
    }

    private static void ReadCaptureHeader1244(BinaryReader reader,
        out bool portable, out char separator, out string recipe, out bool nativeFocusGearCatalog1339,
        out string? compatibilityProfile)
    {
        string format = ReadText(reader);
        compatibilityProfile = null;
        if (format is LocalCompatibilityFormat or PortableCompatibilityFormat)
        {
            portable = format == PortableCompatibilityFormat;
            separator = portable ? (char)reader.ReadByte() : Path.DirectorySeparatorChar;
            if (separator is not ('/' or '\\')) throw Rejected();
            recipe = ValidateDependencyRecipe1244(ReadText(reader));
            nativeFocusGearCatalog1339 = ReadBool(reader);
            compatibilityProfile = ReadText(reader);
            return; // Constructor validates the exact profile and required package closure.
        }
        nativeFocusGearCatalog1339 = format is LocalFocusFormat1339 or PortableFocusFormat1339;
        portable = format is PortableFormat or PortableRecipeFormat1244 or PortableFocusFormat1339;
        bool versioned = nativeFocusGearCatalog1339 || format is LocalRecipeFormat1244 or PortableRecipeFormat1244;
        if (!portable && format is not (Format or LocalRecipeFormat1244 or LocalFocusFormat1339)) throw Rejected();
        separator = portable ? (char)reader.ReadByte() : Path.DirectorySeparatorChar;
        if (separator is not ('/' or '\\')) throw Rejected();
        recipe = versioned ? ValidateDependencyRecipe1244(ReadText(reader)) : LegacyDependencyRecipe1244;
        // Recipe formats still reject a redundant legacy recipe. Projection formats
        // carry any known dependency recipe, without changing the historical recipes.
        if (versioned && !nativeFocusGearCatalog1339 && recipe == LegacyDependencyRecipe1244) throw Rejected();
    }
}
