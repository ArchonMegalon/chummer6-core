#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using Chummer.Application.Owners;
using Chummer.Application.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class HeadlessCoreContentBundleValidationTests
{
    private static readonly object EnvironmentLock = new();
    private const string RequireContentBundleEnvironmentVariable = "CHUMMER_REQUIRE_CONTENT_BUNDLE";

    [TestMethod]
    public void AddChummerHeadlessCore_require_content_bundle_throws_when_content_is_missing()
    {
        string root = CreateTempDirectory();
        try
        {
            var services = new ServiceCollection();

            InvalidOperationException? ex = null;
            try
            {
                services.AddChummerHeadlessCore(root, root, requireContentBundle: true);
            }
            catch (InvalidOperationException captured)
            {
                ex = captured;
            }

            Assert.IsNotNull(ex);
            StringAssert.Contains(ex.Message, "no data directories");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void AddChummerHeadlessCore_require_content_bundle_accepts_minimal_content_tree()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDirectory = Path.Combine(root, "data");
            string languageDirectory = Path.Combine(root, "lang");
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(languageDirectory);
            File.WriteAllText(Path.Combine(dataDirectory, "lifemodules.xml"), "<chummer><modules /></chummer>");
            File.WriteAllText(Path.Combine(languageDirectory, "en-us.xml"), "<chummer><name>English (US)</name></chummer>");

            var services = new ServiceCollection();
            services.AddChummerHeadlessCore(root, root, requireContentBundle: true);

            using ServiceProvider provider = services.BuildServiceProvider();
            IContentOverlayCatalogService overlays = provider.GetRequiredService<IContentOverlayCatalogService>();
            IOwnerContextAccessor ownerContextAccessor = provider.GetRequiredService<IOwnerContextAccessor>();
            IRulesetPlugin[] plugins = provider.GetServices<IRulesetPlugin>().ToArray();
            string resolved = overlays.ResolveDataFile("lifemodules.xml");
            Assert.AreEqual(Path.Combine(dataDirectory, "lifemodules.xml"), resolved);
            Assert.AreEqual(OwnerScope.LocalSingleUser.NormalizedValue, ownerContextAccessor.Current.NormalizedValue);
            Assert.IsFalse(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr4, StringComparison.Ordinal)));
            Assert.IsTrue(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr5, StringComparison.Ordinal)));
            Assert.IsTrue(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr6, StringComparison.Ordinal)));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void AddChummerHeadlessCore_env_toggle_enables_content_bundle_validation()
    {
        lock (EnvironmentLock)
        {
            string root = CreateTempDirectory();
            string? previous = Environment.GetEnvironmentVariable(RequireContentBundleEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(RequireContentBundleEnvironmentVariable, "true");
                var services = new ServiceCollection();

                InvalidOperationException? ex = null;
                try
                {
                    services.AddChummerHeadlessCore(root, root);
                }
                catch (InvalidOperationException captured)
                {
                    ex = captured;
                }

                Assert.IsNotNull(ex);
                StringAssert.Contains(ex.Message, "Content bundle validation failed");
            }
            finally
            {
                Environment.SetEnvironmentVariable(RequireContentBundleEnvironmentVariable, previous);
                DeleteTempDirectory(root);
            }
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
