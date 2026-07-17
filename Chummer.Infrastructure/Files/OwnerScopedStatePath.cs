using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Files;

internal static class OwnerScopedStatePath
{
    private const string OwnerHashDomain = "chummer-owner-state-v1\0";
    private const string OwnerComponentPrefix = "owner-v1-";
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly HashSet<string> WindowsDeviceNames = BuildWindowsDeviceNames();

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

    public static string ResolveWorkspaceOwnerDirectory(string stateDirectory, OwnerScope owner)
    {
        string stateRoot = Path.GetFullPath(stateDirectory);
        if (owner.IsLocalSingleUser || string.IsNullOrWhiteSpace(owner.NormalizedValue))
        {
            return stateRoot;
        }

        string ownersDirectory = Path.GetFullPath(Path.Combine(stateRoot, "owners"));
        EnsureContained(stateRoot, ownersDirectory, "workspace owners directory");
        string ownerDirectory = Path.GetFullPath(Path.Combine(
            ownersDirectory,
            BuildOwnerComponent(owner)));
        EnsureContained(ownersDirectory, ownerDirectory, "workspace owner directory");
        return ownerDirectory;
    }

    public static bool TryResolveContainedLegacyOwnerDirectory(
        string stateDirectory,
        OwnerScope owner,
        out string legacyOwnerDirectory)
    {
        legacyOwnerDirectory = string.Empty;
        if (owner.IsLocalSingleUser || string.IsNullOrWhiteSpace(owner.NormalizedValue))
        {
            return false;
        }

        string legacyComponent = Uri.EscapeDataString(owner.NormalizedValue);
        if (!IsSafeLegacyComponent(legacyComponent))
        {
            return false;
        }

        string stateRoot = Path.GetFullPath(stateDirectory);
        string ownersDirectory = Path.GetFullPath(Path.Combine(stateRoot, "owners"));
        EnsureContained(stateRoot, ownersDirectory, "workspace owners directory");
        string candidate = Path.GetFullPath(Path.Combine(ownersDirectory, legacyComponent));
        if (!IsContained(ownersDirectory, candidate)
            || !string.Equals(Path.GetFileName(candidate), legacyComponent, PathComparison))
        {
            return false;
        }

        legacyOwnerDirectory = candidate;
        return true;
    }

    internal static string BuildOwnerComponent(OwnerScope owner)
    {
        // OwnerScope equality is ordinal over its normalized text. Hash those exact UTF-8 bytes;
        // NFC/NFD principals must not collapse to the same storage identity here.
        byte[] input = Encoding.UTF8.GetBytes(OwnerHashDomain + owner.NormalizedValue);
        string base64Url = Convert.ToBase64String(SHA256.HashData(input))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return OwnerComponentPrefix + base64Url;
    }

    private static bool IsSafeLegacyComponent(string component)
    {
        if (string.IsNullOrWhiteSpace(component)
            || component is "." or ".."
            || component.EndsWith(' ')
            || component.EndsWith('.')
            || component.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || !string.Equals(Path.GetFileName(component), component, StringComparison.Ordinal))
        {
            return false;
        }

        string deviceStem = component.Split('.')[0];
        return !WindowsDeviceNames.Contains(deviceStem);
    }

    private static void EnsureContained(string root, string candidate, string description)
    {
        if (!IsContained(root, candidate))
        {
            throw new IOException($"The {description} escapes its storage root.");
        }
    }

    private static bool IsContained(string root, string candidate)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedCandidate = Path.GetFullPath(candidate);
        if (string.Equals(normalizedRoot, normalizedCandidate, PathComparison))
        {
            return true;
        }

        string prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, PathComparison);
    }

    private static HashSet<string> BuildWindowsDeviceNames()
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL"
        };
        for (int index = 1; index <= 9; index++)
        {
            names.Add($"COM{index}");
            names.Add($"LPT{index}");
        }

        return names;
    }
}
