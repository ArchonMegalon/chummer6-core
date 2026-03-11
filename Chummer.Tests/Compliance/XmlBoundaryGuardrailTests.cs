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
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
        yield return "/src";
    }
}
