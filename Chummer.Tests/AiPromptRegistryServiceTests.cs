#nullable enable annotations

using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiPromptRegistryServiceTests
{
    [TestMethod]
    public void Default_prompt_registry_lists_current_route_prompts()
    {
        DefaultAiPromptRegistryService service = new();

        AiPromptCatalog catalog = service.ListPrompts(
            OwnerScope.LocalSingleUser,
            new AiPromptCatalogQuery(PersonaId: AiPersonaIds.DeckerContact, MaxCount: 10));

        Assert.IsGreaterThanOrEqualTo(catalog.TotalCount, 5);
        Assert.IsTrue(catalog.Items.Any(item => item.RouteType == AiRouteTypes.Coach && item.PersonaId == AiPersonaIds.DeckerContact));
        Assert.IsTrue(catalog.Items.Any(item => item.RouteType == AiRouteTypes.Docs));
    }

    [TestMethod]
    public void Default_prompt_registry_exposes_prompt_detail_for_coach_route()
    {
        DefaultAiPromptRegistryService service = new();

        AiPromptDescriptor? prompt = service.GetPrompt(OwnerScope.LocalSingleUser, AiRouteTypes.Coach);

        Assert.IsNotNull(prompt);
        Assert.AreEqual(AiPromptKinds.RouteSystem, prompt.PromptKind);
        Assert.AreEqual(AiPersonaIds.DeckerContact, prompt.PersonaId);
        CollectionAssert.Contains(prompt.RequiredGroundingSectionIds.ToArray(), AiGroundingSectionIds.Runtime);
        CollectionAssert.Contains(prompt.RequiredGroundingSectionIds.ToArray(), AiGroundingSectionIds.RetrievedItems);
        CollectionAssert.Contains(prompt.AllowedToolIds.ToArray(), AiToolIds.SearchBuildIdeas);
        CollectionAssert.Contains(prompt.AllowedToolIds.ToArray(), AiToolIds.SearchHubProjects);
        CollectionAssert.Contains(prompt.AllowedToolIds.ToArray(), AiToolIds.CreateApplyPreview);
    }

    [TestMethod]
    public void Default_prompt_registry_filters_by_route_and_persona()
    {
        DefaultAiPromptRegistryService service = new();

        AiPromptCatalog catalog = service.ListPrompts(
            OwnerScope.LocalSingleUser,
            new AiPromptCatalogQuery(RouteType: AiRouteTypes.Coach, PersonaId: AiPersonaIds.DeckerContact, MaxCount: 5));

        Assert.AreEqual(1, catalog.TotalCount);
        Assert.AreEqual(AiRouteTypes.Coach, catalog.Items[0].RouteType);
        Assert.AreEqual(AiPersonaIds.DeckerContact, catalog.Items[0].PersonaId);
    }
}
