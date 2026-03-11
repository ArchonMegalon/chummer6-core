#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Compliance;

[TestClass]
public class ArchitectureGuardrailTests
{
    private static readonly string[] HeadlessProjects =
    {
        "Chummer.Contracts",
        "Chummer.Core",
        "Chummer.Application",
        "Chummer.Infrastructure",
        "Chummer.Presentation",
        "Chummer.Rulesets.Hosting",
        "Chummer.Rulesets.Sr5",
        "Chummer.Desktop.Runtime"
    };

    private static readonly string[] BannedUiReferences =
    {
        "using System.Windows.Forms",
        "using Avalonia",
        "using Microsoft.AspNetCore.Components",
        "using Microsoft.JSInterop"
    };

    private static readonly string[] UiHeadProjects =
    {
        "Chummer.Blazor",
        "Chummer.Blazor.Desktop",
        "Chummer.Avalonia",
        "Chummer.Avalonia.Browser",
        "Chummer.Coach.Web",
        "Chummer.Hub.Web",
        "Chummer.Session.Web",
        "Chummer.Portal"
    };

    private static readonly string[] ForbiddenUiHeadLayerUsings =
    {
        "using Chummer.Application",
        "using Chummer.Core",
        "using Chummer.Infrastructure",
        "global using Chummer.Application",
        "global using Chummer.Core",
        "global using Chummer.Infrastructure"
    };

    [TestMethod]
    public void Program_host_file_stays_transport_only()
    {
        string programPath = FindPath("Chummer.Api", "Program.cs");
        string text = File.ReadAllText(programPath);

        StringAssert.Contains(text, "app.MapInfoEndpoints();");
        StringAssert.Contains(text, "app.MapCharacterEndpoints();");
        StringAssert.Contains(text, "app.MapLifeModulesEndpoints();");
        StringAssert.Contains(text, "app.MapToolsEndpoints();");
        StringAssert.Contains(text, "app.MapSettingsEndpoints();");
        StringAssert.Contains(text, "app.MapRosterEndpoints();");
        StringAssert.Contains(text, "app.MapCommandEndpoints();");
        StringAssert.Contains(text, "app.MapNavigationEndpoints();");
        StringAssert.Contains(text, "app.MapShellEndpoints();");
        StringAssert.Contains(text, "app.MapWorkspaceEndpoints();");
        StringAssert.Contains(text, "builder.Services.AddChummerHeadlessCore(");

        Assert.IsFalse(text.Contains("Path.GetTempPath()", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("File.", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("XDocument.Parse", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("public sealed record", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("app.MapPost(\"/api/characters/sections", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("Chummer.Core.LifeModules", StringComparison.Ordinal));
        StringAssert.Contains(text, "AddSingleton<IOwnerContextAccessor>(");
    }

    [TestMethod]
    public void Blazor_head_exposes_health_endpoint()
    {
        string programPath = FindPath("Chummer.Blazor", "Program.cs");
        string text = File.ReadAllText(programPath);

        StringAssert.Contains(text, "app.MapGet(\"/health\"");
        StringAssert.Contains(text, "head = \"blazor\"");
    }

    [TestMethod]
    public void Headless_projects_do_not_reference_ui_frameworks()
    {
        foreach (string project in HeadlessProjects)
        {
            string directory = FindDirectory(project);
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (string banned in BannedUiReferences)
                {
                    Assert.IsFalse(
                        text.Contains(banned, StringComparison.Ordinal),
                        $"Forbidden UI reference '{banned}' found in {file}.");
                }
            }
        }
    }

    [TestMethod]
    public void Ui_head_projects_do_not_import_non_presentation_layers()
    {
        foreach (string project in UiHeadProjects)
        {
            string directory = FindDirectory(project);
            foreach (string file in EnumerateUiHeadSourceFiles(directory))
            {
                string text = File.ReadAllText(file);
                foreach (string bannedUsing in ForbiddenUiHeadLayerUsings)
                {
                    Assert.IsFalse(
                        text.Contains(bannedUsing, StringComparison.Ordinal),
                        $"Forbidden layer import '{bannedUsing}' found in {file}.");
                }
            }
        }
    }

    [TestMethod]
    public void Production_projects_do_not_depend_on_legacy_rule_or_script_host_contracts()
    {
        string[] productionProjects =
        {
            "Chummer.Application",
            "Chummer.Infrastructure",
            "Chummer.Infrastructure.Browser",
            "Chummer.Presentation",
            "Chummer.Api",
            "Chummer.Portal",
            "Chummer.Desktop.Runtime",
            "Chummer.Blazor",
            "Chummer.Blazor.Desktop",
            "Chummer.Avalonia",
            "Chummer.Avalonia.Browser",
            "Chummer.Coach.Web",
            "Chummer.Hub.Web",
            "Chummer.Session.Web"
        };

        string[] bannedTokens =
        {
            "IRulesetRuleHost",
            "IRulesetScriptHost",
            "RulesetRuleEvaluationRequest",
            "RulesetRuleEvaluationResult",
            "RulesetScriptExecutionRequest",
            "RulesetScriptExecutionResult"
        };

        foreach (string project in productionProjects)
        {
            string directory = FindDirectory(project);
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (string bannedToken in bannedTokens)
                {
                    Assert.IsFalse(
                        text.Contains(bannedToken, StringComparison.Ordinal),
                        $"Legacy rules/script host token '{bannedToken}' found in {file}.");
                }
            }
        }
    }

    [TestMethod]
    public void Legacy_shell_catalogs_remain_confined_to_compatibility_resolver()
    {
        string compatibilityResolverPath = FindPath("Chummer.Presentation", "Shell", "CatalogOnlyRulesetShellCatalogResolver.cs");
        string[] legacyCatalogTokens =
        {
            "AppCommandCatalog.ForRuleset(",
            "NavigationTabCatalog.ForRuleset(",
            "WorkspaceSurfaceActionCatalog.ForTab(",
            "WorkspaceSurfaceActionCatalog.ForRuleset("
        };

        string[] productionProjects =
        {
            "Chummer.Application",
            "Chummer.Infrastructure",
            "Chummer.Infrastructure.Browser",
            "Chummer.Presentation",
            "Chummer.Api",
            "Chummer.Portal",
            "Chummer.Desktop.Runtime",
            "Chummer.Blazor",
            "Chummer.Blazor.Desktop",
            "Chummer.Avalonia",
            "Chummer.Avalonia.Browser",
            "Chummer.Hub.Web",
            "Chummer.Session.Web",
            "Chummer.Rulesets.Hosting"
        };

        foreach (string project in productionProjects)
        {
            string directory = FindDirectory(project);
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (string.Equals(file, compatibilityResolverPath, StringComparison.Ordinal))
                {
                    continue;
                }

                string text = File.ReadAllText(file);
                foreach (string legacyCatalogToken in legacyCatalogTokens)
                {
                    Assert.IsFalse(
                        text.Contains(legacyCatalogToken, StringComparison.Ordinal),
                        $"Legacy shell catalog token '{legacyCatalogToken}' found outside compatibility resolver in {file}.");
                }
            }
        }
    }

    [TestMethod]
    public void Tools_endpoints_stay_transport_only()
    {
        string endpointPath = FindPath("Chummer.Api", "Endpoints", "ToolsEndpoints.cs");
        string text = File.ReadAllText(endpointPath);

        Assert.IsFalse(text.Contains("Directory.", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("File.", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("Path.Combine", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("XDocument", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Workspace_endpoints_stay_transport_only()
    {
        string endpointPath = FindPath("Chummer.Api", "Endpoints", "WorkspaceEndpoints.cs");
        string text = File.ReadAllText(endpointPath);

        StringAssert.Contains(text, "IWorkspaceService workspaceService");
        Assert.IsFalse(text.Contains("Directory.", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("File.", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("Path.Combine", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("XDocument", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("ICharacterFileQueries", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("ICharacterSectionQueries", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("ICharacterMetadataCommands", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Web_project_does_not_reference_core_directly()
    {
        string projectPath = FindPath("Chummer.Web", "Chummer.Web.csproj");
        string text = File.ReadAllText(projectPath);

        Assert.IsFalse(text.Contains(@"..\Chummer.Core\Chummer.Core.csproj", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Api_project_does_not_reference_core_directly()
    {
        string projectPath = FindPath("Chummer.Api", "Chummer.Api.csproj");
        string text = File.ReadAllText(projectPath);

        Assert.IsFalse(text.Contains(@"..\Chummer.Core\Chummer.Core.csproj", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Character_xml_parsers_live_in_infrastructure_not_core()
    {
        string infrastructureSectionPath = FindPath("Chummer.Infrastructure", "Xml", "ICharacterSectionService.cs");
        string infrastructureFilePath = FindPath("Chummer.Infrastructure", "Xml", "ICharacterFileService.cs");
        string? coreCharactersDirectory = TryFindDirectory("Chummer.Core", "Characters");

        Assert.IsTrue(File.Exists(infrastructureSectionPath));
        Assert.IsTrue(File.Exists(infrastructureFilePath));

        if (!string.IsNullOrWhiteSpace(coreCharactersDirectory))
        {
            string coreSectionPath = Path.Combine(coreCharactersDirectory, "ICharacterSectionService.cs");
            string coreFilePath = Path.Combine(coreCharactersDirectory, "ICharacterFileService.cs");

            Assert.IsFalse(File.Exists(coreSectionPath), "Character section parser interface must not live in Chummer.Core.");
            Assert.IsFalse(File.Exists(coreFilePath), "Character file parser interface must not live in Chummer.Core.");
        }
    }

    [TestMethod]
    public void Infrastructure_project_does_not_reference_core_project()
    {
        string projectPath = FindPath("Chummer.Infrastructure", "Chummer.Infrastructure.csproj");
        string text = File.ReadAllText(projectPath);

        Assert.IsFalse(text.Contains(@"..\Chummer.Core\Chummer.Core.csproj", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Headless_project_references_follow_layering_rules()
    {
        Dictionary<string, HashSet<string>> allowedReferences = new(StringComparer.Ordinal)
        {
            ["Chummer.Contracts"] = new HashSet<string>(StringComparer.Ordinal),
            ["Chummer.Core"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Contracts" },
            ["Chummer.Application"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Contracts" },
            ["Chummer.Presentation"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Contracts", "Chummer.Rulesets.Hosting" },
            ["Chummer.Rulesets.Hosting"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Application", "Chummer.Contracts" },
            ["Chummer.Rulesets.Sr5"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Application", "Chummer.Contracts" },
            ["Chummer.Infrastructure"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Application", "Chummer.Contracts", "Chummer.Rulesets.Hosting", "Chummer.Rulesets.Sr5", "Chummer.Rulesets.Sr6" },
            ["Chummer.Infrastructure.Browser"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Application", "Chummer.Contracts" },
            ["Chummer.Api"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Application", "Chummer.Contracts", "Chummer.Infrastructure" },
            ["Chummer.Portal"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Contracts" },
            ["Chummer.Web"] = new HashSet<string>(StringComparer.Ordinal),
            ["Chummer.Blazor"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Contracts", "Chummer.Presentation", "Chummer.Rulesets.Hosting", "Chummer.Rulesets.Sr5", "Chummer.Rulesets.Sr6" },
            ["Chummer.Hub.Web"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Contracts", "Chummer.Presentation" },
            ["Chummer.Session.Web"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Contracts", "Chummer.Infrastructure.Browser", "Chummer.Presentation" },
            ["Chummer.Desktop.Runtime"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Application", "Chummer.Contracts", "Chummer.Infrastructure", "Chummer.Presentation", "Chummer.Rulesets.Hosting", "Chummer.Rulesets.Sr5", "Chummer.Rulesets.Sr6" },
            ["Chummer.Blazor.Desktop"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Blazor", "Chummer.Contracts", "Chummer.Desktop.Runtime", "Chummer.Presentation" },
            ["Chummer.Avalonia"] = new HashSet<string>(StringComparer.Ordinal) { "Chummer.Contracts", "Chummer.Desktop.Runtime", "Chummer.Presentation" },
            ["Chummer.Avalonia.Browser"] = new HashSet<string>(StringComparer.Ordinal)
        };

        foreach ((string project, HashSet<string> allowed) in allowedReferences)
        {
            HashSet<string> actual = ReadProjectReferenceNames(project);
            List<string> forbidden = actual.Except(allowed, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();

            Assert.IsEmpty(
                forbidden,
                $"{project} has forbidden project references: {string.Join(", ", forbidden)}.");
        }
    }

    [TestMethod]
    public void Core_lifemodule_xml_services_are_removed()
    {
        string? coreLifeModulesDirectory = TryFindDirectory("Chummer.Core", "LifeModules");
        if (string.IsNullOrWhiteSpace(coreLifeModulesDirectory))
            return;

        string[] legacyFiles =
        {
            "ILifeModulesService.cs",
            "LifeModuleModels.cs",
            "LifeModulesPathResolver.cs",
            "LifeModulesService.cs"
        };

        foreach (string file in legacyFiles)
        {
            Assert.IsFalse(
                File.Exists(Path.Combine(coreLifeModulesDirectory, file)),
                $"Legacy life modules XML service file must be removed from Chummer.Core: {file}");
        }
    }

    private static string FindPath(params string[] parts)
    {
        foreach (string? root in CandidateRoots())
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            DirectoryInfo current = new(root);
            while (true)
            {
                string candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                    return candidate;

                if (current.Parent == null)
                    break;

                current = current.Parent;
            }
        }

        throw new FileNotFoundException("Could not locate file.", Path.Combine(parts));
    }

    private static HashSet<string> ReadProjectReferenceNames(string projectName)
    {
        string projectPath = FindPath(projectName, $"{projectName}.csproj");
        XDocument document = XDocument.Load(projectPath);

        return document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar)))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateUiHeadSourceFiles(string directory)
    {
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                string extension = Path.GetExtension(path);
                return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".razor", StringComparison.OrdinalIgnoreCase);
            });
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

    private static string? TryFindDirectory(params string[] parts)
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

        return null;
    }

    private static IEnumerable<string?> CandidateRoots()
    {
        yield return Environment.GetEnvironmentVariable("CHUMMER_REPO_ROOT");
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
        yield return "/src";
    }
}
