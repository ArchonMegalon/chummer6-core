#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed partial class BenchmarkGuardrailContractTests
{
    [TestMethod]
    public void Budget_file_matches_executable_benchmarkdotnet_workloads()
    {
        string benchmarkSource = File.ReadAllText(FindRepoPath("Chummer.Benchmarks", "MigrationWorkspaceBenchmarks.cs"));
        using JsonDocument budget = JsonDocument.Parse(File.ReadAllText(FindRepoPath(
            "Chummer.Benchmarks",
            "workspace-benchmark-budgets.json")));

        string[] benchmarkDescriptions = BenchmarkDescriptionRegex()
            .Matches(benchmarkSource)
            .Select(match => match.Groups["name"].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] budgetFactoryNames = BudgetFactoryNameRegex()
            .Matches(benchmarkSource)
            .Select(match => match.Groups["name"].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] budgetFileNames = budget.RootElement
            .GetProperty("workloads")
            .EnumerateArray()
            .Select(workload => workload.GetProperty("name").GetString() ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEquivalent(benchmarkDescriptions, budgetFactoryNames,
            "Every budget-runner workload must map to a BenchmarkDotNet benchmark description.");
        CollectionAssert.AreEquivalent(benchmarkDescriptions, budgetFileNames,
            "Every budgeted workload must be executable as a BenchmarkDotNet benchmark.");
        Assert.IsTrue(benchmarkDescriptions.Length >= 5, "The guardrail must cover import, section, save, explain, and export workloads.");
    }

    [TestMethod]
    public void Benchmark_project_uses_current_sdk_package_references_only()
    {
        string projectText = File.ReadAllText(FindRepoPath("Chummer.Benchmarks", "Chummer.Benchmarks.csproj"));

        StringAssert.Contains(projectText, "<TargetFramework>net10.0</TargetFramework>");
        StringAssert.Contains(projectText, "<PackageReference Include=\"BenchmarkDotNet\"");
        Assert.IsFalse(File.Exists(FindOptionalRepoPath("Chummer.Benchmarks", "packages.config")),
            "The benchmark project must not carry stale .NET Framework packages.config dependencies.");
        Assert.IsFalse(File.Exists(FindOptionalRepoPath("Chummer.Benchmarks", "App.config")),
            "The benchmark project must not carry stale .NET Framework runtime configuration.");
    }

    [GeneratedRegex("\\[Benchmark\\(Description\\s*=\\s*\"(?<name>[^\"]+)\"\\)\\]", RegexOptions.Compiled)]
    private static partial Regex BenchmarkDescriptionRegex();

    [GeneratedRegex("Name\\s*[:=]\\s*\"(?<name>[^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex BudgetFactoryNameRegex();

    private static string FindRepoPath(params string[] segments)
    {
        string? candidate = FindOptionalRepoPath(segments);
        return candidate ?? throw new FileNotFoundException($"Could not find repo file '{Path.Combine(segments)}'.");
    }

    private static string? FindOptionalRepoPath(params string[] segments)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
