using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Files;

internal static class OwnerScopedStatePath
{
    public static string ResolveOwnerDirectory(string stateDirectory, OwnerScope owner)
    {
        if (owner.IsLocalSingleUser || string.IsNullOrWhiteSpace(owner.NormalizedValue))
        {
            return stateDirectory;
        }

        return Path.Combine(
            stateDirectory,
            "owners",
            Uri.EscapeDataString(owner.NormalizedValue));
    }
}
