using Chummer.Application.Tools;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;

namespace Chummer.Infrastructure.Files;

public sealed class SettingsShellPreferencesStore : IShellPreferencesStore
{
    private const string PreferredRulesetIdKey = "preferredRulesetId";
    private readonly ISettingsStore _settingsStore;

    public SettingsShellPreferencesStore(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public ShellPreferences Load()
    {
        return Load(OwnerScope.LocalSingleUser);
    }

    public ShellPreferences Load(OwnerScope owner)
    {
        var settings = _settingsStore.Load(owner, SettingsOwnerScope.GlobalSettingsScope);
        string preferredRulesetId = settings[PreferredRulesetIdKey]?.GetValue<string>() ?? string.Empty;
        return new ShellPreferences(preferredRulesetId);
    }

    public void Save(ShellPreferences preferences)
    {
        Save(OwnerScope.LocalSingleUser, preferences);
    }

    public void Save(OwnerScope owner, ShellPreferences preferences)
    {
        var settings = _settingsStore.Load(owner, SettingsOwnerScope.GlobalSettingsScope);
        settings[PreferredRulesetIdKey] = preferences.PreferredRulesetId;
        _settingsStore.Save(owner, SettingsOwnerScope.GlobalSettingsScope, settings);
    }
}
