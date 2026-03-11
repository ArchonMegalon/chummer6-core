#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Desktop.Runtime;
using Chummer.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class ServiceCollectionDesktopRuntimeExtensionsTests
{
    private static readonly object EnvironmentLock = new();
    private const string DefaultRulesetEnvironmentVariable = "CHUMMER_DEFAULT_RULESET";

    [TestMethod]
    public void Default_mode_registers_inprocess_client()
    {
        lock (EnvironmentLock)
        {
            string root = CreateTempDirectory();
            try
            {
                ApplyEnvironment(mode: null, baseUrl: null, apiKey: null, () =>
                {
                    var services = new ServiceCollection();
                    services.AddChummerLocalRuntimeClient(root, root);

                    using ServiceProvider provider = services.BuildServiceProvider();
                    IChummerClient client = provider.GetRequiredService<IChummerClient>();
                    ISessionClient sessionClient = provider.GetRequiredService<ISessionClient>();
                    IOwnerContextAccessor ownerContextAccessor = provider.GetRequiredService<IOwnerContextAccessor>();
                    IReadOnlyList<IRulesetPlugin> plugins = provider.GetServices<IRulesetPlugin>().ToArray();

                    Assert.IsInstanceOfType<InProcessChummerClient>(client);
                    Assert.IsInstanceOfType<InProcessSessionClient>(sessionClient);
                    Assert.AreEqual(OwnerScope.LocalSingleUser.NormalizedValue, ownerContextAccessor.Current.NormalizedValue);
                    Assert.IsFalse(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr4, StringComparison.Ordinal)));
                    Assert.IsTrue(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr5, StringComparison.Ordinal)));
                    Assert.IsTrue(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr6, StringComparison.Ordinal)));
                });
            }
            finally
            {
                DeleteTempDirectory(root);
            }
        }
    }

    [TestMethod]
    public void Http_mode_requires_explicit_api_base_url()
    {
        lock (EnvironmentLock)
        {
            string root = CreateTempDirectory();
            try
            {
                ApplyEnvironment(mode: "http", baseUrl: null, apiKey: null, () =>
                {
                    var services = new ServiceCollection();

                    InvalidOperationException? ex = null;
                    try
                    {
                        services.AddChummerLocalRuntimeClient(root, root);
                    }
                    catch (InvalidOperationException captured)
                    {
                        ex = captured;
                    }

                    Assert.IsNotNull(ex);
                    StringAssert.Contains(ex.Message, "CHUMMER_API_BASE_URL");
                });
            }
            finally
            {
                DeleteTempDirectory(root);
            }
        }
    }

    [TestMethod]
    public void Http_mode_registers_http_client_and_api_key_header_when_configured()
    {
        lock (EnvironmentLock)
        {
            string root = CreateTempDirectory();
            try
            {
                ApplyEnvironment(mode: "http", baseUrl: "https://api.example.invalid/", apiKey: "test-key", () =>
                {
                    var services = new ServiceCollection();
                    services.AddChummerLocalRuntimeClient(root, root);

                    using ServiceProvider provider = services.BuildServiceProvider();
                    IChummerClient client = provider.GetRequiredService<IChummerClient>();
                    ISessionClient sessionClient = provider.GetRequiredService<ISessionClient>();
                    HttpClient httpClient = provider.GetRequiredService<HttpClient>();
                    IReadOnlyList<IRulesetPlugin> plugins = provider.GetServices<IRulesetPlugin>().ToArray();

                    Assert.IsInstanceOfType<HttpChummerClient>(client);
                    Assert.IsInstanceOfType<HttpSessionClient>(sessionClient);
                    Assert.IsNotNull(httpClient.BaseAddress);
                    Assert.AreEqual("https://api.example.invalid/", httpClient.BaseAddress!.ToString());
                    Assert.IsTrue(httpClient.DefaultRequestHeaders.Contains("X-Api-Key"));
                    Assert.IsFalse(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr4, StringComparison.Ordinal)));
                    Assert.IsTrue(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr5, StringComparison.Ordinal)));
                    Assert.IsTrue(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr6, StringComparison.Ordinal)));
                    string[] expectedApiKeyValues = ["test-key"];
                    CollectionAssert.AreEqual(
                        expectedApiKeyValues,
                        new List<string>(httpClient.DefaultRequestHeaders.GetValues("X-Api-Key")));
                });
            }
            finally
            {
                DeleteTempDirectory(root);
            }
        }
    }

    [TestMethod]
    public void Legacy_desktop_client_mode_environment_variable_remains_supported()
    {
        lock (EnvironmentLock)
        {
            string root = CreateTempDirectory();
            try
            {
                ApplyEnvironment(mode: null, legacyMode: "http", baseUrl: "https://legacy.example.invalid/", apiKey: null, () =>
                {
                    var services = new ServiceCollection();
                    services.AddChummerLocalRuntimeClient(root, root);

                    using ServiceProvider provider = services.BuildServiceProvider();
                    IChummerClient client = provider.GetRequiredService<IChummerClient>();
                    ISessionClient sessionClient = provider.GetRequiredService<ISessionClient>();
                    HttpClient httpClient = provider.GetRequiredService<HttpClient>();
                    IReadOnlyList<IRulesetPlugin> plugins = provider.GetServices<IRulesetPlugin>().ToArray();

                    Assert.IsInstanceOfType<HttpChummerClient>(client);
                    Assert.IsInstanceOfType<HttpSessionClient>(sessionClient);
                    Assert.IsNotNull(httpClient.BaseAddress);
                    Assert.AreEqual("https://legacy.example.invalid/", httpClient.BaseAddress!.ToString());
                    Assert.IsFalse(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr4, StringComparison.Ordinal)));
                    Assert.IsTrue(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr5, StringComparison.Ordinal)));
                    Assert.IsTrue(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr6, StringComparison.Ordinal)));
                });
            }
            finally
            {
                DeleteTempDirectory(root);
            }
        }
    }

    [TestMethod]
    public void Default_ruleset_environment_variable_controls_shell_catalog_resolution()
    {
        lock (EnvironmentLock)
        {
            string root = CreateTempDirectory();
            try
            {
                ApplyEnvironment(mode: null, baseUrl: null, apiKey: null, action: () =>
                {
                    var services = new ServiceCollection();
                    services.AddChummerLocalRuntimeClient(root, root);

                    using ServiceProvider provider = services.BuildServiceProvider();
                    IRulesetSelectionPolicy selectionPolicy = provider.GetRequiredService<IRulesetSelectionPolicy>();
                    IRulesetShellCatalogResolver shellCatalogResolver = provider.GetRequiredService<IRulesetShellCatalogResolver>();

                    Assert.AreEqual(RulesetDefaults.Sr6, selectionPolicy.GetDefaultRulesetId());

                    IReadOnlyList<AppCommandDefinition> commands = shellCatalogResolver.ResolveCommands(null);
                    IReadOnlyList<NavigationTabDefinition> tabs = shellCatalogResolver.ResolveNavigationTabs(null);

                    Assert.IsNotEmpty(commands, "Expected SR6 to expose shell commands.");
                    Assert.IsNotEmpty(tabs, "Expected SR6 to expose navigation tabs.");
                    Assert.IsTrue(commands.All(command => string.Equals(command.RulesetId, RulesetDefaults.Sr6, StringComparison.Ordinal)));
                    Assert.IsTrue(tabs.All(tab => string.Equals(tab.RulesetId, RulesetDefaults.Sr6, StringComparison.Ordinal)));
                }, defaultRulesetId: RulesetDefaults.Sr6);
            }
            finally
            {
                DeleteTempDirectory(root);
            }
        }
    }

    [TestMethod]
    public void Default_ruleset_environment_variable_fails_when_ruleset_is_not_registered()
    {
        lock (EnvironmentLock)
        {
            string root = CreateTempDirectory();
            try
            {
                ApplyEnvironment(mode: null, baseUrl: null, apiKey: null, action: () =>
                {
                    var services = new ServiceCollection();
                    services.AddChummerLocalRuntimeClient(root, root);

                    using ServiceProvider provider = services.BuildServiceProvider();
                    IRulesetSelectionPolicy selectionPolicy = provider.GetRequiredService<IRulesetSelectionPolicy>();
                    IRulesetShellCatalogResolver shellCatalogResolver = provider.GetRequiredService<IRulesetShellCatalogResolver>();

                    InvalidOperationException selectionPolicyEx = Assert.ThrowsExactly<InvalidOperationException>(() =>
                        selectionPolicy.GetDefaultRulesetId());
                    InvalidOperationException shellCatalogEx = Assert.ThrowsExactly<InvalidOperationException>(() =>
                        shellCatalogResolver.ResolveCommands(null));

                    StringAssert.Contains(selectionPolicyEx.Message, "Configured default ruleset 'sr4'");
                    StringAssert.Contains(selectionPolicyEx.Message, $"environment:{DefaultRulesetEnvironmentVariable}");
                    StringAssert.Contains(shellCatalogEx.Message, "Configured default ruleset 'sr4'");
                    StringAssert.Contains(shellCatalogEx.Message, $"environment:{DefaultRulesetEnvironmentVariable}");
                }, defaultRulesetId: RulesetDefaults.Sr4);
            }
            finally
            {
                DeleteTempDirectory(root);
            }
        }
    }

    private static void ApplyEnvironment(string? mode, string? baseUrl, string? apiKey, Action action, string? defaultRulesetId = null)
        => ApplyEnvironment(mode, legacyMode: mode, baseUrl, apiKey, action, defaultRulesetId);

    private static void ApplyEnvironment(
        string? mode,
        string? legacyMode,
        string? baseUrl,
        string? apiKey,
        Action action,
        string? defaultRulesetId = null)
    {
        string? previousMode = Environment.GetEnvironmentVariable("CHUMMER_CLIENT_MODE");
        string? previousLegacyMode = Environment.GetEnvironmentVariable("CHUMMER_DESKTOP_CLIENT_MODE");
        string? previousBaseUrl = Environment.GetEnvironmentVariable("CHUMMER_API_BASE_URL");
        string? previousApiKey = Environment.GetEnvironmentVariable("CHUMMER_API_KEY");
        string? previousDefaultRulesetId = Environment.GetEnvironmentVariable(DefaultRulesetEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable("CHUMMER_CLIENT_MODE", mode);
            Environment.SetEnvironmentVariable("CHUMMER_DESKTOP_CLIENT_MODE", legacyMode);
            Environment.SetEnvironmentVariable("CHUMMER_API_BASE_URL", baseUrl);
            Environment.SetEnvironmentVariable("CHUMMER_API_KEY", apiKey);
            Environment.SetEnvironmentVariable(DefaultRulesetEnvironmentVariable, defaultRulesetId);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CHUMMER_CLIENT_MODE", previousMode);
            Environment.SetEnvironmentVariable("CHUMMER_DESKTOP_CLIENT_MODE", previousLegacyMode);
            Environment.SetEnvironmentVariable("CHUMMER_API_BASE_URL", previousBaseUrl);
            Environment.SetEnvironmentVariable("CHUMMER_API_KEY", previousApiKey);
            Environment.SetEnvironmentVariable(DefaultRulesetEnvironmentVariable, previousDefaultRulesetId);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "chummer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures in tests.
        }
    }
}
