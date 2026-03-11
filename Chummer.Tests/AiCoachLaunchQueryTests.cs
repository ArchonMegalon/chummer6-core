#nullable enable annotations

using Chummer.Contracts.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiCoachLaunchQueryTests
{
    [TestMethod]
    public void BuildRelativeUri_encodes_runtime_character_and_build_query_context()
    {
        string uri = AiCoachLaunchQuery.BuildRelativeUri(
            "/coach/",
            new AiCoachLaunchContext(
                RouteType: AiRouteTypes.Coach,
                RuntimeFingerprint: "sha256:runtime-profile",
                CharacterId: "char-1",
                WorkspaceId: "ws-1",
                RulesetId: "sr5",
                Message: "What should I spend 18 Karma on next?",
                BuildIdeaQuery: "stealth decker"));

        Assert.AreEqual(
            "/coach/?routeType=coach&runtimeFingerprint=sha256%3Aruntime-profile&characterId=char-1&workspaceId=ws-1&rulesetId=sr5&message=What%20should%20I%20spend%2018%20Karma%20on%20next%3F&buildIdeaQuery=stealth%20decker",
            uri);
    }

    [TestMethod]
    public void Parse_round_trips_launch_context_and_defaults_route_type()
    {
        AiCoachLaunchContext parsed = AiCoachLaunchQuery.Parse(
            "?runtimeFingerprint=sha256%3Aruntime-profile&characterId=char-1&workspaceId=ws-9&rulesetId=sr6&message=Stay%20grounded&buildIdeaQuery=drone%20support");

        Assert.AreEqual(AiRouteTypes.Coach, parsed.RouteType);
        Assert.AreEqual("sha256:runtime-profile", parsed.RuntimeFingerprint);
        Assert.AreEqual("char-1", parsed.CharacterId);
        Assert.AreEqual("ws-9", parsed.WorkspaceId);
        Assert.AreEqual("sr6", parsed.RulesetId);
        Assert.AreEqual("Stay grounded", parsed.Message);
        Assert.AreEqual("drone support", parsed.BuildIdeaQuery);
    }
}
