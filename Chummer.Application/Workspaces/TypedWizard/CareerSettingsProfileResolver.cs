// Existing typed adapter from the paired Presentation snapshot; Core executes it
// only after store admission. Rules remain in Chummer.Contracts.Characters.
using System.Xml.Linq;
using Chummer.Application.Characters;

namespace Chummer.Application.Workspaces.TypedWizard;

/// <summary>Compatibility bridge for Career editors; never persists or invents a profile.</summary>
internal static class CareerSettingsProfileResolver
{
    private sealed record SourceAuthority(string State);

    internal static XElement Resolve(
        XElement character, string? settingsCatalogJson, ICharacterSourceDataContext? sourceContext)
    {
        string id = CareerActiveSkillAdvanceEditorProjector.ReadRequiredText(character, "settings", "The saved runner");
        if (sourceContext is null
            || !sourceContext.TryResolveCareerSettingsProfile(out string xml, out string authority)
            || string.IsNullOrWhiteSpace(authority))
            throw new InvalidOperationException("The saved runner's exact Chummer5 settings profile is unavailable.");

        XElement settings = ParseSettings(xml);
        XElement[] ids = settings.Elements("id").Take(2).ToArray();
        if (ids.Length != 1 || ids[0].HasElements || ids[0].HasAttributes
            || !string.Equals(ids[0].Value.Trim(), id, StringComparison.Ordinal))
            throw new InvalidOperationException("The resolved Career settings do not identify this runner's exact profile.");
        settings.AddAnnotation(new SourceAuthority(authority));
        return settings;
    }

    internal static string RuleState(XElement settings)
    {
        string xml = settings.ToString(SaveOptions.DisableFormatting);
        return xml + SourceAuthoritySuffix(settings);
    }

    internal static string SourceAuthoritySuffix(XElement settings)
        => settings.Annotation<SourceAuthority>() is { } source ? "\n" + source.State : string.Empty;

    internal static bool TryParseBoolean(string text, out bool value)
    {
        if (text == "1") { value = true; return true; }
        if (text == "0") { value = false; return true; }
        return bool.TryParse(text, out value);
    }

    private static XElement ParseSettings(string xml)
    {
        XElement settings = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        return settings.Name == XName.Get("settings") ? settings
            : throw new InvalidOperationException("The active Chummer5 settings profile has an invalid root node.");
    }
}
