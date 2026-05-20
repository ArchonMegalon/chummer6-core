#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Compliance;

[TestClass]
public class XmlBoundaryGuardrailTests
{
    private static readonly Regex PublicInterfaceRegex = new(@"public\s+interface\s+(?<name>[A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex XmlParameterRegex = new(@"\bstring\s+xml\b", RegexOptions.Compiled);
    private static readonly Regex LegacyCharacterXmlDocumentRegex = new(@"\bCharacterXmlDocument\b", RegexOptions.Compiled);

    private static readonly Dictionary<string, int> AllowedXmlInterfaceParameterCounts = new(StringComparer.Ordinal);

    [TestMethod]
    public void Xml_string_parameters_in_public_interfaces_do_not_expand()
    {
        string applicationDirectory = FindDirectory("Chummer.Application");
        string presentationDirectory = FindDirectory("Chummer.Presentation");

        Dictionary<string, int> actualXmlParameterCounts = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(applicationDirectory, "*.cs", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(presentationDirectory, "*.cs", SearchOption.AllDirectories)))
        {
            string text = File.ReadAllText(file);
            Match interfaceMatch = PublicInterfaceRegex.Match(text);
            if (!interfaceMatch.Success)
                continue;

            int xmlParameterCount = XmlParameterRegex.Count(text);
            if (xmlParameterCount <= 0)
                continue;

            string interfaceName = interfaceMatch.Groups["name"].Value;
            actualXmlParameterCounts[interfaceName] = xmlParameterCount;
        }

        List<string> unexpectedInterfaces = actualXmlParameterCounts.Keys
            .Except(AllowedXmlInterfaceParameterCounts.Keys, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.IsEmpty(unexpectedInterfaces,
            "Unexpected public interfaces with raw xml string parameters: " + string.Join(", ", unexpectedInterfaces));

        foreach ((string interfaceName, int baselineCount) in AllowedXmlInterfaceParameterCounts)
        {
            actualXmlParameterCounts.TryGetValue(interfaceName, out int actualCount);
            Assert.IsLessThanOrEqualTo(
                baselineCount,
                actualCount,
                $"{interfaceName} introduced additional raw xml parameters. Baseline: {baselineCount}, actual: {actualCount}.");
        }
    }

    [TestMethod]
    public void Application_and_presentation_layers_do_not_reference_legacy_characterxmldocument()
    {
        string applicationDirectory = FindDirectory("Chummer.Application");
        string presentationDirectory = FindDirectory("Chummer.Presentation");

        List<string> offenders = Directory.EnumerateFiles(applicationDirectory, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(presentationDirectory, "*.cs", SearchOption.AllDirectories))
            .Where(file => LegacyCharacterXmlDocumentRegex.IsMatch(File.ReadAllText(file)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.IsEmpty(offenders, "Legacy CharacterXmlDocument references found:\n" + string.Join('\n', offenders));
    }

    private static string FindDirectory(params string[] parts)
    {
        string? canonicalOwnerDirectory = TryResolveCanonicalOwnerDirectory(parts);
        if (!string.IsNullOrWhiteSpace(canonicalOwnerDirectory))
        {
            return canonicalOwnerDirectory;
        }

        foreach (string? root in CandidateRoots())
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            DirectoryInfo current = new(root);
            while (true)
            {
                string candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
                if (Directory.Exists(candidate))
                    return candidate;

                if (current.Parent == null)
                    break;

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate directory: " + Path.Combine(parts));
    }

    private static IEnumerable<string?> CandidateRoots()
    {
        yield return Environment.GetEnvironmentVariable("CHUMMER_REPO_ROOT");
        yield return "/docker/chummercomplete";
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
        yield return "/src";
    }

    private static string? TryResolveCanonicalOwnerDirectory(params string[] parts)
    {
        string[]? aliasParts = ResolveCanonicalOwnerAlias(parts);
        if (aliasParts is null)
        {
            return null;
        }

        foreach (string? root in CandidateRoots())
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            DirectoryInfo current = new(root);
            while (true)
            {
                string candidate = Path.GetFullPath(Path.Combine(new[] { current.FullName }.Concat(aliasParts).ToArray()));
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                if (current.Parent is null)
                {
                    break;
                }

                current = current.Parent;
            }
        }

        return null;
    }

    private static string[]? ResolveCanonicalOwnerAlias(params string[] parts)
    {
        if (parts.Length == 0)
        {
            return null;
        }

        return parts[0] switch
        {
            "Chummer.Application" or "Chummer.Benchmarks" or "Chummer.Contracts" or "Chummer.Core"
                or "Chummer.FeatureSlice.Tests" or "Chummer.Infrastructure" or "Chummer.Infrastructure.Browser"
                or "Chummer.Rulesets.Hosting" or "Chummer.Rulesets.Sr4" or "Chummer.Rulesets.Sr5"
                or "Chummer.Rulesets.Sr6" or "Chummer.Tests" or "Chummer.CoreEngine.Tests" or "Chummer"
                => new[] { "..", "chummer-core-engine" }.Concat(parts).ToArray(),
            "Chummer.Api" or "Chummer.Avalonia" or "Chummer.Avalonia.Browser" or "Chummer.Blazor"
                or "Chummer.Blazor.Desktop" or "Chummer.Desktop.Installer" or "Chummer.Desktop.Runtime"
                or "Chummer.Desktop.Runtime.Tests" or "Chummer.Presentation" or "Chummer.Portal"
                or "ChummerDataViewer" or "TextblockConverter" or "Translator"
                => new[] { "..", "chummer-presentation" }.Concat(parts).ToArray(),
            _ => null
        };
    }
}
