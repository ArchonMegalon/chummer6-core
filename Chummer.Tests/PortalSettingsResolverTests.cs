#nullable enable annotations

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class PortalSettingsResolverTests
{
    [TestMethod]
    public void Resolve_setting_uses_specific_environment_variable_before_fallback_alias_value()
    {
        const string environmentVariable = "CHUMMER_TEST_PORTAL_AI_PROXY_URL";
        string? original = Environment.GetEnvironmentVariable(environmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(environmentVariable, "https://ai.direct.example/");

            IConfiguration configuration = new ConfigurationBuilder().Build();
            string resolved = PortalSettingsResolver.ResolveSetting(
                configuration,
                "Portal:AiProxyBaseUrl",
                environmentVariable,
                "https://chummer.run/");

            Assert.AreEqual("https://ai.direct.example/", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, original);
        }
    }

    [TestMethod]
    public void Resolve_setting_uses_fallback_alias_when_specific_environment_variable_is_missing()
    {
        const string environmentVariable = "CHUMMER_TEST_PORTAL_AI_PROXY_URL";
        string? original = Environment.GetEnvironmentVariable(environmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);

            IConfiguration configuration = new ConfigurationBuilder().Build();
            string resolved = PortalSettingsResolver.ResolveSetting(
                configuration,
                "Portal:AiProxyBaseUrl",
                environmentVariable,
                "https://chummer.run/");

            Assert.AreEqual("https://chummer.run/", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, original);
        }
    }
}
