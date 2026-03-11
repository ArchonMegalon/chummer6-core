using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Tools;

public sealed class ShellPreferencesService : IShellPreferencesService
{
    private readonly IShellPreferencesStore _store;

    public ShellPreferencesService(IShellPreferencesStore store)
    {
        _store = store;
    }

    public ShellPreferences Load()
    {
        return Load(OwnerScope.LocalSingleUser);
    }

    public ShellPreferences Load(OwnerScope owner)
    {
        ShellPreferences stored = _store.Load(owner);
        return new ShellPreferences(
            PreferredRulesetId: RulesetDefaults.NormalizeOptional(stored.PreferredRulesetId) ?? string.Empty);
    }

    public void Save(ShellPreferences preferences)
    {
        Save(OwnerScope.LocalSingleUser, preferences);
    }

    public void Save(OwnerScope owner, ShellPreferences preferences)
    {
        ShellPreferences normalized = new(
            PreferredRulesetId: RulesetDefaults.NormalizeOptional(preferences.PreferredRulesetId) ?? string.Empty);
        _store.Save(owner, normalized);
    }
}
