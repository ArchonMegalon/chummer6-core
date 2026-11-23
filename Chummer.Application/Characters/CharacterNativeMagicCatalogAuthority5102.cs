using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Source authentication shared by the native sheet owner and retained-current
// consumer. This is not Priority authority or a new selection/rules compiler.
internal sealed record CharacterNativeMagicCatalogAuthority5102(string AuthorityDigest,
    CharacterCreationKarmaMagicPolicy Policy, IReadOnlyList<CharacterCreationKarmaMagicCatalogSlice> Catalogs)
{
    internal static bool TryResolve(ICharacterSourceDataContext source, string method, string settings,
        out CharacterNativeMagicCatalogAuthority5102? authority)
    {
        authority = null;
        if (method is not (CharacterCreationBuildMethods.Karma or CharacterCreationBuildMethods.LifeModules)
            || string.IsNullOrWhiteSpace(settings)
            || !source.TryResolveCreationSourceProfile(out var profile)
            || profile.BuildMethod != method || profile.SettingsProfileId != settings) return false;

        string digest, profileDigest;
        CharacterCreationKarmaMagicPolicy policy;
        IReadOnlyList<CharacterCreationKarmaMagicCatalogSlice> slices;
        if (method == CharacterCreationBuildMethods.Karma)
        {
            if (!source.TryResolveCreationKarmaMagicCatalog(out var karma)
                || !CharacterCreationKarmaMagicSelectionRules.IsValidCatalog(karma)) return false;
            digest = karma!.AuthorityDigest; profileDigest = karma.RawProfileInputsDigest;
            policy = karma.Policy; slices = karma.Catalogs;
        }
        else
        {
            if (!source.TryResolveCreationLifeModuleMagicCatalog(out var modules)
                || !CharacterCreationLifeModuleMagicRules.IsValidCatalog(modules)) return false;
            digest = modules!.AuthorityDigest; profileDigest = modules.RawProfileInputsDigest;
            policy = modules.Policy; slices = modules.Catalogs;
        }
        if (profileDigest != profile.RawProfileInputsDigest || policy.SettingsProfileId != settings
            || CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(policy.SettingsInputsDigest,
                policy.SettingsProfileId) != profileDigest) return false;
        authority = new(digest, policy, slices);
        return true;
    }
}
