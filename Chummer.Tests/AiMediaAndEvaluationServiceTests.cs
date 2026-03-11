#nullable enable annotations

using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiMediaAndEvaluationServiceTests
{
    [TestMethod]
    public void Not_implemented_ai_media_job_service_exposes_explicit_media_boundaries()
    {
        NotImplementedAiMediaJobService service = new();

        AiApiResult<AiMediaJobReceipt> portrait = service.QueuePortraitJob(
            OwnerScope.LocalSingleUser,
            new AiMediaJobRequest("Portrait prompt", CharacterId: "char-1", RuntimeFingerprint: "sha256:portrait"));
        AiApiResult<AiMediaJobReceipt> dossier = service.QueueDossierJob(
            OwnerScope.LocalSingleUser,
            new AiMediaJobRequest("Dossier prompt", CharacterId: "char-2"));
        AiApiResult<AiMediaJobReceipt> routeVideo = service.QueueRouteVideoJob(
            OwnerScope.LocalSingleUser,
            new AiMediaJobRequest("Route prompt"));

        Assert.IsFalse(portrait.IsImplemented);
        Assert.AreEqual(AiMediaApiOperations.QueuePortraitJob, portrait.NotImplemented?.Operation);
        Assert.IsFalse(dossier.IsImplemented);
        Assert.AreEqual(AiMediaApiOperations.QueueDossierJob, dossier.NotImplemented?.Operation);
        Assert.IsFalse(routeVideo.IsImplemented);
        Assert.AreEqual(AiMediaApiOperations.QueueRouteVideoJob, routeVideo.NotImplemented?.Operation);
    }

    [TestMethod]
    public void Not_implemented_ai_evaluation_service_exposes_explicit_admin_boundary()
    {
        NotImplementedAiEvaluationService service = new();

        AiApiResult<AiEvaluationCatalog> result = service.ListEvaluations(
            OwnerScope.LocalSingleUser,
            new AiEvaluationQuery(RouteType: AiRouteTypes.Coach, MaxCount: 5));

        Assert.IsFalse(result.IsImplemented);
        Assert.AreEqual(AiEvaluationApiOperations.ListEvaluations, result.NotImplemented?.Operation);
        Assert.AreEqual(AiRouteTypes.Coach, result.NotImplemented?.RouteType);
    }
}
