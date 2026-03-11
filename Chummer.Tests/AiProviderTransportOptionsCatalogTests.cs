#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Infrastructure.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiProviderTransportOptionsCatalogTests
{
    private static readonly string[] EnvironmentVariables =
    [
        EnvironmentAiProviderTransportOptionsCatalog.EnableRemoteExecutionEnvironmentVariable,
        EnvironmentAiProviderTransportOptionsCatalog.AiMagicxBaseUrlEnvironmentVariable,
        EnvironmentAiProviderTransportOptionsCatalog.AiMagicxModelEnvironmentVariable,
        EnvironmentAiProviderTransportOptionsCatalog.OneMinAiBaseUrlEnvironmentVariable,
        EnvironmentAiProviderTransportOptionsCatalog.OneMinAiModelEnvironmentVariable
    ];

    [TestMethod]
    public void Environment_transport_options_catalog_parses_provider_base_urls_models_and_remote_execution_flag()
    {
        Dictionary<string, string?> originalValues = CaptureEnvironmentValues();

        try
        {
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.EnableRemoteExecutionEnvironmentVariable, "true");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.AiMagicxBaseUrlEnvironmentVariable, "https://beta.aimagicx.com/api/v1");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.AiMagicxModelEnvironmentVariable, "magicx-coach");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.OneMinAiBaseUrlEnvironmentVariable, "https://api.1min.ai/api/chat-with-ai");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.OneMinAiModelEnvironmentVariable, "1minai-chat");

            EnvironmentAiProviderTransportOptionsCatalog catalog = new();
            IReadOnlyDictionary<string, AiProviderTransportOptions> options = catalog.GetConfiguredTransportOptions();

            Assert.IsTrue(options[AiProviderIds.AiMagicx].TransportConfigured);
            Assert.AreEqual("https://beta.aimagicx.com/api/v1", options[AiProviderIds.AiMagicx].BaseUrl);
            Assert.AreEqual("magicx-coach", options[AiProviderIds.AiMagicx].DefaultModelId);
            Assert.IsTrue(options[AiProviderIds.AiMagicx].RemoteExecutionEnabled);
            Assert.IsTrue(options[AiProviderIds.OneMinAi].TransportConfigured);
            Assert.AreEqual("https://api.1min.ai/api/chat-with-ai", options[AiProviderIds.OneMinAi].BaseUrl);
            Assert.AreEqual("1minai-chat", options[AiProviderIds.OneMinAi].DefaultModelId);
            Assert.IsTrue(options[AiProviderIds.OneMinAi].RemoteExecutionEnabled);
        }
        finally
        {
            RestoreEnvironmentValues(originalValues);
        }
    }

    [TestMethod]
    public void Environment_transport_options_catalog_keeps_live_execution_disabled_when_transport_or_global_flag_is_missing()
    {
        Dictionary<string, string?> originalValues = CaptureEnvironmentValues();

        try
        {
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.EnableRemoteExecutionEnvironmentVariable, "false");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.AiMagicxBaseUrlEnvironmentVariable, "https://beta.aimagicx.com/api/v1");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.AiMagicxModelEnvironmentVariable, "");

            EnvironmentAiProviderTransportOptionsCatalog catalog = new();
            IReadOnlyDictionary<string, AiProviderTransportOptions> options = catalog.GetConfiguredTransportOptions();

            Assert.IsFalse(options[AiProviderIds.AiMagicx].TransportConfigured);
            Assert.IsFalse(options[AiProviderIds.AiMagicx].RemoteExecutionEnabled);
            Assert.IsFalse(options[AiProviderIds.OneMinAi].TransportConfigured);
            Assert.IsFalse(options[AiProviderIds.OneMinAi].RemoteExecutionEnabled);
        }
        finally
        {
            RestoreEnvironmentValues(originalValues);
        }
    }

    [TestMethod]
    public void Environment_transport_options_catalog_requires_both_base_url_and_model_before_marking_transport_configured()
    {
        Dictionary<string, string?> originalValues = CaptureEnvironmentValues();

        try
        {
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.EnableRemoteExecutionEnvironmentVariable, "true");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.AiMagicxBaseUrlEnvironmentVariable, "https://beta.aimagicx.com/api/v1");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.AiMagicxModelEnvironmentVariable, "");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.OneMinAiBaseUrlEnvironmentVariable, "");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderTransportOptionsCatalog.OneMinAiModelEnvironmentVariable, "gpt-4.1-mini");

            EnvironmentAiProviderTransportOptionsCatalog catalog = new();
            IReadOnlyDictionary<string, AiProviderTransportOptions> options = catalog.GetConfiguredTransportOptions();

            Assert.IsFalse(options[AiProviderIds.AiMagicx].TransportConfigured);
            Assert.IsFalse(options[AiProviderIds.AiMagicx].RemoteExecutionEnabled);
            Assert.IsFalse(options[AiProviderIds.OneMinAi].TransportConfigured);
            Assert.IsFalse(options[AiProviderIds.OneMinAi].RemoteExecutionEnabled);
        }
        finally
        {
            RestoreEnvironmentValues(originalValues);
        }
    }

    private static Dictionary<string, string?> CaptureEnvironmentValues()
        => EnvironmentVariables.ToDictionary(static key => key, static key => Environment.GetEnvironmentVariable(key), StringComparer.Ordinal);

    private static void RestoreEnvironmentValues(IReadOnlyDictionary<string, string?> values)
    {
        foreach ((string key, string? value) in values)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
